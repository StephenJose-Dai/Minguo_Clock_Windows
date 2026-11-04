using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Minguo.Core;
using WinForms = System.Windows.Forms;

namespace MinguoCalendar;

/// <summary>
/// 程序入口。同一个 exe 有三种身份：
/// - 普通启动 / 开机自启（--tray）：托盘 + 桌面小组件 + 定时更新，只允许一个实例；
/// - 屏幕保护程序：Windows 以 .scr 启动，参数 /s 全屏显示、/p 小预览、/c 打开设置；
/// - 再次双击：通知已运行的实例打开设置窗口。
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\MinguoCalendar.Single";
    private const string ShowEventName = @"Local\MinguoCalendar.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Tray? _tray;
    private DispatcherTimer? _debounce;

    /// <summary>界面需要刷新（天气、壁纸、名言、设置、语言变化后，在界面线程上触发，已合并抖动）</summary>
    public static event Action? Refreshed;

    public static App Instance => (App)Current;

    public WidgetWindow? Widget { get; private set; }

    public static bool IsScreenSaverProcess { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log(ex.Exception);
            ex.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log(ex.Exception);
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log(ex.ExceptionObject as Exception);

        L.ToHantConverter = Native.ToTraditional;
        L.Refresh(SettingsStore.Current.Language);
        Bing.ScreenSize = PrimaryPixels;
        Theme.Apply();

        var (mode, hwnd) = ParseArgs(e.Args);
        switch (mode)
        {
            case "s":
                IsScreenSaverProcess = true;
                ScreenSaverWindow.ShowAll();
                return;
            case "p":
                IsScreenSaverProcess = true;
                if (!ScreenSaverWindow.ShowPreview(hwnd)) Shutdown();
                return;
            case "c":
                // 屏保的“设置”按钮：打开（或唤起）主程序的屏保设置页
                IsScreenSaverProcess = true;
                OpenMainApp("--page=screensaver");
                Shutdown();
                return;
        }

        // ---------- 普通模式：单实例 ----------
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try
            {
                if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
                    EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // 对方还没准备好就算了
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (Dispatcher.HasShutdownStarted) break;
                Dispatcher.BeginInvoke(() => ShowSettings());
            }
        }) { IsBackground = true, Name = "show-settings" };
        listener.Start();

        WinForms.Application.EnableVisualStyles();
        WeatherService.DeviceLocator = Location.GetAsync;

        // 把后台线程的变化合并成一次界面刷新
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refreshed?.Invoke();
        };
        WeatherService.Updated += RequestRefresh;
        Bing.Changed += RequestRefresh;
        Quotes.Changed += RequestRefresh;
        L.Changed += RequestRefresh;
        SettingsStore.Saved += RequestRefresh;

        _tray = new Tray();
        if (SettingsStore.Current.WidgetEnabled) ShowWidget(true);
        AutoStart.Sync();
        ScreenSaver.SyncCopy();
        Scheduler.Start();

        var page = e.Args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?[7..];
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) ShowSettings(page);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Scheduler.Stop();
            _tray?.Dispose();
            _showEvent?.Dispose();
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
        catch
        {
            // 退出时出错不影响
        }
        base.OnExit(e);
    }

    private void RequestRefresh()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_debounce == null) return;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    /// <summary>屏保参数：/s、/p 1234、/p:1234、/c、/c:1234；以 .scr 身份双击且无参数时 = /c</summary>
    private static (string Mode, IntPtr Hwnd) ParseArgs(string[] args)
    {
        var isScr = string.Equals(Path.GetExtension(Environment.ProcessPath ?? ""), ".scr", StringComparison.OrdinalIgnoreCase);
        if (args.Length == 0) return (isScr ? "c" : "", IntPtr.Zero);
        var a = args[0].Trim();
        if (a.Length < 2 || (a[0] != '/' && a[0] != '-')) return (isScr ? "c" : "", IntPtr.Zero);
        var mode = char.ToLowerInvariant(a[1]).ToString();
        if (mode is not ("s" or "p" or "c")) return ("", IntPtr.Zero);
        string? num = a.Length > 3 && a[2] == ':' ? a[3..] : args.Length > 1 ? args[1] : null;
        var hwnd = IntPtr.Zero;
        if (num != null && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) hwnd = new IntPtr(n);
        return (mode, hwnd);
    }

    /// <summary>从屏保进程打开主程序（已安装时找安装目录的 exe；找不到就用自己）</summary>
    private static void OpenMainApp(string args)
    {
        var exe = ScreenSaver.MainExePath() ?? Environment.ProcessPath;
        if (exe == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false });
        }
        catch
        {
            // 打不开就算了
        }
    }

    /// <summary>主屏幕物理像素（选必应壁纸清晰度、生成锁屏图用）</summary>
    public static (int W, int H) PrimaryPixels()
    {
        var b = WinForms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var max = WinForms.Screen.AllScreens.Select(s => s.Bounds).DefaultIfEmpty(b).MaxBy(r => r.Width * r.Height);
        return (Math.Max(b.Width, max.Width), Math.Max(b.Height, max.Height));
    }

    // ---------- 窗口 ----------

    public void ShowSettings(string? page = null)
    {
        var w = Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (w == null)
        {
            w = new SettingsWindow();
            w.Show();
        }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        if (!string.IsNullOrEmpty(page)) w.Navigate(page);
        w.Activate();
        w.Topmost = true;  // 从托盘打开时确保到最前
        w.Topmost = false;
        w.Focus();
    }

    public void ShowWidget(bool show)
    {
        var s = SettingsStore.Current;
        if (s.WidgetEnabled != show)
        {
            s.WidgetEnabled = show;
            SettingsStore.Save();
        }
        if (show)
        {
            if (Widget == null)
            {
                Widget = new WidgetWindow();
                Widget.Closed += (_, _) => Widget = null;
                Widget.Show();
            }
        }
        else
        {
            Widget?.Close();
            Widget = null;
        }
        _tray?.UpdateMenu();
    }

    public void Quit()
    {
        foreach (Window w in Windows.Cast<Window>().ToList())
        {
            try { w.Close(); } catch { /* 忽略 */ }
        }
        Shutdown();
    }

    /// <summary>后台任务：不等结果，出错记日志</summary>
    public static void Fire(Func<Task> work)
    {
        Task.Run(async () =>
        {
            try { await work().ConfigureAwait(false); }
            catch (Exception ex) { Log(ex); }
        });
    }

    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var f = Path.Combine(SettingsStore.CacheDir, "error.log");
            if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
            File.AppendAllText(f, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch
        {
            // 日志写不了就算了
        }
    }
}
