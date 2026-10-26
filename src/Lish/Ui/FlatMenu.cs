using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lish.Ui
{
    /// <summary>
    /// LISH 스타일 우클릭 메뉴: 흰 카드 + 둥근 모서리(Windows 11 DWM), 항목마다 아이콘·단축키, 둥근 선택 배경, 위험 항목(삭제) 빨간색.
    /// </summary>
    internal sealed class FlatMenu : ContextMenuStrip
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpRound = 2;

        /// <summary>DWM 이 모서리·테두리를 그리는지 (Windows 11). 아니면 렌더러가 사각 테두리를 그린다.</summary>
        internal bool RoundedByDwm { get; private set; }

        public FlatMenu()
        {
            Renderer = new FlatMenuRenderer();
            Font = Theme.UiFont;
            BackColor = Color.White;
            ShowImageMargin = true;
            ShowCheckMargin = false;
            ImageScalingSize = new Size(Theme.Scale(22), Theme.Scale(16));
            Padding = new Padding(Theme.Scale(4), Theme.Scale(6), Theme.Scale(4), Theme.Scale(6));
        }

        public FlatMenuItem Add(string text, string glyph, Action onClick, string shortcut = null)
        {
            var item = new FlatMenuItem(text, glyph, onClick) { ShortcutKeyDisplayString = shortcut };
            Items.Add(item);
            return item;
        }

        /// <summary>삭제처럼 되돌리기 어려운 동작 (빨간색).</summary>
        public FlatMenuItem AddDanger(string text, string glyph, Action onClick)
        {
            var item = Add(text, glyph, onClick);
            item.Danger = true;
            return item;
        }

        public void AddSeparator() => Items.Add(new ToolStripSeparator { Margin = new Padding(0, Theme.Scale(3), 0, Theme.Scale(3)) });

        protected override void OnItemAdded(ToolStripItemEventArgs e)
        {
            base.OnItemAdded(e);
            if (e.Item is ToolStripMenuItem)
                e.Item.Padding = new Padding(Theme.Scale(2), Theme.Scale(6), Theme.Scale(8), Theme.Scale(6));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var corner = DwmwcpRound;
                RoundedByDwm = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref corner, sizeof(int)) == 0;
                if (RoundedByDwm)
                {
                    var color = ColorTranslator.ToWin32(Theme.Border);
                    DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref color, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                RoundedByDwm = false;
            }
        }
    }

    internal sealed class FlatMenuItem : ToolStripMenuItem
    {
        public string Glyph { get; set; }
        public bool Danger { get; set; }

        // 이미지가 없으면 WinForms 가 아이콘 칸을 넓혀 주지 않으므로, 투명 이미지로 칸 폭을 잡고 글리프는 렌더러가 그린다
        private static readonly Bitmap Placeholder = new Bitmap(Theme.Scale(22), Theme.Scale(16));

        public FlatMenuItem(string text, string glyph, Action onClick) : base(text)
        {
            Glyph = glyph;
            Image = Placeholder;
            ImageScaling = ToolStripItemImageScaling.None;
            if (onClick != null)
                Click += (s, e) => onClick();
        }
    }

    /// <summary>FlatMenu / 드롭다운 공용 렌더러.</summary>
    internal sealed class FlatMenuRenderer : ToolStripRenderer
    {
        private static readonly Color DangerSoft = Color.FromArgb(0xFD, 0xEE, 0xF1);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Color.White))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is FlatMenu m && m.RoundedByDwm)
                return;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // 아이콘 칸도 흰 배경 그대로
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var danger = (item as FlatMenuItem)?.Danger == true;
            var g = e.Graphics;
            Theme.Smooth(g);
            if (item.Selected && item.Enabled)
            {
                var r = new RectangleF(Theme.Scale(2), 0.5f, item.Width - Theme.Scale(4), item.Height - 1);
                using (var path = Theme.RoundRect(r, Theme.Scale(6)))
                using (var b = new SolidBrush(danger ? DangerSoft : Theme.PrimarySoft))
                    g.FillPath(b, path);
            }

            if (item is FlatMenuItem fi && !string.IsNullOrEmpty(fi.Glyph))
            {
                var color = !item.Enabled ? Theme.Faint
                    : danger ? Theme.Danger
                    : item.Selected ? Theme.Deep : Theme.SubText;
                Theme.DrawIcon(g, fi.Glyph, Theme.Icons, color, new Rectangle(Theme.Scale(8), 0, Theme.Scale(24), item.Height));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = e.Item;
            var shortcut = item is ToolStripMenuItem mi && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString;
            var danger = (item as FlatMenuItem)?.Danger == true;
            e.TextColor = !item.Enabled || shortcut ? Theme.Faint
                : danger ? Theme.Danger
                : item.Selected ? Theme.Deep : Theme.Ink;
            if (shortcut)
                e.TextFont = Theme.Small;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, Theme.Scale(10), y, e.Item.Width - Theme.Scale(10), y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.SubText;
            base.OnRenderArrow(e);
        }
    }
}
