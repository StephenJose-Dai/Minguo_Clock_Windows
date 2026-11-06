using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>
/// 系统资源组件：CPU / 显卡 / 内存 / 磁盘 / 温度 / 网速 / 连接数 / 开机时长。
/// 独立于时钟组件，可单独摆放、调大小、设透明度（可淡成水印）。进度条带动画；点“连接”看详情。
/// </summary>
public sealed class SysWidgetWindow : DesktopWidget
{
    private const double TrackW = 210;

    private readonly StackPanel _content = new() { Margin = new Thickness(18, 12, 18, 12), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, MetricRow> _rows = new();
    private StackPanel? _connsPanel;
    private StackPanel? _netPanel;
    private string _built = "";
    private Brush _text = Brushes.White, _sub = Brushes.White;

    protected override double CfgX { get => SettingsStore.Current.SysWidgetX; set => SettingsStore.Current.SysWidgetX = value; }
    protected override double CfgY { get => SettingsStore.Current.SysWidgetY; set => SettingsStore.Current.SysWidgetY = value; }
    protected override double CfgW { get => SettingsStore.Current.SysWidgetWidth; set => SettingsStore.Current.SysWidgetWidth = value; }
    protected override double CfgH { get => SettingsStore.Current.SysWidgetHeight; set => SettingsStore.Current.SysWidgetHeight = value; }
    protected override bool Locked { get => SettingsStore.Current.SysWidgetLocked; set => SettingsStore.Current.SysWidgetLocked = value; }
    protected override (double W, double H) DefaultSize => (340, 300);
    protected override double MinW => 200;
    protected override double MinH => 110;
    protected override double ContentOpacity => SettingsStore.Current.SysWidgetOpacity;

    public SysWidgetWindow()
    {
        Init("MinguoSysWidget");
        SysInfo.Acquire();
        SysInfo.Updated += OnData;
        App.Refreshed += Refresh;
        Theme.Changed += Refresh;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            SysInfo.Updated -= OnData;
            App.Refreshed -= Refresh;
            Theme.Changed -= Refresh;
            SysInfo.Release();
        };
    }

    private sealed class MetricRow
    {
        public required StackPanel Panel;
        public required TextBlock Value;
        public Border? Fill;     // 进度条（磁盘）
        public Spark? Spark;     // 动态折线（CPU/显卡/内存/网络）
    }

    /// <summary>动态折线图：渐变填充 + 顶部描线，数据滚动更新，随窗口缩放</summary>
    private sealed class Spark
    {
        private const double W = 240, H = 34;
        private readonly Polygon _area = new();
        private readonly Polyline _line = new() { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        public Grid Root { get; }

        public Spark()
        {
            var canvas = new Canvas { Width = W, Height = H, ClipToBounds = true };
            canvas.Children.Add(_area);
            canvas.Children.Add(_line);
            Root = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            Root.Children.Add(canvas);
        }

        public void Update(IReadOnlyList<double> vals, double max, Color color)
        {
            if (vals.Count < 2) { _area.Points = null; _line.Points = null; return; }
            var n = vals.Count;
            max = Math.Max(max, 1);
            var pts = new PointCollection(n);
            for (var i = 0; i < n; i++)
            {
                var x = i / (double)(n - 1) * W;
                var y = H - Math.Clamp(vals[i] / max, 0, 1) * (H - 2) - 1;
                pts.Add(new Point(x, y));
            }
            _line.Points = pts;
            _line.Stroke = new SolidColorBrush(color);
            // 快满了（最后一个值超过 90%）给折线加红色辉光
            var hot = vals[n - 1] / max >= 0.9;
            _line.Effect = hot ? new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x4D, 0x4D), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 } : null;
            var area = new PointCollection(pts) { new Point(W, H), new Point(0, H) };
            _area.Points = area;
            _area.Fill = new LinearGradientBrush(
                Color.FromArgb(0x80, color.R, color.G, color.B),
                Color.FromArgb(0x08, color.R, color.G, color.B),
                90);
        }
    }

    protected override FrameworkElement BuildContent()
    {
        _content.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.75, Color = Colors.Black };
        return _content;
    }

    /// <summary>后台采样回调：只更新数值、动画进度条，不重排</summary>
    private void OnData()
    {
        var st = SysInfo.Current;
        if (st == null) return;
        // 需要的行集合变了（磁盘增减、开关变化）才重建
        if (Signature(st) != _built) { Refresh(); return; }
        UpdateValues(st);
    }

    private static string Signature(SysStat st)
    {
        var s = SettingsStore.Current;
        var ids = new List<string>();
        if (s.SysShowCpu) ids.Add("cpu");
        if (s.SysShowGpu && st.Gpu is not null) ids.Add("gpu");
        if (s.SysShowMem) ids.Add("mem");
        if (s.SysShowDisk) ids.AddRange(st.Disks.Select(d => "disk:" + d.Name));
        if (s.SysShowDiskIo) ids.Add("diskio");
        if (s.SysShowTemp) ids.Add("temp");
        if (s.SysShowNet) ids.Add("net");
        if (s.SysShowConns) ids.Add("conns");
        if (s.SysShowUptime) ids.Add("up");
        return string.Join("|", ids);
    }

    public void Refresh()
    {
        var s = SettingsStore.Current;
        var dark = s.SysWidgetTextStyle == 1;
        _text = dark ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x14)) : Brushes.White;
        _sub = dark ? new SolidColorBrush(Color.FromArgb(0xC0, 0x1F, 0x1A, 0x14)) : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        _content.Effect = dark
            ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45, Color = Colors.White }
            : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.75, Color = Colors.Black };
        SetChromeColors(dark);
        ApplyContentOpacity();

        var st = SysInfo.Current;
        _built = st == null ? "" : Signature(st);
        Rebuild(st);
        if (st != null) UpdateValues(st);
        LayoutContent();
    }

    private void Rebuild(SysStat? st)
    {
        _content.Children.Clear();
        _rows.Clear();
        _connsPanel = null;
        _netPanel = null;
        var s = SettingsStore.Current;
        var font = AppFonts.Of(s.FontId);

        // kind: 0 纯文字，1 进度条，2 动态折线
        MetricRow Row(string id, string label, int kind)
        {
            var box = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = label, FontSize = 16, Foreground = _sub, FontFamily = font, VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock { FontSize = 16, Foreground = _text, FontFamily = font, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(v, 1);
            g.Children.Add(l);
            g.Children.Add(v);
            box.Children.Add(g);
            Border? fill = null;
            Spark? spark = null;
            if (kind == 1)
            {
                var track = new Border { Width = TrackW, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                fill = new Border { Width = 0, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
                track.Child = fill;
                box.Children.Add(track);
            }
            else if (kind == 2)
            {
                spark = new Spark();
                box.Children.Add(spark.Root);
            }
            var row = new MetricRow { Panel = box, Value = v, Fill = fill, Spark = spark };
            _rows[id] = row;
            _content.Children.Add(box);
            return row;
        }

        if (st == null)
        {
            var t = new TextBlock { Text = L.Tr("读取系统资源…", "讀取系統資源…"), FontSize = 16, Foreground = _sub, FontFamily = font };
            _content.Children.Add(t);
            return;
        }
        if (s.SysShowCpu) Row("cpu", "CPU", 2);
        if (s.SysShowGpu && st.Gpu is not null) Row("gpu", L.Tr("显卡", "顯示卡"), 2);
        if (s.SysShowMem) Row("mem", L.Tr("内存", "記憶體"), 2);
        if (s.SysShowDisk) foreach (var d in st.Disks) Row("disk:" + d.Name, L.Tr("磁盘 ", "磁碟 ") + d.Name, 1);
        if (s.SysShowDiskIo) Row("diskio", L.Tr("磁盘读写", "磁碟讀寫"), 2);
        if (s.SysShowTemp) Row("temp", L.Tr("温度", "溫度"), 0);
        if (s.SysShowNet)
        {
            var r = Row("net", L.Tr("网络", "網路"), 2);
            r.Panel.Cursor = Cursors.Hand;
            r.Panel.ToolTip = L.Tr("点一下看连接详情", "點一下看連線詳情");
            _netPanel = r.Panel;
        }
        if (s.SysShowConns)
        {
            var r = Row("conns", L.Tr("连接", "連線"), 0);
            r.Panel.Cursor = Cursors.Hand;
            r.Panel.ToolTip = L.Tr("点一下看连接详情", "點一下看連線詳情");
            _connsPanel = r.Panel;
        }
        if (s.SysShowUptime) Row("up", L.Tr("开机", "開機"), 0);
    }

    private void UpdateValues(SysStat st)
    {
        var s = SettingsStore.Current;
        void Bar(string id, double frac)
        {
            if (!_rows.TryGetValue(id, out var r) || r.Fill == null) return;
            var f = Math.Clamp(frac, 0, 1);
            var color = f < 0.7 ? Color.FromRgb(0x5B, 0xD6, 0x8A) : f < 0.9 ? Color.FromRgb(0xFF, 0xB4, 0x54) : Color.FromRgb(0xFF, 0x6B, 0x6B);
            r.Fill.Background = new LinearGradientBrush(
                Color.FromArgb(0xFF, color.R, color.G, color.B),
                Color.FromArgb(0xB0, color.R, color.G, color.B), 0);
            // 快满了（≥90%）加红色辉光
            r.Fill.Effect = f >= 0.9 ? new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x4D, 0x4D), BlurRadius = 7, ShadowDepth = 0, Opacity = 0.9 } : null;
            var anim = new DoubleAnimation(f * TrackW, TimeSpan.FromMilliseconds(500)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            r.Fill.BeginAnimation(WidthProperty, anim);
        }
        void Val(string id, string text)
        {
            if (_rows.TryGetValue(id, out var r)) r.Value.Text = text;
        }
        static Color Grade(double frac) => frac < 0.7 ? Color.FromRgb(0x5B, 0xD6, 0x8A) : frac < 0.9 ? Color.FromRgb(0xFF, 0xB4, 0x54) : Color.FromRgb(0xFF, 0x6B, 0x6B);
        void Line(string id, IReadOnlyList<double> hist, double max, Color col)
        {
            if (_rows.TryGetValue(id, out var r)) r.Spark?.Update(hist, max, col);
        }

        if (s.SysShowCpu)
        {
            var t = s.SysShowTemp && st.TempC is { } c ? "  " + SysInfo.Temp(c) : "";
            Val("cpu", SysInfo.Pct(st.Cpu) + t);
            Line("cpu", SysInfo.CpuHist, 100, Grade(st.Cpu / 100));
        }
        if (s.SysShowGpu && st.Gpu is { } g)
        {
            Val("gpu", SysInfo.Pct(g));
            Line("gpu", SysInfo.GpuHist, 100, Grade(g / 100));
        }
        if (s.SysShowMem)
        {
            Val("mem", $"{SysInfo.Pct(st.MemPercent)}  {SysInfo.Gb(st.MemUsedGB)}/{SysInfo.Gb(st.MemTotalGB)} GB");
            Line("mem", SysInfo.MemHist, 100, Grade(st.MemPercent / 100));
        }
        if (s.SysShowDisk)
            foreach (var d in st.Disks)
            {
                Val("disk:" + d.Name, $"{SysInfo.Pct(d.Percent)}  {SysInfo.Gb(d.UsedGB)}/{SysInfo.Gb(d.TotalGB)} GB");
                Bar("disk:" + d.Name, d.Percent / 100);
            }
        if (s.SysShowDiskIo)
        {
            Val("diskio", $"{L.Tr("读", "讀")} {SysInfo.Speed(st.DiskRead)}   {L.Tr("写", "寫")} {SysInfo.Speed(st.DiskWrite)}");
            Line("diskio", SysInfo.DiskIoHist, SysInfo.DiskIoMax, Color.FromRgb(0xC9, 0x9A, 0xF0));
        }
        if (s.SysShowTemp) Val("temp", SysInfo.Temp(st.TempC));
        if (s.SysShowNet)
        {
            Val("net", $"↓ {SysInfo.Speed(st.NetDown)}   ↑ {SysInfo.Speed(st.NetUp)}");
            Line("net", SysInfo.NetHist, SysInfo.NetMax, Color.FromRgb(0x6A, 0xC7, 0xFF));
        }
        if (s.SysShowConns) Val("conns", st.ConnCount + L.Tr(" 个 · 详情", " 個 · 詳情"));
        if (s.SysShowUptime) Val("up", SysInfo.UptimeText(st.Uptime));
    }

    protected override bool OnPlainClick(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Visual v
            && ((_connsPanel != null && _connsPanel.IsAncestorOf(v)) || (_netPanel != null && _netPanel.IsAncestorOf(v))))
        {
            ConnectionsWindow.ShowSingleton();
            return true;
        }
        return false;
    }

    protected override void FillMenu(ContextMenu m)
    {
        var s = SettingsStore.Current;
        MenuItem Item(string text, Action a, bool check = false, bool isChecked = false)
        {
            var i = new MenuItem { Header = text, IsCheckable = check, IsChecked = check && isChecked };
            i.Click += (_, _) => a();
            m.Items.Add(i);
            return i;
        }
        Item(L.Tr("锁定位置和大小", "鎖定位置和大小"), () =>
        {
            s.SysWidgetLocked = !s.SysWidgetLocked;
            SettingsStore.Save();
            ShowChrome(false);
        }, true, s.SysWidgetLocked);
        if (s.SysShowConns) Item(L.Tr("连接详情…", "連線詳情…"), ConnectionsWindow.ShowSingleton);
        Item(L.Tr("文字颜色：", "文字顏色：") + (s.SysWidgetTextStyle == 1 ? L.Tr("改为白字", "改為白字") : L.Tr("改为深色字", "改為深色字")), () =>
        {
            s.SysWidgetTextStyle = s.SysWidgetTextStyle == 1 ? 0 : 1;
            SettingsStore.Save();
            Refresh();
        });
        Item(L.Tr("恢复默认位置和大小", "恢復預設位置和大小"), ResetBounds);
        m.Items.Add(new Separator());
        Item(L.Tr("设置…", "設定…"), () => App.Instance.ShowSettings("sys"));
        Item(L.Tr("隐藏资源组件", "隱藏資源組件"), () => App.Instance.ShowSysWidget(false));
    }
}
