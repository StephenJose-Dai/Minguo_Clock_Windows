using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Minguo.Core;
using WinForms = System.Windows.Forms;

namespace MinguoCalendar;

/// <summary>
/// 桌面小组件：完全透明（没有底色、没有边框），固定在桌面层（不会盖住其他窗口，“显示桌面”时也不会被收起）。
/// 鼠标放上去才出现淡淡的虚线框和调整手柄；拖动移动，拖边角调整宽高；右键菜单可锁定位置。
/// 内容按高度 300 设计，随窗口等比缩放；变宽时文字不变大，只是名言换行更少。
/// </summary>
public sealed class WidgetWindow : Window
{
    private const double DesignHeight = 300;
    private const double MinW = 220, MinH = 120;

    private readonly Grid _design = new() { Height = DesignHeight };
    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform };
    private readonly Rectangle _frame = new()
    {
        StrokeThickness = 1.2,
        StrokeDashArray = new DoubleCollection { 4, 3 },
        RadiusX = 14,
        RadiusY = 14,
        Visibility = Visibility.Hidden,
        IsHitTestVisible = false,
    };
    private readonly Border _hover = new() { CornerRadius = new CornerRadius(14), Visibility = Visibility.Hidden, IsHitTestVisible = false };
    private readonly Grid _handles = new() { Visibility = Visibility.Hidden };

    private readonly TextBlock _time = new() { FontSize = 96, FontWeight = FontWeights.Light };
    private readonly TextBlock _ampm = new() { FontSize = 26, Margin = new Thickness(0, 0, 10, 18), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _date = new() { FontSize = 26, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _lunar = new() { FontSize = 19, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _weatherRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _weather = new() { FontSize = 19, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _quote = new() { FontSize = 18, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand };
    private readonly StackPanel _content = new() { Margin = new Thickness(22, 14, 22, 14) };
    private readonly DispatcherTimer _clock = new();
    private IntPtr _hwnd;
    private Brush _text = Brushes.White, _sub = Brushes.White;

    public WidgetWindow()
    {
        Title = "MinguoWidget";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 透明度 1/255：肉眼看不见，但鼠标能点到（完全透明的地方鼠标会穿过去，就拖不动了）
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        UseLayoutRounding = true;

        var s = SettingsStore.Current;
        Width = Math.Max(MinW, s.WidgetWidth);
        Height = Math.Max(MinH, s.WidgetHeight);
        var wa = SystemParameters.WorkArea;
        Left = double.IsNaN(s.WidgetX) ? wa.Right - Width - 40 : s.WidgetX;
        Top = double.IsNaN(s.WidgetY) ? wa.Top + 60 : s.WidgetY;
        ApplyMaxSize();

        // ---------- 内容 ----------
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        timeRow.Children.Add(_ampm);
        timeRow.Children.Add(_time);
        _content.Children.Add(timeRow);
        _content.Children.Add(_date);
        _content.Children.Add(_lunar);
        _content.Children.Add(_weatherRow);
        _content.Children.Add(_quote);
        _content.VerticalAlignment = VerticalAlignment.Center;
        _design.Children.Add(_content);
        _viewbox.Child = _design;

        var root = new Grid();
        root.Children.Add(_hover);
        root.Children.Add(_viewbox);
        root.Children.Add(_frame);
        BuildHandles();
        root.Children.Add(_handles);
        Content = root;

        MouseEnter += (_, _) => ShowChrome(true);
        MouseLeave += (_, _) => ShowChrome(false);
        MouseLeftButtonDown += OnMouseDown;
        SizeChanged += (_, _) => UpdateScale();
        StateChanged += (_, _) =>
        {
            // 拖到屏幕顶部时系统会尝试最大化，改回来
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        };
        SourceInitialized += OnSourceInitialized;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, _) => FillMenu(ContextMenu);

        _clock.Tick += (_, _) => TickClock();
        Loaded += (_, _) =>
        {
            Refresh();
            TickClock();
            EnsureOnScreen();
        };
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

    // ---------- 固定在桌面 ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // 不出现在 Alt+Tab
        var ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex = (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        // 归属到桌面（Progman）：始终在桌面图标之上、所有普通窗口之下，“显示桌面”也不会收起
        var progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero) Native.SetWindowLong(_hwnd, Native.GWLP_HWNDPARENT, progman);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_WINDOWPOSCHANGING:
                // 不许被提到前面
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((pos.flags & Native.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
            case Native.WM_MOUSEACTIVATE:
                // 点击不抢焦点（不会让正在打字的窗口失去焦点）
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    // ---------- 拖动 / 调整大小 ----------

    private bool _moved;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _moved = false;
        var onQuote = e.OriginalSource is DependencyObject d && (d == _quote || (d is Visual v && _quote.IsAncestorOf(v)));
        if (!SettingsStore.Current.WidgetLocked)
        {
            var before = new Point(Left, Top);
            try { DragMove(); } catch { return; }
            _moved = Math.Abs(Left - before.X) > 1 || Math.Abs(Top - before.Y) > 1;
            if (_moved)
            {
                EnsureOnScreen();
                SaveBounds();
                return;
            }
        }
        // 没拖动，只是点了一下名言：换一句
        if (onQuote && _quote.Visibility == Visibility.Visible) App.Fire(Quotes.NextAsync);
    }

    private void BuildHandles()
    {
        const double t = 8; // 边缘手柄粗细
        void Add(HorizontalAlignment ha, VerticalAlignment va, double w, double h, int hit, Cursor cursor)
        {
            var r = new Rectangle
            {
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Cursor = cursor,
            };
            if (!double.IsNaN(w)) r.Width = w;
            if (!double.IsNaN(h)) r.Height = h;
            r.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (SettingsStore.Current.WidgetLocked || _hwnd == IntPtr.Zero) return;
                // 交给系统做调整大小（流畅，并且自动遵守最小 / 最大尺寸）
                Native.ReleaseCapture();
                Native.SendMessage(_hwnd, Native.WM_NCLBUTTONDOWN, new IntPtr(hit), IntPtr.Zero);
                EnsureOnScreen();
                SaveBounds();
            };
            _handles.Children.Add(r);
        }
        const double n = double.NaN;
        var S = HorizontalAlignment.Stretch;
        var VS = VerticalAlignment.Stretch;
        Add(HorizontalAlignment.Left, VS, t, n, Native.HTLEFT, Cursors.SizeWE);
        Add(HorizontalAlignment.Right, VS, t, n, Native.HTRIGHT, Cursors.SizeWE);
        Add(S, VerticalAlignment.Top, n, t, Native.HTTOP, Cursors.SizeNS);
        Add(S, VerticalAlignment.Bottom, n, t, Native.HTBOTTOM, Cursors.SizeNS);
        Add(HorizontalAlignment.Left, VerticalAlignment.Top, 16, 16, Native.HTTOPLEFT, Cursors.SizeNWSE);
        Add(HorizontalAlignment.Right, VerticalAlignment.Bottom, 16, 16, Native.HTBOTTOMRIGHT, Cursors.SizeNWSE);
        Add(HorizontalAlignment.Right, VerticalAlignment.Top, 16, 16, Native.HTTOPRIGHT, Cursors.SizeNESW);
        Add(HorizontalAlignment.Left, VerticalAlignment.Bottom, 16, 16, Native.HTBOTTOMLEFT, Cursors.SizeNESW);

        // 右下角的小三角提示可以拖
        var grip = new Path
        {
            Data = Geometry.Parse("M14,2 L14,14 L2,14 Z"),
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        grip.SetResourceReference(Shape.FillProperty, "AccentBrush");
        grip.Opacity = 0.8;
        _handles.Children.Add(grip);
    }

    /// <summary>宽度最多一屏，高度最多屏幕的 60%</summary>
    private void ApplyMaxSize()
    {
        var wa = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinW, wa.Width);
        MaxHeight = Math.Max(MinH, Math.Min(wa.Height * 0.6, 720));
        if (Width > MaxWidth) Width = MaxWidth;
        if (Height > MaxHeight) Height = MaxHeight;
    }

    /// <summary>确保至少有一部分在某个屏幕里（拔掉副屏后不会“消失”）</summary>
    public void EnsureOnScreen()
    {
        ApplyMaxSize();
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        var visible = WinForms.Screen.AllScreens.Any(sc =>
        {
            var b = sc.WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var l = b.Left / dpi.DpiScaleX; var t = b.Top / dpi.DpiScaleY;
            var r = b.Right / dpi.DpiScaleX; var bt = b.Bottom / dpi.DpiScaleY;
            return Left + Width - 60 > l && Left + 60 < r && Top + 40 > t && Top < bt - 40;
        });
        if (!visible)
        {
            var wa = SystemParameters.WorkArea;
            Left = Math.Clamp(wa.Right - Width - 40, vl, Math.Max(vl, vr - Width));
            Top = Math.Clamp(wa.Top + 60, vt, Math.Max(vt, vb - Height));
            SaveBounds();
        }
    }

    private void SaveBounds()
    {
        var s = SettingsStore.Current;
        s.WidgetX = Left;
        s.WidgetY = Top;
        s.WidgetWidth = ActualWidth > 0 ? ActualWidth : Width;
        s.WidgetHeight = ActualHeight > 0 ? ActualHeight : Height;
        SettingsStore.Save();
    }

    public void ResetBounds()
    {
        var wa = SystemParameters.WorkArea;
        Width = 420;
        Height = 300;
        Left = wa.Right - Width - 40;
        Top = wa.Top + 60;
        SaveBounds();
    }

    private void ShowChrome(bool on)
    {
        var show = on && !SettingsStore.Current.WidgetLocked;
        _frame.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        _hover.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        _handles.Visibility = show ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>内容按高度缩放：设计宽度 = 300 × 宽高比</summary>
    private void UpdateScale()
    {
        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (h <= 0) return;
        _design.Width = DesignHeight * w / h;
    }

    // ---------- 内容 ----------

    private void TickClock()
    {
        var now = DateTime.Now;
        var (hm, ampm) = Roc.HourMinute(now, ClockFace.Is24Hour());
        _time.Text = hm;
        _ampm.Text = ampm ?? "";
        _ampm.Visibility = ampm == null ? Visibility.Collapsed : Visibility.Visible;
        // 对齐到下一分钟整点再跳
        _clock.Interval = TimeSpan.FromMilliseconds(Math.Max(200, 60_000 - now.Second * 1000 - now.Millisecond + 30));
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
        _frame.Stroke = dark ? new SolidColorBrush(Color.FromArgb(0x90, 0x1F, 0x1A, 0x14)) : new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
        _hover.Background = dark ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));

        var font = AppFonts.Of(s.FontId);
        foreach (var t in new[] { _time, _ampm, _date, _lunar, _weather, _quote })
            t.FontFamily = font;
        _time.Foreground = _text;
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
    }

    private void FillMenu(ContextMenu m)
    {
        var s = SettingsStore.Current;
        m.Items.Clear();
        MenuItem Item(string text, Action a, bool check = false)
        {
            var i = new MenuItem { Header = text, IsCheckable = check, IsChecked = check && s.WidgetLocked };
            i.Click += (_, _) => a();
            m.Items.Add(i);
            return i;
        }
        Item(L.Tr("锁定位置和大小", "鎖定位置和大小"), () =>
        {
            s.WidgetLocked = !s.WidgetLocked;
            SettingsStore.Save();
            ShowChrome(false);
        }, true);
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
        Item(L.Tr("隐藏小组件", "隱藏小工具"), () => App.Instance.ShowWidget(false));
    }
}
