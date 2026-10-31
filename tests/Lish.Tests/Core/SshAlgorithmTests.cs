using Lish.Core;
using Renci.SshNet;
using Xunit;

namespace Lish.Tests.Core
{
    /// <summary>
    /// Weak algorithms (1024-bit DH, 3DES) must never be negotiated; strong defaults must stay available.
    /// </summary>
    public class SshAlgorithmTests
    {
        private static ConnectionInfo Info()
        {
            var info = new PasswordConnectionInfo("example.invalid", 22, "user", "pw");
            SshSession.RemoveWeakAlgorithms(info);
            return info;
        }

        [Fact]
        public void WeakAlgorithms_ArePresentInSshNetDefaults()
        {
            // Guards the test below: if SSH.NET drops them upstream this documents it.
            var info = new PasswordConnectionInfo("example.invalid", 22, "user", "pw");
            Assert.Contains("diffie-hellman-group1-sha1", info.KeyExchangeAlgorithms.Keys);
            Assert.Contains("3des-cbc", info.Encryptions.Keys);
        }

        [Fact]
        public void RemoveWeakAlgorithms_DropsWeakKexAndCiphers()
        {
            var info = Info();
            foreach (var name in SshSession.WeakKeyExchanges)
                Assert.DoesNotContain(name, info.KeyExchangeAlgorithms.Keys);
            foreach (var name in SshSession.WeakEncryptions)
                Assert.DoesNotContain(name, info.Encryptions.Keys);
        }

        [Theory]
        [InlineData("curve25519-sha256")]
        [InlineData("ecdh-sha2-nistp256")]
        [InlineData("diffie-hellman-group14-sha256")]
        [InlineData("diffie-hellman-group-exchange-sha256")]
        public void RemoveWeakAlgorithms_KeepsStrongKex(string name)
        {
            Assert.Contains(name, Info().KeyExchangeAlgorithms.Keys);
        }

        [Theory]
        [InlineData("aes128-ctr")]
        [InlineData("aes256-gcm@openssh.com")]
        [InlineData("chacha20-poly1305@openssh.com")]
        public void RemoveWeakAlgorithms_KeepsStrongCiphers(string name)
        {
            Assert.Contains(name, Info().Encryptions.Keys);
        }

        [Fact]
        public void RemoveWeakAlgorithms_IsIdempotent()
        {
            var info = Info();
            SshSession.RemoveWeakAlgorithms(info);
            Assert.DoesNotContain("3des-cbc", info.Encryptions.Keys);
        }
    }
}
