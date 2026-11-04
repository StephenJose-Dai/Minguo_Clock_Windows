using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>设置窗口：左侧导航，右侧各页面（代码生成，切换语言 / 主题时整页重建）</summary>
public sealed class SettingsWindow : Window
{
    private static readonly (string Id, string Hans, string Hant)[] Pages =
    [
        ("home", "首页", "首頁"),
        ("display", "显示", "顯示"),
        ("widget", "桌面小组件", "桌面小工具"),
        ("lock", "锁屏背景", "鎖定畫面背景"),
        ("screensaver", "屏幕保护", "螢幕保護"),
        ("wallpaper", "壁纸", "桌布"),
        ("weather", "天气", "天氣"),
        ("about", "关于", "關於"),
    ];

    private readonly StackPanel _nav = new() { Margin = new Thickness(14, 0, 14, 14) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _brand = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _page = "home";
    private ClockFace? _preview;
    private Border? _weatherHost;
    private Border? _locationHost;
    private TextBlock? _lockStatus;
    private TextBlock? _bingStatus;

    public SettingsWindow()
    {
        Title = L.Tr("民国纪年", "民國紀年");
        Width = 1040;
        Height = 720;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.png")); } catch { /* 没图标也能用 */ }
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel();
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 26, 14, 22) };
        try
        {
            brandRow.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")),
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
            });
        }
        catch
        {
            // 忽略
        }
        _brand.FontSize = 19;
        _brand.FontWeight = FontWeights.SemiBold;
        _brand.VerticalAlignment = VerticalAlignment.Center;
        _brand.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        brandRow.Children.Add(_brand);
        DockPanel.SetDock(brandRow, Dock.Top);
        left.Children.Add(brandRow);
        left.Children.Add(_nav);
        root.Children.Add(left);

        _scroll.Padding = new Thickness(8, 26, 30, 26);
        Grid.SetColumn(_scroll, 1);
        root.Children.Add(_scroll);
        Content = root;

        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        _tick.Tick += (_, _) => _preview?.Refresh(DateTime.Now);
        _tick.Start();

        App.Refreshed += OnRefreshed;
        Theme.Changed += Build;
        L.Changed += OnLanguage;
        Closed += (_, _) =>
        {
            _tick.Stop();
            App.Refreshed -= OnRefreshed;
            Theme.Changed -= Build;
            L.Changed -= OnLanguage;
        };
        Build();
    }

    public void Navigate(string page)
    {
        if (Pages.All(p => p.Id != page)) return;
        _page = page;
        Build();
    }

    private void OnLanguage()
    {
        Theme.Apply();
        Build();
    }

    /// <summary>重建导航 + 当前页</summary>
    private void Build()
    {
        Title = L.Tr("民国纪年", "民國紀年");
        _brand.Text = Title;
        _nav.Children.Clear();
        foreach (var (id, hans, hant) in Pages)
        {
            var r = new RadioButton { Content = L.Tr(hans, hant), GroupName = "nav", IsChecked = id == _page };
            r.SetResourceReference(StyleProperty, "NavItem");
            r.Checked += (_, _) =>
            {
                if (_page == id) return;
                _page = id;
                BuildPage();
            };
            _nav.Children.Add(r);
        }
        BuildPage();
    }

    private void BuildPage()
    {
        _preview = null;
        _weatherHost = null;
        _locationHost = null;
        _lockStatus = null;
        _bingStatus = null;
        var content = _page switch
        {
            "display" => DisplayPage(),
            "widget" => WidgetPage(),
            "lock" => LockPage(),
            "screensaver" => ScreenSaverPage(),
            "wallpaper" => WallpaperPage(),
            "weather" => WeatherPage(),
            "about" => AboutPage(),
            _ => HomePage(),
        };
        content.MaxWidth = 860;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        _scroll.Content = content;
        _scroll.ScrollToTop();
    }

    private void OnRefreshed()
    {
        _preview?.LoadBackground();
        if (_weatherHost != null) _weatherHost.Child = WeatherCard();
        if (_locationHost != null) _locationHost.Child = LocationInfo();
        if (_lockStatus != null) _lockStatus.Text = LockImage.LastMessage;
        if (_bingStatus != null) _bingStatus.Text = BingStatus();
    }

    // ================= 通用 =================

    private static AppSettings S => SettingsStore.Current;

    private static void Save() => SettingsStore.Save();

    private static StackPanel Page(string title, string? subtitle, params UIElement[] items)
    {
        var p = new StackPanel();
        var t = Ui.Text(title, 26, bold: true);
        t.Margin = new Thickness(0, 0, 0, subtitle == null ? 18 : 4);
        p.Children.Add(t);
        if (subtitle != null)
        {
            var st = Ui.Text(subtitle, 13, "SubBrush");
            st.Margin = new Thickness(0, 0, 0, 18);
            p.Children.Add(st);
        }
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    /// <summary>16:9 的画面预览（圆角）</summary>
    private FrameworkElement Preview()
    {
        _preview = new ClockFace(ClockFace.Kind.Preview);
        var host = new Border { Child = _preview, Margin = new Thickness(0, 0, 0, 16) };
        host.SizeChanged += (_, e) =>
        {
            var h = e.NewSize.Width * 9 / 16;
            if (Math.Abs(host.Height - h) > 0.5) host.Height = h;
            host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, h), 18, 18);
        };
        return host;
    }

    private static TextBlock Status(string text, string brush = "SubBrush")
    {
        var t = Ui.Text(text, 12.5, brush);
        t.Margin = new Thickness(0, 6, 0, 2);
        return t;
    }

    private static string Ago(DateTime t)
    {
        if (t == default) return L.Tr("从未", "從未");
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return L.Tr("刚刚", "剛剛");
        if (d.TotalHours < 1) return L.Tr($"{(int)d.TotalMinutes} 分钟前", $"{(int)d.TotalMinutes} 分鐘前");
        if (d.TotalDays < 1) return L.Tr($"{(int)d.TotalHours} 小时前", $"{(int)d.TotalHours} 小時前");
        return t.ToString("yyyy-MM-dd HH:mm");
    }

    // ================= 首页 =================

    private StackPanel HomePage()
    {
        var now = DateTime.Now;
        var style = S.Style;
        var buttons = new List<UIElement>
        {
            Ui.Btn(L.Tr("换一句", "換一句"), () => App.Fire(Quotes.NextAsync), accent: true),
        };
        if (S.WallpaperMode == 2) buttons.Add(Ui.Btn(L.Tr("换一张壁纸", "換一張桌布"), () => App.Fire(() => Bing.RotateAsync())));
        buttons.Add(Ui.Btn(L.Tr("全屏预览", "全螢幕預覽"), ScreenSaver.Preview));

        return Page(Roc.DateLine(now, style) + "  " + Roc.Weekday(now), Roc.LunarLine(now, style),
            Preview(),
            Ui.Buttons(buttons.ToArray()),
            new Border { Height = 14 },
            Ui.Section(L.Tr("功能", "功能"),
                Ui.SwitchRow(L.Tr("桌面小组件", "桌面小工具"), L.Tr("透明显示在桌面上，可拖动、调整大小", "透明顯示在桌面上，可拖曳、調整大小"),
                    S.WidgetEnabled, v => App.Instance.ShowWidget(v)),
                Ui.SwitchRow(L.Tr("锁屏背景", "鎖定畫面背景"), L.Tr("把民国日期、天气、名言画进锁屏图片", "把民國日期、天氣、名言畫進鎖定畫面圖片"),
                    S.LockImageEnabled, SetLockImage),
                Ui.SwitchRow(L.Tr("屏幕保护程序", "螢幕保護程式"), L.Tr("电脑闲置时全屏显示民国时钟", "電腦閒置時全螢幕顯示民國時鐘"),
                    S.ScreensaverInstalled && ScreenSaver.IsActive(), SetScreenSaver),
                Ui.SwitchRow(L.Tr("天气", "天氣"), L.Tr("自动定位，每 30 分钟更新", "自動定位，每 30 分鐘更新"),
                    S.WeatherEnabled, v =>
                    {
                        S.WeatherEnabled = v;
                        Save();
                        if (v) App.Fire(() => WeatherService.RefreshAsync());
                    })));
    }

    private void SetLockImage(bool on)
    {
        S.LockImageEnabled = on;
        Save();
        if (on) UpdateLockImage();
    }

    private void UpdateLockImage()
    {
        if (_lockStatus != null) _lockStatus.Text = L.Tr("正在生成…", "正在產生…");
        App.Fire(async () =>
        {
            var err = await LockImage.UpdateAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (_lockStatus != null) _lockStatus.Text = LockImage.LastMessage;
                if (err != null && IsVisible)
                    MessageBox.Show(this, err, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        });
    }

    private void SetScreenSaver(bool on)
    {
        if (on)
        {
            var err = ScreenSaver.Install(S.ScreensaverTimeoutMinutes);
            if (err != null) MessageBox.Show(this, err, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            ScreenSaver.Uninstall();
        }
    }

    // ================= 显示 =================

    private StackPanel DisplayPage()
    {
        var sig = Ui.Input(S.Signature, L.Tr("显示在屏保和锁屏背景上", "顯示在螢幕保護和鎖定畫面背景上"));
        sig.MaxLength = 40;
        sig.TextChanged += (_, _) =>
        {
            S.Signature = sig.Text.Trim();
            Save();
        };
        var fonts = AppFonts.All;
        var fontIdx = Math.Max(0, Array.FindIndex(fonts, f => f.Id == S.FontId));
        var h24 = S.Use24Hour switch { null => 0, false => 1, true => 2 };

        return Page(L.Tr("显示", "顯示"), null,
            Ui.Section(L.Tr("外观", "外觀"),
                Ui.Text(L.Tr("主题", "主題"), 14),
                Ui.Choice([L.Tr("暗黑", "暗黑"), L.Tr("白天", "白天"), L.Tr("自动（18:00–6:00 暗黑）", "自動（18:00–6:00 暗黑）")], S.ThemeMode, i =>
                {
                    S.ThemeMode = i;
                    Save();
                    Theme.Apply();
                    App.Instance.Dispatcher.BeginInvoke(Build); // 图标颜色跟着变
                }),
                Ui.Text(L.Tr("字体", "字型"), 14),
                Ui.Choice(fonts.Select(AppFonts.Label).ToList(), fontIdx, i =>
                {
                    S.FontId = fonts[i].Id;
                    Save();
                }),
                Ui.Para(L.Tr("字体用于小组件、屏保和锁屏背景。", "字型用於小工具、螢幕保護和鎖定畫面背景。"))),
            Ui.Section(L.Tr("日期与语言", "日期與語言"),
                Ui.Text(L.Tr("语言", "語言"), 14),
                Ui.Choice([L.Tr("跟随系统", "跟隨系統"), "简体中文", "繁體中文"], S.Language, i =>
                {
                    S.Language = i;
                    Save();
                    L.Refresh(i); // 触发 L.Changed → 整页重建
                }),
                Ui.Text(L.Tr("纪年写法", "紀年寫法"), 14),
                Ui.Choice([L.Tr("民国", "民國"), L.Tr("中华民国", "中華民國")], S.FullEra ? 1 : 0, i =>
                {
                    S.FullEra = i == 1;
                    Save();
                }),
                Ui.Text(L.Tr("数字", "數字"), 14),
                Ui.Choice([L.Tr("阿拉伯数字（115年11月3日）", "阿拉伯數字（115年11月3日）"), L.Tr("中文数字（一一五年十一月三日）", "中文數字（一一五年十一月三日）")],
                    S.ChineseNumerals ? 1 : 0, i =>
                    {
                        S.ChineseNumerals = i == 1;
                        Save();
                    }),
                Ui.Text(L.Tr("时间格式", "時間格式"), 14),
                Ui.Choice([L.Tr("跟随系统", "跟隨系統"), L.Tr("12 小时制", "12 小時制"), L.Tr("24 小时制", "24 小時制")], h24, i =>
                {
                    S.Use24Hour = i switch { 1 => false, 2 => true, _ => null };
                    Save();
                }),
                Ui.SwitchRow(L.Tr("屏保显示秒", "螢幕保護顯示秒"), null, S.ShowSeconds, v =>
                {
                    S.ShowSeconds = v;
                    Save();
                })),
            Ui.Section(L.Tr("签名", "簽名"),
                Ui.Para(L.Tr("一句你自己的话，显示在屏保和锁屏背景的最下面，留空就不显示。", "一句你自己的話，顯示在螢幕保護和鎖定畫面背景的最下面，留空就不顯示。")),
                sig),
            Ui.Section(L.Tr("启动", "啟動"),
                Ui.SwitchRow(L.Tr("开机自动运行", "開機自動執行"), L.Tr("在托盘显示民国日期，并显示桌面小组件", "在通知區域顯示民國日期，並顯示桌面小工具"),
                    S.StartWithWindows, v =>
                    {
                        S.StartWithWindows = v;
                        Save();
                        AutoStart.Sync();
                    })));
    }

    // ================= 桌面小组件 =================

    private StackPanel WidgetPage()
    {
        return Page(L.Tr("桌面小组件", "桌面小工具"), L.Tr("透明背景，没有边框，固定在桌面上，不会挡住其他窗口", "透明背景，沒有邊框，固定在桌面上，不會擋住其他視窗"),
            Ui.Section(L.Tr("小组件", "小工具"),
                Ui.SwitchRow(L.Tr("显示桌面小组件", "顯示桌面小工具"), null, S.WidgetEnabled, v => App.Instance.ShowWidget(v)),
                Ui.Text(L.Tr("文字颜色", "文字顏色"), 14),
                Ui.Choice([L.Tr("白字（深色壁纸）", "白字（深色桌布）"), L.Tr("深色字（浅色壁纸）", "深色字（淺色桌布）")], S.WidgetTextStyle, i =>
                {
                    S.WidgetTextStyle = i;
                    Save();
                }),
                Ui.SwitchRow(L.Tr("显示天气", "顯示天氣"), null, S.WidgetShowWeather, v =>
                {
                    S.WidgetShowWeather = v;
                    Save();
                }),
                Ui.SwitchRow(L.Tr("显示名言", "顯示名言"), L.Tr("每 30 分钟换一句，点一下也能换", "每 30 分鐘換一句，點一下也能換"), S.WidgetShowQuote, v =>
                {
                    S.WidgetShowQuote = v;
                    Save();
                }),
                Ui.SwitchRow(L.Tr("锁定位置和大小", "鎖定位置和大小"), L.Tr("锁定后不能拖动，也不显示虚线框", "鎖定後不能拖曳，也不顯示虛線框"), S.WidgetLocked, v =>
                {
                    S.WidgetLocked = v;
                    Save();
                }),
                Ui.Buttons(Ui.Btn(L.Tr("恢复默认位置和大小", "恢復預設位置和大小"), () =>
                {
                    if (App.Instance.Widget is { } w) w.ResetBounds();
                    else
                    {
                        S.WidgetX = double.NaN;
                        S.WidgetY = double.NaN;
                        S.WidgetWidth = 420;
                        S.WidgetHeight = 300;
                        Save();
                    }
                }))),
            Ui.Section(L.Tr("怎么调整", "怎麼調整"),
                Ui.Para(L.Tr(
                    "· 鼠标移到小组件上会出现虚线框：按住拖动可以移动；拖四条边或四个角可以调整宽度和高度（宽度最多一屏，高度最多屏幕的 60%）。\n· 右键小组件：锁定、换一句、切换文字颜色、隐藏。\n· 按 Win + D 显示桌面时小组件仍然在。",
                    "· 滑鼠移到小工具上會出現虛線框：按住拖曳可以移動；拖曳四條邊或四個角可以調整寬度和高度（寬度最多一個螢幕，高度最多螢幕的 60%）。\n· 右鍵小工具：鎖定、換一句、切換文字顏色、隱藏。\n· 按 Win + D 顯示桌面時小工具仍然在。"))));
    }

    // ================= 锁屏背景 =================

    private StackPanel LockPage()
    {
        _lockStatus = Status(LockImage.LastMessage.Length > 0 ? LockImage.LastMessage
            : S.LockImageSetAt == default ? "" : L.Tr("上次更新：", "上次更新：") + Ago(S.LockImageSetAt));
        return Page(L.Tr("锁屏背景", "鎖定畫面背景"), null,
            Ui.Section(L.Tr("锁屏背景", "鎖定畫面背景"),
                Ui.SwitchRow(L.Tr("自动更新锁屏背景", "自動更新鎖定畫面背景"),
                    L.Tr("每 30 分钟把壁纸、民国日期、农历、天气、名言画成一张图，设为锁屏图片", "每 30 分鐘把桌布、民國日期、農曆、天氣、名言畫成一張圖，設為鎖定畫面圖片"),
                    S.LockImageEnabled, SetLockImage),
                Ui.Buttons(
                    Ui.Btn(L.Tr("立即更新", "立即更新"), UpdateLockImage, accent: true),
                    Ui.Btn(L.Tr("打开系统锁屏设置", "開啟系統鎖定畫面設定"), LockImage.OpenSystemSettings)),
                _lockStatus),
            Ui.Section(L.Tr("说明", "說明"),
                Ui.Para(L.Tr(
                    "Windows 不允许第三方程序替换锁屏界面本身：锁屏上的时间、密码 / PIN 输入框都由系统显示，程序只能设置锁屏图片。所以这里把民国日期等内容画进图片里，放在画面右侧，避开系统时钟（Windows 10 在左下角，Windows 11 在上方正中）。",
                    "Windows 不允許第三方程式取代鎖定畫面本身：鎖定畫面上的時間、密碼 / PIN 輸入框都由系統顯示，程式只能設定鎖定畫面圖片。所以這裡把民國日期等內容畫進圖片裡，放在畫面右側，避開系統時鐘（Windows 10 在左下角，Windows 11 在上方正中）。")),
                Ui.Para(L.Tr(
                    "如果锁屏没有变化：打开系统锁屏设置，把“个性化锁屏界面”从“Windows 聚焦”改成“图片”。锁屏图片里的时间不会走动，所以图片里只放日期，不放时间。",
                    "如果鎖定畫面沒有變化：開啟系統鎖定畫面設定，把「個人化鎖定畫面」從「Windows 焦點」改成「圖片」。鎖定畫面圖片裡的時間不會走動，所以圖片裡只放日期，不放時間。"))));
    }

    // ================= 屏幕保护 =================

    private StackPanel ScreenSaverPage()
    {
        var mins = ScreenSaver.Timeouts;
        var idx = Math.Max(0, Array.IndexOf(mins, S.ScreensaverTimeoutMinutes));
        if (Array.IndexOf(mins, S.ScreensaverTimeoutMinutes) < 0) idx = 2;
        var active = S.ScreensaverInstalled && ScreenSaver.IsActive();
        return Page(L.Tr("屏幕保护", "螢幕保護"), L.Tr("电脑闲置一段时间后，全屏显示民国时钟、日期、农历、天气和名言", "電腦閒置一段時間後，全螢幕顯示民國時鐘、日期、農曆、天氣和名言"),
            Preview(),
            Ui.Section(L.Tr("屏幕保护程序", "螢幕保護程式"),
                Ui.SwitchRow(L.Tr("设为屏幕保护程序", "設為螢幕保護程式"), active ? L.Tr("已启用", "已啟用") : L.Tr("未启用", "未啟用"), active, v =>
                {
                    SetScreenSaver(v);
                    BuildPage();
                }),
                Ui.Text(L.Tr("等待时间", "等待時間"), 14),
                Ui.Choice(mins.Select(m => m == 60 ? L.Tr("1 小时", "1 小時") : L.Tr($"{m} 分钟", $"{m} 分鐘")).ToList(), idx, i =>
                {
                    S.ScreensaverTimeoutMinutes = mins[i];
                    Save();
                    if (S.ScreensaverInstalled) ScreenSaver.Install(mins[i]);
                }),
                Ui.Buttons(
                    Ui.Btn(L.Tr("全屏预览", "全螢幕預覽"), ScreenSaver.Preview, accent: true),
                    Ui.Btn(L.Tr("打开系统屏保设置", "開啟系統螢幕保護設定"), ScreenSaver.OpenSystemSettings))),
            Ui.Section(L.Tr("说明", "說明"),
                Ui.Para(L.Tr(
                    "预览时移动鼠标或按任意键退出。多台显示器会同时显示。\n想从屏保回来时要输入密码：在系统屏保设置里勾选“在恢复时显示登录屏幕”。\n画面跟随“壁纸”页的设置；签名在“显示”页填写。",
                    "預覽時移動滑鼠或按任意鍵結束。多台螢幕會同時顯示。\n想從螢幕保護回來時要輸入密碼：在系統螢幕保護設定裡勾選「繼續執行後，顯示登入畫面」。\n畫面跟隨「桌布」頁的設定；簽名在「顯示」頁填寫。"))));
    }

    // ================= 壁纸 =================

    private string BingStatus()
    {
        var f = Bing.CurrentFile;
        if (f == null) return L.Tr("正在获取必应壁纸…（需要联网）", "正在取得 Bing 桌布…（需要連網）");
        return L.Tr("上次更换：", "上次更換：") + Ago(S.BingChangedAt) + L.Tr("，每 30 分钟自动换一张", "，每 30 分鐘自動換一張");
    }

    private StackPanel WallpaperPage()
    {
        var items = new List<UIElement>
        {
            Preview(),
        };
        var mode = new List<UIElement>
        {
            Ui.Choice([L.Tr("主题渐变", "主題漸層"), L.Tr("自选图片", "自選圖片"), L.Tr("必应每日壁纸", "Bing 每日桌布")], S.WallpaperMode, i =>
            {
                S.WallpaperMode = i;
                Save();
                if (i == 2) App.Fire(() => Bing.RefreshAsync());
                BuildPage();
            }),
        };
        switch (S.WallpaperMode)
        {
            case 0:
                var sw = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                for (var i = 0; i < WallThemes.All.Length; i++)
                {
                    var idx = i;
                    var t = WallThemes.All[i];
                    var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 8), Cursor = Cursors.Hand };
                    var swatch = new Border
                    {
                        Width = 92,
                        Height = 56,
                        CornerRadius = new CornerRadius(12),
                        Background = WallThemes.Brush(t),
                        BorderThickness = new Thickness(S.ThemeIndex == i ? 2.5 : 0),
                    };
                    swatch.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                    cell.Children.Add(swatch);
                    var label = Ui.Text(L.Tr(t.Hans, t.Hant), 12, S.ThemeIndex == i ? "AccentBrush" : "SubBrush");
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.Margin = new Thickness(0, 4, 0, 0);
                    cell.Children.Add(label);
                    cell.MouseLeftButtonUp += (_, _) =>
                    {
                        S.ThemeIndex = idx;
                        Save();
                        BuildPage();
                    };
                    sw.Children.Add(cell);
                }
                mode.Add(sw);
                break;
            case 1:
                var path = Ui.Text(string.IsNullOrEmpty(S.CustomWallpaper) ? L.Tr("还没有选择图片", "還沒有選擇圖片") : S.CustomWallpaper, 12.5, "SubBrush");
                path.Margin = new Thickness(0, 8, 0, 0);
                mode.Add(path);
                mode.Add(Ui.Buttons(Ui.Btn(L.Tr("选择图片…", "選擇圖片…"), () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = L.Tr("图片", "圖片") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.jfif",
                        Title = L.Tr("选择壁纸", "選擇桌布"),
                    };
                    if (dlg.ShowDialog(this) != true) return;
                    S.CustomWallpaper = dlg.FileName;
                    Save();
                    BuildPage();
                }, accent: true)));
                break;
            default:
                _bingStatus = Status(BingStatus());
                mode.Add(_bingStatus);
                mode.Add(Ui.Buttons(Ui.Btn(L.Tr("换一张", "換一張"), () =>
                {
                    if (_bingStatus != null) _bingStatus.Text = L.Tr("正在下载…", "正在下載…");
                    App.Fire(() => Bing.RotateAsync());
                }, accent: true)));
                break;
        }
        items.Add(Ui.Section(L.Tr("壁纸来源", "桌布來源"), mode.ToArray()));

        int[] dims = [0, 15, 25, 40, 55, 70];
        var di = Array.IndexOf(dims, S.WallpaperDim);
        if (di < 0) di = 2;
        items.Add(Ui.Section(L.Tr("暗化", "暗化"),
            Ui.Para(L.Tr("给图片蒙一层黑色，文字更清楚（渐变主题不受影响）。", "給圖片蒙一層黑色，文字更清楚（漸層主題不受影響）。")),
            Ui.Choice(dims.Select(d => d + "%").ToList(), di, i =>
            {
                S.WallpaperDim = dims[i];
                Save();
            })));
        items.Add(Ui.Section(L.Tr("说明", "說明"),
            Ui.Para(L.Tr("壁纸用于屏幕保护和锁屏背景；桌面小组件是透明的，直接显示在你的桌面壁纸上。",
                "桌布用於螢幕保護和鎖定畫面背景；桌面小工具是透明的，直接顯示在你的桌面桌布上。"))));
        return Page(L.Tr("壁纸", "桌布"), null, items.ToArray());
    }

    // ================= 天气 =================

    private StackPanel WeatherPage()
    {
        _locationHost = new Border { Child = LocationInfo() };
        _weatherHost = new Border { Child = WeatherCard() };

        var (_, _, _, own) = Geo.ApiConfig();
        var key = Ui.Input(S.ApiKey);
        var secret = Ui.Input(S.ApiSecret);
        var url = Ui.Input(string.IsNullOrWhiteSpace(S.ApiUrl) ? AppConfig.ApiUrl : S.ApiUrl);
        var apiStatus = Status(ApiStatusText(own), own ? "GoodBrush" : "SubBrush");
        var testResult = Status("");

        var locating = Status("");
        var locateBtn = Ui.Btn(L.Tr("立即定位", "立即定位"), () => { }, accent: true);
        locateBtn.Click += async (_, _) =>
        {
            locateBtn.IsEnabled = false;
            locating.Text = L.Tr("正在定位…", "正在定位…");
            try
            {
                S.AutoLocate = true;
                Save();
                var name = await Task.Run(() => WeatherService.LocateNowAsync());
                if (name == null)
                {
                    locating.Text = L.Tr("定位失败，请检查网络，或在 Windows 设置里打开定位服务", "定位失敗，請檢查網路，或在 Windows 設定裡開啟定位服務");
                }
                else
                {
                    locating.Text = L.Tr("已定位：", "已定位：") + name;
                    await Task.Run(() => WeatherService.RefreshAsync());
                }
            }
            catch (Exception ex)
            {
                locating.Text = ex.Message;
            }
            finally
            {
                locateBtn.IsEnabled = true;
            }
        };

        var deniedHint = new StackPanel { Visibility = Visibility.Collapsed };
        deniedHint.Children.Add(Status(L.Tr("系统定位已关闭，只能按网络大致定位。打开后更准确：", "系統定位已關閉，只能按網路大致定位。開啟後更準確："), "WarnBrush"));
        deniedHint.Children.Add(Ui.Buttons(Ui.Btn(L.Tr("打开定位设置", "開啟定位設定"), () => Ui.Open("ms-settings:privacy-location"))));
        _ = ShowIfDenied(deniedHint);

        var testBtn = Ui.Btn(L.Tr("测试", "測試"), () => { });
        testBtn.Click += async (_, _) =>
        {
            if (!S.HasLocation)
            {
                testResult.Text = L.Tr("请先定位或选择城市", "請先定位或選擇城市");
                return;
            }
            testBtn.IsEnabled = false;
            testResult.Text = L.Tr("正在测试…", "正在測試…");
            var r = await Task.Run(() => Geo.ApiAsync(S.Lat, S.Lon));
            testBtn.IsEnabled = true;
            if (r.Error != null)
            {
                testResult.Text = L.Tr("失败：", "失敗：") + r.Error;
                testResult.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }
            else
            {
                testResult.Text = L.Tr("成功：", "成功：") + (r.Address.Length > 0 ? r.Address : r.Place?.Label);
                testResult.SetResourceReference(TextBlock.ForegroundProperty, "GoodBrush");
            }
        };

        return Page(L.Tr("天气", "天氣"), null,
            Ui.Section(L.Tr("天气", "天氣"),
                Ui.SwitchRow(L.Tr("显示天气", "顯示天氣"), L.Tr("每 30 分钟更新一次", "每 30 分鐘更新一次"), S.WeatherEnabled, v =>
                {
                    S.WeatherEnabled = v;
                    Save();
                    if (v) App.Fire(() => WeatherService.RefreshAsync());
                }),
                Ui.SwitchRow(L.Tr("自动定位", "自動定位"), L.Tr("用 Windows 定位服务（Wi‑Fi / GPS），拿不到时按网络大致定位", "用 Windows 定位服務（Wi‑Fi / GPS），拿不到時按網路大致定位"), S.AutoLocate, v =>
                {
                    S.AutoLocate = v;
                    if (v) S.LocatedAt = default;
                    Save();
                    if (v) App.Fire(() => WeatherService.RefreshAsync());
                })),
            Ui.Section(L.Tr("位置", "位置"),
                _locationHost,
                Ui.Buttons(locateBtn, Ui.Btn(L.Tr("搜索城市…", "搜尋城市…"), SearchCity)),
                locating,
                deniedHint),
            Ui.Section(L.Tr("当前天气", "目前天氣"),
                _weatherHost,
                Ui.Buttons(Ui.Btn(L.Tr("刷新", "重新整理"), () => App.Fire(async () =>
                {
                    if (S.WeatherCache != null) S.WeatherCache = S.WeatherCache with { FetchedAt = default };
                    await WeatherService.RefreshAsync().ConfigureAwait(false);
                })))),
            Ui.Section(L.Tr("天气接口", "天氣介面"),
                Ui.Para(L.Tr("用于把定位坐标换成所在的区县，填了自己的 Key 后优先使用。", "用於把定位座標換成所在的區縣，填了自己的 Key 後優先使用。")),
                apiStatus,
                Ui.Labeled("Key", key),
                Ui.Labeled(L.Tr("私钥（没开数字签名就留空）", "私鑰（沒開數位簽章就留空）"), secret),
                Ui.Labeled(L.Tr("接口地址", "介面位址"), url),
                Ui.Buttons(
                    Ui.Btn(L.Tr("保存", "儲存"), () =>
                    {
                        S.ApiKey = key.Text.Trim();
                        S.ApiSecret = secret.Text.Trim();
                        var u = url.Text.Trim();
                        S.ApiUrl = u == AppConfig.ApiUrl ? "" : u;
                        S.PlaceCountry = ""; // 让下次刷新重新解析地名
                        Save();
                        var nowOwn = Geo.ApiConfig().Own;
                        apiStatus.Text = ApiStatusText(nowOwn);
                        apiStatus.SetResourceReference(TextBlock.ForegroundProperty, nowOwn ? "GoodBrush" : "SubBrush");
                        testResult.Text = L.Tr("已保存", "已儲存");
                        App.Fire(() => WeatherService.RefreshAsync());
                    }, accent: true),
                    testBtn,
                    Ui.Btn(L.Tr("清空", "清除"), () =>
                    {
                        key.Text = "";
                        secret.Text = "";
                        url.Text = AppConfig.ApiUrl;
                    })),
                testResult));
    }

    private static string ApiStatusText(bool own)
    {
        if (own) return L.Tr("当前使用你自己的 Key", "目前使用你自己的 Key");
        if (!string.IsNullOrWhiteSpace(AppConfig.ApiKey)) return L.Tr("当前使用公共接口，频率将受到限制", "目前使用公共介面，頻率將受到限制");
        return L.Tr("未填写 Key，天气数据可能会不准确", "未填寫 Key，天氣資料可能會不準確");
    }

    private static async Task ShowIfDenied(UIElement hint)
    {
        if (await Location.IsDeniedAsync()) hint.Visibility = Visibility.Visible;
    }

    private static UIElement LocationInfo()
    {
        var p = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Icons.Filled(Icons.Pin, Theme.Brush("AccentBrush"), 20));
        var name = Ui.Text(S.HasLocation && S.CityName.Length > 0 ? S.CityName : L.Tr("尚未定位", "尚未定位"), 17, bold: true);
        name.Margin = new Thickness(8, 0, 0, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(name);
        p.Children.Add(row);
        if (S.HasLocation)
        {
            var src = Location.SourceLabel(S.LocSource);
            var detail = (src.Length > 0 ? src + " · " : "") + L.Tr("定位于", "定位於") + " " + Ago(S.LocatedAt);
            if (!S.AutoLocate) detail += " · " + L.Tr("自动定位已关闭", "自動定位已關閉");
            var d = Ui.Text(detail, 12.5, "SubBrush");
            d.Margin = new Thickness(28, 4, 0, 4);
            p.Children.Add(d);
        }
        return p;
    }

    private static UIElement WeatherCard()
    {
        var w = WeatherService.Current;
        if (w == null)
            return Ui.Para(S.WeatherEnabled ? L.Tr("还没有天气数据：请定位或搜索城市，并确认已联网。", "還沒有天氣資料：請定位或搜尋城市，並確認已連網。")
                : L.Tr("天气已关闭。", "天氣已關閉。"));
        var accent = Theme.Brush("AccentBrush");
        var sub = Theme.Brush("SubBrush");
        var p = new StackPanel();

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(Icons.WeatherIcon(w.Cond.Icon(w.IsDay), accent, 56));
        var temp = Ui.Text(WeatherService.Temp(w.Temp), 44, bold: false, wrap: false);
        temp.Margin = new Thickness(14, 0, 18, 0);
        temp.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(temp);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Ui.Text(WeatherService.Summary(w), 15));
        var extra = new List<string> { WeatherService.CityLabel(w) };
        if (w.Aqi is { } aqi) extra.Add(L.Tr("空气", "空氣") + " " + WeatherService.AqiLabel(aqi) + " " + aqi);
        if (w.Humidity >= 0) extra.Add(L.Tr("湿度", "濕度") + " " + w.Humidity + "%");
        if (!double.IsNaN(w.FeelsLike)) extra.Add(L.Tr("体感", "體感") + " " + WeatherService.Temp(w.FeelsLike));
        info.Children.Add(Ui.Text(string.Join("  ·  ", extra), 12.5, "SubBrush"));
        info.Children.Add(Ui.Text(L.Tr("更新于 ", "更新於 ") + Ago(w.FetchedAt), 12, "SubBrush"));
        top.Children.Add(info);
        p.Children.Add(top);

        var advice = WeatherService.Advice(w);
        if (advice.Count > 0)
        {
            var a = Ui.Para(string.Join("\n", advice.Select(x => "· " + x)), new Thickness(0, 12, 0, 4));
            p.Children.Add(a);
        }

        if (w.Days.Count > 0)
        {
            var days = new UniformGrid { Rows = 1, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var d in w.Days.Where(d => d.Date.Date >= DateTime.Today).Take(7))
            {
                var c = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                var label = Ui.Text(Roc.RelativeDay(d.Date, DateTime.Today), 12.5, "SubBrush", wrap: false);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                c.Children.Add(label);
                var icon = Icons.WeatherIcon(d.Cond.Icon(), sub, 26);
                icon.Margin = new Thickness(0, 6, 0, 6);
                c.Children.Add(icon);
                var t = Ui.Text($"{WeatherService.Temp(d.Min)} / {WeatherService.Temp(d.Max)}", 12.5, wrap: false);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                c.Children.Add(t);
                if (d.RainChance is > 0)
                {
                    var rc = Ui.Text("☂ " + d.RainChance + "%", 11.5, "SubBrush", wrap: false);
                    rc.HorizontalAlignment = HorizontalAlignment.Center;
                    c.Children.Add(rc);
                }
                days.Children.Add(c);
            }
            p.Children.Add(days);
        }
        return p;
    }

    /// <summary>搜索城市（手动选择后关闭自动定位）</summary>
    private void SearchCity()
    {
        var dlg = new Window
        {
            Owner = this,
            Title = L.Tr("搜索城市", "搜尋城市"),
            Width = 460,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        dlg.SetResourceReference(BackgroundProperty, "PageBrush");
        dlg.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        dlg.SourceInitialized += (_, _) => Theme.ApplyTitleBar(dlg);

        var root = new DockPanel { Margin = new Thickness(20) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var input = Ui.Input("", null);
        input.Margin = new Thickness(0, 0, 8, 0);
        var go = Ui.Btn(L.Tr("搜索", "搜尋"), () => { }, accent: true);
        go.Margin = new Thickness(0);
        DockPanel.SetDock(go, Dock.Right);
        bar.Children.Add(go);
        bar.Children.Add(input);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var hint = Ui.Para(L.Tr("输入城市或区县名，例如：福州、芗城、台北、东京", "輸入城市或區縣名，例如：福州、薌城、台北、東京"));
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        var list = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dlg.Content = root;

        async Task Run()
        {
            var q = input.Text.Trim();
            if (q.Length == 0) return;
            go.IsEnabled = false;
            list.Children.Clear();
            list.Children.Add(Ui.Para(L.Tr("正在搜索…", "正在搜尋…")));
            try
            {
                // 繁体输入先转成简体再搜也能搜到，这里两种都试
                var results = await Task.Run(() => WeatherService.SearchCityAsync(q));
                list.Children.Clear();
                if (results.Count == 0) list.Children.Add(Ui.Para(L.Tr("没有找到", "沒有找到")));
                foreach (var c in results)
                {
                    var content = new StackPanel();
                    content.Children.Add(Ui.Text(c.Name, 14, wrap: false));
                    content.Children.Add(Ui.Text(c.Region, 12, "SubBrush", wrap: false));
                    var b = new Button
                    {
                        Content = content,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 8),
                    };
                    var city = c;
                    b.Click += (_, _) =>
                    {
                        var place = WeatherService.PlaceOf(city);
                        S.AutoLocate = false;
                        WeatherService.SetLocation(place.Label.Length > 0 ? place.Label : city.Name, city.Lat, city.Lon, place);
                        S.LocSource = "manual";
                        S.LocatedAt = DateTime.Now;
                        S.WeatherCache = null;
                        Save();
                        App.Fire(() => WeatherService.RefreshAsync());
                        dlg.Close();
                        BuildPage();
                    };
                    list.Children.Add(b);
                }
            }
            catch (Exception ex)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Para(L.Tr("搜索失败：", "搜尋失敗：") + ex.Message));
            }
            finally
            {
                go.IsEnabled = true;
            }
        }

        go.Click += async (_, _) => await Run();
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await Run();
        };
        dlg.Loaded += (_, _) => input.Focus();
        dlg.ShowDialog();
    }

    // ================= 关于 =================

    private StackPanel AboutPage()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var ver = version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        var accent = Theme.Brush("AccentBrush");

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        try
        {
            head.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")), Width = 72, Height = 72 });
        }
        catch
        {
            // 忽略
        }
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        names.Children.Add(Ui.Text(L.Tr("民国纪年", "民國紀年"), 24, "AccentBrush", bold: true));
        names.Children.Add(Ui.Text(L.Tr("版本 ", "版本 ") + ver + " · Windows", 13, "SubBrush"));
        names.Children.Add(Ui.Text(L.Tr("作者 ", "作者 ") + AppConfig.Author, 13, "SubBrush"));
        head.Children.Add(names);

        UIElement LinkRow(string icon, string label, string url)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ic = Icons.Filled(icon, accent, 26);
            ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(Ui.Text(label, 14));
            if (string.IsNullOrWhiteSpace(url))
            {
                right.Children.Add(Ui.Text(L.Tr("未设置", "未設定"), 12.5, "SubBrush"));
            }
            else
            {
                var l = Ui.Link(url, () => Ui.Open(url));
                l.HorizontalAlignment = HorizontalAlignment.Left;
                right.Children.Add(l);
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        var licenses = Path.Combine(AppContext.BaseDirectory, "Licenses");
        var copyright = Ui.Text($"© {DateTime.Now.Year} {AppConfig.Author}", 12.5, "SubBrush");
        copyright.HorizontalAlignment = HorizontalAlignment.Center;
        copyright.Margin = new Thickness(0, 10, 0, 0);

        return Page(L.Tr("关于", "關於"), null,
            head,
            Ui.Section(L.Tr("联系", "聯絡"),
                LinkRow(Icons.GitHub, "GitHub", AppConfig.GitHubUrl),
                LinkRow(Icons.Telegram, "Telegram", AppConfig.TelegramUrl)),
            Ui.Section(L.Tr("数据", "資料"),
                Ui.Para(L.Tr("设置保存在 %APPDATA%\\MinguoCalendar，缓存（壁纸、锁屏图）在 %LOCALAPPDATA%\\MinguoCalendar。",
                    "設定儲存在 %APPDATA%\\MinguoCalendar，快取（桌布、鎖定畫面圖）在 %LOCALAPPDATA%\\MinguoCalendar。")),
                Ui.Buttons(
                    Ui.Btn(L.Tr("清空缓存", "清除快取"), () =>
                    {
                        try
                        {
                            var bing = Path.Combine(SettingsStore.CacheDir, "bing");
                            if (Directory.Exists(bing)) Directory.Delete(bing, true);
                        }
                        catch
                        {
                            // 正在使用的文件删不掉就算了
                        }
                        LockImage.CleanCache();
                        S.BingList = [];
                        S.BingListDate = "";
                        S.BingIndex = -1;
                        Save();
                        App.Fire(() => Bing.RefreshAsync(true));
                        MessageBox.Show(this, L.Tr("缓存已清空", "快取已清除"), Title);
                    }),
                    Ui.Btn(L.Tr("打开数据文件夹", "開啟資料資料夾"), () => Ui.Open(SettingsStore.Dir)),
                    Ui.Btn(L.Tr("恢复默认设置", "恢復預設設定"), ResetAll))),
            Ui.Buttons(Ui.Link(L.Tr("字体与图标许可", "字型與圖示授權"), () => Ui.Open(Directory.Exists(licenses) ? licenses : AppContext.BaseDirectory))),
            copyright);
    }

    private void ResetAll()
    {
        if (MessageBox.Show(this, L.Tr("所有设置都会恢复默认，确定吗？", "所有設定都會恢復預設，確定嗎？"), Title,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var hadScreensaver = S.ScreensaverInstalled;
        if (hadScreensaver) ScreenSaver.Uninstall();
        SettingsStore.Reset();
        L.Refresh(S.Language);
        Theme.Apply();
        AutoStart.Sync();
        var widget = S.WidgetEnabled;
        App.Instance.Widget?.Close();
        App.Instance.ShowWidget(widget);
        App.Fire(async () =>
        {
            await Bing.RefreshAsync(true).ConfigureAwait(false);
            await WeatherService.RefreshAsync().ConfigureAwait(false);
        });
        _page = "home";
        Build();
    }
}
