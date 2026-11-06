namespace Minguo.Core;

/// <summary>发布相关配置：按需修改后重新编译</summary>
public static class AppConfig
{
    public const string Author = "StephenJose_Dai";

    /// <summary>“关于”页的 GitHub、Telegram 地址：没填时显示“未设置”</summary>
    public const string GitHubUrl = "https://github.com/StephenJose-Dai/Minguo_Clock_Windows";   // 例如 https://github.com/你的用户名/仓库名
    public const string TelegramUrl = "https://t.me/+bmFk2CWSOGM0OWFl"; // 例如 https://t.me/你的用户名或频道名

    /// <summary>
    /// 天气接口（公共接口）：所有用户共用这个 Key 的调用次数，所以有频率限制。
    /// 用户也可以在 设置 → 天气 → 天气接口 里填自己的 Key（优先使用）。
    /// 用于逆地理编码（坐标 → 省市区）；ApiSecret 是数字签名私钥，没开就留空。
    /// </summary>
    public const string ApiKey = "15999063c76b38d2f3ec102f5b830648";
    public const string ApiSecret = "";
    public const string ApiUrl = "https://restapi.amap.com/v3/geocode/regeo";
}
