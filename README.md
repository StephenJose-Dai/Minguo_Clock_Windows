# 民国纪年 · Windows 版

托盘民国日期、透明桌面小组件、屏幕保护程序、锁屏背景图。支持 Windows 10（1809 及以上）和 Windows 11，x64 / x86 / ARM64。

## 功能

- **托盘**：图标显示今天几号，悬停显示民国日期、农历、天气；左键打开设置，右键菜单。
- **桌面小组件**：完全透明、无边框，固定在桌面层；可拖动、拖边角调整宽高（宽度最多一屏，高度最多屏幕 60%）；右键可锁定。
- **屏幕保护程序**：全屏时钟 + 民国日期 + 农历 + 天气 + 名言 + 签名，多显示器同时显示。
- **锁屏背景**：每 30 分钟把壁纸和民国日期等内容画成图片，设为系统锁屏图片。
- **天气**：Windows 定位服务自动定位（拿不到时按网络大致定位），每 30 分钟更新。
- **壁纸**：每日壁纸（每 30 分钟换一张）/ 自选图片 / 渐变主题。
- **名言**：每 30 分钟换一句。
- 暗黑（默认）/ 白天 / 自动主题；简体 / 繁體；民国 / 中华民国；阿拉伯数字 / 中文数字。

## 构建

推送到 GitHub 后由 Actions 自动构建（`.github/workflows/build.yml`），在 Actions 页面下载：

| 文件 | 说明 |
| --- | --- |
| `MinguoCalendar-版本-x64-setup.exe` | 安装包（64 位 Intel / AMD，绝大多数电脑） |
| `MinguoCalendar-版本-arm64-setup.exe` | 安装包（ARM 电脑，如骁龙 X） |
| `MinguoCalendar-版本-x86-setup.exe` | 安装包（32 位系统） |
| `MinguoCalendar-版本-架构-portable.zip` | 绿色版，解压直接运行 |

推送 `v` 开头的标签（如 `v1.0.0`）会自动发布到 Releases。

本地构建（Windows，需 .NET 8 SDK）：

```
dotnet publish src/MinguoCalendar -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

## 需要修改的配置

`src/Minguo.Core/AppConfig.cs`：

- `GitHubUrl`、`TelegramUrl`：关于页的链接（留空显示“未设置”）
- `ApiKey`、`ApiSecret`、`ApiUrl`：天气接口的公共 Key（用户也可以在 设置 → 天气 → 天气接口 填自己的）

版本号在 `src/MinguoCalendar/MinguoCalendar.csproj` 的 `<Version>`。

## 已知限制

- Windows 不允许第三方程序替换锁屏界面本身，只能设置锁屏图片；Windows 11 锁屏会在上方显示系统时钟，所以图片上的内容放在右侧。
- 锁屏图片需要在系统设置里把“个性化锁屏界面”设为“图片”（不是“Windows 聚焦”）。

## 数据位置

- 设置：`%APPDATA%\MinguoCalendar\settings.json`
- 缓存（壁纸、锁屏图、屏保文件、错误日志）：`%LOCALAPPDATA%\MinguoCalendar`

## 许可

内置字体与图标的许可见 `Licenses` 文件夹。
