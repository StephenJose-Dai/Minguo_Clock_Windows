using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Minguo.Core;
using Windows.Storage;
using Windows.System.UserProfile;

namespace MinguoCalendar;

/// <summary>
/// 锁屏背景图：Windows 不允许第三方程序替换锁屏界面本身（时间、密码输入都由系统负责），
/// 所以这里把“壁纸 + 民国日期 + 农历 + 天气 + 名言 + 签名”画成一张图，设为系统锁屏图片；天气、名言、壁纸一更新就同时重画。
/// </summary>
internal static class LockImage
{
    public static string LastMessage { get; private set; } = "";

    private static string Dir
    {
        get
        {
            var d = Path.Combine(SettingsStore.CacheDir, "lock");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>生成并设置。成功返回 null，否则返回原因（同时把图片另存到“图片”文件夹）</summary>
    public static async Task<string?> UpdateAsync()
    {
        var (w, h) = App.PrimaryPixels();
        var app = Application.Current;
        if (app == null) return "";
        byte[] jpg;
        try
        {
            jpg = await app.Dispatcher.InvokeAsync(() => Render(w, h));
        }
        catch (Exception e)
        {
            App.Log(e);
            return LastMessage = L.Tr("生成图片失败：", "產生圖片失敗：") + e.Message;
        }

        // 每次用新文件名，系统才会刷新（同名文件会被缓存）
        foreach (var old in Directory.GetFiles(Dir, "lock_*.jpg"))
        {
            try { File.Delete(old); } catch { /* 正在用就留着 */ }
        }
        var file = Path.Combine(Dir, $"lock_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
        await File.WriteAllBytesAsync(file, jpg).ConfigureAwait(false);

        string? error = null;
        try
        {
            var sf = await StorageFile.GetFileFromPathAsync(file);
            await LockScreen.SetImageFileAsync(sf);
        }
        catch (Exception e)
        {
            error = e.Message;
            try
            {
                if (UserProfilePersonalizationSettings.IsSupported())
                {
                    var sf = await StorageFile.GetFileFromPathAsync(file);
                    if (await UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(sf)) error = null;
                }
            }
            catch (Exception e2)
            {
                error = e2.Message;
            }
        }

        var s = SettingsStore.Current;
        s.LockImageSetAt = DateTime.Now;
        if (error == null)
        {
            s.LockImageApplied = true; // 记下我们接管了锁屏，卸载/关闭时好恢复
            SettingsStore.Save();
            LastMessage = L.Tr("已更新锁屏背景：", "已更新鎖定畫面背景：") + DateTime.Now.ToString("HH:mm");
            return null;
        }
        SettingsStore.Save();

        // 设置不了：另存一份到“图片\民国纪年”，用户可以自己在系统设置里选
        var copy = SaveToPictures(file);
        LastMessage = L.Tr("系统不允许直接设置锁屏图片（", "系統不允許直接設定鎖定畫面圖片（") + error + L.Tr(
            "）。图片已保存到：", "）。圖片已儲存到：") + copy + L.Tr(
            "，可在 Windows 设置 → 个性化 → 锁屏界面 里选择这张图片。",
            "，可在 Windows 設定 → 個人化 → 鎖定畫面 裡選擇這張圖片。");
        return LastMessage;
    }

    private static string SaveToPictures(string file)
    {
        try
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), L.Tr("民国纪年", "民國紀年"));
            Directory.CreateDirectory(d);
            var dst = Path.Combine(d, L.Tr("锁屏背景.jpg", "鎖定畫面背景.jpg"));
            File.Copy(file, dst, true);
            return dst;
        }
        catch
        {
            return file;
        }
    }

    /// <summary>在界面线程上把 ClockFace 画成 JPEG</summary>
    private static byte[] Render(int w, int h)
    {
        var face = new ClockFace(ClockFace.Kind.LockImage) { Width = w, Height = h };
        face.Measure(new Size(w, h));
        face.Arrange(new Rect(0, 0, w, h));
        face.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(face);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 把锁屏背景恢复成 Windows 默认图（关闭功能、重置、卸载时用）。
    /// 只有我们之前真的接管过（LockImageApplied）才动，免得改到用户自己设的锁屏。
    /// </summary>
    public static async Task RestoreDefaultAsync()
    {
        var s = SettingsStore.Current;
        if (!s.LockImageApplied) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Screen");
            var img = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jpg").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() : null;
            if (img != null)
            {
                var sf = await StorageFile.GetFileFromPathAsync(img);
                await LockScreen.SetImageFileAsync(sf);
            }
        }
        catch
        {
            // 恢复不了也不阻塞卸载
        }
        s.LockImageApplied = false;
        SettingsStore.Save();
    }

    /// <summary>打开系统锁屏设置</summary>
    public static void OpenSystemSettings() => Ui.Open("ms-settings:lockscreen");

    public static void CleanCache()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Dir)) File.Delete(f);
        }
        catch
        {
            // 忽略
        }
    }
}
