using System.Text.RegularExpressions;

namespace Minguo.Core;

/// <summary>
/// 判断网络在中国大陆还是境外，用来选择必应、一言的节点：
/// 出口 IP（Cloudflare → myip.ipip.net）→ 定位国家 → 系统地区。结果缓存 6 小时。
/// </summary>
public static partial class Region
{
    private static bool? _mainland;
    private static DateTime _checkedAt;
    private static readonly TimeSpan Cache = TimeSpan.FromHours(6);

    public static void Invalidate() => _checkedAt = DateTime.MinValue;

    public static async Task<bool> IsMainlandAsync()
    {
        if (_mainland is { } m && DateTime.Now - _checkedAt < Cache) return m;
        var result = await ByCloudflare().ConfigureAwait(false)
                     ?? await ByIpip().ConfigureAwait(false)
                     ?? ByLocation()
                     ?? Guess();
        _mainland = result;
        _checkedAt = DateTime.Now;
        return result;
    }

    [GeneratedRegex("(?m)^loc=([A-Z]{2})")]
    private static partial Regex LocRegex();

    private static async Task<bool?> ByCloudflare()
    {
        foreach (var u in new[] { "https://www.cloudflare.com/cdn-cgi/trace", "https://1.1.1.1/cdn-cgi/trace" })
        {
            try
            {
                var t = await Net.GetStringAsync(u, timeoutMs: 3_000).ConfigureAwait(false);
                var m = LocRegex().Match(t);
                if (m.Success) return m.Groups[1].Value == "CN";
            }
            catch
            {
                // 换下一个
            }
        }
        return null;
    }

    /// <summary>myip.ipip.net 返回如：当前 IP：1.2.3.4  来自于：中国 福建 福州  电信</summary>
    private static async Task<bool?> ByIpip()
    {
        try
        {
            var t = await Net.GetStringAsync("https://myip.ipip.net", timeoutMs: 3_000).ConfigureAwait(false);
            var i = t.IndexOf("来自于：", StringComparison.Ordinal);
            if (i < 0) return null;
            var from = t[(i + 4)..].Trim();
            if (!from.StartsWith("中国", StringComparison.Ordinal)) return false;
            return !(from.Contains("台湾") || from.Contains("香港") || from.Contains("澳门"));
        }
        catch
        {
            return null;
        }
    }

    private static bool? ByLocation()
    {
        var c = SettingsStore.Current.PlaceCountry;
        return string.IsNullOrEmpty(c) ? null : c == "CN";
    }

    private static bool Guess()
    {
        var tz = TimeZoneInfo.Local.Id;
        return tz is "China Standard Time" or "Asia/Shanghai" or "Asia/Chongqing" or "Asia/Urumqi";
    }

    public static async Task<string> BingHostAsync() =>
        await IsMainlandAsync().ConfigureAwait(false) ? "https://cn.bing.com" : "https://www.bing.com";

    /// <summary>必应地区：大陆 zh-CN；境外繁体界面用 zh-TW</summary>
    public static async Task<string> BingMarketAsync() =>
        await IsMainlandAsync().ConfigureAwait(false) ? "zh-CN" : (L.Current == Lang.Hant ? "zh-TW" : "zh-CN");

    public static async Task<string> HitokotoHostAsync() =>
        await IsMainlandAsync().ConfigureAwait(false) ? "https://v1.hitokoto.cn" : "https://international.v1.hitokoto.cn";
}
