using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Minguo.Core;

/// <summary>统一的天气现象（中国气象局代码、WMO 代码都映射到这里）</summary>
public enum Cond
{
    Clear, MostlyClear, Partly, Overcast, Fog, Haze, Dust, Sand,
    Drizzle, LightRain, Rain, HeavyRain, StormRain, Shower, FreezingRain, Sleet,
    LightSnow, Snow, HeavySnow, Blizzard, SnowShower, Thunder, Hail, Wind, Unknown,
}

public static class CondInfo
{
    private static readonly Dictionary<Cond, (string Hans, string Hant)> Names = new()
    {
        [Cond.Clear] = ("晴", "晴"), [Cond.MostlyClear] = ("晴间多云", "晴間多雲"), [Cond.Partly] = ("多云", "多雲"),
        [Cond.Overcast] = ("阴", "陰"), [Cond.Fog] = ("雾", "霧"), [Cond.Haze] = ("霾", "霾"), [Cond.Dust] = ("浮尘", "浮塵"),
        [Cond.Sand] = ("沙尘暴", "沙塵暴"), [Cond.Drizzle] = ("毛毛雨", "毛毛雨"), [Cond.LightRain] = ("小雨", "小雨"),
        [Cond.Rain] = ("中雨", "中雨"), [Cond.HeavyRain] = ("大雨", "大雨"), [Cond.StormRain] = ("暴雨", "暴雨"),
        [Cond.Shower] = ("阵雨", "陣雨"), [Cond.FreezingRain] = ("冻雨", "凍雨"), [Cond.Sleet] = ("雨夹雪", "雨夾雪"),
        [Cond.LightSnow] = ("小雪", "小雪"), [Cond.Snow] = ("中雪", "中雪"), [Cond.HeavySnow] = ("大雪", "大雪"),
        [Cond.Blizzard] = ("暴雪", "暴雪"), [Cond.SnowShower] = ("阵雪", "陣雪"), [Cond.Thunder] = ("雷阵雨", "雷陣雨"),
        [Cond.Hail] = ("冰雹", "冰雹"), [Cond.Wind] = ("大风", "大風"), [Cond.Unknown] = ("--", "--"),
    };

    public static string Label(this Cond c) => L.Tr(Names[c].Hans, Names[c].Hant);

    public static bool IsRain(this Cond c) => c is Cond.Drizzle or Cond.LightRain or Cond.Rain or Cond.HeavyRain or Cond.StormRain
        or Cond.Shower or Cond.FreezingRain or Cond.Thunder or Cond.Sleet or Cond.Hail;

    public static bool IsSnow(this Cond c) => c is Cond.Sleet or Cond.LightSnow or Cond.Snow or Cond.HeavySnow or Cond.Blizzard or Cond.SnowShower;

    /// <summary>图标名：sun / moon / partly / cloud / fog / rain / snow / thunder</summary>
    public static string Icon(this Cond c, bool isDay = true) => c switch
    {
        Cond.Clear => isDay ? "sun" : "moon",
        Cond.MostlyClear => isDay ? "partly" : "moon",
        Cond.Partly => isDay ? "partly" : "cloud",
        Cond.Fog or Cond.Haze or Cond.Dust or Cond.Sand => "fog",
        Cond.Thunder or Cond.Hail => "thunder",
        _ when c.IsSnow() => "snow",
        _ when c.IsRain() => "rain",
        _ => "cloud",
    };

    public static Cond FromWmo(int code) => code switch
    {
        0 => Cond.Clear, 1 => Cond.MostlyClear, 2 => Cond.Partly, 3 => Cond.Overcast,
        45 or 48 => Cond.Fog, 51 or 53 or 55 => Cond.Drizzle, 56 or 57 or 66 or 67 => Cond.FreezingRain,
        61 => Cond.LightRain, 63 => Cond.Rain, 65 or 82 => Cond.HeavyRain,
        71 or 77 => Cond.LightSnow, 73 => Cond.Snow, 75 => Cond.HeavySnow,
        80 or 81 => Cond.Shower, 85 or 86 => Cond.SnowShower, 95 => Cond.Thunder, 96 or 99 => Cond.Hail,
        _ => Cond.Unknown,
    };

    /// <summary>中国气象局天气现象代码（腾讯天气通用）</summary>
    public static Cond FromCma(string code) => (int.TryParse(code.Trim(), out var n) ? n : -1) switch
    {
        0 => Cond.Clear, 1 => Cond.Partly, 2 => Cond.Overcast, 3 => Cond.Shower, 4 => Cond.Thunder, 5 => Cond.Hail,
        6 => Cond.Sleet, 7 => Cond.LightRain, 8 or 21 => Cond.Rain, 9 or 22 => Cond.HeavyRain,
        10 or 11 or 12 or 23 or 24 or 25 => Cond.StormRain, 13 => Cond.SnowShower, 14 => Cond.LightSnow,
        15 or 26 => Cond.Snow, 16 or 27 => Cond.HeavySnow, 17 or 28 => Cond.Blizzard,
        18 or 32 or 49 or 57 or 58 => Cond.Fog, 19 => Cond.FreezingRain, 20 or 31 => Cond.Sand,
        29 or 30 => Cond.Dust, 53 or 54 or 55 or 56 => Cond.Haze, 301 => Cond.Rain, 302 => Cond.Snow,
        _ => Cond.Unknown,
    };
}

public sealed record DayForecast(DateTime Date, Cond Cond, double Max, double Min, int? RainChance);

public sealed record WeatherData
{
    public string City { get; init; } = "";
    public double Temp { get; init; } = double.NaN;
    public double FeelsLike { get; init; } = double.NaN;
    public int Humidity { get; init; } = -1;
    public double WindKmh { get; init; } = double.NaN;
    public Cond Cond { get; init; } = Cond.Unknown;
    public bool IsDay { get; init; } = true;
    public List<DayForecast> Days { get; init; } = [];
    public int? Aqi { get; init; }
    public double UvMax { get; init; } = double.NaN;
    public DateTime FetchedAt { get; init; }
    /// <summary>短时预报一句话（如“约 2 小时后可能有降水”）</summary>
    public string Keypoint { get; init; } = "";
    /// <summary>生活指数（腾讯：穿衣、雨伞、紫外线、感冒…）</summary>
    public List<string> LifeIndex { get; init; } = [];
    public List<string> Alerts { get; init; } = [];

    public DayForecast? Today => Days.FirstOrDefault(d => d.Date.Date == DateTime.Today);
}

public sealed record CityResult(string Name, string Region, double Lat, double Lon, string Country, string Admin1, string Admin2);

/// <summary>
/// 天气：
/// - 位置在中国大陆：腾讯天气（免注册，中央气象台数据），含逐日、逐小时降水、生活指数、预警、空气质量；
/// - 境外或腾讯不可用：Open-Meteo（免注册），含未来 2 小时降水。
/// 自动定位：系统定位（Windows 定位服务）→ IP 兜底；成功后 30 分钟内不重复定位，失败 5 分钟后重试。
/// </summary>
public static partial class WeatherService
{
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Relocate = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);
    private static DateTime _lastLocateAttempt = DateTime.MinValue;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>系统定位（Windows 端注入）：返回 (纬度, 经度, 来源, 精度米)</summary>
    public static Func<CancellationToken, Task<(double Lat, double Lon, string Source, double Accuracy)?>>? DeviceLocator { get; set; }

    /// <summary>天气更新后通知界面</summary>
    public static event Action? Updated;

    public static WeatherData? Current => SettingsStore.Current.WeatherCache;

    public static bool NeedsRefresh()
    {
        var s = SettingsStore.Current;
        if (!s.WeatherEnabled) return false;
        var w = s.WeatherCache;
        var now = DateTime.Now;
        var locDue = s.AutoLocate && (!s.HasLocation || now - s.LocatedAt > Relocate) && now - _lastLocateAttempt > Retry;
        return w == null || now - w.FetchedAt > Stale || locDue;
    }

    public static async Task RefreshIfStaleAsync()
    {
        if (NeedsRefresh()) await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>定位一次（不受频率限制，“定位”按钮用）。成功返回地名</summary>
    public static async Task<string?> LocateNowAsync(CancellationToken ct = default)
    {
        _lastLocateAttempt = DateTime.Now;
        var s = SettingsStore.Current;
        (double Lat, double Lon, string Source, double Accuracy)? loc = null;
        if (DeviceLocator != null)
        {
            try { loc = await DeviceLocator(ct).ConfigureAwait(false); } catch { loc = null; }
        }
        Place? place;
        if (loc is { } l)
        {
            place = await ReverseBounded(l.Lat, l.Lon).ConfigureAwait(false);
            SetLocation(place?.Label ?? s.CityName, l.Lat, l.Lon, place);
            s.LocSource = l.Source;
        }
        else
        {
            var ip = await Geo.IpFallbackAsync().ConfigureAwait(false);
            if (ip == null) return null;
            place = ip.Value.Place;
            SetLocation(place.Label, ip.Value.Lat, ip.Value.Lon, place);
            s.LocSource = "ip";
        }
        s.LocatedAt = DateTime.Now;
        SettingsStore.Save();
        return s.CityName;
    }

    private static async Task<Place?> ReverseBounded(double lat, double lon)
    {
        var t = Geo.ReverseAsync(lat, lon);
        var done = await Task.WhenAny(t, Task.Delay(8_000)).ConfigureAwait(false);
        return done == t ? await t.ConfigureAwait(false) : Geo.Offline(lat, lon);
    }

    public static void SetLocation(string name, double lat, double lon, Place? place)
    {
        var s = SettingsStore.Current;
        s.CityName = name;
        s.Lat = lat;
        s.Lon = lon;
        s.PlaceCountry = place?.Country ?? "";
        s.PlaceProvince = place?.Province ?? "";
        s.PlaceCity = place?.City ?? "";
        s.PlaceDistrict = place?.District ?? "";
        SettingsStore.Save();
    }

    public static async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = SettingsStore.Current;
            if (!s.WeatherEnabled) return false;
            var now = DateTime.Now;

            // 1. 自动定位
            if (s.AutoLocate && (!s.HasLocation || now - s.LocatedAt > Relocate) && now - _lastLocateAttempt > Retry)
                await LocateNowAsync(ct).ConfigureAwait(false);

            if (!s.HasLocation) return false;

            // 2. 地名
            if (string.IsNullOrEmpty(s.PlaceCountry))
            {
                var p = await ReverseBounded(s.Lat, s.Lon).ConfigureAwait(false);
                if (p != null)
                {
                    s.PlaceCountry = p.Country; s.PlaceProvince = p.Province; s.PlaceCity = p.City; s.PlaceDistrict = p.District;
                    if (string.IsNullOrEmpty(s.CityName)) s.CityName = p.Label;
                }
            }

            // 3. 预报：大陆 → 腾讯；否则 / 失败 → Open-Meteo
            WeatherData? w = null;
            if (s.PlaceCountry == "CN" && s.PlaceProvince.Length > 0)
            {
                try { w = await FetchTencentAsync(s.PlaceProvince, s.PlaceCity, s.PlaceDistrict, s.CityName, ct).ConfigureAwait(false); }
                catch { w = null; }
            }
            if (w == null)
            {
                try { w = await FetchOpenMeteoAsync(s.Lat, s.Lon, s.CityName, ct).ConfigureAwait(false); }
                catch { w = null; }
            }
            if (w == null) return false;
            s.WeatherCache = w;
            SettingsStore.Save();
            Updated?.Invoke();
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    // ---------------- Open-Meteo ----------------

    private static double D(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.NaN;

    private static int? I(JsonElement e) => e.ValueKind == JsonValueKind.Number ? (int)Math.Round(e.GetDouble()) : null;

    public static async Task<WeatherData> FetchOpenMeteoAsync(double lat, double lon, string city, CancellationToken ct = default)
    {
        var url = string.Format(CultureInfo.InvariantCulture,
            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
            "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,uv_index_max" +
            "&minutely_15=precipitation&forecast_minutely_15=8&timezone=auto&forecast_days=4", lat, lon);
        using var doc = JsonDocument.Parse(await Net.GetStringAsync(url, ct: ct).ConfigureAwait(false));
        var o = doc.RootElement;
        var cur = o.GetProperty("current");
        var daily = o.GetProperty("daily");
        var times = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var maxs = daily.GetProperty("temperature_2m_max");
        var mins = daily.GetProperty("temperature_2m_min");
        daily.TryGetProperty("precipitation_probability_max", out var pops);
        daily.TryGetProperty("uv_index_max", out var uvs);
        var days = new List<DayForecast>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            days.Add(new DayForecast(
                DateTime.ParseExact(times[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                CondInfo.FromWmo(I(codes[i]) ?? -1), D(maxs[i]), D(mins[i]),
                pops.ValueKind == JsonValueKind.Array ? I(pops[i]) : null));
        }

        int? aqi = null;
        try
        {
            var aqUrl = string.Format(CultureInfo.InvariantCulture,
                "https://air-quality-api.open-meteo.com/v1/air-quality?latitude={0}&longitude={1}&current=us_aqi", lat, lon);
            using var aq = JsonDocument.Parse(await Net.GetStringAsync(aqUrl, timeoutMs: 6_000, ct: ct).ConfigureAwait(false));
            aqi = I(aq.RootElement.GetProperty("current").GetProperty("us_aqi"));
        }
        catch
        {
            // 空气质量拿不到不影响天气
        }

        // 未来 2 小时（8 个 15 分钟）降水 → 一句话
        var keypoint = "";
        if (o.TryGetProperty("minutely_15", out var m15) && m15.TryGetProperty("precipitation", out var pr))
        {
            var values = pr.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0).ToList();
            var first = values.FindIndex(v => v >= 0.1);
            keypoint = values.Count == 0 ? "" :
                first < 0 ? L.Tr("未来两小时预计无降水", "未來兩小時預計無降雨") :
                first == 0 ? L.Tr("当前有降水", "目前有降雨") :
                L.Tr($"约 {first * 15} 分钟后可能开始下雨", $"約 {first * 15} 分鐘後可能開始下雨");
        }

        return new WeatherData
        {
            City = city,
            Temp = D(cur.GetProperty("temperature_2m")),
            FeelsLike = D(cur.GetProperty("apparent_temperature")),
            Humidity = I(cur.GetProperty("relative_humidity_2m")) ?? -1,
            WindKmh = D(cur.GetProperty("wind_speed_10m")),
            Cond = CondInfo.FromWmo(I(cur.GetProperty("weather_code")) ?? -1),
            IsDay = (I(cur.GetProperty("is_day")) ?? 1) == 1,
            Days = days,
            Aqi = aqi,
            UvMax = uvs.ValueKind == JsonValueKind.Array && uvs.GetArrayLength() > 0 ? D(uvs[0]) : double.NaN,
            FetchedAt = DateTime.Now,
            Keypoint = keypoint,
        };
    }

    // ---------------- 腾讯天气（免注册） ----------------

    [GeneratedRegex("(壮族|回族|维吾尔)?自治区$|特别行政区$|省$|市$")]
    private static partial Regex SuffixRegex();

    /// <summary>去掉行政区后缀：福建省→福建，福州市→福州</summary>
    private static string ShortName(string s) => SuffixRegex().Replace(s, "");

    private static string S(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    private static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

    public static async Task<WeatherData> FetchTencentAsync(string province, string city, string district, string label, CancellationToken ct = default)
    {
        async Task<JsonDocument?> Call(string pv, string c, string ds)
        {
            var url = "https://wis.qq.com/weather/common?source=pc&weather_type=" +
                      Uri.EscapeDataString("observe|forecast_1h|forecast_24h|index|alarm|tips|air") +
                      "&province=" + Uri.EscapeDataString(pv) + "&city=" + Uri.EscapeDataString(c) +
                      (ds.Length > 0 ? "&county=" + Uri.EscapeDataString(ds) : "");
            var body = await Net.GetStringAsync(url, new Dictionary<string, string> { ["Referer"] = "https://tianqi.qq.com/" }, ct: ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("observe", out var obs) && S(obs, "degree").Length > 0)
                return doc;
            doc.Dispose();
            return null;
        }

        var ctName = city.Length > 0 ? city : province;
        using var doc = await Call(province, ctName, district).ConfigureAwait(false)
                        ?? await Call(province, ctName, "").ConfigureAwait(false)
                        ?? await Call(ShortName(province), ShortName(ctName), "").ConfigureAwait(false)
                        ?? throw new InvalidOperationException("腾讯天气无数据");
        var data = doc.RootElement.GetProperty("data");
        var obs = data.GetProperty("observe");
        var nowCond = CondInfo.FromCma(S(obs, "weather_code"));
        var today = DateTime.Today;

        // 逐日：forecast_24h 的 "0" 是昨天，只保留今天及以后
        var days = new List<DayForecast>();
        if (data.TryGetProperty("forecast_24h", out var f24) && f24.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in f24.EnumerateObject())
            {
                if (!DateTime.TryParseExact(S(p.Value, "time"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (date < today) continue;
                days.Add(new DayForecast(date, CondInfo.FromCma(S(p.Value, "day_weather_code")), Num(S(p.Value, "max_degree")), Num(S(p.Value, "min_degree")), null));
            }
        }
        days.Sort((a, b) => a.Date.CompareTo(b.Date));

        // 逐小时：接下来 6 小时会不会下雨
        var hours = new List<JsonElement>();
        if (data.TryGetProperty("forecast_1h", out var f1h) && f1h.ValueKind == JsonValueKind.Object)
            hours.AddRange(f1h.EnumerateObject().Select(p => p.Value));
        var nowStamp = DateTime.Now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        var upcoming = hours.Where(h => string.CompareOrdinal(S(h, "update_time").PadRight(10)[..10], nowStamp) > 0)
            .OrderBy(h => S(h, "update_time")).Take(6).ToList();
        var rainAt = upcoming.FindIndex(h => { var c = CondInfo.FromCma(S(h, "weather_code")); return c.IsRain() || c.IsSnow(); });
        var keypoint = nowCond.IsRain() || nowCond.IsSnow() ? L.Tr("当前有降水", "目前有降雨") :
            rainAt >= 0 ? L.Tr($"约 {rainAt + 1} 小时后可能有降水", $"約 {rainAt + 1} 小時後可能有降雨") :
            upcoming.Count > 0 ? L.Tr("未来 6 小时无降水", "未來 6 小時無降雨") : "";

        // 生活指数
        var life = new List<string>();
        if (data.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "clothes", "umbrella", "ultraviolet", "cold", "comfort", "carwash" })
            {
                if (!idx.TryGetProperty(key, out var o)) continue;
                var name = S(o, "name").Replace("指数", "");
                var info = S(o, "info");
                if (name.Length > 0 && info.Length > 0) life.Add(L.Zh($"{name}：{info}"));
            }
        }

        var alerts = new List<string>();
        if (data.TryGetProperty("alarm", out var alarm) && alarm.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in alarm.EnumerateObject())
            {
                var t = S(a.Value, "type_name");
                if (t.Length > 0) alerts.Add(L.Zh(t + S(a.Value, "level_name") + "预警"));
            }
        }

        int? aqi = null;
        if (data.TryGetProperty("air", out var air))
        {
            var v = Num(S(air, "aqi"));
            if (!double.IsNaN(v)) aqi = (int)Math.Round(v);
        }

        var hour = DateTime.Now.Hour;
        return new WeatherData
        {
            City = L.Zh(label),
            Temp = Num(S(obs, "degree")),
            Humidity = int.TryParse(S(obs, "humidity"), out var hum) ? hum : -1,
            Cond = nowCond,
            IsDay = hour is >= 6 and <= 18,
            Days = days,
            Aqi = aqi,
            FetchedAt = DateTime.Now,
            Keypoint = keypoint,
            LifeIndex = life,
            Alerts = alerts,
        };
    }

    // ---------------- 城市搜索 ----------------

    public static async Task<List<CityResult>> SearchCityAsync(string query)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query.Trim()) + "&count=10&language=zh&format=json";
        using var doc = JsonDocument.Parse(await Net.GetStringAsync(url).ConfigureAwait(false));
        var list = new List<CityResult>();
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return list;
        foreach (var o in arr.EnumerateArray())
        {
            var region = string.Join(" · ", new[] { S(o, "admin1"), S(o, "country") }.Where(x => x.Length > 0).Distinct());
            list.Add(new CityResult(S(o, "name"), region, o.GetProperty("latitude").GetDouble(), o.GetProperty("longitude").GetDouble(),
                S(o, "country_code").ToUpperInvariant(), S(o, "admin1"), S(o, "admin2")));
        }
        return list;
    }

    public static Place PlaceOf(CityResult c) =>
        new(c.Country, c.Admin1, string.IsNullOrEmpty(c.Admin2) ? c.Name : c.Admin2, string.IsNullOrEmpty(c.Admin2) ? "" : c.Name);

    // ---------------- 显示 ----------------

    public static string Temp(double t) => double.IsNaN(t) ? "--°" : $"{Math.Round(t):0}°";

    /// <summary>城市名：空格换成不换行空格，避免“漳州市 芗城区”被拆到两行</summary>
    public static string CityLabel(WeatherData w) =>
        (string.IsNullOrWhiteSpace(w.City) ? L.Tr("当前位置", "目前位置") : w.City).Replace(' ', ' ');

    /// <summary>多云 18°  15°/22°</summary>
    public static string Summary(WeatherData w)
    {
        var t = w.Today;
        var range = t == null ? "" : $"  {Temp(t.Min)}/{Temp(t.Max)}";
        return $"{w.Cond.Label()} {Temp(w.Temp)}{range}";
    }

    public static string AqiLabel(int aqi) => aqi switch
    {
        <= 50 => L.Tr("优", "優"),
        <= 100 => L.Tr("良", "良"),
        <= 150 => L.Tr("轻度污染", "輕度污染"),
        <= 200 => L.Tr("中度污染", "中度污染"),
        <= 300 => L.Tr("重度污染", "重度污染"),
        _ => L.Tr("严重污染", "嚴重污染"),
    };

    private static string Clothing(double t) => t switch
    {
        < 5 => L.Tr("天冷，穿羽绒服或厚外套", "天冷，穿羽絨衣或厚外套"),
        < 12 => L.Tr("较冷，毛衣加外套", "偏冷，毛衣加外套"),
        < 18 => L.Tr("微凉，带件外套", "微涼，帶件外套"),
        < 25 => L.Tr("舒适，长袖或薄外套", "舒適，長袖或薄外套"),
        < 30 => L.Tr("较热，短袖为宜", "偏熱，短袖為宜"),
        _ => L.Tr("炎热，注意防暑多喝水", "炎熱，注意防暑多喝水"),
    };

    /// <summary>生活建议</summary>
    public static List<string> Advice(WeatherData w)
    {
        var o = new List<string>();
        o.AddRange(w.Alerts.Select(a => "⚠ " + a));
        if (w.Keypoint.Length > 0) o.Add(w.Keypoint);
        var today = w.Today;
        var rain = today?.RainChance ?? 0;
        var todayRain = w.Cond.IsRain() || today?.Cond.IsRain() == true;
        if (w.Cond is Cond.Thunder or Cond.Hail)
            o.Add(L.Tr("有雷雨，尽量待在室内，远离高处和大树", "有雷雨，盡量待在室內，遠離高處和大樹"));
        if ((todayRain && rain >= 40) || w.Cond.IsRain())
            o.Add(L.Tr("今天有雨，出门记得带伞", "今天有雨，出門記得帶傘"));
        else if (rain >= 30)
            o.Add(L.Tr("可能有阵雨，建议备把伞", "可能有陣雨，建議備把傘"));
        if (w.Cond.IsSnow() || today?.Cond.IsSnow() == true)
            o.Add(L.Tr("有雪，路面湿滑，注意出行安全", "有雪，路面濕滑，注意出行安全"));
        if (w.Aqi is > 150) o.Add(L.Tr("空气较差，减少户外活动，外出戴口罩", "空氣較差，減少戶外活動，外出戴口罩"));
        else if (w.Aqi is > 100) o.Add(L.Tr("空气一般，敏感人群减少户外活动", "空氣普通，敏感族群減少戶外活動"));
        if (w.Cond is Cond.Fog or Cond.Haze or Cond.Sand or Cond.Dust)
            o.Add(L.Tr("能见度低，开车注意减速慢行", "能見度低，開車注意減速慢行"));
        var t = !double.IsNaN(w.Temp) ? w.Temp : today?.Max ?? double.NaN;
        if (w.LifeIndex.Count > 0) o.AddRange(w.LifeIndex);
        else if (!double.IsNaN(t)) o.Add(Clothing(t));
        if (today != null && today.Max - today.Min >= 10)
            o.Add(L.Tr("昼夜温差大，注意增减衣物", "晝夜溫差大，注意增減衣物"));
        if (w.LifeIndex.Count == 0)
        {
            if (w.UvMax >= 8) o.Add(L.Tr("紫外线很强，外出注意防晒", "紫外線很強，外出注意防曬"));
            else if (w.UvMax >= 6) o.Add(L.Tr("紫外线较强，适当防晒", "紫外線較強，適當防曬"));
        }
        if (w.WindKmh >= 38) o.Add(L.Tr("风力较大，注意高空坠物", "風力較大，注意高空墜物"));
        return o;
    }
}
