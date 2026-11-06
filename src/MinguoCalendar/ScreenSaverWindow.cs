using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Minguo.Core;
using WinForms = System.Windows.Forms;

namespace MinguoCalendar;

/// <summary>屏幕保护程序：每个显示器一个全屏窗口，动鼠标、按键即退出</summary>
public sealed class ScreenSaverWindow : Window
{
    private readonly ClockFace _face = new(ClockFace.Kind.Screensaver);
    private readonly WinForms.Screen _screen;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Point? _firstMouse;
    private static bool _closing;

    private ScreenSaverWindow(WinForms.Screen screen)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.Bounds.X;
        Top = screen.Bounds.Y;
        Width = 800;
        Height = 600;
        Content = _face;
        _timer.Tick += (_, _) => _face.Refresh(DateTime.Now);
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) =>
        {
            // 用物理像素铺满对应显示器（多显示器、不同缩放比例都准确）
            var h = new WindowInteropHelper(this).Handle;
            var b = _screen.Bounds;
            Native.SetWindowPos(h, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, Native.SWP_SHOWWINDOW);
        };
        PreviewMouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (_firstMouse == null) { _firstMouse = p; return; }
            if (Math.Abs(p.X - _firstMouse.Value.X) > 8 || Math.Abs(p.Y - _firstMouse.Value.Y) > 8) Exit();
        };
        PreviewMouseDown += (_, _) => Exit();
        PreviewMouseWheel += (_, _) => Exit();
        PreviewKeyDown += (_, _) => Exit();
        Deactivated += (_, _) =>
        {
            // 只有主显示器窗口失去焦点（比如按了 Win 键）才退出
            if (_screen.Primary) Exit();
        };
    }

    private static void Exit()
    {
        if (_closing) return;
        _closing = true;
        Application.Current.Shutdown();
    }

    /// <summary>/s：全屏显示</summary>
    public static void ShowAll()
    {
        var windows = new System.Collections.Generic.List<ScreenSaverWindow>();
        foreach (var sc in WinForms.Screen.AllScreens)
        {
            var w = new ScreenSaverWindow(sc);
            windows.Add(w);
            w.Show();
        }
        foreach (var w in windows)
            if (w._screen.Primary) w.Activate();
        // 屏保可能一开几个小时：每 3 秒看一眼主程序有没有完成新的一批更新（天气、名言、壁纸）。
        // 只有整批提交完成（SyncStamp 变了）才切换，所有显示器在同一瞬间一起换，不会半新半旧。
        static string BgKey()
        {
            var st = SettingsStore.Current;
            return $"{st.WallpaperMode}|{st.CustomWallpaper}|{Bing.CurrentFile}|{st.WallpaperDim}|{st.ThemeIndex}";
        }
        var lastBg = BgKey();
        var reload = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        reload.Tick += (_, _) =>
        {
            if (!SettingsStore.ReloadIfSyncChanged()) return;
            var bg = BgKey();
            var bgChanged = bg != lastBg;
            lastBg = bg;
            var now = DateTime.Now;
            foreach (var w in windows)
            {
                if (bgChanged) w._face.LoadBackground(); // 换图（里面也会刷新文字）
                else w._face.Refresh(now);              // 只刷新天气、名言
            }
        };
        reload.Start();

        // 屏保开始后的一点抖动不算“动了鼠标”
        var grace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        grace.Tick += (_, _) =>
        {
            grace.Stop();
            foreach (var w in windows) w._firstMouse = null;
        };
        grace.Start();
    }

    /// <summary>/p：在“屏幕保护程序设置”对话框的小屏幕里预览</summary>
    public static bool ShowPreview(IntPtr parent)
    {
        if (parent == IntPtr.Zero || !Native.IsWindow(parent)) return false;
        Native.GetClientRect(parent, out var r);
        var p = new HwndSourceParameters("MinguoPreview")
        {
            ParentWindow = parent,
            WindowStyle = unchecked((int)(0x40000000 | 0x10000000 | 0x02000000)), // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN
            PositionX = 0,
            PositionY = 0,
            Width = Math.Max(1, r.Right - r.Left),
            Height = Math.Max(1, r.Bottom - r.Top),
        };
        var src = new HwndSource(p);
        var face = new ClockFace(ClockFace.Kind.Preview);
        src.RootVisual = face;
        // 尺寸按物理像素给的，换成 WPF 单位
        var scale = src.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        face.Width = p.Width * scale.M11;
        face.Height = p.Height * scale.M22;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (!Native.IsWindow(parent))
            {
                timer.Stop();
                src.Dispose();
                Application.Current.Shutdown();
                return;
            }
            face.Refresh(DateTime.Now);
        };
        timer.Start();
        src.Disposed += (_, _) => Application.Current?.Shutdown();
        return true;
    }
}
