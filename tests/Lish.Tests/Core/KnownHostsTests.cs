using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lish.Core;
using Xunit;

namespace Lish.Tests.Core
{
    [Collection(DataDirCollection.Name)]
    public class KnownHostsTests : IDisposable
    {
        private readonly TestDataDir _data = new TestDataDir();

        // Host names under the reserved .invalid TLD, unique per run, so entries in the
        // developer's real %USERPROFILE%\.ssh\known_hosts (also read by KnownHosts) can never match.
        private readonly string _host = "lish-test-" + Guid.NewGuid().ToString("N") + ".invalid";

        private static readonly byte[] KeyA = Encoding.ASCII.GetBytes("test-host-key-A");
        private static readonly byte[] KeyB = Encoding.ASCII.GetBytes("test-host-key-B");

        public void Dispose() => _data.Dispose();

        private void WriteKnownHosts(params string[] lines) =>
            File.WriteAllLines(Paths.KnownHostsFile, lines);

        [Theory]
        [InlineData("example.com", 22, "example.com")]
        [InlineData("example.com", 2222, "[example.com]:2222")]
        [InlineData("10.0.0.5", 80, "[10.0.0.5]:80")]
        public void HostToken_FollowsOpenSshConvention(string host, int port, string expected)
        {
            Assert.Equal(expected, KnownHosts.HostToken(host, port));
        }

        [Fact]
        public void Fingerprint_IsUnpaddedBase64Sha256()
        {
            // SHA-256 of the empty input is a well-known test vector.
            Assert.Equal("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU", KnownHosts.Fingerprint(new byte[0]));
        }

        [Fact]
        public void UnknownHost_IsUnknown()
        {
            Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyA));
        }

        [Fact]
        public void Add_ThenCheck_IsTrusted_AndWrittenInOpenSshFormat()
        {
            KnownHosts.Add(_host, 2222, "ssh-ed25519", KeyA);

            Assert.Equal(HostKeyStatus.Trusted, KnownHosts.Check(_host, 2222, "ssh-ed25519", KeyA));
            var line = File.ReadAllText(Paths.KnownHostsFile).Trim();
            Assert.Equal($"[{_host}]:2222 ssh-ed25519 {Convert.ToBase64String(KeyA)}", line);
        }

        [Fact]
        public void Entry_IsBoundToPort()
        {
            KnownHosts.Add(_host, 2222, "ssh-ed25519", KeyA);
            Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyA));
        }

        [Fact]
        public void DifferentKeyOfSameType_IsChanged()
        {
            KnownHosts.Add(_host, 22, "ssh-ed25519", KeyA);
            Assert.Equal(HostKeyStatus.Changed, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyB));
        }

        [Fact]
        public void KeyOfOtherType_IsUnknownNotChanged()
        {
            KnownHosts.Add(_host, 22, "ssh-ed25519", KeyA);
            Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(_host, 22, "ecdsa-sha2-nistp256", KeyB));
        }

        [Fact]
        public void CommaSeparatedPatterns_MatchCaseInsensitively()
        {
            WriteKnownHosts($"other.invalid,{_host.ToUpperInvariant()},10.9.9.9 ssh-rsa {Convert.ToBase64String(KeyA)}");
            Assert.Equal(HostKeyStatus.Trusted, KnownHosts.Check(_host, 22, "ssh-rsa", KeyA));
        }

        [Fact]
        public void HashedEntry_MatchesHostToken()
        {
            var salt = new byte[20];
            for (var i = 0; i < salt.Length; i++)
                salt[i] = (byte)(i * 7 + 1);
            var token = KnownHosts.HostToken(_host, 2200);
            string hash;
            using (var hmac = new HMACSHA1(salt))
                hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(token)));
            WriteKnownHosts($"|1|{Convert.ToBase64String(salt)}|{hash} ssh-ed25519 {Convert.ToBase64String(KeyA)}");

            Assert.Equal(HostKeyStatus.Trusted, KnownHosts.Check(_host, 2200, "ssh-ed25519", KeyA));
            Assert.Equal(HostKeyStatus.Changed, KnownHosts.Check(_host, 2200, "ssh-ed25519", KeyB));
            Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyA));
        }

        [Fact]
        public void CommentsMarkersAndMalformedLines_AreIgnored()
        {
            var key = Convert.ToBase64String(KeyA);
            WriteKnownHosts(
                "# comment " + _host + " ssh-ed25519 " + key,
                "",
                "@cert-authority " + _host + " ssh-ed25519 " + key,
                "@revoked " + _host + " ssh-ed25519 " + key,
                _host + " ssh-ed25519",                          // missing key
                "|1|not*base64|xxx ssh-ed25519 " + key,          // bad salt must not throw
                "|1|onlysalt ssh-ed25519 " + key);               // incomplete hash
            Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyA));
        }

        [Fact]
        public void TabSeparatedEntry_IsParsed()
        {
            WriteKnownHosts(_host + "\tssh-ed25519\t" + Convert.ToBase64String(KeyA) + "\tcomment");
            Assert.Equal(HostKeyStatus.Trusted, KnownHosts.Check(_host, 22, "ssh-ed25519", KeyA));
        }
    }
}
