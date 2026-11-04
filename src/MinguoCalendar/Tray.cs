using System;
using System.Linq;
using Minguo.Core;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace MinguoCalendar;

/// <summary>
/// 托盘：图标上显示今天几号（每天自动变），鼠标悬停显示民国日期、农历、天气；左键打开设置，右键菜单。
/// </summary>
internal sealed class Tray : IDisposable
{
    private readonly WinForms.NotifyIcon _icon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private Drawing.Icon? _current;
    private int _iconDay = -1;
    private bool _iconDark;

    public Tray()
    {
        _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) App.Instance.ShowSettings();
        };
        _menu.Opening += (_, _) => UpdateMenu();
        _menu.ShowImageMargin = false;
        _menu.Font = new Drawing.Font("Microsoft YaHei UI", 9.5f);
        UpdateMenu();
        Update();
        _icon.Visible = true;
        App.Refreshed += Update;
        Scheduler.Minute += Update;
        Theme.Changed += UpdateMenu;
    }

    public void Dispose()
    {
        App.Refreshed -= Update;
        Scheduler.Minute -= Update;
        Theme.Changed -= UpdateMenu;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _current?.Dispose();
    }

    /// <summary>更新图标和悬停文字</summary>
    public void Update()
    {
        var now = DateTime.Now;
        var s = SettingsStore.Current;
        var dark = Theme.IsDark;
        if (now.Day != _iconDay || dark != _iconDark)
        {
            SetIcon(DrawDay(now.Day));
            _iconDay = now.Day;
            _iconDark = dark;
        }
        var style = s.Style;
        var tip = Roc.DateLine(now, style) + " " + Roc.Weekday(now) + "\n" + Roc.LunarLine(now, style);
        var w = s.WeatherEnabled ? WeatherService.Current : null;
        if (w != null) tip += "\n" + WeatherService.Summary(w) + " " + WeatherService.CityLabel(w);
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void SetIcon(Drawing.Icon icon)
    {
        var old = _current;
        _current = icon;
        _icon.Icon = icon;
        if (old != null)
        {
            Native.DestroyIcon(old.Handle);
            old.Dispose();
        }
    }

    /// <summary>金色圆角方块 + 日期数字（按 32×32 画，系统会按缩放比例显示）</summary>
    private static Drawing.Icon DrawDay(int day)
    {
        var size = Math.Max(32, WinForms.SystemInformation.SmallIconSize.Width * 2);
        using var bmp = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);
            var r = new Drawing.RectangleF(size * 0.03f, size * 0.03f, size * 0.94f, size * 0.94f);
            using var path = RoundRect(r, size * 0.22f);
            using var fill = new Drawing.Drawing2D.LinearGradientBrush(r, Drawing.Color.FromArgb(0xF3, 0xD8, 0x95), Drawing.Color.FromArgb(0xC9, 0x9A, 0x3B), 90f);
            g.FillPath(fill, path);
            // 顶部一道深色“日历夹”
            using var band = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x5A, 0x3A, 0x0A));
            g.FillRectangle(band, r.X + size * 0.18f, r.Y + size * 0.06f, size * 0.64f, size * 0.07f);
            var text = day.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var font = new Drawing.Font("Segoe UI", size * (text.Length > 1 ? 0.50f : 0.58f), Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var ink = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x2A, 0x1F, 0x05));
            var fmt = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString(text, font, ink, new Drawing.RectangleF(0, size * 0.06f, size, size), fmt);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static Drawing.Drawing2D.GraphicsPath RoundRect(Drawing.RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>右键菜单（每次打开时按当前语言、状态重建）</summary>
    public void UpdateMenu()
    {
        var s = SettingsStore.Current;
        _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new MenuColors(Theme.IsDark)) { RoundedEdges = true };
        _menu.ForeColor = Theme.IsDark ? Drawing.Color.FromArgb(0xEE, 0xEE, 0xEE) : Drawing.Color.FromArgb(0x1F, 0x1A, 0x14);
        _menu.Items.Clear();
        WinForms.ToolStripMenuItem Item(string text, Action a)
        {
            var i = new WinForms.ToolStripMenuItem(text) { ForeColor = _menu.ForeColor, Padding = new WinForms.Padding(4, 3, 4, 3) };
            i.Click += (_, _) => a();
            _menu.Items.Add(i);
            return i;
        }
        var head = new WinForms.ToolStripLabel(Roc.DateLine(DateTime.Now, s.Style) + "  " + Roc.LunarShort(DateTime.Now, s.Style))
        {
            ForeColor = Theme.IsDark ? Drawing.Color.FromArgb(0xE8, 0xC7, 0x7A) : Drawing.Color.FromArgb(0x9A, 0x6B, 0x12),
            Padding = new WinForms.Padding(4, 4, 4, 4),
        };
        _menu.Items.Add(head);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Item(L.Tr("打开设置", "開啟設定"), () => App.Instance.ShowSettings());
        Item(s.WidgetEnabled ? L.Tr("隐藏桌面小组件", "隱藏桌面小工具") : L.Tr("显示桌面小组件", "顯示桌面小工具"),
            () => App.Instance.ShowWidget(!SettingsStore.Current.WidgetEnabled));
        Item(L.Tr("换一句名言", "換一句名言"), () => App.Fire(Quotes.NextAsync));
        if (s.WallpaperMode == 2) Item(L.Tr("换一张壁纸", "換一張桌布"), () => App.Fire(() => Bing.RotateAsync()));
        if (s.LockImageEnabled) Item(L.Tr("立即更新锁屏背景", "立即更新鎖定畫面背景"), () => App.Fire(() => LockImage.UpdateAsync()));
        if (s.WeatherEnabled) Item(L.Tr("刷新天气", "重新整理天氣"), () => App.Fire(() => WeatherService.RefreshAsync()));
        Item(L.Tr("预览屏保", "預覽螢幕保護程式"), ScreenSaver.Preview);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Item(L.Tr("退出", "結束"), () => App.Instance.Quit());
    }

    /// <summary>菜单颜色：跟随暗黑 / 白天</summary>
    private sealed class MenuColors(bool dark) : WinForms.ProfessionalColorTable
    {
        private Drawing.Color Bg => dark ? Drawing.Color.FromArgb(0x17, 0x1C, 0x24) : Drawing.Color.White;
        private Drawing.Color Hover => dark ? Drawing.Color.FromArgb(0x2A, 0x31, 0x3E) : Drawing.Color.FromArgb(0xF1, 0xEA, 0xDC);
        private Drawing.Color Line => dark ? Drawing.Color.FromArgb(0x33, 0x3A, 0x46) : Drawing.Color.FromArgb(0xE0, 0xD8, 0xC8);

        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Bg;
    }
}
