using System.Globalization;

namespace Minguo.Core;

/// <summary>显示风格：fullEra = 中華民國 / 中华民国；chineseNumerals = 一百一十五年</summary>
public readonly record struct DisplayStyle(Lang Lang, bool FullEra, bool ChineseNumerals)
{
    public bool Traditional => Lang == Lang.Hant;
}

/// <summary>民国纪年 + 农历 的格式化</summary>
public static class Roc
{
    private const string Digits = "〇一二三四五六七八九";
    private static readonly string[] WeekZh = ["一", "二", "三", "四", "五", "六", "日"];
    private const string Stems = "甲乙丙丁戊己庚辛壬癸";
    private const string Branches = "子丑寅卯辰巳午未申酉戌亥";
    private const string ZodiacT = "鼠牛虎兔龍蛇馬羊猴雞狗豬";
    private const string ZodiacS = "鼠牛虎兔龙蛇马羊猴鸡狗猪";
    private static readonly string[] LunarMonthT = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "臘"];
    private static readonly string[] LunarMonthS = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
    private static readonly ChineseLunisolarCalendar Lunar = new();

    /// <summary>1..999 转中文数字：115 → 一百一十五，105 → 一百零五</summary>
    public static string ChineseNumber(int n)
    {
        if (n < 0) return "负" + ChineseNumber(-n);
        if (n < 10) return Digits[n].ToString();
        if (n < 20) return "十" + (n % 10 == 0 ? "" : Digits[n % 10].ToString());
        if (n < 100) return Digits[n / 10] + "十" + (n % 10 == 0 ? "" : Digits[n % 10].ToString());
        if (n < 1000)
        {
            var rest = n % 100;
            var head = Digits[n / 100] + "百";
            if (rest == 0) return head;
            if (rest < 10) return head + "零" + Digits[rest];
            if (rest < 20) return head + "一" + ChineseNumber(rest);
            return head + ChineseNumber(rest);
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static string Num(int n, DisplayStyle s) => s.ChineseNumerals ? ChineseNumber(n) : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>民國 / 中華民國 / 民国 / 中华民国</summary>
    public static string Era(DisplayStyle s) => s.Traditional ? (s.FullEra ? "中華民國" : "民國") : (s.FullEra ? "中华民国" : "民国");

    /// <summary>民國115年 / 民國前1年</summary>
    public static string YearText(int gregorianYear, DisplayStyle s)
    {
        var roc = gregorianYear - 1911;
        return roc >= 1 ? Era(s) + Num(roc, s) + "年" : Era(s) + "前" + Num(1 - roc, s) + "年";
    }

    /// <summary>民國115年11月3日</summary>
    public static string DateLine(DateTime date, DisplayStyle s) =>
        YearText(date.Year, s) + Num(date.Month, s) + "月" + Num(date.Day, s) + "日";

    /// <summary>星期一</summary>
    public static string Weekday(DateTime date)
    {
        var i = ((int)date.DayOfWeek + 6) % 7; // 周一 = 0
        return "星期" + WeekZh[i];
    }

    /// <summary>農曆丙午馬年 九月廿四</summary>
    public static string LunarLine(DateTime date, DisplayStyle s)
    {
        var (cycle, month, leap, day) = LunarParts(date);
        var trad = s.Traditional;
        var ganzhi = Stems[cycle % 10].ToString() + Branches[cycle % 12];
        var zodiac = (trad ? ZodiacT : ZodiacS)[cycle % 12];
        var prefix = trad ? "農曆" : "农历";
        var leapText = leap ? (trad ? "閏" : "闰") : "";
        var monthName = (trad ? LunarMonthT : LunarMonthS)[month - 1];
        return $"{prefix}{ganzhi}{zodiac}年 {leapText}{monthName}月{LunarDay(day)}";
    }

    /// <summary>农历短格式：九月廿四</summary>
    public static string LunarShort(DateTime date, DisplayStyle s)
    {
        var line = LunarLine(date, s);
        var i = line.IndexOf(' ');
        return i >= 0 ? line[(i + 1)..] : line;
    }

    /// <summary>
    /// 农历各部分：cycle 0..59（0 = 甲子），month 1..12（已去掉闰月偏移），是否闰月，日。
    /// .NET 的 ChineseLunisolarCalendar 用“第几个月”表示（有闰月的年份共 13 个月），这里换算回正常月份。
    /// </summary>
    public static (int Cycle, int Month, bool Leap, int Day) LunarParts(DateTime date)
    {
        var d = date.Date;
        var year = Lunar.GetYear(d);
        var m = Lunar.GetMonth(d);       // 1..13
        var day = Lunar.GetDayOfMonth(d);
        var leapMonth = Lunar.GetLeapMonth(year); // 0 = 无闰月；否则是“闰月在第几个位置”（如闰六月 = 7）
        var leap = leapMonth > 0 && m == leapMonth;
        var month = leapMonth > 0 && m >= leapMonth ? m - 1 : m;
        var cycle = Lunar.GetSexagenaryYear(d) - 1; // 1..60 → 0..59
        return (cycle, month, leap, day);
    }

    private static string LunarDay(int d) => d switch
    {
        10 => "初十",
        20 => "二十",
        30 => "三十",
        < 10 => "初" + Digits[d],
        < 20 => "十" + Digits[d - 10],
        _ => "廿" + Digits[d - 20],
    };

    /// <summary>时间：(时:分, 上午/下午 或 null)</summary>
    public static (string Hm, string? AmPm) HourMinute(DateTime t, bool is24)
    {
        var m = t.Minute.ToString("00", CultureInfo.InvariantCulture);
        if (is24) return (t.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + m, null);
        var h = (t.Hour + 11) % 12 + 1;
        return ($"{h}:{m}", t.Hour < 12 ? "上午" : "下午");
    }

    /// <summary>今天 / 明天 / 後天 / 星期X</summary>
    public static string RelativeDay(DateTime date, DateTime today) => (date.Date - today.Date).Days switch
    {
        0 => L.Tr("今天", "今天"),
        1 => L.Tr("明天", "明天"),
        2 => L.Tr("后天", "後天"),
        _ => Weekday(date),
    };
}
