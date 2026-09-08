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
        private readonly Timer _clockTimer;
        private readonly Timer _picTimer;

        private readonly UILedBulb _plcLamp = new UILedBulb();
        private readonly UILedBulb _dbLamp = new UILedBulb();
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
        private UILabel _countLabel;
        private readonly IList<string> _allProducts = new List<string>();
        private readonly string _productPicDir;
        private readonly string _instructionDir;
        private string _currentProductName = string.Empty;
        private string _loadedImagePath;
        private DateTime _loadedImageTime;
        private bool _closing;
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
            Font = new Font("Microsoft YaHei UI", 9F);
            Padding = new Padding(1);
            DoubleBuffered = true;

            BuildBrandBar();
            BuildLeft();
            BuildRight();
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
            RefreshProductCombo();
            Shown += (s, e) =>
            {
                Bounds = Screen.FromControl(this).WorkingArea;
                ApplyResponsiveLayout();
                DoQuery();
            };
        }

        public AppConfig Config => _manager.Config;

        // ---------- 控件工厂 ----------

        private static UIPanel CreatePanel(Rectangle bounds, string caption, string captionEn, Color mark, string captionRight)
        {
            var p = new UIPanel { Bounds = bounds };
            StylePanel(p, FlatTheme.Panel);
            if (string.IsNullOrEmpty(caption)) return p;

            var markCtl = new UIPanel { Bounds = new Rectangle(18, 19, 8, 8) };
            StylePanel(markCtl, mark);
            markCtl.RectColor = mark;
            p.Controls.Add(markCtl);
            p.Controls.Add(CreateLabel(caption, new Rectangle(34, 11, 240, 22), FlatTheme.Text,
                new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ContentAlignment.MiddleLeft, FlatTheme.Panel));
            if (!string.IsNullOrEmpty(captionEn))
                p.Controls.Add(CreateLabel(captionEn, new Rectangle(p.Width - 190, 16, 170, 14), FlatTheme.Text3,
                    FlatTheme.MonoSmall, ContentAlignment.MiddleRight, FlatTheme.Panel));
            if (!string.IsNullOrEmpty(captionRight))
                p.Controls.Add(CreateLabel(captionRight, new Rectangle(p.Width - 220, 16, 200, 14), FlatTheme.Text3,
                    FlatTheme.MonoSmall, ContentAlignment.MiddleRight, FlatTheme.Panel));

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

        private static UIButton CreateButton(string text, Rectangle bounds, bool primary)
        {
            var btn = new UIButton
            {
                Text = text,
                Bounds = bounds,
                Font = FlatTheme.Ui,
                StyleCustomMode = true,
                Style = UIStyle.Custom,
                Radius = FlatTheme.Radius,
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
            if (primary)
            {
                btn.Font = new Font(FlatTheme.Ui.FontFamily, 11F, FontStyle.Bold);
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
                new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), ContentAlignment.MiddleLeft, FlatTheme.Bg2);
            var subtitle = CreateLabel("LIANDIAN DATA COLLECTION · SCADA", new Rectangle(24, 42, 360, 16), FlatTheme.Steel,
                FlatTheme.MonoSmall, ContentAlignment.MiddleLeft, FlatTheme.Bg2);
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
            _clockDateLabel.Font = FlatTheme.MonoSmall;
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
                using (var dialog = new ExitConfirmForm())
                    if (dialog.ShowDialog(this) == DialogResult.OK) Close();
            };

            brand.Controls.AddRange(new Control[]
            {
                title, subtitle, divider, _plcLamp, plcLabel, _dbLamp, dbLabel,
                _clockLabel, _clockDateLabel, instrBtn, exitBtn
            });
            Controls.Add(brand);
            Controls.Add(accent);
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
            Controls.Add(imagePanel);
        }

        private static void AddMeta(MetaRowPanel panel, UILabel valueLabel, string key, int index)
        {
            int w = panel.Width / 3;
            int x = index * w;
            var k = CreateLabel(key, new Rectangle(x, 2, w, 18), FlatTheme.Text3, FlatTheme.UiSmall,
                ContentAlignment.MiddleCenter, FlatTheme.Panel);
            valueLabel.Font = new Font("Consolas", 11F);
            FlatTheme.ApplyLabel(valueLabel, FlatTheme.Text, FlatTheme.Panel);
            valueLabel.ForeColor = FlatTheme.Text;
            valueLabel.BackColor = FlatTheme.Panel;
            valueLabel.TextAlign = ContentAlignment.MiddleCenter;
            valueLabel.Bounds = new Rectangle(x, 24, w, 24);
            panel.Controls.Add(k);
            panel.Controls.Add(valueLabel);
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
            _productCombo.Font = FlatTheme.Mono;
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
            new ProductDropdownRefresh(_productCombo, () =>
            {
                try { RefreshProductCombo(true); }
                catch (Exception ex) { SetWarn("产品列表刷新失败：" + ex.Message); }
            });

            var btn = CreateButton("查 询", new Rectangle(1000, 68, 100, 38), true);
            btn.Click += (s, e) => { _pageIndex = 0; DoQuery(); };
            var exportBtn = CreateButton("导出 CSV", new Rectangle(1120, 68, 120, 38), false);
            exportBtn.Click += (s, e) => ExportData();

            queryPanel.Controls.AddRange(new Control[]
            {
                fromLabel, toLabel, proLabel, _fromPicker, _toPicker, _productCombo, btn, exportBtn
            });
            Controls.Add(queryPanel);

            _countLabel = CreateLabel("共 0 条", new Rectangle(0, 0, 200, 14), FlatTheme.Text3, FlatTheme.MonoSmall,
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
            picker.Font = FlatTheme.Mono;
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
            _grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5F);
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
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(16, 0, 12, 0);
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
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
            _grid.Columns["电压"].Width = 100;
            _grid.Columns["电阻"].Width = 115;
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
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "耐压结果" && _grid.Columns[e.ColumnIndex].Name != "气压结果") return;
            string s = e.Value as string;
            if (string.IsNullOrEmpty(s) || (s != "OK" && s != "NG")) return;

            e.PaintBackground(e.CellBounds, true);
            Color color = s == "OK" ? FlatTheme.Ok : FlatTheme.Ng;
            var rect = e.CellBounds;
            var badge = new Rectangle(rect.X + (rect.Width - 46) / 2, rect.Y + (rect.Height - 22) / 2, 46, 22);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(badge, 3))
            {
                using (var fill = new SolidBrush(Color.FromArgb(26, color)))
                    e.Graphics.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(90, color)))
                    e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, s, FlatTheme.MonoSmall, badge, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            e.Handled = true;
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

        private void WireEvents()
        {
            _manager.Plc.ConnectionChanged += (s, e) => SafeInvoke(RefreshLamps);
            _manager.DbHealth.HealthChanged += (s, e) => SafeInvoke(RefreshLamps);
            _manager.Batch.BatchIssued += OnBatchIssued;
            _manager.Withstand.RecordUploaded += OnUpload;
            _manager.Pressure.RecordUploaded += OnUpload;
            _manager.Withstand.DataMismatch += (s, e) => SafeInvoke(() => SetWarn(e.Message));
            _manager.Pressure.DataMismatch += (s, e) => SafeInvoke(() => SetWarn(e.Message));
            _manager.Batch.ErrorOccurred += (s, e) => SafeInvoke(() => SetWarn(e.Message));
            _manager.Withstand.ErrorOccurred += (s, e) => SafeInvoke(() => SetWarn(e.Message));
            _manager.Pressure.ErrorOccurred += (s, e) => SafeInvoke(() => SetWarn(e.Message));
        }

        private void OnBatchIssued(object sender, EventArgs e)
        {
            SafeInvoke(() =>
            {
                RefreshCurrentBatch();
                _pageIndex = 0;
                // 下发成功后显示实时批次，避免历史日期或产品筛选隐藏新记录。
                DateTime today = DateTime.Today;
                if (_fromPicker.Value.Date > today) _fromPicker.Value = today;
                if (_toPicker.Value.Date < today) _toPicker.Value = today;
                try
                {
                    RefreshProductCombo();
                }
                catch (Exception ex)
                {
                    SetWarn("产品列表刷新失败：" + ex.Message);
                    return;
                }
                DoQuery();
                if (_grid.Rows.Count > 0)
                    _grid.FirstDisplayedScrollingRowIndex = 0;
            });
        }

        private void OnUpload(object sender, UploadEventArgs e)
        {
            SafeInvoke(() =>
            {
                DoQuery();
                if (e.State == FlagState.Error)
                    SetWarn("批次存在历史测试数据（标志位=3），请到 PLC 侧确认后清除标志位");
                RefreshCurrentBatch();
            });
        }

        private void SetWarn(string message)
        {
            _warnLabel.Text = string.IsNullOrEmpty(message) ? "" : "⚠ " + message;
        }

        private void ApplyResponsiveLayout()
        {
            if (_tablePanel == null || _statusLabel == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int left = Math.Max(300, Math.Min(440, w / 4 - 40));
            int rightX = 24 + left + 16, rightWidth = Math.Max(800, w - rightX - 24);
            _brandPanel.Bounds = new Rectangle(24, 16, w - 48, 72);
            foreach (Control c in Controls)
                if (c is UIPanel && c.Height == 2) c.Bounds = new Rectangle(24, 86, w - 48, 2);
            _batchPanel.Bounds = new Rectangle(24, 104, left, 210);
            _imagePanel.Bounds = new Rectangle(24, 330, left, Math.Max(240, h - 372));
            _productImage.Bounds = new Rectangle(18, 54, left - 36, _imagePanel.Height - 72);
            _batchNoLabel.Bounds = new Rectangle(20, 50, left - 40, 84);
            var meta = _batchPanel.Controls.OfType<MetaRowPanel>().First();
            meta.Bounds = new Rectangle(20, 146, left - 40, 52);
            int col = meta.Width / 3;
            foreach (Control c in meta.Controls)
            {
                int index = c == _productValue || c.Text == "产品名称" ? 0 : c == _employeeValue || c.Text == "员工工号" ? 1 : 2;
                c.Bounds = new Rectangle(index * col, c.Top, col, c.Height);
            }
            _queryPanel.Bounds = new Rectangle(rightX, 104, rightWidth, 128);
            _tablePanel.Bounds = new Rectangle(rightX, 248, rightWidth, Math.Max(220, h - 294));
            _grid.Bounds = new Rectangle(0, 46, rightWidth, _tablePanel.Height - 46);
            _countLabel.Bounds = new Rectangle(rightWidth - 235, 14, 215, 22);
            _previousPage.Bounds = new Rectangle(rightWidth - 420, 8, 82, 30);
            _nextPage.Bounds = new Rectangle(rightWidth - 330, 8, 82, 30);
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
            _clockDateLabel.Bounds = new Rectangle(_brandPanel.Width - 450, 40, 230, 20);
            foreach (Control c in _brandPanel.Controls.OfType<UIButton>())
                c.Left = _brandPanel.Width - (c.Text == "退出" ? 80 : 190);
            foreach (var panel in new[] { _batchPanel, _imagePanel, _queryPanel, _tablePanel })
                foreach (Control c in panel.Controls)
                {
                    if (c is UIPanel && c.Height == 1) c.Width = panel.Width - 2;
                    if (c is UILabel && (c.Text == "CURRENT BATCH" || c.Text == "PRODUCT IMAGE" || c.Text == "QUERY"))
                    { c.Left = panel.Width - 190; c.Visible = panel.Width >= 400; }
                }
            _statusLabel.Bounds = new Rectangle(24, h - 30, 260, 24);
            _warnLabel.Bounds = new Rectangle(300, h - 30, Math.Max(500, w - 324), 24);
        }

        private void RefreshClock()
        {
            _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockDateLabel.Text = DateTime.Now.ToString("yyyy-MM-dd dddd");
        }

        private void RefreshLamps()
        {
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
            if (string.Equals(path, _loadedImagePath, StringComparison.OrdinalIgnoreCase) && modified == _loadedImageTime) return;
            Image image = ImageResolver.LoadSafely(path);
            _loadedImagePath = path;
            _loadedImageTime = image == null && path != null ? DateTime.MinValue : modified;
            Image old = _productImage.BackgroundImage;
            _productImage.BackgroundImage = image;
            old?.Dispose();
        }

        private void RefreshProductCombo(bool preserveSelection = false)
        {
            string selected = _productCombo.Text;
            var products = _manager.BatchRepo.GetDistinctProductNames()
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            _allProducts.Clear();
            foreach (string n in products)
                _allProducts.Add(n);
            // 更新数据源通知 SunnyUI 重建弹出列表，避免只改 Items 后使用旧缓存。
            var options = new List<string> { "全部产品" };
            options.AddRange(_allProducts);
            _productCombo.DataSource = options;
            _productCombo.Text = preserveSelection && !string.IsNullOrEmpty(selected) ? selected : "全部产品";
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
                IList<BatchRecord> rows = await Task.Run(() => _manager.BatchRepo.QueryPage(from, to, product, offset, PageSize + 1));
                if (_closing || _queryPending) return;
                bool hasMore = rows.Count > PageSize;
                _grid.Rows.Clear();
                foreach (BatchRecord r in rows.Take(PageSize))
                {
                    _grid.Rows.Add(
                        r.BatchDate,
                        r.BatchNo.ToString("00000"),
                        r.ProductName ?? "--",
                        r.EmployeeNo ?? "--",
                        Format(r.Voltage),
                        Format(r.Resistance),
                        Format(r.Current),
                        r.WithstandResult.HasValue ? (r.WithstandResult == 1 ? "OK" : "NG") : "--",
                        Format(r.Pressure),
                        r.PressureResult.HasValue ? (r.PressureResult == 1 ? "OK" : "NG") : "--",
                        r.IssueTime == DateTime.MinValue ? "--" : r.IssueTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        r.QrGrade ?? "--");
                }
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
            using (var bg = new SolidBrush(FlatTheme.Bg))
                e.Graphics.FillRectangle(bg, 0, 0, Width, Height);
            using (var pen = new Pen(FlatTheme.BgGrid))
            {
                for (int x = 0; x < Width; x += 64)
                    e.Graphics.DrawLine(pen, x, 0, x, Height);
                for (int y = 0; y < Height; y += 64)
                    e.Graphics.DrawLine(pen, 0, y, Width, y);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            _clockTimer?.Dispose();
            _picTimer?.Dispose();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                _clockTimer?.Dispose(); _picTimer?.Dispose();
                _productImage.BackgroundImage?.Dispose(); _productImage.BackgroundImage = null;
            }
            base.Dispose(disposing);
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
                int w = Width / 3;
                e.Graphics.DrawLine(pen, w, 0, w, Height);
                e.Graphics.DrawLine(pen, w * 2, 0, w * 2, Height);
            }
        }
    }
}
