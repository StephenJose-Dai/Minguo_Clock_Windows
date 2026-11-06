using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>
/// 时钟组件：时分秒、民国日期星期、农历、天气、名言。透明、无边框，固定在桌面。
/// 系统资源在另一个独立组件里（SysWidgetWindow），各自摆放。
/// </summary>
public sealed class WidgetWindow : DesktopWidget
{
    private readonly TextBlock _time = new() { FontSize = 96, FontWeight = FontWeights.Light };
    private readonly TextBlock _seconds = new() { FontSize = 34, Margin = new Thickness(10, 0, 0, 16), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _ampm = new() { FontSize = 26, Margin = new Thickness(0, 0, 10, 18), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _date = new() { FontSize = 26, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _lunar = new() { FontSize = 19, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _weatherRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _weather = new() { FontSize = 19, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _quote = new() { FontSize = 18, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand, MaxWidth = 560 };
    private readonly StackPanel _content = new() { Margin = new Thickness(22, 14, 22, 14), MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _clock = new();
    private Brush _text = Brushes.White, _sub = Brushes.White;

    protected override double CfgX { get => SettingsStore.Current.WidgetX; set => SettingsStore.Current.WidgetX = value; }
    protected override double CfgY { get => SettingsStore.Current.WidgetY; set => SettingsStore.Current.WidgetY = value; }
    protected override double CfgW { get => SettingsStore.Current.WidgetWidth; set => SettingsStore.Current.WidgetWidth = value; }
    protected override double CfgH { get => SettingsStore.Current.WidgetHeight; set => SettingsStore.Current.WidgetHeight = value; }
    protected override bool Locked { get => SettingsStore.Current.WidgetLocked; set => SettingsStore.Current.WidgetLocked = value; }
    protected override (double W, double H) DefaultSize => (420, 300);
    protected override double MinW => 220;
    protected override double MinH => 120;

    public WidgetWindow()
    {
        Init("MinguoWidget");
        _clock.Tick += (_, _) => TickClock();
        Loaded += (_, _) => { Refresh(); TickClock(); };
        App.Refreshed += Refresh;
        Theme.Changed += Refresh;
        Scheduler.Minute += Refresh;
        Closed += (_, _) =>
        {
            _clock.Stop();
            App.Refreshed -= Refresh;
            Theme.Changed -= Refresh;
            Scheduler.Minute -= Refresh;
        };
    }

    protected override FrameworkElement BuildContent()
    {
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        timeRow.Children.Add(_ampm);
        timeRow.Children.Add(_time);
        timeRow.Children.Add(_seconds);
        _content.Children.Add(timeRow);
        _content.Children.Add(_date);
        _content.Children.Add(_lunar);
        _content.Children.Add(_weatherRow);
        _content.Children.Add(_quote);
        return _content;
    }

    protected override bool OnPlainClick(MouseButtonEventArgs e)
    {
        var onQuote = e.OriginalSource is DependencyObject d && (d == _quote || (d is Visual v && _quote.IsAncestorOf(v)));
        if (onQuote && _quote.Visibility == Visibility.Visible)
        {
            App.Fire(Quotes.NextAsync);
            return true;
        }
        return false;
    }

    private void TickClock()
    {
        var now = DateTime.Now;
        var (hm, ampm) = Roc.HourMinute(now, ClockFace.Is24Hour());
        _time.Text = hm;
        _ampm.Text = ampm ?? "";
        _ampm.Visibility = ampm == null ? Visibility.Collapsed : Visibility.Visible;
        _seconds.Text = now.Second.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        _clock.Interval = TimeSpan.FromMilliseconds(Math.Max(50, 1000 - now.Millisecond + 10));
        _clock.Start();
    }

    public void Refresh()
    {
        var s = SettingsStore.Current;
        var dark = s.WidgetTextStyle == 1;
        _text = dark ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x14)) : Brushes.White;
        _sub = dark ? new SolidColorBrush(Color.FromArgb(0xC0, 0x1F, 0x1A, 0x14)) : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        _content.Effect = dark
            ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45, Color = Colors.White }
            : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.75, Color = Colors.Black };
        SetChromeColors(dark);

        var font = AppFonts.Of(s.FontId);
        foreach (var t in new[] { _time, _seconds, _ampm, _date, _lunar, _weather, _quote }) t.FontFamily = font;
        _time.Foreground = _text;
        _seconds.Foreground = _text;
        _date.Foreground = _text;
        _ampm.Foreground = _sub;
        _lunar.Foreground = _sub;
        _weather.Foreground = _sub;
        _quote.Foreground = _sub;

        var now = DateTime.Now;
        var style = s.Style;
        _date.Text = Roc.DateLine(now, style) + "  " + Roc.Weekday(now);
        _lunar.Text = Roc.LunarLine(now, style);

        var w = s.WeatherEnabled && s.WidgetShowWeather ? WeatherService.Current : null;
        _weatherRow.Children.Clear();
        if (w != null)
        {
            var icon = Icons.WeatherIcon(w.Cond.Icon(w.IsDay), _sub, 24);
            icon.Margin = new Thickness(0, 0, 8, 0);
            _weatherRow.Children.Add(icon);
            _weather.Text = WeatherService.Summary(w) + "  ·  " + WeatherService.CityLabel(w);
            _weatherRow.Children.Add(_weather);
            _weatherRow.Visibility = Visibility.Visible;
        }
        else
        {
            _weatherRow.Visibility = Visibility.Collapsed;
        }

        _quote.Visibility = s.WidgetShowQuote ? Visibility.Visible : Visibility.Collapsed;
        _quote.Text = "「" + Quotes.Current + "」";
        _quote.ToolTip = L.Tr("点一下换一句", "點一下換一句");

        TickClock();
        LayoutContent();
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
            s.WidgetLocked = !s.WidgetLocked;
            SettingsStore.Save();
            ShowChrome(false);
        }, true, s.WidgetLocked);
        Item(L.Tr("换一句", "換一句"), () => App.Fire(Quotes.NextAsync));
        Item(L.Tr("文字颜色：", "文字顏色：") + (s.WidgetTextStyle == 1 ? L.Tr("改为白字", "改為白字") : L.Tr("改为深色字", "改為深色字")), () =>
        {
            s.WidgetTextStyle = s.WidgetTextStyle == 1 ? 0 : 1;
            SettingsStore.Save();
            Refresh();
        });
        Item(L.Tr("恢复默认位置和大小", "恢復預設位置和大小"), ResetBounds);
        m.Items.Add(new Separator());
        Item(L.Tr("设置…", "設定…"), () => App.Instance.ShowSettings("widget"));
        Item(L.Tr("隐藏时钟组件", "隱藏時鐘組件"), () => App.Instance.ShowWidget(false));
    }
}
