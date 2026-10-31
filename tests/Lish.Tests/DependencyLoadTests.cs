using Renci.SshNet;
using Xunit;

namespace Lish.Tests
{
    /// <summary>
    /// lish.exe embeds SSH.NET and its dependencies as resources; the test run instead loads them from the
    /// output folder. This checks the dependency chain (SSH.NET, BouncyCastle, Microsoft.Bcl.*) resolves.
    /// </summary>
    public class DependencyLoadTests
    {
        [Fact]
        public void SshNet_LoadsAndBuildsConnectionInfo()
        {
            var info = new PasswordConnectionInfo("example.invalid", 2222, "user", "pw");
            Assert.Equal("example.invalid", info.Host);
            Assert.Equal(2222, info.Port);
            Assert.Contains("ssh-ed25519", info.HostKeyAlgorithms.Keys);
        }

        [Fact]
        public void LishAssembly_IsTheExe()
        {
            Assert.Equal("lish", typeof(Lish.Core.KnownHosts).Assembly.GetName().Name);
        }
    }
}
