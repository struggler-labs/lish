using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    /// <summary>세션 추가/편집.</summary>
    internal sealed class SessionDialog : StyledDialog
    {
        private readonly ProfileStore _store;
        private readonly HostProfile _original;
        private readonly TableLayoutPanel _grid;

        private readonly InputBox _name, _host, _port, _user, _password, _key, _proxy, _term, _keepAlive, _forwards, _socks, _note;
        private readonly ComboField _group, _jump, _encoding;
        private readonly List<string> _folders;   // _group 항목 순서대로 (0 = 최상위 null)
        private readonly Toggle _savePassword;
        private readonly Label _error;

        public HostProfile Result { get; private set; }
        public bool ConnectAfterSave { get; private set; }

        public SessionDialog(ProfileStore store, HostProfile existing, string defaultGroup = null)
            : base(existing == null ? "새 세션" : "세션 편집", "접속 정보는 이 PC의 사용자 프로필에만 저장됩니다.")
        {
            _store = store;
            _original = existing;
            ClientSize = new Size(Theme.Scale(600), Theme.Scale(680));

            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            _grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = Color.White,
                Padding = new Padding(0, 0, Theme.Scale(8), 0),
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(128)));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroller.Controls.Add(_grid);
            Body.Controls.Add(scroller);

            var p = existing?.Clone() ?? new HostProfile { Group = defaultGroup };

            Section("연결");
            _name = Field("세션 이름", new InputBox("예: web-01"));
            // 폴더는 세션 목록에서 만들고, 여기서는 고르기만 한다
            _group = Field("폴더", new ComboField(false));
            _folders = new[] { (string)null }.Concat(store.Folders).ToList();
            if (p.Group != null && !_folders.Contains(p.Group, StringComparer.OrdinalIgnoreCase))
                _folders.Add(p.Group);
            _group.Combo.Items.AddRange(_folders.Select(f => (object)(f == null ? "(최상위)" : FolderPath.Display(f))).ToArray());
            _host = Field("호스트", new InputBox("IP 또는 도메인 (user@host:port 도 가능)"));
            _port = Field("포트", new InputBox("22"));
            _user = Field("사용자", new InputBox("로그인 계정"));

            Section("인증");
            _password = Field("비밀번호", new InputBox(string.IsNullOrEmpty(p.SavedPassword) ? "접속 시 입력 (선택)" : "저장됨 · 변경하려면 입력", Theme.IconLock, true));
            _savePassword = Field("", new Toggle("비밀번호 저장 (Windows DPAPI 암호화)") { Checked = !string.IsNullOrEmpty(p.SavedPassword) });
            var keyRow = new Panel { Height = Theme.Scale(32), BackColor = Color.White };
            _key = new InputBox("개인키 파일 (미지정 시 ~/.ssh/id_* 자동)", Theme.IconKey) { Dock = DockStyle.Fill };
            var browse = new FlatButton("찾아보기", ButtonKind.Secondary) { Dock = DockStyle.Right };
            browse.Click += (s, e) => BrowseKey();
            keyRow.Controls.Add(_key);
            keyRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.Scale(6), BackColor = Color.White });
            keyRow.Controls.Add(browse);
            Field("개인키", keyRow);

            Section("네트워크");
            _jump = Field("점프 호스트", new ComboField(true));
            _jump.Combo.Items.Add("");
            _jump.Combo.Items.AddRange(store.All.Where(x => existing == null || x.Name != existing.Name).Select(x => (object)x.Name).ToArray());
            Hint("배스천 서버를 경유합니다. 저장된 세션 이름 또는 user@host:port");
            _proxy = Field("프록시", new InputBox("http://proxy:8080 · socks5://host:1080"));

            Section("터미널");
            _encoding = Field("문자셋", new ComboField(true));
            _encoding.Combo.Items.AddRange(new object[] { "UTF-8", "CP949", "EUC-KR" });
            _term = Field("터미널 타입", new InputBox("xterm-256color"));
            _keepAlive = Field("Keepalive (초)", new InputBox("30 · 0 = 끔"));

            Section("포트 포워딩");
            _forwards = Field("로컬 (-L)", new InputBox("8080:10.0.0.5:80  (한 줄에 하나)") { Height = Theme.Scale(64) });
            _forwards.Multiline = true;
            _socks = Field("SOCKS (-D)", new InputBox("1080  (콤마로 여러 개)"));

            Section("메모");
            _note = Field("설명", new InputBox("선택"));

            _name.Text = p.Name ?? "";
            _group.Combo.SelectedIndex = Math.Max(0, _folders.FindIndex(f => string.Equals(f, p.Group, StringComparison.OrdinalIgnoreCase)));
            _host.Text = p.Host ?? "";
            _port.Text = p.Port.ToString();
            _user.Text = p.User ?? "";
            _key.Text = p.KeyFile ?? "";
            _jump.Text = p.Jump ?? "";
            _proxy.Text = p.Proxy ?? "";
            _encoding.Text = string.IsNullOrEmpty(p.Encoding) ? "UTF-8" : p.Encoding.ToUpperInvariant();
            _term.Text = p.Term ?? "";
            _keepAlive.Text = p.KeepAlive.ToString();
            _forwards.Text = string.Join("\r\n", p.LocalForwardList);
            _socks.Text = string.Join(", ", p.DynamicForwardList);
            _note.Text = p.Note ?? "";

            _error = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Danger, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, Width = Theme.Scale(220), Height = Theme.Scale(32) };

            var connect = AddButton("저장 후 접속", ButtonKind.Primary, DialogResult.None);
            var save = AddButton("저장", ButtonKind.Secondary, DialogResult.None);
            AddButton("취소", ButtonKind.Ghost, DialogResult.Cancel);
            Buttons.Controls.Add(_error);
            connect.Click += (s, e) => Commit(true);
            save.Click += (s, e) => Commit(false);
            AcceptButton = connect;

            Shown += (s, e) => (existing == null ? _name.Box : _host.Box).Focus();
        }

        // TableLayoutPanel 의 RowStyles 는 행 인덱스와 1:1 로 매칭되므로 모든 행(섹션·필드·힌트)이 반드시 RowStyle 을 하나씩 추가해야 함
        private void Section(string title)
        {
            var first = _grid.RowCount == 0;
            var l = new Label
            {
                Text = title,
                Font = Theme.Heading,
                ForeColor = Theme.Deep,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0, first ? 0 : Theme.Scale(12), 0, Theme.Scale(4)),
            };
            _grid.Controls.Add(l, 0, _grid.RowCount);
            _grid.SetColumnSpan(l, 2);
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(26) + l.Margin.Vertical));
            _grid.RowCount++;
        }

        private T Field<T>(string label, T control) where T : Control
        {
            var l = new Label
            {
                Text = label,
                ForeColor = Theme.SubText,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, Theme.Scale(3), 0, Theme.Scale(3)),
            };
            var h = control.Height; // 그리드에 넣으면 Dock=Fill 로 셀 크기에 맞춰지므로 미리 읽어 둠
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, Theme.Scale(3), 0, Theme.Scale(3));
            _grid.Controls.Add(l, 0, _grid.RowCount);
            _grid.Controls.Add(control, 1, _grid.RowCount);
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h + Theme.Scale(6)));
            _grid.RowCount++;
            return control;
        }

        private void Hint(string text)
        {
            var l = new Label { Text = text, ForeColor = Theme.Faint, Font = Theme.Small, AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.Scale(4)) };
            _grid.Controls.Add(l, 1, _grid.RowCount);
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(18) + l.Margin.Vertical));
            _grid.RowCount++;
        }

        private void BrowseKey()
        {
            using (var d = new OpenFileDialog
            {
                Title = "개인키 파일 선택",
                Filter = "개인키 (*.pem;*.key;*.ppk;id_*)|*.pem;*.key;*.ppk;id_*|모든 파일 (*.*)|*.*",
                InitialDirectory = System.IO.Directory.Exists(Paths.UserSshDir) ? Paths.UserSshDir : null,
            })
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                    _key.Text = d.FileName;
            }
        }

        private void Fail(string message, Control focus)
        {
            _error.Text = message;
            focus?.Focus();
        }

        private void Commit(bool connect)
        {
            var name = _name.Text.Trim();
            var hostText = _host.Text.Trim();

            HostProfile parsed;
            try
            {
                parsed = Address.Parse(hostText);
            }
            catch (FormatException ex)
            {
                Fail(ex.Message, _host.Box);
                return;
            }
            if (string.IsNullOrEmpty(name))
                name = parsed.Host;
            if ((_original == null || !string.Equals(_original.Name, name, StringComparison.OrdinalIgnoreCase)) && _store.Find(name) != null)
            {
                Fail($"'{name}' 이름의 세션이 이미 있습니다.", _name.Box);
                return;
            }

            var p = _original?.Clone() ?? new HostProfile();
            p.Name = name;
            p.Group = _folders[Math.Max(0, _group.Combo.SelectedIndex)];
            p.Host = parsed.Host;
            p.Port = parsed.Port;
            if (hostText.IndexOf(':') < 0 || hostText.StartsWith("["))
            {
                if (!int.TryParse(_port.Text.Trim(), out var port) || port < 1 || port > 65535)
                {
                    Fail("포트는 1~65535 사이여야 합니다.", _port.Box);
                    return;
                }
                if (parsed.Port == 22)
                    p.Port = port;
            }
            p.User = parsed.User ?? (string.IsNullOrWhiteSpace(_user.Text) ? null : _user.Text.Trim());
            p.KeyFile = Empty(_key.Text);
            p.Jump = Empty(_jump.Text);
            p.Proxy = Empty(_proxy.Text);
            p.Term = Empty(_term.Text);
            p.Note = Empty(_note.Text);

            var enc = _encoding.Text.Trim();
            try
            {
                HostProfile.ResolveEncoding(enc);
            }
            catch (ArgumentException)
            {
                Fail($"알 수 없는 문자셋: {enc}", _encoding.Combo);
                return;
            }
            p.Encoding = enc.Length == 0 || enc.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) ? null : enc.ToLowerInvariant();

            if (!int.TryParse(_keepAlive.Text.Trim(), out var ka) || ka < 0 || ka > 3600)
            {
                Fail("Keepalive 는 0~3600 초입니다.", _keepAlive.Box);
                return;
            }
            p.KeepAlive = ka;

            try
            {
                p.LocalForwards = _forwards.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                foreach (var f in p.LocalForwardList)
                    SshSession.ParseLocalForward(f).Dispose();
                p.DynamicForwards = _socks.Text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                foreach (var f in p.DynamicForwardList)
                    SshSession.ParseDynamicForward(f).Dispose();
            }
            catch (FormatException ex)
            {
                Fail(ex.Message, _forwards.Box);
                return;
            }
            if (p.LocalForwardList.Count == 0) p.LocalForwards = null;
            if (p.DynamicForwardList.Count == 0) p.DynamicForwards = null;

            if (!_savePassword.Checked)
                p.SavedPassword = null;
            else if (_password.Text.Length > 0)
                p.SavedPassword = Secret.Protect(_password.Text);

            if (_original != null && !string.Equals(_original.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                _store.Remove(_original.Name);
            _store.Upsert(p);
            _store.Save();

            Result = p;
            ConnectAfterSave = connect;
            DialogResult = DialogResult.OK;
        }

        private static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
