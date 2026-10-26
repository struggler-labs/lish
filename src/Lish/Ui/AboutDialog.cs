using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Lish.Ui
{
    /// <summary>LISH 정보: 버전, 빌드 해시, 라이선스, 내장 오픈소스 라이브러리 목록과 고지문.</summary>
    internal sealed class AboutDialog : StyledDialog
    {
        public const string RepositoryUrl = "https://github.com/struggler-labs/lish";

        public AboutDialog() : base("LISH 정보")
        {
            ClientSize = new Size(Theme.Scale(560), Theme.Scale(600));

            var hash = Program.BuildHash;

            // ── 로고 + 이름 + 버전 ──
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(96), BackColor = Color.White };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var icon = Theme.Scale(64);
                BrandIcon.Draw(g, new RectangleF(0, Theme.Scale(8), icon, icon));
                var x = icon + Theme.Scale(18);
                BrandIcon.DrawWordmark(g, new Point(x, Theme.Scale(6)), Theme.Scale(30));
                TextRenderer.DrawText(g, "Lightweight SSH & SFTP Client for Windows.", Theme.UiFont,
                    new Point(x, Theme.Scale(46)), Theme.SubText, TextFormatFlags.NoPrefix);
                // 버전 배지
                var label = "v" + Program.Version;
                var w = TextRenderer.MeasureText(label, Theme.UiBold).Width + Theme.Scale(16);
                var badge = new RectangleF(x, Theme.Scale(68), w, Theme.Scale(22));
                Theme.Smooth(g);
                using (var path = Theme.RoundRect(badge, Theme.Scale(11)))
                using (var b = new SolidBrush(Theme.PrimarySoft))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, label, Theme.UiBold, Rectangle.Round(badge), Theme.Deep,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };

            // ── 버전·빌드·라이선스·저장소 ──
            var info = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, BackColor = Color.White, Padding = new Padding(0, Theme.Scale(12), 0, Theme.Scale(4)) };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(96)));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddInfoRow(info, "버전", Program.Version);
            var hashLabel = AddInfoRow(info, "빌드", hash == null ? "알 수 없음 (git 없이 빌드됨)" : Short(hash));
            if (hash != null)
            {
                new ToolTip().SetToolTip(hashLabel, hash);
                var copy = MakeLink("복사");
                copy.LinkClicked += (s, e) =>
                {
                    Clipboard.SetText(hash);
                    copy.Text = "복사됨";
                };
                info.Controls.Add(copy, 2, info.RowCount - 1);
            }
            AddInfoRow(info, "라이선스", "GNU General Public License v3.0");
            var repo = MakeLink(RepositoryUrl);
            repo.LinkClicked += (s, e) => OpenUrl(RepositoryUrl);
            AddInfoRow(info, "저장소", null, repo);

            // ── 오픈소스 라이브러리 ──
            var heading = new Label
            {
                Dock = DockStyle.Top,
                Text = "오픈소스 라이브러리",
                Font = Theme.Heading,
                ForeColor = Theme.Deep,
                Height = Theme.Scale(36),
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, Theme.Scale(6)),
            };
            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Text = "lish.exe 에 내장된 구성 요소입니다. 두 번 클릭하면 프로젝트 페이지를 엽니다.",
                Font = Theme.Small,
                ForeColor = Theme.Faint,
                Height = Theme.Scale(24),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var list = BuildLibraryList();
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
            listHost.Controls.Add(list);

            Body.Controls.Add(listHost);
            Body.Controls.Add(hint);
            Body.Controls.Add(heading);
            Body.Controls.Add(info);
            Body.Controls.Add(header);

            AcceptButton = AddButton("닫기", ButtonKind.Primary, DialogResult.OK);
            CancelButton = AcceptButton;
            var notices = AddButton("오픈소스 고지 전체 보기", ButtonKind.Secondary, DialogResult.None);
            notices.Click += (s, e) => ShowNotices(this);
        }

        private static string Short(string hash) => hash.Length > 10 ? hash.Substring(0, 10) : hash;

        private static Label AddInfoRow(TableLayoutPanel grid, string name, string value, Control custom = null)
        {
            var row = grid.RowCount;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(28)));
            grid.Controls.Add(new Label { Text = name, ForeColor = Theme.SubText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, row);
            Label valueLabel = null;
            if (custom == null)
            {
                valueLabel = new Label { Text = value, ForeColor = Theme.Ink, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty, UseMnemonic = false };
                custom = valueLabel;
            }
            custom.Dock = DockStyle.Fill;
            custom.Margin = Padding.Empty;
            grid.Controls.Add(custom, 1, row);
            grid.RowCount++;
            return valueLabel;
        }

        private static LinkLabel MakeLink(string text) => new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            LinkColor = Theme.Primary,
            ActiveLinkColor = Theme.Deep,
            VisitedLinkColor = Theme.Primary,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };

        private sealed class Library
        {
            public string Name, Version, License, Url;
        }

        private static ListView BuildLibraryList()
        {
            var libs = (Program.ReadTextResource("thirdparty.txt") ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('|'))
                .Where(p => p.Length == 2)
                .Select(p => new Library { Name = p[0], Version = p[1], License = LicenseOf(p[0]), Url = $"https://www.nuget.org/packages/{p[0]}/{p[1]}" })
                .OrderBy(l => l.Name.StartsWith("System.") || l.Name.StartsWith("Microsoft.") ? 1 : 0)
                .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            libs.Add(new Library { Name = "D2Coding (글꼴)", Version = "1.3.2", License = "OFL-1.1", Url = "https://github.com/naver/d2codingfont" });

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                Font = Theme.UiFont,
                ForeColor = Theme.Ink,
            };
            list.Columns.Add("이름", Theme.Scale(300));
            list.Columns.Add("버전", Theme.Scale(90));
            list.Columns.Add("라이선스", Theme.Scale(90));
            foreach (var l in libs)
            {
                var item = new ListViewItem(l.Name) { Tag = l.Url };
                item.SubItems.Add(l.Version);
                item.SubItems.Add(l.License);
                list.Items.Add(item);
            }
            list.HandleCreated += (s, e) => Theme.ExplorerStyle(list);
            list.Resize += (s, e) =>
                list.Columns[0].Width = Math.Max(Theme.Scale(160), list.ClientSize.Width - list.Columns[1].Width - list.Columns[2].Width);
            list.ItemActivate += (s, e) =>
            {
                if (list.SelectedItems.Count > 0)
                    OpenUrl((string)list.SelectedItems[0].Tag);
            };
            return list;
        }

        /// <summary>내장 패키지의 라이선스 (현재 모두 MIT). 새 의존성이 생기면 THIRD-PARTY-NOTICES.txt 와 함께 갱신.</summary>
        private static string LicenseOf(string id) =>
            id == "SSH.NET" || id.StartsWith("BouncyCastle.", StringComparison.Ordinal)
            || id.StartsWith("Microsoft.", StringComparison.Ordinal) || id.StartsWith("System.", StringComparison.Ordinal)
                ? "MIT"
                : "고지 참조";

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // 사내망 등 브라우저를 열 수 없으면 주소만 복사해 둔다
                Clipboard.SetText(url);
            }
        }

        private static void ShowNotices(IWin32Window owner)
        {
            using (var d = new NoticesDialog())
                d.ShowDialog(owner);
        }

        /// <summary>THIRD-PARTY-NOTICES.txt + D2Coding OFL 전문.</summary>
        private sealed class NoticesDialog : StyledDialog
        {
            public NoticesDialog() : base("오픈소스 고지")
            {
                ClientSize = new Size(Theme.Scale(760), Theme.Scale(560));
                var text = (Program.ReadTextResource("notices.THIRD-PARTY-NOTICES.txt") ?? "")
                           + "\n\n\nD2Coding - SIL Open Font License 1.1\n------------------------------------\n\n"
                           + (Program.ReadTextResource("notices.D2Coding-OFL.txt") ?? "");
                Body.Controls.Add(new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White,
                    Font = Theme.Mono,
                    Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                });
                AcceptButton = CancelButton = AddButton("닫기", ButtonKind.Primary, DialogResult.OK);
            }
        }
    }
}
