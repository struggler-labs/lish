using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Lish.Ui
{
    internal enum ButtonKind { Primary, Secondary, Ghost, Danger }

    /// <summary>라운드 플랫 버튼 (아이콘 글리프 + 텍스트).</summary>
    internal class FlatButton : Control, IButtonControl
    {
        private bool _hover;
        private bool _pressed;
        private string _glyph;

        public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
        public DialogResult DialogResult { get; set; }
        public int Radius { get; set; } = 6;

        [DefaultValue(null)]
        public string Glyph
        {
            get => _glyph;
            set { _glyph = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
            Height = Theme.Scale(32);
        }

        public FlatButton(string text, ButtonKind kind, string glyph = null) : this()
        {
            Text = text;
            Kind = kind;
            _glyph = glyph;
            AutoSizeWidth();
        }

        public void AutoSizeWidth()
        {
            var textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Theme.UiBold).Width;
            var glyphW = string.IsNullOrEmpty(_glyph) ? 0 : Theme.Scale(16) + (textW > 0 ? Theme.Scale(6) : 0);
            Width = textW + glyphW + Theme.Scale(textW > 0 ? 24 : 16);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None && FindForm() is Form f)
                f.DialogResult = DialogResult;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                PerformClick();
        }

        public void NotifyDefault(bool value) { }

        public void PerformClick()
        {
            if (Enabled)
                OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            Color fill, border, fore;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = !Enabled ? Theme.Accent : _pressed ? Theme.Deep : _hover ? Theme.PrimaryHover : Theme.Primary;
                    border = fill;
                    fore = Color.White;
                    break;
                case ButtonKind.Danger:
                    fill = _pressed ? Color.FromArgb(0xC9, 0x34, 0x4B) : _hover ? Color.FromArgb(0xFD, 0xEC, 0xEF) : Color.White;
                    border = _hover ? Theme.Danger : Theme.Border;
                    fore = _pressed ? Color.White : Theme.Danger;
                    break;
                case ButtonKind.Ghost:
                    fill = _pressed ? Theme.PrimarySoft : _hover ? Theme.PrimarySofter : Color.Transparent;
                    border = fill;
                    fore = Enabled ? (_hover ? Theme.Deep : Theme.SubText) : Theme.Faint;
                    break;
                default:
                    fill = _pressed ? Theme.PrimarySoft : _hover ? Theme.PrimarySofter : Color.White;
                    border = _hover ? Theme.Accent : Theme.Border;
                    fore = Enabled ? Theme.Ink : Theme.Faint;
                    break;
            }

            using (var path = Theme.RoundRect(r, Theme.Scale(Radius)))
            {
                if (fill.A > 0)
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.A > 0 && border != fill)
                    using (var p = new Pen(border)) g.DrawPath(p, path);
                if (Focused && ShowFocusCues)
                    using (var p = new Pen(Color.FromArgb(120, Theme.Primary), 2f)) g.DrawPath(p, path);
            }

            var font = Kind == ButtonKind.Primary ? Theme.UiBold : Theme.UiFont;
            var textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var glyphW = string.IsNullOrEmpty(_glyph) ? 0 : Theme.Scale(16);
            var gap = glyphW > 0 && textW > 0 ? Theme.Scale(6) : 0;
            var x = (Width - (textW + glyphW + gap)) / 2;
            if (glyphW > 0)
                Theme.DrawIcon(g, _glyph, Theme.Icons, fore, new Rectangle(x, 0, glyphW, Height));
            if (textW > 0)
                TextRenderer.DrawText(g, Text, font, new Rectangle(x + glyphW + gap, 0, textW + 2, Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>라운드 테두리 입력 상자 (포커스 시 보라색 테두리).</summary>
    internal sealed class InputBox : Panel
    {
        public readonly TextBox Box;
        private string _glyph;

        public InputBox(string cue = null, string glyph = null, bool password = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _glyph = glyph;
            BackColor = Color.White;
            Height = Theme.Scale(32);
            Box = new CueTextBox
            {
                Cue = cue,
                BorderStyle = BorderStyle.None,
                Font = Theme.UiFont,
                ForeColor = Theme.Ink,
                BackColor = Color.White,
                UseSystemPasswordChar = password,
            };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            // EM_SETCUEBANNER 는 단일 행 EDIT 에서만 동작 → 여러 줄일 때는 CueTextBox 가 직접 그림
            Box.HandleCreated += (s, e) => { if (cue != null) Theme.SetCue(Box, cue); };
            Padding = new Padding(Theme.Scale(string.IsNullOrEmpty(glyph) ? 10 : 30), 0, Theme.Scale(10), 0);
        }

        public override string Text
        {
            get => Box.Text;
            set => Box.Text = value;
        }

        public bool Multiline
        {
            get => Box.Multiline;
            set
            {
                Box.Multiline = value;
                Box.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
                Box.AcceptsReturn = value;
                LayoutBox();
                Box.Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            if (Box == null)
                return; // 생성자에서 Height 설정 시 Box 생성 전에 호출됨
            var h = Box.Multiline ? Height - Theme.Scale(12) : Box.PreferredHeight;
            Box.SetBounds(Padding.Left, Box.Multiline ? Theme.Scale(6) : (Height - h) / 2, Math.Max(10, Width - Padding.Left - Padding.Right), h);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Box.Focus();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.BackColor = Enabled ? Color.White : Theme.Window;
            BackColor = Box.BackColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.Scale(6)))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
                var focused = Box.Focused;
                using (var p = new Pen(focused ? Theme.Primary : Theme.Border, focused ? 1.5f : 1f)) g.DrawPath(p, path);
            }
            if (!string.IsNullOrEmpty(_glyph))
                Theme.DrawIcon(g, _glyph, Theme.IconsSmall, Theme.Faint, new Rectangle(Theme.Scale(8), 0, Theme.Scale(16), Height));
        }
    }

    /// <summary>여러 줄 모드에서도 플레이스홀더를 보여주는 TextBox (단일 행은 EM_SETCUEBANNER 사용).</summary>
    internal sealed class CueTextBox : TextBox
    {
        private const int WM_PAINT = 0x000F;
        private const int EM_GETMARGINS = 0x00D4;

        public string Cue { get; set; }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Multiline) Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (Multiline) Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (Multiline) Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT || !Multiline || TextLength > 0 || string.IsNullOrEmpty(Cue))
                return;
            // EDIT 컨트롤의 왼쪽 여백(LOWORD)에 맞춰 단일 행 큐 배너와 같은 위치·색으로 그림
            var left = (int)SendMessage(Handle, EM_GETMARGINS, IntPtr.Zero, IntPtr.Zero) & 0xFFFF;
            using (var g = Graphics.FromHwnd(Handle))
                TextRenderer.DrawText(g, Cue, Font, new Rectangle(left, 0, ClientSize.Width - left, ClientSize.Height), SystemColors.GrayText,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>스타일 적용된 드롭다운 (라운드 테두리 안의 ComboBox).</summary>
    internal sealed class ComboField : Panel
    {
        public readonly ComboBox Combo;

        public ComboField(bool editable)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = Theme.Scale(32);
            Combo = new ComboBox
            {
                FlatStyle = FlatStyle.Flat,
                Font = Theme.UiFont,
                ForeColor = Theme.Ink,
                BackColor = Color.White,
                DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            };
            Controls.Add(Combo);
            Combo.GotFocus += (s, e) => Invalidate();
            Combo.LostFocus += (s, e) => Invalidate();
        }

        public override string Text
        {
            get => Combo.Text;
            set => Combo.Text = value;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Combo == null)
                return;
            var h = Combo.PreferredHeight;
            Combo.SetBounds(Theme.Scale(4), (Height - h) / 2, Width - Theme.Scale(8), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            Theme.Smooth(g);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Scale(6)))
            {
                g.FillPath(Brushes.White, path);
                var focused = Combo.Focused;
                using (var p = new Pen(focused ? Theme.Primary : Theme.Border, focused ? 1.5f : 1f)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>가는 구분선</summary>
    internal sealed class Divider : Control
    {
        public Divider(DockStyle dock)
        {
            Dock = dock;
            BackColor = Theme.Border;
            if (dock == DockStyle.Left || dock == DockStyle.Right)
                Width = 1;
            else
                Height = 1;
        }
    }

    /// <summary>체크박스 대신 쓰는 토글 스위치.</summary>
    internal sealed class Toggle : Control
    {
        private bool _checked;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Toggle(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Text = text;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
            Height = Theme.Scale(26);
            Width = Theme.Scale(46) + TextRenderer.MeasureText(text, Theme.UiFont).Width;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
                Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var h = Theme.Scale(18);
            var w = Theme.Scale(32);
            var track = new RectangleF(0.5f, (Height - h) / 2f, w, h);
            using (var path = Theme.RoundRect(track, h / 2f))
            using (var b = new SolidBrush(Checked ? Theme.Primary : Color.FromArgb(0xD6, 0xD3, 0xE3)))
                g.FillPath(b, path);
            var knob = h - Theme.Scale(4);
            var kx = Checked ? track.Right - knob - Theme.Scale(2) : track.Left + Theme.Scale(2);
            g.FillEllipse(Brushes.White, kx, track.Top + Theme.Scale(2), knob, knob);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(w + Theme.Scale(10), 0, Width - w, Height), Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>접고 펼치는 구역의 머리줄: 구분선 + 제목·요약 + 오른쪽 "펼치기/접기" 버튼. 줄 어디를 눌러도 토글.</summary>
    internal sealed class ExpanderHeader : Control
    {
        private readonly string _summary;
        private bool _expanded, _hover;

        public event EventHandler Toggled;

        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Invalidate();
            }
        }

        public ExpanderHeader(string title, string summary)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
            Text = title;
            _summary = summary;
            Cursor = Cursors.Hand;
            Height = Theme.Scale(58);
            TabStop = true;
        }

        private void Toggle()
        {
            Expanded = !Expanded;
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Toggle();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
                Toggle();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            var right = Width - Theme.Scale(8);   // 위 그리드의 오른쪽 여백과 맞춤
            var lineY = Theme.Scale(14);
            using (var pen = new Pen(Theme.Border))
                g.DrawLine(pen, 0, lineY, right, lineY);

            var rowTop = lineY + Theme.Scale(6);
            var rowH = Height - rowTop - Theme.Scale(4);

            // 오른쪽 버튼
            var label = Expanded ? "접기" : "펼치기";
            var textW = TextRenderer.MeasureText(label, Theme.UiFont).Width;
            var pill = new RectangleF(right - textW - Theme.Scale(40), rowTop + (rowH - Theme.Scale(28)) / 2f, textW + Theme.Scale(40), Theme.Scale(28));
            using (var path = Theme.RoundRect(pill, Theme.Scale(14)))
            {
                using (var b = new SolidBrush(_hover || Focused ? Theme.PrimarySoft : Theme.PrimarySofter))
                    g.FillPath(b, path);
                if (Focused)
                    using (var pen = new Pen(Theme.Accent)) g.DrawPath(pen, path);
            }
            Theme.DrawIcon(g, Expanded ? Theme.IconChevronDown : Theme.IconChevronRight, Theme.IconsSmall, Theme.Primary,
                new Rectangle((int)pill.X + Theme.Scale(10), (int)pill.Y, Theme.Scale(14), (int)pill.Height));
            TextRenderer.DrawText(g, label, Theme.UiFont, new Rectangle((int)pill.X + Theme.Scale(28), (int)pill.Y, textW + Theme.Scale(4), (int)pill.Height),
                Theme.Primary, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // 제목 + 요약
            var titleW = TextRenderer.MeasureText(Text, Theme.Heading).Width;
            var textRect = new Rectangle(0, rowTop, (int)pill.X - Theme.Scale(8), rowH);
            TextRenderer.DrawText(g, Text, Theme.Heading, textRect, Theme.Deep,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _summary, Theme.Small, new Rectangle(titleW + Theme.Scale(6), rowTop, textRect.Width - titleW - Theme.Scale(6), rowH), Theme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>얇은 진행 막대 (0~100). 기본 ProgressBar 대신 테마 색으로 그린다.</summary>
    internal sealed class ThinProgress : Control
    {
        private int _value;

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.Scale(3);
            BackColor = Theme.PrimarySoft;
        }

        public int Value
        {
            get => _value;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (_value == value) return;
                _value = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var w = Width * _value / 100;
            if (w > 0)
                using (var b = new SolidBrush(Theme.Primary))
                    e.Graphics.FillRectangle(b, 0, 0, w, Height);
        }
    }
}
