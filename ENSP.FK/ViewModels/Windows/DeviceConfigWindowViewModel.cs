using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Models.Configuration;
using ENSP.ZD.Models.Topology;
using ENSP.ZD.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace ENSP.ZD.ViewModels.Windows;

public enum DeviceKind { Router, Switch, Terminal, Firewall, Other }

public partial class ConfigTreeNode : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _rawConfig = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<ConfigTreeNode> Children { get; } = new();
}

public partial class DeviceConfigWindowViewModel : ObservableObject
{
    private readonly DeviceConnectionManager _connectionMgr;
    private readonly AIConfigGenerator _aiGenerator;
    private readonly Models.ApiConfig _apiConfig;

    public DeviceConfigWindowViewModel(
        DeviceConnectionManager connectionMgr,
        AIConfigGenerator aiGenerator,
        Models.ApiConfig apiConfig)
    {
        _connectionMgr = connectionMgr;
        _aiGenerator = aiGenerator;
        _apiConfig = apiConfig;
    }

    // ── Window ──────────────────────────────────────────────

    [ObservableProperty] private string _windowTitle = "设备配置";
    [ObservableProperty] private string _deviceName = string.Empty;
    [ObservableProperty] private string _deviceModel = string.Empty;
    [ObservableProperty] private int _consolePort;
    [ObservableProperty] private string _statusText = string.Empty;

    // ── Device type ─────────────────────────────────────────

    [ObservableProperty] private DeviceKind _currentDeviceKind = DeviceKind.Router;

    public bool IsRouterDevice => CurrentDeviceKind == DeviceKind.Router;
    public bool IsSwitchDevice => CurrentDeviceKind == DeviceKind.Switch;
    public bool IsTerminalDevice => CurrentDeviceKind == DeviceKind.Terminal;
    public bool IsNetworkDevice => IsRouterDevice || IsSwitchDevice;

    // ── Config tree ─────────────────────────────────────────

    public ObservableCollection<ConfigTreeNode> ConfigTreeNodes { get; } = new();

    [ObservableProperty] private ConfigTreeNode? _selectedTreeNode;
    [ObservableProperty] private string _activeContentKey = "global";

    public bool IsContentGlobal => ActiveContentKey == "global";
    public bool IsContentStaticRoute => ActiveContentKey == "staticroute";
    public bool IsContentRip => ActiveContentKey == "rip";
    public bool IsContentOspf => ActiveContentKey == "ospf";
    public bool IsContentIsis => ActiveContentKey == "isis";
    public bool IsContentBgp => ActiveContentKey == "bgp";
    public bool IsContentVlan => ActiveContentKey == "vlan";
    public bool IsContentInterface => ActiveContentKey.StartsWith("iface:");
    public string ActiveInterfaceName => IsContentInterface ? ActiveContentKey[6..] : string.Empty;

    // ── Form fields: Global ─────────────────────────────────

    [ObservableProperty] private string _hostname = string.Empty;
    [ObservableProperty] private string _deviceMac = string.Empty;
    [ObservableProperty] private string _configStatus = string.Empty;

    // ── Form fields: Static Route ───────────────────────────

    [ObservableProperty] private string _staticRouteDest = string.Empty;
    [ObservableProperty] private string _staticRouteMask = "255.255.255.0";
    [ObservableProperty] private string _staticRouteNextHop = string.Empty;

    public ObservableCollection<StaticRouteEntry> StaticRoutes { get; } = new();

    [ObservableProperty] private string _staticRouteCliPreview = string.Empty;

    // ── Form fields: RIP ────────────────────────────────────

    [ObservableProperty] private string _ripVersion = "2";
    [ObservableProperty] private string _ripNetwork = string.Empty;

    public ObservableCollection<RipNetworkEntry> RipNetworks { get; } = new();

    [ObservableProperty] private string _ripCliPreview = string.Empty;

    // ── Form fields: OSPF ───────────────────────────────────

    [ObservableProperty] private string _ospfProcessId = "1";
    [ObservableProperty] private string _ospfRouterId = string.Empty;
    [ObservableProperty] private string _ospfArea = "0";
    [ObservableProperty] private string _ospfNetwork = string.Empty;

    public ObservableCollection<OspfNetworkEntry> OspfNetworks { get; } = new();

    [ObservableProperty] private string _ospfCliPreview = string.Empty;

    // ── Form fields: BGP ────────────────────────────────────

    [ObservableProperty] private string _bgpAsNumber = string.Empty;
    [ObservableProperty] private string _bgpRouterId = string.Empty;
    [ObservableProperty] private string _bgpNetwork = string.Empty;
    [ObservableProperty] private string _bgpPeerIp = string.Empty;
    [ObservableProperty] private int _bgpPeerAsNumber;

    public ObservableCollection<BgpNetworkEntry> BgpNetworks { get; } = new();
    public ObservableCollection<BgpPeerEntry> BgpPeers { get; } = new();

    [ObservableProperty] private string _bgpCliPreview = string.Empty;

    // ── Form fields: IS-IS ──────────────────────────────────

    [ObservableProperty] private string _isisSystemId = string.Empty;
    [ObservableProperty] private string _isisLevel = "level-1-2";
    [ObservableProperty] private string _isisNetwork = string.Empty;
    [ObservableProperty] private string _isisCliPreview = string.Empty;

    public ObservableCollection<IsisNetworkEntry> IsisNetworks { get; } = new();

    // ── Form fields: VLAN ───────────────────────────────────

    [ObservableProperty] private string _vlanId = string.Empty;
    [ObservableProperty] private string _vlanName = string.Empty;

    public ObservableCollection<VlanEntry> Vlans { get; } = new();

    [ObservableProperty] private string _vlanCliPreview = string.Empty;

    // ── CLI Terminal tab ────────────────────────────────────

    [ObservableProperty] private bool _isCliConnected;
    [ObservableProperty] private string _cliOutput = string.Empty;
    [ObservableProperty] private string _cliInput = string.Empty;
    [ObservableProperty] private string _cliStatus = "未连接";

    // ── AI tab ──────────────────────────────────────────────

    [ObservableProperty] private string _aiPrompt = string.Empty;
    [ObservableProperty] private string _aiResult = string.Empty;
    [ObservableProperty] private bool _isAiGenerating;
    [ObservableProperty] private string _aiStatus = string.Empty;

    // ── Internal state ──────────────────────────────────────

    private ParsedDeviceConfig? _parsedConfig;
    private string _fetchedRawConfig = string.Empty;

    // ── Initialization ──────────────────────────────────────

    public void Initialize(string deviceName, int consolePort, string deviceModel = "")
    {
        DeviceName = deviceName;
        ConsolePort = consolePort;
        DeviceModel = deviceModel;
        WindowTitle = string.IsNullOrEmpty(deviceModel)
            ? $"{deviceName} — 设备配置"
            : $"{deviceName} ({deviceModel}) — 设备配置";
        CurrentDeviceKind = ResolveDeviceKind(deviceModel);
        UpdateConnectionState();
    }

    private static DeviceKind ResolveDeviceKind(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return DeviceKind.Router;

        var m = model.ToUpperInvariant();
        if (m.Contains("PC") || m.Contains("CLIENT") || m.Contains("SERVER")
            || m.Contains("PHONE") || m.Contains("STA") || m.Contains("MCS"))
            return DeviceKind.Terminal;
        if (m.Contains("USG") || m.Contains("FW") || m.Contains("NGFW"))
            return DeviceKind.Firewall;
        if (m.StartsWith("S") || m.Contains("SWITCH") || m.Contains("LS"))
            return DeviceKind.Switch;
        return DeviceKind.Router;
    }

    private void UpdateConnectionState()
    {
        var session = _connectionMgr.Sessions.FirstOrDefault(s => s.DeviceName == DeviceName);
        IsCliConnected = session?.IsConnected ?? false;
        CliStatus = IsCliConnected ? "已连接" : "未连接";
    }

    // Notify all device-type derived properties
    private void NotifyDeviceKindChanged()
    {
        OnPropertyChanged(nameof(IsRouterDevice));
        OnPropertyChanged(nameof(IsSwitchDevice));
        OnPropertyChanged(nameof(IsTerminalDevice));
        OnPropertyChanged(nameof(IsNetworkDevice));
    }

    // Notify all content-visibility properties
    private void NotifyContentVisibility()
    {
        OnPropertyChanged(nameof(IsContentGlobal));
        OnPropertyChanged(nameof(IsContentStaticRoute));
        OnPropertyChanged(nameof(IsContentRip));
        OnPropertyChanged(nameof(IsContentOspf));
        OnPropertyChanged(nameof(IsContentIsis));
        OnPropertyChanged(nameof(IsContentBgp));
        OnPropertyChanged(nameof(IsContentVlan));
        OnPropertyChanged(nameof(IsContentInterface));
        OnPropertyChanged(nameof(ActiveInterfaceName));
    }

    // ── Fetch config ────────────────────────────────────────

    [RelayCommand]
    private async Task FetchConfigAsync()
    {
        StatusText = "正在获取运行配置...";
        try
        {
            var snapshot = await _connectionMgr.FetchConfigAsync(DeviceName);
            if (snapshot?.ParsedConfig != null)
            {
                _parsedConfig = snapshot.ParsedConfig;
                _fetchedRawConfig = snapshot.RawConfig;
                AutoFillFormFields(snapshot.ParsedConfig);
                BuildConfigTree(snapshot);
                StatusText = $"配置获取成功 — {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                StatusText = "获取配置失败 — 设备可能未连接或未就绪";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
    }

    private void AutoFillFormFields(ParsedDeviceConfig cfg)
    {
        Hostname = cfg.Hostname;

        // Tables
        StaticRoutes.Clear();
        foreach (var r in cfg.StaticRoutes) StaticRoutes.Add(r);

        RipVersion = cfg.RipVersion;
        RipNetworks.Clear();
        foreach (var r in cfg.RipNetworkEntries) RipNetworks.Add(r);

        OspfProcessId = cfg.OspfProcessId;
        OspfRouterId = cfg.OspfRouterId;
        OspfArea = cfg.OspfArea;
        OspfNetworks.Clear();
        foreach (var n in cfg.OspfNetworkEntries) OspfNetworks.Add(n);

        IsisSystemId = cfg.IsisSystemId;
        IsisLevel = cfg.IsisLevel;
        IsisNetworks.Clear();
        foreach (var n in cfg.IsisNetworkEntries) IsisNetworks.Add(n);

        BgpAsNumber = cfg.BgpAsNumber;
        BgpRouterId = cfg.BgpRouterId;
        BgpNetworks.Clear();
        foreach (var n in cfg.BgpNetworkEntries) BgpNetworks.Add(n);
        BgpPeers.Clear();
        foreach (var p in cfg.BgpPeerEntries) BgpPeers.Add(p);

        Vlans.Clear();
        foreach (var v in cfg.Vlans) Vlans.Add(v);

        ConfigStatus = $"{DateTime.Now:HH:mm:ss} 已加载";
        RefreshAllCliPreviews();
    }

    // ── Config tree ─────────────────────────────────────────

    private void BuildConfigTree(DeviceConfigSnapshot snapshot)
    {
        ConfigTreeNodes.Clear();
        var cfg = snapshot.ParsedConfig!;

        // GLOBAL
        var globalNode = new ConfigTreeNode
        {
            Key = "global", DisplayName = "全局配置", RawConfig = cfg.GlobalConfig, IsExpanded = true
        };
        ConfigTreeNodes.Add(globalNode);

        if (IsNetworkDevice)
        {
            // ROUTING parent
            var routingNodes = new List<ConfigTreeNode>();
            if (!string.IsNullOrEmpty(cfg.StaticRouteConfig))
                routingNodes.Add(new ConfigTreeNode { Key = "staticroute", DisplayName = $"静态路由 ({cfg.StaticRoutes.Count})", RawConfig = cfg.StaticRouteConfig });
            if (!string.IsNullOrEmpty(cfg.RipConfig))
                routingNodes.Add(new ConfigTreeNode { Key = "rip", DisplayName = $"RIP v{cfg.RipVersion}", RawConfig = cfg.RipConfig });
            if (!string.IsNullOrEmpty(cfg.OspfConfig))
                routingNodes.Add(new ConfigTreeNode { Key = "ospf", DisplayName = $"OSPF {cfg.OspfProcessId}", RawConfig = cfg.OspfConfig });
            if (!string.IsNullOrEmpty(cfg.IsisConfig))
                routingNodes.Add(new ConfigTreeNode { Key = "isis", DisplayName = $"IS-IS {cfg.IsisLevel}", RawConfig = cfg.IsisConfig });
            if (!string.IsNullOrEmpty(cfg.BgpConfig))
                routingNodes.Add(new ConfigTreeNode { Key = "bgp", DisplayName = $"BGP AS {cfg.BgpAsNumber}", RawConfig = cfg.BgpConfig });

            if (routingNodes.Count > 0)
            {
                var routingParent = new ConfigTreeNode { Key = "routing", DisplayName = "路由协议", IsExpanded = true };
                foreach (var n in routingNodes) routingParent.Children.Add(n);
                ConfigTreeNodes.Add(routingParent);
            }

            // SWITCHING parent (VLAN)
            if (!string.IsNullOrEmpty(cfg.VlanConfig))
            {
                var swParent = new ConfigTreeNode { Key = "switching", DisplayName = "交换配置", IsExpanded = true };
                swParent.Children.Add(new ConfigTreeNode { Key = "vlan", DisplayName = $"VLAN ({cfg.Vlans.Count})", RawConfig = cfg.VlanConfig });
                ConfigTreeNodes.Add(swParent);
            }
        }

        // INTERFACE
        if (cfg.InterfaceNames.Count > 0)
        {
            var ifaceParent = new ConfigTreeNode { Key = "interfaces", DisplayName = $"接口 ({cfg.InterfaceNames.Count})", IsExpanded = true };
            foreach (var ifName in cfg.InterfaceNames)
            {
                var ifBlock = ExtractInterfaceBlock(snapshot.RawConfig, ifName);
                ifaceParent.Children.Add(new ConfigTreeNode
                {
                    Key = $"iface:{ifName}", DisplayName = ShortIfName(ifName), RawConfig = ifBlock
                });
            }
            ConfigTreeNodes.Add(ifaceParent);
        }

        // TERMINAL: Network settings node
        if (IsTerminalDevice && !string.IsNullOrEmpty(cfg.TerminalIfaceName))
        {
            ConfigTreeNodes.Add(new ConfigTreeNode
            {
                Key = "terminal",
                DisplayName = "网络设置",
                RawConfig = cfg.Ipv4Address,
                IsExpanded = true
            });
        }
    }

    partial void OnSelectedTreeNodeChanged(ConfigTreeNode? value)
    {
        if (value == null) return;
        ActiveContentKey = value.Key;
        NotifyContentVisibility();

        if (value.Key == "routing" || value.Key == "switching" || value.Key == "interfaces")
            return; // parent nodes — keep previous content

        // For leaf nodes, load raw config into the old-style editor for backward compat
        // Form fields already loaded via AutoFillFormFields
    }

    partial void OnActiveContentKeyChanged(string value)
    {
        NotifyContentVisibility();
        RefreshAllCliPreviews();
    }

    // ── CLI Preview builders ────────────────────────────────

    private void RefreshAllCliPreviews()
    {
        RefreshStaticRouteCliPreview();
        RefreshRipCliPreview();
        RefreshOspfCliPreview();
        RefreshBgpCliPreview();
        RefreshIsisCliPreview();
        RefreshVlanCliPreview();
    }

    private void RefreshStaticRouteCliPreview()
    {
        var sb = new StringBuilder();
        foreach (var r in StaticRoutes)
        {
            var line = r.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
        }
        StaticRouteCliPreview = sb.ToString().TrimEnd();
    }

    private void RefreshRipCliPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"rip");
        if (!string.IsNullOrWhiteSpace(RipVersion)) sb.AppendLine($" version {RipVersion}");
        foreach (var n in RipNetworks)
        {
            var line = n.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine($" {line}");
        }
        RipCliPreview = sb.ToString().TrimEnd();
    }

    private void RefreshOspfCliPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ospf {OspfProcessId}");
        if (!string.IsNullOrWhiteSpace(OspfRouterId)) sb.AppendLine($" router-id {OspfRouterId}");
        foreach (var n in OspfNetworks)
        {
            var line = n.CliLine.Replace("network ", "  network ");
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine($" area {OspfArea}{Environment.NewLine}{line}");
        }
        OspfCliPreview = sb.ToString().TrimEnd();
    }

    private void RefreshBgpCliPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"bgp {BgpAsNumber}");
        if (!string.IsNullOrWhiteSpace(BgpRouterId)) sb.AppendLine($" router-id {BgpRouterId}");
        foreach (var n in BgpNetworks)
        {
            var line = n.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine($" {line}");
        }
        foreach (var p in BgpPeers)
        {
            var line = p.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine($" {line}");
        }
        BgpCliPreview = sb.ToString().TrimEnd();
    }

    private void RefreshIsisCliPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"isis");
        if (!string.IsNullOrWhiteSpace(IsisSystemId)) sb.AppendLine($" network-entity {IsisSystemId}");
        sb.AppendLine($" is-level {IsisLevel}");
        foreach (var n in IsisNetworks)
        {
            var line = n.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine($" {line}");
        }
        IsisCliPreview = sb.ToString().TrimEnd();
    }

    private void RefreshVlanCliPreview()
    {
        var sb = new StringBuilder();
        foreach (var v in Vlans)
        {
            var line = v.CliLine;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
        }
        VlanCliPreview = sb.ToString().TrimEnd();
    }

    // ── Table commands: Static Route ────────────────────────

    [RelayCommand]
    private void AddStaticRoute()
    {
        if (string.IsNullOrWhiteSpace(StaticRouteDest) || string.IsNullOrWhiteSpace(StaticRouteNextHop))
        {
            StatusText = "请填写目标网络和下一跳";
            return;
        }
        StaticRoutes.Add(new StaticRouteEntry
        {
            Dest = StaticRouteDest, Mask = StaticRouteMask, NextHop = StaticRouteNextHop
        });
        StaticRouteDest = StaticRouteNextHop = string.Empty;
        StaticRouteMask = "255.255.255.0";
        RefreshStaticRouteCliPreview();
    }

    [RelayCommand]
    private void DeleteStaticRoute(StaticRouteEntry entry)
    {
        StaticRoutes.Remove(entry);
        RefreshStaticRouteCliPreview();
    }

    // ── Table commands: RIP ─────────────────────────────────

    [RelayCommand]
    private void AddRipNetwork()
    {
        if (string.IsNullOrWhiteSpace(RipNetwork)) { StatusText = "请填写网络地址"; return; }
        RipNetworks.Add(new RipNetworkEntry { Network = RipNetwork });
        RipNetwork = string.Empty;
        RefreshRipCliPreview();
    }

    [RelayCommand]
    private void DeleteRipNetwork(RipNetworkEntry entry)
    {
        RipNetworks.Remove(entry);
        RefreshRipCliPreview();
    }

    // ── Table commands: OSPF ────────────────────────────────

    [RelayCommand]
    private void AddOspfNetwork()
    {
        if (string.IsNullOrWhiteSpace(OspfNetwork)) { StatusText = "请填写网络地址"; return; }
        OspfNetworks.Add(new OspfNetworkEntry { Network = OspfNetwork, Area = OspfArea });
        OspfNetwork = string.Empty;
        RefreshOspfCliPreview();
    }

    [RelayCommand]
    private void DeleteOspfNetwork(OspfNetworkEntry entry)
    {
        OspfNetworks.Remove(entry);
        RefreshOspfCliPreview();
    }

    // ── Table commands: BGP ─────────────────────────────────

    [RelayCommand]
    private void AddBgpNetwork()
    {
        if (string.IsNullOrWhiteSpace(BgpNetwork)) { StatusText = "请填写网络地址"; return; }
        BgpNetworks.Add(new BgpNetworkEntry { Network = BgpNetwork });
        BgpNetwork = string.Empty;
        RefreshBgpCliPreview();
    }

    [RelayCommand]
    private void DeleteBgpNetwork(BgpNetworkEntry entry)
    {
        BgpNetworks.Remove(entry);
        RefreshBgpCliPreview();
    }

    [RelayCommand]
    private void AddBgpPeer()
    {
        if (string.IsNullOrWhiteSpace(BgpPeerIp)) { StatusText = "请填写 Peer IP"; return; }
        BgpPeers.Add(new BgpPeerEntry { PeerIp = BgpPeerIp });
        BgpPeerIp = string.Empty;
        BgpPeerAsNumber = 0;
        RefreshBgpCliPreview();
    }

    [RelayCommand]
    private void DeleteBgpPeer(BgpPeerEntry entry)
    {
        BgpPeers.Remove(entry);
        RefreshBgpCliPreview();
    }

    // ── Table commands: IS-IS ───────────────────────────────

    [RelayCommand]
    private void AddIsisNetwork()
    {
        if (string.IsNullOrWhiteSpace(IsisNetwork)) { StatusText = "请填写 NET 地址"; return; }
        IsisNetworks.Add(new IsisNetworkEntry { Network = IsisNetwork });
        IsisNetwork = string.Empty;
        RefreshIsisCliPreview();
    }

    [RelayCommand]
    private void DeleteIsisNetwork(IsisNetworkEntry entry)
    {
        IsisNetworks.Remove(entry);
        RefreshIsisCliPreview();
    }

    // ── Table commands: VLAN ────────────────────────────────

    [RelayCommand]
    private void AddVlan()
    {
        if (string.IsNullOrWhiteSpace(VlanId)) { StatusText = "请填写 VLAN ID"; return; }
        Vlans.Add(new VlanEntry { VlanId = VlanId, Name = VlanName });
        VlanId = VlanName = string.Empty;
        RefreshVlanCliPreview();
    }

    [RelayCommand]
    private void DeleteVlan(VlanEntry entry)
    {
        Vlans.Remove(entry);
        RefreshVlanCliPreview();
    }

    // ── Per-section send commands ───────────────────────────

    [RelayCommand]
    private async Task SendGlobalConfig()
    {
        var commands = new List<string> { "sys" };
        if (!string.IsNullOrWhiteSpace(Hostname))
            commands.Add($"sysname {Hostname}");
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendStaticRouteConfig()
    {
        var commands = StaticRoutes.Select(r => r.CliLine).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendRipConfig()
    {
        var commands = RipCliPreview.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendOspfConfig()
    {
        var commands = OspfCliPreview.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendBgpConfig()
    {
        var commands = BgpCliPreview.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendIsisConfig()
    {
        var commands = IsisCliPreview.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendVlanConfig()
    {
        var commands = VlanCliPreview.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (_, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        StatusText = msg;
    }

    [RelayCommand]
    private async Task SendAllSectionsAsync()
    {
        var allNodes = new List<ConfigTreeNode>();
        void Flatten(ConfigTreeNode node)
        {
            allNodes.Add(node);
            foreach (var child in node.Children) Flatten(child);
        }
        foreach (var node in ConfigTreeNodes) Flatten(node);

        int sent = 0;
        foreach (var node in allNodes.Where(n => !string.IsNullOrWhiteSpace(n.RawConfig)))
        {
            StatusText = $"推送 {node.DisplayName}...";
            var commands = node.RawConfig.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
            var (success, _) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
            if (success) sent++;
        }
        StatusText = $"已推送 {sent}/{allNodes.Count(n => !string.IsNullOrWhiteSpace(n.RawConfig))} 个配置段";
    }

    // ── CLI Terminal ────────────────────────────────────────

    [RelayCommand]
    private async Task ConnectCliAsync()
    {
        CliStatus = "正在连接...";
        try
        {
            _connectionMgr.Initialize(new[] { (DeviceName, ConsolePort) });
            await _connectionMgr.ConnectAsync(DeviceName);
            var session = _connectionMgr.Sessions.FirstOrDefault(s => s.DeviceName == DeviceName);
            IsCliConnected = session?.IsConnected ?? false;
            CliStatus = IsCliConnected ? "已连接" : "连接失败";
            if (IsCliConnected && session != null)
                CliOutput = session.TerminalOutput;
        }
        catch (Exception ex)
        {
            CliStatus = $"连接失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DisconnectCli()
    {
        _connectionMgr.Disconnect(DeviceName);
        IsCliConnected = false;
        CliStatus = "已断开";
    }

    [RelayCommand]
    private async Task SendCliCommandAsync()
    {
        if (!IsCliConnected || string.IsNullOrWhiteSpace(CliInput)) return;
        var cmd = CliInput.Trim();
        CliInput = string.Empty;
        CliOutput += $"\r\n> {cmd}\r\n";
        var session = _connectionMgr.Sessions.FirstOrDefault(s => s.DeviceName == DeviceName);
        if (session != null)
        {
            await _connectionMgr.SendCommandsAsync(DeviceName, new[] { cmd });
            CliOutput = session.TerminalOutput;
        }
    }

    // ── AI Generation ───────────────────────────────────────

    [RelayCommand]
    private async Task GenerateAiConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(AiPrompt))
        {
            AiStatus = "请输入需求描述";
            return;
        }

        IsAiGenerating = true;
        AiStatus = "正在生成 AI 配置...";
        AiResult = string.Empty;

        try
        {
            var (reachable, latency, err) = await _aiGenerator.TestConnectivityAsync();
            if (!reachable)
            {
                AiStatus = $"AI 不可达 ({latency}ms): {err}";
                IsAiGenerating = false;
                return;
            }
            AiStatus = $"AI 已连接 ({latency}ms) — 生成中...";

            var singleDeviceTopo = new Topology
            {
                Devices = new List<Device> { new() { Name = DeviceName, Model = DeviceModel, ConsolePort = ConsolePort } },
                Links = new List<TopologyLink>()
            };

            var configs = await _aiGenerator.GenerateAsync(singleDeviceTopo, new List<ENSP.ZD.Models.Requirements.TaskRequirement>(), AiPrompt);

            if (configs != null && configs.Count > 0)
            {
                var config = configs.First();
                AiResult = config.RenderAll();
                AiStatus = $"生成成功 — {config.Sections.Count} 个配置段";
            }
            else
            {
                AiResult = _aiGenerator.LastRawResponse;
                AiStatus = $"生成完成 — 但无法解析配置结构。原始响应如下。{_aiGenerator.LastError}";
            }
        }
        catch (Exception ex)
        {
            AiStatus = $"AI 生成失败: {ex.Message}";
        }
        finally
        {
            IsAiGenerating = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAiConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(AiResult)) { AiStatus = "没有可应用的配置"; return; }
        var commands = AiResult.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
        var (success, msg) = await _connectionMgr.SendCommandsAsync(DeviceName, commands);
        AiStatus = msg;
    }

    // ── Refresh ─────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshDiffAsync()
    {
        StatusText = "正在刷新...";
        await FetchConfigAsync();
    }

    // ── Helpers ─────────────────────────────────────────────

    private static string ExtractInterfaceBlock(string rawConfig, string ifaceName)
    {
        var pattern = $@"^interface\s+{System.Text.RegularExpressions.Regex.Escape(ifaceName)}\s*\r?\n(?:\s+.+\r?\n)*";
        var m = System.Text.RegularExpressions.Regex.Match(rawConfig, pattern, System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success ? m.Value.TrimEnd() : string.Empty;
    }

    private static string ShortIfName(string fullName)
    {
        if (fullName.StartsWith("GigabitEthernet", StringComparison.OrdinalIgnoreCase))
            return "GE" + fullName[15..];
        if (fullName.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase))
            return "Eth" + fullName[7..];
        if (fullName.StartsWith("Serial", StringComparison.OrdinalIgnoreCase))
            return "Ser" + fullName[5..];
        if (fullName.StartsWith("Vlanif", StringComparison.OrdinalIgnoreCase))
            return "Vlanif" + fullName[6..];
        return fullName;
    }
}
