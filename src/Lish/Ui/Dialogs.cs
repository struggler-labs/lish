using System;
using System.Drawing;
using System.Windows.Forms;
using Lish.Core;

namespace Lish.Ui
{
    /// <summary>LISH 스타일 대화상자 기본형: 흰 배경, 제목 + 설명, 하단 버튼 줄.</summary>
    internal class StyledDialog : Form
    {
        protected readonly Panel Body;
        protected readonly FlowLayoutPanel Buttons;
        private readonly Label _title;
        private readonly Label _subtitle;

        public StyledDialog(string title, string subtitle = null)
        {
            Text = title;
            Font = Theme.UiFont;
            BackColor = Color.White;
            ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = BrandIcon.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            Padding = new Padding(Theme.Scale(24), Theme.Scale(20), Theme.Scale(24), Theme.Scale(16));

            Buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = Theme.Scale(48),
                Padding = new Padding(0, Theme.Scale(12), 0, 0),
                BackColor = Color.White,
            };

            Body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            _subtitle = new Label
            {
                Dock = DockStyle.Top,
                Text = subtitle ?? "",
                ForeColor = Theme.SubText,
                AutoSize = false,
                Height = string.IsNullOrEmpty(subtitle) ? 0 : Theme.Scale(30),
                Padding = new Padding(0, Theme.Scale(2), 0, 0),
            };
            _title = new Label
            {
                Dock = DockStyle.Top,
                Text = title,
                Font = Theme.Title,
                ForeColor = Theme.Ink,
                Height = Theme.Scale(30),
            };

            Controls.Add(Body);
            Controls.Add(Buttons);
            Controls.Add(_subtitle);
            Controls.Add(_title);
        }

        protected FlatButton AddButton(string text, ButtonKind kind, DialogResult result)
        {
            var b = new FlatButton(text, kind) { DialogResult = result, Margin = new Padding(Theme.Scale(8), 0, 0, 0) };
            b.Width = Math.Max(b.Width, Theme.Scale(84));
            Buttons.Controls.Add(b);
            if (result == DialogResult.Cancel)
                CancelButton = b;
            return b;
        }

        protected void SetSubtitle(string text)
        {
            _subtitle.Text = text;
            _subtitle.Height = string.IsNullOrEmpty(text) ? 0 : Theme.Scale(30);
        }

        protected override void OnLoad(EventArgs e)
        {
            // 긴 제목/설명이 고정 높이에 잘리지 않도록 줄바꿈 높이만큼 늘리고 창도 키운다.
            var grow = FitHeight(_title) + FitHeight(_subtitle);
            if (grow > 0)
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + grow);
            base.OnLoad(e);
        }

        /// <summary>폭에 맞춰 줄바꿈했을 때 필요한 높이로 늘리고, 늘어난 픽셀 수를 반환한다.</summary>
        protected int FitHeight(Label label)
        {
            if (string.IsNullOrEmpty(label.Text) || label.Height == 0)
                return 0;
            var width = ClientSize.Width - Padding.Horizontal - label.Padding.Horizontal;
            var need = TextRenderer.MeasureText(label.Text, label.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + label.Padding.Vertical + Theme.Scale(4);
            if (need <= label.Height)
                return 0;
            var grow = need - label.Height;
            label.Height = need;
            return grow;
        }
    }

    /// <summary>비밀번호/OTP/사용자 이름 입력.</summary>
    internal sealed class InputDialog : StyledDialog
    {
        private readonly InputBox _input;

        public InputDialog(string title, string message, bool secret) : base(title)
        {
            ClientSize = new Size(Theme.Scale(420), Theme.Scale(200));
            var label = new Label
            {
                Text = message,
                Dock = DockStyle.Top,
                AutoSize = false,
                ForeColor = Theme.SubText,
                Height = Theme.Scale(message.Split('\n').Length > 1 ? 44 : 26),
            };
            if (message.Split('\n').Length > 2)
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + Theme.Scale(18 * (message.Split('\n').Length - 2)));
            _input = new InputBox(null, secret ? Theme.IconLock : null, secret) { Dock = DockStyle.Top };
            Body.Controls.Add(_input);
            Body.Controls.Add(label);
            AcceptButton = AddButton("확인", ButtonKind.Primary, DialogResult.OK);
            AddButton("취소", ButtonKind.Secondary, DialogResult.Cancel);
            Shown += (s, e) => _input.Box.Focus();
            _input.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DialogResult = DialogResult.OK;
                }
            };
        }

        public string Value => _input.Text;

        public static string Ask(IWin32Window owner, string title, string message, bool secret, string initial = null)
        {
            using (var d = new InputDialog(title, message, secret))
            {
                if (initial != null)
                {
                    d._input.Text = initial;
                    d.Shown += (s, e) => d._input.Box.SelectAll();
                }
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
            }
        }
    }

    /// <summary>처음 보는 서버 / 바뀐 서버 키 확인.</summary>
    internal sealed class HostKeyDialog : StyledDialog
    {
        public HostKeyDialog(string host, string keyType, string fingerprint, bool changed)
            : base(changed ? "경고: 서버 키가 변경되었습니다" : "처음 접속하는 서버입니다")
        {
            ClientSize = new Size(Theme.Scale(520), Theme.Scale(changed ? 330 : 270));
            SetSubtitle(changed
                ? "중간자 공격(MITM)이거나 서버 키가 재생성되었을 수 있어 연결을 차단했습니다."
                : "서버 지문을 관리자에게 받은 값과 비교한 뒤 신뢰하세요.");

            var card = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(96), BackColor = changed ? Color.FromArgb(0xFD, 0xEE, 0xF1) : Theme.PrimarySofter };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                TextRenderer.DrawText(g, "호스트", Theme.Small, new Point(Theme.Scale(14), Theme.Scale(12)), Theme.SubText);
                TextRenderer.DrawText(g, host, Theme.UiBold, new Point(Theme.Scale(84), Theme.Scale(11)), Theme.Ink);
                TextRenderer.DrawText(g, "키 타입", Theme.Small, new Point(Theme.Scale(14), Theme.Scale(38)), Theme.SubText);
                TextRenderer.DrawText(g, keyType, Theme.UiFont, new Point(Theme.Scale(84), Theme.Scale(37)), Theme.Ink);
                TextRenderer.DrawText(g, "지문", Theme.Small, new Point(Theme.Scale(14), Theme.Scale(64)), Theme.SubText);
                TextRenderer.DrawText(g, fingerprint, Theme.Mono, new Point(Theme.Scale(84), Theme.Scale(62)), changed ? Theme.Danger : Theme.Deep);
            };
            Body.Controls.Add(card);

            if (changed)
            {
                var hint = new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = Theme.Scale(56),
                    ForeColor = Theme.SubText,
                    Text = "정상적인 변경이라면 아래 파일에서 해당 호스트 줄을 삭제한 뒤 다시 접속하세요.\n" + string.Join("\n", KnownHosts.Files),
                };
                Body.Controls.Add(hint);
                Load += (s, e) => ClientSize = new Size(ClientSize.Width, ClientSize.Height + FitHeight(hint));
                AcceptButton = AddButton("확인", ButtonKind.Primary, DialogResult.Cancel);
            }
            else
            {
                // 다른 창에서 치던 Enter 가 포커스를 가져간 이 창에 들어가 키를 신뢰해 버리지 않도록
                // Enter 기본 동작은 '취소'로 두고, 신뢰는 버튼을 직접 눌러야 한다.
                AddButton("신뢰하고 연결", ButtonKind.Primary, DialogResult.OK);
                var cancel = AddButton("취소", ButtonKind.Secondary, DialogResult.Cancel);
                AcceptButton = cancel;
                Shown += (s, e) => cancel.Focus();
            }
        }
    }

    /// <summary>SSH 스레드에서 호출 → UI 스레드에서 대화상자를 띄운다.</summary>
    internal sealed class UiInteraction : IUserInteraction
    {
        private readonly Control _owner;
        private readonly Action<string> _status;

        public UiInteraction(Control owner, Action<string> status)
        {
            _owner = owner;
            _status = status;
        }

        private T OnUi<T>(Func<T> f, T fallback)
        {
            try
            {
                if (_owner.IsDisposed || !_owner.IsHandleCreated)
                    return fallback;
                return _owner.InvokeRequired ? (T)_owner.Invoke(f) : f();
            }
            catch (ObjectDisposedException)
            {
                return fallback;
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }

        public bool ConfirmNewHostKey(string host, string keyType, string fingerprint) =>
            OnUi(() =>
            {
                using (var d = new HostKeyDialog(host, keyType, fingerprint, false))
                    return d.ShowDialog(_owner.FindForm()) == DialogResult.OK;
            }, false);

        public void ShowHostKeyChanged(string host, string keyType, string fingerprint) =>
            OnUi(() =>
            {
                using (var d = new HostKeyDialog(host, keyType, fingerprint, true))
                    d.ShowDialog(_owner.FindForm());
                return true;
            }, true);

        public string Prompt(string title, string message, bool secret) =>
            OnUi(() => InputDialog.Ask(_owner.FindForm(), title, message, secret), null);

        public void Status(string message) => _status(message);
    }
}
