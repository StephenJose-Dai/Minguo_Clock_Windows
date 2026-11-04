using System.Globalization;

namespace Minguo.Core;

/// <summary>界面语言：只支持简体、繁體</summary>
public enum Lang { Hans, Hant }

/// <summary>语言：默认跟随系统（繁体地区/繁体系统用繁體，其它用简体），设置里手动选过就用手动的</summary>
public static class L
{
    /// <summary>当前语言（界面读取这个）</summary>
    public static Lang Current { get; private set; } = Lang.Hans;

    /// <summary>语言变化时通知界面刷新</summary>
    public static event Action? Changed;

    /// <summary>简体 → 繁體 转换（Windows 端注入系统转换；未注入时原样返回）</summary>
    public static Func<string, string> ToHantConverter { get; set; } = s => s;

    /// <summary>setting：0 跟随系统，1 简体，2 繁體</summary>
    public static Lang Resolve(int setting) => setting switch
    {
        1 => Lang.Hans,
        2 => Lang.Hant,
        _ => FromSystem(),
    };

    public static Lang FromSystem()
    {
        var c = CultureInfo.CurrentUICulture;
        var name = c.Name; // zh-CN / zh-TW / zh-Hant-TW / en-US …
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("TW", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("MO", StringComparison.OrdinalIgnoreCase))
                return Lang.Hant;
            return Lang.Hans;
        }
        var region = CultureInfo.CurrentCulture.Name;
        return region.EndsWith("TW", StringComparison.OrdinalIgnoreCase) ||
               region.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ||
               region.EndsWith("MO", StringComparison.OrdinalIgnoreCase)
            ? Lang.Hant
            : Lang.Hans;
    }

    public static void Refresh(int setting)
    {
        var v = Resolve(setting);
        if (v == Current) return;
        Current = v;
        Changed?.Invoke();
    }

    /// <summary>简繁文本</summary>
    public static string Tr(string hans, string hant) => Current == Lang.Hant ? hant : hans;

    public static string Tr(string hans, string hant, Lang lang) => lang == Lang.Hant ? hant : hans;

    /// <summary>把简体内容（名言、天气生活指数等）按当前语言显示</summary>
    public static string Zh(string simplified) => Current == Lang.Hant ? ToHantConverter(simplified) : simplified;
}
