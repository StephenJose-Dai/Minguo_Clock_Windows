using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>
/// 界面主题：暗黑（默认，黑金配色）/ 白天 / 自动（6:00–18:00 白天，其余暗黑）。
/// 只影响设置界面；屏保、锁屏背景的颜色跟随壁纸，小组件按自己的白字/深色字。
/// </summary>
internal static class Theme
{
    public const int Dark = 0, Light = 1, Auto = 2;

    public static bool IsDark { get; private set; } = true;

    public static event Action? Changed;

    private static readonly Dictionary<string, (string Dark, string Light)> Colors = new()
    {
        ["PageBrush"] = ("#0E1116", "#F6F2EA"),
        ["CardBrush"] = ("#171C24", "#FFFFFF"),
        ["FieldBrush"] = ("#0F1319", "#EFE9DE"),
        ["HoverBrush"] = ("#222936", "#E6DFD1"),
        ["TextBrush"] = ("#FFFFFF", "#1F1A14"),
        ["SubBrush"] = ("#8E98A8", "#6E6658"),
        ["AccentBrush"] = ("#E8C77A", "#9A6B12"),
        ["OnAccentBrush"] = ("#2A1F05", "#FFFFFF"),
        ["GoodBrush"] = ("#5BD68A", "#1E9E57"),
        ["WarnBrush"] = ("#FFB454", "#C77700"),
        ["DangerBrush"] = ("#FF6B6B", "#D93C3C"),
        ["HairlineBrush"] = ("#33FFFFFF", "#22000000"),
    };

    public static bool Resolve(int mode) => mode switch
    {
        Light => false,
        Auto => DateTime.Now.Hour is < 6 or >= 18,
        _ => true,
    };

    /// <summary>按设置刷新（自动模式每分钟调用一次）</summary>
    public static void Apply()
    {
        var dark = Resolve(SettingsStore.Current.ThemeMode);
        var res = Application.Current.Resources;
        foreach (var (key, (d, l)) in Colors)
        {
            var c = (Color)ColorConverter.ConvertFromString(dark ? d : l);
            res[key] = new SolidColorBrush(c);
        }
        res["UiFont"] = new FontFamily(L.Current == Lang.Hant
            ? "Microsoft JhengHei UI, Microsoft YaHei UI, Segoe UI"
            : "Microsoft YaHei UI, Microsoft JhengHei UI, Segoe UI");
        var changed = dark != IsDark;
        IsDark = dark;
        foreach (Window w in Application.Current.Windows)
            if (w is not WidgetWindow and not ScreenSaverWindow) ApplyTitleBar(w);
        if (changed) Changed?.Invoke();
    }

    public static void ApplyTitleBar(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) Native.SetDarkTitleBar(h, IsDark);
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
