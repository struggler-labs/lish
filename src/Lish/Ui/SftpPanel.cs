using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Lish.Ui
{
    /// <summary>
    /// SFTP 파일 브라우저 (사이드바 SFTP 탭, 터미널 탭마다 하나). 별도 SFTP 연결(같은 경로/자격증명)로 동작한다.
    /// 좁은 사이드바에 맞춰 아이콘 도구 모음 + 경로 입력줄 + 목록(이름/크기, 넓히면 날짜/권한) 으로 구성.
    /// </summary>
    internal sealed class SftpPanel : UserControl
    {
        private readonly Func<SftpClient> _connect;
        private readonly Func<string> _initialPath;   // 처음 열 폴더 (터미널 셸의 현재 폴더, 작업 스레드에서 호출)
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private SftpClient _client;
        private string _path = "/";
        private string _home;
        private bool _connecting;
        private bool _navigating;

        private readonly InputBox _pathBox;
        private readonly ListView _list;
        private readonly Panel _listFrame;
        private readonly Label _dropBanner;
        private readonly Label _status;
        private readonly ThinProgress _progress;
        private readonly FlatButton _cancelButton;
        private readonly ToolTip _tips = new ToolTip();
        private readonly System.Windows.Forms.Timer _ticker;
        private readonly System.Windows.Forms.Timer _dropLeaveTimer;
        private readonly ImageList _icons;
        private TransferJob _job;   // 진행 중인 전송 (UI 스레드에서만 바꾼다)
        private bool _dropActive;
        private bool _draggingOut;  // 목록 → 탐색기 끌기 중 (자기 목록에 다시 놓으면 업로드되지 않게)

        // 전송 결과 문구 유지: 끝난 뒤 목록 새로 고침의 'N개 항목' 이 결과를 바로 덮지 않게 한다.
        private readonly System.Windows.Forms.Timer _holdTimer;
        private bool _holdResult;   // true 면 'N개 항목' 은 _listText 에 담아 두기만 한다
        private string _listText;   // 유지가 끝나면 보여 줄 목록 문구

        // 탐색기로 끌어 놓기용 임시 파일 (원격 파일 → 받아 둔 로컬 경로). 같은 파일을 다시 끌면 바로 쓴다.
        private const long DragLimit = 1024L * 1024 * 1024;
        private readonly Dictionary<string, string> _dragCache = new Dictionary<string, string>();
        private readonly List<string> _dragDirs = new List<string>();

        public SftpPanel(Func<SftpClient> connect, Encoding encoding, Func<string> initialPath = null)
        {
            _connect = connect;
            _initialPath = initialPath;
            BackColor = Theme.Sidebar;
            Font = Theme.UiFont;

            // ── 도구 모음: 아이콘 버튼 (좁은 사이드바에서도 한 줄) ──
            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = Theme.Scale(34),
                WrapContents = false,
                BackColor = Theme.Sidebar,
                Padding = new Padding(Theme.Scale(8), Theme.Scale(2), Theme.Scale(8), 0),
            };
            tools.Controls.Add(ToolButton(Theme.IconUp, "상위 폴더 (Backspace)", (s, e) => Navigate(ParentOf(_path))));
            tools.Controls.Add(ToolButton(Theme.IconHome, "홈 폴더", (s, e) => Navigate(_home ?? _path)));
            tools.Controls.Add(ToolButton(Theme.IconRefresh, "새로 고침 (F5)", (s, e) => Navigate(_path)));
            tools.Controls.Add(ToolButton(Theme.IconNewFolder, "새 폴더", (s, e) => NewFolder()));
            tools.Controls.Add(ToolButton(Theme.IconUpload, "업로드 (탐색기에서 파일·폴더를 목록으로 끌어 놓아도 됩니다)", (s, e) => UploadDialog()));
            tools.Controls.Add(ToolButton(Theme.IconDownload, "선택 항목 다운로드", (s, e) => DownloadSelected()));

            var pathRow = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(40), Padding = new Padding(Theme.Scale(10), Theme.Scale(2), Theme.Scale(10), Theme.Scale(6)), BackColor = Theme.Sidebar };
            _pathBox = new InputBox("경로 입력 후 Enter", Theme.IconFolder) { Dock = DockStyle.Fill };
            _pathBox.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Navigate(_pathBox.Text.Trim());
                    _list.Focus();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _pathBox.Text = _path;
                    _list.Focus();
                }
            };
            pathRow.Controls.Add(_pathBox);

            _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(Theme.Scale(18), Theme.Scale(18)) };
            _icons.Images.Add("dir", GlyphImage(Theme.IconFolder, Theme.Primary));
            _icons.Images.Add("file", GlyphImage(Theme.IconFile, Theme.SubText));
            _icons.Images.Add("link", GlyphImage(Theme.IconChevronRight, Theme.Accent));

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                SmallImageList = _icons,
                AllowDrop = true,
                LabelEdit = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = Theme.UiFont,
                ForeColor = Theme.Ink,
                BackColor = Color.White,
            };
            _list.Columns.Add("이름", Theme.Scale(190));
            _list.Columns.Add("크기", Theme.Scale(70), HorizontalAlignment.Right);
            _list.Columns.Add("수정한 날짜", Theme.Scale(120));
            _list.Columns.Add("권한", Theme.Scale(86));
            _list.HandleCreated += (s, e) =>
            {
                Theme.ExplorerStyle(_list);
                FitColumns();
            };
            _list.Resize += (s, e) => FitColumns();
            _list.ItemActivate += (s, e) => OpenSelected();
            _list.KeyDown += OnListKeyDown;
            _list.ItemDrag += OnItemDrag;
            _list.BeforeLabelEdit += (s, e) => _editing = true;
            _list.AfterLabelEdit += OnAfterLabelEdit;
            _list.ContextMenuStrip = BuildMenu();

            // 탐색기에서 끌어 오면 목록 테두리/배경을 강조하고 위에 '놓으면 업로드' 띠를 보인다.
            // 띠와 테두리도 놓기 대상이라 그 위에 놓아도 같은 폴더로 올라간다.
            _dropBanner = new Label
            {
                Dock = DockStyle.Top,
                Height = Theme.Scale(30),
                BackColor = Theme.Primary,
                ForeColor = Color.White,
                Font = Theme.UiBold,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Visible = false,
                AllowDrop = true,
            };
            _listFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AllowDrop = true };
            _listFrame.Controls.Add(_list);
            _listFrame.Controls.Add(_dropBanner);
            _dropLeaveTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _dropLeaveTimer.Tick += (s, e) =>
            {
                _dropLeaveTimer.Stop();
                SetDropHighlight(false);
            };
            foreach (var c in new Control[] { _list, _dropBanner, _listFrame })
            {
                c.DragEnter += OnFileDragOver;
                c.DragOver += OnFileDragOver;
                // 목록 ↔ 띠 사이를 지날 때 깜빡이지 않도록 잠깐 기다렸다 끈다
                c.DragLeave += (s, e) => _dropLeaveTimer.Start();
                c.DragDrop += OnFileDrop;
            }

            // ── 아래: 얇은 진행 막대 + 상태 문구 + 취소 ──
            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scale(28), BackColor = Theme.Sidebar, Padding = new Padding(Theme.Scale(10), 0, Theme.Scale(4), 0) };
            _status = new Label { Dock = DockStyle.Fill, ForeColor = Theme.SubText, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _status.TextChanged += (s, e) => _tips.SetToolTip(_status, _status.Text);
            _progress = new ThinProgress { Dock = DockStyle.Bottom, Visible = false };
            _cancelButton = new FlatButton(null, ButtonKind.Ghost, Theme.IconClose) { Dock = DockStyle.Right, Width = Theme.Scale(28), Visible = false };
            _tips.SetToolTip(_cancelButton, "전송 취소 (전송 중이던 파일은 삭제)");
            _cancelButton.Click += (s, e) => CancelTransfer();
            footer.Controls.Add(_status);
            footer.Controls.Add(_cancelButton);
            _ticker = new System.Windows.Forms.Timer { Interval = 150 };
            _ticker.Tick += (s, e) => ShowProgress();
            _holdTimer = new System.Windows.Forms.Timer { Interval = 6000 };
            _holdTimer.Tick += (s, e) =>
            {
                var text = _listText;
                ReleaseHold();
                if (text != null && _job == null)
                    SetStatus(text);
            };

            Controls.Add(_listFrame);
            Controls.Add(new Divider(DockStyle.Top));
            Controls.Add(pathRow);
            Controls.Add(tools);
            Controls.Add(_progress);
            Controls.Add(new Divider(DockStyle.Bottom));
            Controls.Add(footer);
        }

        /// <summary>현재 폴더</summary>
        public string CurrentPath => _path;

        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>
        /// 터미널 폴더 따라가기를 미뤄야 하는 상태: 전송·연결·목록 읽기 중, 경로 입력 중, 이름 바꾸는 중, 끌어 놓는 중.
        /// </summary>
        public bool Busy => _job != null || _connecting || _navigating || _pathBox.Box.Focused || _dropActive || _draggingOut || _editing;

        private bool _editing;

        /// <summary>터미널 폴더 따라가기: 바쁘지 않을 때만 그 폴더로 이동한다. 이동을 시작했으면 true.</summary>
        public bool FollowTo(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsConnected || Busy)
                return false;
            path = NormalizePath(path);
            if (path == _path)
                return true;
            Navigate(path);
            return true;
        }

        // ───────────── 탐색기 → 목록 끌어 놓기 (업로드) ─────────────

        private bool CanAcceptDrop(IDataObject data) =>
            !_draggingOut && _client != null && data != null && data.GetDataPresent(DataFormats.FileDrop);

        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            if (!CanAcceptDrop(e.Data))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Copy;
            _dropLeaveTimer.Stop();
            SetDropHighlight(true);
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            _dropLeaveTimer.Stop();
            SetDropHighlight(false);
            if (CanAcceptDrop(e.Data) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
                DropFiles(files);
        }

        /// <summary>끌어 놓기 강조 (보라 테두리 + 연보라 배경 + '놓으면 업로드' 띠)</summary>
        public void SetDropHighlight(bool on)
        {
            if (_dropActive == on || IsDisposed)
                return;
            _dropActive = on;
            _dropBanner.Text = on ? $"놓으면 업로드  →  {_path}" : "";
            _dropBanner.Visible = on;
            _listFrame.Padding = on ? new Padding(Theme.Scale(2)) : Padding.Empty;
            _listFrame.BackColor = on ? Theme.Primary : Color.White;
            _list.BackColor = on ? Theme.PrimarySofter : Color.White;
        }

        /// <summary>로컬 파일/폴더를 현재 폴더로 올린다 (끌어 놓기·테스트 훅 공통 경로)</summary>
        public void DropFiles(string[] files)
        {
            if (files == null || files.Length == 0)
                return;
            if (_client == null)
            {
                SetError("SFTP 가 아직 연결되지 않았습니다.");
                return;
            }
            Upload(files);
        }

        /// <summary>
        /// 고정 열(크기/날짜/권한)은 실제 글자 폭에 맞추고 남는 폭은 모두 이름 열에 준다.
        /// 이름 열이 너무 좁아지면 권한 → 날짜 순으로 숨긴다.
        /// </summary>
        private void FitColumns()
        {
            if (_list.Columns.Count < 4) return;
            var w = _list.ClientSize.Width;
            if (w <= 0) return;
            var pad = Theme.Scale(14);
            var size = TextRenderer.MeasureText("1023.9 KB", _list.Font).Width + pad;
            var date = TextRenderer.MeasureText("0000-00-00 00:00", _list.Font).Width + pad;
            var perm = TextRenderer.MeasureText("drwxr-xr-x", _list.Font).Width + pad;
            var minName = Theme.Scale(160);
            if (w - size - date - perm < minName) perm = 0;
            if (w - size - date - perm < minName) date = 0;
            _list.BeginUpdate();
            _list.Columns[1].Width = size;
            _list.Columns[2].Width = date;
            _list.Columns[3].Width = perm;
            // 가로 스크롤바가 생기지 않도록 1px 여유
            _list.Columns[0].Width = Math.Max(Theme.Scale(80), w - size - date - perm - 1);
            _list.EndUpdate();
        }

        private static Bitmap GlyphImage(string glyph, Color color)
        {
            var s = Theme.Scale(18);
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, Theme.Icons, b, new RectangleF(0, 0, s, s), sf);
            }
            return bmp;
        }

        private FlatButton ToolButton(string glyph, string tip, EventHandler click)
        {
            var b = new FlatButton(null, ButtonKind.Ghost, glyph) { Width = Theme.Scale(30), Height = Theme.Scale(30), Margin = new Padding(0, 0, Theme.Scale(2), 0) };
            _tips.SetToolTip(b, tip);
            b.Click += click;
            return b;
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip { Renderer = new FlatMenuRenderer(), Font = Theme.UiFont };
            m.Items.Add("열기", null, (s, e) => OpenSelected());
            m.Items.Add("다운로드...", null, (s, e) => DownloadSelected());
            m.Items.Add("업로드...", null, (s, e) => UploadDialog());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("이름 바꾸기", null, (s, e) => _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.BeginEdit());
            m.Items.Add("새 폴더", null, (s, e) => NewFolder());
            m.Items.Add("삭제", null, (s, e) => DeleteSelected());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("경로 복사", null, (s, e) =>
            {
                var e1 = SelectedEntries().FirstOrDefault();
                Clipboard.SetText(e1 != null ? e1.FullName : _path);
            });
            m.Items.Add("새로 고침", null, (s, e) => Navigate(_path));
            return m;
        }

        // ───────────── 연결/탐색 ─────────────

        public void EnsureConnected()
        {
            if (_client != null && _client.IsConnected)
                return;
            if (_connecting)
                return;
            _connecting = true;
            SetStatus("SFTP 연결 중...");
            RunAsync(() =>
            {
                try
                {
                    var c = _connect();
                    _client = c;
                    _home = NormalizePath(c.WorkingDirectory);
                    // 터미널 셸의 현재 폴더에서 연다. 모르면 홈 폴더.
                    string cwd = null;
                    try { cwd = _initialPath?.Invoke(); } catch (Exception) { /* 모름 */ }
                    _path = string.IsNullOrEmpty(cwd) ? _home : NormalizePath(cwd);
                }
                finally
                {
                    _connecting = false;
                }
            }, () => Navigate(_path));
        }

        /// <summary>SSH 재접속 후 SFTP 도 다시 연결</summary>
        public void Reconnect()
        {
            if (!Visible)
                return;
            try { _client?.Dispose(); } catch { /* 무시 */ }
            _client = null;
            EnsureConnected();
        }

        /// <summary>SSH 연결이 끊겼을 때: 전송을 취소하고 SFTP 연결도 닫는다 (다시 접속하면 Reconnect).</summary>
        public void Shutdown()
        {
            _job?.Cts.Cancel();
            var c = _client;
            _client = null;
            if (c != null)
                Task.Run(() => { try { c.Dispose(); } catch { /* 무시 */ } });
            if (!IsDisposed)
            {
                _list.Items.Clear();
                SetStatus("연결 끊김");
            }
        }

        /// <param name="keepResult">전송 직후의 새로 고침이면 true: 상태 표시줄의 전송 결과를 잠시 유지한다.</param>
        private void Navigate(string path, bool keepResult = false)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!keepResult)
                ReleaseHold();
            if (_client == null || !_client.IsConnected)
            {
                EnsureConnected();
                return;
            }
            List<ISftpFile> entries = null;
            var target = path;
            _navigating = true;
            RunAsync(() =>
            {
                var full = _client.GetAttributes(target).IsDirectory ? target : ParentOf(target);
                entries = _client.ListDirectory(full).Where(f => f.Name != ".").ToList();
                target = full;
            }, () =>
            {
                _path = NormalizePath(target);
                if (!_pathBox.Box.Focused)
                    _pathBox.Text = _path;
                Populate(entries);
                var text = $"{entries.Count(f => f.Name != "..")}개 항목";
                if (_holdResult)
                    _listText = text;   // 전송 결과를 보여 주는 중이면 나중에 표시
                else
                    SetStatus(text);
            }, () => _navigating = false);
        }

        private void Populate(List<ISftpFile> entries)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            var ordered = entries
                .Where(f => f.Name != "..")
                .OrderByDescending(f => f.IsDirectory)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            if (_path != "/")
            {
                var up = new ListViewItem("..", "dir") { Tag = null };
                up.SubItems.AddRange(new[] { "", "", "" });
                _list.Items.Add(up);
            }
            foreach (var f in ordered)
            {
                var item = new ListViewItem(f.Name, f.IsDirectory ? "dir" : f.IsSymbolicLink ? "link" : "file") { Tag = f };
                item.SubItems.Add(f.IsDirectory ? "" : FormatSize(f.Length));
                item.SubItems.Add(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(Permissions(f));
                if (f.Name.StartsWith("."))
                    item.ForeColor = Theme.SubText;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            // 세로 스크롤바가 생기거나 사라지면 ClientSize 는 바뀌지만 Resize 는 오지 않으므로 다시 맞춘다
            FitColumns();
        }

        private static string Permissions(ISftpFile f)
        {
            var sb = new StringBuilder(10);
            sb.Append(f.IsDirectory ? 'd' : f.IsSymbolicLink ? 'l' : '-');
            sb.Append(f.OwnerCanRead ? 'r' : '-').Append(f.OwnerCanWrite ? 'w' : '-').Append(f.OwnerCanExecute ? 'x' : '-');
            sb.Append(f.GroupCanRead ? 'r' : '-').Append(f.GroupCanWrite ? 'w' : '-').Append(f.GroupCanExecute ? 'x' : '-');
            sb.Append(f.OthersCanRead ? 'r' : '-').Append(f.OthersCanWrite ? 'w' : '-').Append(f.OthersCanExecute ? 'x' : '-');
            return sb.ToString();
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            var u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
        }

        private static string NormalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "/";
            p = p.Replace('\\', '/');
            while (p.Length > 1 && p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        private static string ParentOf(string p)
        {
            p = NormalizePath(p);
            var i = p.LastIndexOf('/');
            return i <= 0 ? "/" : p.Substring(0, i);
        }

        private static string Combine(string dir, string name) => (dir.EndsWith("/") ? dir : dir + "/") + name;

        private IEnumerable<ISftpFile> SelectedEntries() =>
            _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as ISftpFile).Where(f => f != null);

        private void OpenSelected()
        {
            var item = _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
            if (item == null)
                return;
            if (item.Tag == null)
            {
                Navigate(ParentOf(_path));
                return;
            }
            var f = (ISftpFile)item.Tag;
            if (f.IsDirectory || f.IsSymbolicLink)
                Navigate(f.FullName);
            else
                DownloadSelected();
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Back: Navigate(ParentOf(_path)); e.Handled = true; break;
                case Keys.F5: Navigate(_path); e.Handled = true; break;
                case Keys.Delete: DeleteSelected(); e.Handled = true; break;
                case Keys.F2: _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.BeginEdit(); e.Handled = true; break;
            }
        }

        // ───────────── 파일 작업 ─────────────

        private void UploadDialog()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = $"업로드 → {_path}" })
            {
                if (d.ShowDialog(FindForm()) == DialogResult.OK)
                    Upload(d.FileNames);
            }
        }

        private void Upload(string[] localPaths)
        {
            if (_client == null || TransferBusy())
                return;
            var dir = _path;
            RunTransfer("업로드", job =>
            {
                // 로컬 폴더 훑기도 작업 스레드에서 (큰 폴더에서 UI 가 멈추지 않게)
                var files = new List<(string local, string remote, long length)>();
                var dirs = new List<string>();
                foreach (var p in localPaths)
                {
                    job.Token.ThrowIfCancellationRequested();
                    if (Directory.Exists(p))
                    {
                        var root = Combine(dir, Path.GetFileName(p));
                        dirs.Add(root);
                        foreach (var sub in Directory.GetDirectories(p, "*", SearchOption.AllDirectories))
                            dirs.Add(Combine(root, sub.Substring(p.Length + 1).Replace('\\', '/')));
                        foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                            files.Add((f, Combine(root, f.Substring(p.Length + 1).Replace('\\', '/')), new FileInfo(f).Length));
                    }
                    else if (File.Exists(p))
                    {
                        files.Add((p, Combine(dir, Path.GetFileName(p)), new FileInfo(p).Length));
                    }
                }
                job.SetTotal(files.Count, files.Sum(f => f.length));
                foreach (var d in dirs)
                {
                    job.Token.ThrowIfCancellationRequested();
                    if (!_client.Exists(d))
                        _client.CreateDirectory(d);
                }
                foreach (var (local, remote, length) in files)
                    UploadOne(job, local, remote, length);
            }, () => Navigate(dir, true));
        }

        private void UploadOne(TransferJob job, string local, string remote, long length)
        {
            job.Token.ThrowIfCancellationRequested();
            job.BeginFile(Path.GetFileName(local), remote, true);
            // 원격 파일은 SSH.NET 이 열고(덮어쓰기) 나서 첫 Read 를 하므로, 첫 Read 시점부터 '받다 만 파일'로 본다.
            using (var fs = new CancelableStream(File.OpenRead(local), job))
                _client.UploadFile(fs, remote, true, n => job.Progress((long)n));
            job.EndFile(length);
        }

        private void DownloadSelected()
        {
            var entries = SelectedEntries().ToList();
            if (entries.Count == 0 || _client == null || TransferBusy())
                return;

            if (entries.Count == 1 && !entries[0].IsDirectory)
            {
                string dest;
                // 형식별 필터(*.txt 등)를 주면 탐색기 설정에 따라 이름 칸에서 확장자가 숨겨지므로
                // '모든 파일' 하나만 두어 원격 이름이 확장자까지 그대로 보이고 그대로 저장되게 한다.
                using (var d = new SaveFileDialog
                {
                    Title = "다운로드",
                    Filter = "모든 파일 (*.*)|*.*",
                    FileName = SafeName(entries[0].Name),   // 못 쓰는 글자가 있으면 대화 상자가 이름을 비우므로 치환
                    AddExtension = false,                   // 원격 이름 그대로 (확장자를 덧붙이지 않음)
                    OverwritePrompt = true,
                })
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK)
                        return;
                    dest = d.FileName;
                }
                var single = entries[0];
                RunTransfer("다운로드", job =>
                {
                    job.SetTotal(1, single.Length);
                    DownloadOne(job, single, dest);
                }, null, dest);
                return;
            }

            string targetDir;
            using (var d = new FolderBrowserDialog { Description = $"{entries.Count}개 항목을 저장할 폴더" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK)
                    return;
                targetDir = d.SelectedPath;
            }
            DownloadTo(entries, targetDir);
        }

        /// <summary>항목들(폴더는 재귀)을 로컬 폴더로 받는다. 대화 상자 없이 쓰는 공통 경로 (테스트 훅도 사용).</summary>
        private void DownloadTo(List<ISftpFile> entries, string targetDir)
        {
            RunTransfer("다운로드", job =>
            {
                // 먼저 하위 폴더까지 훑어 전체 크기를 구한 뒤 받는다 (진행률을 전체 기준으로).
                var files = new List<(ISftpFile remote, string local)>();
                foreach (var e in entries)
                    ScanRemote(job, e, targetDir, files);
                job.SetTotal(files.Count, files.Sum(f => f.remote.Length));
                foreach (var (remote, local) in files)
                    DownloadOne(job, remote, local);
            }, null, targetDir);
        }

        /// <summary>테스트 훅: 현재 폴더의 이름이 name 인 항목을 localDir 로 받는다 (저장 대화 상자 없이).</summary>
        public bool TestDownload(string name, string localDir)
        {
            var entry = _list.Items.Cast<ListViewItem>().Select(i => i.Tag as ISftpFile).FirstOrDefault(f => f != null && f.Name == name);
            if (entry == null || _client == null || !Directory.Exists(localDir) || TransferBusy())
                return false;
            DownloadTo(new List<ISftpFile> { entry }, localDir);
            return true;
        }

        /// <summary>원격 항목을 재귀로 훑어 받을 파일 목록을 만들고, 로컬 폴더는 미리 만든다.</summary>
        private void ScanRemote(TransferJob job, ISftpFile entry, string localDir, List<(ISftpFile remote, string local)> files)
        {
            job.Token.ThrowIfCancellationRequested();
            var local = Path.Combine(localDir, SafeName(entry.Name));
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(local);
                foreach (var child in _client.ListDirectory(entry.FullName))
                {
                    if (child.Name == "." || child.Name == "..")
                        continue;
                    ScanRemote(job, child, local, files);
                }
                return;
            }
            files.Add((entry, local));
        }

        private void DownloadOne(TransferJob job, ISftpFile remote, string local)
        {
            job.Token.ThrowIfCancellationRequested();
            job.BeginFile(remote.Name, local, false);
            using (var fs = new CancelableStream(File.Create(local), job))
            {
                job.PartialStarted = true;  // 로컬 파일은 만든 순간부터 정리 대상
                _client.DownloadFile(remote.FullName, fs, n => job.Progress((long)n));
            }
            job.EndFile(remote.Length);
        }

        private static string SafeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void NewFolder()
        {
            var name = InputDialog.Ask(FindForm(), "새 폴더", $"{_path} 에 만들 폴더 이름", false);
            if (string.IsNullOrWhiteSpace(name) || _client == null)
                return;
            var dir = _path;
            RunAsync(() => _client.CreateDirectory(Combine(dir, name.Trim())), () => Navigate(dir));
        }

        private void DeleteSelected()
        {
            var entries = SelectedEntries().ToList();
            if (entries.Count == 0 || _client == null)
                return;
            var what = entries.Count == 1 ? $"'{entries[0].Name}'" : $"{entries.Count}개 항목";
            if (MessageBox.Show(FindForm(), $"{what}을(를) 원격 서버에서 영구 삭제합니다.\n폴더는 내용까지 모두 삭제됩니다.", "삭제",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            var dir = _path;
            RunAsync(() =>
            {
                foreach (var e in entries)
                    DeleteRecursive(e);
            }, () => Navigate(dir));
        }

        private void DeleteRecursive(ISftpFile entry)
        {
            if (entry.IsDirectory)
            {
                foreach (var child in _client.ListDirectory(entry.FullName))
                    if (child.Name != "." && child.Name != "..")
                        DeleteRecursive(child);
                _client.DeleteDirectory(entry.FullName);
            }
            else
            {
                _client.DeleteFile(entry.FullName);
            }
        }

        private void OnAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            _editing = false;
            var item = _list.Items[e.Item];
            if (string.IsNullOrWhiteSpace(e.Label) || !(item.Tag is ISftpFile f) || e.Label == f.Name)
            {
                e.CancelEdit = true;
                return;
            }
            var dir = _path;
            var newName = e.Label.Trim();
            RunAsync(() => _client.RenameFile(f.FullName, Combine(dir, newName)), () => Navigate(dir));
        }

        /// <summary>
        /// 목록에서 탐색기로 끌어 놓기. 탐색기에는 로컬 파일 경로(FileDrop)만 줄 수 있으므로 임시 폴더에 먼저 받는다.
        /// 받는 동안은 일반 전송처럼 작업 스레드에서 진행률/취소를 보이고(UI 는 멈추지 않음),
        /// 다 받았을 때 마우스 단추를 아직 누르고 있으면 그대로 끌기를 시작한다.
        /// 이미 놓았다면 받아 둔 파일을 기억해 두었다가 다시 끌 때 바로 넘긴다.
        /// </summary>
        private void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            var entries = SelectedEntries().Where(f => !f.IsDirectory).ToList();
            if (entries.Count == 0 || _client == null)
                return;

            // 이미 받아 둔 파일만이면 바로 끌기 (전송 중이어도 가능)
            var cached = entries.Select(CachedDragPath).ToList();
            if (cached.All(p => p != null))
            {
                StartDrag(cached);
                return;
            }
            if (TransferBusy())
                return;
            var missing = entries.Where(f => CachedDragPath(f) == null).ToList();
            if (missing.Sum(f => f.Length) > DragLimit)
            {
                SetStatus($"끌어 놓기는 {FormatSize(DragLimit)}까지입니다. 큰 파일은 '다운로드'를 사용하세요.");
                return;
            }

            var temp = Path.Combine(Path.GetTempPath(), "lish-drag-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var ready = false;
            RunTransfer("끌어 놓기 준비", job =>
            {
                Directory.CreateDirectory(temp);
                lock (_dragDirs)
                    _dragDirs.Add(temp);
                job.SetTotal(missing.Count, missing.Sum(f => f.Length));
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in missing)
                {
                    // 다른 폴더의 같은 이름 파일이 섞여 있으면 하위 폴더로 나눈다 (탐색기에서는 원래 이름으로 보이게)
                    var dir = temp;
                    var name = SafeName(f.Name);
                    for (var n = 1; !used.Add(Path.Combine(dir, name)); n++)
                    {
                        dir = Path.Combine(temp, n.ToString());
                        Directory.CreateDirectory(dir);
                    }
                    var local = Path.Combine(dir, name);
                    DownloadOne(job, f, local);
                    lock (_dragCache)
                        _dragCache[DragKey(f)] = local;
                }
                ready = true;
            }, () =>
            {
                if (!ready || IsDisposed)
                    return;
                var paths = entries.Select(CachedDragPath).ToList();
                if (paths.Any(p => p == null))
                    return;
                if ((MouseButtons & MouseButtons.Left) != 0)
                    StartDrag(paths);
                else
                    SetStatus($"끌어 놓기 준비 완료 ({paths.Count}개) · 다시 끌어서 놓으면 바로 복사됩니다.");
            });
        }

        private void StartDrag(List<string> paths)
        {
            _draggingOut = true;
            try
            {
                var effect = DoDragDrop(new DataObject(DataFormats.FileDrop, paths.ToArray()), DragDropEffects.Copy);
                if (effect != DragDropEffects.None)
                    SetStatus($"{paths.Count}개 파일을 끌어 놓았습니다.");
            }
            catch (Exception ex)
            {
                SetError("끌어 놓기 실패: " + ex.Message);
            }
            finally
            {
                _draggingOut = false;
            }
        }

        private static string DragKey(ISftpFile f) => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";

        /// <summary>받아 둔 임시 파일이 그대로 있으면 경로, 아니면 null</summary>
        private string CachedDragPath(ISftpFile f)
        {
            string local;
            lock (_dragCache)
                if (!_dragCache.TryGetValue(DragKey(f), out local))
                    return null;
            try
            {
                if (File.Exists(local) && new FileInfo(local).Length == f.Length)
                    return local;
            }
            catch (Exception)
            {
                // 접근할 수 없으면 다시 받는다
            }
            lock (_dragCache)
                _dragCache.Remove(DragKey(f));
            return null;
        }

        // ───────────── 비동기 실행 ─────────────

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetStatus(text)));
                return;
            }
            ReleaseHold();
            _status.ForeColor = Theme.SubText;
            _status.Text = text;
        }

        private void SetError(string text)
        {
            if (IsDisposed) return;
            ReleaseHold();
            _status.ForeColor = Theme.Danger;
            _status.Text = text;
        }

        /// <summary>
        /// 방금 보인 전송 결과를 유지한다. timed 면 몇 초 뒤 목록 문구로 돌아가고,
        /// 아니면(실패/취소) 사용자가 다음 작업을 할 때까지 남긴다.
        /// </summary>
        private void HoldResult(bool timed)
        {
            _holdTimer.Stop();
            _holdResult = true;
            _listText = null;
            if (timed)
                _holdTimer.Start();
        }

        private void ReleaseHold()
        {
            _holdTimer.Stop();
            _holdResult = false;
            _listText = null;
        }

        private void RunAsync(Action work, Action onDone, Action always = null)
        {
            Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    work();
                    return (Exception)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
                finally
                {
                    _gate.Release();
                }
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                always?.Invoke();
                if (t.Result != null)
                    SetError(t.Result.Message);
                else
                    onDone?.Invoke();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ───────────── 전송 (진행률/취소) ─────────────

        private bool TransferBusy()
        {
            if (_job == null)
                return false;
            SetStatus("다른 전송이 진행 중입니다. 끝나거나 취소한 뒤 다시 시도하세요.");
            return true;
        }

        private void CancelTransfer()
        {
            var job = _job;
            if (job == null || job.Cts.IsCancellationRequested)
                return;
            job.Cts.Cancel();
            _cancelButton.Enabled = false;
            ShowProgress();
        }

        /// <summary>
        /// 전송을 작업 스레드에서 실행한다. 진행률은 UI 타이머가 job 을 읽어 표시하고,
        /// 취소/실패 시 전송 중이던 파일 하나(받다 만 로컬 파일 또는 올리다 만 원격 파일)는 지운다. 끝난 파일은 그대로 둔다.
        /// </summary>
        private void RunTransfer(string label, Action<TransferJob> work, Action onDone, string openPath = null)
        {
            var job = new TransferJob(label);
            _job = job;
            _progress.Value = 0;
            _progress.Visible = true;
            _cancelButton.Enabled = true;
            _cancelButton.Visible = true;
            SetStatus(label + " 준비 중...");
            _ticker.Start();

            Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    job.Token.ThrowIfCancellationRequested();
                    work(job);
                    return ((Exception)null, (string)null);
                }
                catch (Exception ex)
                {
                    return (ex, RemovePartial(job));
                }
                finally
                {
                    _gate.Release();
                }
            }).ContinueWith(t =>
            {
                if (_job == job)
                    _job = null;
                if (IsDisposed) return;
                _ticker.Stop();
                _progress.Visible = false;
                _cancelButton.Visible = false;
                var (error, partial) = t.Result;
                var count = job.FileCount > 0 ? $" ({job.FilesDone}/{job.FileCount}개, {FormatSize(job.Bytes)})" : "";
                if (error == null)
                    SetStatus($"{label} 완료{count}" + (openPath != null ? $" → {openPath}" : ""));
                else if (job.Cts.IsCancellationRequested)
                    SetStatus($"{label} 취소됨{count}" + (partial ?? ""));
                else
                    SetError($"{label} 실패: {error.Message}" + (partial ?? ""));
                // 이어지는 목록 새로 고침이 결과 문구를 덮지 않게 (성공은 몇 초, 취소/실패는 다음 작업까지)
                HoldResult(error == null);
                onDone?.Invoke();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>전송 중이던 파일을 지운다. 결과를 상태 표시줄 문구로 돌려준다 (작업 스레드에서 호출).</summary>
        private string RemovePartial(TransferJob job)
        {
            var path = job.PartialPath;
            if (path == null || !job.PartialStarted)
                return null;
            try
            {
                if (job.PartialRemote)
                    _client?.DeleteFile(path);
                else
                    File.Delete(path);
                return $" · 전송 중이던 '{job.FileName}' 은(는) 삭제함";
            }
            catch (Exception)
            {
                return $" · 불완전한 파일이 남음: {path}";
            }
        }

        private void ShowProgress()
        {
            var job = _job;
            if (job == null || IsDisposed)
                return;
            _status.ForeColor = Theme.SubText;
            var prefix = job.Cts.IsCancellationRequested ? "취소하는 중... " : "";
            if (job.FileCount == 0)
            {
                _status.Text = $"{prefix}{job.Label} · 파일 목록 확인 중...";
                return;
            }
            var total = job.Total;
            var bytes = job.Bytes;
            var pct = total > 0 ? (int)Math.Min(100, bytes * 100 / total) : job.FilesDone * 100 / job.FileCount;
            _progress.Value = Math.Max(0, Math.Min(100, pct));
            var secs = job.Clock.Elapsed.TotalSeconds;
            var rate = secs >= 1 ? $" · {FormatSize((long)(bytes / secs))}/s" : "";
            _status.Text = $"{prefix}{job.Label} {Math.Max(1, job.FileIndex)}/{job.FileCount} · {job.FileName} · "
                           + $"{FormatSize(bytes)} / {FormatSize(total)} ({pct}%){rate}";
        }

        /// <summary>전송 하나의 진행 상태. 작업 스레드가 쓰고 UI 타이머가 읽는다.</summary>
        private sealed class TransferJob
        {
            private long _done, _current, _total;
            private int _fileCount, _fileIndex, _filesDone;

            public TransferJob(string label) => Label = label;

            public string Label { get; }
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
            public CancellationToken Token => Cts.Token;
            public Stopwatch Clock { get; } = Stopwatch.StartNew();

            public long Total => Interlocked.Read(ref _total);
            /// <summary>끝난 파일 + 지금 파일의 전송 바이트</summary>
            public long Bytes => Interlocked.Read(ref _done) + Interlocked.Read(ref _current);
            public int FileCount => Volatile.Read(ref _fileCount);
            public int FileIndex => Volatile.Read(ref _fileIndex);
            public int FilesDone => Volatile.Read(ref _filesDone);
            public volatile string FileName;

            // 취소/실패 시 정리할 '전송 중이던 파일'
            public volatile string PartialPath;
            public volatile bool PartialRemote;
            public volatile bool PartialStarted;

            public void SetTotal(int files, long bytes)
            {
                Interlocked.Exchange(ref _total, bytes);
                Volatile.Write(ref _fileCount, files);
            }

            public void BeginFile(string name, string partialPath, bool remote)
            {
                Interlocked.Exchange(ref _current, 0);
                FileName = name;
                PartialRemote = remote;
                PartialStarted = false;
                PartialPath = partialPath;
                Interlocked.Increment(ref _fileIndex);
            }

            public void Progress(long bytesInFile) => Interlocked.Exchange(ref _current, bytesInFile);

            public void EndFile(long length)
            {
                PartialPath = null;
                // 전송 중 파일 크기가 바뀌었을 수 있으므로 실제 보낸 양과 큰 쪽을 더한다
                Interlocked.Add(ref _done, Math.Max(length, Interlocked.Exchange(ref _current, 0)));
                Interlocked.Increment(ref _filesDone);
            }
        }

        /// <summary>
        /// SSH.NET 이 청크마다 부르는 Read/Write 에서 취소 여부를 확인하는 스트림 래퍼.
        /// 취소되면 OperationCanceledException 으로 전송 루프를 빠져나오게 한다.
        /// </summary>
        private sealed class CancelableStream : Stream
        {
            private readonly Stream _inner;
            private readonly TransferJob _job;

            public CancelableStream(Stream inner, TransferJob job)
            {
                _inner = inner;
                _job = job;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _job.Token.ThrowIfCancellationRequested();
                _job.PartialStarted = true;
                return _inner.Read(buffer, offset, count);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _job.Token.ThrowIfCancellationRequested();
                _job.PartialStarted = true;
                _inner.Write(buffer, offset, count);
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated)
                EnsureConnected();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _job?.Cts.Cancel();
                _ticker.Dispose();
                _holdTimer.Dispose();
                _dropLeaveTimer.Dispose();
                _tips.Dispose();
                // 끌어 놓기용 임시 폴더 정리 (탐색기가 아직 복사 중이면 잠겨 있어 남을 수 있음)
                string[] dirs;
                lock (_dragDirs)
                    dirs = _dragDirs.ToArray();
                foreach (var d in dirs)
                    try { Directory.Delete(d, true); } catch { /* 무시 */ }
                var c = _client;
                _client = null;
                Task.Run(() => { try { c?.Dispose(); } catch { /* 무시 */ } });
                _icons.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
