using System;

namespace Lish.Terminal
{
    [Flags]
    internal enum CellAttr : ushort
    {
        None = 0,
        Bold = 1,
        Dim = 2,
        Italic = 4,
        Underline = 8,
        Inverse = 16,
        Hidden = 32,
        Strike = 64,
        Blink = 128,
    }

    /// <summary>
    /// 화면 한 칸. 색상: -1 = 기본색, 0~255 = 팔레트, (RgbFlag | 0xRRGGBB) = 트루컬러.
    /// Width: 1 = 일반, 2 = 전각(한글 등) 앞칸, 0 = 전각 뒷칸.
    /// </summary>
    internal struct Cell
    {
        public const int DefaultColor = -1;
        public const int RgbFlag = 0x1000000;

        public char Ch;
        public char Low;      // 서로게이트 쌍의 하위 문자 (이모지 등), 없으면 '\0'
        public int Fg;
        public int Bg;
        public CellAttr Attr;
        public byte Width;

        public static Cell Blank(int bg) => new Cell { Ch = ' ', Fg = DefaultColor, Bg = bg, Width = 1 };

        public bool SameStyle(in Cell o) => Fg == o.Fg && Bg == o.Bg && Attr == o.Attr;
    }

    internal sealed class Line
    {
        public Cell[] Cells;
        /// <summary>자동 줄바꿈으로 다음 줄과 이어진 줄 (복사 시 줄바꿈을 넣지 않음)</summary>
        public bool Wrapped;

        public Line(int cols, int bg)
        {
            Cells = new Cell[cols];
            Clear(bg);
        }

        public void Clear(int bg)
        {
            var blank = Cell.Blank(bg);
            for (var i = 0; i < Cells.Length; i++)
                Cells[i] = blank;
            Wrapped = false;
        }

        public void Resize(int cols)
        {
            if (cols == Cells.Length)
                return;
            var old = Cells;
            Cells = new Cell[cols];
            var n = Math.Min(cols, old.Length);
            Array.Copy(old, Cells, n);
            var blank = Cell.Blank(Cell.DefaultColor);
            for (var i = n; i < cols; i++)
                Cells[i] = blank;
            // 잘린 전각 문자 앞칸 정리
            if (n > 0 && Cells[n - 1].Width == 2 && n == cols)
                Cells[n - 1] = blank;
        }
    }

    internal static class CharWidth
    {
        /// <summary>터미널에서 차지하는 칸 수 (wcwidth 간이 구현, 한글/CJK/이모지 = 2)</summary>
        public static int Of(int cp)
        {
            if (cp < 0x300)
                return cp < 0x20 ? 0 : 1;
            if (cp <= 0x36F) return 0;                       // 결합 분음 부호
            if (cp >= 0x1160 && cp <= 0x11FF) return 0;      // 한글 자모 중성/종성 (조합형)
            if (cp >= 0x200B && cp <= 0x200F) return 0;      // 폭 없는 공백/방향 표시
            if (cp >= 0xFE00 && cp <= 0xFE0F) return 0;      // 이체자 선택자
            if (cp == 0x20E3 || (cp >= 0xE0100 && cp <= 0xE01EF)) return 0;
            return IsWide(cp) ? 2 : 1;
        }

        private static bool IsWide(int cp) =>
            (cp >= 0x1100 && cp <= 0x115F) ||
            (cp >= 0x231A && cp <= 0x231B) ||
            (cp >= 0x2329 && cp <= 0x232A) ||
            (cp >= 0x23E9 && cp <= 0x23EC) || cp == 0x23F0 || cp == 0x23F3 ||
            (cp >= 0x25FD && cp <= 0x25FE) ||
            (cp >= 0x2614 && cp <= 0x2615) ||
            (cp >= 0x2648 && cp <= 0x2653) || cp == 0x267F || cp == 0x2693 || cp == 0x26A1 ||
            (cp >= 0x26AA && cp <= 0x26AB) || (cp >= 0x26BD && cp <= 0x26BE) ||
            (cp >= 0x26C4 && cp <= 0x26C5) || cp == 0x26CE || cp == 0x26D4 || cp == 0x26EA ||
            (cp >= 0x26F2 && cp <= 0x26F3) || cp == 0x26F5 || cp == 0x26FA || cp == 0x26FD ||
            cp == 0x2705 || (cp >= 0x270A && cp <= 0x270B) || cp == 0x2728 || cp == 0x274C || cp == 0x274E ||
            (cp >= 0x2753 && cp <= 0x2755) || cp == 0x2757 || (cp >= 0x2795 && cp <= 0x2797) ||
            cp == 0x27B0 || cp == 0x27BF || (cp >= 0x2B1B && cp <= 0x2B1C) || cp == 0x2B50 || cp == 0x2B55 ||
            (cp >= 0x2E80 && cp <= 0x303E) ||
            (cp >= 0x3041 && cp <= 0x33FF) ||
            (cp >= 0x3400 && cp <= 0x4DBF) ||
            (cp >= 0x4E00 && cp <= 0x9FFF) ||
            (cp >= 0xA000 && cp <= 0xA4CF) ||
            (cp >= 0xA960 && cp <= 0xA97F) ||
            (cp >= 0xAC00 && cp <= 0xD7A3) ||                // 한글 음절
            (cp >= 0xF900 && cp <= 0xFAFF) ||
            (cp >= 0xFE10 && cp <= 0xFE19) ||
            (cp >= 0xFE30 && cp <= 0xFE6F) ||
            (cp >= 0xFF00 && cp <= 0xFF60) ||
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||
            (cp >= 0x1F300 && cp <= 0x1F64F) ||
            (cp >= 0x1F680 && cp <= 0x1F6FF) ||
            (cp >= 0x1F900 && cp <= 0x1F9FF) ||
            (cp >= 0x1FA70 && cp <= 0x1FAFF) ||
            (cp >= 0x20000 && cp <= 0x3FFFD);
    }
}
