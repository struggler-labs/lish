using System.Windows.Forms;
using Lish.Core;
using Lish.Terminal;
using Xunit;

namespace Lish.Tests.Terminal
{
    public class KeyMapTests
    {
        private static string Map(Keys keys)
        {
            using (var control = new TerminalControl(new AppSettings()))
                return control.MapKey(keys);
        }

        [Fact]
        public void Enter_IsLeftToKeyPress()
        {
            // 일반 Enter 는 OnKeyPress 에서 \r 로 보낸다.
            Assert.Null(Map(Keys.Enter));
        }

        [Fact]
        public void ShiftEnter_SendsEscCr()
        {
            // Shift+Enter 가 Enter 와 구분되어야 TUI(Claude Code 등)에서 줄바꿈으로 쓸 수 있다.
            Assert.Equal("\x1b\r", Map(Keys.Shift | Keys.Enter));
        }

        [Fact]
        public void AltEnter_SendsEscCr()
        {
            Assert.Equal("\x1b\r", Map(Keys.Alt | Keys.Enter));
        }
    }
}
