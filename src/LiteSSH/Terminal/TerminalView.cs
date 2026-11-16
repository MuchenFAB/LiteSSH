using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiteSSH.Core;

namespace LiteSSH.Terminal;

/// <summary>Grid renderer for the VT screen with selection, scrollback and keyboard input.</summary>
public sealed class TerminalView : FrameworkElement
{
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly Dictionary<(string Text, uint Fg, int Flags), FormattedText> _cache = new();
    private Typeface _regular = new("Consolas");
    private Typeface _bold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private Typeface _italic = new(new FontFamily("Consolas"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    private double _cellW = 8;
    private double _cellH = 16;
    private double _pixelsPerDip = 1;
    private double _fontSize = 13;
    private string _fontFamily = "Consolas";
    private TerminalPalette _palette = TerminalPalette.MiuixDark;
    private int _scrollOffset;
    private bool _cursorOn = true;
    private bool _dragging;
    private (int Line, int Col) _selAnchor;
    private (int Line, int Col)? _selEnd;
    private bool _hasFocus;

    public TerminalView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        UseLayoutRounding = true;
        _blink.Tick += (_, _) =>
        {
            _cursorOn = !_cursorOn;
            InvalidateVisual();
        };
        _blink.Start();
    }

    public TerminalEmulator? Emulator { get; private set; }

    public event Action<byte[]>? Input;

    public event Action<int, int>? GridResized;

    public event Action? ReconnectRequested;

    public event Action? Scrolled;

    public bool CopyOnSelect { get; set; }

    public string FontFamilyName
    {
        get => _fontFamily;
        set
        {
            _fontFamily = string.IsNullOrWhiteSpace(value) ? "Consolas" : value;
            RebuildFont();
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = Math.Clamp(value, 8, 32);
            RebuildFont();
        }
    }

    public TerminalPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            _cache.Clear();
            InvalidateVisual();
        }
    }

    public int ScrollOffset => _scrollOffset;

    public void Attach(TerminalEmulator emulator)
    {
        if (Emulator != null)
        {
            Emulator.Damaged -= OnDamaged;
            Emulator.Response -= OnResponse;
        }

        Emulator = emulator;
        Emulator.Damaged += OnDamaged;
        Emulator.Response += OnResponse;
        _scrollOffset = 0;
        RebuildFont();
        InvalidateVisual();
    }

    public void Feed(byte[] data)
    {
        Emulator?.Feed(data);
    }

    private void OnResponse(string text)
    {
        if (Emulator != null)
        {
            Input?.Invoke(Encoding.UTF8.GetBytes(text));
        }
    }

    private void OnDamaged(object? sender, EventArgs e)
    {
        if (_scrollOffset > 0)
        {
            // keep the viewport pinned while output streams in
            InvalidateVisual();
            return;
        }
        InvalidateVisual();
    }

    private void RebuildFont()
    {
        try
        {
            var family = new FontFamily(_fontFamily);
            _regular = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _bold = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            _italic = new Typeface(family, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _regular, _fontSize, Brushes.White, _pixelsPerDip);
            _cellW = Math.Round(ft.WidthIncludingTrailingWhitespace / 10.0, 2);
            _cellH = Math.Ceiling(ft.Height);
        }
        catch
        {
            _cellW = _fontSize * 0.6;
            _cellH = _fontSize * 1.3;
        }

        if (_cellW < 1)
        {
            _cellW = _fontSize * 0.6;
        }
        if (_cellH < 1)
        {
            _cellH = _fontSize * 1.3;
        }

        _cache.Clear();
        UpdateGrid();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateGrid();
    }

    private void UpdateGrid()
    {
        if (Emulator == null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var cols = Math.Max(2, (int)Math.Floor(ActualWidth / _cellW));
        var rows = Math.Max(2, (int)Math.Floor(ActualHeight / _cellH));
        if (cols == Emulator.Screen.Cols && rows == Emulator.Screen.Rows)
        {
            return;
        }

        Emulator.Resize(cols, rows);
        GridResized?.Invoke(cols, rows);
    }

    private FormattedText GetText(string text, uint fg, CellFlags flags)
    {
        var key = (text, fg, (int)flags);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var typeface = (flags & CellFlags.Bold) != 0 ? _bold : (flags & CellFlags.Italic) != 0 ? _italic : _regular;
        var brush = new SolidColorBrush(FromUint(fg));
        brush.Freeze();
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, _fontSize, brush, _pixelsPerDip)
        {
            Trimming = TextTrimming.None,
        };

        if (_cache.Count > 6000)
        {
            _cache.Clear();
        }
        _cache[key] = ft;
        return ft;
    }

    public static Color FromUint(uint rgb)
        => Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private Brush _bgBrush = Brushes.Black;

    protected override void OnRender(DrawingContext dc)
    {
        var bmp = FromUint(_palette.Background);
        var bgBrush = new SolidColorBrush(bmp);
        bgBrush.Freeze();
        _bgBrush = bgBrush;
        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Emulator == null)
        {
            return;
        }

        var screen = Emulator.Screen;
        var scrollback = screen.Scrollback;
        var totalLines = scrollback.Count + screen.Rows;
        var visibleRows = Math.Max(1, (int)Math.Floor(ActualHeight / _cellH));
        var lastLine = totalLines - _scrollOffset;
        var firstLine = Math.Max(0, lastLine - visibleRows);

        var selection = NormalizedSelection();
        var selBrush = new SolidColorBrush(FromUint(TerminalPalette.Mix(_palette.Background, _palette.Selection, 0.85)));
        selBrush.Freeze();

        for (var line = firstLine; line < lastLine && line < totalLines; line++)
        {
            var cells = line < scrollback.Count ? scrollback[line] : GetScreenRow(screen, line - scrollback.Count);
            var y = (line - firstLine) * _cellH;

            if (selection != null)
            {
                var range = SelectionRangeForLine(selection.Value, line, cells);
                if (range != null)
                {
                    var left = range.Value.Start * _cellW;
                    var width = Math.Max(_cellW * 0.5, (range.Value.End - range.Value.Start + 1) * _cellW);
                    dc.DrawRectangle(selBrush, null, new Rect(left, y, width, _cellH));
                }
            }

            RenderLine(dc, cells, y, line);
        }

        // cursor
        if (_hasFocus || true)
        {
            var cursorLine = scrollback.Count + screen.CursorY;
            if (screen.CursorVisible && _cursorOn && cursorLine >= firstLine && cursorLine < lastLine && _scrollOffset == 0)
            {
                var cx = screen.CursorX * _cellW;
                var cy = (cursorLine - firstLine) * _cellH;
                var brush = new SolidColorBrush(FromUint(_palette.Cursor));
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(cx, cy, Math.Max(1, _cellW), _cellH));
                var cell = screen.GetCell(screen.CursorY, screen.CursorX);
                if (cell.Ch > 0)
                {
                    var text = char.ConvertFromUtf32(cell.Ch);
                    var ft = GetText(text, _palette.Background, CellFlags.None);
                    dc.DrawText(ft, new Point(cx, cy + (_cellH - ft.Height) / 2));
                }
            }
        }

        if (firstLine > 0)
        {
            // subtle hint that older lines exist above
            var hint = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
            hint.Freeze();
            dc.DrawRectangle(hint, null, new Rect(0, 0, ActualWidth, 1));
        }
    }

    private static Cell[] GetScreenRow(TermScreen screen, int row)
    {
        var cells = new Cell[screen.Cols];
        for (var col = 0; col < screen.Cols; col++)
        {
            cells[col] = screen.GetCell(row, col);
        }
        return cells;
    }

    private void RenderLine(DrawingContext dc, Cell[] cells, double y, int line)
    {
        var col = 0;
        var count = cells.Length;
        while (col < count)
        {
            var cell = cells[col];
            if (cell.Ch == 0 && (cell.Flags & CellFlags.Continuation) == 0 && cell.Bg < 0)
            {
                col++;
                continue;
            }

            if ((cell.Flags & CellFlags.Continuation) != 0)
            {
                col++;
                continue;
            }

            var flags = cell.Flags;
            var fg = cell.Fg < 0 ? _palette.Foreground : _palette.Color(cell.Fg);
            var bg = cell.Bg < 0 ? -1 : cell.Bg;
            if ((flags & CellFlags.Reverse) != 0)
            {
                var oldFg = fg;
                fg = bg < 0 ? _palette.Background : _palette.Color(bg);
                bg = (int)(oldFg | 0x1000000);
            }
            if ((flags & CellFlags.Faint) != 0)
            {
                fg = TerminalPalette.Mix(_palette.Background, fg, 0.6);
            }

            // background span
            var spanEnd = col + 1;
            while (spanEnd < count)
            {
                var next = cells[spanEnd];
                if ((next.Flags & CellFlags.Continuation) != 0)
                {
                    spanEnd++;
                    continue;
                }
                var nextBg = next.Bg;
                if ((next.Flags & CellFlags.Reverse) != 0)
                {
                    nextBg = (int)((next.Fg < 0 ? _palette.Foreground : _palette.Color(next.Fg)) | 0x1000000);
                }
                if (nextBg != cell.Bg || next.Ch == 0 && next.Bg < 0)
                {
                    break;
                }
                spanEnd++;
            }

            if (cell.Bg >= 0)
            {
                var bgBrush = new SolidColorBrush(FromUint(_palette.Color(bg >= 0 ? bg : (int)(0x1000000 | _palette.Background))));
                bgBrush.Freeze();
                dc.DrawRectangle(bgBrush, null, new Rect(col * _cellW, y, (spanEnd - col) * _cellW, _cellH));
            }

            // selection is painted per line before the glyphs are drawn
            if ((flags & CellFlags.Hidden) == 0 && cell.Ch > 0)
            {
                var width = (flags & CellFlags.Wide) != 0 ? 2 : 1;
                if (width == 2)
                {
                    var text = char.ConvertFromUtf32(cell.Ch);
                    var ft = GetText(text, fg, flags);
                    dc.DrawText(ft, new Point(col * _cellW, y + (_cellH - ft.Height) / 2));
                    col += 2;
                }
                else
                {
                    var sb = new StringBuilder();
                    var runStart = col;
                    var runCol = col;
                    while (runCol < count)
                    {
                        var c = cells[runCol];
                        if ((c.Flags & CellFlags.Continuation) != 0)
                        {
                            break;
                        }
                        if ((c.Flags & CellFlags.Wide) != 0)
                        {
                            break;
                        }
                        if (c.Ch == 0)
                        {
                            break;
                        }
                        var cFlags = c.Flags;
                        var cFg = c.Fg < 0 ? _palette.Foreground : _palette.Color(c.Fg);
                        var cBg = c.Bg;
                        if ((cFlags & CellFlags.Reverse) != 0)
                        {
                            cFg = cBg < 0 ? _palette.Background : _palette.Color(cBg);
                            cBg = (int)((c.Fg < 0 ? _palette.Foreground : _palette.Color(c.Fg)) | 0x1000000);
                        }
                        if ((cFlags & CellFlags.Faint) != 0)
                        {
                            cFg = TerminalPalette.Mix(_palette.Background, cFg, 0.6);
                        }
                        if (cBg != cell.Bg || (cFlags & (CellFlags.Bold | CellFlags.Underline | CellFlags.Hidden)) != (flags & (CellFlags.Bold | CellFlags.Underline | CellFlags.Hidden)))
                        {
                            break;
                        }
                        sb.Append(char.ConvertFromUtf32(c.Ch));
                        runCol++;
                    }

                    if (sb.Length > 0)
                    {
                        var ft = GetText(sb.ToString(), fg, flags);
                        dc.DrawText(ft, new Point(runStart * _cellW, y + (_cellH - ft.Height) / 2));
                        if ((flags & CellFlags.Underline) != 0)
                        {
                            var pen = new Pen(new SolidColorBrush(FromUint(fg)), 1);
                            pen.Freeze();
                            var underlineY = y + _cellH - 1.5;
                            dc.DrawLine(pen, new Point(runStart * _cellW, underlineY), new Point((runStart + sb.Length) * _cellW, underlineY));
                        }
                        col = runCol;
                    }
                    else
                    {
                        col++;
                    }
                }
            }
            else
            {
                col += (flags & CellFlags.Wide) != 0 ? 2 : 1;
            }
        }
    }

    private ((int Line, int Col) Start, (int Line, int Col) End)? NormalizedSelection()
    {
        if (_selEnd == null)
        {
            return null;
        }

        var a = _selAnchor;
        var b = _selEnd.Value;
        return Compare(a, b) <= 0 ? (a, b) : (b, a);
    }

    private static int Compare((int Line, int Col) a, (int Line, int Col) b)
        => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Col.CompareTo(b.Col);

    private static (int Start, int End)? SelectionRangeForLine(((int Line, int Col) Start, (int Line, int Col) End) sel, int line, Cell[] cells)
    {
        if (line < sel.Start.Line || line > sel.End.Line)
        {
            return null;
        }

        var cols = cells.Length;
        var start = line == sel.Start.Line ? sel.Start.Col : 0;
        var end = line == sel.End.Line ? sel.End.Col : cols - 1;
        start = Math.Clamp(start, 0, cols - 1);
        end = Math.Clamp(end, 0, cols - 1);

        // do not paint trailing blanks past the end of the line
        var contentEnd = -1;
        for (var i = cols - 1; i >= 0; i--)
        {
            if (cells[i].Ch != 0)
            {
                contentEnd = i;
                break;
            }
        }

        if (contentEnd < 0)
        {
            return null;
        }

        if (end > contentEnd)
        {
            end = Math.Max(start, contentEnd);
        }

        return end < start ? null : (start, end);
    }

    public void SelectAll()
    {
        if (Emulator == null)
        {
            return;
        }
        var total = Emulator.Screen.Scrollback.Count + Emulator.Screen.Rows;
        _selAnchor = (0, 0);
        _selEnd = (total - 1, Emulator.Screen.Cols - 1);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        _selEnd = null;
        InvalidateVisual();
    }

    public string? GetSelectedText()
    {
        var sel = NormalizedSelection();
        if (sel == null || Emulator == null)
        {
            return null;
        }

        var screen = Emulator.Screen;
        var scrollback = screen.Scrollback;
        var sb = new StringBuilder();
        for (var line = sel.Value.Start.Line; line <= sel.Value.End.Line; line++)
        {
            var cells = line < scrollback.Count ? scrollback[line] : GetScreenRow(screen, line - scrollback.Count);
            var from = line == sel.Value.Start.Line ? sel.Value.Start.Col : 0;
            var to = line == sel.Value.End.Line ? sel.Value.End.Col : cells.Length - 1;
            var lineText = new StringBuilder();
            for (var c = from; c <= to && c < cells.Length; c++)
            {
                var cell = cells[c];
                if ((cell.Flags & CellFlags.Continuation) != 0)
                {
                    continue;
                }
                lineText.Append(cell.Ch == 0 ? ' ' : char.ConvertFromUtf32(cell.Ch));
            }
            sb.Append(lineText.ToString().TrimEnd());
            if (line != sel.Value.End.Line)
            {
                sb.Append(Environment.NewLine);
            }
        }

        return sb.ToString();
    }

    public void CopySelection()
    {
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            SetClipboard(text);
        }
    }

    /// <summary>WPF clipboard writes fail transiently when another process holds the clipboard.</summary>
    private static bool SetClipboard(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    private static string? ReadClipboard()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception)
            {
                Thread.Sleep(40);
            }
        }
        return null;
    }

    public void Paste()
    {
        var text = ReadClipboard();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (Emulator?.Screen.BracketedPaste == true)
        {
            Input?.Invoke(Encoding.UTF8.GetBytes("\u001b[200~" + text + "\u001b[201~"));
        }
        else
        {
            Input?.Invoke(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\r")));
        }

        ScrollToBottom();
    }

    public void ScrollBy(int lines)
    {
        if (Emulator == null)
        {
            return;
        }
        var max = Emulator.Screen.Scrollback.Count;
        _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, max);
        InvalidateVisual();
        Scrolled?.Invoke();
    }

    public void ScrollToBottom()
    {
        _scrollOffset = 0;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ScrollBy(e.Delta > 0 ? 3 : -3);
        e.Handled = true;
    }

    private (int Line, int Col) HitTest(Point p)
    {
        var screen = Emulator!.Screen;
        var totalLines = screen.Scrollback.Count + screen.Rows;
        var visibleRows = Math.Max(1, (int)Math.Floor(ActualHeight / _cellH));
        var lastLine = totalLines - _scrollOffset;
        var firstLine = Math.Max(0, lastLine - visibleRows);
        var line = firstLine + (int)(p.Y / _cellH);
        var col = (int)(p.X / _cellW);
        return (Math.Clamp(line, 0, totalLines - 1), Math.Clamp(col, 0, screen.Cols - 1));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Emulator == null)
        {
            return;
        }

        Focus();
        var hit = HitTest(e.GetPosition(this));

        if (e.ClickCount == 2)
        {
            // double click selects the word / path under the cursor
            var word = WordAt(hit);
            _selAnchor = (hit.Line, word.Start);
            _selEnd = (hit.Line, word.End);
            _dragging = false;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (e.ClickCount >= 3)
        {
            _selAnchor = (hit.Line, 0);
            _selEnd = (hit.Line, Emulator.Screen.Cols - 1);
            _dragging = false;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selEnd != null)
        {
            _selEnd = hit;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _dragging = true;
        _selAnchor = hit;
        _selEnd = hit;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Emulator == null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var totalLines = Emulator.Screen.Scrollback.Count + Emulator.Screen.Rows;
        var visibleRows = Math.Max(1, (int)Math.Floor(ActualHeight / _cellH));
        var lastLine = totalLines - _scrollOffset;
        var firstLine = Math.Max(0, lastLine - visibleRows);

        // auto-scroll while dragging past the top or bottom edge
        if (position.Y < 0 && _scrollOffset < Emulator.Screen.Scrollback.Count)
        {
            ScrollBy(1);
        }
        else if (position.Y > ActualHeight && _scrollOffset > 0)
        {
            ScrollBy(-1);
        }

        var line = firstLine + (int)Math.Floor(position.Y / _cellH);
        var col = (int)Math.Floor(position.X / _cellW);
        _selEnd = (Math.Clamp(line, 0, totalLines - 1), Math.Clamp(col, 0, Emulator.Screen.Cols - 1));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        ReleaseMouseCapture();

        if (Emulator != null)
        {
            // snap the selection end to the release position so jitter during the drag cannot add a row
            var position = e.GetPosition(this);
            var totalLines = Emulator.Screen.Scrollback.Count + Emulator.Screen.Rows;
            var visibleRows = Math.Max(1, (int)Math.Floor(ActualHeight / _cellH));
            var firstLine = Math.Max(0, totalLines - _scrollOffset - visibleRows);
            var line = firstLine + (int)Math.Floor(position.Y / _cellH);
            var col = (int)Math.Floor(position.X / _cellW);
            _selEnd = (Math.Clamp(line, 0, totalLines - 1), Math.Clamp(col, 0, Emulator.Screen.Cols - 1));
            InvalidateVisual();
        }

        if (CopyOnSelect)
        {
            CopySelection();
        }
        e.Handled = true;
    }

    private (int Start, int End) WordAt((int Line, int Col) hit)
    {
        var screen = Emulator!.Screen;
        var scrollback = screen.Scrollback;
        var cells = hit.Line < scrollback.Count
            ? scrollback[hit.Line]
            : GetScreenRow(screen, hit.Line - scrollback.Count);

        static bool IsWordChar(int ch)
            => ch != 0 && ch != ' ' && ch != '\t';

        var start = hit.Col;
        var end = hit.Col;
        if (!IsWordChar(cells[start].Ch))
        {
            return (hit.Col, hit.Col);
        }

        while (start > 0 && IsWordChar(cells[start - 1].Ch))
        {
            start--;
        }
        while (end < cells.Length - 1 && IsWordChar(cells[end + 1].Ch))
        {
            end++;
        }
        return (start, end);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Focus();

        // PuTTY / Windows Terminal behaviour: copy the selection, otherwise paste.
        // Shift + right-click opens the menu.
        if (Keyboard.Modifiers != ModifierKeys.Shift)
        {
            if (_selEnd != null && GetSelectedText() is { Length: > 0 } selected)
            {
                    SetClipboard(selected);
                ClearSelection();
            }
            else
            {
                ClearSelection();
                Paste();
            }
            e.Handled = true;
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu();
        var copy = new System.Windows.Controls.MenuItem { Header = L10n.T("Action.Copy"), IsEnabled = _selEnd != null };
        copy.Click += (_, _) => CopySelection();
        var paste = new System.Windows.Controls.MenuItem { Header = L10n.T("Action.Paste") };
        paste.Click += (_, _) => Paste();
        var selectAll = new System.Windows.Controls.MenuItem { Header = L10n.T("Action.SelectAll") };
        selectAll.Click += (_, _) => SelectAll();
        var clear = new System.Windows.Controls.MenuItem { Header = L10n.T("Action.Clear") };
        clear.Click += (_, _) =>
        {
            Emulator?.Reset();
            ScrollToBottom();
        };
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(selectAll);
        menu.Items.Add(clear);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && Clipboard.ContainsText())
        {
            Focus();
            Paste();
            e.Handled = true;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _hasFocus = true;
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _hasFocus = false;
        InvalidateVisual();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var appCursor = Emulator?.Screen.ApplicationCursorKeys == true;

        // Ctrl+C copies when there is a selection, otherwise it is sent as SIGINT.
        if (ctrl && e.Key == Key.C)
        {
            if (_selEnd != null)
            {
                CopySelection();
                e.Handled = true;
                return;
            }
            if (shift)
            {
                e.Handled = true;
                return;
            }
        }

        // Ctrl+V pastes; Ctrl+Shift+V stays reserved for a literal ^V.
        if (ctrl && e.Key == Key.V)
        {
            Paste();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }
        if (ctrl && shift && e.Key == Key.V)
        {
            Paste();
            e.Handled = true;
            return;
        }
        if (ctrl && shift && e.Key == Key.R)
        {
            ReconnectRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (ctrl && shift && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Insert)
        {
            CopySelection();
            e.Handled = true;
            return;
        }
        if (shift && e.Key == Key.Insert)
        {
            Paste();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageUp)
        {
            ScrollBy(-10);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.PageDown)
        {
            ScrollBy(10);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _selEnd != null)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        string? seq = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\u007f",
            Key.Tab => shift ? "\u001b[Z" : "\t",
            Key.Escape => "\u001b",
            Key.Up => appCursor && !ctrl ? "\u001bOA" : ctrl ? "\u001b[1;5A" : "\u001b[A",
            Key.Down => appCursor && !ctrl ? "\u001bOB" : ctrl ? "\u001b[1;5B" : "\u001b[B",
            Key.Right => appCursor && !ctrl ? "\u001bOC" : ctrl ? "\u001b[1;5C" : "\u001b[C",
            Key.Left => appCursor && !ctrl ? "\u001bOD" : ctrl ? "\u001b[1;5D" : "\u001b[D",
            Key.Home => appCursor ? "\u001bOH" : "\u001b[H",
            Key.End => appCursor ? "\u001bOF" : "\u001b[F",
            Key.Insert => "\u001b[2~",
            Key.Delete => "\u001b[3~",
            Key.F1 => "\u001bOP",
            Key.F2 => "\u001bOQ",
            Key.F3 => "\u001bOR",
            Key.F4 => "\u001bOS",
            Key.F5 => "\u001b[15~",
            Key.F6 => "\u001b[17~",
            Key.F7 => "\u001b[18~",
            Key.F8 => "\u001b[19~",
            Key.F9 => "\u001b[20~",
            Key.F10 => "\u001b[21~",
            Key.F11 => "\u001b[23~",
            Key.F12 => "\u001b[24~",
            Key.Space when ctrl => "\u0000",
            _ => null,
        };

        if (seq != null)
        {
            Input?.Invoke(Encoding.UTF8.GetBytes(seq));
            ScrollToBottom();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key >= Key.A && e.Key <= Key.Z)
        {
            var code = e.Key - Key.A + 1;
            Input?.Invoke(new[] { (byte)code });
            ScrollToBottom();
            e.Handled = true;
            return;
        }

        if (alt && e.Key >= Key.A && e.Key <= Key.Z && !ctrl)
        {
            Input?.Invoke(Encoding.UTF8.GetBytes("\u001b" + (char)('a' + (e.Key - Key.A))));
            ScrollToBottom();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.System && alt)
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var text = e.Text;
        if (text.Length == 1 && (text[0] < 0x20 || text[0] == 0x7F))
        {
            return;
        }

        Input?.Invoke(Encoding.UTF8.GetBytes(text));
        ScrollToBottom();
        e.Handled = true;
    }
}
