using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Lish.Core;
using Lish.Ui;

namespace Lish.Terminal
{
    /// <summary>
    /// 터미널 화면 컨트롤. VtTerminal 을 GDI(ExtTextOut)로 격자에 맞춰 그리고,
    /// 키보드/마우스/한글 IME 입력을 VT 시퀀스로 변환해 Input 이벤트로 내보낸다.
    /// </summary>
    internal sealed class TerminalControl : Control
    {
        private const int Pad = 8;
        private const int ScrollBarWidth = 10;

        private readonly VtTerminal _term;
        private readonly TerminalTheme _theme = TerminalTheme.Default;
        private readonly System.Windows.Forms.Timer _timer;
        private AppSettings _settings;

        private IntPtr _hFont, _hBold, _hFallback, _hSymbol, _hEmoji;
        private int _cw = 8, _ch = 16, _ascent = 12;
        private int[] _dx = new int[0];
        private readonly Dictionary<char, byte> _glyphFont = new Dictionary<char, byte>();
        private readonly StringBuilder _run = new StringBuilder(256);

        private long _lastVersion = -1;
        private long _lastPushed;
        private int _scrollOffset;
        private bool _resizePending;
        private int _resizeAt;

        // 선택 영역 (전역 줄 번호 = Dropped + 인덱스)
        private bool _selecting;
        private bool _hasSel;
        private long _selStartLine, _selEndLine;
        private int _selStartCol, _selEndCol;
        // 선택 확정 시점의 줄 객체·내용 스냅숏. 출력으로 선택 영역 내용이 바뀌면 선택을 해제한다.
        private Line[] _selLines;
        private string[] _selTexts;

        // 스크롤바 드래그
        private bool _thumbDrag;
        private int _thumbDragOffset;
        private bool _hoverScroll;

        private string _composition = "";
        private int _mouseButtonDown = -1;

        public event Action<string> Input;
        public event Action<int, int> TerminalResized;
        public event Action<string> TitleChanged;

        public VtTerminal Terminal => _term;
        public int Columns => _term.Cols;
        public int RowsCount => _term.Rows;

        public TerminalControl(AppSettings settings)
        {
            _settings = settings;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.IBeam;
            BackColor = Color.FromArgb(_theme.Background | unchecked((int)0xFF000000));
            ImeMode = ImeMode.NoControl;

            _term = new VtTerminal(80, 24, settings.Scrollback);
            _term.Response += s => Input?.Invoke(s);
            _term.TitleChanged += t =>
            {
                if (IsHandleCreated)
                    BeginInvoke((Action)(() => TitleChanged?.Invoke(t)));
            };

            CreateFonts();
            ContextMenuStrip = BuildContextMenu();

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            lock (_term.Sync)
                _term.MaxScrollback = settings.Scrollback;
            CreateFonts();
            ScheduleResize(immediate: true);
            Invalidate();
        }

        /// <summary>원격 출력(디코딩된 문자)을 터미널에 넣는다. 아무 스레드에서나 호출 가능.</summary>
        public void Feed(char[] buffer, int count) => _term.Write(buffer, 0, count);

        public void Feed(string text) => _term.Write(text);

        // ───────────── 글꼴 ─────────────

        private static string ResolveFontName(string preferred)
        {
            foreach (var name in new[] { preferred, "D2Coding", "Cascadia Mono", "Consolas", "Courier New" })
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                var embedded = EmbeddedFonts.Find(name);
                if (embedded != null)
                    return embedded.Name;
                try
                {
                    using (var ff = new FontFamily(name))
                        return ff.Name;
                }
                catch (ArgumentException)
                {
                    // 설치되지 않은 글꼴
                }
            }
            return FontFamily.GenericMonospace.Name;
        }

        private void CreateFonts()
        {
            DeleteFonts();
            var size = _settings.FontSize < 6 ? 10.5f : _settings.FontSize;
            var name = ResolveFontName(_settings.FontName);
            _hFont = MakeFont(name, size, FontStyle.Regular);
            _hBold = MakeFont(name, size, FontStyle.Bold);
            _hFallback = MakeFont("Malgun Gothic", size, FontStyle.Regular);
            _hSymbol = MakeFont("Segoe UI Symbol", size, FontStyle.Regular);
            _hEmoji = MakeFont("Segoe UI Emoji", size, FontStyle.Regular);
            _glyphFont.Clear();

            var hdc = Gdi.GetDC(IntPtr.Zero);
            try
            {
                var old = Gdi.SelectObject(hdc, _hFont);
                Gdi.GetTextMetrics(hdc, out var tm);
                Gdi.GetTextExtentPoint32(hdc, "M", 1, out var sz);
                Gdi.SelectObject(hdc, old);
                _cw = Math.Max(4, sz.cx);
                // 행 높이 = 글꼴 높이(ascent+descent)에서 여분 1px 을 뺀 값. 줄 간격이 큰 글꼴(Cascadia 등)은
                // em 의 1.3배로 제한하고 기준선을 가운데로 맞춘다. em 보다는 작아지지 않게 해서 한글(맑은 고딕
                // 폴백 포함)·선문자 위아래가 잘리지 않게 한다. (D2Coding 21px: em 21 / 글꼴 24 / 한글 -3~17)
                var emPx = tm.tmHeight - tm.tmInternalLeading;
                var maxH = (int)Math.Ceiling(emPx * 1.3);
                _ch = Math.Max(8, Math.Max(emPx + 1, Math.Min(tm.tmHeight, maxH)));
                _ascent = tm.tmAscent - Math.Max(0, (tm.tmHeight - _ch) / 2);
            }
            finally
            {
                Gdi.ReleaseDC(IntPtr.Zero, hdc);
            }
            _dx = new int[0];
        }

        private static IntPtr MakeFont(string name, float size, FontStyle style)
        {
            try
            {
                using (var f = EmbeddedFonts.Create(name, size, style))
                    return f.ToHfont();
            }
            catch (ArgumentException)
            {
                using (var f = new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point))
                    return f.ToHfont();
            }
        }

        private void DeleteFonts()
        {
            foreach (var h in new[] { _hFont, _hBold, _hFallback, _hSymbol, _hEmoji })
                if (h != IntPtr.Zero)
                    Gdi.DeleteObject(h);
            _hFont = _hBold = _hFallback = _hSymbol = _hEmoji = IntPtr.Zero;
        }

        /// <summary>0 = 기본 글꼴, 1 = 맑은 고딕(한글/CJK), 2 = Segoe UI Symbol</summary>
        private byte FontFor(IntPtr hdc, char c)
        {
            if (c < 0x80)
                return 0;
            if (_glyphFont.TryGetValue(c, out var f))
                return f;
            f = HasGlyph(hdc, _hFont, c) ? (byte)0 : HasGlyph(hdc, _hFallback, c) ? (byte)1 : (byte)2;
            _glyphFont[c] = f;
            return f;
        }

        private static bool HasGlyph(IntPtr hdc, IntPtr font, char c)
        {
            var old = Gdi.SelectObject(hdc, font);
            var idx = new ushort[1];
            Gdi.GetGlyphIndices(hdc, c.ToString(), 1, idx, Gdi.GGI_MARK_NONEXISTING_GLYPHS);
            Gdi.SelectObject(hdc, old);
            return idx[0] != 0xFFFF;
        }

        // ───────────── 크기 ─────────────

        private int GridWidth => Math.Max(1, ClientSize.Width - Pad * 2 - ScrollBarWidth);
        private int GridHeight => Math.Max(1, ClientSize.Height - Pad * 2);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ScheduleResize(immediate: false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ScheduleResize(immediate: true);
        }

        private void ScheduleResize(bool immediate)
        {
            _resizePending = true;
            _resizeAt = Environment.TickCount - (immediate ? 1000 : 0);
        }

        private void ApplyResize()
        {
            _resizePending = false;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            var cols = Math.Max(10, GridWidth / _cw);
            var rows = Math.Max(3, GridHeight / _ch);
            bool changed;
            lock (_term.Sync)
            {
                changed = cols != _term.Cols || rows != _term.Rows;
                if (changed)
                    _term.Resize(cols, rows);
                _scrollOffset = Math.Min(_scrollOffset, _term.Scrollback.Count);
            }
            if (changed)
                TerminalResized?.Invoke(cols, rows);
            Invalidate();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_resizePending && unchecked(Environment.TickCount - _resizeAt) > 60)
                ApplyResize();

            var v = Interlocked.Read(ref _term.Version);
            if (v == _lastVersion)
                return;
            _lastVersion = v;
            lock (_term.Sync)
            {
                var pushed = _term.Pushed;
                var pushedDelta = pushed - _lastPushed;
                if (_scrollOffset > 0)
                    _scrollOffset = Math.Min(_scrollOffset + (int)pushedDelta, _term.Scrollback.Count);
                _lastPushed = pushed;

                // 선택한 부분의 내용이 바뀌었으면(clear, 덮어쓰기, 전체화면 앱 스크롤 등) 선택을 해제한다.
                if (_hasSel && !_selecting && SelectionContentChanged(pushedDelta))
                {
                    _hasSel = false;
                    _selLines = null;
                    _selTexts = null;
                }
            }
            Invalidate();
        }

        // ───────────── 그리기 ─────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var hdc = e.Graphics.GetHdc();
            try
            {
                Gdi.Fill(hdc, 0, 0, ClientSize.Width, ClientSize.Height, _theme.Background);
                Gdi.SetBkMode(hdc, Gdi.OPAQUE);
                Gdi.SetTextAlign(hdc, Gdi.TA_BASELINE);
                lock (_term.Sync)
                {
                    if (_dx.Length < _term.Cols + 2)
                    {
                        _dx = new int[_term.Cols + 2];
                        for (var i = 0; i < _dx.Length; i++)
                            _dx[i] = _cw;
                    }
                    var first = _term.Scrollback.Count - _scrollOffset;
                    for (var r = 0; r < _term.Rows; r++)
                    {
                        var line = _term.GetLine(first + r);
                        if (line != null)
                            DrawLine(hdc, line, first + r, r);
                    }
                    DrawCursor(hdc);
                    DrawComposition(hdc);
                    DrawScrollBar(hdc);
                }
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
        }

        private void DrawLine(IntPtr hdc, Line line, int index, int row)
        {
            var cells = line.Cells;
            var cols = _term.Cols;
            var blank = Cell.Blank(Cell.DefaultColor);
            var x = 0;
            while (x < cols)
            {
                var c = x < cells.Length ? cells[x] : blank;
                var orphan = c.Width == 0;
                var w = orphan ? 1 : c.Width;
                var ch = orphan || c.Ch == '\0' ? ' ' : c.Ch;
                var sel = InSelection(index, x);
                ResolveColors(c, sel, out var fg, out var bg);
                var font = c.Low != '\0' ? (byte)3 : FontFor(hdc, ch);

                if (w == 2 || font != 0)
                {
                    DrawCell(hdc, ch, c.Low, x, Math.Min(w, cols - x), row, fg, bg, font, c.Attr);
                    x += w;
                    continue;
                }

                var start = x;
                _run.Clear();
                _run.Append(ch);
                x++;
                while (x < cols)
                {
                    var n = x < cells.Length ? cells[x] : blank;
                    if (n.Width != 1 || n.Low != '\0' || !n.SameStyle(c) || InSelection(index, x) != sel)
                        break;
                    var nc = n.Ch == '\0' ? ' ' : n.Ch;
                    if (FontFor(hdc, nc) != 0)
                        break;
                    _run.Append(nc);
                    x++;
                }
                DrawText(hdc, _run.ToString(), start, x - start, row, fg, bg, (c.Attr & CellAttr.Bold) != 0 ? _hBold : _hFont, c.Attr);
            }
        }

        private void DrawCell(IntPtr hdc, char ch, char low, int x, int w, int row, int fg, int bg, byte font, CellAttr attr)
        {
            IntPtr hf;
            switch (font)
            {
                case 1: hf = _hFallback; break;
                case 2: hf = _hSymbol; break;
                case 3: hf = _hEmoji; break;
                default: hf = (attr & CellAttr.Bold) != 0 ? _hBold : _hFont; break;
            }
            var text = low != '\0' ? new string(new[] { ch, low }) : ch.ToString();
            DrawText(hdc, text, x, w, row, fg, bg, hf, attr);
        }

        private void DrawText(IntPtr hdc, string text, int col, int cellCount, int row, int fg, int bg, IntPtr font, CellAttr attr)
        {
            var left = Pad + col * _cw;
            var top = Pad + row * _ch;
            var rc = new Gdi.RECT(left, top, left + cellCount * _cw, top + _ch);
            var old = Gdi.SelectObject(hdc, font);
            Gdi.SetTextColor(hdc, Gdi.ToColorRef(fg));
            Gdi.SetBkColor(hdc, Gdi.ToColorRef(bg));

            int[] dx;
            if (text.Length == cellCount)
            {
                dx = _dx;
            }
            else
            {
                // 전각 문자/서로게이트 쌍: 첫 글자에 칸 전체 폭을 준다.
                dx = new int[text.Length];
                dx[0] = cellCount * _cw;
            }
            Gdi.ExtTextOut(hdc, left, top + _ascent, Gdi.ETO_OPAQUE | Gdi.ETO_CLIPPED, ref rc, text, text.Length, dx);
            Gdi.SelectObject(hdc, old);

            if ((attr & CellAttr.Underline) != 0)
                Gdi.Fill(hdc, rc.Left, rc.Bottom - 2, rc.Right, rc.Bottom - 1, fg);
            if ((attr & CellAttr.Strike) != 0)
                Gdi.Fill(hdc, rc.Left, top + _ch / 2, rc.Right, top + _ch / 2 + 1, fg);
        }

        private void ResolveColors(in Cell c, bool selected, out int fg, out int bg)
        {
            var fgIndex = c.Fg;
            if ((c.Attr & CellAttr.Bold) != 0 && fgIndex >= 0 && fgIndex < 8)
                fgIndex += 8;
            fg = _theme.Resolve(fgIndex, _theme.Foreground);
            bg = _theme.Resolve(c.Bg, _theme.Background);
            if ((c.Attr & CellAttr.Inverse) != 0)
            {
                var t = fg;
                fg = bg;
                bg = t;
            }
            if ((c.Attr & CellAttr.Dim) != 0)
                fg = TerminalTheme.Blend(fg, bg, 0.45f);
            if ((c.Attr & CellAttr.Hidden) != 0)
                fg = bg;
            if (selected)
            {
                fg = _theme.SelectionFg;
                bg = _theme.SelectionBg;
            }
        }

        private void DrawCursor(IntPtr hdc)
        {
            if (_scrollOffset != 0 || !_term.CursorVisible || _composition.Length > 0)
                return;
            var line = _term.GetLine(_term.Scrollback.Count + _term.CursorY);
            if (line == null)
                return;
            var x = Math.Min(_term.CursorX, _term.Cols - 1);
            var c = x < line.Cells.Length ? line.Cells[x] : Cell.Blank(Cell.DefaultColor);
            if (c.Width == 0 && x > 0)
            {
                x--;
                c = line.Cells[x];
            }
            var w = c.Width == 2 ? 2 : 1;
            var left = Pad + x * _cw;
            var top = Pad + _term.CursorY * _ch;

            if (Focused)
            {
                var ch = c.Ch == '\0' ? ' ' : c.Ch;
                var font = c.Low != '\0' ? (byte)3 : FontFor(hdc, ch);
                DrawCell(hdc, ch, c.Low, x, w, _term.CursorY, _theme.CursorText, _theme.Cursor, font, c.Attr & ~CellAttr.Underline);
            }
            else
            {
                var right = left + w * _cw;
                var bottom = top + _ch;
                Gdi.Fill(hdc, left, top, right, top + 1, _theme.Cursor);
                Gdi.Fill(hdc, left, bottom - 1, right, bottom, _theme.Cursor);
                Gdi.Fill(hdc, left, top, left + 1, bottom, _theme.Cursor);
                Gdi.Fill(hdc, right - 1, top, right, bottom, _theme.Cursor);
            }
        }

        private void DrawComposition(IntPtr hdc)
        {
            if (_composition.Length == 0)
                return;
            var x = _term.CursorX;
            foreach (var ch in _composition)
            {
                var w = CharWidth.Of(ch);
                if (w <= 0)
                    continue;
                if (x + w > _term.Cols)
                    break;
                DrawCell(hdc, ch, '\0', x, w, _term.CursorY, 0xFFFFFF, _theme.CompositionBg, FontFor(hdc, ch), CellAttr.Underline);
                x += w;
            }
        }

        // ───────────── 스크롤바 (오버레이) ─────────────

        private Rectangle TrackRect => new Rectangle(ClientSize.Width - ScrollBarWidth - 2, Pad, ScrollBarWidth, Math.Max(1, ClientSize.Height - Pad * 2));

        private bool ThumbRect(out Rectangle thumb)
        {
            thumb = Rectangle.Empty;
            var total = _term.Scrollback.Count + _term.Rows;
            if (_term.Scrollback.Count == 0)
                return false;
            var track = TrackRect;
            var h = Math.Max(24, track.Height * _term.Rows / total);
            var maxTop = track.Height - h;
            var pos = _term.Scrollback.Count - _scrollOffset; // 0 = 맨 위
            var top = track.Top + (int)((long)maxTop * pos / Math.Max(1, _term.Scrollback.Count));
            thumb = new Rectangle(track.Left + 2, top, track.Width - 4, h);
            return true;
        }

        private void DrawScrollBar(IntPtr hdc)
        {
            if (!ThumbRect(out var thumb))
                return;
            if (!_hoverScroll && !_thumbDrag && _scrollOffset == 0)
                return;
            var color = _thumbDrag ? 0xA78BFA : _hoverScroll ? 0x7E63E6 : 0x4E4470;
            Gdi.Fill(hdc, thumb.Left, thumb.Top + 1, thumb.Right, thumb.Bottom - 1, color);
            Gdi.Fill(hdc, thumb.Left + 1, thumb.Top, thumb.Right - 1, thumb.Bottom, color);
        }

        private void ScrollTo(int offset)
        {
            lock (_term.Sync)
                _scrollOffset = Math.Max(0, Math.Min(offset, _term.Scrollback.Count));
            Invalidate();
        }

        public void ScrollLines(int delta) => ScrollTo(_scrollOffset + delta);

        // ───────────── 선택 ─────────────

        private bool InSelection(int index, int x)
        {
            if (!_hasSel)
                return false;
            GetOrderedSelection(out var sl, out var sc, out var el, out var ec);
            var g = index + _term.Dropped;
            if (g < sl || g > el)
                return false;
            if (g == sl && x < sc)
                return false;
            if (g == el && x > ec)
                return false;
            return true;
        }

        private void GetOrderedSelection(out long sl, out int sc, out long el, out int ec)
        {
            if (_selStartLine < _selEndLine || (_selStartLine == _selEndLine && _selStartCol <= _selEndCol))
            {
                sl = _selStartLine; sc = _selStartCol; el = _selEndLine; ec = _selEndCol;
            }
            else
            {
                sl = _selEndLine; sc = _selEndCol; el = _selStartLine; ec = _selStartCol;
            }
        }

        /// <summary>줄의 [from, to] 칸 문자열 (선택 내용 비교용). 줄 끝까지면 뒤 공백은 무시(창 크기 변경 대비).</summary>
        private static string LineSegment(Line line, int from, int to)
        {
            var cells = line.Cells;
            var toEnd = to >= cells.Length - 1;
            to = Math.Min(to, cells.Length - 1);
            var sb = new StringBuilder(Math.Max(0, to - from + 1));
            for (var x = Math.Max(0, from); x <= to; x++)
            {
                var c = cells[x];
                if (c.Width == 0)
                    continue;
                sb.Append(c.Ch == '\0' ? ' ' : c.Ch);
                if (c.Low != '\0')
                    sb.Append(c.Low);
            }
            return toEnd ? sb.ToString().TrimEnd(' ') : sb.ToString();
        }

        /// <summary>선택이 확정될 때 선택된 줄 객체와 내용을 기억해 둔다.</summary>
        private void SnapshotSelection()
        {
            _selLines = null;
            _selTexts = null;
            if (!_hasSel)
                return;
            lock (_term.Sync)
            {
                GetOrderedSelection(out var sl, out var sc, out var el, out var ec);
                var n = (int)(el - sl + 1);
                var lines = new Line[n];
                var texts = new string[n];
                for (var i = 0; i < n; i++)
                {
                    var idx = sl + i - _term.Dropped;
                    var line = idx < 0 ? null : _term.GetLine((int)idx);
                    lines[i] = line;
                    if (line != null)
                        texts[i] = LineSegment(line, i == 0 ? sc : 0, i == n - 1 ? ec : int.MaxValue);
                }
                _selLines = lines;
                _selTexts = texts;
            }
        }

        /// <summary>
        /// 선택 영역 내용이 스냅숏과 다른지 검사한다 (Sync 잠금 하에서 호출).
        /// 줄 객체가 바뀌었으면(밀려남·지워진 스크롤백·전체화면 스크롤) 바로 변경으로 보고,
        /// 같은 객체라도 화면(및 이번에 스크롤백으로 막 넘어간) 줄은 제자리 수정될 수 있으니 내용까지 비교한다.
        /// </summary>
        private bool SelectionContentChanged(long pushedDelta)
        {
            if (_selLines == null)
                return false;
            GetOrderedSelection(out var sl, out var sc, out var el, out var ec);
            var n = (int)(el - sl + 1);
            if (n != _selLines.Length)
                return true;
            var mutableFrom = _term.Scrollback.Count - (int)Math.Min(_term.Scrollback.Count, Math.Max(0, pushedDelta));
            for (var i = 0; i < n; i++)
            {
                var idx = sl + i - _term.Dropped;
                var line = idx < 0 ? null : _term.GetLine((int)idx);
                if (!ReferenceEquals(line, _selLines[i]))
                    return true;
                if (line == null || idx < mutableFrom)
                    continue;
                if (LineSegment(line, i == 0 ? sc : 0, i == n - 1 ? ec : int.MaxValue) != _selTexts[i])
                    return true;
            }
            return false;
        }

        private void PointToCell(Point p, out long globalLine, out int col)
        {
            col = Math.Max(0, Math.Min(_term.Cols - 1, (p.X - Pad) / _cw));
            var row = (int)Math.Floor((p.Y - Pad) / (double)_ch);
            lock (_term.Sync)
            {
                var index = _term.Scrollback.Count - _scrollOffset + row;
                index = Math.Max(0, Math.Min(_term.TotalLines - 1, index));
                globalLine = _term.Dropped + index;
            }
        }

        public bool HasSelection => _hasSel;

        public string SelectedText
        {
            get
            {
                if (!_hasSel)
                    return "";
                GetOrderedSelection(out var sl, out var sc, out var el, out var ec);
                var d = _term.Dropped;
                return _term.GetText((int)Math.Max(0, sl - d), sc, (int)Math.Max(0, el - d), ec);
            }
        }

        public void Copy()
        {
            var text = SelectedText;
            if (text.Length > 0)
            {
                try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { /* 클립보드 사용 중 */ }
            }
        }

        public void Paste()
        {
            string text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return;
            }
            if (string.IsNullOrEmpty(text))
                return;
            text = text.Replace("\r\n", "\r").Replace('\n', '\r');
            var lines = text.TrimEnd('\r').Split('\r').Length;
            if (lines > 1 && !_term.BracketedPaste && _settings.ConfirmMultilinePaste)
            {
                if (MessageBox.Show(FindForm(), $"{lines}줄을 붙여넣습니다. 각 줄이 명령으로 실행될 수 있습니다.\n계속할까요?",
                        "여러 줄 붙여넣기", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            if (_term.BracketedPaste)
                text = "\x1b[200~" + text.Replace("\x1b", "") + "\x1b[201~";
            SendText(text);
        }

        public void SelectAll()
        {
            lock (_term.Sync)
            {
                _selStartLine = _term.Dropped;
                _selStartCol = 0;
                _selEndLine = _term.Dropped + _term.TotalLines - 1;
                _selEndCol = _term.Cols - 1;
                _hasSel = true;
            }
            SnapshotSelection();
            Invalidate();
        }

        public void ClearSelection()
        {
            _hasSel = false;
            _selLines = null;
            _selTexts = null;
            Invalidate();
        }

        private void SelectWord(long globalLine, int col)
        {
            Line line;
            lock (_term.Sync)
                line = _term.GetLine((int)(globalLine - _term.Dropped));
            if (line == null)
                return;
            bool IsWord(int x) => x >= 0 && x < line.Cells.Length && (line.Cells[x].Width == 0 || " \t\"'`()[]{}<>|;,".IndexOf(line.Cells[x].Ch) < 0) && line.Cells[x].Ch != '\0';
            if (!IsWord(col))
                return;
            var s = col;
            var e = col;
            while (IsWord(s - 1)) s--;
            while (IsWord(e + 1)) e++;
            _selStartLine = _selEndLine = globalLine;
            _selStartCol = s;
            _selEndCol = e;
            _hasSel = true;
            SnapshotSelection();
            Invalidate();
        }

        private ContextMenuStrip BuildContextMenu()
        {
            // 다른 메뉴(MainForm/SftpPanel)와 같은 플랫 렌더러·글꼴
            var menu = new ContextMenuStrip { Renderer = new FlatMenuRenderer(), Font = Theme.UiFont };
            var copy = new ToolStripMenuItem("복사", null, (s, e) => Copy()) { ShortcutKeyDisplayString = "Ctrl+Shift+C" };
            var paste = new ToolStripMenuItem("붙여넣기", null, (s, e) => Paste()) { ShortcutKeyDisplayString = "Ctrl+Shift+V" };
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("모두 선택", null, (s, e) => SelectAll());
            menu.Items.Add("스크롤백 지우기", null, (s, e) => { _term.ClearScrollback(); ScrollTo(0); });
            menu.Opening += (s, e) =>
            {
                copy.Enabled = _hasSel;
                paste.Enabled = Clipboard.ContainsText();
            };
            return menu;
        }

        // ───────────── 입력 ─────────────

        private void SendText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return;
            if (_scrollOffset != 0)
                ScrollTo(0);
            Input?.Invoke(s);
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Shift | Keys.C:
                    Copy();
                    return true;
                case Keys.Control | Keys.Shift | Keys.V:
                case Keys.Shift | Keys.Insert:
                    Paste();
                    return true;
                case Keys.Shift | Keys.PageUp:
                    ScrollLines(_term.Rows - 1);
                    return true;
                case Keys.Shift | Keys.PageDown:
                    ScrollLines(-(_term.Rows - 1));
                    return true;
                case Keys.Control | Keys.Shift | Keys.Home:
                    ScrollTo(int.MaxValue);
                    return true;
                case Keys.Control | Keys.Shift | Keys.End:
                    ScrollTo(0);
                    return true;
            }

            // 폼 단축키(탭 전환 등)가 먼저 처리할 기회를 갖는다.
            if (base.ProcessCmdKey(ref msg, keyData))
                return true;

            if (keyData == (Keys.Alt | Keys.F4))
                return false;

            var seq = MapKey(keyData);
            if (seq == null)
                return false;
            SendText(seq);
            return true;
        }

        private string MapKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var shift = (keyData & Keys.Shift) != 0;
            var ctrl = (keyData & Keys.Control) != 0;
            var alt = (keyData & Keys.Alt) != 0;
            var mod = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

            string Cursor(char c) =>
                mod == 1 ? (_term.AppCursorKeys ? "\x1bO" : "\x1b[") + c : $"\x1b[1;{mod}{c}";
            string Tilde(int n) => mod == 1 ? $"\x1b[{n}~" : $"\x1b[{n};{mod}~";
            string Ss3(char c) => mod == 1 ? "\x1bO" + c : $"\x1b[1;{mod}{c}";

            switch (key)
            {
                case Keys.Up: return Cursor('A');
                case Keys.Down: return Cursor('B');
                case Keys.Right: return Cursor('C');
                case Keys.Left: return Cursor('D');
                case Keys.Home: return Cursor('H');
                case Keys.End: return Cursor('F');
                case Keys.Insert: return Tilde(2);
                case Keys.Delete: return Tilde(3);
                case Keys.PageUp: return Tilde(5);
                case Keys.PageDown: return Tilde(6);
                case Keys.F1: return Ss3('P');
                case Keys.F2: return Ss3('Q');
                case Keys.F3: return Ss3('R');
                case Keys.F4: return Ss3('S');
                case Keys.F5: return Tilde(15);
                case Keys.F6: return Tilde(17);
                case Keys.F7: return Tilde(18);
                case Keys.F8: return Tilde(19);
                case Keys.F9: return Tilde(20);
                case Keys.F10: return Tilde(21);
                case Keys.F11: return Tilde(23);
                case Keys.F12: return Tilde(24);
                case Keys.Tab: return shift ? "\x1b[Z" : "\t";
                case Keys.Space: return ctrl ? "\0" : null;
                case Keys.D2: return ctrl && shift ? "\0" : null;
                case Keys.Enter: return alt ? "\x1b\r" : null;
                case Keys.Back:
                    if (alt) return "\x1b\x7f";
                    return ctrl ? "\b" : "\x7f";
                case Keys.Escape: return "\x1b";
            }
            return null;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Backspace/Esc/방향키 등은 ProcessCmdKey(MapKey)에서 이미 처리되어 여기 오지 않는다.
            e.Handled = true;
            SendText(e.KeyChar.ToString());
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Gdi.WM_SYSCHAR:
                    // Alt+문자 → ESC 접두 (bash/emacs 메타 키)
                    var ch = (char)m.WParam.ToInt64();
                    if (ch != ' ')
                    {
                        SendText("\x1b" + ch);
                        return;
                    }
                    break;

                case Gdi.WM_IME_SETCONTEXT:
                    // 기본 조합 창은 숨기고 커서 위치에 직접 그린다.
                    m.LParam = new IntPtr(m.LParam.ToInt64() & ~Gdi.ISC_SHOWUICOMPOSITIONWINDOW);
                    break;

                case Gdi.WM_IME_STARTCOMPOSITION:
                    UpdateImePosition();
                    return;

                case Gdi.WM_IME_COMPOSITION:
                    HandleImeComposition(m.LParam.ToInt64());
                    return;

                case Gdi.WM_IME_ENDCOMPOSITION:
                    _composition = "";
                    Invalidate();
                    return;

                case Gdi.WM_IME_CHAR:
                    return; // 결과 문자열은 WM_IME_COMPOSITION 에서 이미 보냄
            }
            base.WndProc(ref m);
        }

        private void HandleImeComposition(long flags)
        {
            var himc = Gdi.ImmGetContext(Handle);
            if (himc == IntPtr.Zero)
                return;
            try
            {
                if ((flags & Gdi.GCS_RESULTSTR) != 0)
                    SendText(GetImeString(himc, Gdi.GCS_RESULTSTR));
                _composition = (flags & Gdi.GCS_COMPSTR) != 0 ? GetImeString(himc, Gdi.GCS_COMPSTR) : "";
            }
            finally
            {
                Gdi.ImmReleaseContext(Handle, himc);
            }
            UpdateImePosition();
            Invalidate();
        }

        private static string GetImeString(IntPtr himc, int index)
        {
            var len = Gdi.ImmGetCompositionString(himc, index, null, 0);
            if (len <= 0)
                return "";
            var buf = new byte[len];
            Gdi.ImmGetCompositionString(himc, index, buf, len);
            return Encoding.Unicode.GetString(buf);
        }

        private void UpdateImePosition()
        {
            var himc = Gdi.ImmGetContext(Handle);
            if (himc == IntPtr.Zero)
                return;
            try
            {
                var pt = new Gdi.POINT { x = Pad + _term.CursorX * _cw, y = Pad + _term.CursorY * _ch };
                var cf = new Gdi.COMPOSITIONFORM { dwStyle = Gdi.CFS_POINT, ptCurrentPos = pt };
                Gdi.ImmSetCompositionWindow(himc, ref cf);
                var cand = new Gdi.CANDIDATEFORM { dwIndex = 0, dwStyle = Gdi.CFS_CANDIDATEPOS, ptCurrentPos = new Gdi.POINT { x = pt.x, y = pt.y + _ch } };
                Gdi.ImmSetCandidateWindow(himc, ref cand);
            }
            finally
            {
                Gdi.ImmReleaseContext(Handle, himc);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        // ───────────── 마우스 ─────────────

        private bool MouseReporting => _term.MouseTracking != 0 && (ModifierKeys & Keys.Shift) == 0;

        private void SendMouse(int button, bool press, Point p, bool motion = false)
        {
            var col = Math.Max(0, Math.Min(_term.Cols - 1, (p.X - Pad) / _cw)) + 1;
            var row = Math.Max(0, Math.Min(_term.Rows - 1, (p.Y - Pad) / _ch)) + 1;
            var b = button;
            if (motion) b += 32;
            var mods = ModifierKeys;
            if ((mods & Keys.Control) != 0) b += 16;
            if ((mods & Keys.Alt) != 0) b += 8;

            if (_term.MouseSgr)
            {
                SendText($"\x1b[<{b};{col};{row}{(press ? 'M' : 'm')}");
            }
            else
            {
                if (!press) b = 3 + (b & ~3);
                if (col > 223 || row > 223)
                    return;
                SendText("\x1b[M" + (char)(32 + b) + (char)(32 + col) + (char)(32 + row));
            }
        }

        private static int ButtonCode(MouseButtons b) =>
            b == MouseButtons.Left ? 0 : b == MouseButtons.Middle ? 1 : b == MouseButtons.Right ? 2 : -1;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && TrackRect.Contains(e.Location) && ThumbRect(out var thumb))
            {
                if (thumb.Contains(e.Location))
                {
                    _thumbDrag = true;
                    _thumbDragOffset = e.Y - thumb.Top;
                }
                else
                {
                    ScrollLines(e.Y < thumb.Top ? _term.Rows : -_term.Rows);
                }
                return;
            }

            if (MouseReporting)
            {
                var code = ButtonCode(e.Button);
                if (code >= 0)
                {
                    _mouseButtonDown = code;
                    SendMouse(code, true, e.Location);
                }
                return;
            }

            switch (e.Button)
            {
                case MouseButtons.Left:
                    PointToCell(e.Location, out var line, out var col);
                    if (e.Clicks == 2)
                    {
                        SelectWord(line, col);
                        if (_hasSel && _settings.CopyOnSelect)
                            Copy();
                        return;
                    }
                    _selStartLine = _selEndLine = line;
                    _selStartCol = _selEndCol = col;
                    _selecting = true;
                    if (_hasSel)
                    {
                        _hasSel = false;
                        Invalidate();
                    }
                    break;
                case MouseButtons.Middle:
                    Paste();
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var hover = e.X >= TrackRect.Left - 4;
            if (hover != _hoverScroll)
            {
                _hoverScroll = hover;
                Cursor = hover ? Cursors.Default : Cursors.IBeam;
                Invalidate();
            }

            if (_thumbDrag)
            {
                var track = TrackRect;
                if (ThumbRect(out var thumb))
                {
                    var maxTop = track.Height - thumb.Height;
                    var top = Math.Max(0, Math.Min(maxTop, e.Y - _thumbDragOffset - track.Top));
                    var pos = maxTop == 0 ? 0 : (int)((long)top * _term.Scrollback.Count / maxTop);
                    ScrollTo(_term.Scrollback.Count - pos);
                }
                return;
            }

            if (_term.MouseTracking != 0 && !_selecting)
            {
                if ((_term.MouseTracking == 1002 && _mouseButtonDown >= 0) || _term.MouseTracking == 1003)
                    SendMouse(_mouseButtonDown >= 0 ? _mouseButtonDown : 3, true, e.Location, motion: true);
                return;
            }

            if (_selecting && e.Button == MouseButtons.Left)
            {
                if (e.Y < 0)
                    ScrollLines(1);
                else if (e.Y > ClientSize.Height)
                    ScrollLines(-1);
                PointToCell(e.Location, out var line, out var col);
                _selEndLine = line;
                _selEndCol = col;
                _hasSel = _selEndLine != _selStartLine || _selEndCol != _selStartCol;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_thumbDrag)
            {
                _thumbDrag = false;
                Invalidate();
                return;
            }
            if (_mouseButtonDown >= 0)
            {
                SendMouse(_mouseButtonDown, false, e.Location);
                _mouseButtonDown = -1;
                return;
            }
            if (_selecting)
            {
                _selecting = false;
                SnapshotSelection();
                if (_hasSel && _settings.CopyOnSelect)
                    Copy();
            }
            if (e.Button == MouseButtons.Right && _settings.RightClickPaste && !_hasSel)
                Paste();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverScroll)
            {
                _hoverScroll = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var notches = e.Delta / 120;
            if (notches == 0)
                notches = Math.Sign(e.Delta);

            if (MouseReporting)
            {
                for (var i = 0; i < Math.Abs(notches); i++)
                    SendMouse(notches > 0 ? 64 : 65, true, e.Location);
                return;
            }
            if (_term.AltScreen)
            {
                // less/vim 등 전체화면 앱: 휠을 방향키로 보낸다.
                var seq = (_term.AppCursorKeys ? "\x1bO" : "\x1b[") + (notches > 0 ? "A" : "B");
                var sb = new StringBuilder();
                for (var i = 0; i < Math.Abs(notches) * 3; i++)
                    sb.Append(seq);
                SendText(sb.ToString());
                return;
            }
            ScrollLines(notches * 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                ContextMenuStrip?.Dispose();
            }
            DeleteFonts();
            base.Dispose(disposing);
        }
    }
}
