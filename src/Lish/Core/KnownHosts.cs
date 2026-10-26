using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Lish.Core
{
    internal enum HostKeyStatus { Trusted, Unknown, Changed }

    /// <summary>
    /// OpenSSH known_hosts 형식 호환 호스트 키 검증.
    /// 읽기: LISH 데이터 폴더의 known_hosts + %USERPROFILE%\.ssh\known_hosts (해시 항목 포함)
    /// 쓰기: LISH 데이터 폴더의 known_hosts 에만 추가.
    /// </summary>
    internal static class KnownHosts
    {
        private static readonly object FileLock = new object();

        private sealed class Entry
        {
            public string[] Patterns;   // 평문 호스트 패턴 (콤마 구분)
            public string HashedSalt;   // |1|salt|hash 형식일 때
            public string HashedValue;
            public string KeyType;
            public string KeyBase64;

            public bool Matches(string hostToken)
            {
                if (HashedSalt != null)
                {
                    try
                    {
                        using (var hmac = new HMACSHA1(Convert.FromBase64String(HashedSalt)))
                        {
                            var h = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(hostToken)));
                            return h == HashedValue;
                        }
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }
                return Patterns.Any(p => string.Equals(p, hostToken, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>OpenSSH 와 같은 규칙: 22번 포트는 host, 그 외는 [host]:port</summary>
        public static string HostToken(string host, int port) => port == 22 ? host : "[" + host + "]:" + port;

        public static string Fingerprint(byte[] keyBlob)
        {
            using (var sha = SHA256.Create())
                return "SHA256:" + Convert.ToBase64String(sha.ComputeHash(keyBlob)).TrimEnd('=');
        }

        public static HostKeyStatus Check(string host, int port, string keyType, byte[] keyBlob)
        {
            var token = HostToken(host, port);
            var keyB64 = Convert.ToBase64String(keyBlob);
            var entries = LoadAll().Where(e => e.Matches(token)).ToList();

            if (entries.Any(e => e.KeyType == keyType && e.KeyBase64 == keyB64))
                return HostKeyStatus.Trusted;
            if (entries.Any(e => e.KeyType == keyType))
                return HostKeyStatus.Changed;
            return HostKeyStatus.Unknown;
        }

        public static void Add(string host, int port, string keyType, byte[] keyBlob)
        {
            lock (FileLock)
            {
                File.AppendAllText(Paths.KnownHostsFile,
                    $"{HostToken(host, port)} {keyType} {Convert.ToBase64String(keyBlob)}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }

        public static string[] Files => new[] { Paths.KnownHostsFile, Path.Combine(Paths.UserSshDir, "known_hosts") };

        private static IEnumerable<Entry> LoadAll()
        {
            foreach (var path in Files)
            {
                if (!File.Exists(path))
                    continue;
                string[] lines;
                try
                {
                    lock (FileLock)
                        lines = File.ReadAllLines(path);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var raw in lines)
                {
                    var e = Parse(raw);
                    if (e != null)
                        yield return e;
                }
            }
        }

        private static Entry Parse(string line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                return null;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return null;
            // @cert-authority / @revoked 마커는 지원하지 않음
            if (parts[0].StartsWith("@"))
                return null;

            var e = new Entry { KeyType = parts[1], KeyBase64 = parts[2] };
            if (parts[0].StartsWith("|1|"))
            {
                var h = parts[0].Split('|');
                if (h.Length < 4)
                    return null;
                e.HashedSalt = h[2];
                e.HashedValue = h[3];
            }
            else
            {
                e.Patterns = parts[0].Split(',');
            }
            return e;
        }
    }
}
