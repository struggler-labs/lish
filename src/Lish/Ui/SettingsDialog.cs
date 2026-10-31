using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    internal sealed class SettingsDialog : StyledDialog
    {
        private readonly AppSettings _settings;
        private readonly ComboField _font;
        private readonly InputBox _size, _scrollback;
        private readonly Toggle _copyOnSelect, _rightPaste, _confirmPaste, _verticalTabs;
        private readonly Panel _preview;

        public SettingsDialog(AppSettings settings) : base("설정", "터미널 표시와 입력 동작")
        {
            _settings = settings;
            ClientSize = new Size(Theme.Scale(520), Theme.Scale(600));

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, BackColor = Color.White };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(120)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            T Row<T>(string label, T c) where T : Control
            {
                // 높이는 그리드에 넣기 전에 읽어야 함: 스타일 없는 행에 Dock=Fill 로 들어가면 셀 크기로 늘어남
                // (첫 행은 그리드 기본 높이 100 에 맞춰져 글꼴 행만 100px 이 되던 원인)
                var h = c.Height;
                var l = new Label { Text = label, ForeColor = Theme.SubText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(0, Theme.Scale(4), 0, Theme.Scale(4));
                grid.Controls.Add(l, 0, grid.RowCount);
                grid.Controls.Add(c, 1, grid.RowCount);
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h + Theme.Scale(8)));
                grid.RowCount++;
                return c;
            }

            _font = Row("글꼴", new ComboField(false));
            foreach (var f in MonospaceFonts())
                _font.Combo.Items.Add(f);
            _size = Row("글꼴 크기", new InputBox("11"));
            _scrollback = Row("스크롤백 줄 수", new InputBox("5000"));
            _copyOnSelect = Row("", new Toggle("드래그로 선택하면 자동 복사"));
            _rightPaste = Row("", new Toggle("오른쪽 클릭으로 붙여넣기 (PuTTY 방식)"));
            _confirmPaste = Row("", new Toggle("여러 줄 붙여넣기 전에 확인"));
            _verticalTabs = Row("", new Toggle("세션 탭을 왼쪽에 세로로 보기"));

            _preview = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(86), Margin = new Padding(0, Theme.Scale(10), 0, 0) };
            _preview.Paint += PaintPreview;

            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.Scale(52), Padding = new Padding(0, Theme.Scale(14), 0, 0), BackColor = Color.White };
            var folder = new FlatButton("데이터 폴더 열기", ButtonKind.Ghost, Theme.IconFolder);
            folder.Click += (s, e) => Process.Start("explorer.exe", Paths.DataDir);
            tools.Controls.Add(folder);

            Body.Controls.Add(tools);
            Body.Controls.Add(_preview);
            Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Scale(10) });
            Body.Controls.Add(grid);

            var fontName = string.IsNullOrEmpty(settings.FontName) ? MonospaceFonts().FirstOrDefault() : settings.FontName;
            _font.Combo.SelectedItem = _font.Combo.Items.Cast<string>().FirstOrDefault(n => string.Equals(n, fontName, StringComparison.OrdinalIgnoreCase));
            if (_font.Combo.SelectedIndex < 0 && _font.Combo.Items.Count > 0)
                _font.Combo.SelectedIndex = 0;
            _size.Text = settings.FontSize.ToString("0.#");
            _scrollback.Text = settings.Scrollback.ToString();
            _copyOnSelect.Checked = settings.CopyOnSelect;
            _rightPaste.Checked = settings.RightClickPaste;
            _confirmPaste.Checked = settings.ConfirmMultilinePaste;
            _verticalTabs.Checked = settings.VerticalTabs;
            _font.Combo.SelectedIndexChanged += (s, e) => _preview.Invalidate();
            _size.Box.TextChanged += (s, e) => _preview.Invalidate();

            AcceptButton = AddButton("저장", ButtonKind.Primary, DialogResult.None);
            ((FlatButton)AcceptButton).Click += (s, e) =>
            {
                if (Apply())
                    DialogResult = DialogResult.OK;
            };
            AddButton("취소", ButtonKind.Secondary, DialogResult.Cancel);
        }

        private static string[] MonospaceFonts()
        {
            var preferred = new[] { "D2Coding", "Cascadia Mono", "Cascadia Code", "Consolas", "JetBrains Mono", "Fira Code", "Source Code Pro", "나눔고딕코딩", "NanumGothicCoding", "Lucida Console", "Courier New" };
            return preferred.Where(Theme.FontExists).ToArray();
        }

        private float PreviewSize => float.TryParse(_size.Text, out var v) && v >= 6 && v <= 40 ? v : 10.5f;

        private void PaintPreview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, _preview.Width - 1.5f, _preview.Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.Scale(8)))
            using (var b = new SolidBrush(Theme.TerminalBg))
                g.FillPath(b, path);
            var name = _font.Combo.SelectedItem as string ?? "Consolas";
            using (var f = EmbeddedFonts.Create(name, PreviewSize))
            {
                TextRenderer.DrawText(g, "user@lish:~$ ls -la  한글 출력 테스트", f, new Point(Theme.Scale(14), Theme.Scale(14)), Color.FromArgb(0xE6, 0xE1, 0xF5));
                TextRenderer.DrawText(g, "drwxr-xr-x  2 root root  4096  config", f, new Point(Theme.Scale(14), Theme.Scale(16) + f.Height), Theme.Accent);
            }
        }

        private bool Apply()
        {
            if (!float.TryParse(_size.Text.Trim(), out var size) || size < 6 || size > 40)
            {
                MessageBox.Show(this, "글꼴 크기는 6~40 사이여야 합니다.", "설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!int.TryParse(_scrollback.Text.Trim(), out var sb) || sb < 0 || sb > 200000)
            {
                MessageBox.Show(this, "스크롤백은 0~200000 줄입니다.", "설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _settings.FontName = _font.Combo.SelectedItem as string;
            _settings.FontSize = size;
            _settings.Scrollback = sb;
            _settings.CopyOnSelect = _copyOnSelect.Checked;
            _settings.RightClickPaste = _rightPaste.Checked;
            _settings.ConfirmMultilinePaste = _confirmPaste.Checked;
            _settings.VerticalTabs = _verticalTabs.Checked;
            _settings.Save();
            return true;
        }
    }
}
