using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>一次采样的系统资源快照</summary>
internal sealed record SysStat
{
    public double Cpu { get; init; }            // 0..100
    public double MemPercent { get; init; }
    public double MemUsedGB { get; init; }
    public double MemTotalGB { get; init; }
    public IReadOnlyList<DiskStat> Disks { get; init; } = [];
    public double? TempC { get; init; }         // CPU/主板温度，取不到为 null
    public double? Gpu { get; init; }           // 显卡使用率 0..100，取不到为 null
    public double NetDown { get; init; }        // 字节/秒
    public double NetUp { get; init; }
    public double DiskRead { get; init; }       // 全机磁盘读 字节/秒
    public double DiskWrite { get; init; }      // 全机磁盘写 字节/秒
    public int ConnCount { get; init; }         // 当前 TCP 连接数
    public TimeSpan Uptime { get; init; }
}

internal sealed record DiskStat(string Name, double Percent, double UsedGB, double TotalGB);

/// <summary>一条 TCP 连接：进程、本地地址、远程地址、状态</summary>
public sealed record ConnInfo(int Pid, string Process, string Local, string Remote, string State);

/// <summary>
/// 系统资源采集（CPU、内存、磁盘、温度、网速、开机时长）。
/// 用谁多引用计数，没人看就停，省电。CPU 用 GetSystemTimes（不需要性能计数器，启动快）。
/// 温度走 WMI 的 MSAcpi_ThermalZoneTemperature，很多机器拿不到（需要主板/BIOS 支持），拿不到就显示“—”。
/// </summary>
internal static class SysInfo
{
    public static event Action? Updated;
    public static SysStat? Current { get; private set; }

    // 最近若干次采样，给小组件画动态折线图
    public const int HistLen = 48;
    private static readonly List<double> _cpuHist = new(), _gpuHist = new(), _memHist = new(), _netHist = new(), _dioHist = new();
    public static IReadOnlyList<double> CpuHist => _cpuHist;
    public static IReadOnlyList<double> GpuHist => _gpuHist;
    public static IReadOnlyList<double> MemHist => _memHist;
    public static IReadOnlyList<double> NetHist => _netHist;
    public static IReadOnlyList<double> DiskIoHist => _dioHist;
    /// <summary>网速折线的纵轴上限（窗口内最大值，至少 64 KB/s）</summary>
    public static double NetMax => Math.Max(64 * 1024, _netHist.Count == 0 ? 0 : _netHist.Max());
    /// <summary>磁盘读写折线的纵轴上限（至少 1 MB/s）</summary>
    public static double DiskIoMax => Math.Max(1024 * 1024, _dioHist.Count == 0 ? 0 : _dioHist.Max());

    private static void Push(List<double> h, double v)
    {
        h.Add(v);
        if (h.Count > HistLen) h.RemoveAt(0);
    }

    private static DispatcherTimer? _timer;
    private static int _users;

    // CPU
    private static long _lastIdle, _lastKernel, _lastUser;
    // 网络
    private static long _lastRx, _lastTx;
    private static DateTime _lastNetAt;
    // 温度（较慢，几秒查一次）
    private static int _tick;
    private static double? _temp;

    /// <summary>采样间隔（毫秒），跟随设置，可调</summary>
    public static int IntervalMs => Math.Clamp(SettingsStore.Current.SysInterval, 300, 10_000);

    public static void Acquire()
    {
        _users++;
        if (_timer != null) { ApplyInterval(); return; }
        Sample(); // 先出一帧，界面不空白
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalMs) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    /// <summary>设置里改了间隔后调用</summary>
    public static void ApplyInterval()
    {
        if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
    }

    public static void Release()
    {
        if (_users > 0) _users--;
        if (_users > 0 || _timer == null) return;
        _timer.Stop();
        _timer = null;
    }

    private static void Sample()
    {
        try
        {
            var cpu = ReadCpu();
            ReadMem(out var used, out var total);
            ReadNet();
            var (dr, dw) = ReadDiskIo();
            Current = new SysStat
            {
                Cpu = cpu,
                MemUsedGB = used,
                MemTotalGB = total,
                MemPercent = total > 0 ? used / total * 100 : 0,
                Disks = ReadDisks(),
                TempC = ReadTemp(),
                Gpu = ReadGpu(),
                NetDown = _down,
                NetUp = _up,
                DiskRead = dr,
                DiskWrite = dw,
                ConnCount = ReadConnCount(),
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            };
            Push(_cpuHist, Current.Cpu);
            Push(_gpuHist, Current.Gpu ?? 0);
            Push(_memHist, Current.MemPercent);
            Push(_netHist, Current.NetDown + Current.NetUp);
            Push(_dioHist, dr + dw);
            Updated?.Invoke();
        }
        catch (Exception e)
        {
            App.Log(e);
        }
    }

    // ---------------- CPU ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    private static long T(FILETIME f) => (long)(((ulong)f.High << 32) | f.Low);

    private static double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return Current?.Cpu ?? 0;
        long i = T(idle), k = T(kernel), u = T(user);
        var di = i - _lastIdle;
        var dk = k - _lastKernel;
        var du = u - _lastUser;
        _lastIdle = i; _lastKernel = k; _lastUser = u;
        var busy = dk + du - di;           // kernel 含 idle
        var totalTime = dk + du;
        if (totalTime <= 0) return Current?.Cpu ?? 0;
        return Math.Clamp(100.0 * busy / totalTime, 0, 100);
    }

    // ---------------- 内存 ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);

    private static bool ReadMem(out double usedGB, out double totalGB)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) { usedGB = totalGB = 0; return false; }
        const double G = 1024.0 * 1024 * 1024;
        totalGB = m.ullTotalPhys / G;
        usedGB = (m.ullTotalPhys - m.ullAvailPhys) / G;
        return true;
    }

    // ---------------- 磁盘 ----------------

    private static List<DiskStat> ReadDisks()
    {
        var list = new List<DiskStat>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                const double G = 1024.0 * 1024 * 1024;
                var total = d.TotalSize / G;
                if (total <= 0) continue;
                var used = (d.TotalSize - d.TotalFreeSpace) / G;
                list.Add(new DiskStat(d.Name.TrimEnd('\\'), used / total * 100, used, total));
            }
        }
        catch
        {
            // 某个盘读不到就跳过
        }
        return list;
    }

    // ---------------- 网速 ----------------

    private static double _down, _up;

    private static void ReadNet()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            var now = DateTime.UtcNow;
            if (_lastNetAt != default)
            {
                var dt = (now - _lastNetAt).TotalSeconds;
                if (dt > 0.1)
                {
                    _down = Math.Max(0, (rx - _lastRx) / dt);
                    _up = Math.Max(0, (tx - _lastTx) / dt);
                }
            }
            _lastRx = rx; _lastTx = tx; _lastNetAt = now;
        }
        catch
        {
            _down = _up = 0;
        }
    }

    // ---------------- 温度 ----------------

    private static double? ReadTemp()
    {
        // 查询较慢，约每 6 秒更新一次
        if (_tick++ % 4 != 0 && Current != null) return _temp;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double max = double.MinValue;
            foreach (var mo in searcher.Get())
            {
                var raw = Convert.ToDouble(mo["CurrentTemperature"], CultureInfo.InvariantCulture); // 开尔文的十分之一
                var c = raw / 10.0 - 273.15;
                if (c is > -20 and < 125 && c > max) max = c;
            }
            _temp = max > double.MinValue ? Math.Round(max, 1) : null;
        }
        catch
        {
            _temp = null; // 机器不支持
        }
        return _temp;
    }

    // ---------------- 磁盘读写速度（PerformanceCounter，全机） ----------------

    private static PerformanceCounter? _diskRead, _diskWrite;

    private static (double Read, double Write) ReadDiskIo()
    {
        try
        {
            _diskRead ??= new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWrite ??= new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            return (_diskRead.NextValue(), _diskWrite.NextValue());
        }
        catch
        {
            _diskRead = null;
            _diskWrite = null;
            return (0, 0);
        }
    }

    // ---------------- 进程可执行文件路径 ----------------

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static readonly Dictionary<int, string> PathCache = new();

    /// <summary>进程的完整路径（拿不到权限返回空）</summary>
    public static string ProcPath(int pid)
    {
        if (pid <= 4) return "";
        if (PathCache.TryGetValue(pid, out var p)) return p;
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) { PathCache[pid] = ""; return ""; }
        try
        {
            var sb = new StringBuilder(1024);
            var sz = sb.Capacity;
            p = QueryFullProcessImageName(h, 0, sb, ref sz) ? sb.ToString() : "";
        }
        finally
        {
            CloseHandle(h);
        }
        if (PathCache.Count > 400) PathCache.Clear();
        PathCache[pid] = p;
        return p;
    }

    // ---------------- 显卡（PerformanceCounter，best-effort） ----------------

    private static int _gpuTick;
    private static double? _gpu;
    private static PerformanceCounter[]? _gpuCounters;

    private static double? ReadGpu()
    {
        if (_gpuTick++ % 4 != 0 && Current != null) return _gpu;
        try
        {
            if (_gpuCounters == null)
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                _gpuCounters = cat.GetInstanceNames()
                    .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(cat.GetCounters)
                    .Where(c => c.CounterName == "Utilization Percentage")
                    .ToArray();
            }
            double sum = 0;
            foreach (var c in _gpuCounters)
            {
                try { sum += c.NextValue(); } catch { /* 个别实例读不到 */ }
            }
            _gpu = _gpuCounters.Length == 0 ? null : Math.Clamp(sum, 0, 100);
        }
        catch
        {
            _gpu = null;
            _gpuCounters = null; // 没有显卡计数器
        }
        return _gpu;
    }

    // ---------------- TCP 连接（GetExtendedTcpTable，等同任务管理器/netstat） ----------------

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int cls, int reserved);

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    private static readonly Dictionary<int, string> ProcNames = new();

    private static List<(uint State, uint La, uint Lp, uint Ra, uint Rp, uint Pid)> TcpRows()
    {
        var rows = new List<(uint, uint, uint, uint, uint, uint)>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return rows;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return rows;
            var n = Marshal.ReadInt32(buf);
            var p = buf + 4;
            for (var i = 0; i < n; i++)
            {
                rows.Add((
                    (uint)Marshal.ReadInt32(p),
                    (uint)Marshal.ReadInt32(p + 4),
                    (uint)Marshal.ReadInt32(p + 8),
                    (uint)Marshal.ReadInt32(p + 12),
                    (uint)Marshal.ReadInt32(p + 16),
                    (uint)Marshal.ReadInt32(p + 20)));
                p += 24;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return rows;
    }

    private static int ReadConnCount()
    {
        try { return TcpRows().Count; }
        catch { return 0; }
    }

    private static string Ip(uint a) => new IPAddress(new[] { (byte)a, (byte)(a >> 8), (byte)(a >> 16), (byte)(a >> 24) }).ToString();
    private static int Port(uint p) => ((int)(p & 0xFF) << 8) | (int)((p >> 8) & 0xFF);

    private static readonly string[] States =
        ["", "CLOSED", "LISTEN", "SYN-SENT", "SYN-RCVD", "ESTABLISHED", "FIN-WAIT1", "FIN-WAIT2", "CLOSE-WAIT", "CLOSING", "LAST-ACK", "TIME-WAIT", "DELETE-TCB"];

    private static string ProcName(int pid)
    {
        if (pid == 0) return "System Idle";
        if (pid == 4) return "System";
        if (ProcNames.TryGetValue(pid, out var name)) return name;
        try { name = Process.GetProcessById(pid).ProcessName + ".exe"; }
        catch { name = "—"; }
        ProcNames[pid] = name;
        return name;
    }

    /// <summary>当前所有 TCP 连接（详情窗口用）。按进程名排序</summary>
    public static List<ConnInfo> Connections()
    {
        if (ProcNames.Count > 400) ProcNames.Clear(); // 偶尔清掉已退出进程的缓存
        var list = new List<ConnInfo>();
        try
        {
            foreach (var r in TcpRows())
            {
                var remote = r.Ra == 0 && Port(r.Rp) == 0 ? "—" : $"{Ip(r.Ra)}:{Port(r.Rp)}";
                var state = r.State < States.Length ? States[r.State] : r.State.ToString(CultureInfo.InvariantCulture);
                list.Add(new ConnInfo((int)r.Pid, ProcName((int)r.Pid), $"{Ip(r.La)}:{Port(r.Lp)}", remote, state));
            }
        }
        catch
        {
            // 读不到就返回已有的
        }
        return list.OrderBy(c => c.Process, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Remote, StringComparer.Ordinal).ToList();
    }

    // ---------------- 显示格式 ----------------

    public static string Pct(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture) + "%";

    public static string Gb(double v) => v >= 100 ? Math.Round(v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);

    public static string Speed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{Math.Round(bytesPerSec)} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec / 1024 / 1024:0.0} MB/s";
    }

    public static string Temp(double? c) => c is { } v ? $"{v:0.#}°C" : "—";

    public static string UptimeText(TimeSpan t) =>
        t.TotalDays >= 1
            ? L.Tr($"{(int)t.TotalDays} 天 {t.Hours} 小时", $"{(int)t.TotalDays} 天 {t.Hours} 小時")
            : L.Tr($"{t.Hours} 小时 {t.Minutes} 分", $"{t.Hours} 小時 {t.Minutes} 分");
}
