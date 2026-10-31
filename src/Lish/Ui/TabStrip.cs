using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lish.Ui
{
    /// <summary>세션 탭 바. 탭마다 연결 상태 점 + 제목 + 닫기 버튼.
    /// 가로(위쪽 띠) / 세로(왼쪽 열) 두 가지 배치를 같은 로직으로 그린다.</summary>
    internal sealed class TabStrip : Control
    {
        private readonly List<SessionView> _tabs = new List<SessionView>();
        private readonly List<Rectangle> _tabRects = new List<Rectangle>();
        private int _hover = -1;
        private bool _hoverClose;
        private Rectangle _addRect;
        private Rectangle _settingsRect, _infoRect;
        private bool _hoverAdd, _hoverSettings, _hoverInfo;
        private readonly ToolTip _tip = new ToolTip();

        // 세로 모드: 위쪽 머리글 아래가 탭 목록 영역, 넘치면 휠로 스크롤
        private bool _vertical;
        private Rectangle _listRect;
        private int _scroll, _maxScroll, _contentHeight;
        private bool _ensureSelectedVisible;

        public SessionView Selected { get; private set; }

        public event EventHandler SelectedChanged;
        public event Action<SessionView> CloseRequested;
        public event EventHandler AddRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler InfoRequested;
        /// <summary>오른쪽 클릭. 탭 밖 빈 곳이면 SessionView 는 null.</summary>
        public event Action<SessionView, Point> ContextRequested;

        public IReadOnlyList<SessionView> Tabs => _tabs;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Theme.Scale(40);
            BackColor = Theme.Window;
            Font = Theme.UiFont;
        }

        /// <summary>true 면 터미널 왼쪽에 세로 열로 배치 (Dock 과 크기도 같이 바뀜)</summary>
        public bool Vertical
        {
            get => _vertical;
            set
            {
                if (_vertical == value) return;
                _vertical = value;
                _scroll = 0;
                _hover = -1;
                _hoverClose = _hoverAdd = _hoverSettings = _hoverInfo = false;
                _ensureSelectedVisible = true;
                if (value)
                {
                    Dock = DockStyle.Left;
                    Width = Theme.Scale(200);
                }
                else
                {
                    Dock = DockStyle.Top;
                    Height = Theme.Scale(40);
                }
                Invalidate();
            }
        }

        public void Add(SessionView view)
        {
            _tabs.Add(view);
            view.StateChanged += (s, e) => Invalidate();
            Select(view);
        }

        public void Remove(SessionView view)
        {
            var idx = _tabs.IndexOf(view);
            if (idx < 0) return;
            _tabs.RemoveAt(idx);
            if (Selected == view)
                Select(_tabs.Count == 0 ? null : _tabs[Math.Min(idx, _tabs.Count - 1)]);
            Invalidate();
        }

        public void Select(SessionView view)
        {
            Selected = view;
            _ensureSelectedVisible = true;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectNext(int delta)
        {
            if (_tabs.Count == 0) return;
            var i = Selected == null ? 0 : _tabs.IndexOf(Selected);
            Select(_tabs[((i + delta) % _tabs.Count + _tabs.Count) % _tabs.Count]);
        }

        private static string TitleOf(SessionView v) => v.Profile.Title;

        private int RowHeight => Theme.Scale(36);
        private int RowGap => Theme.Scale(2);
        private int HeaderHeight => Theme.Scale(44);

        private void LayoutTabs(Graphics g)
        {
            if (_vertical)
            {
                LayoutVertical();
                return;
            }
            _tabRects.Clear();
            _listRect = ClientRectangle;
            var x = Theme.Scale(8);
            var top = Theme.Scale(6);
            var h = Height - top;
            var right = Width - Theme.Scale(92);
            var maxW = Theme.Scale(220);
            var minW = Theme.Scale(110);
            var avail = Math.Max(minW, (right - x - Theme.Scale(36)) / Math.Max(1, _tabs.Count) - Theme.Scale(4));
            foreach (var t in _tabs)
            {
                var textW = TextRenderer.MeasureText(g, TitleOf(t), Theme.UiFont).Width;
                var w = Math.Min(Math.Min(maxW, avail), Math.Max(minW, textW + Theme.Scale(60)));
                _tabRects.Add(new Rectangle(x, top, w, h));
                x += w + Theme.Scale(4);
            }
            _addRect = new Rectangle(x + Theme.Scale(2), top + Theme.Scale(4), Theme.Scale(28), h - Theme.Scale(8));
            // 오른쪽 끝: [정보] [설정] (SFTP 는 사이드바 SFTP 탭)
            _settingsRect = new Rectangle(Width - Theme.Scale(40), Theme.Scale(6), Theme.Scale(32), Height - Theme.Scale(12));
            _infoRect = new Rectangle(_settingsRect.X - Theme.Scale(36), _settingsRect.Y, _settingsRect.Width, _settingsRect.Height);
        }

        /// <summary>세로 배치: 머리글 [+] ··· [정보] [설정], 그 아래로 탭 한 줄씩</summary>
        private void LayoutVertical()
        {
            _tabRects.Clear();
            var w = Width - 1;   // 오른쪽 경계선
            var hh = HeaderHeight;
            var btn = Theme.Scale(32);
            var by = (hh - btn) / 2;
            _addRect = new Rectangle(Theme.Scale(8), by, btn, btn);
            _settingsRect = new Rectangle(w - Theme.Scale(8) - btn, by, btn, btn);
            _infoRect = new Rectangle(_settingsRect.X - Theme.Scale(36), by, btn, btn);

            _listRect = new Rectangle(0, hh, w, Math.Max(0, Height - hh));
            var pad = Theme.Scale(4);
            _contentHeight = pad * 2 + _tabs.Count * (RowHeight + RowGap);
            _maxScroll = Math.Max(0, _contentHeight - _listRect.Height);

            if (_ensureSelectedVisible)
            {
                _ensureSelectedVisible = false;
                var si = Selected == null ? -1 : _tabs.IndexOf(Selected);
                if (si >= 0)
                {
                    var rowTop = pad + si * (RowHeight + RowGap);
                    if (rowTop < _scroll)
                        _scroll = rowTop;
                    else if (rowTop + RowHeight + pad > _scroll + _listRect.Height)
                        _scroll = rowTop + RowHeight + pad - _listRect.Height;
                }
            }
            _scroll = Math.Max(0, Math.Min(_scroll, _maxScroll));

            var x = Theme.Scale(6);
            var rw = w - Theme.Scale(12);
            for (var i = 0; i < _tabs.Count; i++)
                _tabRects.Add(new Rectangle(x, _listRect.Y + pad + i * (RowHeight + RowGap) - _scroll, rw, RowHeight));
        }

        private Rectangle CloseRect(Rectangle tab) =>
            new Rectangle(tab.Right - Theme.Scale(28), tab.Top + (tab.Height - Theme.Scale(20)) / 2, Theme.Scale(20), Theme.Scale(20));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Window);
            Theme.Smooth(g);
            LayoutTabs(g);

            using (var line = new Pen(Theme.Border))
            {
                if (_vertical)
                {
                    g.DrawLine(line, Width - 1, 0, Width - 1, Height);
                    g.DrawLine(line, Theme.Scale(8), HeaderHeight - 1, Width - Theme.Scale(9), HeaderHeight - 1);
                }
                else
                {
                    g.DrawLine(line, 0, Height - 1, Width, Height - 1);
                }
            }

            if (_vertical)
                g.SetClip(_listRect);
            for (var i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                var r = _tabRects[i];
                if (_vertical && !r.IntersectsWith(_listRect))
                    continue;
                var active = t == Selected;
                var hover = i == _hover;

                if (active || hover)
                {
                    if (_vertical)
                        PaintVerticalTabBack(g, r, active);
                    else
                        PaintHorizontalTabBack(g, r, active);
                }

                // 상태 점
                var dot = t.State == SessionState.Connected ? Theme.Success
                    : t.State == SessionState.Connecting ? Theme.Warning
                    : Theme.Faint;
                var ds = Theme.Scale(8);
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, r.X + Theme.Scale(12), r.Y + (r.Height - ds) / 2f, ds, ds);

                var textRect = new Rectangle(r.X + Theme.Scale(26), r.Y, r.Width - Theme.Scale(58), r.Height);
                TextRenderer.DrawText(g, TitleOf(t), active ? Theme.UiBold : Theme.UiFont, textRect, active ? Theme.Ink : Theme.SubText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                if (active || hover)
                {
                    var cr = CloseRect(r);
                    if (hover && _hoverClose)
                        using (var path = Theme.RoundRect(cr, Theme.Scale(4)))
                        using (var b = new SolidBrush(Theme.PrimarySoft))
                            g.FillPath(b, path);
                    Theme.DrawIcon(g, Theme.IconClose, Theme.IconsSmall, hover && _hoverClose ? Theme.Deep : Theme.SubText, cr);
                }
            }
            if (_vertical)
            {
                // 스크롤 위치 표시 (넘칠 때만, 얇은 막대)
                if (_maxScroll > 0 && _listRect.Height > 0)
                {
                    var th = Math.Max(Theme.Scale(24), _listRect.Height * _listRect.Height / Math.Max(1, _contentHeight));
                    var ty = _listRect.Y + (int)((long)_scroll * (_listRect.Height - th) / _maxScroll);
                    using (var path = Theme.RoundRect(new RectangleF(_listRect.Right - Theme.Scale(5), ty, Theme.Scale(3), th), Theme.Scale(2)))
                    using (var b = new SolidBrush(Theme.Border))
                        g.FillPath(b, path);
                }
                g.ResetClip();
            }

            // + 버튼
            if (_hoverAdd)
                using (var path = Theme.RoundRect(_addRect, Theme.Scale(6)))
                using (var b = new SolidBrush(Theme.PrimarySoft))
                    g.FillPath(b, path);
            Theme.DrawIcon(g, Theme.IconAdd, Theme.IconsSmall, _hoverAdd ? Theme.Deep : Theme.SubText, _addRect);

            // 설정
            if (_hoverSettings)
                using (var path = Theme.RoundRect(_settingsRect, Theme.Scale(6)))
                using (var b = new SolidBrush(Theme.PrimarySoft))
                    g.FillPath(b, path);
            Theme.DrawIcon(g, Theme.IconSettings, Theme.Icons, _hoverSettings ? Theme.Deep : Theme.SubText, _settingsRect);

            // 정보
            if (_hoverInfo)
                using (var path = Theme.RoundRect(_infoRect, Theme.Scale(6)))
                using (var b = new SolidBrush(Theme.PrimarySoft))
                    g.FillPath(b, path);
            Theme.DrawIcon(g, Theme.IconInfo, Theme.Icons, _hoverInfo ? Theme.Deep : Theme.SubText, _infoRect);
        }

        private static void PaintHorizontalTabBack(Graphics g, Rectangle r, bool active)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height + 8);
            using (var path = Theme.RoundRect(rf, Theme.Scale(8)))
            using (var b = new SolidBrush(active ? Color.White : Color.FromArgb(0xEF, 0xED, 0xF7)))
            {
                g.SetClip(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height));
                g.FillPath(b, path);
                if (active)
                    using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
                g.ResetClip();
            }
            if (active)
            {
                // 선택 탭: 아래 경계선 지우고 상단에 브랜드 색 강조
                using (var b = new SolidBrush(Color.White))
                    g.FillRectangle(b, r.X + 1, r.Bottom - 2, r.Width - 2, 3);
                using (var path = Theme.RoundRect(new RectangleF(r.X + Theme.Scale(10), r.Y, r.Width - Theme.Scale(20), Theme.Scale(3)), Theme.Scale(2)))
                using (var b = new SolidBrush(Theme.Primary))
                    g.FillPath(b, path);
            }
        }

        private static void PaintVerticalTabBack(Graphics g, Rectangle r, bool active)
        {
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using (var path = Theme.RoundRect(rf, Theme.Scale(8)))
            using (var b = new SolidBrush(active ? Color.White : Color.FromArgb(0xEF, 0xED, 0xF7)))
            {
                g.FillPath(b, path);
                if (active)
                    using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            if (active)
            {
                // 선택 탭: 왼쪽에 브랜드 색 강조
                using (var path = Theme.RoundRect(new RectangleF(r.X, r.Y + Theme.Scale(9), Theme.Scale(3), r.Height - Theme.Scale(18)), Theme.Scale(2)))
                using (var b = new SolidBrush(Theme.Primary))
                    g.FillPath(b, path);
            }
        }

        private int HitTab(Point p)
        {
            if (_vertical && !_listRect.Contains(p))
                return -1;
            for (var i = 0; i < _tabRects.Count; i++)
                if (_tabRects[i].Contains(p))
                    return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.Location);
        }

        private void UpdateHover(Point pt)
        {
            var hover = HitTab(pt);
            var hoverClose = hover >= 0 && CloseRect(_tabRects[hover]).Contains(pt);
            var hoverAdd = _addRect.Contains(pt);
            var hoverSettings = _settingsRect.Contains(pt);
            var hoverInfo = _infoRect.Contains(pt);
            if (hover != _hover || hoverClose != _hoverClose || hoverAdd != _hoverAdd || hoverSettings != _hoverSettings || hoverInfo != _hoverInfo)
            {
                if (hover != _hover || hoverAdd != _hoverAdd || hoverSettings != _hoverSettings || hoverInfo != _hoverInfo)
                    _tip.SetToolTip(this, hover >= 0 ? _tabs[hover].Profile.Display() + (string.IsNullOrEmpty(_tabs[hover].RemoteTitle) ? "" : "\n" + _tabs[hover].RemoteTitle)
                        : hoverAdd ? "빠른 접속 (Ctrl+Shift+L)"
                        : hoverSettings ? "설정"
                        : hoverInfo ? "LISH 정보"
                        : null);
                _hover = hover;
                _hoverClose = hoverClose;
                _hoverAdd = hoverAdd;
                _hoverSettings = hoverSettings;
                _hoverInfo = hoverInfo;
                Cursor = hover >= 0 || hoverAdd || hoverSettings || hoverInfo ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_vertical || _maxScroll <= 0)
                return;
            var next = Math.Max(0, Math.Min(_maxScroll, _scroll - e.Delta * (RowHeight + RowGap) / 120));
            if (next == _scroll)
                return;
            // 탭 위치를 바로 옮겨 hover 를 새 위치 기준으로 다시 계산
            var dy = _scroll - next;
            _scroll = next;
            for (var i = 0; i < _tabRects.Count; i++)
                _tabRects[i] = new Rectangle(_tabRects[i].X, _tabRects[i].Y + dy, _tabRects[i].Width, _tabRects[i].Height);
            UpdateHover(e.Location);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            _hoverClose = _hoverAdd = _hoverSettings = _hoverInfo = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var i = HitTab(e.Location);
            if (e.Button == MouseButtons.Middle && i >= 0)
            {
                CloseRequested?.Invoke(_tabs[i]);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (i >= 0)
                {
                    Select(_tabs[i]);
                    ContextRequested?.Invoke(_tabs[i], e.Location);
                }
                else if (!_addRect.Contains(e.Location) && !_settingsRect.Contains(e.Location) && !_infoRect.Contains(e.Location))
                {
                    ContextRequested?.Invoke(null, e.Location);
                }
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;
            if (i >= 0)
            {
                if (CloseRect(_tabRects[i]).Contains(e.Location))
                    CloseRequested?.Invoke(_tabs[i]);
                else
                    Select(_tabs[i]);
            }
            else if (_addRect.Contains(e.Location))
            {
                AddRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (_settingsRect.Contains(e.Location))
            {
                SettingsRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (_infoRect.Contains(e.Location))
            {
                InfoRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
