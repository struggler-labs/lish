using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    internal sealed class MainForm : Form
    {
        private readonly ProfileStore _store;
        private readonly AppSettings _settings;
        private readonly string[] _args;

        private readonly InputBox _search;
        private readonly SessionTree _tree;
        private readonly TabStrip _tabs;
        private readonly Panel _content;
        private readonly Panel _empty;
        private readonly Panel _status;
        private readonly Panel _sidebar;
        private readonly SegmentedTabs _sideTabs;
        private readonly Panel _sessionsPage;
        private readonly SftpSidebar _sftpPage;
        private bool _sidebarSftp;

        // 좌우 2분할 그룹: 두 탭을 탭 바에서 한 묶음으로 보이고, 그룹의 탭을 고르면 두 칸을 나란히 보인다.
        // 활성 칸의 탭이 _tabs.Selected 다. 그룹 밖의 탭을 고르면 그 탭만 보이고 그룹은 남는다.
        private readonly Panel _splitBar;
        private SessionView _left, _right;   // 그룹이 있을 때만 (아니면 null)
        private double _splitRatio = 0.5;
        private bool Split => _left != null && _right != null;
        /// <summary>지금 분할 화면을 보이는 중인지 (그룹의 탭이 선택됨)</summary>
        private bool ShowingSplit => Split && (_tabs.Selected == _left || _tabs.Selected == _right);
        private static int PaneBorder => Theme.Scale(2);

        /// <summary>SFTP 탭일 때 사이드바 최소/기본 폭 (아이콘 도구 모음 + 이름/크기 열이 들어가는 폭)</summary>
        private static int SftpMinWidth => Theme.Scale(260);
        private static int SftpDefaultWidth => Theme.Scale(320);

        public MainForm(string[] args)
        {
            _args = args;
            _store = ProfileStore.Load();
            _settings = AppSettings.Load();

            Text = "LISH";
            Icon = BrandIcon.AppIcon;
            Font = Theme.UiFont;
            BackColor = Theme.Window;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.Scale(900), Theme.Scale(560));
            KeyPreview = false;
            LoadWindowBounds();

            // ── 사이드바: 위 [세션 | SFTP] 탭, 아래 페이지 ──
            _sidebar = new Panel { Dock = DockStyle.Left, Width = Math.Max(Theme.Scale(200), _settings.SidebarWidth), BackColor = Theme.Sidebar };
            var tabsRow = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(52), BackColor = Theme.Sidebar, Padding = new Padding(Theme.Scale(12), Theme.Scale(12), Theme.Scale(12), Theme.Scale(6)) };
            _sideTabs = new SegmentedTabs(new[] { "세션", "SFTP" }, new[] { Theme.IconTerminal, Theme.IconFolder }) { Dock = DockStyle.Fill };
            _sideTabs.SelectedIndexChanged += (s, e) => SetSidebarPage(_sideTabs.SelectedIndex == 1);
            tabsRow.Controls.Add(_sideTabs);

            _sessionsPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
            var sideTop = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(46), BackColor = Theme.Sidebar, Padding = new Padding(Theme.Scale(12), Theme.Scale(4), Theme.Scale(12), Theme.Scale(10)) };
            _search = new InputBox("세션 검색", Theme.IconSearch) { Dock = DockStyle.Fill };
            _search.Box.TextChanged += (s, e) => RefreshTree();
            sideTop.Controls.Add(_search);

            _sftpPage = new SftpSidebar(_settings) { Dock = DockStyle.Fill, Visible = false };

            _tree = new SessionTree { Dock = DockStyle.Fill };
            _tree.ConnectRequested += p => Open(p.Clone());
            _tree.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    ShowTreeMenu(e.Node, e.Location);
            };
            _tree.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && _tree.GetNodeAt(e.Location) == null)
                    ShowTreeMenu(null, e.Location);
            };
            _tree.KeyDown += (s, e) =>
            {
                var folder = _tree.SelectedFolder;
                if (e.KeyCode == Keys.Delete)
                {
                    if (folder != null) DeleteFolder(folder);
                    else DeleteSession(_tree.SelectedProfile);
                }
                if (e.KeyCode == Keys.F2)
                {
                    if (folder != null) RenameFolder(folder);
                    else if (_tree.SelectedProfile != null) EditSession(_tree.SelectedProfile);
                }
            };
            _tree.MoveRequested += MoveInTree;

            _sessionsPage.Controls.Add(_tree);
            _sessionsPage.Controls.Add(sideTop);
            _sidebar.Controls.Add(_sessionsPage);
            _sidebar.Controls.Add(_sftpPage);
            _sidebar.Controls.Add(tabsRow);

            var splitter = new Splitter { Dock = DockStyle.Left, Width = Theme.Scale(4), BackColor = Theme.Border, MinSize = Theme.Scale(180) };
            splitter.Paint += (s, e) =>
            {
                e.Graphics.Clear(Theme.Window);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, 0, splitter.Height);
            };
            // 폭은 탭별로 기억 (SFTP 는 목록이 넓어야 해서 따로)
            splitter.SplitterMoved += (s, e) => RememberSidebarWidth();

            // ── 탭 + 내용 ──
            _tabs = new TabStrip { Dock = DockStyle.Top };
            _tabs.SelectedChanged += (s, e) => ShowSelected();
            _tabs.CloseRequested += CloseTab;
            _tabs.AddRequested += (s, e) => QuickConnect();
            _tabs.SettingsRequested += (s, e) => ShowSettings();
            _tabs.InfoRequested += (s, e) => { using (var d = new AboutDialog()) d.ShowDialog(this); };
            _tabs.ContextRequested += ShowTabMenu;

            _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.TerminalBg };
            _empty = BuildEmptyState();
            _content.Controls.Add(_empty);
            _content.Resize += (s, e) => LayoutPanes();
            _content.Paint += PaintPaneBorder;
            _splitBar = BuildSplitBar();
            _content.Controls.Add(_splitBar);

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
            main.Controls.Add(_content);
            main.Controls.Add(_tabs);

            // ── 상태바 ──
            _status = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(28), BackColor = Color.White };
            _status.Paint += PaintStatus;

            Controls.Add(main);
            Controls.Add(splitter);
            Controls.Add(_sidebar);
            Controls.Add(_status);

            RefreshTree();
            ShowSelected();
            Shown += OnShown;
        }

        private void LoadWindowBounds()
        {
            StartPosition = FormStartPosition.Manual;
            var size = new Size(Math.Max(Theme.Scale(900), _settings.WindowWidth), Math.Max(Theme.Scale(560), _settings.WindowHeight));
            var loc = new Point(_settings.WindowX, _settings.WindowY);
            var visible = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(loc, new Size(200, 100))));
            if (_settings.WindowX < 0 || !visible)
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
                loc = new Point(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2);
            }
            Bounds = new Rectangle(loc, size);
            if (_settings.WindowMaximized)
                WindowState = FormWindowState.Maximized;
        }

        private void OnShown(object sender, EventArgs e)
        {
            if (ProfileStore.LoadError != null)
                MessageBox.Show(this, ProfileStore.LoadError, "LISH", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            foreach (var arg in _args.Where(a => !a.StartsWith("-")))
            {
                try
                {
                    Open(Address.ResolveDestination(arg, _store));
                }
                catch (FormatException ex)
                {
                    MessageBox.Show(this, ex.Message, "LISH", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ───────────── 시작 화면 ─────────────

        private Panel BuildEmptyState()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
            var newBtn = new FlatButton("새 세션 만들기", ButtonKind.Primary, Theme.IconAdd) { Height = Theme.Scale(38) };
            newBtn.Width += Theme.Scale(12);
            newBtn.Click += (s, e) => NewSession(null);
            p.Controls.Add(newBtn);

            int ContentTop() => Math.Max(Theme.Scale(20), p.Height / 2 - Theme.Scale(190));

            p.Resize += (s, e) =>
            {
                var top = ContentTop() + Theme.Scale(250);
                newBtn.Location = new Point((p.Width - newBtn.Width) / 2, top);
                p.Invalidate();
            };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Theme.Smooth(g);
                var top = ContentTop();
                var iconSize = Theme.Scale(96);
                var iconRect = new RectangleF((p.Width - iconSize) / 2f, top, iconSize, iconSize);
                // 은은한 그림자
                for (var i = 6; i >= 1; i--)
                    using (var path = Theme.RoundRect(new RectangleF(iconRect.X - i, iconRect.Y - i + Theme.Scale(8), iconRect.Width + i * 2, iconRect.Height + i * 2), iconSize * 0.26f))
                    using (var b = new SolidBrush(Color.FromArgb(5, Theme.Primary)))
                        g.FillPath(b, path);
                BrandIcon.Draw(g, iconRect);

                var wmSize = Theme.Scale(40);
                var wm = BrandIcon.MeasureWordmark(g, wmSize);
                BrandIcon.DrawWordmark(g, new Point((int)((p.Width - wm.Width) / 2), top + iconSize + Theme.Scale(22)), wmSize);

                var tag = "Lightweight SSH & SFTP Client for Windows.";
                var tagSize = TextRenderer.MeasureText(g, tag, Theme.Heading, Size.Empty, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, tag, Theme.Heading, new Point((p.Width - tagSize.Width) / 2, top + iconSize + Theme.Scale(80)), Theme.SubText, TextFormatFlags.NoPrefix);

                var hints = new[]
                {
                    "왼쪽 목록에서 세션을 더블클릭하거나, 탭의 + (Ctrl+Shift+L) 로 user@host 에 바로 접속하세요.",
                    "접속하면 왼쪽 SFTP 탭에서 터미널의 현재 폴더가 자동으로 열립니다. 탐색기에서 파일을 끌어 놓으면 업로드됩니다.",
                    "Ctrl+Shift+T 탭 복제   ·   Ctrl+Shift+W 탭 닫기   ·   Ctrl+Tab 탭 전환   ·   Ctrl+Shift+F 세션/SFTP 탭 전환",
                };
                var y = top + Theme.Scale(320);
                foreach (var h in hints)
                {
                    var sz = TextRenderer.MeasureText(g, h, Theme.UiFont);
                    TextRenderer.DrawText(g, h, Theme.UiFont, new Point((p.Width - sz.Width) / 2, y), Theme.Faint);
                    y += Theme.Scale(24);
                }
            };
            return p;
        }

        // ───────────── 상태바 ─────────────

        private void PaintStatus(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            using (var pen = new Pen(Theme.Border))
                g.DrawLine(pen, 0, 0, _status.Width, 0);
            var v = _tabs.Selected;
            var x = Theme.Scale(14);
            var h = _status.Height;
            if (v != null)
            {
                var color = v.State == SessionState.Connected ? Theme.Success : v.State == SessionState.Connecting ? Theme.Warning : Theme.Faint;
                var stateText = v.State == SessionState.Connected ? "연결됨" : v.State == SessionState.Connecting ? "연결 중" : "연결 끊김";
                var ds = Theme.Scale(8);
                using (var b = new SolidBrush(color))
                    g.FillEllipse(b, x, (h - ds) / 2f, ds, ds);
                x += Theme.Scale(14);
                var text = $"{stateText}  ·  {v.Profile.Display()}";
                TextRenderer.DrawText(g, text, Theme.Small, new Point(x, (h - Theme.Small.Height) / 2), Theme.Ink);

                var right = $"{(string.IsNullOrEmpty(v.Profile.Encoding) ? "UTF-8" : v.Profile.Encoding.ToUpperInvariant())}   ·   {v.Term.Columns}×{v.Term.RowsCount}   ·   {v.Profile.TermOrDefault}";
                var sz = TextRenderer.MeasureText(g, right, Theme.Small);
                var rightX = _status.Width - sz.Width - Theme.Scale(14);
                TextRenderer.DrawText(g, right, Theme.Small, new Point(rightX, (h - Theme.Small.Height) / 2), Theme.SubText);
            }
            else
            {
                TextRenderer.DrawText(g, $"LISH {Program.Version}  ·  세션 {_store.All.Count}개  ·  {Paths.DataDir}", Theme.Small,
                    new Point(x, (h - Theme.Small.Height) / 2), Theme.SubText);
            }
        }

        // ───────────── 사이드바 탭 ─────────────

        /// <summary>사이드바를 세션 목록 / SFTP 로 바꾼다. SFTP 는 좁으면 최소 폭까지 넓힌다 (탭별 폭 기억).</summary>
        private void SetSidebarPage(bool sftp)
        {
            if (_sideTabs.SelectedIndex != (sftp ? 1 : 0))
            {
                _sideTabs.SelectedIndex = sftp ? 1 : 0;   // 이벤트로 다시 들어온다
                return;
            }
            if (_sidebarSftp == sftp)
                return;
            RememberSidebarWidth();
            _sidebarSftp = sftp;
            SuspendLayout();
            int width;
            if (sftp)
                width = Math.Max(SftpMinWidth, _settings.SftpSidebarWidth > 0 ? _settings.SftpSidebarWidth : Math.Max(SftpDefaultWidth, _settings.SidebarWidth));
            else
                width = Math.Max(Theme.Scale(200), _settings.SidebarWidth);
            // 터미널 영역이 너무 좁아지지 않게
            _sidebar.Width = Math.Max(Theme.Scale(200), Math.Min(width, ClientSize.Width - Theme.Scale(420)));
            _sessionsPage.Visible = !sftp;
            _sftpPage.Visible = sftp;
            ResumeLayout();
            _sftpPage.ShowFor(_tabs.Selected);
        }

        private void RememberSidebarWidth()
        {
            if (_sidebarSftp)
                _settings.SftpSidebarWidth = _sidebar.Width;
            else
                _settings.SidebarWidth = _sidebar.Width;
        }

        // ───────────── 세션 목록 ─────────────

        private void RefreshTree() => _tree.Populate(_store, _search.Text);

        private void NewSession(string group)
        {
            using (var d = new SessionDialog(_store, null, group ?? _tree.SelectedGroup))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                RefreshTree();
                if (d.ConnectAfterSave)
                    Open(d.Result.Clone());
            }
        }

        private void EditSession(HostProfile p)
        {
            if (p == null) return;
            using (var d = new SessionDialog(_store, p))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                RefreshTree();
                if (d.ConnectAfterSave)
                    Open(d.Result.Clone());
            }
        }

        private void DuplicateSession(HostProfile p)
        {
            if (p == null) return;
            var copy = p.Clone();
            copy.Name = _store.UniqueName(p.Name + " 복사본");
            _store.Upsert(copy);
            _store.Save();
            RefreshTree();
        }

        private void DeleteSession(HostProfile p)
        {
            if (p == null) return;
            if (MessageBox.Show(this, $"세션 '{p.Name}' 을(를) 삭제할까요?", "세션 삭제", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _store.Remove(p.Name);
            _store.Save();
            RefreshTree();
            _status.Invalidate();
        }

        // ───────────── 폴더 ─────────────

        private void NewFolder(string parent)
        {
            var title = parent == null ? "새 폴더" : "새 하위 폴더";
            var message = parent == null ? "폴더 이름" : $"'{FolderPath.Display(parent)}' 안에 만들 폴더 이름";
            for (var initial = ""; ;)
            {
                var name = InputDialog.Ask(this, title, message, false, initial);
                if (name == null)
                    return;
                try
                {
                    var path = _store.CreateFolder(parent, name);
                    _store.Save();
                    RefreshTree();
                    _tree.SelectFolder(path);
                    return;
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    initial = name;
                }
            }
        }

        private void RenameFolder(string path)
        {
            for (var initial = FolderPath.NameOf(path); ;)
            {
                var name = InputDialog.Ask(this, "폴더 이름 바꾸기", "새 폴더 이름", false, initial);
                if (name == null || name.Trim() == FolderPath.NameOf(path))
                    return;
                try
                {
                    var target = _store.RenameFolder(path, name);
                    _store.Save();
                    _tree.FolderMoved(path, target);
                    RefreshTree();
                    _tree.SelectFolder(target);
                    return;
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, "폴더 이름 바꾸기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    initial = name;
                }
            }
        }

        private void DeleteFolder(string path)
        {
            var sessions = _store.SessionsUnder(path).Count();
            var subfolders = _store.Folders.Count(f => FolderPath.IsUnder(f, path)) - 1;
            var message = $"폴더 '{FolderPath.Display(path)}' 을(를) 삭제할까요?";
            if (sessions > 0 || subfolders > 0)
            {
                var parts = new List<string>();
                if (subfolders > 0) parts.Add($"하위 폴더 {subfolders}개");
                if (sessions > 0) parts.Add($"세션 {sessions}개");
                message += $"\n\n안에 있는 {string.Join(", ", parts)}도 함께 삭제됩니다.";
            }
            if (MessageBox.Show(this, message, "폴더 삭제", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _store.DeleteFolder(path);
            _store.Save();
            _tree.FolderMoved(path, null);
            RefreshTree();
            _status.Invalidate();
        }

        /// <summary>세션(HostProfile) 또는 폴더(경로)를 target 폴더(null = 최상위)로 옮긴다.</summary>
        private void MoveInTree(object item, string target)
        {
            try
            {
                if (item is HostProfile p)
                {
                    p.Group = target;
                    _store.Upsert(p);
                }
                else if (item is string folder)
                {
                    var moved = _store.MoveFolder(folder, target);
                    _tree.FolderMoved(folder, moved);
                    item = moved;
                }
                _store.Save();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "이동", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshTree();
            if (item is string path)
                _tree.SelectFolder(path);
        }

        private void ShowTreeMenu(TreeNode node, Point location)
        {
            var m = new FlatMenu();
            if (node?.Tag is HostProfile p)
            {
                _tree.SelectedNode = node;
                m.Add("접속", Theme.IconConnect, () => Open(p.Clone()), "Enter").Font = Theme.UiBold;
                m.Add("편집...", Theme.IconEdit, () => EditSession(p), "F2");
                m.Add("복제", Theme.IconCopy, () => DuplicateSession(p));
                m.Add("주소 복사", Theme.IconLink, () => Clipboard.SetText(p.Target));
                m.AddSeparator();
                m.AddDanger("삭제", Theme.IconDelete, () => DeleteSession(p)).ShortcutKeyDisplayString = "Del";
            }
            else if (node?.Tag is string folder)
            {
                _tree.SelectedNode = node;
                m.Add("새 세션...", Theme.IconAdd, () => NewSession(folder));
                m.Add("새 하위 폴더...", Theme.IconNewFolder, () => NewFolder(folder));
                var all = _store.SessionsUnder(folder).ToList();
                m.Add($"폴더 전체 접속 ({all.Count})", Theme.IconPlay, () =>
                {
                    foreach (var gp in all)
                        Open(gp.Clone());
                }).Enabled = all.Count > 0;
                m.AddSeparator();
                m.Add("이름 바꾸기...", Theme.IconRename, () => RenameFolder(folder), "F2");
                if (FolderPath.ParentOf(folder) != null)
                    m.Add("최상위로 이동", Theme.IconMoveTo, () => MoveInTree(folder, null));
                m.AddDanger("폴더 삭제", Theme.IconDelete, () => DeleteFolder(folder)).ShortcutKeyDisplayString = "Del";
            }
            else
            {
                m.Add("새 세션...", Theme.IconAdd, () => NewSession(null), "Ctrl+Shift+N");
                m.Add("새 폴더...", Theme.IconNewFolder, () => NewFolder(null));
            }
            m.Closed += (s, e) => BeginInvoke((Action)m.Dispose);
            m.Show(_tree, location);
        }

        private void ShowSettings()
        {
            using (var d = new SettingsDialog(_settings))
            {
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                foreach (var v in _tabs.Tabs)
                    v.ApplySettings(_settings);
            }
        }

        // ───────────── 탭 ─────────────

        private void QuickConnect()
        {
            var text = InputDialog.Ask(this, "빠른 접속", "user@host:port 또는 세션 이름", false)?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            try
            {
                Open(Address.ResolveDestination(text, _store));
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "빠른 접속", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Open(HostProfile profile)
        {
            var view = new SessionView(profile, _store, _settings) { Visible = false };
            view.Bounds = _content.ClientRectangle;
            // 분할 중 다른 칸을 클릭하면 그 칸이 활성
            view.Enter += (s, e) => { if (ShowingSplit && view != _tabs.Selected && (view == _left || view == _right)) _tabs.Select(view); };
            view.StateChanged += (s, e) =>
            {
                _tabs.Invalidate();
                if (view == _tabs.Selected)
                {
                    _status.Invalidate();
                    _sftpPage.ShowFor(view);   // 연결/끊김에 따라 SFTP 자동 열기 ↔ 빈 화면
                }
            };
            view.Term.TerminalResized += (c, r) => { if (view == _tabs.Selected) _status.Invalidate(); };
            _content.Controls.Add(view);
            _tabs.Add(view);
            view.Connect();
        }

        private void ShowSelected()
        {
            var sel = _tabs.Selected;
            var split = ShowingSplit;
            foreach (Control c in _content.Controls)
                if (c is SessionView v)
                    v.Visible = split ? v == _left || v == _right : v == sel;
            _empty.Visible = sel == null;
            LayoutPanes();
            if (sel != null)
            {
                sel.BringToFront();
                sel.Term.Focus();
                Text = $"{sel.Profile.Title} — LISH";
            }
            else
            {
                Text = "LISH";
            }
            _sftpPage.ShowFor(sel);
            _status.Invalidate();
        }

        private void CloseTab(SessionView view)
        {
            if (view == null) return;
            // 그룹의 탭을 닫으면 그룹이 풀린다. 분할 화면을 보던 중이면 남은 칸만 보인다.
            SessionView remaining = null;
            if (Split && (view == _left || view == _right))
            {
                if (ShowingSplit)
                    remaining = view == _left ? _right : _left;
                _left = _right = null;
            }
            _tabs.Remove(view);
            _content.Controls.Remove(view);
            view.Disconnect();
            view.Dispose();
            if (remaining != null && _tabs.Selected != remaining)
                _tabs.Select(remaining);
            else
                ShowSelected();
        }

        // ───────────── 좌우 분할 ─────────────

        /// <summary>
        /// view 를 오른쪽 칸, 지금 선택된 탭(view 자신이면 이웃 탭)을 왼쪽 칸으로 하는 그룹을 만든다.
        /// 그룹은 하나만 — 이미 있으면 새 그룹으로 바뀐다.
        /// </summary>
        private void SplitWith(SessionView view)
        {
            var left = _tabs.Selected != view ? _tabs.Selected : _tabs.Tabs.FirstOrDefault(t => t != view);
            if (view == null || left == null)
                return;
            _left = left;
            _right = view;
            _tabs.SetGroup(_left, _right);
            if (_tabs.Selected == view)
                ShowSelected();
            else
                _tabs.Select(view);
        }

        private void Unsplit()
        {
            if (!Split) return;
            _left = _right = null;
            _tabs.SetGroup(null, null);
            ShowSelected();
        }

        /// <summary>분할이면 두 칸과 경계선, 아니면 선택 탭이 내용 영역 전체</summary>
        private void LayoutPanes()
        {
            var r = _content.ClientRectangle;
            if (!ShowingSplit)
            {
                _splitBar.Visible = false;
                foreach (Control c in _content.Controls)
                    if (c is SessionView v)
                        v.Bounds = r;
            }
            else
            {
                var bar = _splitBar.Width;
                var min = Theme.Scale(160);
                var lw = Math.Max(min, Math.Min(r.Width - bar - min, (int)((r.Width - bar) * _splitRatio)));
                var b = PaneBorder;
                _left.Bounds = Rectangle.Inflate(new Rectangle(0, 0, lw, r.Height), -b, -b);
                _right.Bounds = Rectangle.Inflate(new Rectangle(lw + bar, 0, r.Width - lw - bar, r.Height), -b, -b);
                _splitBar.Bounds = new Rectangle(lw, 0, bar, r.Height);
                _splitBar.Visible = true;
                _splitBar.BringToFront();
            }
            _content.Invalidate();
        }

        /// <summary>분할 중 활성 칸에 보라색 테두리</summary>
        private void PaintPaneBorder(object sender, PaintEventArgs e)
        {
            if (!ShowingSplit)
                return;
            var sel = _tabs.Selected;
            var b = PaneBorder;
            var r = Rectangle.Inflate(sel.Bounds, b, b);
            using (var pen = new Pen(Theme.Primary, b) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                e.Graphics.DrawRectangle(pen, r);
        }

        /// <summary>두 칸 사이 경계선. 끌어서 폭 조절, 두 번 클릭하면 반반.</summary>
        private Panel BuildSplitBar()
        {
            var bar = new Panel { Width = Theme.Scale(6), Cursor = Cursors.VSplit, BackColor = Color.FromArgb(0x2A, 0x26, 0x44), Visible = false };
            var dragging = false;
            bar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) dragging = true; };
            bar.MouseUp += (s, e) => dragging = false;
            bar.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var x = bar.Left + e.X - bar.Width / 2;
                var w = _content.ClientSize.Width - bar.Width;
                if (w <= 0) return;
                _splitRatio = Math.Max(0.15, Math.Min(0.85, x / (double)w));
                LayoutPanes();
                _content.Update();
            };
            bar.DoubleClick += (s, e) => { _splitRatio = 0.5; LayoutPanes(); };
            return bar;
        }

        /// <summary>Ctrl+Shift+F: 사이드바 세션 ↔ SFTP 탭</summary>
        private void ToggleSftp()
        {
            SetSidebarPage(!_sidebarSftp);
            if (!_sidebarSftp)
                _tabs.Selected?.Term.Focus();
        }

        private void ShowTabMenu(SessionView view, Point location)
        {
            var m = new FlatMenu();
            m.Add("다시 접속", Theme.IconRefresh, () => view.Connect()).Enabled = view.State == SessionState.Disconnected;
            m.Add("탭 복제", Theme.IconNewWindow, () => Open(view.Profile.Clone()), "Ctrl+Shift+T");
            m.Add("SFTP 열기 (사이드바)", Theme.IconFolder, () => SetSidebarPage(true), "Ctrl+Shift+F").Enabled = !_sidebarSftp;
            m.AddSeparator();
            if (Split && (view == _left || view == _right))
                m.Add("분할 해제 (그룹 풀기)", Theme.IconUnsplit, Unsplit);
            else
                m.Add("오른쪽에 분할로 보기", Theme.IconSplit, () => SplitWith(view)).Enabled = _tabs.Tabs.Count >= 2;
            if (string.IsNullOrEmpty(view.Profile.Name) || _store.Find(view.Profile.Name) == null)
                m.Add("세션으로 저장...", Theme.IconSave, () =>
                {
                    using (var d = new SessionDialog(_store, null))
                    {
                        if (d.ShowDialog(this) == DialogResult.OK)
                            RefreshTree();
                    }
                });
            m.AddSeparator();
            m.Add("닫기", Theme.IconClose, () => CloseTab(view), "Ctrl+Shift+W");
            m.Add("다른 탭 모두 닫기", null, () =>
            {
                foreach (var t in _tabs.Tabs.Where(t => t != view).ToList())
                    CloseTab(t);
            });
            m.Closed += (s, e) => BeginInvoke((Action)m.Dispose);
            m.Show(_tabs, location);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Tab:
                    _tabs.SelectNext(1);
                    return true;
                case Keys.Control | Keys.Shift | Keys.Tab:
                    _tabs.SelectNext(-1);
                    return true;
                case Keys.Control | Keys.Shift | Keys.T:
                    if (_tabs.Selected != null) Open(_tabs.Selected.Profile.Clone());
                    return true;
                case Keys.Control | Keys.Shift | Keys.W:
                    CloseTab(_tabs.Selected);
                    return true;
                case Keys.Control | Keys.Shift | Keys.N:
                    NewSession(null);
                    return true;
                case Keys.Control | Keys.Shift | Keys.F:
                    ToggleSftp();
                    return true;
                case Keys.Control | Keys.Shift | Keys.L:
                    QuickConnect();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ───────────── 테스트 훅 (LISH_TEST_HOOKS=1 일 때만) ─────────────
        // 실제 마우스/키보드 없이 GUI 를 검사하려고 WM_COPYDATA 로 명령을 받는다. 끌어 놓기·다운로드는 실제 처리 경로를 그대로 탄다.

        private const int WM_COPYDATA = 0x004A;
        private static readonly bool TestHooks = Environment.GetEnvironmentVariable("LISH_TEST_HOOKS") == "1";

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CopyData
        {
            public IntPtr Data;
            public int Length;
            public IntPtr Ptr;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA && TestHooks)
            {
                var cd = (CopyData)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(CopyData));
                var cmd = System.Runtime.InteropServices.Marshal.PtrToStringUni(cd.Ptr, cd.Length / 2);
                m.Result = (IntPtr)(RunTestCommand(cmd) ? 1 : 0);
                return;
            }
            base.WndProc(ref m);
        }

        private bool RunTestCommand(string cmd)
        {
            var i = cmd.IndexOf(':');
            var name = i < 0 ? cmd : cmd.Substring(0, i);
            var arg = i < 0 ? "" : cmd.Substring(i + 1);
            var panel = _sftpPage.Current;
            switch (name)
            {
                case "sidebar": SetSidebarPage(arg == "sftp"); return true;
                case "tab":
                    if (int.TryParse(arg, out var n) && n >= 0 && n < _tabs.Tabs.Count) { _tabs.Select(_tabs.Tabs[n]); return true; }
                    return false;
                case "follow": _sftpPage.FollowTerminal = arg == "on"; return true;
                case "close": CloseTab(_tabs.Selected); return true;
                case "split":
                    if (arg == "off") { Unsplit(); return true; }
                    if (int.TryParse(arg, out var si) && si >= 0 && si < _tabs.Tabs.Count) { SplitWith(_tabs.Tabs[si]); return true; }
                    return false;
                case "type": _tabs.Selected?.TestType(arg.Replace("\\r", "\r")); return _tabs.Selected != null;
                case "drag": panel?.SetDropHighlight(arg == "on"); return panel != null;
                case "drop": panel?.DropFiles(arg.Split('|')); return panel != null;
                case "download":
                    var parts = arg.Split('|');
                    return panel != null && parts.Length == 2 && panel.TestDownload(parts[0], parts[1]);
                case "width": if (int.TryParse(arg, out var w)) { _sidebar.Width = w; RememberSidebarWidth(); return true; } return false;
            }
            return false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var open = _tabs.Tabs.Count(t => t.State == SessionState.Connected);
            if (open > 0 && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, $"{open}개의 연결이 열려 있습니다. 모두 종료할까요?", "LISH 종료",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }

            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : base.RestoreBounds;
            _settings.WindowX = b.X;
            _settings.WindowY = b.Y;
            _settings.WindowWidth = b.Width;
            _settings.WindowHeight = b.Height;
            RememberSidebarWidth();
            _settings.Save();

            foreach (var t in _tabs.Tabs.ToList())
                t.Disconnect();
            base.OnFormClosing(e);
        }
    }
}
