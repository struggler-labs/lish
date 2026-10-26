using System;
using System.Linq;

namespace Lish.Core
{
    /// <summary>[user@]host[:port] 형식 주소 해석.</summary>
    internal static class Address
    {
        /// <summary>저장된 세션 이름이면 그 복사본, 아니면 [user@]host[:port] 로 해석.</summary>
        public static HostProfile ResolveDestination(string dest, ProfileStore store)
        {
            var saved = store.Find(dest);
            return saved != null ? saved.Clone() : Parse(dest);
        }

        public static HostProfile Parse(string dest)
        {
            dest = (dest ?? "").Trim();
            string user = null;
            var at = dest.LastIndexOf('@');
            if (at >= 0)
            {
                user = dest.Substring(0, at);
                dest = dest.Substring(at + 1);
            }

            var host = dest;
            var port = 22;
            string portText = null;
            if (dest.StartsWith("["))
            {
                // [IPv6]:port
                var end = dest.IndexOf(']');
                if (end < 0)
                    throw new FormatException($"잘못된 주소: {dest}");
                host = dest.Substring(1, end - 1);
                var rest = dest.Substring(end + 1);
                if (rest.StartsWith(":"))
                    portText = rest.Substring(1);
            }
            else if (dest.Count(c => c == ':') == 1)
            {
                var idx = dest.IndexOf(':');
                host = dest.Substring(0, idx);
                portText = dest.Substring(idx + 1);
            }

            if (portText != null && (!int.TryParse(portText, out port) || port < 1 || port > 65535))
                throw new FormatException($"잘못된 포트: {portText}");
            if (string.IsNullOrWhiteSpace(host) || host.Contains(" "))
                throw new FormatException("호스트 주소가 올바르지 않습니다. 예: user@10.0.0.5:22");

            return new HostProfile { Host = host, Port = port, User = string.IsNullOrEmpty(user) ? null : user };
        }
    }
}
