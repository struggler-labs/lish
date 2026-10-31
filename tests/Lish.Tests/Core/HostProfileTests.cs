using System.Text;
using Lish.Core;
using Xunit;

namespace Lish.Tests.Core
{
    public class HostProfileTests
    {
        [Fact]
        public void Target_OmitsDefaultPortAndMissingUser()
        {
            Assert.Equal("host", new HostProfile { Host = "host" }.Target);
            Assert.Equal("u@host", new HostProfile { Host = "host", User = "u" }.Target);
            Assert.Equal("u@host:2222", new HostProfile { Host = "host", User = "u", Port = 2222 }.Target);
        }

        [Fact]
        public void Title_FallsBackToTarget()
        {
            Assert.Equal("u@h", new HostProfile { Host = "h", User = "u" }.Title);
            Assert.Equal("my server", new HostProfile { Name = "my server", Host = "h" }.Title);
        }

        [Fact]
        public void Defaults()
        {
            var p = new HostProfile();
            Assert.Equal(22, p.Port);
            Assert.Equal(30, p.KeepAlive);
            Assert.Equal(15, p.Timeout);
            Assert.Equal("xterm-256color", p.TermOrDefault);
            Assert.Equal("vt100", new HostProfile { Term = "vt100" }.TermOrDefault);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("utf8")]
        [InlineData(" UTF-8 ")]
        public void ResolveEncoding_Utf8WithoutBom(string name)
        {
            var e = HostProfile.ResolveEncoding(name);
            Assert.Equal(65001, e.CodePage);
            Assert.Empty(e.GetPreamble());
        }

        [Theory]
        [InlineData("cp949")]
        [InlineData("MS949")]
        [InlineData("uhc")]
        public void ResolveEncoding_KoreanAliasesMapToCodePage949(string name)
        {
            Assert.Equal(949, HostProfile.ResolveEncoding(name).CodePage);
        }

        [Fact]
        public void ResolveEncoding_Cp949_RoundTripsHangul()
        {
            var e = HostProfile.ResolveEncoding("cp949");
            var bytes = e.GetBytes("한글");
            Assert.Equal(new byte[] { 0xC7, 0xD1, 0xB1, 0xDB }, bytes);
            Assert.Equal("한글", e.GetString(bytes));
        }

        [Fact]
        public void ResolveEncoding_OtherNamesUseFramework()
        {
            Assert.Equal(Encoding.GetEncoding("euc-kr").CodePage, HostProfile.ResolveEncoding("EUC-KR").CodePage);
        }

        [Fact]
        public void Clone_IsIndependentCopy()
        {
            var p = new HostProfile { Name = "a", Host = "h", Group = "g" };
            var c = p.Clone();
            Assert.NotSame(p, c);
            c.Host = "other";
            Assert.Equal("h", p.Host);
            Assert.Equal("g", c.Group);
        }
    }
}
