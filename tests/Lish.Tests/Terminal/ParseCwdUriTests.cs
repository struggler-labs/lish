using Lish.Terminal;
using Xunit;

namespace Lish.Tests.Terminal
{
    /// <summary>OSC 7 "file://host/path" parsing used to follow the shell's working directory in SFTP.</summary>
    public class ParseCwdUriTests
    {
        [Fact]
        public void SplitsHostAndPath()
        {
            var r = VtTerminal.ParseCwdUri("file://server01/home/user");
            Assert.NotNull(r);
            Assert.Equal("server01", r.Value.host);
            Assert.Equal("/home/user", r.Value.path);
        }

        [Fact]
        public void EmptyHost_IsAllowed()
        {
            var r = VtTerminal.ParseCwdUri("file:///var/log");
            Assert.NotNull(r);
            Assert.Equal("", r.Value.host);
            Assert.Equal("/var/log", r.Value.path);
        }

        [Fact]
        public void PercentEncodedUtf8_IsDecoded()
        {
            var r = VtTerminal.ParseCwdUri("file://h/tmp/%ED%95%9C%EA%B8%80%20dir");
            Assert.NotNull(r);
            Assert.Equal("/tmp/한글 dir", r.Value.path);
        }

        [Fact]
        public void RootPath_IsAccepted()
        {
            var r = VtTerminal.ParseCwdUri("file://h/");
            Assert.NotNull(r);
            Assert.Equal("/", r.Value.path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/home/user")]            // no scheme
        [InlineData("relative/dir")]          // relative path, no scheme
        [InlineData("://h/home")]             // empty scheme
        [InlineData("file://hostonly")]       // no path at all
        public void MalformedUri_ReturnsNull(string uri)
        {
            Assert.Null(VtTerminal.ParseCwdUri(uri));
        }

        [Fact]
        public void EncodedNul_IsRejected()
        {
            Assert.Null(VtTerminal.ParseCwdUri("file://h/tmp/a%00b"));
        }
    }
}
