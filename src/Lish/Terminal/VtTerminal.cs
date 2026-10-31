using System;
using System.Collections.Generic;
using System.Text;

namespace Lish.Terminal
{
    /// <summary>
    /// xterm 호환 터미널 상태(화면 버퍼, 커서, 모드)와 화면 조작.
    /// 파서는 VtParser.cs (partial). 모든 접근은 Sync 잠금 하에서 한다.
    /// </summary>
    internal sealed partial class VtTerminal
    {
        public readonly object Sync = new object();

        public int Cols { get; private set; }
        public int Rows { get; private set; }

        private Line[] _main;
        private Line[] _alt;
        private Line[] _screen;

        public readonly List<Line> Scrollback = new List<Line>();
        public int MaxScrollback;
        /// <summary>스크롤백 앞에서 잘려나간 누적 줄 수 (선택 영역 좌표 보정용)</summary>
        public long Dropped { get; private set; }
        /// <summary>스크롤백에 들어간 누적 줄 수 (스크롤 위치 고정용)</summary>
        public long Pushed { get; private set; }
        /// <summary>화면이 바뀔 때마다 증가 (렌더링 트리거)</summary>
        public long Version;

        public int CursorX;
        public int CursorY;
        private bool _wrapPending;

        private int _fg = Cell.DefaultColor;
        private int _bg = Cell.DefaultColor;
        private CellAttr _attr;

        private int _top;
        private int _bottom;
        private bool[] _tabs;

        // 모드
        public bool AppCursorKeys;
        public bool AppKeypad;
        public bool AutoWrap = true;
        public bool OriginMode;
        public bool InsertMode;
        public bool NewLineMode;
        public bool CursorVisible = true;
        public bool BracketedPaste;
        public bool AltScreen;
        /// <summary>0 = 끔, 1000 = 클릭, 1002 = 드래그, 1003 = 모든 움직임</summary>
        public int MouseTracking;
        public bool MouseSgr;

        // 문자셋 (DEC Special Graphics = 선 그리기)
        private bool _g0Graphics;
        private bool _g1Graphics;
        private bool _shiftOut;

        private struct SavedCursor
        {
            public int X, Y, Fg, Bg;
            public CellAttr Attr;
            public bool Origin, G0, G1, Pending;
        }

        private SavedCursor _saved;
        private char _lastPrinted = ' ';

        public string Title { get; private set; }

        /// <summary>터미널이 원격으로 돌려보내야 하는 응답 (DA, CPR 등)</summary>
        public event Action<string> Response;
        public event Action<string> TitleChanged;
        /// <summary>셸이 OSC 7 로 알린 현재 폴더 (호스트, 경로)</summary>
        public event Action<string, string> CwdChanged;
        public event Action Bell;

        public VtTerminal(int cols, int rows, int maxScrollback)
        {
            Cols = Math.Max(2, cols);
            Rows = Math.Max(2, rows);
            MaxScrollback = maxScrollback;
            _main = NewScreen(Cols, Rows);
            _alt = NewScreen(Cols, Rows);
            _screen = _main;
            _top = 0;
            _bottom = Rows - 1;
            ResetTabs();
        }

        private static Line[] NewScreen(int cols, int rows)
        {
            var s = new Line[rows];
            for (var i = 0; i < rows; i++)
                s[i] = new Line(cols, Cell.DefaultColor);
            return s;
        }

        /// <summary>스크롤백 + 현재 화면을 이은 인덱스로 줄 조회.</summary>
        public Line GetLine(int index)
        {
            if (index < 0)
                return null;
            if (index < Scrollback.Count)
                return Scrollback[index];
            index -= Scrollback.Count;
            return index < Rows ? _screen[index] : null;
        }

        public int TotalLines => Scrollback.Count + Rows;

        // ───────────── 크기 변경 ─────────────

        public void Resize(int cols, int rows)
        {
            cols = Math.Max(2, cols);
            rows = Math.Max(2, rows);
            if (cols == Cols && rows == Rows)
                return;

            foreach (var l in _main) l.Resize(cols);
            foreach (var l in _alt) l.Resize(cols);

            // 주 화면: 줄이면 커서 아래 빈 줄부터 버리고, 그래도 넘치면 위쪽을 스크롤백으로 보낸다.
            var mainCursorY = AltScreen ? _saved.Y : CursorY;
            _main = ResizeRows(_main, cols, rows, ref mainCursorY, allowScrollback: true);
            var altCursorY = AltScreen ? CursorY : 0;
            _alt = ResizeRows(_alt, cols, rows, ref altCursorY, allowScrollback: false);

            if (AltScreen)
            {
                CursorY = altCursorY;
                _saved.Y = Math.Min(mainCursorY, rows - 1);
            }
            else
            {
                CursorY = mainCursorY;
            }

            Cols = cols;
            Rows = rows;
            _screen = AltScreen ? _alt : _main;
            _top = 0;
            _bottom = rows - 1;
            CursorX = Math.Min(CursorX, cols - 1);
            CursorY = Clamp(CursorY, 0, rows - 1);
            _wrapPending = false;
            ResetTabs();
            Version++;
        }

        private Line[] ResizeRows(Line[] screen, int cols, int rows, ref int cursorY, bool allowScrollback)
        {
            var list = new List<Line>(screen);
            if (rows < list.Count)
            {
                var excess = list.Count - rows;
                var belowCursor = Math.Max(0, list.Count - 1 - cursorY);
                var removeBottom = Math.Min(excess, belowCursor);
                list.RemoveRange(list.Count - removeBottom, removeBottom);
                var removeTop = excess - removeBottom;
                for (var i = 0; i < removeTop; i++)
                {
                    if (allowScrollback)
                        PushScrollback(list[i]);
                }
                list.RemoveRange(0, removeTop);
                cursorY -= removeTop;
            }
            else if (rows > list.Count)
            {
                var add = rows - list.Count;
                if (allowScrollback && !AltScreen)
                {
                    // 스크롤백에서 줄을 되돌려 화면 위쪽을 채운다.
                    var pull = Math.Min(add, Scrollback.Count);
                    for (var i = 0; i < pull; i++)
                    {
                        var l = Scrollback[Scrollback.Count - 1];
                        Scrollback.RemoveAt(Scrollback.Count - 1);
                        Pushed--;
                        l.Resize(cols);
                        list.Insert(0, l);
                    }
                    cursorY += pull;
                    add -= pull;
                }
                for (var i = 0; i < add; i++)
                    list.Add(new Line(cols, Cell.DefaultColor));
            }
            return list.ToArray();
        }

        private void PushScrollback(Line line)
        {
            if (MaxScrollback <= 0)
                return;
            Scrollback.Add(line);
            Pushed++;
            // 한 줄씩 지우면 O(n) 이므로 여유분이 찰 때 한 번에 정리
            if (Scrollback.Count > MaxScrollback + 512)
            {
                var drop = Scrollback.Count - MaxScrollback;
                Scrollback.RemoveRange(0, drop);
                Dropped += drop;
            }
        }

        public void ClearScrollback()
        {
            lock (Sync)
            {
                Dropped += Scrollback.Count;
                Scrollback.Clear();
                Version++;
            }
        }

        // ───────────── 출력 ─────────────

        private Cell BlankCell => Cell.Blank(_bg);

        private void Print(char ch, char low, int width)
        {
            if (_wrapPending)
            {
                if (AutoWrap)
                {
                    _screen[CursorY].Wrapped = true;
                    CursorX = 0;
                    Index();
                }
                _wrapPending = false;
            }

            if (width == 2 && CursorX == Cols - 1)
            {
                if (AutoWrap)
                {
                    _screen[CursorY].Cells[CursorX] = BlankCell;
                    _screen[CursorY].Wrapped = true;
                    CursorX = 0;
                    Index();
                }
                else
                {
                    width = 1;
                }
            }

            var cells = _screen[CursorY].Cells;
            if (InsertMode)
                InsertBlanks(cells, CursorX, width);

            FixWideOverlap(cells, CursorX, width);
            cells[CursorX] = new Cell { Ch = ch, Low = low, Fg = _fg, Bg = _bg, Attr = _attr, Width = (byte)width };
            if (width == 2)
                cells[CursorX + 1] = new Cell { Ch = ' ', Fg = _fg, Bg = _bg, Attr = _attr, Width = 0 };

            CursorX += width;
            if (CursorX >= Cols)
            {
                CursorX = Cols - 1;
                _wrapPending = true;
            }
            _lastPrinted = ch;
        }

        /// <summary>전각 문자의 반쪽을 덮어쓸 때 나머지 반쪽을 지운다.</summary>
        private void FixWideOverlap(Cell[] cells, int x, int width)
        {
            if (cells[x].Width == 0 && x > 0)
                cells[x - 1] = BlankCell;
            var end = x + width - 1;
            if (end < Cols && cells[end].Width == 2 && end + 1 < Cols)
                cells[end + 1] = BlankCell;
        }

        private void InsertBlanks(Cell[] cells, int x, int n)
        {
            n = Math.Min(n, Cols - x);
            Array.Copy(cells, x, cells, x + n, Cols - x - n);
            for (var i = 0; i < n; i++)
                cells[x + i] = BlankCell;
        }

        private void CarriageReturn()
        {
            CursorX = 0;
            _wrapPending = false;
        }

        private void Index()
        {
            if (CursorY == _bottom)
                ScrollUp(1, _top, _bottom, true);
            else if (CursorY < Rows - 1)
                CursorY++;
        }

        private void ReverseIndex()
        {
            if (CursorY == _top)
                ScrollDown(1, _top, _bottom);
            else if (CursorY > 0)
                CursorY--;
        }

        private void LineFeed()
        {
            _wrapPending = false;
            Index();
            if (NewLineMode)
                CursorX = 0;
        }

        private void Backspace()
        {
            if (_wrapPending)
                _wrapPending = false;
            else if (CursorX > 0)
                CursorX--;
        }

        private void Tab(int n)
        {
            _wrapPending = false;
            for (var k = 0; k < n; k++)
            {
                var x = CursorX + 1;
                while (x < Cols - 1 && !_tabs[x])
                    x++;
                CursorX = Math.Min(x, Cols - 1);
            }
        }

        private void BackTab(int n)
        {
            _wrapPending = false;
            for (var k = 0; k < n; k++)
            {
                var x = CursorX - 1;
                while (x > 0 && !_tabs[x])
                    x--;
                CursorX = Math.Max(0, x);
            }
        }

        private void ResetTabs()
        {
            _tabs = new bool[Cols];
            for (var i = 8; i < Cols; i += 8)
                _tabs[i] = true;
        }

        private void ScrollUp(int n, int top, int bottom, bool toScrollback)
        {
            n = Math.Min(n, bottom - top + 1);
            for (var k = 0; k < n; k++)
            {
                var removed = _screen[top];
                Array.Copy(_screen, top + 1, _screen, top, bottom - top);
                if (toScrollback && top == 0 && !AltScreen)
                {
                    PushScrollback(removed);
                    _screen[bottom] = new Line(Cols, _bg);
                }
                else
                {
                    removed.Clear(_bg);
                    _screen[bottom] = removed;
                }
            }
        }

        private void ScrollDown(int n, int top, int bottom)
        {
            n = Math.Min(n, bottom - top + 1);
            for (var k = 0; k < n; k++)
            {
                var removed = _screen[bottom];
                Array.Copy(_screen, top, _screen, top + 1, bottom - top);
                removed.Clear(_bg);
                _screen[top] = removed;
            }
        }

        private void EraseInLine(int mode)
        {
            var cells = _screen[CursorY].Cells;
            int from, to;
            switch (mode)
            {
                case 1: from = 0; to = CursorX; break;
                case 2: from = 0; to = Cols - 1; break;
                default: from = CursorX; to = Cols - 1; break;
            }
            for (var x = from; x <= to && x < Cols; x++)
                cells[x] = BlankCell;
            if (mode != 1)
                _screen[CursorY].Wrapped = false;
        }

        private void EraseInDisplay(int mode)
        {
            switch (mode)
            {
                case 0:
                    EraseInLine(0);
                    for (var y = CursorY + 1; y < Rows; y++)
                        _screen[y].Clear(_bg);
                    break;
                case 1:
                    EraseInLine(1);
                    for (var y = 0; y < CursorY; y++)
                        _screen[y].Clear(_bg);
                    break;
                case 2:
                    for (var y = 0; y < Rows; y++)
                        _screen[y].Clear(_bg);
                    break;
                case 3:
                    Dropped += Scrollback.Count;
                    Scrollback.Clear();
                    break;
            }
        }

        private void InsertChars(int n)
        {
            InsertBlanks(_screen[CursorY].Cells, CursorX, n);
        }

        private void DeleteChars(int n)
        {
            var cells = _screen[CursorY].Cells;
            n = Math.Min(n, Cols - CursorX);
            Array.Copy(cells, CursorX + n, cells, CursorX, Cols - CursorX - n);
            for (var x = Cols - n; x < Cols; x++)
                cells[x] = BlankCell;
        }

        private void EraseChars(int n)
        {
            var cells = _screen[CursorY].Cells;
            for (var x = CursorX; x < CursorX + n && x < Cols; x++)
                cells[x] = BlankCell;
        }

        private void InsertLines(int n)
        {
            if (CursorY < _top || CursorY > _bottom)
                return;
            ScrollDown(n, CursorY, _bottom);
            CursorX = 0;
        }

        private void DeleteLines(int n)
        {
            if (CursorY < _top || CursorY > _bottom)
                return;
            ScrollUp(n, CursorY, _bottom, false);
            CursorX = 0;
        }

        private void SetCursor(int x, int y)
        {
            _wrapPending = false;
            CursorX = Clamp(x, 0, Cols - 1);
            if (OriginMode)
                CursorY = Clamp(y + _top, _top, _bottom);
            else
                CursorY = Clamp(y, 0, Rows - 1);
        }

        private void MoveCursorVertical(int dy)
        {
            _wrapPending = false;
            // 스크롤 영역 안에 있으면 영역 경계에서 멈춘다.
            var min = CursorY >= _top ? _top : 0;
            var max = CursorY <= _bottom ? _bottom : Rows - 1;
            CursorY = Clamp(CursorY + dy, min, max);
        }

        private void MoveCursorHorizontal(int dx)
        {
            _wrapPending = false;
            CursorX = Clamp(CursorX + dx, 0, Cols - 1);
        }

        private void SetScrollRegion(int top, int bottom)
        {
            if (top < bottom && bottom < Rows)
            {
                _top = top;
                _bottom = bottom;
                SetCursor(0, 0);
            }
        }

        private void SaveCursor()
        {
            _saved = new SavedCursor
            {
                X = CursorX, Y = CursorY, Fg = _fg, Bg = _bg, Attr = _attr,
                Origin = OriginMode, G0 = _g0Graphics, G1 = _g1Graphics, Pending = _wrapPending,
            };
        }

        private void RestoreCursor()
        {
            CursorX = Clamp(_saved.X, 0, Cols - 1);
            CursorY = Clamp(_saved.Y, 0, Rows - 1);
            _fg = _saved.Fg;
            _bg = _saved.Bg;
            _attr = _saved.Attr;
            OriginMode = _saved.Origin;
            _g0Graphics = _saved.G0;
            _g1Graphics = _saved.G1;
            _wrapPending = _saved.Pending;
        }

        private void SetAltScreen(bool on, bool saveRestore, bool clear)
        {
            if (on == AltScreen)
                return;
            if (on)
            {
                if (saveRestore)
                    SaveCursor();
                _screen = _alt;
                AltScreen = true;
                if (clear)
                    foreach (var l in _alt) l.Clear(_bg);
            }
            else
            {
                if (clear)
                    foreach (var l in _alt) l.Clear(Cell.DefaultColor);
                _screen = _main;
                AltScreen = false;
                if (saveRestore)
                    RestoreCursor();
            }
            _top = 0;
            _bottom = Rows - 1;
        }

        private void FullReset()
        {
            _fg = _bg = Cell.DefaultColor;
            _attr = CellAttr.None;
            SetAltScreen(false, false, false);
            foreach (var l in _main) l.Clear(Cell.DefaultColor);
            foreach (var l in _alt) l.Clear(Cell.DefaultColor);
            CursorX = CursorY = 0;
            _top = 0;
            _bottom = Rows - 1;
            AppCursorKeys = AppKeypad = OriginMode = InsertMode = NewLineMode = BracketedPaste = false;
            AutoWrap = CursorVisible = true;
            MouseTracking = 0;
            MouseSgr = false;
            _g0Graphics = _g1Graphics = _shiftOut = false;
            _wrapPending = false;
            ResetTabs();
        }

        private void SoftReset()
        {
            _fg = _bg = Cell.DefaultColor;
            _attr = CellAttr.None;
            InsertMode = OriginMode = AppCursorKeys = false;
            AutoWrap = CursorVisible = true;
            _top = 0;
            _bottom = Rows - 1;
            _g0Graphics = _g1Graphics = _shiftOut = false;
        }

        private void Respond(string s) => Response?.Invoke(s);

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        /// <summary>선택 영역 텍스트. 좌표는 GetLine 인덱스 기준, 끝 칸 포함.</summary>
        public string GetText(int startLine, int startCol, int endLine, int endCol)
        {
            var sb = new StringBuilder();
            lock (Sync)
            {
                for (var li = startLine; li <= endLine; li++)
                {
                    var line = GetLine(li);
                    if (line == null)
                        break;
                    var from = li == startLine ? startCol : 0;
                    var to = li == endLine ? endCol : line.Cells.Length - 1;
                    var lineText = new StringBuilder();
                    for (var x = Math.Max(0, from); x <= to && x < line.Cells.Length; x++)
                    {
                        var c = line.Cells[x];
                        if (c.Width == 0)
                            continue;
                        lineText.Append(c.Ch == '\0' ? ' ' : c.Ch);
                        if (c.Low != '\0')
                            lineText.Append(c.Low);
                    }
                    var text = lineText.ToString();
                    var wrapped = line.Wrapped && li != endLine;
                    sb.Append(wrapped ? text : text.TrimEnd(' '));
                    if (li != endLine && !wrapped)
                        sb.Append("\r\n");
                }
            }
            return sb.ToString();
        }
    }
}
