using System.Text.Json;
using Minguo.Core;

var fails = 0;
void Check(string name, object? actual, object? expected)
{
    var ok = Equals(actual, expected);
    if (!ok) fails++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: {actual}{(ok ? "" : $"   (期望 {expected})")}");
}

var hans = new DisplayStyle(Lang.Hans, false, false);
var hant = new DisplayStyle(Lang.Hant, true, true);

// 民国纪年
Check("民国年", Roc.DateLine(new DateTime(2026, 11, 3), hans), "民国115年11月3日");
Check("中華民國中文数字", Roc.DateLine(new DateTime(2026, 11, 3), hant), "中華民國一百一十五年十一月三日");
Check("民国元年", Roc.YearText(1912, hans), "民国1年");
Check("民国前", Roc.YearText(1911, hans), "民国前1年");
Check("中文数字 105", Roc.ChineseNumber(105), "一百零五");
Check("中文数字 110", Roc.ChineseNumber(110), "一百一十");
Check("星期", Roc.Weekday(new DateTime(2026, 11, 3)), "星期二");

// 农历（2026-11-03 安卓截图上是 农历丙午马年 九月廿五）
Check("农历", Roc.LunarLine(new DateTime(2026, 11, 3), hans), "农历丙午马年 九月廿五");
Check("農曆繁体", Roc.LunarLine(new DateTime(2026, 11, 3), hant), "農曆丙午馬年 九月廿五");
// 2025 年闰六月：2025-07-25 是闰六月初一，2025-06-25 是六月初一
Check("闰月", Roc.LunarShort(new DateTime(2025, 7, 25), hans), "闰六月初一");
Check("闰月前", Roc.LunarShort(new DateTime(2025, 6, 25), hans), "六月初一");
Check("闰月后", Roc.LunarShort(new DateTime(2025, 8, 23), hans), "七月初一");
Check("春节", Roc.LunarShort(new DateTime(2026, 2, 17), hans), "正月初一");
Check("除夕", Roc.LunarShort(new DateTime(2026, 2, 16), hans), "腊月廿九");
Check("12 小时制", Roc.HourMinute(new DateTime(2026, 1, 1, 13, 5, 0), false).ToString(), "(1:05, 下午)");

// 离线定位
Check("漳州芗城", Geo.Offline(24.513, 117.647)?.Label, "漳州市 芗城区");
Check("福州鼓楼", Geo.Offline(26.08, 119.296)?.Label, "福州市 鼓楼区");
Check("北京", Geo.Offline(39.908, 116.397)?.Province, "北京市");
Check("台北", Geo.Offline(25.04, 121.51)?.Country, "TW");
Check("香港", Geo.Offline(22.28, 114.16)?.Country, "HK");
Check("东京", Geo.Offline(35.68, 139.76), null);
var g = Geo.Wgs84ToGcj02(39.9087, 116.3975);
Check("坐标换算", $"{g.Lat:F4},{g.Lon:F4}", "39.9101,116.4037");

// 天气代码
Check("CMA 晴", CondInfo.FromCma("00"), Cond.Clear);
Check("WMO 雷", CondInfo.FromWmo(95), Cond.Thunder);
Check("图标", Cond.LightRain.Icon(), "rain");

// 名言
Check("内置名言数量", Quotes.Builtin.Length, 127);
Check("名言无出处", Quotes.Builtin[0], "愿你出走半生，归来仍是少年。");

// 设置 JSON（含 NaN）往返
var s = new AppSettings { Lat = double.NaN, CityName = "漳州市 芗城区", WeatherCache = new WeatherData { City = "x", Days = [new DayForecast(DateTime.Today, Cond.Rain, 20, double.NaN, null)] } };
var json = JsonSerializer.Serialize(s, SettingsStore.Json);
var back = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.Json)!;
Check("设置往返 NaN", double.IsNaN(back.Lat), true);
Check("设置往返 天气", back.WeatherCache?.Days[0].Cond, Cond.Rain);
Check("天气 Summary", WeatherService.Summary(back.WeatherCache! with { Cond = Cond.Rain }), "中雨 --°  --°/20°");

Console.WriteLine(fails == 0 ? "全部通过" : $"{fails} 项失败");
return fails == 0 ? 0 : 1;
