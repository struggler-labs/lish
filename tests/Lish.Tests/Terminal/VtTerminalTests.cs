using System.Collections.Generic;
using Lish.Terminal;
using Xunit;

namespace Lish.Tests.Terminal
{
    public class VtTerminalTests
    {
        private static VtTerminal NewTerm(int cols = 20, int rows = 5, int scrollback = 100) =>
            new VtTerminal(cols, rows, scrollback);

        /// <summary>Text of one screen row (index relative to the visible screen), trailing blanks trimmed.</summary>
        private static string Row(VtTerminal t, int y)
        {
            var index = t.Scrollback.Count + y;
            return t.GetText(index, 0, index, t.Cols - 1);
        }

        private static Cell CellAt(VtTerminal t, int x, int y) => t.GetLine(t.Scrollback.Count + y).Cells[x];

        [Fact]
        public void PlainText_IsPrintedAndCursorAdvances()
        {
            var t = NewTerm();
            t.Write("hello");
            Assert.Equal("hello", Row(t, 0));
            Assert.Equal(5, t.CursorX);
            Assert.Equal(0, t.CursorY);
        }

        [Fact]
        public void CrLf_MovesToStartOfNextLine()
        {
            var t = NewTerm();
            t.Write("ab\r\ncd");
            Assert.Equal("ab", Row(t, 0));
            Assert.Equal("cd", Row(t, 1));
            Assert.Equal(2, t.CursorX);
            Assert.Equal(1, t.CursorY);
        }

        [Fact]
        public void CursorPosition_IsOneBased()
        {
            var t = NewTerm();
            t.Write("\x1b[3;5HX");
            Assert.Equal('X', CellAt(t, 4, 2).Ch);
            Assert.Equal(5, t.CursorX);
            Assert.Equal(2, t.CursorY);
        }

        [Fact]
        public void EraseInLine_ClearsFromCursorToEnd()
        {
            var t = NewTerm();
            t.Write("hello\x1b[1;3H\x1b[K");
            Assert.Equal("he", Row(t, 0));
        }

        [Fact]
        public void EraseInDisplay2_ClearsWholeScreen()
        {
            var t = NewTerm();
            t.Write("one\r\ntwo\r\nthree\x1b[2J");
            for (var y = 0; y < t.Rows; y++)
                Assert.Equal("", Row(t, y));
        }

        [Fact]
        public void DeleteChars_ShiftsRestOfLineLeft()
        {
            var t = NewTerm();
            t.Write("abcdef\x1b[1;2H\x1b[2P");
            Assert.Equal("adef", Row(t, 0));
        }

        [Fact]
        public void RepeatPrecedingCharacter_Rep()
        {
            var t = NewTerm();
            t.Write("a\x1b[3b");
            Assert.Equal("aaaa", Row(t, 0));
        }

        [Fact]
        public void Sgr_BasicColorAndBoldThenReset()
        {
            var t = NewTerm();
            t.Write("\x1b[31;1mA\x1b[0mB");
            var a = CellAt(t, 0, 0);
            var b = CellAt(t, 1, 0);
            Assert.Equal(1, a.Fg);
            Assert.Equal(CellAttr.Bold, a.Attr);
            Assert.Equal(Cell.DefaultColor, b.Fg);
            Assert.Equal(CellAttr.None, b.Attr);
        }

        [Fact]
        public void Sgr_BrightColorsMapToPaletteEightToFifteen()
        {
            var t = NewTerm();
            t.Write("\x1b[92;107mA");
            var a = CellAt(t, 0, 0);
            Assert.Equal(10, a.Fg);
            Assert.Equal(15, a.Bg);
        }

        [Fact]
        public void Sgr_256ColorPalette()
        {
            var t = NewTerm();
            t.Write("\x1b[38;5;200;48;5;17mA");
            var a = CellAt(t, 0, 0);
            Assert.Equal(200, a.Fg);
            Assert.Equal(17, a.Bg);
        }

        [Fact]
        public void Sgr_TrueColorSemicolonForm()
        {
            var t = NewTerm();
            t.Write("\x1b[48;2;10;20;30mA");
            Assert.Equal(Cell.RgbFlag | 0x0A141E, CellAt(t, 0, 0).Bg);
        }

        [Fact]
        public void Sgr_TrueColorColonFormWithEmptyColorSpace()
        {
            var t = NewTerm();
            t.Write("\x1b[38:2::1:2:3mA");
            Assert.Equal(Cell.RgbFlag | 0x010203, CellAt(t, 0, 0).Fg);
        }

        [Fact]
        public void EscapeSequenceSplitAcrossWrites_IsStillParsed()
        {
            var t = NewTerm();
            t.Write("\x1b[3");
            t.Write("4mA");
            Assert.Equal(4, CellAt(t, 0, 0).Fg);
            Assert.Equal("A", Row(t, 0));
        }

        [Fact]
        public void KoreanSyllables_TakeTwoCellsEach()
        {
            var t = NewTerm();
            t.Write("한글a");
            Assert.Equal('한', CellAt(t, 0, 0).Ch);
            Assert.Equal(2, CellAt(t, 0, 0).Width);
            Assert.Equal(0, CellAt(t, 1, 0).Width);
            Assert.Equal('글', CellAt(t, 2, 0).Ch);
            Assert.Equal('a', CellAt(t, 4, 0).Ch);
            Assert.Equal(5, t.CursorX);
            Assert.Equal("한글a", Row(t, 0));
        }

        [Fact]
        public void WideCharAtLastColumn_WrapsToNextLine()
        {
            var t = NewTerm(cols: 5);
            t.Write("abcd한");
            Assert.Equal("abcd", Row(t, 0).TrimEnd());
            Assert.True(t.GetLine(0).Wrapped);
            Assert.Equal('한', CellAt(t, 0, 1).Ch);
            Assert.Equal(2, CellAt(t, 0, 1).Width);
        }

        [Fact]
        public void SurrogatePairEmoji_IsStoredInOneWideCell()
        {
            var t = NewTerm();
            t.Write("\U0001F600");
            var c = CellAt(t, 0, 0);
            Assert.Equal(2, c.Width);
            Assert.Equal("\U0001F600", new string(new[] { c.Ch, c.Low }));
            Assert.Equal(2, t.CursorX);
        }

        [Fact]
        public void AutoWrap_ContinuesOnNextLineAndCopiesAsOneLine()
        {
            var t = NewTerm(cols: 5);
            t.Write("abcdefg");
            Assert.Equal("abcde", Row(t, 0));
            Assert.Equal("fg", Row(t, 1));
            Assert.True(t.GetLine(0).Wrapped);
            Assert.Equal("abcdefg", t.GetText(0, 0, 1, 4));
        }

        [Fact]
        public void ScrolledOffLines_GoToScrollback()
        {
            var t = NewTerm(rows: 3);
            t.Write("1\r\n2\r\n3\r\n4\r\n5");
            Assert.Equal(2, t.Scrollback.Count);
            Assert.Equal("1", t.GetText(0, 0, 0, t.Cols - 1));
            Assert.Equal("3", Row(t, 0));
            Assert.Equal("5", Row(t, 2));
            Assert.Equal(5, t.TotalLines);
        }

        [Fact]
        public void AlternateScreen1049_RestoresMainScreenAndCursor()
        {
            var t = NewTerm();
            t.Write("main");
            t.Write("\x1b[?1049h");
            Assert.True(t.AltScreen);
            Assert.Equal("", Row(t, 0));
            t.Write("\x1b[3;1Halt");
            t.Write("\x1b[?1049l");
            Assert.False(t.AltScreen);
            Assert.Equal("main", Row(t, 0));
            Assert.Equal("", Row(t, 2));
            Assert.Equal(4, t.CursorX);
            Assert.Equal(0, t.CursorY);
        }

        [Fact]
        public void DecSpecialGraphics_MapsToBoxDrawing()
        {
            var t = NewTerm();
            t.Write("\x1b(0qx\x1b(Bq");
            Assert.Equal("─│q", Row(t, 0));
        }

        [Fact]
        public void DecPrivateModes_AreTracked()
        {
            var t = NewTerm();
            t.Write("\x1b[?2004h\x1b[?1h\x1b[?25l\x1b[?1002h\x1b[?1006h");
            Assert.True(t.BracketedPaste);
            Assert.True(t.AppCursorKeys);
            Assert.False(t.CursorVisible);
            Assert.Equal(1002, t.MouseTracking);
            Assert.True(t.MouseSgr);

            t.Write("\x1b[?2004l\x1b[?1002l");
            Assert.False(t.BracketedPaste);
            Assert.Equal(0, t.MouseTracking);
        }

        [Fact]
        public void DeviceAttributesAndCursorPositionReport_SendResponses()
        {
            var t = NewTerm();
            var responses = new List<string>();
            t.Response += responses.Add;
            t.Write("\x1b[c\x1b[2;3H\x1b[6n");
            Assert.Equal(new[] { "\x1b[?62;22c", "\x1b[2;3R" }, responses);
        }

        [Fact]
        public void Osc0And2_SetTitle()
        {
            var t = NewTerm();
            var titles = new List<string>();
            t.TitleChanged += titles.Add;
            t.Write("\x1b]0;first\x07");
            t.Write("\x1b]2;second title\x1b\\");
            Assert.Equal(new[] { "first", "second title" }, titles);
            Assert.Equal("second title", t.Title);
        }

        [Fact]
        public void Osc7_RaisesCwdChangedWithDecodedPath()
        {
            var t = NewTerm();
            string host = null, path = null;
            t.CwdChanged += (h, p) => { host = h; path = p; };
            t.Write("\x1b]7;file://myhost/home/user/my%20dir\x07");
            Assert.Equal("myhost", host);
            Assert.Equal("/home/user/my dir", path);
        }

        [Fact]
        public void Osc7_WithoutPath_DoesNotRaiseCwdChanged()
        {
            var t = NewTerm();
            var raised = false;
            t.CwdChanged += (h, p) => raised = true;
            t.Write("\x1b]7;file://myhost\x07");
            t.Write("\x1b]7;relative/dir\x07");
            Assert.False(raised);
        }

        [Fact]
        public void Osc_DoesNotPrintPayloadToScreen()
        {
            var t = NewTerm();
            t.Write("\x1b]0;title\x07ok");
            Assert.Equal("ok", Row(t, 0));
        }

        [Fact]
        public void Resize_KeepsExistingText()
        {
            var t = NewTerm(cols: 10, rows: 3);
            t.Write("hello\r\nworld");
            t.Resize(20, 6);
            Assert.Equal(20, t.Cols);
            Assert.Equal(6, t.Rows);
            Assert.Equal("hello", Row(t, 0));
            Assert.Equal("world", Row(t, 1));
            Assert.Equal(1, t.CursorY);
        }

        [Fact]
        public void FullReset_ClearsScreenAndModes()
        {
            var t = NewTerm();
            t.Write("text\x1b[?2004h\x1b[31m\u001bc");   // ESC c = RIS (\x1bc would parse as U+01BC)
            Assert.Equal("", Row(t, 0));
            Assert.False(t.BracketedPaste);
            Assert.Equal(0, t.CursorX);
            t.Write("A");
            Assert.Equal(Cell.DefaultColor, CellAt(t, 0, 0).Fg);
        }
    }
}
