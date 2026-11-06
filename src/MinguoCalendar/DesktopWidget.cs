using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Minguo.Core;
using WinForms = System.Windows.Forms;

namespace MinguoCalendar;

/// <summary>
/// 桌面小组件的公共底座：完全透明、无边框，固定在桌面层（不盖住其他窗口，“显示桌面”也不收起）；
/// 鼠标移上去出现虚线框和调整手柄，可拖动、拖边角改大小；右键菜单。
/// 时钟组件和系统资源组件都从它派生，各自保存自己的位置、大小、锁定、透明度。
/// </summary>
public abstract class DesktopWidget : Window
{
    private readonly Grid _design = new();
    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform };
    private readonly Border _contentHost = new();
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
    private FrameworkElement? _contentRoot;
    private bool _moved;

    protected IntPtr Hwnd { get; private set; }

    // 子类用自己的设置字段实现下面这些
    protected abstract double CfgX { get; set; }
    protected abstract double CfgY { get; set; }
    protected abstract double CfgW { get; set; }
    protected abstract double CfgH { get; set; }
    protected abstract bool Locked { get; set; }
    protected abstract (double W, double H) DefaultSize { get; }
    protected virtual double MinW => 180;
    protected virtual double MinH => 90;
    protected virtual double MaxHeightFraction => 0.7;
    /// <summary>内容透明度（水印效果用，1 = 不透明）</summary>
    protected virtual double ContentOpacity => 1;

    /// <summary>子类构建内容（会被放进 Viewbox 等比缩放），返回用来测量的根元素</summary>
    protected abstract FrameworkElement BuildContent();
    /// <summary>没有拖动、只是点了一下时调用；返回 true 表示已处理</summary>
    protected virtual bool OnPlainClick(MouseButtonEventArgs e) => false;
    /// <summary>右键菜单（每次打开重建）</summary>
    protected abstract void FillMenu(ContextMenu menu);

    /// <summary>子类字段都准备好后，在构造函数末尾调用</summary>
    protected void Init(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // 1/255：看不见但点得到
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        UseLayoutRounding = true;

        var def = DefaultSize;
        Width = Math.Max(MinW, double.IsNaN(CfgW) || CfgW <= 0 ? def.W : CfgW);
        Height = Math.Max(MinH, double.IsNaN(CfgH) || CfgH <= 0 ? def.H : CfgH);
        var wa = SystemParameters.WorkArea;
        Left = double.IsNaN(CfgX) ? wa.Right - Width - 40 : CfgX;
        Top = double.IsNaN(CfgY) ? wa.Top + 60 : CfgY;
        ApplyMaxSize();

        _contentRoot = BuildContent();
        _contentHost.Child = _design;
        _design.Children.Add(_contentRoot);
        _viewbox.Child = _contentHost;

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
        SizeChanged += (_, _) => LayoutContent();
        StateChanged += (_, _) => { if (WindowState != WindowState.Normal) WindowState = WindowState.Normal; };
        SourceInitialized += OnSourceInitialized;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, e) =>
        {
            // 锁定后右键什么都不弹，所有选项都在设置里
            if (Locked) { e.Handled = true; return; }
            ContextMenu.Items.Clear();
            FillMenu(ContextMenu);
        };
        Loaded += (_, _) => { ApplyContentOpacity(); LayoutContent(); EnsureOnScreen(); };
    }

    protected void ApplyContentOpacity() => _contentHost.Opacity = Math.Clamp(ContentOpacity, 0.05, 1);

    // ---------- 固定在桌面 ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLong(Hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex = (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLong(Hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        var progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero) Native.SetWindowLong(Hwnd, Native.GWLP_HWNDPARENT, progman);
        HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        Native.SetWindowPos(Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_WINDOWPOSCHANGING:
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((pos.flags & Native.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    // ---------- 拖动 / 调整大小 ----------

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _moved = false;
        if (!Locked)
        {
            var before = new Point(Left, Top);
            try { DragMove(); } catch { return; }
            _moved = Math.Abs(Left - before.X) > 1 || Math.Abs(Top - before.Y) > 1;
            if (_moved) { EnsureOnScreen(); SaveBounds(); return; }
        }
        OnPlainClick(e);
    }

    private void BuildHandles()
    {
        const double t = 8;
        void Add(HorizontalAlignment ha, VerticalAlignment va, double w, double h, int hit, Cursor cursor)
        {
            var r = new Rectangle { HorizontalAlignment = ha, VerticalAlignment = va, Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = cursor };
            if (!double.IsNaN(w)) r.Width = w;
            if (!double.IsNaN(h)) r.Height = h;
            r.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (Locked || Hwnd == IntPtr.Zero) return;
                Native.ReleaseCapture();
                Native.SendMessage(Hwnd, Native.WM_NCLBUTTONDOWN, new IntPtr(hit), IntPtr.Zero);
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

        var grip = new Path
        {
            Data = Geometry.Parse("M14,2 L14,14 L2,14 Z"),
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Opacity = 0.8,
        };
        grip.SetResourceReference(Shape.FillProperty, "AccentBrush");
        _handles.Children.Add(grip);
    }

    private void ApplyMaxSize()
    {
        var wa = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinW, wa.Width);
        MaxHeight = Math.Max(MinH, Math.Min(wa.Height * MaxHeightFraction, 900));
        if (Width > MaxWidth) Width = MaxWidth;
        if (Height > MaxHeight) Height = MaxHeight;
    }

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
            var l = b.Left / dpi.DpiScaleX; var tp = b.Top / dpi.DpiScaleY;
            var r = b.Right / dpi.DpiScaleX; var bt = b.Bottom / dpi.DpiScaleY;
            return Left + Width - 60 > l && Left + 60 < r && Top + 40 > tp && Top < bt - 40;
        });
        if (!visible)
        {
            var wa = SystemParameters.WorkArea;
            Left = Math.Clamp(wa.Right - Width - 40, vl, Math.Max(vl, vr - Width));
            Top = Math.Clamp(wa.Top + 60, vt, Math.Max(vt, vb - Height));
            SaveBounds();
        }
    }

    protected void SaveBounds()
    {
        CfgX = Left;
        CfgY = Top;
        CfgW = ActualWidth > 0 ? ActualWidth : Width;
        CfgH = ActualHeight > 0 ? ActualHeight : Height;
        SettingsStore.Save();
    }

    public void ResetBounds()
    {
        var wa = SystemParameters.WorkArea;
        (Width, Height) = DefaultSize;
        Left = wa.Right - Width - 40;
        Top = wa.Top + 60;
        SaveBounds();
    }

    protected void ShowChrome(bool on)
    {
        var show = on && !Locked;
        _frame.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        _hover.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        _handles.Visibility = show ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>设计画布尺寸 = 内容实际大小，Viewbox 再等比缩放铺满窗口</summary>
    protected void LayoutContent()
    {
        if (_contentRoot == null) return;
        _contentRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = _contentRoot.DesiredSize;
        _design.Width = Math.Max(1, sz.Width);
        _design.Height = Math.Max(1, sz.Height);
    }

    protected void SetChromeColors(bool darkText)
    {
        _frame.Stroke = darkText ? new SolidColorBrush(Color.FromArgb(0x90, 0x1F, 0x1A, 0x14)) : new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
        _hover.Background = darkText ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));
    }
}
