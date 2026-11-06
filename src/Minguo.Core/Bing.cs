using System.Text.Json;
using System.Text.RegularExpressions;

namespace Minguo.Core;

/// <summary>
/// 必应壁纸轮换（屏保、锁屏背景用）：
/// - 在最近十几天的必应壁纸里轮换，每 30 分钟（以及屏保启动时）换一张；
/// - 图片列表每天拉一次；图片下载后缓存在 %LOCALAPPDATA%\MinguoCalendar\bing，换完顺便预下载下一张；
/// - 按屏幕分辨率选清晰度：普通屏用 1920x1080，2K 以上用 UHD。
/// </summary>
public static partial class Bing
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>屏幕最大尺寸（Windows 端注入），用来选清晰度</summary>
    public static Func<(int W, int H)> ScreenSize { get; set; } = () => (1920, 1080);

    public static event Action? Changed;

    private static string CacheDir
    {
        get
        {
            var d = Path.Combine(SettingsStore.CacheDir, "bing");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>当前壁纸文件（没有就 null）</summary>
    public static string? CurrentFile
    {
        get
        {
            var s = SettingsStore.Current;
            if (s.BingIndex < 0 || s.BingIndex >= s.BingList.Count) return null;
            var f = Path.Combine(CacheDir, CacheName(s.BingList[s.BingIndex]));
            return File.Exists(f) ? f : null;
        }
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex Unsafe();

    private static string CacheName(string urlbase)
    {
        var i = urlbase.IndexOf("id=", StringComparison.Ordinal);
        var id = Unsafe().Replace(i >= 0 ? urlbase[(i + 3)..] : urlbase, "_");
        if (id.Length > 90) id = id[..90];
        return (id.Length == 0 ? urlbase.GetHashCode(StringComparison.Ordinal).ToString("x") : id) + ".jpg";
    }

    private static async Task<string[]> HostsAsync()
    {
        var preferred = await Region.BingHostAsync().ConfigureAwait(false);
        return new[] { preferred, "https://cn.bing.com", "https://www.bing.com" }.Distinct().ToArray();
    }

    /// <summary>图片列表（urlbase），每天拉一次；拉不到就用旧列表。只返回，不写入设置（写入在 Commit 里和换图一起做）</summary>
    private static async Task<(List<string> List, string Date)> LoadListAsync()
    {
        var s = SettingsStore.Current;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (s.BingListDate == today && s.BingList.Count > 0) return (s.BingList, s.BingListDate);
        var mkt = await Region.BingMarketAsync().ConfigureAwait(false);
        foreach (var host in await HostsAsync().ConfigureAwait(false))
        {
            var market = host.Contains("cn.bing", StringComparison.Ordinal) ? "zh-CN" : mkt;
            var list = new List<string>();
            foreach (var idx in new[] { 0, 7 }) // 一次最多 8 张：idx=0 最近 8 天，idx=7 更早的 8 天
            {
                try
                {
                    using var doc = JsonDocument.Parse(await Net.GetStringAsync($"{host}/HPImageArchive.aspx?format=js&idx={idx}&n=8&mkt={market}").ConfigureAwait(false));
                    foreach (var img in doc.RootElement.GetProperty("images").EnumerateArray())
                    {
                        var u = img.GetProperty("urlbase").GetString();
                        if (!string.IsNullOrEmpty(u) && !list.Contains(u)) list.Add(u);
                    }
                }
                catch
                {
                    // 试下一个
                }
            }
            if (list.Count > 0) return (list, today);
        }
        return (s.BingList, s.BingListDate);
    }

    /// <summary>取图片文件：有缓存直接用，没有就按屏幕尺寸下载</summary>
    private static async Task<string?> ImageFileAsync(string urlbase)
    {
        var f = Path.Combine(CacheDir, CacheName(urlbase));
        if (File.Exists(f) && new FileInfo(f).Length > 10_000) return f;
        var (w, h) = ScreenSize();
        var suffixes = Math.Max(w, h) > 2000 ? new[] { "_UHD.jpg", "_1920x1080.jpg" } : new[] { "_1920x1080.jpg", "_UHD.jpg" };
        foreach (var host in await HostsAsync().ConfigureAwait(false))
        {
            foreach (var sfx in suffixes)
            {
                try
                {
                    var bytes = await Net.GetBytesAsync(host + urlbase + sfx).ConfigureAwait(false);
                    if (bytes.Length <= 10_000) continue;
                    var tmp = f + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
                    File.Move(tmp, f, overwrite: true);
                    return f;
                }
                catch
                {
                    // 试下一个
                }
            }
        }
        return null;
    }

    /// <summary>准备好的下一张：新列表 + 要显示的序号（图片已下载到缓存）</summary>
    public sealed record Next(List<string> List, string Date, int Index);

    /// <summary>
    /// 准备下一张：拉列表、下载图片，但不改当前显示（设置里的 BingIndex 不动）。
    /// 和天气、名言一起准备好后再由 Commit 同时切换，避免先后跳。
    /// </summary>
    public static async Task<Next?> PrepareNextAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = SettingsStore.Current;
            var (list, date) = await LoadListAsync().ConfigureAwait(false);
            if (list.Count == 0) return null;
            // 从“当前这张”在新列表里的位置往后找（列表每天会变，按图片本身找）
            var cur = s.BingIndex >= 0 && s.BingIndex < s.BingList.Count ? s.BingList[s.BingIndex] : null;
            var pos = cur == null ? -1 : list.IndexOf(cur);
            var start = pos < 0 ? 0 : (pos + 1) % list.Count;
            for (var k = 0; k < list.Count; k++)
            {
                var idx = (start + k) % list.Count;
                if (await ImageFileAsync(list[idx]).ConfigureAwait(false) != null) return new Next(list, date, idx);
            }
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>切换到准备好的那张（只改内存，不保存、不发通知；由调用方统一保存、统一刷新）</summary>
    public static void Commit(Next n)
    {
        var s = SettingsStore.Current;
        s.BingList = n.List;
        s.BingListDate = n.Date;
        s.BingIndex = n.Index;
        s.BingChangedAt = DateTime.Now;
    }

    /// <summary>切换之后：删掉不在列表里的旧图，预下载下一张（下次切换不用等网络）</summary>
    public static void AfterCommit()
    {
        var s = SettingsStore.Current;
        var list = s.BingList;
        if (list.Count == 0) return;
        try
        {
            var keep = list.Select(CacheName).ToHashSet();
            foreach (var f in Directory.GetFiles(CacheDir))
                if (!keep.Contains(Path.GetFileName(f))) { try { File.Delete(f); } catch { /* 正在用就留着 */ } }
        }
        catch
        {
            // 忽略
        }
        if (s.BingIndex >= 0) _ = ImageFileAsync(list[(s.BingIndex + 1) % list.Count]);
    }

    /// <summary>立刻换下一张（单独换壁纸时用）</summary>
    public static async Task<bool> RotateAsync()
    {
        var n = await PrepareNextAsync().ConfigureAwait(false);
        if (n == null) return false;
        Commit(n);
        SettingsStore.Save();
        AfterCommit();
        Changed?.Invoke();
        return true;
    }

    /// <summary>没有图就下一张；满 30 分钟就换；force = 立刻换</summary>
    public static async Task<bool> RefreshAsync(bool force = false)
    {
        var s = SettingsStore.Current;
        var due = force || CurrentFile == null || DateTime.Now - s.BingChangedAt >= Interval;
        return !due || await RotateAsync().ConfigureAwait(false);
    }
}
