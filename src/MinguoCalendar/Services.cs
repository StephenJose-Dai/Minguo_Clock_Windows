using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
/// 天气（默认 10 分钟）、名言和必应壁纸（默认 30 分钟）同一节拍按整点更新，锁屏图同时重画；换日、解锁、睡眠唤醒时立即检查；自动主题。
/// </summary>
internal static class Scheduler
{
    private static DispatcherTimer? _timer;
    private static DateTime _lastDay = DateTime.Today;
    private static bool _busy;

    public static event Action? Minute;

    private static DispatcherTimer? _lockDebounce;
    private static string _lockKey = "";

    /// <summary>影响锁屏图样子的设置（签名、字体、排版、壁纸…）：变了 1 秒后自动重新生成</summary>
    private static string LockKey()
    {
        var s = SettingsStore.Current;
        var layout = string.Join(";", s.LockLayout.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + string.Join(",", kv.Value)));
        // 天气每次拉取、名言换句、必应壁纸换张都会让 key 变化 → 几秒后重画锁屏图，锁屏上的天气始终是最新的
        var weather = s.WeatherEnabled && s.WeatherCache is { } w ? w.FetchedAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        return string.Join("|", s.Signature, s.FontId, s.Language, s.FullEra, s.ChineseNumerals, s.WallpaperMode, s.CustomWallpaper,
            s.WallpaperDim, s.ThemeIndex, s.LockDateStyle, layout, weather, s.QuoteText, s.BingIndex, s.WeatherEnabled);
    }

    private static void OnSettingsSaved()
    {
        if (_syncing) return; // 整批更新由 SyncAsync 自己统一重画锁屏
        App.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_syncing) return;
            var s = SettingsStore.Current;
            var key = LockKey();
            if (!s.LockImageEnabled || key == _lockKey) return;
            _lockDebounce ??= CreateLockDebounce();
            _lockDebounce.Stop();
            _lockDebounce.Start(); // 打字（签名）时合并成一次，停手 1 秒就重画
        });
    }

    private static DispatcherTimer CreateLockDebounce()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _lockKey = LockKey();
            App.Fire(() => LockImage.UpdateAsync());
        };
        return t;
    }

    public static void Start()
    {
        _lockKey = LockKey();
        SettingsStore.Saved += OnSettingsSaved;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Tick(false);
        _timer.Start();
        // 整点 :00 / :30 准时触发（每分钟的计时器可能晚将近一分钟）
        _boundary = new DispatcherTimer();
        _boundary.Tick += (_, _) => { _boundary?.Stop(); Tick(false); };
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplay;
        Tick(true);
    }

    public static void Stop()
    {
        _timer?.Stop();
        _boundary?.Stop();
        _lockDebounce?.Stop();
        SettingsStore.Saved -= OnSettingsSaved;
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

    private static void OnDisplay(object? sender, EventArgs e) => App.Current.Dispatcher.BeginInvoke(() =>
    {
        App.Instance.Widget?.EnsureOnScreen();
        App.Instance.SysWidget?.EnsureOnScreen();
    });

    // ================= 天气 / 名言 / 壁纸 / 锁屏图 同步更新 =================
    // 两个频率、同一节拍，都按整点对齐：
    //   · 天气：默认每 10 分钟（可选 5 / 10 / 15 / 30）——天气变化快；
    //   · 名言、壁纸：默认每 30 分钟（可选 10 / 15 / 30 / 60）；到点时天气也一起刷新。
    // 每一拍：要换的几样并行“准备”（这时界面还显示旧的）→ 全部好了在界面线程上同一瞬间“提交”
    // → 立刻统一刷新界面并重画锁屏图；屏保也只在整批提交后才切换。
    // 下一张壁纸提前缓存好，到点不用等下载；天气、一言要联网，一般 1~3 秒，最多等 20 秒。

    private static volatile bool _syncing;
    private static DateTime _lastRetry = DateTime.MinValue;

    private static TimeSpan WeatherPeriod => TimeSpan.FromMinutes(Math.Clamp(SettingsStore.Current.WeatherInterval, 5, 60));
    private static TimeSpan RotatePeriod => TimeSpan.FromMinutes(Math.Clamp(SettingsStore.Current.RotateInterval, 5, 120));

    private static long SlotOf(DateTime t, TimeSpan period) => t.Ticks / period.Ticks;

    private static DispatcherTimer? _boundary;

    /// <summary>把整点计时器对准下一个天气 / 名言壁纸时刻（取更近的那个；睡眠唤醒、改时间、改频率后都会重新对准）</summary>
    private static void ArmBoundary()
    {
        if (_boundary == null) return;
        var now = DateTime.Now;
        DateTime NextOf(TimeSpan p) => new((SlotOf(now, p) + 1) * p.Ticks, now.Kind);
        var s = SettingsStore.Current;
        var next = NextOf(RotatePeriod);
        if (s.WeatherEnabled)
        {
            var w = NextOf(WeatherPeriod);
            if (w < next) next = w;
        }
        _boundary.Stop();
        _boundary.Interval = next - now + TimeSpan.FromMilliseconds(300);
        _boundary.Start();
    }

    /// <summary>每分钟一次 + 每个整点时刻；force：启动、解锁、唤醒、改时间时</summary>
    public static void Tick(bool force)
    {
        var s = SettingsStore.Current;
        if (s.ThemeMode == Theme.Auto) Theme.Apply();
        var dayChanged = DateTime.Today != _lastDay;
        _lastDay = DateTime.Today;
        Minute?.Invoke();
        ArmBoundary();
        if (_busy) return;

        var now = DateTime.Now;
        var wPeriod = WeatherPeriod;
        var rSlot = SlotOf(now, RotatePeriod);
        var wSlot = SlotOf(now, wPeriod);
        var rotate = rSlot != s.SyncSlot;                          // 该换名言、壁纸了（或睡眠/关机跨过了整点）
        var weatherDue = s.WeatherEnabled && wSlot != s.WeatherSlot; // 该刷新天气了
        var missing = (s.WeatherEnabled && s.WeatherCache == null) || string.IsNullOrEmpty(s.QuoteText)
            || (s.WallpaperMode == 2 && Bing.CurrentFile == null);
        // 天气上一拍没拿到（断网等）：过 3 分钟再试，拿到就和锁屏图一起更新
        var weatherLate = s.WeatherEnabled && (s.WeatherCache == null || now - s.WeatherCache.FetchedAt > wPeriod + wPeriod)
            && now - _lastRetry > TimeSpan.FromMinutes(3);
        var lockMissing = s.LockImageEnabled && s.LockImageSetAt == default;
        if (!rotate && !weatherDue && !missing && !weatherLate && !lockMissing && !dayChanged) return;
        if (weatherLate) _lastRetry = now;

        _busy = true;
        App.Fire(async () =>
        {
            try { await SyncAsync(rSlot, wSlot, rotate).ConfigureAwait(false); }
            finally { _busy = false; }
        });
    }

    private static async Task Safe(Task t)
    {
        try { await t.ConfigureAwait(false); }
        catch (Exception e) { App.Log(e); }
    }

    /// <summary>整批：并行准备 → 同时提交 → 统一刷新界面 + 重画锁屏图</summary>
    private static async Task SyncAsync(long rSlot, long wSlot, bool rotate)
    {
        var s = SettingsStore.Current;
        _syncing = true;
        App.BeginBatch();
        try
        {
            var weather = s.WeatherEnabled ? WeatherService.PrepareAsync() : Task.FromResult<WeatherData?>(null);
            var quote = rotate || string.IsNullOrEmpty(s.QuoteText) ? Quotes.PickNextAsync() : Task.FromResult<string>("");
            var wall = s.WallpaperMode == 2 && (rotate || Bing.CurrentFile == null) ? Bing.PrepareNextAsync() : Task.FromResult<Bing.Next?>(null);

            var all = Task.WhenAll(Safe(weather), Safe(quote), Safe(wall));
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(false);

            // 同一瞬间提交：在界面线程上一口气写完，期间不会插进任何一次每秒刷新
            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                if (weather.IsCompletedSuccessfully && weather.Result is { } w) WeatherService.Commit(w);
                if (quote.IsCompletedSuccessfully && quote.Result is { Length: > 0 } q) Quotes.Commit(q);
                if (wall.IsCompletedSuccessfully && wall.Result is { } n) Bing.Commit(n);
                s.SyncSlot = rSlot;
                s.WeatherSlot = wSlot;
                s.SyncStamp = DateTime.Now.Ticks;
                SettingsStore.Save();
                _lockKey = LockKey(); // 锁屏图下面统一重画，不再走防抖
            });
            Bing.AfterCommit(); // 清旧图、预下载下一张
        }
        finally
        {
            _syncing = false;
            App.EndBatch(); // 立刻统一刷新一次界面
        }
        if (s.LockImageEnabled) await LockImage.UpdateAsync().ConfigureAwait(false); // 同时重画锁屏图
    }

    /// <summary>
    /// 手动换一句 / 换一张壁纸 / 刷新天气：只换那一样，但界面、锁屏图、屏保同时更新，不等防抖。
    /// </summary>
    public static void Manual(Func<Task> change)
    {
        App.Fire(async () =>
        {
            _syncing = true;
            App.BeginBatch();
            try
            {
                await change().ConfigureAwait(false);
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    var s = SettingsStore.Current;
                    s.SyncStamp = DateTime.Now.Ticks; // 让正在运行的屏保也切换
                    SettingsStore.Save();
                    _lockKey = LockKey();
                });
            }
            finally
            {
                _syncing = false;
                App.EndBatch();
            }
            if (SettingsStore.Current.LockImageEnabled) await LockImage.UpdateAsync().ConfigureAwait(false);
        });
    }
}
