using System.Drawing;
using System.Windows.Forms;
using LianDian.UI.Theme;
using Sunny.UI;

namespace LianDian.UI.Forms
{
    /// <summary>全 SunnyUI 的退出确认框，颜色使用主界面的共享主题。</summary>
    public sealed class ExitConfirmForm : UIForm
    {
        public ExitConfirmForm() : this("退出系统", "确定要退出数据收集系统吗？\r\n退出后将停止 PLC 通信和数据采集。", true) { }

        public ExitConfirmForm(string heading, string body, bool allowCancel)
        {
            FlatTheme.ApplyForm(this);
            Text = "退出确认";
            FormBorderStyle = FormBorderStyle.None;
            AllowShowTitle = false;
            ShowTitle = false;
            ShowTitleIcon = false;
            TitleHeight = 0;
            ControlBox = false;
            ShowRect = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 238);

            var title = new UILabel { Text = heading, Bounds = new Rectangle(28, 22, 420, 32), Font = FlatTheme.UiBold };
            FlatTheme.ApplyLabel(title, FlatTheme.Text, FlatTheme.Bg);
            var message = new UILabel
            {
                Text = body,
                Bounds = new Rectangle(28, 72, 424, 64),
                Font = FlatTheme.Ui,
                TextAlign = ContentAlignment.MiddleLeft
            };
            FlatTheme.ApplyLabel(message, FlatTheme.Text, FlatTheme.Bg);
            var cancel = MakeButton("取消", new Rectangle(224, 174, 104, 38), DialogResult.Cancel);
            var confirm = MakeButton("确认退出", new Rectangle(344, 174, 108, 38), DialogResult.OK);
            confirm.RectColor = FlatTheme.Cyan;
            cancel.Visible = allowCancel;
            if (!allowCancel) confirm.Text = "确定";
            Controls.AddRange(new Control[] { title, message, cancel, confirm });
            AcceptButton = allowCancel ? cancel : confirm; // 默认取消，避免按回车误退出。
            CancelButton = cancel;
            ActiveControl = allowCancel ? cancel : confirm;
        }

        private static UIButton MakeButton(string text, Rectangle bounds, DialogResult result)
        {
            var button = new UIButton
            {
                StyleCustomMode = true, Style = UIStyle.Custom,
                Text = text, Bounds = bounds, Font = FlatTheme.Ui,
                Radius = FlatTheme.Radius,
                FillColor = FlatTheme.Surface, FillColor2 = FlatTheme.Surface,
                ForeColor = FlatTheme.Text, RectColor = FlatTheme.Border,
                FillHoverColor = FlatTheme.CyanDark, FillPressColor = FlatTheme.CyanDark,
                ForeHoverColor = Color.White, ForePressColor = Color.White,
                RectHoverColor = FlatTheme.Cyan, RectPressColor = FlatTheme.Cyan,
                Cursor = Cursors.Hand
            };
            button.Click += (sender, args) => button.FindForm().DialogResult = result;
            return button;
        }
    }
}
