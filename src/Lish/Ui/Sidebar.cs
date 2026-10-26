using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    /// <summary>사이드바 상단의 세그먼트 탭 (세션 | SFTP). MobaXterm 왼쪽 탭에 해당.</summary>
    internal sealed class SegmentedTabs : Control
    {
        private readonly string[] _texts;
        private readonly string[] _glyphs;
        private int _selected;
        private int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedTabs(string[] texts, string[] glyphs)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _texts = texts;
            _glyphs = glyphs;
            Height = Theme.Scale(34);
            BackColor = Theme.Sidebar;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private Rectangle Segment(int i)
        {
            var pad = Theme.Scale(3);
            var w = (Width - pad * 2) / _texts.Length;
            return new Rectangle(pad + w * i, pad, i == _texts.Length - 1 ? Width - pad * 2 - w * i : w, Height - pad * 2);
        }

        private int Hit(Point p)
        {
            for (var i = 0; i < _texts.Length; i++)
                if (Segment(i).Contains(p))
                    return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Scale(8)))
            {
                using (var b = new SolidBrush(Theme.Window)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            for (var i = 0; i < _texts.Length; i++)
            {
                var r = Segment(i);
                var active = i == _selected;
                if (active || i == _hover)
                    using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.Scale(6)))
                    {
                        using (var b = new SolidBrush(active ? Color.White : Theme.PrimarySofter)) g.FillPath(b, path);
                        if (active)
                            using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
                    }
                var fore = active ? Theme.Deep : i == _hover ? Theme.Ink : Theme.SubText;
                var font = active ? Theme.UiBold : Theme.UiFont;
                var textW = TextRenderer.MeasureText(g, _texts[i], font, Size.Empty, TextFormatFlags.NoPadding).Width;
                var glyphW = Theme.Scale(16);
                var gap = Theme.Scale(6);
                var x = r.X + (r.Width - glyphW - gap - textW) / 2;
                Theme.DrawIcon(g, _glyphs[i], Theme.IconsSmall, active ? Theme.Primary : fore, new Rectangle(x, r.Y, glyphW, r.Height));
                TextRenderer.DrawText(g, _texts[i], font, new Rectangle(x + glyphW + gap, r.Y, textW + 2, r.Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = Hit(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var h = Hit(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0)
                SelectedIndex = h;
        }
    }

    /// <summary>
    /// 사이드바 SFTP 탭: 선택된 터미널 탭의 SFTP 패널을 보여 준다 (탭마다 하나, 처음 볼 때 만들고 유지).
    /// 연결된 세션이면 자동으로 SFTP 를 열고, '터미널 폴더 따라가기' 가 켜져 있으면 셸의 현재 폴더를 2초마다 확인해 따라간다.
    /// </summary>
    internal sealed class SftpSidebar : Panel
    {
        private readonly AppSettings _settings;
        private readonly Panel _host;
        private readonly Panel _empty;
        private readonly Panel _caption;
        private readonly Toggle _follow;
        private readonly Timer _timer;
        private readonly ToolTip _tips = new ToolTip();
        private SessionView _view;
        private bool _polling;

        private static readonly Font BigIcon = new Font(Theme.Icons.FontFamily, 22f);

        public SftpSidebar(AppSettings settings)
        {
            _settings = settings;
            BackColor = Theme.Sidebar;

            // 위: 어떤 세션의 SFTP 인지 (상태 점 + 세션 이름)
            _caption = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(26), BackColor = Theme.Sidebar };
            _caption.Paint += PaintCaption;

            _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
            _empty = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
            _empty.Paint += PaintEmpty;
            _empty.Resize += (s, e) => _empty.Invalidate();
            _host.Controls.Add(_empty);

            // 아래: 터미널 폴더 따라가기
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(38), BackColor = Theme.Sidebar, Padding = new Padding(Theme.Scale(12), 0, Theme.Scale(8), 0) };
            _follow = new Toggle("터미널 폴더 따라가기") { Dock = DockStyle.Left, Checked = settings.SftpFollowTerminal };
            _tips.SetToolTip(_follow, "터미널에서 cd 로 폴더를 옮기면 SFTP 목록도 그 폴더로 따라갑니다.\n전송 중이거나 경로를 입력하는 동안은 기다렸다가 따라갑니다.");
            _follow.CheckedChanged += (s, e) =>
            {
                _settings.SftpFollowTerminal = _follow.Checked;
                if (_follow.Checked && _view != null)
                    _view.FollowedCwd = null;   // 켜는 즉시 터미널 폴더로 맞춘다
                UpdateTimer();
                Poll();
            };
            bottom.Controls.Add(_follow);

            Controls.Add(_host);
            Controls.Add(_caption);
            Controls.Add(new Divider(DockStyle.Bottom));
            Controls.Add(bottom);

            _timer = new Timer { Interval = 2000 };
            _timer.Tick += (s, e) => Poll();
        }

        public bool FollowTerminal
        {
            get => _follow.Checked;
            set => _follow.Checked = value;
        }

        /// <summary>지금 보이는 SFTP 패널 (없으면 null)</summary>
        public SftpPanel Current => _view != null && _view.State == SessionState.Connected && Visible ? _view.Sftp : null;

        /// <summary>선택된 터미널 탭이 바뀌었거나 그 탭의 연결 상태가 바뀌었을 때 호출.</summary>
        public void ShowFor(SessionView view)
        {
            _view = view;
            UpdateView();
        }

        private void UpdateView()
        {
            _caption.Invalidate();
            var view = _view;
            var live = view != null && !view.IsDisposed && view.State == SessionState.Connected;
            // 사이드바가 SFTP 탭이 아닐 때는 패널을 만들지 않는다 (처음 볼 때 연결)
            if (!live || !Visible)
            {
                foreach (Control c in _host.Controls)
                    if (c is SftpPanel p)
                        p.Visible = false;
                _empty.Visible = true;
                _empty.Invalidate();
                UpdateTimer();
                return;
            }

            var panel = view.GetOrCreateSftp();
            if (panel.Parent != _host)
                _host.Controls.Add(panel);
            foreach (Control c in _host.Controls)
                if (c is SftpPanel p && p != panel)
                    p.Visible = false;
            _empty.Visible = false;
            panel.Visible = true;
            panel.BringToFront();
            panel.EnsureConnected();
            UpdateTimer();
            Poll();
        }

        private void UpdateTimer() => _timer.Enabled = Visible && _follow.Checked && _view != null && _view.State == SessionState.Connected;

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (IsHandleCreated)
                UpdateView();
        }

        /// <summary>셸의 현재 폴더를 작업 스레드에서 묻고, 바뀌었으면 SFTP 목록을 옮긴다.</summary>
        private void Poll()
        {
            var view = _view;
            if (_polling || !_follow.Checked || !Visible || view == null || view.IsDisposed || view.State != SessionState.Connected || view.Probe == null)
                return;
            var panel = view.Sftp;
            if (panel == null || !panel.IsConnected || panel.Busy)
                return;
            _polling = true;
            Task.Run(() => view.QueryShellCwd()).ContinueWith(t =>
            {
                _polling = false;
                if (IsDisposed || view.IsDisposed || view != _view || panel.IsDisposed || !_follow.Checked)
                    return;
                var cwd = t.Result;
                // 셸 폴더가 바뀌었을 때만 따라간다 → 사용자가 SFTP 에서 직접 옮긴 폴더는 셸이 cd 할 때까지 유지
                if (string.IsNullOrEmpty(cwd) || cwd == view.FollowedCwd)
                    return;
                if (panel.FollowTo(cwd))
                    view.FollowedCwd = cwd;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void PaintCaption(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var v = _view;
            var x = Theme.Scale(14);
            var h = _caption.Height;
            if (v == null || v.IsDisposed)
            {
                TextRenderer.DrawText(g, "선택된 세션 없음", Theme.Small, new Point(x, (h - Theme.Small.Height) / 2), Theme.Faint);
                return;
            }
            var color = v.State == SessionState.Connected ? Theme.Success : v.State == SessionState.Connecting ? Theme.Warning : Theme.Faint;
            var ds = Theme.Scale(7);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, x, (h - ds) / 2f, ds, ds);
            TextRenderer.DrawText(g, v.Profile.Display(), Theme.Small, new Rectangle(x + Theme.Scale(12), 0, _caption.Width - x - Theme.Scale(20), h), Theme.SubText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void PaintEmpty(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var v = _view;
            string title, sub;
            if (v == null || v.IsDisposed)
            {
                title = "세션에 연결하면 SFTP가 자동으로 열립니다";
                sub = "'세션' 탭에서 세션을 더블클릭해 접속하세요.";
            }
            else if (v.State == SessionState.Connecting || v.State == SessionState.Idle)
            {
                title = "연결 중...";
                sub = "연결되면 터미널의 현재 폴더에서 SFTP가 자동으로 열립니다.";
            }
            else if (v.State == SessionState.Disconnected)
            {
                title = "세션에 연결하면 SFTP가 자동으로 열립니다";
                sub = "연결이 끊겼습니다. 터미널에서 Enter 를 누르면 다시 접속합니다.";
            }
            else
            {
                title = "SFTP 여는 중...";
                sub = "";
            }

            var w = _empty.Width;
            var pad = Theme.Scale(24);
            var top = Math.Max(Theme.Scale(24), _empty.Height / 4);
            var circle = Theme.Scale(60);
            var cr = new Rectangle((w - circle) / 2, top, circle, circle);
            using (var b = new SolidBrush(Theme.PrimarySoft))
                g.FillEllipse(b, cr);
            Theme.DrawIcon(g, Theme.IconFolder, BigIcon, Theme.Primary, cr);

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            var textW = Math.Max(Theme.Scale(80), w - pad * 2);
            var y = cr.Bottom + Theme.Scale(16);
            var ts = TextRenderer.MeasureText(g, title, Theme.Heading, new Size(textW, int.MaxValue), flags);
            TextRenderer.DrawText(g, title, Theme.Heading, new Rectangle(pad, y, textW, ts.Height), Theme.Ink, flags);
            y += ts.Height + Theme.Scale(8);
            if (!string.IsNullOrEmpty(sub))
            {
                var ss = TextRenderer.MeasureText(g, sub, Theme.UiFont, new Size(textW, int.MaxValue), flags);
                TextRenderer.DrawText(g, sub, Theme.UiFont, new Rectangle(pad, y, textW, ss.Height), Theme.SubText, flags);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
