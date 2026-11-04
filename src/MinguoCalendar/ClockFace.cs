using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
/// 屏保（Screensaver）、锁屏背景图（LockImage，不画时间，因为系统锁屏自己会显示时间）、设置页预览（Preview）共用。
/// 内容按 1600×900 设计，用 Viewbox 等比缩放到任意屏幕。
/// </summary>
internal sealed class ClockFace : Grid
{
    public enum Kind { Screensaver, LockImage, Preview }

    private readonly Kind _kind;
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
    private Brush _text = Brushes.White;
    private Brush _sub = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));

    public ClockFace(Kind kind)
    {
        _kind = kind;
        ClipToBounds = true;
        Background = Brushes.Black;
        Children.Add(_bg);
        Children.Add(_dim);

        var design = new Grid { Width = 1600, Height = 900 };
        _content.VerticalAlignment = VerticalAlignment.Center;
        if (kind == Kind.LockImage)
        {
            // 锁屏图：系统时钟在左下（Win10）或上方正中（Win11），我们的内容放右侧
            _content.HorizontalAlignment = HorizontalAlignment.Right;
            _content.Margin = new Thickness(0, 60, 110, 0);
            _content.Width = 760;
        }
        else
        {
            _content.HorizontalAlignment = HorizontalAlignment.Center;
            _content.Width = 1300;
        }
        design.Children.Add(_content);
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = design });

        if (kind != Kind.LockImage)
        {
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
        }

        var align = kind == Kind.LockImage ? TextAlignment.Right : TextAlignment.Center;
        var halign = kind == Kind.LockImage ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        void Setup(TextBlock t, double size, double top)
        {
            t.FontSize = size;
            t.TextAlignment = align;
            t.TextWrapping = TextWrapping.Wrap;
            t.HorizontalAlignment = halign;
            t.Margin = new Thickness(0, top, 0, 0);
            _content.Children.Add(t);
        }
        Setup(_date, kind == Kind.LockImage ? 64 : 56, kind == Kind.LockImage ? 0 : 6);
        Setup(_lunar, 34, 10);
        _weatherRow.HorizontalAlignment = halign;
        _weatherRow.Margin = new Thickness(0, 30, 0, 0);
        _weather.FontSize = 36;
        _weather.VerticalAlignment = VerticalAlignment.Center;
        _weather.TextWrapping = TextWrapping.Wrap;
        _content.Children.Add(_weatherRow);
        Setup(_advice, 28, 10);
        Setup(_quote, 38, 46);
        Setup(_signature, 30, 26);

        Effect = null;
        _content.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black };
        LoadBackground();
        Refresh(DateTime.Now);
    }

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
        _content.Effect = darkText ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black };
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
        _date.Text = Roc.DateLine(now, style) + "  " + Roc.Weekday(now);
        _lunar.Text = Roc.LunarLine(now, style);

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
    }
}
