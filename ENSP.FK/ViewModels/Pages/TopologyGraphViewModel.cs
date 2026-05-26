using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Helpers;
using ENSP.ZD.Models;
using ENSP.ZD.Models.Topology;
using ENSP.ZD.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.ViewModels.Pages;

public partial class TopologyGraphViewModel : ObservableObject, INavigationAware
{
    private readonly ProjectSession _session;
    private readonly VBoxDeviceService _vbox;
    private readonly DeviceIconService _iconService;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _timerCts;

    [ObservableProperty]
    private ObservableCollection<TopologyNode> _nodes = new();

    [ObservableProperty]
    private ObservableCollection<TopologyLinkViewModel> _linkVms = new();

    [ObservableProperty]
    private bool _hasTopology;

    [ObservableProperty]
    private bool _isLayoutRunning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public double CanvasWidth => 1200;
    public double CanvasHeight => 800;

    public TopologyGraphViewModel(ProjectSession session, VBoxDeviceService vbox, DeviceIconService iconService)
    {
        _session = session;
        _vbox = vbox;
        _iconService = iconService;
    }

    public async Task OnNavigatedToAsync()
    {
        LoadFromTopology();

        // Only run layout if .topo had no coordinate data
        bool hasPositions = _session.Topology?.Devices.Any(d => d.X != 0 || d.Y != 0) ?? false;
        if (!hasPositions && Nodes.Count > 0)
            await RunLayoutAsync();

        _ = RefreshRuntimeStatesAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"TopologyGraph state refresh failed: {t.Exception?.InnerException?.Message}");
        }, TaskScheduler.Default);
        StartPeriodicRefresh();
    }

    public Task OnNavigatedFromAsync()
    {
        StopPeriodicRefresh();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RecalculateLayout()
    {
        await RunLayoutAsync();
    }

    private void LoadFromTopology()
    {
        Nodes.Clear();
        LinkVms.Clear();

        if (_session.Topology == null || _session.Topology.Devices.Count == 0)
        {
            HasTopology = false;
            StatusText = "未加载拓扑文件 — 请先在拓扑导入页面导入拓扑";
            return;
        }

        HasTopology = true;

        bool hasPositions = _session.Topology.Devices.Any(d => d.X != 0 || d.Y != 0);

        // Collect connected interfaces per device from links
        var connectedIfaces = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in _session.Topology.Links)
        {
            if (!string.IsNullOrEmpty(link.InterfaceA))
                AddConnected(connectedIfaces, link.DeviceA, link.InterfaceA);
            if (!string.IsNullOrEmpty(link.InterfaceB))
                AddConnected(connectedIfaces, link.DeviceB, link.InterfaceB);
        }

        foreach (var dev in _session.Topology.Devices)
        {
            var iconPath = _iconService.ResolveIconPath(dev.Model);
            connectedIfaces.TryGetValue(dev.Name, out var connected);
            var ifaceText = connected?.Count > 0
                ? string.Join(", ", connected)
                : string.Empty;
            Nodes.Add(new TopologyNode
            {
                DeviceName = dev.Name,
                DeviceType = dev.Type,
                ConsolePort = dev.ConsolePort,
                IconPath = iconPath,
                InterfacesText = ifaceText,
                X = hasPositions ? dev.X : CanvasWidth / 2,
                Y = hasPositions ? dev.Y : CanvasHeight / 2
            });
        }

        foreach (var link in _session.Topology.Links)
        {
            LinkVms.Add(new TopologyLinkViewModel
            {
                LabelA = link.InterfaceA,
                LabelB = link.InterfaceB,
                SourceDevice = link.DeviceA,
                TargetDevice = link.DeviceB,
                X1 = hasPositions ? link.X1 : 0,
                Y1 = hasPositions ? link.Y1 : 0,
                X2 = hasPositions ? link.X2 : 0,
                Y2 = hasPositions ? link.Y2 : 0
            });
        }

        // Only compute offsets for non-topo coords (centered nodes have no edge info yet)
        if (!hasPositions)
            RecalculateAllLinkEndpoints();

        StatusText = $"{Nodes.Count} 台设备, {LinkVms.Count} 条链路";
    }

    private void RecalculateAllLinkEndpoints()
    {
        var nodeDict = Nodes.ToDictionary(n => n.DeviceName);

        // First pass: compute base edge endpoints (without offsets)
        var baseEndpoints = new List<(TopologyLinkViewModel link, double x1, double y1, double x2, double y2)>();
        foreach (var link in LinkVms)
        {
            if (nodeDict.TryGetValue(link.SourceDevice, out var src) &&
                nodeDict.TryGetValue(link.TargetDevice, out var tgt))
            {
                var (x1, y1, x2, y2) = ComputeEdgeEndpoints(src, tgt, 0, 0);
                baseEndpoints.Add((link, x1, y1, x2, y2));
            }
        }

        // Second pass: compute perpendicular offsets for parallel links
        var groups = baseEndpoints
            .GroupBy(x => MakePairKey(x.link.SourceDevice, x.link.TargetDevice))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var parallel = group.ToList();
            int n = parallel.Count;
            for (int i = 0; i < n; i++)
            {
                var (link, x1, y1, x2, y2) = parallel[i];
                double dx = x2 - x1;
                double dy = y2 - y1;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;
                double perpX = -dy / len;
                double perpY = dx / len;
                double offset = (i - (n - 1) / 2.0) * 10;
                link.OffsetX = perpX * offset;
                link.OffsetY = perpY * offset;
                link.X1 = x1 + perpX * offset;
                link.Y1 = y1 + perpY * offset;
                link.X2 = x2 + perpX * offset;
                link.Y2 = y2 + perpY * offset;
            }
        }

        // Apply base endpoints for non-parallel links
        foreach (var (link, x1, y1, x2, y2) in baseEndpoints)
        {
            if (link.OffsetX == 0 && link.OffsetY == 0)
            {
                link.X1 = x1;
                link.Y1 = y1;
                link.X2 = x2;
                link.Y2 = y2;
            }
        }
    }

    private static (double x1, double y1, double x2, double y2) ComputeEdgeEndpoints(
        TopologyNode src, TopologyNode tgt, double offsetX, double offsetY)
    {
        double srcCx = src.X + src.NodeWidth / 2;
        double srcCy = src.Y + src.NodeHeight / 2;
        double tgtCx = tgt.X + tgt.NodeWidth / 2;
        double tgtCy = tgt.Y + tgt.NodeHeight / 2;

        double dx = tgtCx - srcCx;
        double dy = tgtCy - srcCy;
        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);

        if (absDx < 0.01 && absDy < 0.01)
            return (srcCx + offsetX, srcCy + offsetY, tgtCx + offsetX, tgtCy + offsetY);

        double hwSrc = src.NodeWidth / 2;
        double hhSrc = src.NodeHeight / 2;
        double hwTgt = tgt.NodeWidth / 2;
        double hhTgt = tgt.NodeHeight / 2;

        double tSrc = Math.Min(
            absDx > 0.01 ? hwSrc / absDx : double.MaxValue,
            absDy > 0.01 ? hhSrc / absDy : double.MaxValue);
        double tTgt = Math.Min(
            absDx > 0.01 ? hwTgt / absDx : double.MaxValue,
            absDy > 0.01 ? hhTgt / absDy : double.MaxValue);

        if (tSrc + tTgt > 1)
        {
            double scale = 1 / (tSrc + tTgt);
            tSrc *= scale;
            tTgt *= scale;
        }

        double x1 = srcCx + dx * tSrc + offsetX;
        double y1 = srcCy + dy * tSrc + offsetY;
        double x2 = tgtCx - dx * tTgt + offsetX;
        double y2 = tgtCy - dy * tTgt + offsetY;

        return (x1, y1, x2, y2);
    }

    private static string MakePairKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    private static void AddConnected(Dictionary<string, HashSet<string>> dict, string device, string iface)
    {
        if (!dict.TryGetValue(device, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dict[device] = set;
        }
        set.Add(iface);
    }

    private async Task RunLayoutAsync()
    {
        if (Nodes.Count == 0) return;

        IsLayoutRunning = true;
        StatusText = "正在计算布局...";

        var nodeInfos = Nodes.Select(n => new NodeInfo { Id = n.DeviceName, X = n.X, Y = n.Y }).ToList();
        var linkInfos = LinkVms.Select(l => new LinkInfo { SourceId = l.SourceDevice, TargetId = l.TargetDevice }).ToList();

        await Task.Run(() =>
        {
            new ForceDirectedLayout().Layout(nodeInfos, linkInfos, CanvasWidth, CanvasHeight);
        });

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var ni in nodeInfos)
            {
                var node = Nodes.FirstOrDefault(n => n.DeviceName == ni.Id);
                if (node != null)
                {
                    node.X = ni.X;
                    node.Y = ni.Y;
                }
            }

            // Recalculate link endpoints with edge routing
            RecalculateAllLinkEndpoints();

            IsLayoutRunning = false;
            StatusText = $"{Nodes.Count} 台设备, {LinkVms.Count} 条链路";
        });
    }

    private async Task RefreshRuntimeStatesAsync()
    {
        try
        {
            var runningVms = _vbox.ListRunningVms();
            foreach (var node in Nodes)
            {
                if (node.ConsolePort <= 0)
                {
                    node.RuntimeState = DeviceRuntimeState.Off;
                    node.RuntimeStatusText = "无端口";
                    continue;
                }

                if (runningVms.Contains(node.DeviceName, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var client = new System.Net.Sockets.TcpClient();
                        var connectTask = client.ConnectAsync("127.0.0.1", node.ConsolePort);
                        var timeout = Task.Delay(500);
                        var completed = await Task.WhenAny(connectTask, timeout);

                        if (completed == connectTask && client.Connected)
                        {
                            node.RuntimeState = DeviceRuntimeState.Ready;
                            node.RuntimeStatusText = "已就绪";
                        }
                        else
                        {
                            node.RuntimeState = DeviceRuntimeState.Booting;
                            node.RuntimeStatusText = "启动中...";
                        }
                    }
                    catch
                    {
                        node.RuntimeState = DeviceRuntimeState.Booting;
                        node.RuntimeStatusText = "启动中...";
                    }
                }
                else
                {
                    node.RuntimeState = DeviceRuntimeState.Off;
                    node.RuntimeStatusText = "未启动";
                }
            }
        }
        catch
        {
            // VBoxManage unavailable — all devices stay Off
        }
    }

    private void StartPeriodicRefresh()
    {
        StopPeriodicRefresh();
        _timerCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _ = RunPeriodicRefresh(_timerCts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"Periodic refresh failed: {t.Exception?.InnerException?.Message}");
        }, TaskScheduler.Default);
    }

    private void StopPeriodicRefresh()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        _refreshTimer = null;
    }

    private async Task RunPeriodicRefresh(CancellationToken ct)
    {
        while (_refreshTimer != null && !ct.IsCancellationRequested)
        {
            try
            {
                await _refreshTimer.WaitForNextTickAsync(ct);
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await RefreshRuntimeStatesAsync();
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public partial class TopologyNode : ObservableObject
{
    private static readonly SolidColorBrush ReadyBrush = new(Color.FromRgb(0x10, 0x7C, 0x10));
    private static readonly SolidColorBrush BootingBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush TransitionBrush = new(Color.FromRgb(0xFF, 0xAA, 0x00));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xD1, 0x34, 0x38));
    private static readonly SolidColorBrush OffBrush = new(Color.FromRgb(0x88, 0x88, 0x88));

    public string DeviceName { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public int ConsolePort { get; set; }
    public string? IconPath { get; set; }
    public string InterfacesText { get; set; } = string.Empty;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private DeviceRuntimeState _runtimeState = DeviceRuntimeState.Off;

    [ObservableProperty]
    private string _runtimeStatusText = string.Empty;

    public double NodeWidth => 80;
    public double NodeHeight => 84;

    public Brush StatusIndicatorColor => RuntimeState switch
    {
        DeviceRuntimeState.Ready => ReadyBrush,
        DeviceRuntimeState.Booting => BootingBrush,
        DeviceRuntimeState.Cloning => TransitionBrush,
        DeviceRuntimeState.Stopping => TransitionBrush,
        DeviceRuntimeState.Error => ErrorBrush,
        _ => OffBrush
    };
}

public partial class TopologyLinkViewModel : ObservableObject
{
    public string SourceDevice { get; set; } = string.Empty;
    public string TargetDevice { get; set; } = string.Empty;
    public string LabelA { get; set; } = string.Empty;
    public string LabelB { get; set; } = string.Empty;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    [ObservableProperty]
    private double _x1;

    [ObservableProperty]
    private double _y1;

    [ObservableProperty]
    private double _x2;

    [ObservableProperty]
    private double _y2;
}
