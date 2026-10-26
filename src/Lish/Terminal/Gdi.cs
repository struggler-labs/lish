using System;
using System.Runtime.InteropServices;

namespace Lish.Terminal
{
    /// <summary>터미널 렌더링/IME 용 Win32 API.</summary>
    internal static class Gdi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TEXTMETRIC
        {
            public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading,
                tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
            public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
            public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMPOSITIONFORM
        {
            public int dwStyle;
            public POINT ptCurrentPos;
            public RECT rcArea;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CANDIDATEFORM
        {
            public int dwIndex;
            public int dwStyle;
            public POINT ptCurrentPos;
            public RECT rcArea;
        }

        public const int ETO_OPAQUE = 0x2;
        public const int ETO_CLIPPED = 0x4;
        public const int TRANSPARENT = 1;
        public const int OPAQUE = 2;
        public const int TA_BASELINE = 24;
        public const int DC_BRUSH = 18;
        public const int GGI_MARK_NONEXISTING_GLYPHS = 1;

        public const int WM_IME_STARTCOMPOSITION = 0x010D;
        public const int WM_IME_ENDCOMPOSITION = 0x010E;
        public const int WM_IME_COMPOSITION = 0x010F;
        public const int WM_IME_SETCONTEXT = 0x0281;
        public const int WM_IME_CHAR = 0x0286;
        public const int WM_SYSCHAR = 0x0106;
        public const int GCS_COMPSTR = 0x0008;
        public const int GCS_RESULTSTR = 0x0800;
        public const int CFS_POINT = 0x0002;
        public const int CFS_CANDIDATEPOS = 0x0040;
        public const long ISC_SHOWUICOMPOSITIONWINDOW = 0x80000000L;

        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] public static extern int SetBkColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern int SetTextAlign(IntPtr hdc, int align);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] public static extern int SetDCBrushColor(IntPtr hdc, int color);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtTextOutW")]
        public static extern bool ExtTextOut(IntPtr hdc, int x, int y, int options, ref RECT rect, string text, int count, int[] dx);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextMetricsW")]
        public static extern bool GetTextMetrics(IntPtr hdc, out TEXTMETRIC tm);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")]
        public static extern bool GetTextExtentPoint32(IntPtr hdc, string text, int count, out SIZE size);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetGlyphIndicesW")]
        public static extern int GetGlyphIndices(IntPtr hdc, string text, int count, [Out] ushort[] indices, int flags);

        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr hwnd);
        [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode, EntryPoint = "ImmGetCompositionStringW")]
        public static extern int ImmGetCompositionString(IntPtr himc, int index, byte[] buffer, int len);
        [DllImport("imm32.dll")] public static extern bool ImmSetCompositionWindow(IntPtr himc, ref COMPOSITIONFORM form);
        [DllImport("imm32.dll")] public static extern bool ImmSetCandidateWindow(IntPtr himc, ref CANDIDATEFORM form);

        public static int ToColorRef(int rgb) => ((rgb >> 16) & 0xFF) | (rgb & 0xFF00) | ((rgb & 0xFF) << 16);

        public static void Fill(IntPtr hdc, int l, int t, int r, int b, int rgb)
        {
            SetDCBrushColor(hdc, ToColorRef(rgb));
            var rc = new RECT(l, t, r, b);
            FillRect(hdc, ref rc, GetStockObject(DC_BRUSH));
        }
    }
}
