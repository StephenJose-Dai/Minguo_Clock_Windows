using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguo.Core;

/// <summary>所有设置，存在 %APPDATA%\MinguoCalendar\settings.json</summary>
public sealed class AppSettings
{
    // ---------- 通用 ----------
    /// <summary>0 跟随系统，1 简体，2 繁體</summary>
    public int Language { get; set; }
    /// <summary>0 暗黑（默认，黑金），1 白天，2 自动（6:00–18:00 白天）</summary>
    public int ThemeMode { get; set; }
    public bool FullEra { get; set; }
    public bool ChineseNumerals { get; set; }
    public bool ShowSeconds { get; set; } = true;
    /// <summary>null = 跟随系统</summary>
    public bool? Use24Hour { get; set; }
    public bool StartWithWindows { get; set; } = true;
    /// <summary>字体：default / kai / round / iansui / orbitron / mono</summary>
    public string FontId { get; set; } = "default";
    /// <summary>签名（显示在屏保和锁屏背景上）</summary>
    public string Signature { get; set; } = "";

    // ---------- 桌面小组件 ----------
    public bool WidgetEnabled { get; set; } = true;
    public double WidgetX { get; set; } = double.NaN;
    public double WidgetY { get; set; } = double.NaN;
    public double WidgetWidth { get; set; } = 420;
    public double WidgetHeight { get; set; } = 300;
    /// <summary>0 白字，1 深色字（背景始终透明）</summary>
    public int WidgetTextStyle { get; set; }
    public bool WidgetShowWeather { get; set; } = true;
    public bool WidgetShowQuote { get; set; } = true;
    public bool WidgetLocked { get; set; }

    // ---------- 壁纸（屏保、锁屏背景用） ----------
    /// <summary>0 主题渐变，1 自选图片，2 必应壁纸轮换</summary>
    public int WallpaperMode { get; set; } = 2;
    public string CustomWallpaper { get; set; } = "";
    /// <summary>0..70，壁纸暗化百分比</summary>
    public int WallpaperDim { get; set; } = 25;
    public int ThemeIndex { get; set; }
    public List<string> BingList { get; set; } = [];
    public string BingListDate { get; set; } = "";
    public int BingIndex { get; set; } = -1;
    public DateTime BingChangedAt { get; set; }

    // ---------- 锁屏背景 ----------
    public bool LockImageEnabled { get; set; }
    public DateTime LockImageSetAt { get; set; }
    /// <summary>锁屏图各块的位置（设计坐标 1600×900）：块名 → [右边缘 x, 上边缘 y]；没有的块用默认位置</summary>
    public Dictionary<string, double[]> LockLayout { get; set; } = new();

    // ---------- 屏幕保护程序 ----------
    public bool ScreensaverInstalled { get; set; }
    public int ScreensaverTimeoutMinutes { get; set; } = 5;

    // ---------- 天气 ----------
    public bool WeatherEnabled { get; set; } = true;
    public bool AutoLocate { get; set; } = true;
    public double Lat { get; set; } = double.NaN;
    public double Lon { get; set; } = double.NaN;
    public string CityName { get; set; } = "";
    public string PlaceCountry { get; set; } = "";
    public string PlaceProvince { get; set; } = "";
    public string PlaceCity { get; set; } = "";
    public string PlaceDistrict { get; set; } = "";
    public DateTime LocatedAt { get; set; }
    public string LocSource { get; set; } = "";
    /// <summary>天气接口 Key（用户自己填的，优先于 AppConfig 里的公共接口）</summary>
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public WeatherData? WeatherCache { get; set; }

    // ---------- 名言 ----------
    public string QuoteText { get; set; } = "";
    public DateTime QuoteChangedAt { get; set; }
    public List<int> QuoteDeck { get; set; } = [];
    public List<string> QuoteSeen { get; set; } = [];

    [JsonIgnore]
    public DisplayStyle Style => new(L.Current, FullEra, ChineseNumerals);

    [JsonIgnore]
    public bool HasLocation => !double.IsNaN(Lat) && !double.IsNaN(Lon);
}

/// <summary>读写设置（线程安全，写入时先写临时文件再替换，避免写坏）</summary>
public static class SettingsStore
{
    private static readonly object Gate = new();
    private static AppSettings? _current;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>设置目录：%APPDATA%\MinguoCalendar</summary>
    public static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinguoCalendar");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>缓存目录：%LOCALAPPDATA%\MinguoCalendar（必应图片、生成的锁屏图）</summary>
    public static string CacheDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinguoCalendar");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Current
    {
        get
        {
            lock (Gate)
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(FilePath))
                        _current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                }
                catch
                {
                    // 设置文件损坏：用默认设置
                }
                return _current ??= new AppSettings();
            }
        }
    }

    /// <summary>设置变化时通知界面</summary>
    public static event Action? Saved;

    public static void Save()
    {
        lock (Gate)
        {
            var s = Current;
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        Saved?.Invoke();
    }

    /// <summary>重置：删除设置和缓存</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _current = new AppSettings();
            try { File.Delete(FilePath); } catch { /* 忽略 */ }
            try { Directory.Delete(CacheDir, recursive: true); } catch { /* 忽略 */ }
        }
        Save();
    }
}
