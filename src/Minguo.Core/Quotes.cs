using System.Text;
using System.Text.Json;

namespace Minguo.Core;

/// <summary>
/// 名言：内置经典名言（洗牌轮换，全部出现过一遍前不重复）与“一言”在线名言混合；
/// 每 30 分钟换一句（以及息屏、屏保启动时），不显示作者。繁体界面自动转繁体。
/// </summary>
public static class Quotes
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Random Rng = new();

    private static string[]? _builtin;

    /// <summary>内置名言（只取内容，不要出处）</summary>
    public static string[] Builtin
    {
        get
        {
            if (_builtin != null) return _builtin;
            using var s = typeof(Quotes).Assembly.GetManifestResourceStream("quotes.txt")!;
            using var r = new StreamReader(s, Encoding.UTF8);
            var list = new List<string>();
            while (r.ReadLine() is { } line)
            {
                var text = line.Split('|')[0].Trim();
                if (text.Length > 0) list.Add(text);
            }
            return _builtin = [.. list];
        }
    }

    public static event Action? Changed;

    /// <summary>当前名言（按界面语言显示）</summary>
    public static string Current
    {
        get
        {
            var t = SettingsStore.Current.QuoteText;
            if (string.IsNullOrEmpty(t)) t = Builtin[0];
            return L.Zh(t);
        }
    }

    public static async Task MaybeRotateAsync()
    {
        var s = SettingsStore.Current;
        if (string.IsNullOrEmpty(s.QuoteText) || DateTime.Now - s.QuoteChangedAt >= Interval)
            await NextAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 准备下一句：一半概率取一言（失败就用内置），只返回，不改当前显示。
    /// 和天气、壁纸一起准备好后再由 Commit 同时切换。
    /// </summary>
    public static async Task<string> PickNextAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? q = null;
            if (Rng.NextDouble() < 0.5) q = await FetchHitokotoAsync().ConfigureAwait(false);
            return q ?? NextBuiltin();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>切换到准备好的那句（只改内存，不保存、不发通知）</summary>
    public static void Commit(string q)
    {
        var s = SettingsStore.Current;
        s.QuoteText = q;
        s.QuoteChangedAt = DateTime.Now;
    }

    /// <summary>立刻换一句（单独换名言时用）</summary>
    public static async Task NextAsync()
    {
        Commit(await PickNextAsync().ConfigureAwait(false));
        SettingsStore.Save();
        Changed?.Invoke();
    }

    private static string NextBuiltin()
    {
        var s = SettingsStore.Current;
        var deck = s.QuoteDeck.Where(i => i >= 0 && i < Builtin.Length).ToList();
        if (deck.Count == 0)
        {
            deck = Enumerable.Range(0, Builtin.Length).OrderBy(_ => Rng.Next()).ToList();
            // 新一轮的第一句不要和刚才那句一样
            var cur = Array.IndexOf(Builtin, s.QuoteText);
            if (deck[0] == cur && deck.Count > 1) { deck.RemoveAt(0); deck.Add(cur); }
        }
        var idx = deck[0];
        deck.RemoveAt(0);
        s.QuoteDeck = deck;
        return Builtin[idx];
    }

    private static async Task<string?> FetchHitokotoAsync()
    {
        var s = SettingsStore.Current;
        var preferred = await Region.HitokotoHostAsync().ConfigureAwait(false);
        var hosts = new[] { preferred, "https://v1.hitokoto.cn", "https://international.v1.hitokoto.cn" }.Distinct().ToArray();
        foreach (var host in hosts)
        {
            for (var tries = 0; tries < 2; tries++)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await Net.GetStringAsync(host + "/?c=d&c=i&c=k&encode=json&charset=utf-8", timeoutMs: 5_000).ConfigureAwait(false));
                    var text = doc.RootElement.TryGetProperty("hitokoto", out var h) ? h.GetString()?.Trim() ?? "" : "";
                    if (text.Length == 0) continue;
                    var key = text.GetHashCode(StringComparison.Ordinal).ToString("x");
                    if (s.QuoteSeen.Contains(key)) continue; // 最近出现过，换一句
                    s.QuoteSeen.Add(key);
                    if (s.QuoteSeen.Count > 300) s.QuoteSeen.RemoveRange(0, s.QuoteSeen.Count - 300);
                    return text;
                }
                catch
                {
                    break; // 换下一个节点
                }
            }
        }
        return null;
    }
}
