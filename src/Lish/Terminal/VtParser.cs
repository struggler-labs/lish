using System;
using System.Collections.Generic;
using System.Text;

namespace Lish.Terminal
{
    /// <summary>VT100/xterm 제어 시퀀스 파서.</summary>
    internal sealed partial class VtTerminal
    {
        private enum PState { Ground, Esc, EscInter, Charset, Csi, Osc, OscEsc, Dcs, DcsEsc }

        private PState _state;
        private char _escInter;
        private readonly List<int> _params = new List<int>(16);
        private int _curParam = -1;
        private char _private;
        private char _csiInter;
        private readonly StringBuilder _osc = new StringBuilder();
        private char _pendingHigh;

        /// <summary>DEC Special Graphics (ESC ( 0) 문자 → 유니코드 선 문자</summary>
        private static readonly string DecGraphics =
            "◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·"; // 0x60 ~ 0x7E

        public void Write(char[] buf, int start, int count)
        {
            lock (Sync)
            {
                for (var i = start; i < start + count; i++)
                    Process(buf[i]);
                Version++;
            }
        }

        public void Write(string s)
        {
            var chars = s.ToCharArray();
            Write(chars, 0, chars.Length);
        }

        private void Process(char c)
        {
            switch (_state)
            {
                case PState.Ground:
                    if (c < 0x20 || c == 0x7F)
                        Control(c);
                    else
                        PrintChar(c);
                    return;

                case PState.Esc:
                    EscDispatch(c);
                    return;

                case PState.EscInter:
                    if (_escInter == '#' && c == '8')
                        AlignmentTest();
                    _state = PState.Ground;
                    return;

                case PState.Charset:
                    var graphics = c == '0';
                    if (_escInter == '(')
                        _g0Graphics = graphics;
                    else if (_escInter == ')')
                        _g1Graphics = graphics;
                    _state = PState.Ground;
                    return;

                case PState.Csi:
                    CsiCollect(c);
                    return;

                case PState.Osc:
                    if (c == 0x07)
                    {
                        OscDispatch();
                        _state = PState.Ground;
                    }
                    else if (c == 0x1B)
                    {
                        _state = PState.OscEsc;
                    }
                    else if (_osc.Length < 4096)
                    {
                        _osc.Append(c);
                    }
                    return;

                case PState.OscEsc:
                    if (c == '\\')
                    {
                        OscDispatch();
                        _state = PState.Ground;
                    }
                    else
                    {
                        OscDispatch();
                        _state = PState.Esc;
                        EscDispatch(c);
                    }
                    return;

                case PState.Dcs:
                    if (c == 0x1B)
                        _state = PState.DcsEsc;
                    else if (c == 0x07)
                        _state = PState.Ground;
                    return;

                case PState.DcsEsc:
                    _state = c == '\\' ? PState.Ground : PState.Dcs;
                    return;
            }
        }

        private void Control(char c)
        {
            switch (c)
            {
                case '\a': Bell?.Invoke(); break;
                case '\b': Backspace(); break;
                case '\t': Tab(1); break;
                case '\n':
                case '\v':
                case '\f': LineFeed(); break;
                case '\r': CarriageReturn(); break;
                case (char)0x0E: _shiftOut = true; break;
                case (char)0x0F: _shiftOut = false; break;
                case (char)0x1B:
                    _state = PState.Esc;
                    break;
            }
        }

        private void PrintChar(char c)
        {
            if (char.IsHighSurrogate(c))
            {
                _pendingHigh = c;
                return;
            }
            if (char.IsLowSurrogate(c))
            {
                if (_pendingHigh == '\0')
                    return;
                var high = _pendingHigh;
                _pendingHigh = '\0';
                var cp = char.ConvertToUtf32(high, c);
                var w = CharWidth.Of(cp);
                if (w > 0)
                    Print(high, c, w);
                return;
            }
            _pendingHigh = '\0';

            if ((_shiftOut ? _g1Graphics : _g0Graphics) && c >= 0x60 && c <= 0x7E)
                c = DecGraphics[c - 0x60];

            var width = CharWidth.Of(c);
            if (width > 0)
                Print(c, '\0', width);
        }

        private void EscDispatch(char c)
        {
            _state = PState.Ground;
            switch (c)
            {
                case '[':
                    _params.Clear();
                    _curParam = -1;
                    _private = '\0';
                    _csiInter = '\0';
                    _state = PState.Csi;
                    break;
                case ']':
                    _osc.Clear();
                    _state = PState.Osc;
                    break;
                case 'P':
                case 'X':
                case '^':
                case '_':
                    _state = PState.Dcs;
                    break;
                case '(':
                case ')':
                case '*':
                case '+':
                    _escInter = c;
                    _state = PState.Charset;
                    break;
                case '#':
                case '%':
                case ' ':
                    _escInter = c;
                    _state = PState.EscInter;
                    break;
                case '7': SaveCursor(); break;
                case '8': RestoreCursor(); break;
                case 'D': Index(); break;
                case 'E': CarriageReturn(); Index(); break;
                case 'H': _tabs[CursorX] = true; break;
                case 'M': ReverseIndex(); break;
                case 'c': FullReset(); break;
                case '=': AppKeypad = true; break;
                case '>': AppKeypad = false; break;
                case '\x1b': _state = PState.Esc; break;
            }
        }

        private void CsiCollect(char c)
        {
            if (c >= '0' && c <= '9')
            {
                _curParam = (_curParam < 0 ? 0 : _curParam) * 10 + (c - '0');
                if (_curParam > 65535)
                    _curParam = 65535;
            }
            else if (c == ';' || c == ':')
            {
                _params.Add(_curParam);
                _curParam = -1;
            }
            else if (c >= '<' && c <= '?')
            {
                _private = c;
            }
            else if (c >= 0x20 && c <= 0x2F)
            {
                _csiInter = c;
            }
            else if (c >= 0x40 && c <= 0x7E)
            {
                if (_curParam >= 0 || _params.Count > 0)
                    _params.Add(_curParam);
                _state = PState.Ground;
                CsiDispatch(c);
            }
            else if (c == 0x1B)
            {
                _state = PState.Esc;
            }
            else if (c < 0x20)
            {
                Control(c);
            }
        }

        /// <summary>파라미터 (없거나 0이면 기본값)</summary>
        private int P(int i, int def) => i < _params.Count && _params[i] > 0 ? _params[i] : def;

        /// <summary>파라미터 원본 (없으면 -1)</summary>
        private int Raw(int i) => i < _params.Count ? _params[i] : -1;

        private void CsiDispatch(char c)
        {
            if (_private == '?')
            {
                if (c == 'h' || c == 'l')
                {
                    for (var i = 0; i < _params.Count; i++)
                        SetDecMode(_params[i], c == 'h');
                }
                else if (c == 'J')
                {
                    EraseInDisplay(Math.Max(0, Raw(0)));
                }
                else if (c == 'K')
                {
                    EraseInLine(Math.Max(0, Raw(0)));
                }
                return;
            }
            if (_private == '>')
            {
                if (c == 'c')
                    Respond("\x1b[>0;10;1c");
                return;
            }
            if (_private != '\0')
                return;

            if (_csiInter == '!' && c == 'p')
            {
                SoftReset();
                return;
            }
            if (_csiInter != '\0')
                return; // DECSCUSR(커서 모양) 등은 무시

            switch (c)
            {
                case '@': InsertChars(P(0, 1)); break;
                case 'A': MoveCursorVertical(-P(0, 1)); break;
                case 'B':
                case 'e': MoveCursorVertical(P(0, 1)); break;
                case 'C':
                case 'a': MoveCursorHorizontal(P(0, 1)); break;
                case 'D': MoveCursorHorizontal(-P(0, 1)); break;
                case 'E': MoveCursorVertical(P(0, 1)); CursorX = 0; break;
                case 'F': MoveCursorVertical(-P(0, 1)); CursorX = 0; break;
                case 'G':
                case '`': _wrapPending = false; CursorX = Clamp(P(0, 1) - 1, 0, Cols - 1); break;
                case 'H':
                case 'f': SetCursor(P(1, 1) - 1, P(0, 1) - 1); break;
                case 'I': Tab(P(0, 1)); break;
                case 'J': EraseInDisplay(Math.Max(0, Raw(0))); break;
                case 'K': EraseInLine(Math.Max(0, Raw(0))); break;
                case 'L': InsertLines(P(0, 1)); break;
                case 'M': DeleteLines(P(0, 1)); break;
                case 'P': DeleteChars(P(0, 1)); break;
                case 'S': ScrollUp(P(0, 1), _top, _bottom, false); break;
                case 'T': ScrollDown(P(0, 1), _top, _bottom); break;
                case 'X': EraseChars(P(0, 1)); break;
                case 'Z': BackTab(P(0, 1)); break;
                case 'b':
                    for (var i = Math.Min(P(0, 1), 65535); i > 0; i--)
                        Print(_lastPrinted, '\0', Math.Max(1, CharWidth.Of(_lastPrinted)));
                    break;
                case 'c': Respond("\x1b[?62;22c"); break;
                case 'd': SetCursor(CursorX, P(0, 1) - 1); break;
                case 'g':
                    if (Raw(0) == 3)
                        Array.Clear(_tabs, 0, _tabs.Length);
                    else if (Raw(0) <= 0)
                        _tabs[CursorX] = false;
                    break;
                case 'h':
                case 'l':
                    for (var i = 0; i < _params.Count; i++)
                    {
                        if (_params[i] == 4) InsertMode = c == 'h';
                        else if (_params[i] == 20) NewLineMode = c == 'h';
                    }
                    break;
                case 'm': Sgr(); break;
                case 'n':
                    if (Raw(0) == 5)
                        Respond("\x1b[0n");
                    else if (Raw(0) == 6)
                        Respond($"\x1b[{(OriginMode ? CursorY - _top : CursorY) + 1};{CursorX + 1}R");
                    break;
                case 'r': SetScrollRegion(P(0, 1) - 1, P(1, Rows) - 1); break;
                case 's': SaveCursor(); break;
                case 'u': RestoreCursor(); break;
            }
        }

        private void SetDecMode(int mode, bool on)
        {
            switch (mode)
            {
                case 1: AppCursorKeys = on; break;
                case 6: OriginMode = on; SetCursor(0, 0); break;
                case 7: AutoWrap = on; break;
                case 25: CursorVisible = on; break;
                case 47: SetAltScreen(on, false, false); break;
                case 1047: SetAltScreen(on, false, !on); break;
                case 1048: if (on) SaveCursor(); else RestoreCursor(); break;
                case 1049: SetAltScreen(on, true, on); break;
                case 9:
                case 1000:
                case 1002:
                case 1003: MouseTracking = on ? mode : 0; break;
                case 1006: MouseSgr = on; break;
                case 2004: BracketedPaste = on; break;
            }
        }

        private void Sgr()
        {
            if (_params.Count == 0)
            {
                _fg = _bg = Cell.DefaultColor;
                _attr = CellAttr.None;
                return;
            }
            for (var i = 0; i < _params.Count; i++)
            {
                var p = Math.Max(0, _params[i]);
                switch (p)
                {
                    case 0: _fg = _bg = Cell.DefaultColor; _attr = CellAttr.None; break;
                    case 1: _attr |= CellAttr.Bold; break;
                    case 2: _attr |= CellAttr.Dim; break;
                    case 3: _attr |= CellAttr.Italic; break;
                    case 4:
                    case 21: _attr |= CellAttr.Underline; break;
                    case 5:
                    case 6: _attr |= CellAttr.Blink; break;
                    case 7: _attr |= CellAttr.Inverse; break;
                    case 8: _attr |= CellAttr.Hidden; break;
                    case 9: _attr |= CellAttr.Strike; break;
                    case 22: _attr &= ~(CellAttr.Bold | CellAttr.Dim); break;
                    case 23: _attr &= ~CellAttr.Italic; break;
                    case 24: _attr &= ~CellAttr.Underline; break;
                    case 25: _attr &= ~CellAttr.Blink; break;
                    case 27: _attr &= ~CellAttr.Inverse; break;
                    case 28: _attr &= ~CellAttr.Hidden; break;
                    case 29: _attr &= ~CellAttr.Strike; break;
                    case 38: _fg = ExtendedColor(ref i, _fg); break;
                    case 39: _fg = Cell.DefaultColor; break;
                    case 48: _bg = ExtendedColor(ref i, _bg); break;
                    case 49: _bg = Cell.DefaultColor; break;
                    default:
                        if (p >= 30 && p <= 37) _fg = p - 30;
                        else if (p >= 40 && p <= 47) _bg = p - 40;
                        else if (p >= 90 && p <= 97) _fg = p - 90 + 8;
                        else if (p >= 100 && p <= 107) _bg = p - 100 + 8;
                        break;
                }
            }
        }

        /// <summary>38;5;n / 38;2;r;g;b (콜론 형식 38:2::r:g:b 포함)</summary>
        private int ExtendedColor(ref int i, int current)
        {
            var kind = Raw(i + 1);
            if (kind == 5 && i + 2 < _params.Count)
            {
                var n = Raw(i + 2);
                i += 2;
                return n >= 0 && n <= 255 ? n : current;
            }
            if (kind == 2)
            {
                // 색 공간 ID가 비어 있는 콜론 형식 (38:2::r:g:b)
                var offset = Raw(i + 2) == -1 && i + 5 < _params.Count ? 3 : 2;
                if (i + offset + 2 < _params.Count)
                {
                    var r = Clamp(Raw(i + offset), 0, 255);
                    var g = Clamp(Raw(i + offset + 1), 0, 255);
                    var b = Clamp(Raw(i + offset + 2), 0, 255);
                    i += offset + 2;
                    return Cell.RgbFlag | (r << 16) | (g << 8) | b;
                }
            }
            return current;
        }

        private void OscDispatch()
        {
            var s = _osc.ToString();
            var sep = s.IndexOf(';');
            if (sep <= 0)
                return;
            var cmd = s.Substring(0, sep);
            if (cmd == "0" || cmd == "2")
            {
                Title = s.Substring(sep + 1);
                TitleChanged?.Invoke(Title);
            }
        }

        private void AlignmentTest()
        {
            for (var y = 0; y < Rows; y++)
                for (var x = 0; x < Cols; x++)
                    _screen[y].Cells[x] = new Cell { Ch = 'E', Fg = Cell.DefaultColor, Bg = Cell.DefaultColor, Width = 1 };
        }
    }
}
