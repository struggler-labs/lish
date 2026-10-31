using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lish.Core;
using Lish.Terminal;
using Renci.SshNet;

namespace Lish.Ui
{
    internal enum SessionState { Idle, Connecting, Connected, Disconnected }

    /// <summary>탭 하나: 터미널 + SSH 세션 수명 관리. SFTP 패널은 사이드바 SFTP 탭이 이 탭용으로 하나 만들어 보관한다.</summary>
    internal sealed class SessionView : UserControl
    {
        private readonly object _sendLock = new object();

        private SshSession _session;
        private ShellStream _shell;
        private Encoding _encoding;
        private volatile bool _closing;
        private SftpPanel _sftp;
        private volatile string _shellCwd;
        private string _shellCwdHost;

        public HostProfile Profile { get; }
        public TerminalControl Term { get; }
        public SessionState State { get; private set; } = SessionState.Idle;
        public string RemoteTitle { get; private set; }

        /// <summary>
        /// 셸이 OSC 7 로 알린 현재 폴더 (모르면 null). 서버에 명령을 보내지 않고 셸이 터미널에 출력한 값만 쓴다.
        /// 아무 스레드에서나 읽어도 된다.
        /// </summary>
        public string ShellCwd => _shellCwd;
        /// <summary>ShellCwd 가 바뀌었을 때 (UI 스레드)</summary>
        public event EventHandler ShellCwdChanged;
        /// <summary>SFTP '터미널 폴더 따라가기' 가 마지막으로 따라간 셸 폴더 (UI 스레드)</summary>
        public string FollowedCwd { get; set; }
        /// <summary>이 탭의 SFTP 패널 (아직 안 열었으면 null)</summary>
        public SftpPanel Sftp => _sftp;

        public event EventHandler StateChanged;

        public SessionView(HostProfile profile, AppSettings settings)
        {
            Profile = profile;
            BackColor = Theme.TerminalBg;

            Term = new TerminalControl(settings) { Dock = DockStyle.Fill };
            Term.Input += Send;
            Term.TerminalResized += OnTerminalResized;
            Term.TitleChanged += t =>
            {
                RemoteTitle = t;
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            Term.CwdChanged += OnShellCwd;

            Controls.Add(Term);
        }

        public void ApplySettings(AppSettings settings) => Term.ApplySettings(settings);

        // ───────────── 연결 ─────────────

        public void Connect()
        {
            if (State == SessionState.Connecting || State == SessionState.Connected)
                return;
            SetState(SessionState.Connecting);
            _encoding = Profile.GetEncoding();
            WriteInfo($"\x1b[38;2;167;139;250m●\x1b[0m {Profile.Display()} 접속 중...");

            var ui = new UiInteraction(this, WriteInfo);
            var profile = Profile.Clone();
            var thread = new Thread(() => Run(profile, ui)) { IsBackground = true, Name = "lish-session" };
            thread.Start();
        }

        private void Run(HostProfile profile, IUserInteraction ui)
        {
            Exception error = null;
            try
            {
                var session = SshSession.Open(profile, ui);
                // 비밀번호 입력 등으로 사용자 이름이 정해졌으면 반영
                Profile.User = profile.User;
                _session = session;
                var shell = session.Client.CreateShellStream(profile.TermOrDefault, (uint)Term.Columns, (uint)Term.RowsCount, 0, 0, 64 * 1024);
                _shell = shell;
                SetState(SessionState.Connected);
                ReadLoop(shell);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Cleanup();
                if (!_closing)
                {
                    if (error != null)
                        WriteInfo($"\x1b[38;2;240;87;122m✕ {Describe(error)}\x1b[0m");
                    WriteInfo("\x1b[38;2;118;113;146m── 연결 종료 · Enter 키를 누르면 다시 접속합니다 ──\x1b[0m");
                    SetState(SessionState.Disconnected);
                }
            }
        }

        private static string Describe(Exception ex)
        {
            switch (ex)
            {
                case Renci.SshNet.Common.SshAuthenticationException _: return "인증 실패: " + ex.Message;
                case Renci.SshNet.Common.SshOperationTimeoutException _: return "연결 시간 초과: " + ex.Message;
                case System.Net.Sockets.SocketException se: return $"네트워크 오류 ({se.SocketErrorCode}): {se.Message}";
                case OperationCanceledException _: return "접속이 취소되었습니다.";
                default: return ex.Message;
            }
        }

        private void ReadLoop(ShellStream shell)
        {
            var buffer = new byte[32 * 1024];
            var decoder = _encoding.GetDecoder();
            var chars = new char[_encoding.GetMaxCharCount(buffer.Length) + 2];
            while (!_closing)
            {
                int n;
                try
                {
                    n = shell.Read(buffer, 0, buffer.Length);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                if (n <= 0)
                    break;
                var count = decoder.GetChars(buffer, 0, n, chars, 0, false);
                if (count > 0)
                    Term.Feed(chars, count);
            }
        }

        private void Cleanup()
        {
            var shell = _shell;
            var session = _session;
            _shell = null;
            _session = null;
            try { shell?.Dispose(); } catch { /* 이미 닫힘 */ }
            try { session?.Dispose(); } catch { /* 이미 닫힘 */ }
        }

        private void Send(string text)
        {
            if (State == SessionState.Disconnected)
            {
                if (text.IndexOf('\r') >= 0 && IsHandleCreated)
                    BeginInvoke((Action)Connect);
                return;
            }
            var shell = _shell;
            if (shell == null || State != SessionState.Connected)
                return;
            var bytes = _encoding.GetBytes(text);
            lock (_sendLock)
            {
                try
                {
                    shell.Write(bytes, 0, bytes.Length);
                    shell.Flush();
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException || ex is Renci.SshNet.Common.SshException)
                {
                    // 연결이 끊기는 중 — ReadLoop 가 정리한다.
                }
            }
        }

        private void OnTerminalResized(int cols, int rows)
        {
            var shell = _shell;
            if (shell == null)
                return;
            Task.Run(() =>
            {
                try { shell.ChangeWindowSize((uint)cols, (uint)rows, 0, 0); } catch { /* 채널 종료 중 */ }
            });
        }

        private void WriteInfo(string message) => Term.Feed("\r\n" + message.Replace("\n", "\r\n") + "\r\n");

        private void SetState(SessionState state)
        {
            State = state;
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (state != SessionState.Connected)
                        {
                            FollowedCwd = null;
                            _shellCwd = null;
                            _shellCwdHost = null;
                        }
                        StateChanged?.Invoke(this, EventArgs.Empty);
                        if (state == SessionState.Connected && _sftp != null)
                            _sftp.Reconnect();
                        else if (state == SessionState.Disconnected)
                            _sftp?.Shutdown();
                    }));
                }
                catch (InvalidOperationException)
                {
                    // 폼 종료 중
                }
            }
        }

        public void Disconnect()
        {
            _closing = true;
            Task.Run(Cleanup);
        }

        // ───────────── SFTP ─────────────

        /// <summary>이 탭의 SFTP 패널. 처음 부를 때 만들고 탭이 닫힐 때까지 유지한다 (탭을 오가도 폴더/상태 유지).</summary>
        public SftpPanel GetOrCreateSftp()
        {
            if (_sftp == null || _sftp.IsDisposed)
                _sftp = new SftpPanel(OpenSftp, Profile.GetEncoding(), () => _shellCwd) { Dock = DockStyle.Fill };
            return _sftp;
        }

        /// <summary>
        /// OSC 7 수신. 처음 알려 온 호스트를 이 연결의 셸로 보고, 다른 호스트 이름은 무시한다
        /// (터미널 안에서 다시 ssh 로 들어간 서버의 폴더를 이 서버 SFTP 에서 따라가지 않도록).
        /// </summary>
        private void OnShellCwd(string host, string path)
        {
            if (State != SessionState.Connected)
                return;
            if (_shellCwdHost == null)
                _shellCwdHost = host;
            else if (!string.Equals(_shellCwdHost, host, StringComparison.OrdinalIgnoreCase))
                return;
            if (path == _shellCwd)
                return;
            _shellCwd = path;
            ShellCwdChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>테스트 훅: 터미널에 입력한 것처럼 보낸다</summary>
        public void TestType(string text) => Send(text);

        private SftpClient OpenSftp()
        {
            var session = _session;
            if (session == null || State != SessionState.Connected)
                throw new InvalidOperationException("SSH 연결이 필요합니다. 먼저 접속하세요.");
            return session.OpenSftp();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                _sftp?.Dispose();
                Task.Run(Cleanup);
            }
            base.Dispose(disposing);
        }
    }
}
