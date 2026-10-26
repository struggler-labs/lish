using System;
using System.Security.Cryptography;
using System.Text;

namespace Lish.Core
{
    /// <summary>Windows DPAPI(현재 사용자 범위)로 비밀번호를 암호화한다. 다른 계정/PC에서는 복호화 불가.</summary>
    internal static class Secret
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LISH.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return null;
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        public static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher))
                return null;
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
