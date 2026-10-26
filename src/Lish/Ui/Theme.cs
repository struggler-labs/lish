using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lish.Ui
{
    /// <summary>LISH 디자인 가이드 (lish-design.png) 색상/글꼴.</summary>
    internal static class Theme
    {
        public static readonly Color Primary = Color.FromArgb(0x6D, 0x4A, 0xFF);
        public static readonly Color Deep = Color.FromArgb(0x4B, 0x2D, 0xBD);
        public static readonly Color Accent = Color.FromArgb(0xA7, 0x8B, 0xFA);
        public static readonly Color PrimaryHover = Color.FromArgb(0x5E, 0x3C, 0xF0);
        public static readonly Color PrimarySoft = Color.FromArgb(0xEE, 0xEA, 0xFF);
        public static readonly Color PrimarySofter = Color.FromArgb(0xF5, 0xF2, 0xFF);

        public static readonly Color Ink = Color.FromArgb(0x24, 0x1F, 0x3D);
        public static readonly Color SubText = Color.FromArgb(0x76, 0x71, 0x92);
        public static readonly Color Faint = Color.FromArgb(0xA5, 0xA1, 0xBA);
        public static readonly Color Border = Color.FromArgb(0xE6, 0xE3, 0xF0);
        public static readonly Color Window = Color.FromArgb(0xF7, 0xF6, 0xFB);
        public static readonly Color Surface = Color.White;
        public static readonly Color Sidebar = Color.FromArgb(0xFB, 0xFA, 0xFE);
        public static readonly Color TerminalBg = Color.FromArgb(0x17, 0x14, 0x2A);

        public static readonly Color Success = Color.FromArgb(0x22, 0xC5, 0x7A);
        public static readonly Color Warning = Color.FromArgb(0xF5, 0xA5, 0x24);
        public static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x5E);

        public static readonly Font UiFont = new Font(PickUiFont(), 9f);
        public static readonly Font UiBold = new Font(UiFont, FontStyle.Bold);
        public static readonly Font Small = new Font(UiFont.FontFamily, 8f);
        public static readonly Font Title = new Font(UiFont.FontFamily, 13f, FontStyle.Bold);
        public static readonly Font Heading = new Font(UiFont.FontFamily, 10f, FontStyle.Bold);
        public static readonly Font Mono = EmbeddedFonts.Create(PickMonoFont(), 9.5f);
        public static readonly Font Icons = new Font(PickIconFont(), 10f);
        public static readonly Font IconsSmall = new Font(PickIconFont(), 8f);

        // Segoe MDL2 Assets / Segoe Fluent Icons 글리프
        public const string IconAdd = "";
        public const string IconSettings = "";
        public const string IconEdit = "";
        public const string IconDelete = "";
        public const string IconTerminal = "";
        public const string IconFolder = "";
        public const string IconFile = "";
        public const string IconRefresh = "";
        public const string IconUpload = "";
        public const string IconDownload = "";
        public const string IconUp = "";
        public const string IconSearch = "";
        public const string IconClose = "";
        public const string IconNewFolder = "";
        public const string IconConnect = "";
        public const string IconChevronRight = "";
        public const string IconChevronDown = "";
        public const string IconCopy = "";
        public const string IconLock = "";
        public const string IconKey = "";
        public const string IconHome = "";
        public const string IconInfo = "\uE946";
        public const string IconPaste = "\uE77F";
        public const string IconSelectAll = "\uE8B3";
        public const string IconClear = "\uE894";
        public const string IconRename = "\uE8AC";
        public const string IconSave = "\uE74E";
        public const string IconLink = "\uE71B";
        public const string IconOpen = "\uE8E5";
        public const string IconMoveTo = "\uE8DE";
        public const string IconPlay = "\uE768";
        public const string IconNewWindow = "\uE8A7";

        private static string PickUiFont()
        {
            foreach (var n in new[] { "Malgun Gothic", "Segoe UI" })
                if (FontExists(n)) return n;
            return SystemFonts.MessageBoxFont.Name;
        }

        private static string PickMonoFont()
        {
            foreach (var n in new[] { "D2Coding", "Cascadia Mono", "Consolas" })
                if (FontExists(n)) return n;
            return FontFamily.GenericMonospace.Name;
        }

        private static string PickIconFont() => FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        public static bool FontExists(string name)
        {
            if (EmbeddedFonts.Find(name) != null)
                return true;
            try
            {
                using (var ff = new FontFamily(name))
                    return string.Equals(ff.Name, name, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        public static void DrawIcon(Graphics g, string glyph, Font font, Color color, Rectangle bounds)
        {
            TextRenderer.DrawText(g, glyph, font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        public static int Scale(int px) => (int)Math.Round(px * DpiScale);

        public static readonly float DpiScale = GetDpiScale();

        private static float GetDpiScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96f;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        /// <summary>ListView/TreeView 에 탐색기 스타일 적용</summary>
        public static void ExplorerStyle(Control c) => SetWindowTheme(c.Handle, "Explorer", null);

        /// <summary>TextBox 안내 문구</summary>
        public static void SetCue(TextBox box, string cue) => SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
    }

    /// <summary>앱 아이콘 "L_" (디자인 가이드의 라운드 사각형 + 그라데이션).</summary>
    internal static class BrandIcon
    {
        public static void Draw(Graphics g, RectangleF r)
        {
            var state = g.Save();
            Theme.Smooth(g);
            var w = r.Width;
            using (var path = Theme.RoundRect(r, w * 0.235f))
            using (var bg = new LinearGradientBrush(new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom),
                       Color.FromArgb(0x8E, 0x6B, 0xFF), Color.FromArgb(0x48, 0x2A, 0xB8)))
            {
                g.FillPath(bg, path);
                // 우상단 광택
                using (var gloss = new LinearGradientBrush(r, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 135f))
                    g.FillPath(gloss, path);
            }

            RectangleF Rel(float x1, float y1, float x2, float y2) =>
                new RectangleF(r.X + w * x1, r.Y + w * y1, w * (x2 - x1), w * (y2 - y1));

            var radius = Math.Max(0.5f, w * 0.012f);
            using (var lBrush = new LinearGradientBrush(Rel(0.2f, 0.26f, 0.52f, 0.75f), Color.White, Color.FromArgb(0xE2, 0xD9, 0xFF), 90f))
            {
                using (var p = Theme.RoundRect(Rel(0.205f, 0.265f, 0.315f, 0.745f), radius)) g.FillPath(lBrush, p);
                using (var p = Theme.RoundRect(Rel(0.205f, 0.645f, 0.52f, 0.745f), radius)) g.FillPath(lBrush, p);
            }
            using (var u = new SolidBrush(Color.FromArgb(0xEC, 0xE6, 0xFF)))
            using (var p = Theme.RoundRect(Rel(0.585f, 0.70f, 0.835f, 0.785f), radius))
                g.FillPath(u, p);
            g.Restore(state);
        }

        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                var inset = size >= 48 ? size * 0.02f : 0f;
                Draw(g, new RectangleF(inset, inset, size - inset * 2, size - inset * 2));
            }
            return bmp;
        }

        private static Icon _icon;

        public static Icon AppIcon
        {
            get
            {
                if (_icon != null)
                    return _icon;
                using (var ms = new MemoryStream())
                {
                    WriteIco(ms, 16, 20, 24, 32, 40, 48, 64, 256);
                    ms.Position = 0;
                    _icon = new Icon(ms);
                }
                return _icon;
            }
        }

        /// <summary>PNG 압축 항목으로 다중 크기 .ico 를 만든다.</summary>
        public static void WriteIco(Stream output, params int[] sizes)
        {
            var images = new byte[sizes.Length][];
            for (var i = 0; i < sizes.Length; i++)
            {
                using (var bmp = Render(sizes[i]))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images[i] = ms.ToArray();
                }
            }

            var w = new BinaryWriter(output);
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            var offset = 6 + 16 * sizes.Length;
            for (var i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images)
                w.Write(img);
            w.Flush();
        }

        /// <summary>워드마크 "LISH" (I 만 Accent 색)</summary>
        public static void DrawWordmark(Graphics g, Point origin, float size)
        {
            Theme.Smooth(g);
            using (var f = new Font(Theme.FontExists("Segoe UI") ? "Segoe UI" : Theme.UiFont.Name, size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var x = (float)origin.X;
                foreach (var ch in "LISH")
                {
                    var s = ch.ToString();
                    using (var b = new SolidBrush(ch == 'I' ? Theme.Accent : Theme.Deep))
                        g.DrawString(s, f, b, x, origin.Y, StringFormat.GenericTypographic);
                    x += g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width + size * 0.06f;
                }
            }
        }

        public static SizeF MeasureWordmark(Graphics g, float size)
        {
            using (var f = new Font(Theme.FontExists("Segoe UI") ? "Segoe UI" : Theme.UiFont.Name, size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var w = 0f;
                foreach (var ch in "LISH")
                    w += g.MeasureString(ch.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width + size * 0.06f;
                return new SizeF(w, size * 1.2f);
            }
        }
    }
}
