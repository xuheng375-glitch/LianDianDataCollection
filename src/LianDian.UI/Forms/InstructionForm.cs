using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LianDian.UI.Theme;
using Sunny.UI;

namespace LianDian.UI.Forms
{
    /// <summary>作业指导书（绝对定位，SunnyUI 按钮）：分页、放大/缩小图片、窗口放大/还原、滚轮缩放 + 拖动。</summary>
    public sealed class InstructionForm : UIForm
    {
        private const int SmallWidth = 980;
        private const int SmallHeight = 700;

        private readonly List<string> _images = new List<string>();
        private readonly UIImageButton _picture = new UIImageButton { SizeMode = PictureBoxSizeMode.Zoom, BackColor = FlatTheme.Panel2 };
        private readonly UILabel _pageLabel = new UILabel();
        private UIButton _enlargeBtn;
        private UIButton _closeBtn;
        private int _index;
        private float _zoom = 1F;
        private readonly UIPanel _viewport = new UIPanel();
        private Point _dragStart;
        private bool _dragging;

        public InstructionForm(string instructionDir, string productName)
        {
            Text = "作业指导书";
            FlatTheme.ApplyForm(this);
            FormBorderStyle = FormBorderStyle.None;
            AllowShowTitle = false;
            ShowTitle = false;
            ShowTitleIcon = false;
            TitleHeight = 0;
            ControlBox = false;
            ShowRect = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(SmallWidth, SmallHeight);
            BackColor = FlatTheme.Bg;
            ForeColor = FlatTheme.Text;
            Font = new Font("SimSun", 9F);
            Padding = new Padding(1);
            LoadImages(instructionDir, productName);
            BuildUi();
            ApplyLayout();
            ShowPage();
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private void LoadImages(string dir, string productName)
        {
            if (Directory.Exists(dir) && !string.IsNullOrWhiteSpace(productName))
            {
                string pattern = @"^(?:指导书_)?" + Regex.Escape(productName.Trim()) + @"(?:[_-](?:第)?\d+(?:页)?)?$";
                _images.AddRange(Directory.GetFiles(dir).Where(f =>
                    Regex.IsMatch(Path.GetExtension(f), @"^\.(png|jpg|jpeg|bmp|gif)$", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(Path.GetFileNameWithoutExtension(f), pattern, RegexOptions.IgnoreCase)));
            }
            _images.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(
                Regex.Replace(a, @"\d+", m => m.Value.PadLeft(12, '0')),
                Regex.Replace(b, @"\d+", m => m.Value.PadLeft(12, '0'))));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MaximumSize = Size.Empty;
            Bounds = Screen.FromControl(Owner ?? this).Bounds;
            ApplyLayout();
        }

        private static UIButton MakeButton(string text, Rectangle bounds)
        {
            return new UIButton
            {
                Text = text,
                Bounds = bounds,
                Font = FlatTheme.Ui,
                StyleCustomMode = true,
                Style = UIStyle.Custom,
                FillColor = FlatTheme.Surface,
                FillColor2 = FlatTheme.Surface,
                FillDisableColor = Color.FromArgb(30, 40, 47),
                RectColor = FlatTheme.CyanDark,
                RectDisableColor = Color.FromArgb(56, 68, 75),
                ForeColor = FlatTheme.Text,
                ForeDisableColor = FlatTheme.Text2,
                FillHoverColor = Color.FromArgb(23, 77, 90),
                FillPressColor = FlatTheme.CyanDark,
                RectHoverColor = FlatTheme.Cyan,
                RectPressColor = FlatTheme.Cyan,
                ForeHoverColor = Color.White,
                ForePressColor = Color.White,
                Radius = FlatTheme.Radius,
                Cursor = Cursors.Hand
            };
        }

        private static UILabel MakeLabel(string text, Rectangle bounds, Color fore, Font font)
        {
            return new UILabel
            {
                Text = text,
                Bounds = bounds,
                StyleCustomMode = true,
                Style = UIStyle.Custom,
                ForeColor = fore,
                Font = font,
                BackColor = FlatTheme.Bg
            };
        }

        private void BuildUi()
        {
            Controls.Add(MakeLabel("作业指导书", new Rectangle(24, 14, 200, 28), FlatTheme.Text,
                new Font("SimSun", 13F, FontStyle.Bold)));
            _pageLabel.TextAlign = ContentAlignment.MiddleCenter;
            _pageLabel.Font = FlatTheme.Ui;
            _pageLabel.ForeColor = FlatTheme.Text2;
            _pageLabel.BackColor = FlatTheme.Bg;
            _pageLabel.StyleCustomMode = true;
            _pageLabel.Style = UIStyle.Custom;
            FlatTheme.ApplyLabel(_pageLabel, FlatTheme.Text, FlatTheme.Bg);
            Controls.Add(_pageLabel);

            _enlargeBtn = MakeButton("适应窗口", new Rectangle(0, 14, 100, 30));
            _enlargeBtn.Click += (s, e) => { _zoom = 1F; FitImage(); };
            _closeBtn = MakeButton("关闭", new Rectangle(0, 14, 60, 30));
            _closeBtn.Click += (s, e) => Close();
            Controls.Add(_enlargeBtn);
            Controls.Add(_closeBtn);

            _picture.BorderStyle = BorderStyle.FixedSingle;
            _picture.BackgroundImageLayout = ImageLayout.Zoom;
            _viewport.StyleCustomMode = true;
            _viewport.Style = UIStyle.Custom;
            _viewport.FillColor = _viewport.FillColor2 = FlatTheme.Panel2;
            _viewport.RectColor = FlatTheme.Border;
            _viewport.Text = string.Empty;
            _viewport.Radius = 0;
            _picture.MouseWheel += (s, e) => ChangeZoom(e.Delta > 0 ? 0.1F : -0.1F);
            _picture.MouseDown += (s, e) => { _dragging = true; _dragStart = e.Location; };
            _picture.MouseMove += (s, e) =>
            {
                if (!_dragging) return;
                _picture.Location = new Point(_picture.Location.X + e.X - _dragStart.X,
                                              _picture.Location.Y + e.Y - _dragStart.Y);
            };
            _picture.MouseUp += (s, e) => _dragging = false;
            _viewport.Controls.Add(_picture);
            Controls.Add(_viewport);

            var prev = MakeButton("上一页", new Rectangle(24, 0, 90, 34));
            var next = MakeButton("下一页", new Rectangle(124, 0, 90, 34));
            var zoomIn = MakeButton("放大图片", new Rectangle(224, 0, 90, 34));
            var zoomOut = MakeButton("缩小图片", new Rectangle(324, 0, 90, 34));
            var hint = MakeLabel("滚轮缩放 · 按住拖动", new Rectangle(430, 0, 200, 22), FlatTheme.Text3, FlatTheme.UiSmall);
            prev.Click += (s, e) => { if (_index > 0) { _index--; ShowPage(); } };
            next.Click += (s, e) => { if (_index < _images.Count - 1) { _index++; ShowPage(); } };
            zoomIn.Click += (s, e) => ChangeZoom(0.2F);
            zoomOut.Click += (s, e) => ChangeZoom(-0.2F);
            Controls.Add(prev);
            Controls.Add(next);
            Controls.Add(zoomIn);
            Controls.Add(zoomOut);
            Controls.Add(hint);
        }

        /// <summary>按当前窗口尺寸重新排布控件（放大/还原时调用）。</summary>
        private void ApplyLayout()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            _pageLabel.Bounds = new Rectangle(w / 2 - 110, 14, 220, 28);
            _enlargeBtn.Bounds = new Rectangle(w - 200, 14, 110, 30);
            _closeBtn.Bounds = new Rectangle(w - 80, 14, 60, 30);
            _viewport.Bounds = new Rectangle(24, 56, w - 48, h - 140);
            FitImage();

            int by = h - 54;
            foreach (Control c in Controls)
            {
                if (c is UIButton && c != _enlargeBtn && c != _closeBtn)
                    c.Location = new Point(c.Location.X, by);
                if (c is UILabel && c.Text == "滚轮缩放 · 按住拖动")
                    c.Location = new Point(c.Location.X, by + 6);
            }
        }

        private void FitImage()
        {
            var image = _picture.BackgroundImage;
            if (image == null) { _picture.Bounds = _viewport.ClientRectangle; return; }
            double scale = Math.Min((double)_viewport.Width / image.Width, (double)_viewport.Height / image.Height) * _zoom;
            int w = Math.Max(1, (int)(image.Width * scale));
            int h = Math.Max(1, (int)(image.Height * scale));
            _picture.Bounds = new Rectangle((_viewport.Width-w)/2, (_viewport.Height-h)/2, w, h);
        }

        private void ShowPage()
        {
            if (_images.Count == 0)
            {
                _picture.BackgroundImage = null;
                _pageLabel.Text = "当前产品无匹配指导书";
                return;
            }
            int idx = Math.Max(0, Math.Min(_index, _images.Count - 1));
            _index = idx;
            Image img = ImageResolver.LoadSafely(_images[idx]);
            if (_picture.BackgroundImage != null) _picture.BackgroundImage.Dispose();
            _picture.BackgroundImage = img;
            _pageLabel.Text = img == null ? "图片加载失败" : (idx + 1) + " / " + _images.Count;
            _zoom = 1F;
            ApplyLayout();
        }

        private void ChangeZoom(float delta)
        {
            _zoom = Math.Max(0.3F, Math.Min(3F, _zoom + delta));
            FitImage();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _picture.BackgroundImage?.Dispose(); _picture.BackgroundImage = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var bg = new SolidBrush(FlatTheme.Bg))
                e.Graphics.FillRectangle(bg, 0, 0, Width, Height);
        }
    }
}
