namespace Lish.Terminal
{
    /// <summary>터미널 색상 (0xRRGGBB). LISH 브랜드 퍼플 톤 다크 테마.</summary>
    internal sealed class TerminalTheme
    {
        public int Foreground = 0xE6E1F5;
        public int Background = 0x17142A;
        public int Cursor = 0xA78BFA;
        public int CursorText = 0x17142A;
        public int SelectionBg = 0x4B2DBD;
        public int SelectionFg = 0xFFFFFF;
        public int CompositionBg = 0x6D4AFF;

        public readonly int[] Palette = new int[256];

        public static readonly TerminalTheme Default = new TerminalTheme();

        public TerminalTheme()
        {
            int[] basic =
            {
                0x1E1A33, 0xF0577A, 0x4ADE9A, 0xF5C76B, 0x7C8CFF, 0xB98BFA, 0x5FD4E8, 0xD6D1E8,
                0x6B6488, 0xFF7A96, 0x7CF0B6, 0xFFDD8E, 0xA0ACFF, 0xD3B4FF, 0x8AE6F5, 0xFFFFFF,
            };
            for (var i = 0; i < 16; i++)
                Palette[i] = basic[i];

            int[] steps = { 0, 95, 135, 175, 215, 255 };
            for (var i = 0; i < 216; i++)
                Palette[16 + i] = (steps[i / 36] << 16) | (steps[i / 6 % 6] << 8) | steps[i % 6];
            for (var i = 0; i < 24; i++)
            {
                var v = 8 + i * 10;
                Palette[232 + i] = (v << 16) | (v << 8) | v;
            }
        }

        public int Resolve(int color, int fallback)
        {
            if (color < 0)
                return fallback;
            if ((color & Cell.RgbFlag) != 0)
                return color & 0xFFFFFF;
            return Palette[color & 0xFF];
        }

        public static int Blend(int a, int b, float t)
        {
            int ch(int shift) => (int)(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t) & 0xFF;
            return (ch(16) << 16) | (ch(8) << 8) | ch(0);
        }
    }
}
