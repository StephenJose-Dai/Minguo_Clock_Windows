using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace MinguoCalendar;

/// <summary>
/// 每个进程的实时上传/下载速度。Windows 上只能靠 ETW 内核网络事件统计，需要管理员权限。
/// 只在连接详情窗口打开时运行（系统级跟踪，不便一直开）。不是管理员就 Available=false，详情里显示“—”。
/// </summary>
internal static class NetMeter
{
    private static readonly object Lock = new();
    private static Dictionary<int, long[]> _acc = new();                 // pid -> [下载字节, 上传字节]
    private static Dictionary<int, (double Down, double Up)> _rate = new();
    private static Dictionary<int, long[]> _dacc = new();                // pid -> [磁盘读字节, 磁盘写字节]
    private static Dictionary<int, (double Read, double Write)> _drate = new();
    private static DateTime _lastAt;

    private static TraceEventSession? _session;
    private static Thread? _thread;
    private static Timer? _timer;
    private static int _users;
    private static bool _started;

    public static bool Available { get; private set; }
    public static bool NeedsAdmin { get; private set; }

    public static (double Down, double Up) Rate(int pid)
    {
        lock (Lock) return _rate.TryGetValue(pid, out var v) ? v : (0, 0);
    }

    public static (double Read, double Write) DiskRate(int pid)
    {
        lock (Lock) return _drate.TryGetValue(pid, out var v) ? v : (0, 0);
    }

    public static void Acquire()
    {
        _users++;
        if (_started) return;
        Start();
    }

    public static void Release()
    {
        if (_users > 0) _users--;
        if (_users > 0 || !_started) return;
        Stop();
    }

    private static void Start()
    {
        NeedsAdmin = false;
        Available = false;
        try
        {
            if (TraceEventSession.IsElevated() != true) { NeedsAdmin = true; _started = true; return; }

            // 清掉可能残留的同名会话
            try { using (var old = new TraceEventSession("MinguoCalendarNet") { StopOnDispose = true }) { } } catch { /* 没有就算了 */ }

            _session = new TraceEventSession("MinguoCalendarNet") { StopOnDispose = true };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.DiskIO);
            var k = _session.Source.Kernel;
            k.TcpIpRecv += d => Add(d.ProcessID, d.size, true);
            k.TcpIpSend += d => Add(d.ProcessID, d.size, false);
            k.TcpIpRecvIPV6 += d => Add(d.ProcessID, d.size, true);
            k.TcpIpSendIPV6 += d => Add(d.ProcessID, d.size, false);
            k.UdpIpRecv += d => Add(d.ProcessID, d.size, true);
            k.UdpIpSend += d => Add(d.ProcessID, d.size, false);
            k.UdpIpRecvIPV6 += d => Add(d.ProcessID, d.size, true);
            k.UdpIpSendIPV6 += d => Add(d.ProcessID, d.size, false);
            k.DiskIORead += d => AddDisk(d.ProcessID, d.TransferSize, true);
            k.DiskIOWrite += d => AddDisk(d.ProcessID, d.TransferSize, false);

            _lastAt = DateTime.UtcNow;
            _thread = new Thread(() => { try { _session.Source.Process(); } catch { /* 停止时会退出 */ } }) { IsBackground = true, Name = "net-etw" };
            _thread.Start();
            _timer = new Timer(_ => Snapshot(), null, 1000, 1000);
            Available = true;
            _started = true;
        }
        catch (Exception e)
        {
            App.Log(e);
            Available = false;
            _started = true; // 别反复重试
            try { _session?.Dispose(); } catch { /* 忽略 */ }
            _session = null;
        }
    }

    private static void Stop()
    {
        _started = false;
        Available = false;
        try { _timer?.Dispose(); } catch { /* 忽略 */ }
        _timer = null;
        try { _session?.Dispose(); } catch { /* 忽略 */ } // 会让 Process() 返回，线程退出
        _session = null;
        lock (Lock) { _acc = new(); _rate = new(); _dacc = new(); _drate = new(); }
    }

    private static void Add(int pid, int size, bool down)
    {
        if (pid <= 0 || size <= 0) return;
        lock (Lock)
        {
            if (!_acc.TryGetValue(pid, out var a)) { a = new long[2]; _acc[pid] = a; }
            a[down ? 0 : 1] += size;
        }
    }

    private static void AddDisk(int pid, int size, bool read)
    {
        if (pid <= 0 || size <= 0) return;
        lock (Lock)
        {
            if (!_dacc.TryGetValue(pid, out var a)) { a = new long[2]; _dacc[pid] = a; }
            a[read ? 0 : 1] += size;
        }
    }

    private static void Snapshot()
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;
            var dt = (now - _lastAt).TotalSeconds;
            _lastAt = now;
            if (dt < 0.1) return;
            var r = new Dictionary<int, (double, double)>(_acc.Count);
            foreach (var (pid, a) in _acc) r[pid] = (a[0] / dt, a[1] / dt);
            _rate = r;
            _acc = new();
            var dr = new Dictionary<int, (double, double)>(_dacc.Count);
            foreach (var (pid, a) in _dacc) dr[pid] = (a[0] / dt, a[1] / dt);
            _drate = dr;
            _dacc = new();
        }
    }
}
