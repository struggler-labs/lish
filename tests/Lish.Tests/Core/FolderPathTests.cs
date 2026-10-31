using System.Linq;
using Lish.Core;
using Xunit;

namespace Lish.Tests.Core
{
    public class FolderPathTests
    {
        [Theory]
        [InlineData(" prod / / web ", "prod/web")]
        [InlineData("prod", "prod")]
        [InlineData("/a/b/", "a/b")]
        [InlineData("///", null)]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        public void Normalize_TrimsSegmentsAndDropsEmptyOnes(string input, string expected)
        {
            Assert.Equal(expected, FolderPath.Normalize(input));
        }

        [Fact]
        public void Combine_NameOf_ParentOf()
        {
            Assert.Equal("web", FolderPath.Combine(null, "web"));
            Assert.Equal("prod/web", FolderPath.Combine("prod", "web"));
            Assert.Equal("web", FolderPath.NameOf("prod/web"));
            Assert.Equal("prod", FolderPath.NameOf("prod"));
            Assert.Equal("prod", FolderPath.ParentOf("prod/web"));
            Assert.Null(FolderPath.ParentOf("prod"));
            Assert.Null(FolderPath.ParentOf(null));
        }

        [Theory]
        [InlineData("prod", "prod", true)]
        [InlineData("prod/web", "prod", true)]
        [InlineData("PROD/web", "prod", true)]
        [InlineData("production", "prod", false)]   // prefix of a name is not a parent
        [InlineData("prod", "prod/web", false)]
        [InlineData(null, "prod", false)]
        [InlineData("prod", null, false)]
        public void IsUnder(string path, string folder, bool expected)
        {
            Assert.Equal(expected, FolderPath.IsUnder(path, folder));
        }

        [Fact]
        public void Rebase_MovesSubPath()
        {
            Assert.Equal("dev/web/api", FolderPath.Rebase("prod/web/api", "prod", "dev"));
        }

        [Fact]
        public void Validate_RejectsEmptyAndSeparator()
        {
            Assert.NotNull(FolderPath.Validate(""));
            Assert.NotNull(FolderPath.Validate("  "));
            Assert.NotNull(FolderPath.Validate("a/b"));
            Assert.Null(FolderPath.Validate("운영"));
        }

        [Fact]
        public void FolderComparer_SortsParentsBeforeChildrenAndSiblings()
        {
            var sorted = new[] { "prod 2", "prod/web", "dev", "prod" }.OrderBy(x => x, FolderComparer.Instance).ToArray();
            Assert.Equal(new[] { "dev", "prod", "prod/web", "prod 2" }, sorted);
        }
    }
}
