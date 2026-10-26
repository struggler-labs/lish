using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Lish.Core
{
    /// <summary>접속 중 사용자에게 묻거나 알려야 할 일. GUI 스레드로 마샬링하는 구현을 넘긴다.</summary>
    internal interface IUserInteraction
    {
        bool ConfirmNewHostKey(string host, string keyType, string fingerprint);
        void ShowHostKeyChanged(string host, string keyType, string fingerprint);
        /// <summary>null = 취소</summary>
        string Prompt(string title, string message, bool secret);
        void Status(string message);
    }

    /// <summary>점프 호스트 체인과 포워딩을 포함한 하나의 SSH 연결.</summary>
    internal sealed class SshSession : IDisposable
    {
        private const int MaxJumpDepth = 5;

        public SshClient Client { get; private set; }
        public HostProfile Profile { get; private set; }

        private SshSession _jump;
        private IUserInteraction _ui;
        private string _connectHost;
        private int _connectPort;
        private string _password;
        private List<IPrivateKeySource> _keys;
        private readonly List<ForwardedPort> _forwards = new List<ForwardedPort>();

        public static SshSession Open(HostProfile profile, ProfileStore store, IUserInteraction ui) => Open(profile, store, ui, 0);

        private static SshSession Open(HostProfile profile, ProfileStore store, IUserInteraction ui, int depth)
        {
            if (depth > MaxJumpDepth)
                throw new InvalidOperationException("점프 호스트 체인이 너무 깁니다(순환 참조?).");

            var session = new SshSession { Profile = profile, _ui = ui };
            try
            {
                session._connectHost = profile.Host;
                session._connectPort = profile.Port;

                if (!string.IsNullOrEmpty(profile.Jump))
                {
                    var jumpProfile = Address.ResolveDestination(profile.Jump, store);
                    // 프록시는 첫 번째 홉에만 적용된다.
                    if (string.IsNullOrEmpty(jumpProfile.Proxy))
                        jumpProfile.Proxy = profile.Proxy;
                    ui.Status($"점프 호스트 {jumpProfile.Display()} 경유...");
                    session._jump = Open(jumpProfile, store, ui, depth + 1);

                    var tunnel = new ForwardedPortLocal("127.0.0.1", 0, profile.Host, (uint)profile.Port);
                    session._jump.Client.AddForwardedPort(tunnel);
                    tunnel.Start();
                    session._connectHost = "127.0.0.1";
                    session._connectPort = (int)tunnel.BoundPort;
                }

                session.Client = session.Connect();
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public void StartForwards()
        {
            foreach (var spec in Profile.LocalForwardList)
            {
                var fwd = ParseLocalForward(spec);
                Client.AddForwardedPort(fwd);
                fwd.Exception += (s, e) => _ui.Status($"[포워딩 {spec}] {e.Exception.Message}");
                fwd.Start();
                _forwards.Add(fwd);
                _ui.Status($"로컬 포워딩: {fwd.BoundHost}:{fwd.BoundPort} → {fwd.Host}:{fwd.Port}");
            }
            foreach (var spec in Profile.DynamicForwardList)
            {
                var fwd = ParseDynamicForward(spec);
                Client.AddForwardedPort(fwd);
                fwd.Exception += (s, e) => _ui.Status($"[SOCKS {spec}] {e.Exception.Message}");
                fwd.Start();
                _forwards.Add(fwd);
                _ui.Status($"SOCKS 프록시: {fwd.BoundHost}:{fwd.BoundPort}");
            }
        }

        /// <summary>같은 경로(점프 터널 포함)와 이미 확인된 자격증명으로 SFTP 채널용 연결을 연다.</summary>
        public SftpClient OpenSftp()
        {
            while (true)
            {
                var info = CreateConnectionInfo(Profile.User, BuildMethods(Profile.User, _keys.Count > 0, _password));
                var sftp = new SftpClient(info);
                var hostKey = AttachHostKeyCheck(sftp);
                if (Profile.KeepAlive > 0)
                    sftp.KeepAliveInterval = TimeSpan.FromSeconds(Profile.KeepAlive);
                try
                {
                    sftp.Connect();
                    return sftp;
                }
                catch (Exception) when (hostKey.Status != HostKeyStatus.Trusted)
                {
                    sftp.Dispose();
                    ResolveRejectedHostKey(hostKey);
                }
                catch
                {
                    sftp.Dispose();
                    throw;
                }
            }
        }

        /// <summary>[bind:]lport:host:hport</summary>
        public static ForwardedPortLocal ParseLocalForward(string spec)
        {
            var p = spec.Split(':');
            if (p.Length == 3)
                return new ForwardedPortLocal("127.0.0.1", ParsePort(p[0]), p[1], ParsePort(p[2]));
            if (p.Length == 4)
                return new ForwardedPortLocal(p[0].Length == 0 || p[0] == "*" ? "0.0.0.0" : p[0], ParsePort(p[1]), p[2], ParsePort(p[3]));
            throw new FormatException($"잘못된 로컬 포워딩 형식: {spec}  (예: 8080:10.0.0.5:80)");
        }

        /// <summary>[bind:]port</summary>
        public static ForwardedPortDynamic ParseDynamicForward(string spec)
        {
            var p = spec.Split(':');
            if (p.Length == 1)
                return new ForwardedPortDynamic("127.0.0.1", ParsePort(p[0]));
            if (p.Length == 2)
                return new ForwardedPortDynamic(p[0].Length == 0 || p[0] == "*" ? "0.0.0.0" : p[0], ParsePort(p[1]));
            throw new FormatException($"잘못된 SOCKS 포트 형식: {spec}  (예: 1080)");
        }

        private static uint ParsePort(string s)
        {
            if (!uint.TryParse(s, out var port) || port > 65535)
                throw new FormatException($"잘못된 포트: {s}");
            return port;
        }

        private sealed class HostKeyCheck
        {
            public HostKeyStatus Status = HostKeyStatus.Trusted;
            public string KeyType;
            public byte[] Key;
        }

        /// <summary>
        /// 키 교환 중에는 known_hosts 만 확인하고 즉시 판정한다. 사용자에게 묻는 동안
        /// ConnectionInfo.Timeout 이 흘러 연결이 끊기므로, 확인은 연결 실패 후
        /// <see cref="ResolveRejectedHostKey"/> 에서 하고 재접속한다.
        /// </summary>
        private HostKeyCheck AttachHostKeyCheck(BaseClient client)
        {
            var check = new HostKeyCheck();
            client.HostKeyReceived += (s, e) =>
            {
                // 점프 터널 경유 시에도 실제 대상 호스트 이름으로 검증한다.
                check.KeyType = e.HostKeyName;
                check.Key = e.HostKey;
                check.Status = KnownHosts.Check(Profile.Host, Profile.Port, e.HostKeyName, e.HostKey);
                e.CanTrust = check.Status == HostKeyStatus.Trusted;
            };
            return check;
        }

        /// <summary>새 키를 사용자가 승인하면 known_hosts 에 추가하고 반환(재접속), 아니면 예외.</summary>
        private void ResolveRejectedHostKey(HostKeyCheck check)
        {
            var token = KnownHosts.HostToken(Profile.Host, Profile.Port);
            var fingerprint = KnownHosts.Fingerprint(check.Key);
            if (check.Status == HostKeyStatus.Changed)
            {
                _ui.ShowHostKeyChanged(token, check.KeyType, fingerprint);
                throw new SshConnectionException("호스트 키가 변경되어 연결을 중단했습니다.");
            }
            if (!_ui.ConfirmNewHostKey(token, check.KeyType, fingerprint))
                throw new SshConnectionException("호스트 키를 신뢰하지 않아 연결을 중단했습니다.");
            KnownHosts.Add(Profile.Host, Profile.Port, check.KeyType, check.Key);
        }

        private SshClient Connect()
        {
            var p = Profile;
            if (string.IsNullOrEmpty(p.User))
            {
                var user = _ui.Prompt("사용자 이름", $"{p.Host} 에 로그인할 사용자 이름", false);
                if (string.IsNullOrEmpty(user))
                    throw new OperationCanceledException("사용자가 접속을 취소했습니다.");
                p.User = user;
            }

            _keys = LoadKeys(p);
            _password = Secret.Unprotect(p.SavedPassword);
            const int maxPasswordPrompts = 3;
            var passwordPrompts = 0;

            // 키 인증이 한 번 실패하면 이후 재접속에서는 키를 빼고 비밀번호만 시도한다.
            // (SSH.NET 은 RSA 키를 서명 알고리즘별로 여러 번 시도하므로 서버 MaxAuthTries 를 빨리 소진한다)
            var useKeys = _keys.Count > 0;

            while (true)
            {
                var info = CreateConnectionInfo(p.User, BuildMethods(p.User, useKeys, _password));
                var client = new SshClient(info);
                var hostKey = AttachHostKeyCheck(client);
                if (p.KeepAlive > 0)
                    client.KeepAliveInterval = TimeSpan.FromSeconds(p.KeepAlive);

                try
                {
                    client.Connect();
                    if (!useKeys)
                        _keys.Clear();
                    return client;
                }
                catch (Exception) when (hostKey.Status != HostKeyStatus.Trusted)
                {
                    client.Dispose();
                    ResolveRejectedHostKey(hostKey);
                    _ui.Status("호스트 키를 저장했습니다. 다시 접속합니다...");
                }
                catch (Exception ex) when (passwordPrompts < maxPasswordPrompts && IsAuthFailure(ex))
                {
                    client.Dispose();
                    useKeys = false;
                    if (_password != null)
                        _ui.Status("인증 실패. 다시 시도하세요.");
                    passwordPrompts++;
                    _password = _ui.Prompt("비밀번호", $"{p.User}@{p.Host} 비밀번호", true);
                    if (_password == null)
                        throw new OperationCanceledException("사용자가 인증을 취소했습니다.");
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
        }

        private List<AuthenticationMethod> BuildMethods(string user, bool useKeys, string password)
        {
            var methods = new List<AuthenticationMethod>();
            if (useKeys && _keys.Count > 0)
                methods.Add(new PrivateKeyAuthenticationMethod(user, _keys.ToArray()));
            methods.Add(CreateKeyboardInteractive(user, password));
            if (password != null)
                methods.Add(new PasswordAuthenticationMethod(user, password));
            return methods;
        }

        private static bool IsAuthFailure(Exception ex) =>
            ex is SshAuthenticationException
            || (ex is SshConnectionException ce && ce.Message.IndexOf("authentication failures", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// SSH.NET 은 keyboard-interactive 요청을 보낸 직후 ConnectionInfo.Timeout 을 한 번 읽어
        /// 서버의 최종 성공/실패 응답까지 그 시간만 기다린다(프롬프트 콜백은 그 대기 중에 별도 스레드에서 실행).
        /// 그래서 OTP 입력에 시간이 걸리면 "Session operation has timed out" 으로 끊긴다.
        /// OTP 는 재접속으로 재시도할 수 없으므로 이 인증 단계 동안만 타임아웃을 늘리고 끝나면 되돌린다.
        /// </summary>
        private sealed class PatientKeyboardInteractive : KeyboardInteractiveAuthenticationMethod
        {
            // 서버도 LoginGraceTime(기본 120초)이 지나면 끊으므로, 응답 없는 네트워크에서만 이 상한이 의미가 있다.
            private static readonly TimeSpan UserInputTimeout = TimeSpan.FromMinutes(5);

            public PatientKeyboardInteractive(string user) : base(user) { }

            public override AuthenticationResult Authenticate(Session session)
            {
                var info = session.ConnectionInfo;
                var original = info.Timeout;
                if (original < UserInputTimeout)
                    info.Timeout = UserInputTimeout;
                try
                {
                    return base.Authenticate(session);
                }
                finally
                {
                    // 이후 인증 방식·채널 열기 등은 원래 타임아웃을 쓴다.
                    info.Timeout = original;
                }
            }
        }

        private KeyboardInteractiveAuthenticationMethod CreateKeyboardInteractive(string user, string password)
        {
            var kbd = new PatientKeyboardInteractive(user);
            var passwordUsed = false;
            kbd.AuthenticationPrompt += (s, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    var isPassword = prompt.Request.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                                     || prompt.Request.IndexOf("암호", StringComparison.Ordinal) >= 0
                                     || prompt.Request.IndexOf("비밀번호", StringComparison.Ordinal) >= 0;
                    if (isPassword && password != null && !passwordUsed)
                    {
                        passwordUsed = true;
                        prompt.Response = password;
                    }
                    else
                    {
                        // OTP 등 서버가 요구하는 추가 입력. 저장된 비밀번호 없이 PAM 이 "Password:" 를 물으면 비밀번호 창으로 보인다.
                        var message = string.IsNullOrEmpty(e.Instruction) ? prompt.Request : e.Instruction + "\n" + prompt.Request;
                        prompt.Response = _ui.Prompt(isPassword ? "비밀번호" : "추가 인증", message.Trim(), !prompt.IsEchoed) ?? "";
                    }
                }
            };
            return kbd;
        }

        private ConnectionInfo CreateConnectionInfo(string user, List<AuthenticationMethod> methodList)
        {
            var p = Profile;
            var methods = methodList.ToArray();
            ConnectionInfo info;
            if (_jump != null || string.IsNullOrEmpty(p.Proxy))
            {
                info = new ConnectionInfo(_connectHost, _connectPort, user, methods);
            }
            else
            {
                if (!Uri.TryCreate(p.Proxy, UriKind.Absolute, out var uri))
                    throw new FormatException($"잘못된 프록시 주소: {p.Proxy}  (예: http://proxy:8080, socks5://proxy:1080)");

                ProxyTypes type;
                switch (uri.Scheme.ToLowerInvariant())
                {
                    case "http": type = ProxyTypes.Http; break;
                    case "socks4": type = ProxyTypes.Socks4; break;
                    case "socks5": case "socks": type = ProxyTypes.Socks5; break;
                    default: throw new FormatException($"지원하지 않는 프록시 종류: {uri.Scheme}");
                }

                string proxyUser = null, proxyPass = null;
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var ui = uri.UserInfo.Split(new[] { ':' }, 2);
                    proxyUser = Uri.UnescapeDataString(ui[0]);
                    proxyPass = ui.Length > 1 ? Uri.UnescapeDataString(ui[1]) : null;
                }
                var proxyPort = uri.IsDefaultPort || uri.Port <= 0 ? (type == ProxyTypes.Http ? 8080 : 1080) : uri.Port;
                info = new ConnectionInfo(_connectHost, _connectPort, user, type, uri.Host, proxyPort, proxyUser, proxyPass, methods);
            }
            info.Encoding = p.GetEncoding();
            info.Timeout = TimeSpan.FromSeconds(p.Timeout > 0 ? p.Timeout : 15);
            return info;
        }

        private List<IPrivateKeySource> LoadKeys(HostProfile p)
        {
            var result = new List<IPrivateKeySource>();
            IEnumerable<string> candidates;
            var explicitKey = !string.IsNullOrEmpty(p.KeyFile);
            if (explicitKey)
            {
                var path = Environment.ExpandEnvironmentVariables(p.KeyFile);
                if (path.StartsWith("~"))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"키 파일을 찾을 수 없습니다: {path}");
                candidates = new[] { path };
            }
            else
            {
                candidates = new[] { "id_ed25519", "id_ecdsa", "id_rsa" }
                    .Select(n => Path.Combine(Paths.UserSshDir, n))
                    .Where(File.Exists);
            }

            foreach (var path in candidates)
            {
                var key = LoadKey(path, explicitKey);
                if (key != null)
                    result.Add(key);
            }
            return result;
        }

        private PrivateKeyFile LoadKey(string path, bool explicitKey)
        {
            try
            {
                return new PrivateKeyFile(path);
            }
            catch (SshPassPhraseNullOrEmptyException)
            {
                for (var i = 0; i < 3; i++)
                {
                    var pass = _ui.Prompt("키 암호", $"개인키 암호를 입력하세요 (취소 = 건너뛰기)\n{path}", true);
                    if (string.IsNullOrEmpty(pass))
                        return null;
                    try
                    {
                        return new PrivateKeyFile(path, pass);
                    }
                    catch (Exception ex) when (ex is SshException || ex is ArgumentException || ex is System.Security.Cryptography.CryptographicException)
                    {
                        _ui.Status("키 암호가 올바르지 않습니다.");
                    }
                }
                return null;
            }
            catch (Exception ex) when (!explicitKey)
            {
                _ui.Status($"키 '{path}' 를 읽을 수 없어 건너뜁니다: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var f in _forwards)
            {
                try { f.Stop(); } catch { /* 종료 중 오류 무시 */ }
            }
            _forwards.Clear();
            if (Client != null)
            {
                try { Client.Disconnect(); } catch { /* 이미 끊긴 경우 */ }
                Client.Dispose();
                Client = null;
            }
            _jump?.Dispose();
            _jump = null;
        }
    }
}
