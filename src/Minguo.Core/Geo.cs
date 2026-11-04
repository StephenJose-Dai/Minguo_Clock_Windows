using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Minguo.Core;

/// <summary>country 为 ISO 国家码：中国大陆 CN，台湾 TW，香港 HK，澳门 MO</summary>
public sealed record Place(string Country, string Province, string City, string District)
{
    /// <summary>显示用：福州市 鼓楼区</summary>
    public string Label
    {
        get
        {
            var parts = new List<string>();
            var first = string.IsNullOrWhiteSpace(City) ? Province : City;
            if (!string.IsNullOrWhiteSpace(first)) parts.Add(first);
            if (!string.IsNullOrWhiteSpace(District) && District != first) parts.Add(District);
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// 经纬度 → 省市区：
/// 1. 中国大陆：有天气接口 Key 就先用（逆地理编码，准到街道），否则/失败用内置离线区县表（全国 2890 个区县中心坐标）；
/// 2. 港澳台、海外：BigDataCloud（免注册）；
/// 3. 拿不到坐标时：按网络 IP（myip.ipip.net）大致定位到城市。
/// </summary>
public static class Geo
{
    private sealed record Row(string Province, string City, string District, double Lat, double Lon);

    private static List<Row>? _rows;

    private static List<Row> Table()
    {
        if (_rows != null) return _rows;
        var list = new List<Row>(3000);
        try
        {
            using var s = typeof(Geo).Assembly.GetManifestResourceStream("cn_districts.tsv")!;
            using var r = new StreamReader(s, Encoding.UTF8);
            string[] provinces = [];
            while (r.ReadLine() is { } line)
            {
                if (line.StartsWith('#')) { provinces = line[1..].Split('|'); continue; }
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                if (f.Length < 5) continue;
                list.Add(new Row(provinces[int.Parse(f[0], CultureInfo.InvariantCulture)], f[1], f[2],
                    double.Parse(f[3], CultureInfo.InvariantCulture), double.Parse(f[4], CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
            // 资源读取失败时返回空表
        }
        return _rows = list;
    }

    private static string CountryOf(string province) =>
        province.StartsWith("台湾", StringComparison.Ordinal) ? "TW" :
        province.StartsWith("香港", StringComparison.Ordinal) ? "HK" :
        province.StartsWith("澳门", StringComparison.Ordinal) ? "MO" : "CN";

    /// <summary>台湾本岛 + 澎湖的大致范围（离福建很近，不能只看“最近的区县”）</summary>
    private static bool InTaiwan(double lat, double lon) => lat is >= 21.8 and <= 25.4 && lon is >= 119.3 and <= 122.1;

    private static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        var x = (lon2 - lon1) * Math.Cos((lat1 + lat2) / 2 * Math.PI / 180);
        var y = lat2 - lat1;
        return Math.Sqrt(x * x + y * y) * 111.2;
    }

    /// <summary>离线：最近的区县在 150 公里内才算（再远就是境外）</summary>
    public static Place? Offline(double lat, double lon)
    {
        if (InTaiwan(lat, lon)) return new Place("TW", "台湾省", "", "");
        Row? best = null;
        var bestKm = double.MaxValue;
        foreach (var r in Table())
        {
            var d = Km(lat, lon, r.Lat, r.Lon);
            if (d < bestKm) { bestKm = d; best = r; }
        }
        if (best == null || bestKm > 150) return null;
        return new Place(CountryOf(best.Province), best.Province, best.City, best.District);
    }

    /// <summary>上次地名来源：api / offline / bigdatacloud / ip</summary>
    public static string LastSource { get; private set; } = "";

    public static async Task<Place?> ReverseAsync(double lat, double lon)
    {
        var off = Offline(lat, lon);
        if (off is { Country: "CN" })
        {
            var a = await ApiAsync(lat, lon).ConfigureAwait(false);
            if (a.Place != null) { LastSource = "api"; return a.Place; }
            LastSource = "offline";
            return off;
        }
        var online = await BigDataCloudAsync(lat, lon).ConfigureAwait(false);
        if (online != null) { LastSource = "bigdatacloud"; return online; }
        LastSource = "offline";
        return off;
    }

    // ---------------- 天气接口（逆地理编码） ----------------

    public sealed record ApiResult(Place? Place, string Address, string? Error);

    /// <summary>当前生效的接口配置：用户自己填的优先，否则用 AppConfig 里的公共接口</summary>
    public static (string Key, string Secret, string Url, bool Own) ApiConfig()
    {
        var s = SettingsStore.Current;
        var own = !string.IsNullOrWhiteSpace(s.ApiKey);
        var key = own ? s.ApiKey.Trim() : AppConfig.ApiKey.Trim();
        var secret = own ? s.ApiSecret.Trim() : AppConfig.ApiSecret.Trim();
        var url = string.IsNullOrWhiteSpace(s.ApiUrl) ? AppConfig.ApiUrl : s.ApiUrl.Trim();
        return (key, secret, url, own);
    }

    public static async Task<ApiResult> ApiAsync(double lat, double lon)
    {
        var (key, secret, url, _) = ApiConfig();
        if (string.IsNullOrEmpty(key)) return new ApiResult(null, "", L.Tr("未填写 Key", "未填寫 Key"));
        try
        {
            var (glat, glon) = Wgs84ToGcj02(lat, lon);
            var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = key,
                ["location"] = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6}", glon, glat),
                ["extensions"] = "base",
                ["output"] = "JSON",
            };
            var query = string.Join("&", p.Select(kv => kv.Key + "=" + kv.Value));
            // 开了数字签名：sig = MD5(按参数名排序后的“参数=值&…” + 私钥)
            var sig = secret.Length > 0 ? "&sig=" + Md5(query + secret) : "";
            var sep = url.Contains('?') ? "&" : "?";
            using var doc = JsonDocument.Parse(await Net.GetStringAsync(url + sep + query + sig, timeoutMs: 5_000).ConfigureAwait(false));
            var o = doc.RootElement;
            if (Str(o, "status") != "1")
                return new ApiResult(null, "", Str(o, "info") + " (" + Str(o, "infocode") + ")");
            if (!o.TryGetProperty("regeocode", out var rg) || !rg.TryGetProperty("addressComponent", out var ac))
                return new ApiResult(null, "", L.Tr("没有结果", "沒有結果"));
            // 没有值时返回空数组 []
            var province = Str(ac, "province");
            if (province.Length == 0) return new ApiResult(null, Str(rg, "formatted_address"), L.Tr("坐标不在中国境内", "座標不在中國境內"));
            var city = Str(ac, "city");
            if (city.Length == 0) city = province; // 直辖市 city 为空
            return new ApiResult(new Place(CountryOf(province), province, city, Str(ac, "district")), Str(rg, "formatted_address"), null);
        }
        catch (Exception e)
        {
            return new ApiResult(null, "", e.GetType().Name + ": " + e.Message);
        }
    }

    private static string Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    /// <summary>WGS-84（GPS 国际标准）→ GCJ-02（国内地图坐标）。国内两者相差几百米；境外不换算。</summary>
    public static (double Lat, double Lon) Wgs84ToGcj02(double lat, double lon)
    {
        if (lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271) return (lat, lon);
        const double a = 6378245.0;
        const double ee = 0.00669342162296594323;
        static double TLat(double x, double y)
        {
            var r = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
            r += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
            r += (20.0 * Math.Sin(y * Math.PI) + 40.0 * Math.Sin(y / 3.0 * Math.PI)) * 2.0 / 3.0;
            r += (160.0 * Math.Sin(y / 12.0 * Math.PI) + 320 * Math.Sin(y * Math.PI / 30.0)) * 2.0 / 3.0;
            return r;
        }
        static double TLon(double x, double y)
        {
            var r = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
            r += (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0;
            r += (20.0 * Math.Sin(x * Math.PI) + 40.0 * Math.Sin(x / 3.0 * Math.PI)) * 2.0 / 3.0;
            r += (150.0 * Math.Sin(x / 12.0 * Math.PI) + 300.0 * Math.Sin(x / 30.0 * Math.PI)) * 2.0 / 3.0;
            return r;
        }
        var dLat = TLat(lon - 105.0, lat - 35.0);
        var dLon = TLon(lon - 105.0, lat - 35.0);
        var radLat = lat / 180.0 * Math.PI;
        var magic = Math.Sin(radLat);
        magic = 1 - ee * magic * magic;
        var sqrtMagic = Math.Sqrt(magic);
        dLat = dLat * 180.0 / (a * (1 - ee) / (magic * sqrtMagic) * Math.PI);
        dLon = dLon * 180.0 / (a / sqrtMagic * Math.Cos(radLat) * Math.PI);
        return (lat + dLat, lon + dLon);
    }

    // ---------------- BigDataCloud（港澳台、海外） ----------------

    private static async Task<Place?> BigDataCloudAsync(double lat, double lon)
    {
        try
        {
            var url = string.Format(CultureInfo.InvariantCulture,
                "https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={0:F5}&longitude={1:F5}&localityLanguage=zh", lat, lon);
            using var doc = JsonDocument.Parse(await Net.GetStringAsync(url, timeoutMs: 6_000).ConfigureAwait(false));
            var o = doc.RootElement;
            var country = Str(o, "countryCode").ToUpperInvariant();
            if (country.Length == 0) return null;
            string Level(int n)
            {
                if (!o.TryGetProperty("localityInfo", out var li) || !li.TryGetProperty("administrative", out var adm)) return "";
                foreach (var a in adm.EnumerateArray())
                    if (a.TryGetProperty("adminLevel", out var lv) && lv.ValueKind == JsonValueKind.Number && lv.GetInt32() == n)
                        return Str(a, "name");
                return "";
            }
            var province = Str(o, "principalSubdivision");
            if (province.Length == 0) province = Level(4);
            var city = Str(o, "city");
            if (city.Length == 0) city = Level(5);
            if (city.Length == 0) city = province;
            var district = Str(o, "locality");
            if (district == city || district == province) district = "";
            if (district.Length == 0) district = Level(6);
            if (district == city) district = "";
            return new Place(country, province, city, district);
        }
        catch
        {
            return null;
        }
    }

    // ---------------- IP 兜底 ----------------

    /// <summary>按网络出口 IP 大致定位到城市（开了代理/VPN 会定位到代理所在地）</summary>
    public static async Task<(Place Place, double Lat, double Lon)?> IpFallbackAsync()
    {
        try
        {
            var text = await Net.GetStringAsync("https://myip.ipip.net", timeoutMs: 6_000).ConfigureAwait(false);
            var i = text.IndexOf("来自于：", StringComparison.Ordinal);
            if (i < 0) return null;
            var parts = text[(i + 4)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "中国") return null;
            var prov = parts[1];
            var city = parts.Length > 2 ? parts[2] : "";
            var inProv = Table().Where(r => r.Province.StartsWith(prov, StringComparison.Ordinal)).ToList();
            if (inProv.Count == 0) return null;
            var inCity = city.Length == 0 ? [] : inProv.Where(r => r.City.StartsWith(city, StringComparison.Ordinal)).ToList();
            var r = inCity.FirstOrDefault(x => x.District.EndsWith('区')) ?? inCity.FirstOrDefault() ?? inProv[0];
            LastSource = "ip";
            return (new Place(CountryOf(r.Province), r.Province, r.City, ""), r.Lat, r.Lon);
        }
        catch
        {
            return null;
        }
    }
}
