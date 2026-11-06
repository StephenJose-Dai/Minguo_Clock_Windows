using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>壁纸主题渐变（与安卓版一致）</summary>
internal static class WallThemes
{
    public sealed record T(string Hans, string Hant, string[] Colors, bool DarkText);

    public static readonly T[] All =
    [
        new T("墨夜", "墨夜", ["#0F2027", "#203A43", "#2C5364"], false),
        new T("晨曦", "晨曦", ["#6A3093", "#C94B7D", "#FF9A6B"], false),
        new T("青瓷", "青瓷", ["#0B3C49", "#1F6F6B", "#71B280"], false),
        new T("极光", "極光", ["#070B25", "#16305C", "#117A65"], false),
        new T("朱砂", "朱砂", ["#2B0606", "#7A1515", "#B83227"], false),
        new T("宣纸", "宣紙", ["#F7F1E3", "#EADFC8", "#DCCBA8"], true),
    ];

    public static T Current => All[Math.Clamp(SettingsStore.Current.ThemeIndex, 0, All.Length - 1)];

    public static LinearGradientBrush Brush(T t)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < t.Colors.Length; i++)
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(t.Colors[i]), i / (double)(t.Colors.Length - 1)));
        return b;
    }
}

/// <summary>
/// 民国时钟画面：壁纸 + 时间 + 民国日期 + 农历 + 天气 + 名言 + 签名。
/// 屏保（Screensaver）、锁屏背景图（LockImage）、设置页预览（Preview）共用。内容按 1600×900 设计，用 Viewbox 等比缩放到任意屏幕。
/// 锁屏背景图：不画时间和星期（系统锁屏自己会显示），每一块（日期、农历、天气、名言、签名）都能在设置页里拖动位置。
/// </summary>
internal sealed class ClockFace : Grid
{
    public enum Kind { Screensaver, LockImage, Preview }

    public const double DesignW = 1600, DesignH = 900;

    /// <summary>锁屏图可拖动的块（顺序 = 默认从上到下的排列）</summary>
    public static readonly string[] LockBlocks = ["date", "lunar", "weather", "quote", "signature"];
    private static readonly double[] DefaultGaps = [0, 6, 26, 40, 22];
    private const double DefaultRight = 1500; // 默认靠右对齐的位置（避开 Win10 左下、Win11 上方的系统时钟）

    private readonly Kind _kind;
    private readonly bool _editable;
    private readonly Image _bg = new() { Stretch = Stretch.UniformToFill };
    private readonly Rectangle _dim = new() { Fill = Brushes.Black };
    private readonly TextBlock _time = new();
    private readonly TextBlock _seconds = new();
    private readonly TextBlock _ampm = new();
    private readonly TextBlock _date = new();
    private readonly TextBlock _lunar = new();
    private readonly StackPanel _weatherRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _weather = new();
    private readonly TextBlock _advice = new();
    private readonly TextBlock _quote = new();
    private readonly TextBlock _signature = new();
    private readonly StackPanel _content = new();
    private readonly Canvas _canvas = new() { Width = DesignW, Height = DesignH };
    private readonly Dictionary<string, Border> _blocks = new();
    private Brush _text = Brushes.White;
    private Brush _sub = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));

    /// <summary>编辑模式下拖完一块后触发（位置已写进设置，由调用方保存）</summary>
    public event Action? LayoutEdited;

    public ClockFace(Kind kind, bool editable = false)
    {
        _kind = kind;
        _editable = editable && kind == Kind.LockImage;
        ClipToBounds = true;
        Background = Brushes.Black;
        Children.Add(_bg);
        Children.Add(_dim);

        var design = new Grid { Width = DesignW, Height = DesignH };
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = design });
        var shadow = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black };

        if (kind == Kind.LockImage)
        {
            BuildLockBlocks();
            if (_editable) design.Children.Add(SystemClockHint());
            design.Children.Add(_canvas);
            _canvas.Effect = shadow;
        }
        else
        {
            BuildCentered();
            design.Children.Add(_content);
            _content.Effect = shadow;
        }
        LoadBackground();
    }

    // ---------------- 屏保 / 预览：居中排列 ----------------

    private void BuildCentered()
    {
        _content.VerticalAlignment = VerticalAlignment.Center;
        _content.HorizontalAlignment = HorizontalAlignment.Center;
        _content.Width = 1300;

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _time.FontSize = 230;
        _time.FontWeight = FontWeights.Light;
        _seconds.FontSize = 70;
        _seconds.Margin = new Thickness(18, 0, 0, 38);
        _seconds.VerticalAlignment = VerticalAlignment.Bottom;
        _ampm.FontSize = 48;
        _ampm.Margin = new Thickness(0, 0, 18, 46);
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        timeRow.Children.Add(_ampm);
        timeRow.Children.Add(_time);
        timeRow.Children.Add(_seconds);
        _content.Children.Add(timeRow);

        void Add(TextBlock t, double size, double top)
        {
            SetupText(t, size, TextAlignment.Center, HorizontalAlignment.Center);
            t.Margin = new Thickness(0, top, 0, 0);
            _content.Children.Add(t);
        }
        Add(_date, 56, 6);
        Add(_lunar, 34, 10);
        _weatherRow.Margin = new Thickness(0, 30, 0, 0);
        _content.Children.Add(_weatherRow);
        Add(_advice, 28, 10);
        Add(_quote, 38, 46);
        Add(_signature, 30, 26);
        _weather.FontSize = 36;
        _weather.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void SetupText(TextBlock t, double size, TextAlignment align, HorizontalAlignment h)
    {
        t.FontSize = size;
        t.TextAlignment = align;
        t.TextWrapping = TextWrapping.Wrap;
        t.HorizontalAlignment = h;
    }

    // ---------------- 锁屏图：自由摆放的块 ----------------

    private void BuildLockBlocks()
    {
        SetupText(_date, 64, TextAlignment.Right, HorizontalAlignment.Right);
        _date.TextWrapping = TextWrapping.NoWrap;
        SetupText(_lunar, 34, TextAlignment.Right, HorizontalAlignment.Right);
        _lunar.TextWrapping = TextWrapping.NoWrap;
        SetupText(_advice, 28, TextAlignment.Right, HorizontalAlignment.Right);
        _advice.TextWrapping = TextWrapping.NoWrap;
        _advice.Margin = new Thickness(0, 10, 0, 0);
        SetupText(_quote, 38, TextAlignment.Right, HorizontalAlignment.Right);
        _quote.MaxWidth = 940;
        SetupText(_signature, 30, TextAlignment.Right, HorizontalAlignment.Right);
        _signature.TextWrapping = TextWrapping.NoWrap;
        _weather.FontSize = 36;
        _weather.VerticalAlignment = VerticalAlignment.Center;
        _weatherRow.HorizontalAlignment = HorizontalAlignment.Right;

        var weather = new StackPanel();
        weather.Children.Add(_weatherRow);
        weather.Children.Add(_advice);

        var content = new Dictionary<string, UIElement>
        {
            ["date"] = _date,
            ["lunar"] = _lunar,
            ["weather"] = weather,
            ["quote"] = _quote,
            ["signature"] = _signature,
        };
        foreach (var id in LockBlocks)
        {
            var b = new Border { Child = content[id], Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(8) };
            if (_editable) MakeDraggable(id, b);
            _blocks[id] = b;
            _canvas.Children.Add(b);
        }
    }

    /// <summary>编辑时标出系统时钟的大致位置，方便避开（不会画进锁屏图）</summary>
    private static UIElement SystemClockHint()
    {
        var win11 = Environment.OSVersion.Version.Build >= 22000;
        var hint = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            Width = win11 ? 460 : 470,
            Height = 230,
            HorizontalAlignment = win11 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = win11 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = win11 ? new Thickness(0, 50, 0, 0) : new Thickness(40, 0, 0, 80),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = L.Tr("系统时间日期显示在这里\n（Windows 自己显示，不在图片里）", "系統時間日期顯示在這裡\n（Windows 自己顯示，不在圖片裡）"),
                Foreground = Brushes.White,
                FontSize = 28,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.85,
            },
        };
        return hint;
    }

    private void MakeDraggable(string id, Border b)
    {
        b.Cursor = Cursors.SizeAll;
        b.BorderThickness = new Thickness(2);
        b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xE8, 0xC7, 0x7A));
        b.Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)); // 透明的地方也能按住
        b.ToolTip = L.Tr("按住拖动", "按住拖曳");
        Point start = default;
        double left = 0, top = 0;
        var dragging = false;
        b.MouseEnter += (_, _) => b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC7, 0x7A));
        b.MouseLeave += (_, _) =>
        {
            if (!dragging) b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xE8, 0xC7, 0x7A));
        };
        b.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            start = e.GetPosition(_canvas); // 已经换算成 1600×900 设计坐标
            left = Canvas.GetLeft(b);
            top = Canvas.GetTop(b);
            b.CaptureMouse();
            e.Handled = true;
        };
        b.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(_canvas);
            var w = b.ActualWidth;
            var h = b.ActualHeight;
            Canvas.SetLeft(b, Math.Clamp(left + p.X - start.X, 0, Math.Max(0, DesignW - w)));
            Canvas.SetTop(b, Math.Clamp(top + p.Y - start.Y, 0, Math.Max(0, DesignH - h)));
        };
        b.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            b.ReleaseMouseCapture();
            SaveLayout();
            LayoutEdited?.Invoke();
        };
    }

    /// <summary>把所有块当前的位置写进设置（拖动其中一块时其他块保持不动）。
    /// 记的是“右边缘 x、上边缘 y”：文字靠右对齐，名言变长变短时右边不跑位。</summary>
    private void SaveLayout()
    {
        var layout = SettingsStore.Current.LockLayout;
        foreach (var (id, b) in _blocks)
        {
            if (b.Visibility != Visibility.Visible && !layout.ContainsKey(id)) continue;
            layout[id] = [Math.Round(Canvas.GetLeft(b) + b.ActualWidth, 1), Math.Round(Canvas.GetTop(b), 1)];
        }
    }

    /// <summary>按设置摆放；没拖过的块按默认位置（靠右，从上到下）</summary>
    private void PlaceBlocks()
    {
        var layout = SettingsStore.Current.LockLayout;
        var sizes = new Dictionary<string, Size>();
        double total = 0;
        for (var i = 0; i < LockBlocks.Length; i++)
        {
            var b = _blocks[LockBlocks[i]];
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            sizes[LockBlocks[i]] = b.DesiredSize;
            if (b.Visibility == Visibility.Visible) total += b.DesiredSize.Height + DefaultGaps[i];
        }
        var y = Math.Max(20, (DesignH - total) / 2 + 30);
        for (var i = 0; i < LockBlocks.Length; i++)
        {
            var id = LockBlocks[i];
            var b = _blocks[id];
            var size = sizes[id];
            if (b.Visibility == Visibility.Visible) y += DefaultGaps[i];
            if (layout.TryGetValue(id, out var pos) && pos.Length == 2)
            {
                Canvas.SetLeft(b, Math.Clamp(pos[0] - size.Width, 0, Math.Max(0, DesignW - size.Width)));
                Canvas.SetTop(b, Math.Clamp(pos[1], 0, Math.Max(0, DesignH - size.Height)));
            }
            else
            {
                Canvas.SetLeft(b, Math.Max(0, DefaultRight - size.Width));
                Canvas.SetTop(b, y);
            }
            if (b.Visibility == Visibility.Visible) y += size.Height;
        }
    }

    // ---------------- 壁纸 / 文字 ----------------

    /// <summary>按设置载入壁纸（必应 / 自选图片 / 主题渐变）</summary>
    public void LoadBackground()
    {
        var s = SettingsStore.Current;
        var file = s.WallpaperMode switch
        {
            1 => File.Exists(s.CustomWallpaper) ? s.CustomWallpaper : null,
            2 => Bing.CurrentFile,
            _ => null,
        };
        ImageSource? img = null;
        if (file != null)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad; // 读完就释放文件，不锁文件
                bi.UriSource = new Uri(file);
                bi.DecodePixelWidth = 2560;
                bi.EndInit();
                bi.Freeze();
                img = bi;
            }
            catch
            {
                img = null;
            }
        }
        var darkText = false;
        if (img != null)
        {
            _bg.Source = img;
            Background = Brushes.Black;
            _dim.Opacity = Math.Clamp(s.WallpaperDim, 0, 70) / 100.0;
        }
        else
        {
            _bg.Source = null;
            var t = WallThemes.Current;
            Background = WallThemes.Brush(t);
            _dim.Opacity = 0;
            darkText = t.DarkText;
        }
        _text = darkText ? new SolidColorBrush(Color.FromRgb(0x3B, 0x2A, 0x1A)) : Brushes.White;
        _sub = darkText ? new SolidColorBrush(Color.FromArgb(0xA6, 0x3B, 0x2A, 0x1A)) : new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
        var fx = darkText ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black };
        if (_kind == Kind.LockImage) _canvas.Effect = fx;
        else _content.Effect = fx;
        foreach (var t in new[] { _time, _seconds, _date, _quote }) t.Foreground = _text;
        foreach (var t in new[] { _ampm, _lunar, _weather, _advice, _signature }) t.Foreground = _sub;
        Refresh(DateTime.Now);
    }

    public static bool Is24Hour()
    {
        var v = SettingsStore.Current.Use24Hour;
        return v ?? CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
    }

    /// <summary>更新文字（屏保每秒调用）</summary>
    public void Refresh(DateTime now)
    {
        var s = SettingsStore.Current;
        var style = s.Style;
        var font = AppFonts.Of(s.FontId);
        foreach (var t in new[] { _time, _seconds, _ampm, _date, _lunar, _weather, _advice, _quote, _signature }) t.FontFamily = font;

        if (_kind != Kind.LockImage)
        {
            var (hm, ampm) = Roc.HourMinute(now, Is24Hour());
            _time.Text = hm;
            _ampm.Text = ampm ?? "";
            _ampm.Visibility = ampm == null ? Visibility.Collapsed : Visibility.Visible;
            _seconds.Text = now.Second.ToString("00", CultureInfo.InvariantCulture);
            _seconds.Visibility = s.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        }
        // 系统锁屏已经显示“11月6日，星期五”，锁屏图只写民国日期，不重复星期
        if (_kind == Kind.LockImage && s.LockDateStyle == 0)
        {
            // 系统锁屏已经显示“11月6日，星期五”：这里写“民国115年 · 丙午马年”+“农历九月廿八”，不重复公历月日
            var lunar = Roc.LunarLine(now, style);         // 农历丙午马年 九月廿八
            var sp = lunar.IndexOf(' ');
            var head = sp > 2 ? lunar[2..sp] : "";          // 丙午马年
            var prefix = sp > 2 ? lunar[..2] : "";          // 农历
            _date.Text = Roc.YearText(now.Year, style) + (head.Length > 0 ? " · " + head : "");
            _lunar.Text = sp > 0 ? prefix + lunar[(sp + 1)..] : lunar;
        }
        else
        {
            // 屏保、预览：完整日期 + 星期；锁屏图选“完整民国日期”时不加星期（系统已显示）
            _date.Text = Roc.DateLine(now, style) + (_kind == Kind.LockImage ? "" : "  " + Roc.Weekday(now));
            _lunar.Text = Roc.LunarLine(now, style);
        }

        var w = s.WeatherEnabled ? WeatherService.Current : null;
        _weatherRow.Children.Clear();
        if (w != null)
        {
            _weatherRow.Children.Add(Icons.WeatherIcon(w.Cond.Icon(w.IsDay), _sub, 44));
            _weather.Margin = new Thickness(14, 0, 0, 0);
            var aqi = w.Aqi is { } a ? "  " + L.Tr("空气", "空氣") + " " + WeatherService.AqiLabel(a) : "";
            _weather.Text = WeatherService.Summary(w) + aqi + "  ·  " + WeatherService.CityLabel(w);
            _weatherRow.Children.Add(_weather);
            _weatherRow.Visibility = Visibility.Visible;
            var tip = WeatherService.Advice(w).FirstOrDefault();
            _advice.Text = tip ?? "";
            _advice.Visibility = tip == null ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _weatherRow.Visibility = Visibility.Collapsed;
            _advice.Visibility = Visibility.Collapsed;
        }

        _quote.Text = "「" + Quotes.Current + "」";
        _signature.Text = s.Signature;
        _signature.Visibility = string.IsNullOrWhiteSpace(s.Signature) ? Visibility.Collapsed : Visibility.Visible;

        if (_kind == Kind.LockImage)
        {
            _blocks["weather"].Visibility = w != null ? Visibility.Visible : Visibility.Collapsed;
            _blocks["signature"].Visibility = _signature.Visibility;
            PlaceBlocks();
        }
    }
}
