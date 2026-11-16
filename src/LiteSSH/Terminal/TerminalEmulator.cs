using System.Text;

namespace LiteSSH.Terminal;

/// <summary>VT100/xterm-compatible escape sequence parser feeding a <see cref="TermScreen"/>.</summary>
public sealed class TerminalEmulator
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Charset,
        Csi,
        CsiIgnore,
        Osc,
        OscEscape,
        Dcs,
        DcsEscape,
    }

    private readonly TermScreen _screen;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _text = new();
    private readonly int[] _params = new int[32];
    private readonly char[] _prefix = new char[2];
    private State _state = State.Ground;
    private int _paramCount;
    private char _privateMarker;
    private char _intermediate;
    private char _charsetSlot;
    private bool _dirty;
    private char[] _chars = new char[8192];

    private static readonly string[] GraphicsMap =
    {
        "\u25c6", "\u2592", "\u2409", "\u240c", "\u240d", "\u240a", "\u00b0", "\u00b1",
        "\u2424", "\u240b", "\u2518", "\u2510", "\u250c", "\u2514", "\u253c", "\u23ba",
        "\u23bb", "\u2500", "\u23bc", "\u23bd", "\u251c", "\u2524", "\u2534", "\u252c",
        "\u2502", "\u2264", "\u2265", "\u03c0", "\u2260", "\u00a3", "\u00b7",
    };

    public TerminalEmulator(int cols, int rows, int scrollback)
    {
        _screen = new TermScreen(cols, rows, scrollback);
        _screen.Respond = text => Response?.Invoke(text);
        _screen.OnBell = () => Bell?.Invoke();
        _screen.OnTitle = title => TitleChanged?.Invoke(title);
    }

    public TermScreen Screen => _screen;

    public Action<string>? Response { get; set; }

    public Action? Bell { get; set; }

    public Action<string>? TitleChanged { get; set; }

    public Action<string>? Clipboard { get; set; }

    public event EventHandler? Damaged;

    public void Feed(byte[] data)
    {
        var needed = _decoder.GetCharCount(data, 0, data.Length, false) + 2;
        if (_chars.Length < needed)
        {
            _chars = new char[Math.Max(needed, _chars.Length * 2)];
        }

        var count = _decoder.GetChars(data, 0, data.Length, _chars, 0, false);
        for (var i = 0; i < count; i++)
        {
            Consume(_chars[i]);
        }

        if (_dirty)
        {
            _dirty = false;
            Damaged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reset()
    {
        _state = State.Ground;
        _text.Clear();
        _screen.FullReset();
        Damaged?.Invoke(this, EventArgs.Empty);
    }

    public void Resize(int cols, int rows)
    {
        _screen.Resize(cols, rows);
        Damaged?.Invoke(this, EventArgs.Empty);
    }

    private void Consume(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;
            case State.Escape:
                Escape(c);
                break;
            case State.EscapeIntermediate:
                if (c >= 0x20 && c <= 0x2F)
                {
                    _intermediate = c;
                    return;
                }
                EscapeDispatch(c);
                _state = State.Ground;
                break;
            case State.Charset:
                _state = State.Ground;
                if (_charsetSlot == '(')
                {
                    _graphicsMode = c == '0';
                }
                break;
            case State.Csi:
                Csi(c);
                break;
            case State.CsiIgnore:
                if (c >= 0x40 && c <= 0x7E)
                {
                    _state = State.Ground;
                }
                break;
            case State.Osc:
                Osc(c);
                break;
            case State.OscEscape:
                if (c == '\\')
                {
                    FinishOsc();
                }
                else
                {
                    _state = State.Osc;
                    _text.Append('');
                    _text.Append(c);
                }
                break;
            case State.Dcs:
                Dcs(c);
                break;
            case State.DcsEscape:
                if (c == '\\')
                {
                    _state = State.Ground;
                    _text.Clear();
                }
                else
                {
                    _state = State.Dcs;
                }
                break;
        }
    }

    private void Ground(char c)
    {
        if (c == '\u001b')
        {
            _state = State.Escape;
            return;
        }

        if (c < 0x20)
        {
            switch (c)
            {
                case '\a':
                    _screen.OnBell?.Invoke();
                    break;
                case '\b':
                    _screen.Backspace();
                    _dirty = true;
                    break;
                case '\t':
                    _screen.Tab();
                    _dirty = true;
                    break;
                case '\n':
                case '\v':
                case '\f':
                    _screen.LineFeed();
                    _dirty = true;
                    break;
                case '\r':
                    _screen.CarriageReturn();
                    _dirty = true;
                    break;
            }
            return;
        }

        if (c == '\u007f')
        {
            return;
        }

        int cp = c;
        if (char.IsHighSurrogate(c))
        {
            _text.Clear();
            _text.Append(c);
            _state = State.Ground;
            _pendingSurrogate = c;
            return;
        }

        if (char.IsLowSurrogate(c) && _pendingSurrogate != 0)
        {
            cp = char.ConvertToUtf32(_pendingSurrogate, c);
            _pendingSurrogate = '\0';
        }

        if (cp < 0x20)
        {
            return;
        }

        if (_graphicsMode && cp >= 0x60 && cp <= 0x7E)
        {
            var mapped = GraphicsMap[cp - 0x60];
            _screen.Print(mapped[0], 1);
        }
        else
        {
            _screen.Print(cp, CharWidth.Of(cp));
        }
        _dirty = true;
    }

    private char _pendingSurrogate;
    private bool _graphicsMode;

    private void Escape(char c)
    {
        switch (c)
        {
            case '[':
                _state = State.Csi;
                ResetCsi();
                return;
            case ']':
                _state = State.Osc;
                _text.Clear();
                return;
            case 'P':
            case '_':
            case '^':
            case 'X':
                _state = State.Dcs;
                _text.Clear();
                return;
            case '(':
            case ')':
            case '*':
            case '+':
                _charsetSlot = c;
                _state = State.Charset;
                return;
            case '#':
                _intermediate = '#';
                _state = State.EscapeIntermediate;
                return;
            case ' ':
                _state = State.EscapeIntermediate;
                _intermediate = ' ';
                return;
            case '%':
            case '"':
                _state = State.EscapeIntermediate;
                _intermediate = c;
                return;
            default:
                EscapeDispatch(c);
                _state = State.Ground;
                return;
        }
    }

    private void EscapeDispatch(char c)
    {
        switch (c)
        {
            case '7':
                _screen.SaveCursor();
                break;
            case '8':
                _screen.RestoreCursor();
                _dirty = true;
                break;
            case 'D':
                _screen.LineFeed();
                _dirty = true;
                break;
            case 'E':
                _screen.CarriageReturn();
                _screen.LineFeed();
                _dirty = true;
                break;
            case 'H':
                _screen.SetTabStop();
                break;
            case 'M':
                _screen.ReverseIndex();
                _dirty = true;
                break;
            case 'c':
                _screen.FullReset();
                _dirty = true;
                break;
            case '=':
                // application keypad, ignored
                break;
            case '>':
                break;
            case 'N':
            case 'O':
                break;
        }
    }

    private void ResetCsi()
    {
        _paramCount = 0;
        _params[0] = 0;
        _privateMarker = '\0';
        _intermediate = '\0';
    }

    private void Csi(char c)
    {
        if (c == '\u001b')
        {
            _state = State.Escape;
            return;
        }

        if (c == '?' || c == '>' || c == '<' || c == '=')
        {
            _privateMarker = c;
            return;
        }

        if (c >= '0' && c <= '9')
        {
            if (_paramCount == 0)
            {
                _paramCount = 1;
                _params[0] = 0;
            }
            _params[_paramCount - 1] = Math.Min(65535, _params[_paramCount - 1] * 10 + (c - '0'));
            return;
        }

        if (c == ';' || c == ':')
        {
            if (_paramCount == 0)
            {
                _paramCount = 1;
            }
            if (_paramCount < _params.Length)
            {
                _params[_paramCount] = 0;
                _paramCount++;
            }
            return;
        }

        if (c >= 0x20 && c <= 0x2F)
        {
            _intermediate = c;
            return;
        }

        if (c < 0x40 || c > 0x7E)
        {
            return;
        }

        CsiDispatch(c);
        _state = State.Ground;
    }

    private int Param(int index, int fallback)
    {
        if (index >= _paramCount)
        {
            return fallback;
        }
        var value = _params[index];
        return value == 0 ? fallback : value;
    }

    private int ParamRaw(int index)
        => index < _paramCount ? _params[index] : 0;

    private void CsiDispatch(char command)
    {
        if (_intermediate != '\0')
        {
            return;
        }

        switch (command)
        {
            case 'A':
                _screen.MoveCursor(0, -Param(0, 1));
                break;
            case 'B':
            case 'e':
                _screen.MoveCursor(0, Param(0, 1));
                break;
            case 'C':
            case 'a':
                _screen.MoveCursor(Param(0, 1), 0);
                break;
            case 'D':
                _screen.MoveCursor(-Param(0, 1), 0);
                break;
            case 'E':
                _screen.CarriageReturn();
                _screen.MoveCursor(0, Param(0, 1));
                break;
            case 'F':
                _screen.CarriageReturn();
                _screen.MoveCursor(0, -Param(0, 1));
                break;
            case 'G':
            case '`':
                _screen.SetColumn(Param(0, 1) - 1);
                break;
            case 'd':
                _screen.SetRow(Param(0, 1) - 1);
                break;
            case 'H':
            case 'f':
                _screen.SetCursor(Param(0, 1) - 1, Param(1, 1) - 1);
                break;
            case 'I':
                _screen.Tab(Param(0, 1));
                break;
            case 'Z':
                _screen.BackTab(Param(0, 1));
                break;
            case 'J':
                _screen.EraseInDisplay(ParamRaw(0));
                break;
            case 'K':
                _screen.EraseInLine(ParamRaw(0));
                break;
            case 'L':
                _screen.InsertLines(Param(0, 1));
                break;
            case 'M':
                _screen.DeleteLines(Param(0, 1));
                break;
            case 'P':
                _screen.DeleteChars(Param(0, 1));
                break;
            case '@':
                _screen.InsertChars(Param(0, 1));
                break;
            case 'X':
                _screen.EraseChars(Param(0, 1));
                break;
            case 'S':
                _screen.ScrollUp(Param(0, 1));
                break;
            case 'T':
                _screen.ScrollDown(Param(0, 1));
                break;
            case 'b':
                _screen.RepeatLast(Param(0, 1));
                break;
            case 'g':
                _screen.ClearTabStops(ParamRaw(0) == 3);
                break;
            case 'm':
                ApplySgr();
                break;
            case 'h':
                SetMode(true);
                break;
            case 'l':
                SetMode(false);
                break;
            case 'r':
                _screen.SetScrollRegion(Param(0, 1) - 1, Param(1, _screen.Rows) - 1);
                _dirty = true;
                break;
            case 's':
                _screen.SaveCursor();
                break;
            case 'u':
                _screen.RestoreCursor();
                break;
            case 'n':
                if (ParamRaw(0) == 6)
                {
                    var row = _screen.CursorY + 1;
                    var col = _screen.CursorX + 1;
                    Response?.Invoke(_privateMarker == '?' ? $"\u001b[?{row};{col}R" : $"\u001b[{row};{col}R");
                }
                else if (ParamRaw(0) == 5)
                {
                    Response?.Invoke("\u001b[0n");
                }
                break;
            case 'c':
                Response?.Invoke(_privateMarker == '>' ? "\u001b[>0;276;0c" : "\u001b[?62;22c");
                break;
            case 't':
                break;
            case 'q':
                break;
        }

        _dirty = true;
    }

    private void SetMode(bool enabled)
    {
        if (_privateMarker == '?')
        {
            for (var i = 0; i < _paramCount; i++)
            {
                switch (_params[i])
                {
                    case 1:
                        _screen.ApplicationCursorKeys = enabled;
                        break;
                    case 6:
                        _screen.OriginMode = enabled;
                        _screen.SetCursor(0, 0);
                        break;
                    case 7:
                        _screen.AutoWrap = enabled;
                        break;
                    case 25:
                        _screen.CursorVisible = enabled;
                        break;
                    case 47:
                    case 1047:
                        _screen.SetAltScreen(enabled, false);
                        break;
                    case 1048:
                        if (enabled)
                        {
                            _screen.SaveCursor();
                        }
                        else
                        {
                            _screen.RestoreCursor();
                        }
                        break;
                    case 1049:
                        _screen.SetAltScreen(enabled, true);
                        break;
                    case 2004:
                        _screen.BracketedPaste = enabled;
                        break;
                }
            }
        }
        else
        {
            for (var i = 0; i < _paramCount; i++)
            {
                if (_params[i] == 4)
                {
                    _screen.InsertMode = enabled;
                }
            }
        }

        _dirty = true;
    }

    private void ApplySgr()
    {
        if (_paramCount == 0)
        {
            _screen.ResetAttributes();
            return;
        }

        for (var i = 0; i < _paramCount; i++)
        {
            var code = _params[i];
            switch (code)
            {
                case 0:
                    _screen.ResetAttributes();
                    break;
                case 1:
                    _screen.Flags |= CellFlags.Bold;
                    break;
                case 2:
                    _screen.Flags |= CellFlags.Faint;
                    break;
                case 3:
                    _screen.Flags |= CellFlags.Italic;
                    break;
                case 4:
                case 21:
                    _screen.Flags |= CellFlags.Underline;
                    break;
                case 22:
                    _screen.Flags &= ~(CellFlags.Bold | CellFlags.Faint);
                    break;
                case 23:
                    _screen.Flags &= ~CellFlags.Italic;
                    break;
                case 24:
                    _screen.Flags &= ~CellFlags.Underline;
                    break;
                case 7:
                    _screen.Flags |= CellFlags.Reverse;
                    break;
                case 27:
                    _screen.Flags &= ~CellFlags.Reverse;
                    break;
                case 8:
                    _screen.Flags |= CellFlags.Hidden;
                    break;
                case 28:
                    _screen.Flags &= ~CellFlags.Hidden;
                    break;
                case 30:
                case 31:
                case 32:
                case 33:
                case 34:
                case 35:
                case 36:
                case 37:
                    _screen.Fg = code - 30;
                    break;
                case 39:
                    _screen.Fg = -1;
                    break;
                case 40:
                case 41:
                case 42:
                case 43:
                case 44:
                case 45:
                case 46:
                case 47:
                    _screen.Bg = code - 40;
                    break;
                case 49:
                    _screen.Bg = -1;
                    break;
                case 90:
                case 91:
                case 92:
                case 93:
                case 94:
                case 95:
                case 96:
                case 97:
                    _screen.Fg = code - 90 + 8;
                    break;
                case 100:
                case 101:
                case 102:
                case 103:
                case 104:
                case 105:
                case 106:
                case 107:
                    _screen.Bg = code - 100 + 8;
                    break;
                case 38:
                    i = ParseExtendedColor(i, isForeground: true);
                    break;
                case 48:
                    i = ParseExtendedColor(i, isForeground: false);
                    break;
            }
        }
    }

    private int ParseExtendedColor(int index, bool isForeground)
    {
        if (index + 1 >= _paramCount)
        {
            return index;
        }

        var mode = _params[index + 1];
        if (mode == 5 && index + 2 < _paramCount)
        {
            var palette = _params[index + 2];
            if (isForeground)
            {
                _screen.Fg = palette;
            }
            else
            {
                _screen.Bg = palette;
            }
            return index + 2;
        }

        if (mode == 2 && index + 4 < _paramCount)
        {
            var r = Math.Clamp(_params[index + 2], 0, 255);
            var g = Math.Clamp(_params[index + 3], 0, 255);
            var b = Math.Clamp(_params[index + 4], 0, 255);
            var rgb = 0x1000000 | (r << 16) | (g << 8) | b;
            if (isForeground)
            {
                _screen.Fg = rgb;
            }
            else
            {
                _screen.Bg = rgb;
            }
            return index + 4;
        }

        return index + 1;
    }

    private void Osc(char c)
    {
        if (c == '\a')
        {
            FinishOsc();
            return;
        }
        if (c == '\u001b')
        {
            _state = State.OscEscape;
            return;
        }
        if (_text.Length < 8192)
        {
            _text.Append(c);
        }
    }

    private void FinishOsc()
    {
        var payload = _text.ToString();
        _text.Clear();
        _state = State.Ground;

        var split = payload.IndexOf(';');
        if (split < 0)
        {
            return;
        }

        var code = payload[..split];
        var value = payload[(split + 1)..];
        switch (code)
        {
            case "0":
            case "2":
                _screen.Title = value;
                TitleChanged?.Invoke(value);
                break;
            case "52":
                var parts = value.Split(';');
                if (parts.Length >= 2 && parts[1].Length > 0)
                {
                    try
                    {
                        Clipboard?.Invoke(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])));
                    }
                    catch
                    {
                        // malformed
                    }
                }
                break;
        }
    }

    private void Dcs(char c)
    {
        if (c == '\u001b')
        {
            _state = State.DcsEscape;
            return;
        }
        _state = State.Dcs;
    }
}

