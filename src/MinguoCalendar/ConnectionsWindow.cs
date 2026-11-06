using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Minguo.Core;

namespace MinguoCalendar;

/// <summary>
/// TCP 连接详情：进程、PID、本地地址:端口、远程地址:端口、状态。等同任务管理器/netstat，只读自己这台电脑。
/// 自动刷新，间隔可调；可按进程/地址筛选。连接很多时调快或调慢都行。
/// </summary>
public sealed class ConnectionsWindow : Window
{
    private static ConnectionsWindow? _instance;
    private static readonly int[] Intervals = [500, 1000, 2000, 5000];

    private readonly ListView _list = new();
    private readonly TextBlock _count = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _filter;
    private readonly DispatcherTimer _timer = new();
    private bool _busy;

    // 不能结束的关键系统进程（结束会蓝屏/卡死）
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Memory Compression", "csrss.exe", "wininit.exe", "winlogon.exe",
        "services.exe", "lsass.exe", "smss.exe", "svchost.exe", "fontdrvhost.exe", "dwm.exe",
    };

    public static void ShowSingleton()
    {
        if (_instance == null)
        {
            _instance = new ConnectionsWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private ConnectionsWindow()
    {
        Title = L.Tr("连接详情", "連線詳情");
        Width = 1000;
        Height = 580;
        MinWidth = 720;
        MinHeight = 360;
        NetMeter.Acquire(); // 每进程网速（需要管理员）
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "PageBrush");
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);

        var root = new DockPanel { Margin = new Thickness(16) };

        // 顶部：连接数 + 刷新间隔 + 搜索
        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.FontSize = 14;
        _count.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        top.Children.Add(_count);

        var idx = Math.Max(0, Array.IndexOf(Intervals, SettingsStore.Current.ConnInterval));
        if (idx < 0) idx = 1;
        var intervalChoice = Ui.Choice(Intervals.Select(ms => ms >= 1000 ? $"{ms / 1000.0:0.#}s" : $"{ms}ms").ToList(), idx, i =>
        {
            SettingsStore.Current.ConnInterval = Intervals[i];
            SettingsStore.Save();
            _timer.Interval = TimeSpan.FromMilliseconds(Intervals[i]);
        });
        intervalChoice.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(intervalChoice, 1);
        top.Children.Add(intervalChoice);

        _filter = Ui.Input("", L.Tr("按进程或地址筛选", "按行程或位址篩選"));
        _filter.Width = 220;
        _filter.Margin = new Thickness(0);
        _filter.TextChanged += (_, _) => Render();
        Grid.SetColumn(_filter, 2);
        top.Children.Add(_filter);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // 不是管理员：提示 + 以管理员重启（每进程网速要管理员）
        if (NetMeter.NeedsAdmin)
        {
            var banner = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xB4, 0x54)),
            };
            var bg = new Grid();
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var msg = Ui.Text(L.Tr("每个进程的上传/下载速度需要管理员权限才能统计。", "每個行程的上傳/下載速度需要系統管理員權限才能統計。"), 13);
            msg.VerticalAlignment = VerticalAlignment.Center;
            bg.Children.Add(msg);
            var btn = Ui.Btn(L.Tr("以管理员重启", "以系統管理員重啟"), RestartAsAdmin, accent: true);
            btn.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(btn, 1);
            bg.Children.Add(btn);
            banner.Child = bg;
            DockPanel.SetDock(banner, Dock.Top);
            root.Children.Add(banner);
        }

        // 列表
        _list.SetResourceReference(BackgroundProperty, "CardBrush");
        _list.SetResourceReference(ForegroundProperty, "TextBrush");
        _list.BorderThickness = new Thickness(0);
        var gv = new GridView();
        void Col(string header, string path, double w)
        {
            gv.Columns.Add(new GridViewColumn { Header = header, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = w });
        }
        Col(L.Tr("进程", "行程"), nameof(SysRow.Process), 150);
        Col("PID", nameof(SysRow.Pid), 60);
        Col(L.Tr("下载/s", "下載/s"), nameof(SysRow.Down), 86);
        Col(L.Tr("上传/s", "上傳/s"), nameof(SysRow.Up), 86);
        Col(L.Tr("盘读/s", "碟讀/s"), nameof(SysRow.DRead), 86);
        Col(L.Tr("盘写/s", "碟寫/s"), nameof(SysRow.DWrite), 86);
        Col(L.Tr("内存", "記憶體"), nameof(SysRow.Mem), 84);
        Col(L.Tr("远程地址", "遠端位址"), nameof(SysRow.Remote), 185);
        Col(L.Tr("状态", "狀態"), nameof(SysRow.State), 104);
        _list.View = gv;
        _list.SelectionChanged += (_, _) => UpdateStatus();
        _list.ContextMenu = BuildRowMenu();

        _status.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        _status.FontSize = 12.5;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.Margin = new Thickness(2, 8, 2, 0);
        _status.Text = L.Tr("选中一行查看完整路径；右键可结束进程、打开所在目录", "選中一行查看完整路徑；右鍵可結束行程、開啟所在目錄");
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);   // 先放底部状态栏
        root.Children.Add(_list);     // 列表填满剩余
        Content = root;

        _timer.Interval = TimeSpan.FromMilliseconds(SettingsStore.Current.ConnInterval);
        _timer.Tick += (_, _) => Reload();
        SysInfo.Acquire(); // 让网速保持更新（即使资源组件没显示）
        Loaded += (_, _) => { _timer.Start(); Reload(); };
        Closed += (_, _) => { _timer.Stop(); SysInfo.Release(); NetMeter.Release(); };
    }

    private List<SysRow> _all = [];

    private void Reload()
    {
        if (_busy) return;
        _busy = true;
        Task.Run(() =>
        {
            var memCache = new Dictionary<int, string>();
            var pathCache = new Dictionary<int, string>();
            string Mem(int pid)
            {
                if (memCache.TryGetValue(pid, out var m)) return m;
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(pid);
                    var mb = p.WorkingSet64 / 1024.0 / 1024.0;
                    m = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{Math.Round(mb)} MB";
                }
                catch { m = "—"; }
                return memCache[pid] = m;
            }
            string Path(int pid)
            {
                if (pathCache.TryGetValue(pid, out var p)) return p;
                return pathCache[pid] = SysInfo.ProcPath(pid);
            }
            var rows = SysInfo.Connections().Select(c =>
            {
                var (down, up) = NetMeter.Available ? NetMeter.Rate(c.Pid) : (0, 0);
                var (dr, dw) = NetMeter.Available ? NetMeter.DiskRate(c.Pid) : (0, 0);
                var sp = NetMeter.Available;
                return new SysRow(c, sp ? SysInfo.Speed(down) : "—", sp ? SysInfo.Speed(up) : "—",
                    sp ? SysInfo.Speed(dr) : "—", sp ? SysInfo.Speed(dw) : "—", Mem(c.Pid), Path(c.Pid));
            }).ToList();
            Dispatcher.BeginInvoke(() =>
            {
                var selPid = (_list.SelectedItem as SysRow)?.Pid;
                _all = rows;
                Render();
                if (selPid is { } pid) _list.SelectedItem = (_list.ItemsSource as IEnumerable<SysRow>)?.FirstOrDefault(r => r.Pid == pid);
                _busy = false;
            });
        });
    }

    private void Render()
    {
        var q = _filter.Text.Trim();
        var shown = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(r => r.Process.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Local.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Remote.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Pid.ToString().Contains(q, StringComparison.Ordinal)).ToList();
        _list.ItemsSource = shown;
        var net = SysInfo.Current is { } st ? $"   ↓ {SysInfo.Speed(st.NetDown)}  ↑ {SysInfo.Speed(st.NetUp)}" : "";
        _count.Text = L.Tr($"共 {_all.Count} 个连接", $"共 {_all.Count} 個連線") + (shown.Count != _all.Count ? L.Tr($"，显示 {shown.Count}", $"，顯示 {shown.Count}") : "") + net;
    }

    /// <summary>给 ListView 绑定用（属性要 public）</summary>
    public sealed class SysRow
    {
        public SysRow(ConnInfo c, string down, string up, string dRead, string dWrite, string mem, string path)
        {
            Process = c.Process; Pid = c.Pid; Local = c.Local; Remote = c.Remote; State = c.State;
            Down = down; Up = up; DRead = dRead; DWrite = dWrite; Mem = mem; Path = path;
        }
        public string Process { get; }
        public int Pid { get; }
        public string Down { get; }
        public string Up { get; }
        public string DRead { get; }
        public string DWrite { get; }
        public string Mem { get; }
        public string Local { get; }
        public string Remote { get; }
        public string State { get; }
        public string Path { get; }
    }

    private void UpdateStatus()
    {
        if (_list.SelectedItem is not SysRow r)
        {
            _status.Text = L.Tr("选中一行查看完整路径；右键可结束进程、打开所在目录", "選中一行查看完整路徑；右鍵可結束行程、開啟所在目錄");
            return;
        }
        var path = string.IsNullOrEmpty(r.Path) ? L.Tr("（无法获取路径，可能需要管理员）", "（無法取得路徑，可能需要系統管理員）") : r.Path;
        _status.Text = $"{r.Process}  ·  {L.Tr("本地", "本地")} {r.Local}  ·  {path}";
    }

    private ContextMenu BuildRowMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string text, Action<SysRow> a)
        {
            var i = new MenuItem { Header = text };
            i.Click += (_, _) => { if (_list.SelectedItem is SysRow r) a(r); };
            m.Items.Add(i);
            return i;
        }
        Item(L.Tr("结束进程", "結束行程"), KillProcess);
        Item(L.Tr("打开所在目录", "開啟所在目錄"), OpenFolder);
        Item(L.Tr("复制远程地址", "複製遠端位址"), r => { try { Clipboard.SetText(r.Remote); } catch { /* 忽略 */ } });
        Item(L.Tr("复制路径", "複製路徑"), r => { try { if (!string.IsNullOrEmpty(r.Path)) Clipboard.SetText(r.Path); } catch { /* 忽略 */ } });
        return m;
    }

    private void KillProcess(SysRow r)
    {
        if (r.Pid <= 4 || Critical.Contains(r.Process))
        {
            MessageBox.Show(this, L.Tr("这是关键系统进程，不能结束。", "這是關鍵系統行程，不能結束。"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this,
                L.Tr($"确定要结束进程 {r.Process}（PID {r.Pid}）吗？\n未保存的数据会丢失。", $"確定要結束行程 {r.Process}（PID {r.Pid}）嗎？\n未儲存的資料會遺失。"),
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(r.Pid);
            p.Kill(entireProcessTree: true); // 连同子进程一起结束
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.Tr("结束失败：", "結束失敗：") + ex.Message + L.Tr("\n（系统进程可能需要管理员权限）", "\n（系統行程可能需要系統管理員權限）"), Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder(SysRow r)
    {
        if (string.IsNullOrEmpty(r.Path) || !System.IO.File.Exists(r.Path))
        {
            MessageBox.Show(this, L.Tr("拿不到该进程的路径（可能需要管理员权限）。", "拿不到該行程的路徑（可能需要系統管理員權限）。"), Title);
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{r.Path}\"") { UseShellExecute = true }); }
        catch { /* 忽略 */ }
    }

    private void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--tray") { UseShellExecute = true, Verb = "runas" });
            App.Instance.Quit();
        }
        catch
        {
            // 用户取消了 UAC
        }
    }
}
