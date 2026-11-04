using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Minguo.Core;
using Windows.Devices.Geolocation;

namespace MinguoCalendar;

/// <summary>
/// Windows 定位服务（Wi‑Fi / GPS / 基站，系统自己选最准的）。
/// 需要在 Windows 设置 → 隐私和安全性 → 位置 里打开“位置服务”和“允许桌面应用访问你的位置”。
/// 拿不到时由 WeatherService 用网络 IP 兜底。
/// </summary>
internal static class Location
{
    public static async Task<(double Lat, double Lon, string Source, double Accuracy)?> GetAsync(CancellationToken ct)
    {
        try
        {
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access == GeolocationAccessStatus.Denied) return null;
            }
            catch
            {
                // 部分系统版本桌面程序调用会报错，直接尝试定位
            }
            var g = new Geolocator { DesiredAccuracy = PositionAccuracy.High };
            var pos = await g.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15)).AsTask(ct);
            var c = pos.Coordinate;
            var src = c.PositionSource switch
            {
                PositionSource.Satellite => "gps",
                PositionSource.WiFi => "wifi",
                PositionSource.Cellular => "cell",
                PositionSource.IPAddress => "ip",
                _ => "system",
            };
            return (c.Point.Position.Latitude, c.Point.Position.Longitude, src, c.Accuracy);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>系统定位是否被关掉（用于设置页提示）</summary>
    public static async Task<bool> IsDeniedAsync()
    {
        try
        {
            return await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Denied;
        }
        catch
        {
            return false;
        }
    }

    public static string SourceLabel(string src) => src switch
    {
        "gps" => L.Tr("卫星定位", "衛星定位"),
        "wifi" => L.Tr("Wi‑Fi 定位", "Wi‑Fi 定位"),
        "cell" => L.Tr("基站定位", "基地台定位"),
        "system" => L.Tr("系统定位", "系統定位"),
        "ip" => L.Tr("网络大致位置", "網路大致位置"),
        "manual" => L.Tr("手动选择", "手動選擇"),
        _ => "",
    };
}

/// <summary>开机自启：写 HKCU\…\Run（不需要管理员权限）</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MinguoCalendar";

    public static void Sync()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            var exe = Environment.ProcessPath;
            if (SettingsStore.Current.StartWithWindows && exe != null)
                k.SetValue(Name, $"\"{exe}\" --tray");
            else if (k.GetValue(Name) != null)
                k.DeleteValue(Name, false);
        }
        catch
        {
            // 被安全软件拦了就算了
        }
    }
}

/// <summary>
/// 屏幕保护程序：Windows 的屏保就是改名为 .scr 的 exe。
/// 把自己复制成 %LOCALAPPDATA%\MinguoCalendar\MinguoCalendar.scr，再写入当前用户的屏保设置。
/// </summary>
internal static class ScreenSaver
{
    private const string DesktopKey = @"Control Panel\Desktop";
    public static readonly int[] Timeouts = [1, 3, 5, 10, 15, 30, 60];

    public static string ScrPath => Path.Combine(SettingsStore.CacheDir, "MinguoCalendar.scr");

    private static string MainExeRecord => Path.Combine(SettingsStore.CacheDir, "main-exe.txt");

    /// <summary>屏保进程找主程序：安装/运行时记录下来的 exe 路径</summary>
    public static string? MainExePath()
    {
        try
        {
            var p = File.ReadAllText(MainExeRecord).Trim();
            return File.Exists(p) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前系统屏保是不是我们的</summary>
    public static bool IsActive()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(DesktopKey);
            var v = k?.GetValue("SCRNSAVE.EXE") as string;
            if (string.IsNullOrEmpty(v)) return false;
            return string.Equals(LongPath(v), ScrPath, StringComparison.OrdinalIgnoreCase)
                || v.EndsWith("MinguoCalendar.scr", StringComparison.OrdinalIgnoreCase)
                || v.EndsWith("MINGUO~1.SCR", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string LongPath(string p)
    {
        try { return Path.GetFullPath(new FileInfo(p).FullName); }
        catch { return p; }
    }

    private static bool CopySelf()
    {
        var exe = Environment.ProcessPath;
        if (exe == null || string.Equals(exe, ScrPath, StringComparison.OrdinalIgnoreCase)) return File.Exists(ScrPath);
        try
        {
            File.WriteAllText(MainExeRecord, exe);
            var src = new FileInfo(exe);
            var dst = new FileInfo(ScrPath);
            if (!dst.Exists || dst.Length != src.Length || dst.LastWriteTimeUtc < src.LastWriteTimeUtc)
                File.Copy(exe, ScrPath, true);
            return true;
        }
        catch
        {
            return File.Exists(ScrPath); // 屏保正在运行时会被占用，用旧的
        }
    }

    /// <summary>启动时：已启用屏保就把新版本 exe 同步过去</summary>
    public static void SyncCopy()
    {
        var s = SettingsStore.Current;
        if (!s.ScreensaverInstalled) return;
        if (!IsActive())
        {
            // 用户在系统里换成别的屏保了
            s.ScreensaverInstalled = false;
            SettingsStore.Save();
            return;
        }
        CopySelf();
    }

    public static string? Install(int minutes)
    {
        if (!CopySelf()) return L.Tr("复制屏保文件失败", "複製螢幕保護程式檔案失敗");
        try
        {
            var sb = new StringBuilder(260);
            var path = Native.GetShortPathName(ScrPath, sb, (uint)sb.Capacity) > 0 ? sb.ToString() : ScrPath;
            using (var k = Registry.CurrentUser.CreateSubKey(DesktopKey, true))
            {
                k.SetValue("SCRNSAVE.EXE", path);
                k.SetValue("ScreenSaveActive", "1");
                k.SetValue("ScreenSaveTimeOut", (minutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            Native.SystemParametersInfo(Native.SPI_SETSCREENSAVETIMEOUT, (uint)(minutes * 60), IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, 1, IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            var s = SettingsStore.Current;
            s.ScreensaverInstalled = true;
            s.ScreensaverTimeoutMinutes = minutes;
            SettingsStore.Save();
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static void Uninstall()
    {
        try
        {
            if (IsActive())
            {
                using var k = Registry.CurrentUser.CreateSubKey(DesktopKey, true);
                k.DeleteValue("SCRNSAVE.EXE", false);
                k.SetValue("ScreenSaveActive", "0");
                Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, 0, IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            }
        }
        catch
        {
            // 忽略
        }
        try { File.Delete(ScrPath); } catch { /* 正在运行就留着 */ }
        var s = SettingsStore.Current;
        s.ScreensaverInstalled = false;
        SettingsStore.Save();
    }

    /// <summary>立即全屏预览</summary>
    public static void Preview()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        try { Process.Start(new ProcessStartInfo(exe, "/s") { UseShellExecute = false }); }
        catch { /* 忽略 */ }
    }

    public static void OpenSystemSettings()
    {
        try { Process.Start(new ProcessStartInfo("control.exe", "desk.cpl,,@screensaver") { UseShellExecute = true }); }
        catch { /* 忽略 */ }
    }
}

/// <summary>
/// 定时任务（界面线程，每分钟一次）：
/// 天气 30 分钟、名言 30 分钟、必应壁纸 30 分钟、锁屏背景 30 分钟（以及换日、解锁、睡眠唤醒时立即检查），自动主题。
/// </summary>
internal static class Scheduler
{
    private static DispatcherTimer? _timer;
    private static DateTime _lastDay = DateTime.Today;
    private static bool _busy;

    public static event Action? Minute;

    public static void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Tick(false);
        _timer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplay;
        Tick(true);
    }

    public static void Stop()
    {
        _timer?.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplay;
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            App.Current.Dispatcher.BeginInvoke(() => Tick(true));
    }

    private static void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Region.Invalidate(); // 换了网络（比如出差）重新判断
        App.Current.Dispatcher.BeginInvoke(() => Tick(true));
    }

    private static void OnTimeChanged(object? sender, EventArgs e) => App.Current.Dispatcher.BeginInvoke(() => Tick(true));

    private static void OnDisplay(object? sender, EventArgs e) => App.Current.Dispatcher.BeginInvoke(() => App.Instance.Widget?.EnsureOnScreen());

    /// <summary>force：解锁、唤醒、启动时，到期的立刻做</summary>
    public static void Tick(bool force)
    {
        var s = SettingsStore.Current;
        if (s.ThemeMode == Theme.Auto) Theme.Apply();
        var dayChanged = DateTime.Today != _lastDay;
        _lastDay = DateTime.Today;
        Minute?.Invoke();
        if (_busy) return;
        _busy = true;
        App.Fire(async () =>
        {
            try
            {
                var tasks = new System.Collections.Generic.List<Task>
                {
                    Quotes.MaybeRotateAsync(),
                };
                if (s.WallpaperMode == 2) tasks.Add(Bing.RefreshAsync());
                if (s.WeatherEnabled) tasks.Add(WeatherService.RefreshIfStaleAsync());
                foreach (var t in tasks)
                {
                    try { await t.ConfigureAwait(false); }
                    catch (Exception e) { App.Log(e); }
                }
                if (s.LockImageEnabled && (dayChanged || force || DateTime.Now - s.LockImageSetAt >= TimeSpan.FromMinutes(30)))
                {
                    var due = dayChanged || DateTime.Now - s.LockImageSetAt >= TimeSpan.FromMinutes(30);
                    if (due) await LockImage.UpdateAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _busy = false;
            }
        });
    }
}
