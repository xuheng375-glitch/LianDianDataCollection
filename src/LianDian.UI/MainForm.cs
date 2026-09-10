using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LianDian.Business;
using LianDian.Business.Services;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Models;
using LianDian.UI.Forms;
using LianDian.UI.Theme;
using Sunny.UI;

namespace LianDian.UI
{
    /// <summary>主窗体：1920×1080 深色扁平工业科技风，全站 SunnyUI 控件（无心跳显示）。</summary>
    public sealed class MainForm : UIForm
    {
        private readonly SystemManager _manager;
        private readonly List<Font> _ownedFonts = new List<Font>();
        private readonly Icon _applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location);
        private readonly Timer _clockTimer;
        private readonly Timer _picTimer;

        private readonly UILedBulb _plcLamp = new UILedBulb();
        private readonly UILedBulb _dbLamp = new UILedBulb();
        private readonly UILabel _plcText = new UILabel();
        private readonly UILabel _dbText = new UILabel();
        private readonly UILabel _plcCaption = new UILabel();
        private readonly UILabel _dbCaption = new UILabel();
        private readonly UIPanel _statusDivider = new UIPanel();
        private readonly UIPanel _clockDivider = new UIPanel();
        private readonly UILabel _imageCaption = new UILabel();
        private readonly UISymbolLabel _unboundIcon = new UISymbolLabel();
        private readonly UILabel _clockLabel = new UILabel();
        private readonly UILabel _clockDateLabel = new UILabel();
        private readonly UILabel _batchNoLabel = new UILabel();
        private readonly UILabel _productValue = new UILabel();
        private readonly UILabel _employeeValue = new UILabel();
        private readonly UILabel _dateValue = new UILabel();
        private readonly UILabel _warnLabel = new UILabel();
        private readonly UIImageButton _productImage = new UIImageButton();
        private readonly UIDatePicker _fromPicker = new UIDatePicker();
        private readonly UIDatePicker _toPicker = new UIDatePicker();
        private readonly UIComboBox _productCombo = new UIComboBox();
        private readonly UIDataGridView _grid = new UIDataGridView();
        private static readonly Color Workspace = Color.FromArgb(238, 243, 247);
        private static readonly Color Ink = Color.FromArgb(30, 51, 68);
        private static readonly Color LightBorder = Color.FromArgb(209, 222, 231);
        private bool _logPaused;
        private readonly Queue<string> _pausedLogs = new Queue<string>();
        private UIButton _pauseLogButton;
        private readonly Font _ngBadgeFont = new Font("Consolas", 11F, FontStyle.Bold);
        private readonly UILabel _unboundLabel = new UILabel();
        private readonly UIDataGridView _eventLog = new UIDataGridView();
        private UIPanel _logPanel;
        private readonly Dictionary<string, DateTime> _recentLogMessages = new Dictionary<string, DateTime>();
        private UILabel _countLabel;
        private readonly IList<string> _allProducts = new List<string>();
        private readonly string _productPicDir;
        private readonly string _instructionDir;
        private string _currentProductName = string.Empty;
        private string _loadedImagePath;
        private DateTime _loadedImageTime;
        private volatile bool _closing;
        private bool _exitConfirmed;
        private bool _exitPromptOpen;
        private Task _productRefreshTask;
        private DateTime _imageRetryAfter;
        private bool _queryRunning;
        private bool _queryPending;
        private bool _exportRunning;
        private int _pageIndex;
        private const int PageSize = 500;
        private UIButton _previousPage;
        private UIButton _nextPage;
        private UIPanel _brandPanel, _batchPanel, _imagePanel, _queryPanel, _tablePanel;
        private UILabel _statusLabel;

        public MainForm(SystemManager manager)
        {
            _manager = manager;
            _productPicDir = ImageResolver.ResolveDir(manager.Config.Paths.ProductPicDir);
            _instructionDir = ImageResolver.ResolveDir(manager.Config.Paths.InstructionDir);

            Text = "联电数据收集";
            Icon = _applicationIcon;
            FlatTheme.ApplyForm(this);
            FormBorderStyle = FormBorderStyle.None;
            AllowShowTitle = false;
            ShowTitle = false;
            ShowTitleIcon = false;
            TitleHeight = 0;
            ControlBox = false;
            ShowRect = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1920, 1080);
            BackColor = FlatTheme.Bg;
            ForeColor = FlatTheme.Text;
            Font = OwnFont(new Font("SimSun", 9F));
            Padding = new Padding(1);
            DoubleBuffered = true;

            BuildBrandBar();
            BuildLeft();
            BuildRight();

            FlatTheme.ApplyLabel(_unboundLabel, FlatTheme.Text, FlatTheme.Panel);
            _unboundLabel.Font = FlatTheme.UiSmall;
            _unboundLabel.Text = "当前未绑定数据的数量：读取中";
            _queryPanel.Controls.Add(_unboundLabel);
            _unboundIcon.Symbol = 61546;
            _unboundIcon.SymbolSize = 22;
            _unboundIcon.Text = "";
            _unboundIcon.SymbolColor = Color.FromArgb(180, 110, 0);
            _queryPanel.Controls.Add(_unboundIcon);
            ApplyHybridTheme();
            WireEvents();
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(1280, 720);
            ClientSizeChanged += (s, e) => ApplyResponsiveLayout();

            _clockTimer = new Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => SafeInvoke(RefreshClock);
            _clockTimer.Start();
            RefreshClock();

            _picTimer = new Timer { Interval = 1500 };
            _picTimer.Tick += (s, e) => SafeInvoke(() => { RefreshCurrentBatch(); RefreshProductImage(); RefreshLamps(); });
            _picTimer.Start();

            RefreshLamps();
            RefreshCurrentBatch();
            RefreshProductImage();
            _productCombo.DataSource = new List<string> { "全部产品" };
            _productCombo.Text = "全部产品";
            Shown += (s, e) =>
            {
                Bounds = Screen.FromControl(this).WorkingArea;
                ApplyResponsiveLayout();
                RefreshProductCombo();
                DoQuery();
            };
        }

        public AppConfig Config => _manager.Config;

        private void ApplyHybridTheme()
        {
            foreach (var panel in new[] { _queryPanel, _tablePanel })
            {
                StylePanel(panel, Color.White);
                panel.BackColor = Workspace;
                panel.RectColor = LightBorder;
                panel.Radius = 6;
                foreach (Control control in panel.Controls)
                {
                    if (control is UILabel label) FlatTheme.ApplyLabel(label, Ink, Color.White);
                    if (control is UIPanel divider && divider.Height == 1)
                    { StylePanel(divider, LightBorder); divider.RectColor = LightBorder; }
                    if (control is UIButton button)
                    {
                        button.BackColor = Color.White;
                        button.Radius = 10;
                        button.FillColor = button.FillColor2 = Color.White;
                        button.ForeColor = Ink;
                        button.RectColor = LightBorder;
                        button.FillDisableColor = Workspace;
                        button.ForeDisableColor = Color.FromArgb(100, 119, 133);
                        if (button.Text == "查 询")
                        { button.FillColor = button.FillColor2 = FlatTheme.CyanDark; button.ForeColor = Color.White; }
                    }
                }
            }
            foreach (var picker in new[] { _fromPicker, _toPicker })
            { picker.BackColor = Color.White; picker.FillColor = Color.White; picker.ForeColor = Ink; picker.RectColor = LightBorder; }
            _productCombo.BackColor = Color.White;
            _productCombo.FillColor = _productCombo.ItemFillColor = Color.White;
            _productCombo.ForeColor = _productCombo.ItemForeColor = Ink;
            _productCombo.RectColor = _productCombo.ItemRectColor = LightBorder;
            _productCombo.ItemHoverColor = Workspace;
            _grid.BackgroundColor = Color.FromArgb(250, 252, 254);
            _grid.GridColor = LightBorder;
            _grid.DefaultCellStyle.BackColor = Color.White;
            _grid.DefaultCellStyle.ForeColor = Ink;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 245, 249);
            _grid.DefaultCellStyle.SelectionForeColor = Ink;
            _grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            _grid.RowsDefaultCellStyle = _grid.DefaultCellStyle.Clone();
            _grid.AlternatingRowsDefaultCellStyle = _grid.DefaultCellStyle.Clone();
            _grid.StripeEvenColor = Color.White;
            _grid.StripeOddColor = Color.FromArgb(247, 250, 252);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = _grid.StripeOddColor;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 244, 248);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 244, 248);
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            _grid.ColumnHeadersHeight = 60;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 24, 8, 0);
            _grid.Paint += PaintBusinessGroups;
            _grid.SelectionChanged += (s, e) => _grid.Invalidate(new Rectangle(0, 0, _grid.Width, _grid.ColumnHeadersHeight));
            _grid.Scroll += (s, e) => _grid.Invalidate();
            _grid.ScrollBarBackColor = Workspace;
            _grid.ScrollBarColor = Color.FromArgb(143, 164, 179);
            foreach (string name in new[] { "电压", "电阻", "电流", "气压" })
                _grid.Columns[name].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["气压"].HeaderText = "气密值";
            _grid.Columns["气压结果"].HeaderText = "气密结果";
            FlatTheme.ApplyLabel(_unboundLabel, Color.FromArgb(160, 93, 0), Color.FromArgb(255, 246, 225));
            _unboundLabel.TextAlign = ContentAlignment.MiddleLeft;
            _unboundLabel.Padding = new Padding(38, 0, 0, 0);
            _productImage.BackColor = Workspace;
            _unboundLabel.Font = FlatTheme.Ui;
            foreach (var panel in new[] { _batchPanel, _imagePanel, _logPanel }) panel.Radius = 6;
            _grid.DefaultCellStyle.Font = FlatTheme.TableUi;
            _grid.RowTemplate.Height = 40;
            _eventLog.GridColor = FlatTheme.BorderSoft;
            _eventLog.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _eventLog.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _eventLog.ColumnHeadersDefaultCellStyle.SelectionForeColor = FlatTheme.Text2;
            _eventLog.DefaultCellStyle.SelectionBackColor = FlatTheme.Surface;
            _eventLog.RowsDefaultCellStyle.SelectionBackColor = FlatTheme.Surface;
            _eventLog.AlternatingRowsDefaultCellStyle.SelectionBackColor = FlatTheme.Surface;
            _eventLog.StripeEvenColor = _eventLog.StripeOddColor = FlatTheme.Panel;
            _eventLog.ScrollBarStyleInherited = false;
            _eventLog.ScrollBarBackColor = FlatTheme.Panel;
            _eventLog.ScrollBarColor = FlatTheme.Border;
            foreach (Control c in _brandPanel.Controls.OfType<UIButton>())
                ((UIButton)c).RectColor = FlatTheme.Border;
            FlatTheme.ApplyLabel(_statusLabel, FlatTheme.Text2, FlatTheme.Bg);
            FlatTheme.ApplyLabel(_warnLabel, Color.FromArgb(180, 65, 30), Workspace);
        }

        private void PaintBusinessGroups(object sender, PaintEventArgs e)
        {
            string[][] groups = {
                new[] { "批次信息", "时间", "批次日期", "批次号", "产品名称", "员工工号" },
                new[] { "耐压数据", "电压", "电阻", "电流", "耐压结果" },
                new[] { "气密数据", "气压", "气压结果" },
                new[] { "二维码", "二维码等级" }
            };
            foreach (var group in groups)
            {
                int start = -_grid.HorizontalScrollingOffset;
                foreach (var column in _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.DisplayIndex < _grid.Columns[group[1]].DisplayIndex)) start += column.Width;
                int width = group.Skip(1).Sum(name => _grid.Columns[name].Width);
                var area = Rectangle.Intersect(new Rectangle(start, 0, width, 24), new Rectangle(0, 0, _grid.ClientSize.Width, 24));
                if (area.Width <= 0) continue;
                using (var brush = new SolidBrush(Color.FromArgb(227, 237, 245))) e.Graphics.FillRectangle(brush, area);
                using (var pen = new Pen(LightBorder)) e.Graphics.DrawRectangle(pen, area);
                TextRenderer.DrawText(e.Graphics, group[0], FlatTheme.TableSmall, area, Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------- 控件工厂 ----------

        private UIPanel CreatePanel(Rectangle bounds, string caption, string captionEn, Color mark, string captionRight)
        {
            var p = new UIPanel { Bounds = bounds };
            StylePanel(p, FlatTheme.Panel);
            if (string.IsNullOrEmpty(caption)) return p;

            var markCtl = new UIPanel { Bounds = new Rectangle(18, 19, 8, 8) };
            StylePanel(markCtl, mark);
            markCtl.RectColor = mark;
            p.Controls.Add(markCtl);
            p.Controls.Add(CreateLabel(caption, new Rectangle(34, 11, 240, 22), FlatTheme.Text,
                OwnFont(new Font("SimSun", 10F, FontStyle.Bold)), ContentAlignment.MiddleLeft, FlatTheme.Panel));
            if (!string.IsNullOrEmpty(captionEn))
                p.Controls.Add(CreateLabel(captionEn, new Rectangle(p.Width - 190, 16, 170, 14), FlatTheme.Text3,
                    FlatTheme.UiSmall, ContentAlignment.MiddleRight, FlatTheme.Panel));
            if (!string.IsNullOrEmpty(captionRight))
                p.Controls.Add(CreateLabel(captionRight, new Rectangle(p.Width - 220, 16, 200, 14), FlatTheme.Text3,
                    FlatTheme.UiSmall, ContentAlignment.MiddleRight, FlatTheme.Panel));

            var divider = new UIPanel { Bounds = new Rectangle(1, 46, p.Width - 2, 1) };
            StylePanel(divider, FlatTheme.BorderSoft);
            divider.RectColor = FlatTheme.BorderSoft;
            p.Controls.Add(divider);
            return p;
        }

        private static void StylePanel(UIPanel p, Color fill)
        {
            p.StyleCustomMode = true;
            p.Style = UIStyle.Custom;
            p.FillColor = fill;
            p.FillColor2 = fill;
            p.RectColor = FlatTheme.Border;
            p.Radius = FlatTheme.Radius;
            p.Text = string.Empty;
        }

        private static UILabel CreateLabel(string text, Rectangle bounds, Color fore, Font font,
            ContentAlignment align, Color back)
        {
            return new UILabel
            {
                Text = text,
                Bounds = bounds,
                StyleCustomMode = true,
                Style = UIStyle.Custom,
                ForeColor = fore,
                Font = font,
                TextAlign = align,
                BackColor = back
            };
        }

        private UIButton CreateButton(string text, Rectangle bounds, bool primary)
        {
            var btn = new UISymbolButton
            {
                Text = text,
                Bounds = bounds,
                Font = FlatTheme.Ui,
                StyleCustomMode = true,
                Style = UIStyle.Custom,
                Radius = 10,
                BackColor = FlatTheme.Bg2,
                FillColor = FlatTheme.Surface,
                FillColor2 = FlatTheme.Surface,
                RectColor = primary ? FlatTheme.Cyan : FlatTheme.CyanDark,
                ForeColor = FlatTheme.Text,
                FillHoverColor = Color.FromArgb(23, 77, 90),
                FillPressColor = FlatTheme.CyanDark,
                RectHoverColor = FlatTheme.Cyan,
                RectPressColor = FlatTheme.Cyan,
                ForeHoverColor = Color.White,
                ForePressColor = Color.White,
                FillDisableColor = Color.FromArgb(30, 40, 47),
                RectDisableColor = Color.FromArgb(56, 68, 75),
                ForeDisableColor = FlatTheme.Text2,
                Cursor = Cursors.Hand
            };
            btn.Symbol = text == "查 询" ? 61442 : text == "导出 CSV" ? 61639 :
                text == "作业指导书" ? 61485 : text == "退出" ? 61579 :
                text == "暂停滚动" ? 61516 : text == "上一页" ? 61700 : 61701;
            btn.SymbolSize = 18;
            if (primary)
            {
                btn.Font = OwnFont(new Font(FlatTheme.Ui.FontFamily, 11F, FontStyle.Bold));
            }
            return btn;
        }

        // ---------- 品牌栏 ----------

        private void BuildBrandBar()
        {
            var brand = CreatePanel(new Rectangle(24, 20, 1872, 72), null, null, FlatTheme.Cyan, null);
            _brandPanel = brand;
            brand.FillColor = FlatTheme.Bg2;
            brand.FillColor2 = FlatTheme.Bg2;
            var accent = new UIPanel { Bounds = new Rectangle(24, 90, 1872, 2) };
            StylePanel(accent, FlatTheme.Cyan);
            accent.RectColor = FlatTheme.Cyan;

            var title = CreateLabel("联电数据收集", new Rectangle(22, 8, 260, 34), FlatTheme.Text,
                OwnFont(new Font("SimSun", 15F, FontStyle.Bold)), ContentAlignment.MiddleLeft, FlatTheme.Bg2);
            var subtitle = CreateLabel("LIANDIAN DATA COLLECTION · SCADA", new Rectangle(24, 42, 360, 16), FlatTheme.Steel,
                FlatTheme.UiSmall, ContentAlignment.MiddleLeft, FlatTheme.Bg2);
            var divider = new UIPanel { Bounds = new Rectangle(250, 19, 1, 34) };
            StylePanel(divider, FlatTheme.Border);
            divider.RectColor = FlatTheme.Border;

            _plcLamp.Location = new Point(280, 31);
            _dbLamp.Location = new Point(366, 31);
            _plcLamp.Size = _dbLamp.Size = new Size(12, 12);
            _plcLamp.Color = _dbLamp.Color = FlatTheme.Ok;
            _plcLamp.Blink = _dbLamp.Blink = false;
            var plcLabel = CreateLabel("PLC", new Rectangle(294, 27, 40, 20), FlatTheme.Text2, FlatTheme.UiSmall,
                ContentAlignment.MiddleLeft, FlatTheme.Bg2);
            var dbLabel = CreateLabel("数据库", new Rectangle(380, 27, 60, 20), FlatTheme.Text2, FlatTheme.UiSmall,
                ContentAlignment.MiddleLeft, FlatTheme.Bg2);

            _clockLabel.Font = FlatTheme.ClockFont;
            FlatTheme.ApplyLabel(_clockLabel, FlatTheme.Text, FlatTheme.Bg2);
            FlatTheme.ApplyLabel(_clockDateLabel, FlatTheme.Text3, FlatTheme.Bg2);
            _clockLabel.ForeColor = FlatTheme.Text;
            _clockLabel.BackColor = FlatTheme.Bg2;
            _clockLabel.TextAlign = ContentAlignment.MiddleRight;
            _clockLabel.Bounds = new Rectangle(1470, 8, 210, 26);
            _clockDateLabel.Font = FlatTheme.UiSmall;
            _clockDateLabel.ForeColor = FlatTheme.Text3;
            _clockDateLabel.BackColor = FlatTheme.Bg2;
            _clockDateLabel.TextAlign = ContentAlignment.MiddleRight;
            _clockDateLabel.Bounds = new Rectangle(1490, 40, 190, 18);

            var instrBtn = CreateButton("作业指导书", new Rectangle(1702, 20, 100, 32), false);
            instrBtn.Click += (s, e) =>
            {
                RefreshCurrentBatch();
                using (var form = new InstructionForm(_instructionDir, _currentProductName)) form.ShowDialog(this);
            };
            var exitBtn = CreateButton("退出", new Rectangle(1812, 20, 60, 32), false);
            exitBtn.Click += (s, e) =>
            {
                Close();
            };

            brand.Controls.AddRange(new Control[]
            {
                title, subtitle, divider, _plcLamp, plcLabel, _dbLamp, dbLabel,
                _clockLabel, _clockDateLabel, instrBtn, exitBtn
            });
            Controls.Add(brand);
            Controls.Add(accent);
            _plcLamp.Visible = _dbLamp.Visible = plcLabel.Visible = dbLabel.Visible = divider.Visible = false;
            FlatTheme.ApplyLabel(_plcText, FlatTheme.Ok, FlatTheme.Bg2);
            FlatTheme.ApplyLabel(_dbText, FlatTheme.Ok, FlatTheme.Bg2);
            _plcText.Font = _dbText.Font = FlatTheme.UiBold;
            brand.Controls.Add(_plcText);
            brand.Controls.Add(_dbText);
            FlatTheme.ApplyLabel(_plcCaption, FlatTheme.Text, FlatTheme.Bg2);
            FlatTheme.ApplyLabel(_dbCaption, FlatTheme.Text, FlatTheme.Bg2);
            _plcCaption.Text = "PLC";
            _dbCaption.Text = "数据库";
            _plcCaption.Font = _dbCaption.Font = FlatTheme.UiBold;
            foreach (var label in new[] { _plcCaption, _dbCaption, _plcText, _dbText })
                label.TextAlign = ContentAlignment.MiddleLeft;
            StylePanel(_statusDivider, FlatTheme.BorderSoft);
            StylePanel(_clockDivider, FlatTheme.BorderSoft);
            brand.Controls.AddRange(new Control[] { _plcCaption, _dbCaption, _statusDivider, _clockDivider });
        }

        // ---------- 左列 ----------

        private void BuildLeft()
        {
            var batchPanel = CreatePanel(new Rectangle(24, 108, 440, 210), "当前批次", "CURRENT BATCH", FlatTheme.Cyan, null);
            _batchPanel = batchPanel;
            _batchNoLabel.Font = FlatTheme.MonoLarge;
            FlatTheme.ApplyLabel(_batchNoLabel, FlatTheme.Cyan, FlatTheme.Panel);
            _batchNoLabel.ForeColor = FlatTheme.Cyan;
            _batchNoLabel.BackColor = FlatTheme.Panel;
            _batchNoLabel.TextAlign = ContentAlignment.MiddleCenter;
            _batchNoLabel.Bounds = new Rectangle(20, 50, 400, 84);
            var meta = new MetaRowPanel { Bounds = new Rectangle(20, 146, 400, 52) };
            AddMeta(meta, _productValue, "产品名称", 0);
            AddMeta(meta, _employeeValue, "员工工号", 1);
            AddMeta(meta, _dateValue, "日期编码", 2);
            batchPanel.Controls.Add(_batchNoLabel);
            var batchIcon = new UISymbolLabel { StyleCustomMode = true, Style = UIStyle.Custom, Symbol = 61451, SymbolSize = 24, Bounds = new Rectangle(20, 66, 32, 32),
                SymbolColor = FlatTheme.Text2, BackColor = FlatTheme.Panel, Text = "" };
            batchPanel.Controls.Add(batchIcon);
            batchPanel.Controls.Add(meta);
            Controls.Add(batchPanel);

            var imagePanel = CreatePanel(new Rectangle(24, 334, 440, 726), "产品图片", "PRODUCT IMAGE", FlatTheme.Blue, null);
            _imagePanel = imagePanel;
            _productImage.Bounds = new Rectangle(18, 54, 404, 654);
            _productImage.SizeMode = PictureBoxSizeMode.Zoom;
            // 使用背景图的 Zoom 布局，避免 UIImageButton 前景图按按钮原始尺寸绘制而裁切。
            _productImage.BackgroundImageLayout = ImageLayout.Zoom;
            _productImage.ImageOffset = Point.Empty;
            _productImage.BackColor = FlatTheme.Bg2;
            _productImage.TabStop = false;
            _productImage.Cursor = Cursors.Default;
            imagePanel.Controls.Add(_productImage);
            FlatTheme.ApplyLabel(_imageCaption, FlatTheme.Text, FlatTheme.Panel);
            _imageCaption.Font = FlatTheme.UiBold;
            _imageCaption.TextAlign = ContentAlignment.MiddleCenter;
            imagePanel.Controls.Add(_imageCaption);
            Controls.Add(imagePanel);
            _logPanel = CreatePanel(new Rectangle(24, 800, 440, 240), "重要日志", null, FlatTheme.Cyan, null);
            _eventLog.StyleCustomMode = true;
            _eventLog.Style = UIStyle.Custom;
            _eventLog.ReadOnly = true;
            _eventLog.AllowUserToAddRows = false;
            _eventLog.AllowUserToDeleteRows = false;
            _eventLog.RowHeadersVisible = false;
            _eventLog.ColumnHeadersVisible = true;
            _eventLog.EnableHeadersVisualStyles = false;
            _eventLog.ColumnHeadersDefaultCellStyle.BackColor = FlatTheme.Panel;
            _eventLog.ColumnHeadersDefaultCellStyle.ForeColor = FlatTheme.Text2;
            _eventLog.ColumnHeadersDefaultCellStyle.SelectionBackColor = FlatTheme.Panel;
            _eventLog.ColumnHeadersHeight = 28;
            _eventLog.BackgroundColor = FlatTheme.Panel;
            _eventLog.BorderStyle = BorderStyle.None;
            _eventLog.DefaultCellStyle = new DataGridViewCellStyle { BackColor=FlatTheme.Panel, ForeColor=FlatTheme.Text,
                SelectionBackColor=FlatTheme.CyanDark, SelectionForeColor=Color.White, Font=FlatTheme.TableSmall,
                WrapMode=DataGridViewTriState.True };
            _eventLog.RowsDefaultCellStyle = _eventLog.DefaultCellStyle;
            _eventLog.AlternatingRowsDefaultCellStyle = _eventLog.DefaultCellStyle;
            _eventLog.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _eventLog.Columns.Add("time", "时间");
            _eventLog.Columns[0].Width = 74;
            _eventLog.Columns.Add("message", "日志内容");
            _eventLog.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _eventLog.Columns[1].SortMode = DataGridViewColumnSortMode.NotSortable;
            _eventLog.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            _logPanel.Controls.Add(_eventLog);
            _pauseLogButton = CreateButton("暂停滚动", new Rectangle(300, 8, 110, 30), false);
            _pauseLogButton.Click += (s, e) =>
            {
                _logPaused = !_logPaused;
                _pauseLogButton.Text = _logPaused ? "继续滚动" : "暂停滚动";
                ((UISymbolButton)_pauseLogButton).Symbol = _logPaused ? 61515 : 61516;
                if (!_logPaused) while (_pausedLogs.Count > 0) InsertLogRow(_pausedLogs.Dequeue());
            };
            _logPanel.Controls.Add(_pauseLogButton);
            Controls.Add(_logPanel);
            AddImportantLog("系统界面已启动。");
        }

        private void AddMeta(MetaRowPanel panel, UILabel valueLabel, string key, int index)
        {
            int w = panel.Width / 3;
            int x = index * w;
            var k = CreateLabel(key, new Rectangle(x, 2, w, 18), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleCenter, FlatTheme.Panel);
            valueLabel.Font = OwnFont(new Font("SimSun", 11F));
            FlatTheme.ApplyLabel(valueLabel, FlatTheme.Text, FlatTheme.Panel);
            valueLabel.ForeColor = FlatTheme.Text;
            valueLabel.BackColor = FlatTheme.Panel;
            valueLabel.TextAlign = ContentAlignment.MiddleCenter;
            valueLabel.Bounds = new Rectangle(x, 24, w, 24);
            panel.Controls.Add(k);
            panel.Controls.Add(valueLabel);
            panel.Controls.Add(new UISymbolLabel { StyleCustomMode = true, Style = UIStyle.Custom, Name = "metaIcon" + index, Symbol = index == 0 ? 61874 : index == 1 ? 61447 : 61555,
                SymbolSize = 20, SymbolColor = FlatTheme.Text2, BackColor = FlatTheme.Panel, Text = "" });
        }

        // ---------- 右列 ----------

        private void BuildRight()
        {
            var queryPanel = CreatePanel(new Rectangle(480, 108, 1416, 110), "数据查询", "QUERY", FlatTheme.Cyan, null);
            _queryPanel = queryPanel;
            var fromLabel = CreateLabel("开始日期", new Rectangle(96, 52, 176, 16), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleCenter, FlatTheme.Panel);
            var toLabel = CreateLabel("结束日期", new Rectangle(378, 52, 176, 16), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleCenter, FlatTheme.Panel);
            var proLabel = CreateLabel("产品名称（选择或输入查询）", new Rectangle(696, 52, 260, 16), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleCenter, FlatTheme.Panel);

            StyleDatePicker(_fromPicker);
            StyleDatePicker(_toPicker);
            _fromPicker.Bounds = new Rectangle(96, 70, 176, 30);
            _toPicker.Bounds = new Rectangle(378, 70, 176, 30);
            _fromPicker.Value = DateTime.Today.AddDays(-7);
            _toPicker.Value = DateTime.Today;

            _productCombo.Bounds = new Rectangle(696, 70, 260, 30);
            _productCombo.StyleCustomMode = true;
            _productCombo.Style = UIStyle.Custom;
            _productCombo.FillColor = FlatTheme.Panel2;
            _productCombo.FillDisableColor = Color.FromArgb(20, 29, 36);
            _productCombo.RectColor = FlatTheme.Border;
            _productCombo.RectDisableColor = Color.FromArgb(50, 64, 72);
            _productCombo.ForeColor = FlatTheme.Text;
            _productCombo.ForeDisableColor = FlatTheme.Text2;
            _productCombo.Font = FlatTheme.Ui;
            _productCombo.Radius = FlatTheme.Radius;
            // 下拉箭头始终列出全部产品；不能把当前显示的“全部产品”或已选产品作为过滤词。
            _productCombo.ShowFilter = false;
            _productCombo.FilterIgnoreCase = true;
            _productCombo.ItemFillColor = FlatTheme.Panel2;
            _productCombo.ItemForeColor = FlatTheme.Text;
            _productCombo.ItemHoverColor = FlatTheme.Surface;
            _productCombo.ItemSelectBackColor = FlatTheme.CyanDark;
            _productCombo.ItemSelectForeColor = Color.White;
            _productCombo.ItemRectColor = FlatTheme.BorderSoft;
            _productCombo.ItemHeight = 28;
            new ProductDropdownRefresh(_productCombo, () => RefreshProductComboAsync(true));

            var btn = CreateButton("查 询", new Rectangle(1000, 68, 100, 38), true);
            btn.Click += (s, e) => { _pageIndex = 0; DoQuery(); };
            var exportBtn = CreateButton("导出 CSV", new Rectangle(1120, 68, 120, 38), false);
            exportBtn.Click += (s, e) => ExportData();

            queryPanel.Controls.AddRange(new Control[]
            {
                fromLabel, toLabel, proLabel, _fromPicker, _toPicker, _productCombo, btn, exportBtn
            });
            Controls.Add(queryPanel);

            _countLabel = CreateLabel("共 0 条", new Rectangle(0, 0, 200, 14), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleRight, FlatTheme.Panel);
            var tablePanel = CreatePanel(new Rectangle(480, 234, 1416, 792), "测试记录", null, FlatTheme.Cyan, null);
            _tablePanel = tablePanel;
            _countLabel.Bounds = new Rectangle(tablePanel.Width - 220, 16, 200, 14);
            tablePanel.Controls.Add(_countLabel);
            _previousPage = CreateButton("上一页", new Rectangle(920, 8, 82, 30), false);
            _nextPage = CreateButton("下一页", new Rectangle(1012, 8, 82, 30), false);
            _previousPage.Click += (s, e) => { if (_pageIndex > 0) { _pageIndex--; DoQuery(); } };
            _nextPage.Click += (s, e) => { _pageIndex++; DoQuery(); };
            tablePanel.Controls.AddRange(new Control[] { _previousPage, _nextPage });
            InitGrid();
            _grid.Bounds = new Rectangle(0, 46, 1416, 746);
            tablePanel.Controls.Add(_grid);
            Controls.Add(tablePanel);

            var statusLabel = CreateLabel("系统运行正常", new Rectangle(602, 1038, 200, 20), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleLeft, FlatTheme.Bg);
            _statusLabel = statusLabel;
            _warnLabel.Font = FlatTheme.UiSmall;
            FlatTheme.ApplyLabel(_warnLabel, FlatTheme.Warn, FlatTheme.Bg);
            _warnLabel.ForeColor = FlatTheme.Warn;
            _warnLabel.BackColor = FlatTheme.Bg;
            _warnLabel.TextAlign = ContentAlignment.MiddleRight;
            _warnLabel.Bounds = new Rectangle(1300, 1036, 590, 24);
            Controls.Add(statusLabel);
            Controls.Add(_warnLabel);
        }

        private static void StyleDatePicker(UIDatePicker picker)
        {
            picker.DateFormat = "yyyy-MM-dd";
            picker.StyleCustomMode = true;
            picker.Style = UIStyle.Custom;
            picker.FillColor = FlatTheme.Panel2;
            picker.FillDisableColor = Color.FromArgb(20, 29, 36);
            picker.RectColor = FlatTheme.Border;
            picker.RectDisableColor = Color.FromArgb(50, 64, 72);
            picker.ForeColor = FlatTheme.Text;
            picker.ForeDisableColor = FlatTheme.Text2;
            picker.Font = FlatTheme.Ui;
            picker.Radius = FlatTheme.Radius;
            picker.Height = 30;
            picker.ShowToday = true;
        }

        private void InitGrid()
        {
            _grid.StyleCustomMode = true;
            _grid.Style = UIStyle.Custom;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.BackgroundColor = FlatTheme.Panel;
            _grid.BorderStyle = BorderStyle.None;
            _grid.EnableHeadersVisualStyles = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = 36;
            _grid.RowTemplate.Height = 36;
            _grid.DefaultCellStyle.BackColor = FlatTheme.Panel;
            _grid.DefaultCellStyle.SelectionBackColor = FlatTheme.CyanDark;
            _grid.DefaultCellStyle.SelectionForeColor = FlatTheme.Text;
            _grid.DefaultCellStyle.ForeColor = FlatTheme.Text;
            _grid.DefaultCellStyle.Font = OwnFont(new Font("Microsoft YaHei UI", 9.5F));
            // 按用户调整后的截图保留横向留白；垂直居中，避免 36px 行内文字被上下内边距裁切。
            _grid.DefaultCellStyle.Padding = new Padding(16, 0, 12, 0);
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.RowsDefaultCellStyle = _grid.DefaultCellStyle.Clone();
            _grid.AlternatingRowsDefaultCellStyle = _grid.DefaultCellStyle.Clone();
            _grid.AlternatingRowsDefaultCellStyle.BackColor = FlatTheme.Panel;
            // SunnyUI 自带条纹配色，统一为同一深色（用户要求不做隔行区分）
            _grid.StripeEvenColor = FlatTheme.Panel;
            _grid.StripeOddColor = FlatTheme.Panel;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = FlatTheme.Header;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = FlatTheme.Cyan;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = FlatTheme.Header;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = FlatTheme.Cyan;
            _grid.ColumnHeadersDefaultCellStyle.Font = OwnFont(new Font("Microsoft YaHei UI", 9F, FontStyle.Bold));
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(16, 0, 12, 0);
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _grid.GridColor = FlatTheme.BorderSoft;
            // SunnyUI 滚动条改为暗色细条，融入深色主题
            _grid.ScrollBarStyleInherited = false;
            _grid.ScrollBarBackColor = FlatTheme.Panel2;
            _grid.ScrollBarColor = Color.FromArgb(58, 74, 100);
            _grid.ScrollBarRectColor = FlatTheme.Border;
            _grid.ScrollBarWidth = 10;
            _grid.ScrollBarHandleWidth = 6;
            _grid.CellPainting += OnCellPainting;

            string[] cols = { "批次日期", "批次号", "产品名称", "员工工号", "电压", "电阻", "电流", "耐压结果", "气压", "气压结果", "时间", "二维码等级" };
            foreach (string c in cols)
            {
                var col = new DataGridViewTextBoxColumn { Name = c, HeaderText = c, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };
                _grid.Columns.Add(col);
            }
            _grid.Columns["批次日期"].Width = 95;
            _grid.Columns["批次号"].Width = 85;
            _grid.Columns["产品名称"].Width = 110;
            _grid.Columns["员工工号"].Width = 100;
            _grid.Columns["电压"].Width = 140;
            _grid.Columns["电阻"].Width = 140;
            _grid.Columns["电流"].Width = 155;
            _grid.Columns["耐压结果"].Width = 95;
            _grid.Columns["气压"].Width = 100;
            _grid.Columns["气压结果"].Width = 100;
            _grid.Columns["时间"].Width = 205;
            _grid.Columns["时间"].DisplayIndex = 0;
            // 参考图最右列被截图截断，保留足够宽度显示完整标题。
            _grid.Columns["二维码等级"].Width = 120;
            _grid.Columns["二维码等级"].MinimumWidth = 115;
            _grid.Columns["二维码等级"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        private void OnCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            // 所有数据单元格共用同一种边框，避免默认选中态与徽章自绘混用边框颜色。
            var state = e.Graphics.Save();
            try
            {
                string name = _grid.Columns[e.ColumnIndex].Name;
                string value = e.Value as string;
                bool badgeCell = (name == "耐压结果" || name == "气压结果") && (value == "OK" || value == "NG");
                if (!badgeCell)
                    e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border & ~DataGridViewPaintParts.Focus);
                else
                {
                    e.PaintBackground(e.CellBounds, true);
                    Color color = value == "OK" ? Color.FromArgb(26, 128, 65) : Color.FromArgb(200, 40, 45);
                    var rect = e.CellBounds;
                    var badge = new Rectangle(rect.X + (rect.Width - 46) / 2, rect.Y + (rect.Height - 22) / 2, 46, 22);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = RoundedRect(badge, 3))
                    {
                        using (var fill = new SolidBrush(Color.FromArgb(26, color))) e.Graphics.FillPath(fill, path);
                        using (var pen = new Pen(Color.FromArgb(90, color))) e.Graphics.DrawPath(pen, path);
                    }
                    TextRenderer.DrawText(e.Graphics, value, value == "NG" ? _ngBadgeFont : FlatTheme.MonoSmall, badge, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                e.Graphics.SmoothingMode = SmoothingMode.None;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Default;
                using (var pen = new Pen(Color.FromArgb(170, 188, 200)))
                {
                    var rect = e.CellBounds;
                    e.Graphics.DrawLine(pen, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
                    e.Graphics.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
                }
                e.Handled = true;
            }
            finally { e.Graphics.Restore(state); }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------- 数据与事件 ----------

        private Font OwnFont(Font font) { _ownedFonts.Add(font); return font; }

        private void WireEvents()
        {
            _manager.Plc.ConnectionChanged += OnPlcChanged;
            _manager.DbHealth.HealthChanged += OnHealthChanged;
            _manager.Batch.BatchIssued += OnBatchIssued;
            _manager.Withstand.RecordUploaded += OnUpload;
            _manager.Pressure.RecordUploaded += OnUpload;
            _manager.QrUpload.RecordUploaded += OnQrUploaded;
            _manager.QrUpload.ErrorOccurred += OnUploadError;
            _manager.Withstand.DataMismatch += OnMismatch;
            _manager.Pressure.DataMismatch += OnMismatch;
            _manager.Batch.ErrorOccurred += OnBatchError;
            _manager.Withstand.ErrorOccurred += OnUploadError;
            _manager.Pressure.ErrorOccurred += OnUploadError;
        }

        private void OnPlcChanged(object sender, EventArgs e) => SafeInvoke(() => { RefreshLamps(); AddImportantLog(_manager.Plc.Client.IsConnected ? "PLC已连接" : "PLC连接断开"); });
        private void OnHealthChanged(object sender, EventArgs e) => SafeInvoke(() => { RefreshLamps(); AddImportantLog(_manager.DbHealth.IsHealthy ? "数据库正常" : "数据库异常"); });
        private void OnQrUploaded(object sender, EventArgs e) => SafeInvoke(() => { AddImportantLog("二维码匹配入库成功"); DoQuery(); });
        private void OnUploadError(object sender, UploadErrorEventArgs e) => SafeInvoke(() => SetWarn(e.Message));
        private void OnMismatch(object sender, DataMismatchEventArgs e) => SafeInvoke(() => SetWarn(e.Message));
        private void OnBatchError(object sender, BatchErrorEventArgs e) => SafeInvoke(() => SetWarn(e.Message));

        private void UnwireEvents()
        {
            _manager.Plc.ConnectionChanged -= OnPlcChanged;
            _manager.DbHealth.HealthChanged -= OnHealthChanged;
            _manager.QrUpload.RecordUploaded -= OnQrUploaded;
            _manager.QrUpload.ErrorOccurred -= OnUploadError;
            _manager.Withstand.DataMismatch -= OnMismatch;
            _manager.Pressure.DataMismatch -= OnMismatch;
            _manager.Batch.ErrorOccurred -= OnBatchError;
            _manager.Withstand.ErrorOccurred -= OnUploadError;
            _manager.Pressure.ErrorOccurred -= OnUploadError;
            _manager.Batch.BatchIssued -= OnBatchIssued;
            _manager.Withstand.RecordUploaded -= OnUpload;
            _manager.Pressure.RecordUploaded -= OnUpload;
        }

        private void OnBatchIssued(object sender, EventArgs e)
        {
            SafeInvoke(() =>
            {
                AddImportantLog("批次下发成功，下一批次 " + _manager.Batch.CurrentBatchNo.ToString("D5"));
                RefreshCurrentBatch();
                _pageIndex = 0;
                // 下发成功后显示实时批次，避免历史日期或产品筛选隐藏新记录。
                DateTime today = DateTime.Today;
                if (_fromPicker.Value.Date > today) _fromPicker.Value = today;
                if (_toPicker.Value.Date < today) _toPicker.Value = today;
                // 产品目录已在批次事务中增量维护；下拉框展开前再读取小表即可。
                DoQuery();
                if (_grid.Rows.Count > 0)
                    _grid.FirstDisplayedScrollingRowIndex = 0;
            });
        }

        private void OnUpload(object sender, UploadEventArgs e)
        {
            SafeInvoke(() =>
            {
                AddImportantLog((sender == _manager.Withstand ? "耐压" : "气密") + "上传：" + (e.State == FlagState.Success ? "匹配入库成功" : "历史数据已存在"));
                DoQuery();
                if (e.State == FlagState.Error)
                    SetWarn("批次存在历史测试数据（标志位=3），请到 PLC 侧确认后清除标志位");
                RefreshCurrentBatch();
            });
        }

        private void SetWarn(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) AddImportantLog(message);
            _warnLabel.Text = string.IsNullOrEmpty(message) ? "" : "⚠ " + message;
        }

        private void AddImportantLog(string message)
        {
            DateTime now = DateTime.Now;
            DateTime previous;
            if (_recentLogMessages.TryGetValue(message, out previous) && (now - previous).TotalSeconds < 30) return;
            if (_recentLogMessages.Count >= 500) _recentLogMessages.Clear();
            _recentLogMessages[message] = now;
            string line = now.ToString("MM-dd HH:mm:ss") + "  " + message;
            if (_logPaused)
            {
                _pausedLogs.Enqueue(line);
                while (_pausedLogs.Count > 200) _pausedLogs.Dequeue();
                return;
            }
            InsertLogRow(line);
        }

        private void InsertLogRow(string line)
        {
            _eventLog.Rows.Insert(0, line.Substring(6, 8), line.Substring(16));
            _eventLog.Rows[0].Cells[0].ToolTipText = line.Substring(0, 14);
            _eventLog.Rows[0].DefaultCellStyle.ForeColor =
                line.Contains("失败") || line.Contains("异常") || line.Contains("断开") ? FlatTheme.Ng :
                line.Contains("不匹配") || line.Contains("不一致") || line.Contains("暂停") ? FlatTheme.Warn :
                line.Contains("成功") || line.Contains("正常") || line.Contains("已连接") ? FlatTheme.Ok : FlatTheme.Text;
            _eventLog.Rows[0].Cells[1].Style.ForeColor = _eventLog.Rows[0].DefaultCellStyle.ForeColor;
            _eventLog.Rows[0].Cells[1].Style.SelectionForeColor = _eventLog.Rows[0].DefaultCellStyle.ForeColor;
            while (_eventLog.Rows.Count > 200) _eventLog.Rows.RemoveAt(_eventLog.Rows.Count - 1);
            _eventLog.FirstDisplayedScrollingRowIndex = 0;
        }

        private void ApplyResponsiveLayout()
        {
            if (_tablePanel == null || _statusLabel == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int left = Math.Max(300, Math.Min(360, w / 5));
            int rightX = 24 + left + 16, rightWidth = Math.Max(800, w - rightX - 24);
            _brandPanel.Bounds = new Rectangle(24, 16, w - 48, 72);
            foreach (Control c in Controls)
                if (c is UIPanel && c.Height == 2) c.Bounds = new Rectangle(24, 86, w - 48, 2);
            int batchHeight = h >= 900 ? 260 : 210;
            _batchPanel.Bounds = new Rectangle(24, 104, left, batchHeight);
            int logHeight = Math.Max(150, Math.Min(240, h / 5));
            _logPanel.Bounds = new Rectangle(24, h - 42 - logHeight, left, logHeight);
            _eventLog.Bounds = new Rectangle(10, 50, left - 20, logHeight - 60);
            _pauseLogButton.Bounds = new Rectangle(left - 120, 8, 110, 30);
            foreach (UILabel title in _logPanel.Controls.OfType<UILabel>())
                if (title.Text == "重要日志") title.Width = Math.Max(80, _pauseLogButton.Left - title.Left - 12);
            _pauseLogButton.BringToFront();
            _imagePanel.Bounds = new Rectangle(24, _batchPanel.Bottom + 16, left, _logPanel.Top - _batchPanel.Bottom - 32);
            _productImage.Bounds = new Rectangle(14, 50, left - 28, Math.Max(25, _imagePanel.Height - 88));
            _imageCaption.Bounds = new Rectangle(14, _imagePanel.Height - 34, left - 28, 28);
            _imageCaption.Text = _currentProductName;
            _batchNoLabel.Bounds = new Rectangle(20, 48, left - 40, 64);
            var meta = _batchPanel.Controls.OfType<MetaRowPanel>().First();
            meta.Bounds = new Rectangle(20, 116, left - 40, batchHeight - 124);
            int col = meta.Width / 3;
            foreach (Control c in meta.Controls)
            {
                if (c is UISymbolLabel)
                {
                    int iconIndex = int.Parse(c.Name.Substring("metaIcon".Length));
                    c.Bounds = new Rectangle(0, iconIndex * (meta.Height / 3), 26, meta.Height / 3);
                    continue;
                }
                int index = c == _productValue || c.Text == "产品名称" ? 0 : c == _employeeValue || c.Text == "员工工号" ? 1 : 2;
                bool value = c == _productValue || c == _employeeValue || c == _dateValue;
                int rowHeight = meta.Height / 3;
                c.Bounds = new Rectangle(value ? 118 : 30, index * rowHeight, value ? meta.Width - 118 : 88, rowHeight);
                if (c is UILabel label) label.TextAlign = ContentAlignment.MiddleLeft;
            }
            _queryPanel.Bounds = new Rectangle(rightX, 104, rightWidth, 176);
            _tablePanel.Bounds = new Rectangle(rightX, 296, rightWidth, Math.Max(220, h - 342));
            _grid.Bounds = new Rectangle(0, 46, rightWidth, _tablePanel.Height - 46);
            _countLabel.Bounds = new Rectangle(rightWidth - 180, 8, 160, 30);
            _unboundLabel.Bounds = new Rectangle(20, 126, rightWidth - 40, 36);
            _unboundIcon.Bounds = new Rectangle(26, 132, 26, 24);
            // 分页组靠右：按钮间隔20px，下一页与页数区域间隔20px。
            _nextPage.Bounds = new Rectangle(_countLabel.Left - 20 - 82, 8, 82, 30);
            _previousPage.Bounds = new Rectangle(_nextPage.Left - 20 - 82, 8, 82, 30);
            int dateW = Math.Max(140, Math.Min(176, rightWidth / 6));
            _fromPicker.Bounds = new Rectangle(20, 78, dateW, 32);
            _toPicker.Bounds = new Rectangle(36 + dateW, 78, dateW, 32);
            _productCombo.Bounds = new Rectangle(52 + dateW * 2, 78, Math.Max(170, rightWidth - dateW * 2 - 320), 32);
            foreach (Control c in _queryPanel.Controls)
            {
                if (c.Text == "开始日期") c.Bounds = new Rectangle(_fromPicker.Left, 54, dateW, 22);
                if (c.Text == "结束日期") c.Bounds = new Rectangle(_toPicker.Left, 54, dateW, 22);
                if (c.Text.StartsWith("产品名称（")) c.Bounds = new Rectangle(_productCombo.Left, 54, _productCombo.Width, 22);
                if (c is UIButton) c.Bounds = new Rectangle(rightWidth - (c.Text == "查 询" ? 238 : 128), 76, 100, 36);
            }
            _clockLabel.Bounds = new Rectangle(_brandPanel.Width - 450, 8, 230, 26);
            int statusX = _brandPanel.Width - 760;
            _plcCaption.Bounds = new Rectangle(statusX, 22, 45, 30);
            _plcText.Bounds = new Rectangle(statusX + 48, 22, 82, 30);
            _statusDivider.Bounds = new Rectangle(statusX + 142, 22, 1, 30);
            _dbCaption.Bounds = new Rectangle(statusX + 160, 22, 72, 30);
            _dbText.Bounds = new Rectangle(statusX + 235, 22, 65, 30);
            _clockDivider.Bounds = new Rectangle(statusX + 315, 22, 1, 30);
            _clockDateLabel.Bounds = new Rectangle(_brandPanel.Width - 450, 40, 230, 20);
            foreach (Control c in _brandPanel.Controls.OfType<UIButton>())
                c.Left = _brandPanel.Width - (c.Text == "退出" ? 80 : 190);
            foreach (var panel in new[] { _batchPanel, _imagePanel, _logPanel, _queryPanel, _tablePanel })
                foreach (Control c in panel.Controls)
                {
                    if (c is UIPanel && c.Height == 1) c.Width = panel.Width - 2;
                    if (c is UILabel && (c.Text == "CURRENT BATCH" || c.Text == "PRODUCT IMAGE" || c.Text == "QUERY"))
                    { c.Left = panel.Width - 190; c.Visible = panel.Width >= 400; }
                }
            _statusLabel.Bounds = new Rectangle(24, h - 30, 260, 24);
            _warnLabel.Bounds = new Rectangle(rightX, h - 30, rightWidth, 24);
        }

        private void RefreshClock()
        {
            _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockDateLabel.Text = DateTime.Now.ToString("yyyy-MM-dd dddd");
        }

        private void RefreshLamps()
        {
            _plcText.Text = _manager.Plc.IsConnected ? "已连接" : "未连接";
            _plcText.ForeColor = _manager.Plc.IsConnected ? FlatTheme.Ok : FlatTheme.Warn;
            _dbText.Text = _manager.DbHealth.IsHealthy ? "正常" : "异常";
            _dbText.ForeColor = _manager.DbHealth.IsHealthy ? FlatTheme.Ok : FlatTheme.Ng;
            _imageCaption.Text = _currentProductName;
            _plcLamp.On = _manager.Plc.IsConnected;
            _dbLamp.On = _manager.DbHealth.IsHealthy;
            _statusLabel.Text = _manager.Plc.IsConnected && _manager.DbHealth.IsHealthy ? "PLC / 数据库正常" : "连接或存储异常，请检查";
            if (!_manager.DbHealth.IsHealthy) SetWarn("数据库不可写或磁盘空间不足，心跳已停止，请检查日志和磁盘。");
            else if (!string.IsNullOrEmpty(_manager.Backups.LastError)) SetWarn("数据库备份失败：" + _manager.Backups.LastError);
        }

        private void RefreshCurrentBatch()
        {
            _batchNoLabel.Text = _manager.Batch.CurrentBatchNo.ToString("00000");
            string product;
            _manager.Plc.TryGetString(RegisterMap.D5000_ProductName, out product);
            _currentProductName = product ?? string.Empty;
            _productValue.Text = string.IsNullOrEmpty(_currentProductName) ? "--" : _currentProductName;
            int employeeNo;
            _employeeValue.Text = _manager.Plc.TryGetDInt(RegisterMap.D4030_EmployeeNo, out employeeNo) ? employeeNo.ToString() : "--";
            _dateValue.Text = DateTime.Now.ToBatchDateString();
        }

        private void RefreshProductImage()
        {
            string product;
            _manager.Plc.TryGetString(RegisterMap.D5000_ProductName, out product);
            if (string.IsNullOrEmpty(product)) product = _currentProductName;
            string path = ImageResolver.FindProductImage(_productPicDir, product);
            DateTime modified = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (string.Equals(path, _loadedImagePath, StringComparison.OrdinalIgnoreCase) && modified == _loadedImageTime &&
                (_imageRetryAfter == DateTime.MinValue || DateTime.UtcNow < _imageRetryAfter)) return;
            Image image = ImageResolver.LoadSafely(path);
            _loadedImagePath = path;
            _loadedImageTime = modified;
            _imageRetryAfter = image == null && path != null ? DateTime.UtcNow.AddSeconds(30) : DateTime.MinValue;
            if (image == null && path != null) SetWarn("产品图片加载失败，30秒后重试：" + Path.GetFileName(path));
            Image old = _productImage.BackgroundImage;
            _productImage.BackgroundImage = image;
            old?.Dispose();
        }

        private async void RefreshProductCombo(bool preserveSelection = false)
        {
            await RefreshProductComboAsync(preserveSelection);
        }

        private Task RefreshProductComboAsync(bool preserveSelection)
        {
            return _productRefreshTask != null && !_productRefreshTask.IsCompleted
                ? _productRefreshTask : (_productRefreshTask = LoadProductComboAsync(preserveSelection));
        }

        private async Task LoadProductComboAsync(bool preserveSelection)
        {
            try
            {
            var products = await Task.Run(() => _manager.BatchRepo.GetDistinctProductNames()
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList());
            if (_closing || IsDisposed) return;
            string selected = _productCombo.Text;
            _allProducts.Clear();
            foreach (string n in products)
                _allProducts.Add(n);
            // 更新数据源通知 SunnyUI 重建弹出列表，避免只改 Items 后使用旧缓存。
            var options = new List<string> { "全部产品" };
            options.AddRange(_allProducts);
            _productCombo.DataSource = options;
            _productCombo.Text = preserveSelection && !string.IsNullOrEmpty(selected) ? selected : "全部产品";
            }
            catch (Exception ex) { if (!_closing && !IsDisposed) SetWarn("产品列表刷新失败：" + ex.Message); }
        }

        private async void ExportData()
        {
            if (_exportRunning) { SetWarn("数据正在导出，请等待完成。"); return; }
            _exportRunning = true;
            try
            {
                string product = _productCombo.Text;
                string from = _fromPicker.Value.ToBatchDateString(), to = _toPicker.Value.ToBatchDateString();
                if (string.CompareOrdinal(from, to) > 0) { SetWarn("开始日期不能晚于结束日期。"); return; }
                using (var dialog = new SaveFileDialog
                {
                    Filter = "CSV 文件 (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true,
                    FileName = "测试记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv",
                    OverwritePrompt = true
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string target = dialog.FileName;
                    int count = await Task.Run(() =>
                    {
                    string temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                    int exported = 0;
                    try
                    {
                    using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
                    {
                        writer.WriteLine("时间,批次日期,批次号,产品名称,员工工号,电压,电阻,电流,耐压结果,气压,气压结果,二维码等级");
                        foreach (var r in _manager.BatchRepo.ExportRows(from, to, product == "全部产品" ? null : product))
                        {
                            if (_closing) throw new OperationCanceledException("程序退出，已取消导出。");
                            string[] values = { r.IssueTime.ToString("yyyy-MM-dd HH:mm:ss"), r.BatchDate,
                                r.BatchNo.ToString("D5"), r.ProductName, r.EmployeeNo, Format(r.Voltage),
                                Format(r.Resistance), Format(r.Current),
                                r.WithstandResult.HasValue ? (r.WithstandResult == 1 ? "OK" : "NG") : "",
                                Format(r.Pressure), r.PressureResult.HasValue ? (r.PressureResult == 1 ? "OK" : "NG") : "", r.QrGrade };
                            writer.WriteLine(string.Join(",", values.Select(CsvCell)));
                            exported++;
                        }
                    }
                    if (File.Exists(target)) File.Replace(temporary, target, null);
                    else File.Move(temporary, target);
                    return exported;
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    });
                    if (!_closing) SetWarn("已导出 " + count + " 条记录：" + target);
                }
            }
            catch (Exception ex) { if (!_closing) SetWarn("导出失败：" + ex.Message); }
            finally { _exportRunning = false; }
        }

        private static string CsvCell(string value)
        {
            value = value ?? string.Empty;
            // 避免产品名称等外部文本被表格软件当作公式执行。
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private async void DoQuery()
        {
            if (_closing) return;
            if (_queryRunning) { _queryPending = true; return; }
            _queryRunning = true;
            _previousPage.Enabled = _nextPage.Enabled = false;
            try
            {
                string from = _fromPicker.Value.ToBatchDateString();
                string to = _toPicker.Value.ToBatchDateString();
                string product = _productCombo.Text;
                if (string.IsNullOrEmpty(product) || product == "全部产品") product = null;
                int offset = checked(_pageIndex * PageSize);
                long unboundCount = 0;
                IList<BatchRecord> rows = await Task.Run(() =>
                {
                    unboundCount = _manager.BatchRepo.CountUnboundWithstand();
                    return _manager.BatchRepo.QueryPage(from, to, product, offset, PageSize + 1);
                });
                if (_closing || _queryPending) return;
                _unboundLabel.Text = "当前未绑定数据的数量：" + unboundCount;
                _unboundLabel.ForeColor = unboundCount == 0 ? Color.FromArgb(39, 104, 68) : Color.FromArgb(160, 93, 0);
                _unboundLabel.BackColor = unboundCount == 0 ? Color.FromArgb(237, 248, 242) : Color.FromArgb(255, 246, 225);
                _unboundIcon.BackColor = _unboundLabel.BackColor;
                _unboundIcon.SymbolColor = _unboundLabel.ForeColor;
                bool hasMore = rows.Count > PageSize;
                _grid.Rows.Clear();
                foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
                foreach (BatchRecord r in rows.Take(PageSize))
                {
                    int rowIndex = _grid.Rows.Add(
                        r.BatchDate,
                        r.BatchNo.ToString("00000"),
                        r.ProductName ?? "--",
                        r.EmployeeNo ?? "--",
                        FormatScientific(r.Voltage),
                        FormatScientific(r.Resistance),
                        FormatScientific(r.Current),
                        r.WithstandResult.HasValue ? (r.WithstandResult == 1 ? "OK" : "NG") : "--",
                        Format(r.Pressure),
                        r.PressureResult.HasValue ? (r.PressureResult == 1 ? "OK" : "NG") : "--",
                        r.IssueTime == DateTime.MinValue ? "--" : r.IssueTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        r.QrGrade ?? "--");
                    _grid.Rows[rowIndex].Tag = r;
                }
                // 新填充行不代表用户主动选中，取消DataGridView自动选中的第一行。
                _grid.CurrentCell = null;
                _grid.ClearSelection();
                SetWarn(string.Empty);
                _countLabel.Text = "第 " + (_pageIndex + 1) + " 页 · " + Math.Min(rows.Count, PageSize) + " 条";
                _previousPage.Enabled = _pageIndex > 0;
                _nextPage.Enabled = hasMore;
            }
            catch (Exception ex)
            {
                if (!_closing) SetWarn("查询失败：" + ex.Message);
            }
            finally
            {
                _queryRunning = false;
                if (_queryPending && !_closing) { _queryPending = false; DoQuery(); }
            }
        }

        private static string Format(decimal? value)
        {
            return value.HasValue ? value.Value.ToString("0.00") : "--";
        }

        private static string FormatScientific(decimal? value)
        {
            return value.HasValue
                ? value.Value.ToString("+0.00000E+00;-0.00000E+00;+0.00000E+00", System.Globalization.CultureInfo.InvariantCulture)
                : "--";
        }

        private void SafeInvoke(Action action)
        {
            if (_closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(() => { if (!_closing && !IsDisposed) action(); }));
                else action();
            }
            catch (InvalidOperationException) { /* 关闭时句柄可能刚被销毁。 */ }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var bg = new SolidBrush(Workspace))
                e.Graphics.FillRectangle(bg, 0, 0, Width, Height);
            using (var header = new SolidBrush(FlatTheme.Bg2))
                e.Graphics.FillRectangle(header, 0, 0, Width, 88);
            // 深色侧栏连续成面，卡片之间不再被浅色背景割裂。
            if (_batchPanel != null)
                using (var sidebar = new SolidBrush(FlatTheme.Bg))
                    e.Graphics.FillRectangle(sidebar, 0, 88, _batchPanel.Right + 8, Height - 88);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_exitConfirmed)
            {
                if (_exitPromptOpen) { e.Cancel = true; return; }
                _exitPromptOpen = true;
                try
                {
                    using (var dialog = new ExitConfirmForm())
                        _exitConfirmed = dialog.ShowDialog(this) == DialogResult.OK;
                }
                finally { _exitPromptOpen = false; }
                if (!_exitConfirmed) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
            if (e.Cancel) { _exitConfirmed = false; return; }
            _closing = true;
            _clockTimer?.Dispose();
            _picTimer?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                UnwireEvents();
                _clockTimer?.Dispose(); _picTimer?.Dispose();

                _applicationIcon?.Dispose();
                _ngBadgeFont.Dispose();
                _productImage.BackgroundImage?.Dispose(); _productImage.BackgroundImage = null;
            }
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in _ownedFonts) font.Dispose();
                _ownedFonts.Clear();
            }
        }
    }

    /// <summary>批次元信息行：顶部细线 + 列间分隔线。</summary>
    internal sealed class MetaRowPanel : UIPanel
    {
        public MetaRowPanel()
        {
            StyleCustomMode = true;
            Style = UIStyle.Custom;
            FillColor = FlatTheme.Panel;
            FillColor2 = FlatTheme.Panel;
            Text = string.Empty;
            RectColor = FlatTheme.BorderSoft;
            Radius = 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(FlatTheme.Panel);
            using (var pen = new Pen(FlatTheme.BorderSoft))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                e.Graphics.DrawLine(pen, 0, Height / 3, Width, Height / 3);
                e.Graphics.DrawLine(pen, 0, Height * 2 / 3, Width, Height * 2 / 3);
            }
        }
    }
}
