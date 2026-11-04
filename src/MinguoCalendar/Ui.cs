using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>字体：系统默认 + 内置字体（打包在程序里，不用安装）</summary>
internal static class AppFonts
{
    public sealed record Option(string Id, string Hans, string Hant, string? Family);

    public static readonly Option[] All =
    [
        new("default", "系统默认", "系統預設", null),
        new("kai", "文楷", "文楷", "Minguo Kai"),
        new("round", "圆体", "圓體", "Minguo Round"),
        new("iansui", "芫荽", "芫荽", "Iansui"),
        new("orbitron", "Orbitron", "Orbitron", "Orbitron"),
        new("mono", "Share Tech Mono", "Share Tech Mono", "Share Tech Mono"),
    ];

    private static readonly Dictionary<string, FontFamily> Cache = new();

    public static FontFamily Of(string id)
    {
        var key = id + "|" + L.Current;
        if (Cache.TryGetValue(key, out var f)) return f;
        var opt = Array.Find(All, o => o.Id == id);
        f = opt?.Family == null
            ? new FontFamily(L.Current == Lang.Hant ? "Microsoft JhengHei UI, Microsoft YaHei UI, Segoe UI" : "Microsoft YaHei UI, Microsoft JhengHei UI, Segoe UI")
            : new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#" + opt.Family + ", Microsoft YaHei UI");
        Cache[key] = f;
        return f;
    }

    public static string Label(Option o) => L.Tr(o.Hans, o.Hant);
}

/// <summary>矢量图标：天气图标与安卓版同一套线条；GitHub 标志来自官方 Octicons（MIT）；Telegram 为通用纸飞机</summary>
internal static class Icons
{
    private const string Cloud = "M7.5,17h9a3.5,3.5 0 0,0 0.4,-6.98A5,5 0 0,0 7.3,11.1A3,3 0 0,0 7.5,17z";

    private static readonly Dictionary<string, (string Path, double Width)[]> Weather = new()
    {
        ["sun"] = [("M12,8a4,4 0 1,1 0,8a4,4 0 1,1 0,-8", 1.8), ("M12,2.5v2M12,19.5v2M2.5,12h2M19.5,12h2M5.3,5.3l1.4,1.4M17.3,17.3l1.4,1.4M5.3,18.7l1.4,-1.4M17.3,6.7l1.4,-1.4", 1.8)],
        ["moon"] = [("M14.5,3.5a8.5,8.5 0 1,0 6,14a7,7 0 0,1 -6,-14z", 1.8)],
        ["partly"] = [("M6,5.2v1.3M2.6,8.6h1.3M3.6,6.2l0.9,0.9M8.4,6.2l-0.9,0.9M4.3,11.2a2.6,2.6 0 0,1 4.3,-3.1", 1.8), (Cloud, 1.8)],
        ["cloud"] = [(Cloud, 1.8)],
        ["fog"] = [(Cloud, 1.8), ("M5,15.5h14M7,19h10M9,22.5h6", 1.8)],
        ["rain"] = [(Cloud, 1.8), ("M9,17.5l-1,3M12.5,17.5l-1,3M16,17.5l-1,3", 1.8)],
        ["snow"] = [(Cloud, 1.8), ("M8.5,18.5v0.01M12,20.5v0.01M15.5,18.5v0.01M10,22v0.01M14,22v0.01", 2.4)],
        ["thunder"] = [(Cloud, 1.8), ("M12.5,15.5l-2,3.5h3l-2,3.5", 1.8)],
    };

    public const string GitHub = "M10.226 17.284c-2.965-.36-5.054-2.493-5.054-5.256 0-1.123.404-2.336 1.078-3.144-.292-.741-.247-2.314.09-2.965.898-.112 2.111.36 2.83 1.01.853-.269 1.752-.404 2.853-.404 1.1 0 1.999.135 2.807.382.696-.629 1.932-1.1 2.83-.988.315.606.36 2.179.067 2.942.72.854 1.101 2 1.101 3.167 0 2.763-2.089 4.852-5.098 5.234.763.494 1.28 1.572 1.28 2.807v2.336c0 .674.561 1.056 1.235.786 4.066-1.55 7.255-5.615 7.255-10.646C23.5 6.188 18.334 1 11.978 1 5.62 1 .5 6.188.5 12.545c0 4.986 3.167 9.12 7.435 10.669.606.225 1.19-.18 1.19-.786V20.63a2.9 2.9 0 0 1-1.078.224c-1.483 0-2.359-.808-2.987-2.313-.247-.607-.517-.966-1.034-1.033-.27-.023-.359-.135-.359-.27 0-.27.45-.471.898-.471.652 0 1.213.404 1.797 1.235.45.651.921.943 1.483.943.561 0 .92-.202 1.437-.719.382-.381.674-.718.944-.943";

    public const string Telegram = "M2.5,11.2L20.8,3.6c0.8,-0.3 1.5,0.4 1.3,1.2l-3.1,14.6c-0.2,0.9 -1.2,1.2 -1.9,0.7l-4.6,-3.4 -2.3,2.2c-0.3,0.3 -0.8,0.1 -0.8,-0.3l0.1,-3.6 7.9,-7.2c0.3,-0.3 -0.1,-0.6 -0.4,-0.4L7.2,13.6 2.6,12.2c-0.9,-0.3 -0.9,-0.8 -0.1,-1z";

    public const string Pin = "M12,2a7,7 0 0,0 -7,7c0,5.2 7,13 7,13s7,-7.8 7,-13a7,7 0 0,0 -7,-7zM12,11.5a2.5,2.5 0 1,1 0,-5a2.5,2.5 0 0,1 0,5z";

    private static Geometry? Parse(string data)
    {
        try { return Geometry.Parse(data); }
        catch { return null; }
    }

    /// <summary>天气图标（线条），24×24 设计尺寸缩放到 size</summary>
    public static FrameworkElement WeatherIcon(string key, Brush brush, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (!Weather.TryGetValue(key, out var parts)) parts = Weather["cloud"];
        foreach (var (path, width) in parts)
        {
            var g = Parse(path);
            if (g == null) continue;
            canvas.Children.Add(new Path
            {
                Data = g,
                Stroke = brush,
                StrokeThickness = width,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>实心图标（GitHub、Telegram、定位）</summary>
    public static FrameworkElement Filled(string data, Brush brush, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var g = Parse(data);
        if (g != null) canvas.Children.Add(new Path { Data = g, Fill = brush });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}

/// <summary>设置界面的通用组件（卡片、开关行、选项行…），颜色都跟随主题</summary>
internal static class Ui
{
    public static TextBlock Text(string text, double size = 13, string brush = "TextBrush", bool bold = false, bool wrap = true)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Para(string text) => Para(text, new Thickness(0, 2, 0, 8));

    public static TextBlock Para(string text, Thickness margin)
    {
        var t = Text(text, 12.5, "SubBrush");
        t.LineHeight = 20;
        t.Margin = margin;
        return t;
    }

    /// <summary>圆角卡片，带金色标题</summary>
    public static Border Section(string title, params UIElement[] children)
    {
        var panel = new StackPanel();
        var head = Text(title, 15, "AccentBrush", bold: true);
        head.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(head);
        foreach (var c in children) panel.Children.Add(c);
        var b = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 14), Child = panel };
        b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        return b;
    }

    /// <summary>左边文字（标题 + 说明），右边控件</summary>
    public static Grid Row(string title, string? desc, UIElement right)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Text(title, 14));
        if (!string.IsNullOrEmpty(desc))
        {
            var d = Text(desc, 12, "SubBrush");
            d.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(d);
        }
        g.Children.Add(left);
        if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    public static Grid SwitchRow(string title, string? desc, bool value, Action<bool> onChange)
    {
        var t = new ToggleButton { IsChecked = value };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        t.Checked += (_, _) => onChange(true);
        t.Unchecked += (_, _) => onChange(false);
        return Row(title, desc, t);
    }

    /// <summary>一排胶囊选项</summary>
    public static WrapPanel Choice(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        var group = Guid.NewGuid().ToString("N");
        for (var i = 0; i < options.Count; i++)
        {
            var idx = i;
            var r = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected };
            r.SetResourceReference(FrameworkElement.StyleProperty, "Pill");
            r.Checked += (_, _) => onChange(idx);
            p.Children.Add(r);
        }
        return p;
    }

    public static System.Windows.Controls.Button Btn(string text, Action onClick, bool accent = false)
    {
        var b = new System.Windows.Controls.Button { Content = text };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static System.Windows.Controls.Button Link(string text, Action onClick)
    {
        var b = new System.Windows.Controls.Button { Content = text };
        b.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static WrapPanel Buttons(params UIElement[] items)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    public static TextBox Input(string value, string? hint = null)
    {
        var t = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 8) };
        if (!string.IsNullOrEmpty(hint)) t.ToolTip = hint;
        return t;
    }

    public static StackPanel Labeled(string label, UIElement control)
    {
        var p = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(Text(label, 12, "SubBrush"));
        p.Children.Add(control);
        return p;
    }

    /// <summary>在默认浏览器打开链接</summary>
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了
        }
    }
}
