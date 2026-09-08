using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Sunny.UI;
using LianDian.UI.Theme;

namespace PanelTest
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new UIForm { ClientSize = new Size(600, 400), BackColor = FlatTheme.Bg })
            {
                FlatTheme.ApplyForm(form);
                var panel = new UIPanel
                {
                    Bounds = new Rectangle(20, 20, 300, 200),
                    StyleCustomMode = true, Style = UIStyle.Custom,
                    Text = "测试面板", FillColor = FlatTheme.Panel, ForeColor = FlatTheme.Text, RectColor = FlatTheme.Border
                };
                var label = new UILabel
                {
                    Text = "内容",
                    ForeColor = FlatTheme.Text,
                    BackColor = FlatTheme.Panel,
                    Bounds = new Rectangle(10, 60, 100, 30)
                };
                FlatTheme.ApplyLabel(label, FlatTheme.Text, FlatTheme.Panel);
                panel.Controls.Add(label);
                form.Controls.Add(panel);
                form.Shown += (s, e) =>
                {
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LianDian-panel-test.png"), ImageFormat.Png);
                    }
                    form.Close();
                };
                Application.Run(form);
            }
        }
    }
}
