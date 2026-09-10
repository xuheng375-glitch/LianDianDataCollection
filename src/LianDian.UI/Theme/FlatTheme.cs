using System.Drawing;
using Sunny.UI;

namespace LianDian.UI.Theme
{
    /// <summary>工业深色主题（对齐 AirtightInspection/Forms/IndustrialTheme.cs）。</summary>
    public static class FlatTheme
    {
        public static readonly Color Bg = Color.FromArgb(17, 36, 51);
        public static readonly Color Bg2 = Color.FromArgb(19, 41, 58);
        public static readonly Color Panel = Color.FromArgb(22, 45, 62);
        public static readonly Color Panel2 = Color.FromArgb(18, 38, 54);
        public static readonly Color Surface = Color.FromArgb(31, 59, 78);
        public static readonly Color Header = Color.FromArgb(31, 57, 75);
        public static readonly Color Border = Color.FromArgb(54, 82, 101);
        public static readonly Color BorderSoft = Color.FromArgb(40, 65, 83);

        public static readonly Color Cyan = Color.FromArgb(40, 192, 214);
        public static readonly Color CyanDark = Color.FromArgb(12, 133, 156);
        public static readonly Color Blue = Color.FromArgb(0, 188, 212);
        public static readonly Color Steel = Color.FromArgb(145, 166, 177);
        public static readonly Color Ok = Color.FromArgb(57, 211, 129);
        public static readonly Color Ng = Color.FromArgb(239, 83, 80);
        public static readonly Color Warn = Color.FromArgb(255, 183, 43);

        public static readonly Color Text = Color.FromArgb(225, 235, 240);
        public static readonly Color Text2 = Color.FromArgb(145, 166, 177);
        public static readonly Color Text3 = Color.FromArgb(155, 178, 190);

        // 先固定 SunnyUI 自定义样式，再赋具体颜色，避免默认主题覆盖文字色。
        public static void ApplyLabel(UILabel label, Color foreground, Color background)
        {
            label.StyleCustomMode = true;
            label.Style = UIStyle.Custom;
            label.ForeColor = foreground;
            label.BackColor = background;
        }

        public static void ApplyForm(UIForm form)
        {
            form.StyleCustomMode = true;
            form.Style = UIStyle.Custom;
            form.BackColor = Bg;
            form.ForeColor = Text;
            form.RectColor = Border;
        }

        public static readonly Color BgGrid = Color.FromArgb(10, 46, 62, 73);

        public static readonly Font Mono = new Font("Consolas", 10.5F);
        public static readonly Font MonoSmall = new Font("Consolas", 8.5F);
        public static readonly Font TableUi = new Font("Microsoft YaHei UI", 10F);
        public static readonly Font TableSmall = new Font("Microsoft YaHei UI", 8.5F);
        public static readonly Font MonoLarge = new Font("SimSun", 42F, FontStyle.Bold);
        public static readonly Font Ui = new Font("SimSun", 10F);
        public static readonly Font UiSmall = new Font("SimSun", 9F);
        public static readonly Font UiBold = new Font("SimSun", 12F, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("SimSun", 15F, FontStyle.Bold);
        public static readonly Font ClockFont = new Font("SimSun", 12F);

        public const int BrandHeight = 72;
        public const int CaptionHeight = 46;
        public const int Radius = 6;
    }
}
