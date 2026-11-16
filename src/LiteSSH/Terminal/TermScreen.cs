using System.Text;

namespace LiteSSH.Terminal;

[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1,
    Faint = 2,
    Italic = 4,
    Underline = 8,
    Reverse = 16,
    Hidden = 32,
    Wide = 64,
    Continuation = 128,
}

/// <summary>One character cell. Ch is a Unicode code point, 0 when empty.</summary>
public struct Cell
{
    public int Ch;
    public CellFlags Flags;
    public byte Extra;
    public int Fg;
    public int Bg;

    public static Cell Empty => new() { Ch = 0, Fg = -1, Bg = -1 };
}

public static class CharWidth
{
    public static int Of(int cp)
    {
        if (cp == 0)
        {
            return 0;
        }
        if (cp < 0x1100)
        {
            return 1;
        }
        if ((cp >= 0x1100 && cp <= 0x115F) ||
            (cp >= 0x2E80 && cp <= 0xA4CF) ||
            (cp >= 0xA960 && cp <= 0xA97F) ||
            (cp >= 0xAC00 && cp <= 0xD7A3) ||
            (cp >= 0xF900 && cp <= 0xFAFF) ||
            (cp >= 0xFE10 && cp <= 0xFE19) ||
            (cp >= 0xFE30 && cp <= 0xFE6F) ||
            (cp >= 0xFF00 && cp <= 0xFF60) ||
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||
            (cp >= 0x1F300 && cp <= 0x1F64F) ||
            (cp >= 0x1F900 && cp <= 0x1F9FF) ||
            (cp >= 0x1F680 && cp <= 0x1F6FF) ||
            (cp >= 0x20000 && cp <= 0x3FFFD))
        {
            return 2;
        }
        return 1;
    }
}

public sealed class TermScreen
{
    private readonly List<Cell[]> _scrollback = new();
    private Cell[] _buffer;
    private Cell[]? _altBuffer;
    private int _scrollbackLimit;
    private Cell[]? _savedMainBuffer;

    public TermScreen(int cols, int rows, int scrollback)
    {
        Cols = Math.Max(2, cols);
        Rows = Math.Max(2, rows);
        _scrollbackLimit = Math.Max(100, scrollback);
        _buffer = NewBuffer(Cols, Rows);
        Tabs = BuildTabs(Cols);
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        ResetAttributes();
    }

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public bool CursorVisible { get; set; } = true;
    public bool BracketedPaste { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool InsertMode { get; set; }
    public bool AutoWrap { get; set; } = true;
    public bool OriginMode { get; set; }
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }
    public int Fg { get; set; } = -1;
    public int Bg { get; set; } = -1;
    public CellFlags Flags { get; set; }
    public bool InAltScreen { get; private set; }
    public string Title { get; set; } = "";
    public IReadOnlyList<Cell[]> Scrollback => _scrollback;
    public bool ReverseVideo { get; set; }
    public Action<string>? Respond { get; set; }
    public Action<string>? OnTitle;
    public Action? OnBell;

    private bool _pendingWrap;
    private bool[] Tabs { get; set; }
    private int _savedX;
    private int _savedY;
    private int _savedFg = -1;
    private int _savedBg = -1;
    private CellFlags _savedFlags;
    private bool _graphicsMode;
    private int _lastPrinted = ' ';

    private static Cell[] NewBuffer(int cols, int rows)
    {
        var buffer = new Cell[cols * rows];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Cell.Empty;
        }
        return buffer;
    }

    private static bool[] BuildTabs(int cols)
    {
        var tabs = new bool[Math.Max(1, cols)];
        for (var i = 8; i < tabs.Length; i += 8)
        {
            tabs[i] = true;
        }
        return tabs;
    }

    public Cell GetCell(int row, int col)
    {
        if (row < 0 || row >= Rows || col < 0 || col >= Cols)
        {
            return Cell.Empty;
        }
        return _buffer[row * Cols + col];
    }

    public void SetScrollbackLimit(int limit)
    {
        _scrollbackLimit = Math.Max(100, limit);
        TrimScrollback();
    }

    private void TrimScrollback()
    {
        while (_scrollback.Count > _scrollbackLimit)
        {
            _scrollback.RemoveAt(0);
        }
    }

    public void ResetAttributes()
    {
        Fg = -1;
        Bg = -1;
        Flags = CellFlags.None;
        InsertMode = false;
    }

    public void FullReset()
    {
        _scrollback.Clear();
        _buffer = NewBuffer(Cols, Rows);
        CursorX = 0;
        CursorY = 0;
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        InAltScreen = false;
        _altBuffer = null;
        _savedMainBuffer = null;
        AutoWrap = true;
        OriginMode = false;
        CursorVisible = true;
        BracketedPaste = false;
        ApplicationCursorKeys = false;
        ReverseVideo = false;
        Tabs = BuildTabs(Cols);
        ResetAttributes();
        _pendingWrap = false;
    }

    public void Print(int cp, int width)
    {
        if (width <= 0)
        {
            return;
        }

        if (_pendingWrap && AutoWrap)
        {
            CursorX = 0;
            LineFeed();
            _pendingWrap = false;
        }

        if (width == 2 && CursorX + 1 >= Cols)
        {
            if (AutoWrap)
            {
                CursorX = 0;
                LineFeed();
            }
            else
            {
                CursorX = Math.Max(0, Cols - 2);
            }
        }

        if (CursorX >= Cols)
        {
            CursorX = Cols - 1;
        }

        if (InsertMode)
        {
            InsertChars(width);
        }

        // Clear continuation of a wide char being overwritten.
        var current = _buffer[CursorY * Cols + CursorX];
        if ((current.Flags & CellFlags.Continuation) != 0 && CursorX > 0)
        {
            var prev = _buffer[CursorY * Cols + CursorX - 1];
            prev.Flags &= ~CellFlags.Wide;
            prev.Ch = prev.Ch == 0 ? ' ' : prev.Ch;
            _buffer[CursorY * Cols + CursorX - 1] = prev;
        }

        var cell = new Cell { Ch = cp, Fg = Fg, Bg = Bg, Flags = Flags, Extra = (byte)(_graphicsMode ? 1 : 0) };
        if (width == 2)
        {
            cell.Flags |= CellFlags.Wide;
        }
        _buffer[CursorY * Cols + CursorX] = cell;
        if (width == 2 && CursorX + 1 < Cols)
        {
            _buffer[CursorY * Cols + CursorX + 1] = new Cell { Ch = 0, Flags = CellFlags.Continuation, Fg = Fg, Bg = Bg };
        }

        _lastPrinted = cp;
        CursorX += width;
        if (CursorX >= Cols)
        {
            CursorX = Cols - 1;
            _pendingWrap = true;
            if (!AutoWrap)
            {
                _pendingWrap = false;
            }
        }
    }

    public void RepeatLast(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _pendingWrap = false;
            Print(_lastPrinted, CharWidth.Of(_lastPrinted));
        }
    }

    /// <summary>Any explicit cursor movement or erase cancels a pending autowrap, as in xterm.</summary>
    private void CancelWrap() => _pendingWrap = false;

    public void CarriageReturn()
    {
        CancelWrap();
        CursorX = 0;
    }

    public void Backspace()
    {
        if (_pendingWrap)
        {
            _pendingWrap = false;
            return;
        }
        if (CursorX > 0)
        {
            CursorX--;
        }
    }

    public void LineFeed()
    {
        _pendingWrap = false;
        if (CursorY == ScrollBottom)
        {
            ScrollUp(1);
        }
        else if (CursorY < Rows - 1)
        {
            CursorY++;
        }
    }

    public void ReverseIndex()
    {
        _pendingWrap = false;
        if (CursorY == ScrollTop)
        {
            ScrollDown(1);
        }
        else if (CursorY > 0)
        {
            CursorY--;
        }
    }

    public void Tab(int count = 1)
    {
        CancelWrap();
        for (var i = 0; i < count; i++)
        {
            var next = Cols - 1;
            for (var c = CursorX + 1; c < Cols; c++)
            {
                if (Tabs[c])
                {
                    next = c;
                    break;
                }
            }
            CursorX = next;
        }
    }

    public void BackTab(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var prev = 0;
            for (var c = CursorX - 1; c > 0; c--)
            {
                if (Tabs[c])
                {
                    prev = c;
                    break;
                }
            }
            CursorX = prev;
        }
    }

    public void SetTabStop() 
    {
        if (CursorX < Tabs.Length)
        {
            Tabs[CursorX] = true;
        }
    }

    public void ClearTabStops(bool all)
    {
        if (all)
        {
            Array.Clear(Tabs, 0, Tabs.Length);
        }
        else if (CursorX < Tabs.Length)
        {
            Tabs[CursorX] = false;
        }
    }

    public void MoveCursor(int dx, int dy)
    {
        _pendingWrap = false;
        CursorX = Math.Clamp(CursorX + dx, 0, Cols - 1);
        CursorY = Math.Clamp(CursorY + dy, 0, Rows - 1);
        if (OriginMode)
        {
            CursorY = Math.Clamp(CursorY, ScrollTop, ScrollBottom);
        }
    }

    public void SetCursor(int row, int col)
    {
        _pendingWrap = false;
        var baseRow = OriginMode ? ScrollTop : 0;
        var maxRow = OriginMode ? ScrollBottom : Rows - 1;
        CursorY = Math.Clamp(baseRow + row, baseRow, maxRow);
        CursorX = Math.Clamp(col, 0, Cols - 1);
    }

    public void SetColumn(int col)
    {
        CancelWrap();
        CursorX = Math.Clamp(col, 0, Cols - 1);
    }

    public void SetRow(int row)
    {
        CancelWrap();
        var baseRow = OriginMode ? ScrollTop : 0;
        var maxRow = OriginMode ? ScrollBottom : Rows - 1;
        CursorY = Math.Clamp(baseRow + row, baseRow, maxRow);
    }

    public void SaveCursor()
    {
        _savedX = CursorX;
        _savedY = CursorY;
        _savedFg = Fg;
        _savedBg = Bg;
        _savedFlags = Flags;
    }

    public void RestoreCursor()
    {
        CursorX = Math.Clamp(_savedX, 0, Cols - 1);
        CursorY = Math.Clamp(_savedY, 0, Rows - 1);
        Fg = _savedFg;
        Bg = _savedBg;
        Flags = _savedFlags;
        _pendingWrap = false;
    }

    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, Rows - 1);
        bottom = Math.Clamp(bottom, 0, Rows - 1);
        if (bottom <= top)
        {
            return;
        }
        ScrollTop = top;
        ScrollBottom = bottom;
        SetCursor(0, 0);
    }

    public void ScrollUp(int count)
    {
        CancelWrap();
        count = Math.Clamp(count, 1, Math.Max(1, ScrollBottom - ScrollTop + 1));
        for (var i = 0; i < count; i++)
        {
            var topRow = GetRowSpan(ScrollTop);
            var pushToScrollback = ScrollTop == 0 && !InAltScreen;
            if (pushToScrollback)
            {
                _scrollback.Add(topRow.ToArray());
                TrimScrollback();
            }

            for (var row = ScrollTop; row < ScrollBottom; row++)
            {
                Array.Copy(_buffer, (row + 1) * Cols, _buffer, row * Cols, Cols);
            }
            ClearRow(ScrollBottom);
        }
    }

    public void ScrollDown(int count)
    {
        CancelWrap();
        count = Math.Clamp(count, 1, Math.Max(1, ScrollBottom - ScrollTop + 1));
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollBottom; row > ScrollTop; row--)
            {
                Array.Copy(_buffer, (row - 1) * Cols, _buffer, row * Cols, Cols);
            }
            ClearRow(ScrollTop);
        }
    }

    private Span<Cell> GetRowSpan(int row) => _buffer.AsSpan(row * Cols, Cols);

    private void ClearRow(int row)
    {
        for (var col = 0; col < Cols; col++)
        {
            _buffer[row * Cols + col] = Cell.Empty;
        }
    }

    public void EraseInDisplay(int mode)
    {
        CancelWrap();
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (var row = CursorY + 1; row < Rows; row++)
                {
                    ClearRow(row);
                }
                break;
            case 1:
                EraseInLine(1);
                for (var row = 0; row < CursorY; row++)
                {
                    ClearRow(row);
                }
                break;
            case 2:
                for (var row = 0; row < Rows; row++)
                {
                    ClearRow(row);
                }
                break;
            case 3:
                _scrollback.Clear();
                break;
        }
    }

    public void EraseInLine(int mode)
    {
        CancelWrap();
        var start = mode switch
        {
            0 => CursorX,
            1 => 0,
            _ => 0,
        };
        var end = mode switch
        {
            0 => Cols - 1,
            1 => CursorX,
            _ => Cols - 1,
        };
        for (var col = start; col <= end && col < Cols; col++)
        {
            _buffer[CursorY * Cols + col] = Cell.Empty;
        }
    }

    public void EraseChars(int count)
    {
        CancelWrap();
        var end = Math.Min(Cols, CursorX + count);
        for (var col = CursorX; col < end; col++)
        {
            _buffer[CursorY * Cols + col] = Cell.Empty;
        }
    }

    public void InsertChars(int count)
    {
        CancelWrap();
        count = Math.Clamp(count, 1, Cols - CursorX);
        var row = CursorY * Cols;
        Array.Copy(_buffer, row + CursorX, _buffer, row + CursorX + count, Cols - CursorX - count);
        for (var i = 0; i < count; i++)
        {
            _buffer[row + CursorX + i] = Cell.Empty;
        }
    }

    public void DeleteChars(int count)
    {
        CancelWrap();
        count = Math.Clamp(count, 1, Cols - CursorX);
        var row = CursorY * Cols;
        Array.Copy(_buffer, row + CursorX + count, _buffer, row + CursorX, Cols - CursorX - count);
        for (var i = 0; i < count; i++)
        {
            _buffer[row + Cols - count + i] = Cell.Empty;
        }
    }

    public void InsertLines(int count)
    {
        if (CursorY < ScrollTop || CursorY > ScrollBottom)
        {
            return;
        }
        count = Math.Clamp(count, 1, ScrollBottom - CursorY + 1);
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollBottom; row > CursorY; row--)
            {
                Array.Copy(_buffer, (row - 1) * Cols, _buffer, row * Cols, Cols);
            }
            ClearRow(CursorY);
        }
    }

    public void DeleteLines(int count)
    {
        if (CursorY < ScrollTop || CursorY > ScrollBottom)
        {
            return;
        }
        count = Math.Clamp(count, 1, ScrollBottom - CursorY + 1);
        for (var i = 0; i < count; i++)
        {
            for (var row = CursorY; row < ScrollBottom; row++)
            {
                Array.Copy(_buffer, (row + 1) * Cols, _buffer, row * Cols, Cols);
            }
            ClearRow(ScrollBottom);
        }
    }

    public void SetAltScreen(bool enabled, bool saveCursor)
    {
        if (enabled == InAltScreen)
        {
            return;
        }

        if (enabled)
        {
            if (saveCursor)
            {
                SaveCursor();
            }
            _savedMainBuffer = _buffer;
            _altBuffer = NewBuffer(Cols, Rows);
            _buffer = _altBuffer;
            InAltScreen = true;
            CursorX = 0;
            CursorY = 0;
        }
        else
        {
            if (_savedMainBuffer != null)
            {
                _buffer = _savedMainBuffer;
                _savedMainBuffer = null;
            }
            _altBuffer = null;
            InAltScreen = false;
            if (saveCursor)
            {
                RestoreCursor();
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(2, cols);
        rows = Math.Max(2, rows);
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        var newBuffer = NewBuffer(cols, rows);
        var copyRows = Math.Min(rows, Rows);
        var copyCols = Math.Min(cols, Cols);
        var startRow = rows > Rows ? rows - Rows : 0;
        for (var row = 0; row < copyRows; row++)
        {
            for (var col = 0; col < copyCols; col++)
            {
                newBuffer[(startRow + row) * cols + col] = _buffer[row * Cols + col];
            }
        }

        for (var i = 0; i < _scrollback.Count; i++)
        {
            var line = _scrollback[i];
            if (line.Length == cols)
            {
                continue;
            }
            var resized = new Cell[cols];
            for (var c = 0; c < cols; c++)
            {
                resized[c] = c < line.Length ? line[c] : Cell.Empty;
            }
            _scrollback[i] = resized;
        }

        _buffer = newBuffer;
        Cols = cols;
        Rows = rows;
        Tabs = BuildTabs(cols);
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        CursorX = Math.Min(CursorX, cols - 1);
        CursorY = Math.Min(CursorY, rows - 1);
    }

    public void SetGraphicsMode(bool enabled) => _graphicsMode = enabled;
}
