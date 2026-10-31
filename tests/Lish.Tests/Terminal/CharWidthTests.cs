using Lish.Terminal;
using Xunit;

namespace Lish.Tests.Terminal
{
    public class CharWidthTests
    {
        [Theory]
        [InlineData(0x41)]      // 'A'
        [InlineData(0xE9)]      // 'é'
        [InlineData(0x2500)]    // '─' box drawing
        [InlineData(0x2014)]    // '—' em dash
        public void NarrowCharacters_AreOneCell(int cp)
        {
            Assert.Equal(1, CharWidth.Of(cp));
        }

        [Theory]
        [InlineData(0xAC00)]    // '가' first Hangul syllable
        [InlineData(0xD7A3)]    // '힣' last Hangul syllable
        [InlineData(0x4E2D)]    // '中' CJK ideograph
        [InlineData(0xFF21)]    // fullwidth 'Ａ'
        [InlineData(0x1F600)]   // emoji
        public void WideCharacters_AreTwoCells(int cp)
        {
            Assert.Equal(2, CharWidth.Of(cp));
        }

        [Theory]
        [InlineData(0x07)]      // control
        [InlineData(0x0301)]    // combining acute accent
        [InlineData(0x200B)]    // zero width space
        [InlineData(0xFE0F)]    // variation selector
        [InlineData(0x1161)]    // Hangul jungseong (conjoining vowel)
        public void ZeroWidthCharacters_AreZeroCells(int cp)
        {
            Assert.Equal(0, CharWidth.Of(cp));
        }
    }
}
