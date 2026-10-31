using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lish.Ui
{
    /// <summary>세션 탭 바. 탭마다 연결 상태 점 + 제목 + 닫기 버튼.</summary>
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

        public SessionView Selected { get; private set; }

        // 좌우 분할 그룹 (왼쪽 칸, 오른쪽 칸). 탭 바에서 붙여서 한 묶음으로 그린다.
        private SessionView _groupLeft, _groupRight;
        private Rectangle _groupRect;

        /// <summary>분할 그룹을 정한다 (null 이면 해제). 오른쪽 탭은 왼쪽 탭 바로 뒤로 옮긴다.</summary>
        public void SetGroup(SessionView left, SessionView right)
        {
            if (left == null || right == null || !_tabs.Contains(left) || !_tabs.Contains(right))
                left = right = null;
            else
            {
                _tabs.Remove(right);
                _tabs.Insert(_tabs.IndexOf(left) + 1, right);
            }
            _groupLeft = left;
            _groupRight = right;
            Invalidate();
        }

        private bool InGroup(SessionView v) => v != null && (v == _groupLeft || v == _groupRight);

        public event EventHandler SelectedChanged;
        public event Action<SessionView> CloseRequested;
        public event EventHandler AddRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler InfoRequested;
        public event Action<SessionView, Point> ContextRequested;

        public IReadOnlyList<SessionView> Tabs => _tabs;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.Scale(40);
            BackColor = Theme.Window;
            Font = Theme.UiFont;
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
            if (InGroup(view))
                _groupLeft = _groupRight = null;
            if (Selected == view)
                Select(_tabs.Count == 0 ? null : _tabs[Math.Min(idx, _tabs.Count - 1)]);
            Invalidate();
        }

        public void Select(SessionView view)
        {
            Selected = view;
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

        private void LayoutTabs(Graphics g)
        {
            _tabRects.Clear();
            var x = Theme.Scale(8);
            var top = Theme.Scale(6);
            var h = Height - top;
            var right = Width - Theme.Scale(92);
            var maxW = Theme.Scale(220);
            var minW = Theme.Scale(110);
            var groupExtra = _groupLeft != null ? Theme.Scale(36) : 0;   // 분할 그룹의 아이콘 칸 + 여백
            var avail = Math.Max(minW, (right - x - Theme.Scale(36) - groupExtra) / Math.Max(1, _tabs.Count) - Theme.Scale(4));
            _groupRect = Rectangle.Empty;
            var groupIcon = Theme.Scale(26);
            foreach (var t in _tabs)
            {
                // 분할 그룹: 앞에 분할 아이콘 칸, 묶음 테두리 안쪽 여백
                if (t == _groupLeft)
                {
                    x += Theme.Scale(2);
                    _groupRect = new Rectangle(x, Theme.Scale(3), 0, Height - Theme.Scale(3));
                    x += groupIcon;
                }
                var textW = TextRenderer.MeasureText(g, TitleOf(t), Theme.UiFont).Width;
                var w = Math.Min(Math.Min(maxW, avail), Math.Max(minW, textW + Theme.Scale(60)));
                _tabRects.Add(new Rectangle(x, top, w, h));
                x += w + Theme.Scale(t == _groupLeft ? 2 : 4);
                if (t == _groupRight && !_groupRect.IsEmpty)
                {
                    _groupRect.Width = x - _groupRect.X;
                    x += Theme.Scale(6);
                }
            }
            _addRect = new Rectangle(x + Theme.Scale(2), top + Theme.Scale(4), Theme.Scale(28), h - Theme.Scale(8));
            // 오른쪽 끝: [정보] [설정] (SFTP 는 사이드바 SFTP 탭)
            _settingsRect = new Rectangle(Width - Theme.Scale(40), Theme.Scale(6), Theme.Scale(32), Height - Theme.Scale(12));
            _infoRect = new Rectangle(_settingsRect.X - Theme.Scale(36), _settingsRect.Y, _settingsRect.Width, _settingsRect.Height);
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
                g.DrawLine(line, 0, Height - 1, Width, Height - 1);

            // 분할 그룹 묶음: 연한 보라 배경 + 테두리, 앞에 분할 아이콘
            var groupShown = InGroup(Selected);
            if (_groupRect.Width > 0)
            {
                var gr = new RectangleF(_groupRect.X + 0.5f, _groupRect.Y + 0.5f, _groupRect.Width - 1, _groupRect.Height + Theme.Scale(8));
                using (var path = Theme.RoundRect(gr, Theme.Scale(9)))
                using (var b = new SolidBrush(groupShown ? Theme.PrimarySoft : Theme.PrimarySofter))
                using (var p = new Pen(groupShown ? Theme.Accent : Color.FromArgb(0xDD, 0xD5, 0xFB)))
                {
                    g.SetClip(new Rectangle(_groupRect.X - 1, _groupRect.Y - 1, _groupRect.Width + 2, Height - _groupRect.Y));
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                    g.ResetClip();
                }
                Theme.DrawIcon(g, Theme.IconSplit, Theme.IconsSmall, groupShown ? Theme.Primary : Theme.Accent,
                    new Rectangle(_groupRect.X + Theme.Scale(2), _groupRect.Y, Theme.Scale(24), _groupRect.Height));
            }

            for (var i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                var r = _tabRects[i];
                var active = t == Selected;
                var hover = i == _hover;
                var companion = groupShown && InGroup(t) && !active;   // 분할로 함께 보이는 다른 칸

                if (active || hover)
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

                // 상태 점
                var dot = t.State == SessionState.Connected ? Theme.Success
                    : t.State == SessionState.Connecting ? Theme.Warning
                    : Theme.Faint;
                var ds = Theme.Scale(8);
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, r.X + Theme.Scale(12), r.Y + (r.Height - ds) / 2f, ds, ds);

                var textRect = new Rectangle(r.X + Theme.Scale(26), r.Y, r.Width - Theme.Scale(58), r.Height);
                TextRenderer.DrawText(g, TitleOf(t), active ? Theme.UiBold : Theme.UiFont, textRect, active || companion ? Theme.Ink : Theme.SubText,
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

        private int HitTab(Point p)
        {
            for (var i = 0; i < _tabRects.Count; i++)
                if (_tabRects[i].Contains(p))
                    return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hover = HitTab(e.Location);
            var hoverClose = hover >= 0 && CloseRect(_tabRects[hover]).Contains(e.Location);
            var hoverAdd = _addRect.Contains(e.Location);
            var hoverSettings = _settingsRect.Contains(e.Location);
            var hoverInfo = _infoRect.Contains(e.Location);
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
            if (e.Button == MouseButtons.Right && i >= 0)
            {
                Select(_tabs[i]);
                ContextRequested?.Invoke(_tabs[i], e.Location);
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
