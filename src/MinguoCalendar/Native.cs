using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MinguoCalendar;

/// <summary>需要直接调用的 Windows 系统函数</summary>
internal static class Native
{
    // ---------- 简繁转换（系统自带，不需要词库） ----------
    private const uint LcmapTraditionalChinese = 0x04000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string lpLocaleName, uint dwMapFlags, string lpSrcStr, int cchSrc,
        [Out] char[]? lpDestStr, int cchDest, IntPtr lpVersionInformation, IntPtr lpReserved, IntPtr sortHandle);

    /// <summary>简体 → 繁體（失败时原样返回）</summary>
    public static string ToTraditional(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        try
        {
            var n = LCMapStringEx("zh-TW", LcmapTraditionalChinese, s, s.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return s;
            var buf = new char[n];
            n = LCMapStringEx("zh-TW", LcmapTraditionalChinese, s, s.Length, buf, n, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return n > 0 ? new string(buf, 0, n) : s;
        }
        catch
        {
            return s;
        }
    }

    // ---------- 窗口 ----------
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const int GWLP_HWNDPARENT = -8;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_POPUP = 0x80000000L;

    public static readonly IntPtr HWND_BOTTOM = new(1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    // 窗口边缘（拖动调整大小用）
    public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetWindowLong(IntPtr h, int i) => IntPtr.Size == 8 ? GetWindowLongPtr64(h, i) : GetWindowLong32(h, i);

    public static IntPtr SetWindowLong(IntPtr h, int i, IntPtr v) => IntPtr.Size == 8 ? SetWindowLongPtr64(h, i, v) : SetWindowLong32(h, i, v);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    // ---------- 标题栏深色 ----------
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>Windows 10 1809+ / 11：让标题栏跟随暗黑 / 白天</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        try
        {
            var v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)) != 0) // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));    // 旧版 Windows 10 的编号
        }
        catch
        {
            // 系统不支持就算了
        }
    }

    // ---------- 屏幕保护程序 ----------
    public const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    public const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    // ---------- 托盘图标释放 ----------
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);
}
