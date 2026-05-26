using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Models;
using ENSP.ZD.Models.Configuration;
using ENSP.ZD.Models.Requirements;
using ENSP.ZD.Models.Topology;
using ENSP.ZD.Services;
using ENSP.ZD.ViewModels.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.ViewModels.Pages;

// ── Workbench ViewModel ─────────────────────────────────────────────────

public partial class WorkbenchViewModel : ObservableObject, INavigationAware
{
    private readonly TopologyParser _parser;
    private readonly ProjectSession _session;

    private readonly VBoxDeviceService _vbox;
    private readonly AIConfigGenerator _aiGenerator;
    private readonly ConfigurationGenerator _fallbackGenerator;
    private readonly ConfigExporter _exporter;
    private readonly DeviceStartupService _startupService;
    private readonly EnspGuiAutomationService _guiAuto;
    private readonly Models.ApiConfig _apiConfig;
    private readonly Dictionary<string, TelnetConnection> _connections = new();

    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    // ── Section 1: Topology Import ──────────────────────────────────

    [ObservableProperty]
    private string _topoFilePath = string.Empty;

    [ObservableProperty]
    private string _topoStatus = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Device> _importedDevices = new();

    [ObservableProperty]
    private int _linkCount;

    [ObservableProperty]
    private bool _isTopoLoaded;

    // ── Section 2: Requirements ─────────────────────────────────────

    [ObservableProperty]
    private string _rawRequirementText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Device> _availableDevices = new();

    [ObservableProperty]
    private Device? _selectedReqDevice;

    [ObservableProperty]
    private string _selectedReqType = "接口 IP";

    public ObservableCollection<string> ReqTypes { get; } = new()
    {
        "接口 IP", "VLAN", "OSPF", "静态路由", "ACL"
    };

    // Form fields
    [ObservableProperty] private string _ifName = string.Empty;
    [ObservableProperty] private string _ifIp = string.Empty;
    [ObservableProperty] private string _ifMask = "255.255.255.0";
    [ObservableProperty] private string _vlanId = string.Empty;
    [ObservableProperty] private string _vlanName = string.Empty;
    [ObservableProperty] private string _vlanAccessPorts = string.Empty;
    [ObservableProperty] private string _vlanTrunkPorts = string.Empty;
    [ObservableProperty] private string _ospfProcessId = "1";
    [ObservableProperty] private string _ospfRouterId = string.Empty;
    [ObservableProperty] private string _ospfAreaId = "0";
    [ObservableProperty] private string _ospfNetworks = string.Empty;
    [ObservableProperty] private string _routeDest = string.Empty;
    [ObservableProperty] private string _routeMask = string.Empty;
    [ObservableProperty] private string _routeNextHop = string.Empty;
    [ObservableProperty] private string _routeOutIf = string.Empty;
    [ObservableProperty] private string _aclNumber = "3000";
    [ObservableProperty] private string _aclAction = "permit";
    [ObservableProperty] private string _aclProtocol = "ip";
    [ObservableProperty] private string _aclSource = string.Empty;
    [ObservableProperty] private string _aclDest = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TaskRequirement> _addedRequirements = new();

    [ObservableProperty]
    private string _reqStatus = string.Empty;

    public bool HasRequirements => AddedRequirements.Count > 0
        || !string.IsNullOrWhiteSpace(RawRequirementText);

    public bool CanOneClickDeploy => HasRequirements && !IsOneClickRunning;

    public string OneClickDeployToolTip => CanOneClickDeploy
        ? "一切准备就绪，可以开始"
        : "需要在需求配置中导入需求";

    // ── Section 3: Config Generation ────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<DeviceConfig> _deviceConfigs = new();

    [ObservableProperty]
    private DeviceConfig? _selectedDeviceConfig;

    [ObservableProperty]
    private string _configText = string.Empty;

    [ObservableProperty]
    private string _genStatusMessage = "添加需求后点击生成配置";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    [ObservableProperty]
    private string _elapsedTime = string.Empty;

    private Stopwatch _elapsedSw = new();
    private DispatcherTimer? _elapsedTimer;

    // ── Section 4: Device Control ───────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<DeviceItem> _devices = new();

    [ObservableProperty]
    private DeviceItem? _selectedDevice;

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private string _commandInput = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _controlStatus = "就绪 — 选择设备并连接";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPushing;

    [ObservableProperty]
    private bool _isStartingDevices;

    [ObservableProperty]
    private bool _isStoppingDevices;

    // ── Section 3: One-Click All (generate → connect → verify → deploy) ─

    [ObservableProperty]
    private bool _isOneClickRunning;

    [ObservableProperty]
    private string _oneClickStatus = string.Empty;

    [RelayCommand]
    private async Task OneClickAll()
    {
        if (IsOneClickRunning) return;

        try
        {
            IsOneClickRunning = true;

            // Step 1: Generate configs
            OneClickStatus = "▶ 步骤 1/3: 生成配置...";
            await GenerateConfigs();
            if (_session.Configs.Count == 0)
            {
                OneClickStatus = "✗ 配置生成失败 — 请检查需求和 AI 连接";
                return;
            }

            // Step 2: Connect all devices
            OneClickStatus = "▶ 步骤 2/3: 连接全部设备...";
            var connectable = Devices.Where(d => d.HasConsole).ToList();
            if (connectable.Count == 0)
            {
                OneClickStatus = "✗ 没有可连接的设备";
                return;
            }

            // Disconnect any existing connections first
            foreach (var (_, conn) in _connections.ToList())
                conn.Dispose();
            _connections.Clear();

            foreach (var d in connectable)
            {
                d.IsBusy = true;
                d.RuntimeState = DeviceRuntimeState.Booting;
                d.RuntimeStatusText = "连接中...";
            }

            var tasks = connectable.Select(d => ConnectDeviceAsyncInternal(d)).ToArray();
            await Task.WhenAll(tasks);

            foreach (var d in connectable)
                d.IsBusy = false;

            var connected = connectable.Where(d => d.IsConnected).ToList();
            if (connected.Count == 0)
            {
                OneClickStatus = "✗ 所有设备连接失败";
                return;
            }
            if (connected.Count < connectable.Count)
            {
                var missing = connectable.Except(connected).Select(d => d.Name);
                OneClickStatus = $"✗ 部分设备连接失败 ({connected.Count}/{connectable.Count})，未连接: {string.Join(", ", missing)}";
                return;
            }

            // Let devices settle before pushing configs
            OneClickStatus = "设备已就绪，等待稳定...";
            await Task.Delay(3000);

            // Step 3: Push configs
            OneClickStatus = "▶ 步骤 3/3: 推送配置...";
            await OneClickDeploy();

            OneClickStatus = "✓ 全部完成";

            System.Windows.MessageBox.Show(
                $"一键部署完成！\n\n{DeployStatus}",
                "ENSP.FK",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OneClickStatus = $"✗ 一键部署失败: {ex.Message}";
            System.Windows.MessageBox.Show(
                $"部署失败: {ex.Message}",
                "ENSP.FK",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsOneClickRunning = false;
        }
    }

    // ── Section 6: One-Click Deploy ──────────────────────────────────

    [ObservableProperty]
    private string _deployStatus = "完成前面步骤后，点击一键部署将配置推送到所有设备";

    [ObservableProperty]
    private bool _isDeploying;

    // ── Section 5: Verification ─────────────────────────────────────

    [ObservableProperty]
    private string _verifyStatus = string.Empty;

    [ObservableProperty]
    private string _verifyResultText = string.Empty;

    [ObservableProperty]
    private bool _isVerifying2;

    // ── Constructor ─────────────────────────────────────────────────

    public WorkbenchViewModel(
        TopologyParser parser,
        ProjectSession session,
        VBoxDeviceService vbox,
        AIConfigGenerator aiGenerator,
        ConfigurationGenerator fallbackGenerator,
        ConfigExporter exporter,
        DeviceStartupService startupService,
        EnspGuiAutomationService guiAuto,
        Models.ApiConfig apiConfig)
    {
        _parser = parser;
        _session = session;
        _vbox = vbox;
        _aiGenerator = aiGenerator;
        _fallbackGenerator = fallbackGenerator;
        _exporter = exporter;
        _startupService = startupService;
        _guiAuto = guiAuto;
        _apiConfig = apiConfig;
    }

    // ── INavigationAware ────────────────────────────────────────────

    public Task OnNavigatedToAsync()
    {
        LoadFromSession();
        StartRefreshTimer();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        StopRefreshTimer();
        foreach (var device in Devices)
            CancelDeviceStartup(device);
        foreach (var (_, conn) in _connections.ToList())
            conn.Dispose();
        _connections.Clear();
        return Task.CompletedTask;
    }

    private void LoadFromSession()
    {
        if (_session.Topology != null)
        {
            ImportedDevices = new ObservableCollection<Device>(_session.Topology.Devices);
            LinkCount = _session.Topology.Links.Count;
            IsTopoLoaded = true;
            TopoFilePath = _session.TopologyFilePath ?? "";
            TopoStatus = $"已加载 {ImportedDevices.Count} 台设备，{LinkCount} 条链路";
            AvailableDevices = new ObservableCollection<Device>(_session.Topology.Devices);
            RefreshDevices();
        }

        if (_session.Requirements.Count > 0)
            AddedRequirements = new ObservableCollection<TaskRequirement>(_session.Requirements);

        if (!string.IsNullOrWhiteSpace(_session.RawRequirementText))
            RawRequirementText = _session.RawRequirementText;

        if (_session.Configs.Count > 0)
        {
            DeviceConfigs = new ObservableCollection<DeviceConfig>(_session.Configs);
            GenStatusMessage = $"已加载 {_session.Configs.Count} 台设备配置";
        }
    }

    // ── Section 1: Topology Import ──────────────────────────────────

    [RelayCommand]
    private void BrowseTopoFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "eNSP Topology (*.topo)|*.topo|All files (*.*)|*.*",
            Title = "选择 eNSP 拓扑文件"
        };
        if (dlg.ShowDialog() == true)
            TopoFilePath = dlg.FileName;
    }

    [RelayCommand]
    private void ParseTopology()
    {
        if (string.IsNullOrWhiteSpace(TopoFilePath))
        {
            TopoStatus = "请先选择 .topo 文件";
            return;
        }

        try
        {
            var topology = _parser.Parse(TopoFilePath);
            _session.Topology = topology;
            _session.TopologyFilePath = TopoFilePath;
            _session.Requirements.Clear();
            _session.Configs.Clear();

            ImportedDevices = new ObservableCollection<Device>(topology.Devices);
            LinkCount = topology.Links.Count;
            IsTopoLoaded = true;
            TopoStatus = $"解析成功 — {ImportedDevices.Count} 台设备，{LinkCount} 条链路";
            AvailableDevices = new ObservableCollection<Device>(topology.Devices);

            AddedRequirements.Clear();
            DeviceConfigs.Clear();
            ConfigText = string.Empty;
            GenStatusMessage = "添加需求后点击生成配置";

            RefreshDevices();

            // 自动打开 eNSP 并启动全部设备（后台运行，一键部署时无需再等待）
            _ = StartDevicesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            TopoStatus = $"解析失败: {ex.Message}";
        }
    }

    /// <summary>解析后自动打开 eNSP 拓扑并启动全部设备</summary>
    private async Task StartDevicesInBackgroundAsync()
    {
        try
        {
            // 1. 打开 eNSP 拓扑文件（如 eNSP 未运行则启动）
            if (!EnspGuiAutomationService.IsEnspRunning() &&
                !string.IsNullOrWhiteSpace(TopoFilePath) && File.Exists(TopoFilePath))
            {
                ControlStatus = "正在启动 eNSP 并打开拓扑...";
                EnspGuiAutomationService.LaunchEnsp(TopoFilePath);
                await Task.Delay(3000);
                var hwnd = await EnspGuiAutomationService.WaitForEnspWindowReadyAsync(60);
                if (hwnd == IntPtr.Zero)
                {
                    await Task.Delay(5000);
                    hwnd = await EnspGuiAutomationService.WaitForEnspWindowReadyAsync(30);
                }
                if (hwnd == IntPtr.Zero) return;
            }

            // 2. 点击启动全部
            var devices = Devices.Where(d => d.HasConsole && d.RuntimeState == DeviceRuntimeState.Off).ToList();
            if (devices.Count == 0) return;

            ControlStatus = "自动启动设备中...";
            var cts = new CancellationTokenSource();
            var error = await _guiAuto.ClickToolbarButtonAsync("start_all", cts.Token);
            cts.Dispose();
            if (error != null) return;

            // 3. 等待端口就绪
            var expectedPorts = devices.Select(d => d.ConsolePort).Where(p => p > 0).ToHashSet();
            if (expectedPorts.Count > 0)
            {
                for (int attempt = 0; attempt < 45; attempt++)
                {
                    var listening = await Task.Run(() => DeviceStartupService.ScanListeningPorts());
                    int open = expectedPorts.Count(p => listening.Contains(p));
                    if (open == expectedPorts.Count) break;
                    await Task.Delay(2000);
                }
            }

            // 4. 稳定等待
            await Task.Delay(8000);

            // 5. 连接全部设备
            foreach (var d in devices)
            {
                d.IsBusy = true;
                d.RuntimeState = DeviceRuntimeState.Booting;
                d.RuntimeStatusText = "连接中...";
            }

            var connTasks = devices.Select(d => ConnectDeviceAsyncInternal(d)).ToArray();
            await Task.WhenAll(connTasks);

            foreach (var d in devices)
            {
                d.IsBusy = false;
                d.RuntimeState = d.IsConnected ? DeviceRuntimeState.Ready : DeviceRuntimeState.Error;
                d.RuntimeStatusText = d.IsConnected ? "就绪" : "连接失败";
            }

            int ready = devices.Count(d => d.IsConnected);
            ControlStatus = $"设备就绪: {ready}/{devices.Count}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[后台启动] 失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenTopoInEnsp()
    {
        if (string.IsNullOrWhiteSpace(TopoFilePath))
        {
            TopoStatus = "请先选择 .topo 文件";
            return;
        }

        if (!File.Exists(TopoFilePath))
        {
            TopoStatus = "文件不存在";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = TopoFilePath,
                UseShellExecute = true
            });
            TopoStatus = $"已在 eNSP 中打开: {Path.GetFileName(TopoFilePath)}";
        }
        catch (Exception ex)
        {
            TopoStatus = $"打开失败: {ex.Message}";
        }
    }

    // ── Section 2: Requirements ─────────────────────────────────────

    partial void OnRawRequirementTextChanged(string value)
    {
        _session.RawRequirementText = value;
        OnPropertyChanged(nameof(HasRequirements));
        OnPropertyChanged(nameof(CanOneClickDeploy));
    }

    partial void OnAddedRequirementsChanged(ObservableCollection<TaskRequirement> value)
    {
        OnPropertyChanged(nameof(HasRequirements));
        OnPropertyChanged(nameof(CanOneClickDeploy));
    }

    partial void OnIsOneClickRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOneClickDeploy));
        OnPropertyChanged(nameof(OneClickDeployToolTip));
    }

    private void NotifyRequirementsChanged()
    {
        OnPropertyChanged(nameof(HasRequirements));
        OnPropertyChanged(nameof(CanOneClickDeploy));
        OnPropertyChanged(nameof(OneClickDeployToolTip));
    }

    [RelayCommand]
    private void AddRequirement()
    {
        if (SelectedReqDevice == null)
        {
            ReqStatus = "请先选择设备";
            return;
        }

        TaskRequirement? req = SelectedReqType switch
        {
            "接口 IP" => ParseInterfaceIp(),
            "VLAN" => ParseVlan(),
            "OSPF" => ParseOspf(),
            "静态路由" => ParseStaticRoute(),
            "ACL" => ParseAcl(),
            _ => null
        };

        if (req == null) return;

        _session.Requirements.Add(req);
        AddedRequirements.Add(req);
        NotifyRequirementsChanged();
        ReqStatus = $"已添加 {SelectedReqType} 需求到 {SelectedReqDevice.Name}";
    }

    private InterfaceIpRequirement? ParseInterfaceIp()
    {
        if (string.IsNullOrWhiteSpace(IfName) || string.IsNullOrWhiteSpace(IfIp))
        {
            ReqStatus = "请填写接口名称和IP地址";
            return null;
        }
        return new InterfaceIpRequirement
        {
            DeviceName = SelectedReqDevice!.Name,
            InterfaceName = IfName,
            IpAddress = IfIp,
            SubnetMask = IfMask
        };
    }

    private VlanRequirement? ParseVlan()
    {
        if (!int.TryParse(VlanId, out var id))
        {
            ReqStatus = "请输入有效的 VLAN ID";
            return null;
        }
        return new VlanRequirement
        {
            DeviceName = SelectedReqDevice!.Name,
            VlanId = id,
            VlanName = VlanName,
            AccessPorts = VlanAccessPorts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList(),
            TrunkPorts = VlanTrunkPorts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList()
        };
    }

    private OspfRequirement? ParseOspf()
    {
        if (!int.TryParse(OspfProcessId, out var pid) || string.IsNullOrWhiteSpace(OspfRouterId))
        {
            ReqStatus = "请填写 OSPF 进程ID和Router ID";
            return null;
        }
        return new OspfRequirement
        {
            DeviceName = SelectedReqDevice!.Name,
            ProcessId = pid,
            RouterId = OspfRouterId,
            Areas = new List<OspfArea>
            {
                new OspfArea
                {
                    AreaId = OspfAreaId,
                    Networks = OspfNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList()
                }
            }
        };
    }

    private StaticRouteRequirement? ParseStaticRoute()
    {
        if (string.IsNullOrWhiteSpace(RouteDest) || string.IsNullOrWhiteSpace(RouteNextHop))
        {
            ReqStatus = "请填写目标网络和下一跳";
            return null;
        }
        return new StaticRouteRequirement
        {
            DeviceName = SelectedReqDevice!.Name,
            DestinationNetwork = RouteDest,
            SubnetMask = RouteMask,
            NextHop = RouteNextHop,
            OutInterface = RouteOutIf
        };
    }

    private AclRequirement? ParseAcl()
    {
        if (!int.TryParse(AclNumber, out var num))
        {
            ReqStatus = "请输入有效的 ACL 编号";
            return null;
        }
        return new AclRequirement
        {
            DeviceName = SelectedReqDevice!.Name,
            AclNumber = num,
            Rules = new List<AclRule>
            {
                new AclRule
                {
                    Action = AclAction,
                    Protocol = AclProtocol,
                    SourceIp = AclSource,
                    DestIp = AclDest
                }
            }
        };
    }

    [RelayCommand]
    private void DeleteRequirement(TaskRequirement req)
    {
        _session.Requirements.Remove(req);
        AddedRequirements.Remove(req);
        NotifyRequirementsChanged();
        ReqStatus = "已删除需求";
    }

    // ── Section 3: Config Generation ────────────────────────────────

    [RelayCommand]
    private async Task GenerateConfigs()
    {
        if (_session.Topology == null)
        {
            GenStatusMessage = "未加载拓扑，请先导入 .topo 文件";
            return;
        }

        if (_session.Requirements.Count == 0 && string.IsNullOrWhiteSpace(_session.RawRequirementText))
        {
            GenStatusMessage = "未定义需求，请先添加任务需求或粘贴文本描述";
            return;
        }

        try
        {
            IsGenerating = true;
            ChatMessages.Clear();
            StartElapsedTimer();
            GenStatusMessage = "正在通过 AI 生成配置...";

            var (reachable, latency, err) = await _aiGenerator.TestConnectivityAsync();

            if (!reachable)
            {
                AddChatMessage("status", $"✗ 连通性测试失败 ({latency}ms): {err}");
                AddChatMessage("status", "⚠ 回退到模板引擎...");
                _session.Configs = _fallbackGenerator.Generate(_session.Topology, _session.Requirements);
                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"✓ 已用模板引擎为 {_session.Configs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("模板");
                return;
            }

            AddChatMessage("status", $"✓ 连通性测试通过 ({latency}ms) — 正在发送请求...");

            var aiConfigs = await _aiGenerator.GenerateAsync(_session.Topology, _session.Requirements, _session.RawRequirementText);

            if (aiConfigs != null && aiConfigs.Count > 0)
            {
                _session.Configs = aiConfigs;
                AddChatMessage("status", "━━━ AI 原始响应 ━━━");
                AddChatMessage("ai", _aiGenerator.LastRawResponse);
                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"✓ AI 已为 {aiConfigs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("AI");

                // Auto-verify
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await Application.Current.Dispatcher.InvokeAsync(VerifyConfigsInternalAsync);
                });
            }
            else
            {
                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"⚠ AI 生成失败 — {_aiGenerator.LastError}，回退到模板引擎...");
                _session.Configs = _fallbackGenerator.Generate(_session.Topology, _session.Requirements);
                AddChatMessage("status", $"✓ 已用模板引擎为 {_session.Configs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("模板");
            }
        }
        catch (Exception ex)
        {
            AddChatMessage("status", $"✗ 生成过程异常: {ex.Message}");
            GenStatusMessage = $"生成失败: {ex.Message}";
        }
        finally
        {
            if (_elapsedSw.IsRunning)
                StopElapsedTimer();
        }
    }

    private void AddChatMessage(string role, string content)
    {
        ChatMessages.Add(new ChatMessage { Role = role, Content = content });
    }

    private void StartElapsedTimer()
    {
        _elapsedSw.Restart();
        ElapsedTime = "0:00";
        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnElapsedTick, Application.Current.Dispatcher);
        _elapsedTimer.Start();
    }

    private string StopElapsedTimer()
    {
        _elapsedSw.Stop();
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        IsGenerating = false;
        var result = FormatElapsed(_elapsedSw.Elapsed);
        ElapsedTime = string.Empty;
        return result;
    }

    private void OnElapsedTick(object? sender, EventArgs e)
    {
        ElapsedTime = FormatElapsed(_elapsedSw.Elapsed);
    }

    private static string FormatElapsed(TimeSpan ts)
        => ts.TotalSeconds < 60
            ? $"{ts.Seconds} 秒"
            : $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒";

    private void OnConfigsReady(string source)
    {
        DeviceConfigs = new ObservableCollection<DeviceConfig>(_session.Configs);
        if (DeviceConfigs.Count > 0)
            SelectedDeviceConfig = DeviceConfigs[0];
        GenStatusMessage = $"({source}) 已为 {_session.Configs.Count} 台设备生成配置";
    }

    partial void OnSelectedDeviceConfigChanged(DeviceConfig? value)
    {
        if (value != null)
            ConfigText = value.RenderAll();
    }

    [RelayCommand]
    private void ShowAllConfigs()
    {
        if (_session.Configs.Count == 0) return;
        ConfigText = _exporter.RenderAllConfigs(_session.Configs);
        SelectedDeviceConfig = null;
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (string.IsNullOrEmpty(ConfigText)) return;
        Clipboard.SetText(ConfigText);
        GenStatusMessage = "配置已复制到剪贴板";
    }

    [RelayCommand]
    private void ExportToFiles()
    {
        if (_session.Configs.Count == 0) return;
        var outputDir = GetOutputDir();
        _exporter.ExportAll(_session.Configs, outputDir);
        GenStatusMessage = $"已导出 {_session.Configs.Count} 个配置文件到 {outputDir}";
    }

    private string GetOutputDir()
    {
        if (!string.IsNullOrWhiteSpace(_apiConfig.ConfigOutputPath))
            return _apiConfig.ConfigOutputPath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ENSP.ZD", "配置输出");
    }

    [RelayCommand]
    private async Task VerifyConfigs()
    {
        if (_session.Configs.Count == 0)
        {
            AddChatMessage("status", "没有可验证的配置");
            return;
        }
        await VerifyConfigsInternalAsync();
    }

    private async Task VerifyConfigsInternalAsync()
    {
        if (IsVerifying || _session.Configs.Count == 0) return;

        try
        {
            IsVerifying = true;
            AddChatMessage("status", "━━━ 配置验证 ━━━");

            await VerifyOnce();
        }
        catch (Exception ex)
        {
            AddChatMessage("status", $"✗ 验证异常: {ex.Message}");
        }
        finally
        {
            IsVerifying = false;
        }
    }

    private async Task VerifyOnce(bool forSection6 = false)
    {
        if (_session.Topology == null) return;

        if (forSection6)
            VerifyStatus = "正在验证...";
        else
            AddChatMessage("status", "正在验证配置...");

        var renderedConfigs = _exporter.RenderAllConfigs(_session.Configs);
        var result = await _aiGenerator.VerifyAsync(
            _session.Topology, _session.Requirements,
            _session.RawRequirementText, renderedConfigs);

        if (result == null)
        {
            var msg = $"✗ 验证失败 — {_aiGenerator.LastError}";
            if (forSection6) { VerifyResultText = msg; VerifyStatus = msg; }
            else AddChatMessage("status", msg);
            return;
        }

        if (forSection6)
            VerifyResultText = result;
        else
        {
            AddChatMessage("status", "━━━ AI 验证结果 ━━━");
            AddChatMessage("ai", result);
        }

        bool passed = result.Contains("✓ 验证通过") && !result.Contains("✗ 存在问题");
        if (passed)
        {
            var passMsg = "✓ 验证通过 — 配置满足所有需求";
            if (forSection6) { VerifyStatus = passMsg; VerifyResultText = result; }
            else { AddChatMessage("status", passMsg); GenStatusMessage = "AI 验证通过 — 配置满足所有需求"; }
        }
        else
        {
            var warnMsg = "⚠ 验证发现问题 — 请查看结果，手动修改后重新生成";
            if (forSection6) { VerifyStatus = warnMsg; }
            else { AddChatMessage("status", warnMsg); GenStatusMessage = "AI 验证发现问题 — 请查看验证结果"; }
        }
    }

    [RelayCommand]
    private void ClearAllCache()
    {
        _session.Configs.Clear();
        DeviceConfigs.Clear();
        ChatMessages.Clear();
        ConfigText = string.Empty;
        SelectedDeviceConfig = null;
        GenStatusMessage = "已清除所有配置缓存";
    }

    // ── Section 4: Device Control ───────────────────────────────────

    private void RefreshDevices()
    {
        Devices.Clear();
        if (_session.Topology == null) return;

        foreach (var dev in _session.Topology.Devices)
        {
            var hasConsole = dev.ConsolePort > 0;
            Devices.Add(new DeviceItem
            {
                Name = dev.Name,
                DeviceType = dev.Type,
                ConsolePort = dev.ConsolePort,
                HasConsole = hasConsole,
                Address = hasConsole ? $"localhost:{dev.ConsolePort}" : "(无端口)",
                CanvasX = dev.X,
                CanvasY = dev.Y,
                Model = dev.Model
            });
        }
    }

    partial void OnSelectedDeviceChanged(DeviceItem? value)
    {
        if (value != null)
        {
            TerminalOutput = value.TerminalOutput;
            IsConnected = value.IsConnected;
        }
        else
        {
            TerminalOutput = string.Empty;
            IsConnected = false;
        }
        UpdateStatus();
    }

    // ── Telnet helpers ─────────────────────────────────────────────

    private TelnetConnection CreateConnection(DeviceItem device)
    {
        var telnet = new TelnetService();
        var outputBuilder = new StringBuilder();

        void OnData(string data)
        {
            Dispatch(() =>
            {
                outputBuilder.Append(data);
                if (outputBuilder.Length > 65536)
                {
                    var cutPos = outputBuilder.Length - 32768;
                    var nl = outputBuilder.ToString().IndexOf('\n', cutPos);
                    outputBuilder.Remove(0, nl >= 0 ? nl + 1 : cutPos);
                }
                device.TerminalOutput = outputBuilder.ToString();
                if (SelectedDevice == device)
                    TerminalOutput = device.TerminalOutput;
            });
        }

        void OnConnected(bool connected)
        {
            Dispatch(() =>
            {
                device.IsConnected = connected;
                if (SelectedDevice == device)
                    IsConnected = connected;
                if (!connected)
                {
                    if (_connections.Remove(device.Name, out var c))
                        c.Dispose();
                }
                UpdateStatus();
            });
        }

        telnet.DataReceived += OnData;
        telnet.ConnectionChanged += OnConnected;

        return new TelnetConnection(telnet, () =>
        {
            telnet.DataReceived -= OnData;
            telnet.ConnectionChanged -= OnConnected;
        });
    }

    private async Task ConnectDeviceAsyncInternal(DeviceItem device)
    {
        if (!device.HasConsole || _connections.ContainsKey(device.Name))
            return;

        device.TerminalOutput = string.Empty;

        var conn = CreateConnection(device);
        try
        {
            await conn.Telnet.ConnectAsync("localhost", device.ConsolePort);
            _connections[device.Name] = conn;
            device.IsConnected = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Connect failed for {device.Name}: {ex.Message}");
            conn.Dispose();
        }
    }

    private async Task SendCommandsAsync(TelnetService telnet, DeviceItem device, List<ConfigCommand> commands)
    {
        // 1. Wake device
        await telnet.SendAsync("\r\n");
        await Task.Delay(200);

        // 2. Enter system-view
        Dispatch(() =>
        {
            device.TerminalOutput += "\r\n> sys\r\n";
            if (SelectedDevice == device)
                TerminalOutput = device.TerminalOutput;
        });
        await telnet.SendAsync("sys");
        await Task.Delay(500);

        // 3. Send each command
        foreach (var cmd in commands)
        {
            string trimmed = cmd.Command.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('!') || trimmed.StartsWith('#'))
                continue;

            Dispatch(() =>
            {
                device.TerminalOutput += $"\r\n> {trimmed}\r\n";
                if (SelectedDevice == device)
                    TerminalOutput = device.TerminalOutput;
            });
            await telnet.SendAsync(trimmed);
            await Task.Delay(80);
        }

        // 4. Return to user view
        Dispatch(() =>
        {
            device.TerminalOutput += "\r\n> return\r\n";
            if (SelectedDevice == device)
                TerminalOutput = device.TerminalOutput;
        });
        await telnet.SendAsync("return");
        await Task.Delay(500);

        // 5. Save configuration
        Dispatch(() =>
        {
            device.TerminalOutput += "\r\n> save\r\n> y\r\n";
            if (SelectedDevice == device)
                TerminalOutput = device.TerminalOutput;
        });
        await telnet.SendAsync("save");
        await Task.Delay(800);
        await telnet.SendAsync("y");
        await Task.Delay(800);
    }

    private static void CancelDeviceStartup(DeviceItem device)
    {
        try { device._startupCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void UpdateStatus()
    {
        if (IsConnected && SelectedDevice != null)
            ControlStatus = $"已连接 {SelectedDevice.Name} (localhost:{SelectedDevice.ConsolePort})";
        else if (SelectedDevice != null)
            ControlStatus = $"已断开 — {SelectedDevice.Name}";
        else
            ControlStatus = "就绪 — 选择设备并连接";
    }

    [RelayCommand]
    private async Task ConnectDevice(DeviceItem? device)
    {
        if (device == null || !device.HasConsole)
        {
            ControlStatus = "该设备没有控制台端口";
            return;
        }

        if (_connections.ContainsKey(device.Name))
        {
            SelectedDevice = device;
            return;
        }

        SelectedDevice = device;
        device.TerminalOutput = string.Empty;
        TerminalOutput = string.Empty;
        ControlStatus = $"正在连接 {device.Name} (localhost:{device.ConsolePort})...";

        var conn = CreateConnection(device);
        try
        {
            await conn.Telnet.ConnectAsync("localhost", device.ConsolePort);
            _connections[device.Name] = conn;
            device.IsConnected = true;
            IsConnected = true;
            ControlStatus = $"已连接 {device.Name} (localhost:{device.ConsolePort})";
        }
        catch (Exception ex)
        {
            conn.Dispose();
            var hint = ex.Message.Contains("refused") || ex.Message.Contains("closed")
                ? " — 设备可能未启动，请在 eNSP 中启动设备后重试"
                : "";
            ControlStatus = $"连接失败: {ex.Message}{hint}";
        }
    }

    [RelayCommand]
    private async Task ConnectAllDevices()
    {
        var connectable = Devices.Where(d => d.HasConsole).ToList();
        if (connectable.Count == 0)
        {
            ControlStatus = "没有可连接的设备";
            return;
        }

        IsConnecting = true;
        ControlStatus = $"正在连接 {connectable.Count} 个设备...";

        try
        {
            var tasks = connectable.Select(d => ConnectDeviceAsyncInternal(d)).ToArray();
            await Task.WhenAll(tasks);

            var connected = connectable.Count(d => d.IsConnected);
            var failed = connectable.Count - connected;
            ControlStatus = connected > 0
                ? $"已连接 {connected} 个设备" + (failed > 0 ? $"，{failed} 个失败" : "")
                : "所有设备连接失败 — 请在 eNSP 中启动设备后重试";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void DisconnectDevice()
    {
        if (SelectedDevice == null) return;

        if (_connections.Remove(SelectedDevice.Name, out var conn))
        {
            conn.Dispose();
            ControlStatus = $"已断开 {SelectedDevice.Name}";
        }
        else
        {
            ControlStatus = $"{SelectedDevice.Name} 未连接";
        }

        SelectedDevice.IsConnected = false;
        IsConnected = false;
    }

    [RelayCommand]
    private void DisconnectAllDevices()
    {
        foreach (var (_, conn) in _connections.ToList())
            conn.Dispose();
        _connections.Clear();

        foreach (var d in Devices)
            d.IsConnected = false;

        IsConnected = false;
        ControlStatus = "已断开所有设备";
    }

    [RelayCommand]
    private async Task SendCommand()
    {
        if (!IsConnected || SelectedDevice == null || string.IsNullOrWhiteSpace(CommandInput))
            return;

        if (!_connections.TryGetValue(SelectedDevice.Name, out var conn))
            return;

        var cmd = CommandInput.Trim();
        CommandInput = string.Empty;

        SelectedDevice.TerminalOutput += $"\r\n> {cmd}\r\n";
        TerminalOutput = SelectedDevice.TerminalOutput;

        await conn.Telnet.SendAsync(cmd);
    }

    [RelayCommand]
    private async Task PushConfigToSelectedDevice()
    {
        if (SelectedDevice == null || !SelectedDevice.IsConnected)
        {
            ControlStatus = "请先选择并连接设备";
            return;
        }

        if (!_connections.TryGetValue(SelectedDevice.Name, out var conn))
        {
            ControlStatus = "设备未连接";
            return;
        }

        var config = _session.Configs.Find(c =>
            c.DeviceName.Equals(SelectedDevice.Name, StringComparison.OrdinalIgnoreCase));
        if (config == null)
        {
            ControlStatus = $"未找到 {SelectedDevice.Name} 的配置 — 请先生成配置";
            return;
        }

        var commands = config.Sections.SelectMany(s => s.Commands).ToList();
        if (commands.Count == 0) return;

        ControlStatus = $"正在推送配置到 {SelectedDevice.Name} ({commands.Count} 条命令)...";
        await SendCommandsAsync(conn.Telnet, SelectedDevice, commands);
        ControlStatus = $"已推送 {commands.Count} 条命令到 {SelectedDevice.Name}";
    }

    [RelayCommand]
    private async Task PushConfigToAllDevices()
    {
        if (_session.Configs.Count == 0)
        {
            ControlStatus = "没有可用的配置 — 请先生成配置";
            return;
        }

        var connected = Devices.Where(d => d.IsConnected && _connections.ContainsKey(d.Name)).ToList();
        if (connected.Count == 0)
        {
            ControlStatus = "没有已连接的设备 — 请先连接设备";
            return;
        }

        IsPushing = true;
        try
        {
            int pushed = 0;
            foreach (var device in connected)
            {
                var config = _session.Configs.Find(c =>
                    c.DeviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
                if (config == null) continue;

                if (!_connections.TryGetValue(device.Name, out var conn)) continue;

                var commands = config.Sections.SelectMany(s => s.Commands).ToList();
                if (commands.Count == 0) continue;

                ControlStatus = $"正在推送配置到 {device.Name} ({commands.Count} 条命令)...";
                await SendCommandsAsync(conn.Telnet, device, commands);
                pushed++;
            }
            ControlStatus = pushed > 0
                ? $"已推送配置到 {pushed} 个设备"
                : "没有匹配的配置";
        }
        finally
        {
            IsPushing = false;
        }
    }

    [RelayCommand]
    private async Task OpenDeviceConfigPopupAsync()
    {
        if (SelectedDevice == null || !SelectedDevice.HasConsole)
        {
            ControlStatus = "请先选择一个有控制台端口的设备";
            return;
        }

        try
        {
            var window = App.Services.GetRequiredService<Views.Windows.DeviceConfigWindow>();
            var vm = App.Services.GetRequiredService<ViewModels.Windows.DeviceConfigWindowViewModel>();
            vm.Initialize(SelectedDevice.Name, SelectedDevice.ConsolePort, SelectedDevice.Model);
            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.Show();
            ControlStatus = $"已打开 {SelectedDevice.Name} 配置编辑窗口";
        }
        catch (Exception ex)
        {
            ControlStatus = $"打开配置窗口失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task FetchDeviceConfigAsync()
    {
        if (SelectedDevice == null || !SelectedDevice.IsConnected)
        {
            ControlStatus = "请先选择并连接设备";
            return;
        }

        if (!_connections.TryGetValue(SelectedDevice.Name, out var conn))
        {
            ControlStatus = "设备未连接";
            return;
        }

        ControlStatus = $"正在获取 {SelectedDevice.Name} 运行配置...";
        try
        {
            await conn.Telnet.SendAsync("display current-configuration");
            await Task.Delay(2000);
            ControlStatus = $"{SelectedDevice.Name} 配置获取完成 — 请查看终端输出";
        }
        catch (Exception ex)
        {
            ControlStatus = $"{SelectedDevice.Name} 配置获取失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartAllDevices()
    {
        var offDevices = Devices.Where(d => d.HasConsole && d.RuntimeState == DeviceRuntimeState.Off).ToList();
        if (offDevices.Count == 0)
        {
            ControlStatus = "所有设备已启动或正在运行";
            return;
        }

        IsStartingDevices = true;
        var batchCts = new CancellationTokenSource();
        try
        {
            // 1. Ensure eNSP is running — launch with topology file if not
            if (!EnspGuiAutomationService.IsEnspRunning())
            {
                if (string.IsNullOrWhiteSpace(_session.TopologyFilePath) || !File.Exists(_session.TopologyFilePath))
                {
                    ControlStatus = "eNSP 未运行且无拓扑文件 — 请先导入拓扑";
                    return;
                }

                ControlStatus = "正在启动 eNSP 并打开拓扑...";
                EnspGuiAutomationService.LaunchEnsp(_session.TopologyFilePath);

                ControlStatus = "等待 eNSP 窗口就绪...";
                var hwnd = await EnspGuiAutomationService.WaitForEnspWindowReadyAsync(60);
                if (hwnd == IntPtr.Zero)
                {
                    // eNSP might still be loading — wait a bit more and retry
                    await Task.Delay(5000);
                    hwnd = await EnspGuiAutomationService.WaitForEnspWindowReadyAsync(30);
                }
                if (hwnd == IntPtr.Zero)
                {
                    ControlStatus = "eNSP 窗口未就绪 — 请手动打开 eNSP 后重试";
                    return;
                }
            }

            // 2. Click eNSP toolbar "start all" button (reference ReNSP pattern)
            ControlStatus = "正在点击 eNSP 工具栏「启动全部设备」...";
            var error = await _guiAuto.ClickToolbarButtonAsync("start_all", batchCts.Token);

            if (error != null)
            {
                ControlStatus = $"启动失败: {error}";
                return;
            }

            // 2.5 Wait for device TCP ports to become ready (reference ReNSP port polling)
            var expectedPorts = offDevices.Select(d => d.ConsolePort).Where(p => p > 0).ToHashSet();
            if (expectedPorts.Count > 0)
            {
                ControlStatus = "等待设备端口就绪...";
                for (int attempt = 0; attempt < 45; attempt++)
                {
                    batchCts.Token.ThrowIfCancellationRequested();
                    var listening = await Task.Run(() => DeviceStartupService.ScanListeningPorts());
                    int open = expectedPorts.Count(p => listening.Contains(p));
                    if (open == expectedPorts.Count) break;
                    ControlStatus = $"等待设备端口就绪 ({open}/{expectedPorts.Count})...";
                    await Task.Delay(2000, batchCts.Token);
                }
            }

            // 2.6 Stabilization delay — let devices finish internal boot after ports open
            ControlStatus = "等待设备内部启动完成...";
            await Task.Delay(8000, batchCts.Token);

            // 3. Connect to all devices via direct Telnet
            ControlStatus = "设备启动中，等待 CLI 就绪...";
            int total = offDevices.Count;

            foreach (var d in offDevices)
            {
                d.IsBusy = true;
                d.RuntimeState = DeviceRuntimeState.Booting;
                d.RuntimeStatusText = "连接中...";
            }

            var connTasks = offDevices.Select(d => ConnectDeviceAsyncInternal(d)).ToArray();
            await Task.WhenAll(connTasks);

            foreach (var d in offDevices)
            {
                d.IsBusy = false;
                d.RuntimeState = d.IsConnected ? DeviceRuntimeState.Ready : DeviceRuntimeState.Error;
                d.RuntimeStatusText = d.IsConnected ? "就绪" : "连接失败";
            }

            int finalReady = offDevices.Count(d => d.IsConnected);
            ControlStatus = $"启动完成: {finalReady}/{total} 个设备就绪";
        }
        catch (OperationCanceledException)
        {
            ControlStatus = "启动已取消";
        }
        finally
        {
            IsStartingDevices = false;
            batchCts.Dispose();
        }
    }

    /// <summary>Sends a test command to each connected device. Reconnects devices that don't respond.</summary>
    private async Task VerifyAndRetryConnectionsAsync(List<DeviceItem> devices, CancellationToken ct = default)
    {
        var promptPattern = new System.Text.RegularExpressions.Regex(
            @"<[^>]+>|\[[^\]]+\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var device in devices)
        {
            ct.ThrowIfCancellationRequested();
            if (!_connections.TryGetValue(device.Name, out var conn))
                continue;

            device.RuntimeStatusText = "验证连接...";
            await conn.Telnet.SendAsync("\r\n");
            await Task.Delay(300, ct);

            // Quick check: if still connected, assume alive
            if (device.IsConnected)
            {
                device.RuntimeState = DeviceRuntimeState.Ready;
                device.RuntimeStatusText = "就绪";
            }
            else
            {
                // Retry once
                device.RuntimeStatusText = "重新连接...";
                Debug.WriteLine($"[{device.Name}] 验证失败，重连中...");
                if (_connections.Remove(device.Name, out var old))
                    old.Dispose();

                await ConnectDeviceAsyncInternal(device);
                await Task.Delay(500, ct);

                if (device.IsConnected)
                {
                    device.RuntimeState = DeviceRuntimeState.Ready;
                    device.RuntimeStatusText = "就绪";
                }
                else
                {
                    device.RuntimeState = DeviceRuntimeState.Error;
                    device.RuntimeStatusText = "无响应";
                }
            }
        }
    }

    [RelayCommand]
    private async Task StopAllDevices()
    {
        var runningDevices = Devices.Where(d => d.HasConsole && d.RuntimeState != DeviceRuntimeState.Off).ToList();
        if (runningDevices.Count == 0)
        {
            ControlStatus = "没有运行中的设备";
            return;
        }

        IsStoppingDevices = true;
        try
        {
            foreach (var (_, conn) in _connections.ToList())
                conn.Dispose();
            _connections.Clear();

            foreach (var device in runningDevices)
            {
                device.IsConnected = false;

                await Task.Run(() => _vbox.StopDevice(device.Name));
                Dispatch(() =>
                {
                    device.RuntimeState = DeviceRuntimeState.Off;
                    device.RuntimeStatusText = "已停止";
                    device.IsBusy = false;
                    device.StartupProgress = 0;
                });
            }

            IsConnected = false;
            ControlStatus = $"已停止 {runningDevices.Count} 个设备";
        }
        catch (Exception ex)
        {
            ControlStatus = $"停止设备失败: {ex.Message}";
        }
        finally
        {
            IsStoppingDevices = false;
        }
    }

    private async Task SyncRuntimeStates()
    {
        if (_session.Topology == null) return;

        var running = await Task.Run(() => _vbox.ListRunningVms());
        var listening = await Task.Run(() => DeviceStartupService.ScanListeningPorts());

        foreach (var device in Devices)
        {
            bool vmRunning = running.Any(r => r.Contains(device.Name, StringComparison.OrdinalIgnoreCase));
            bool portOpen = device.ConsolePort > 0 && listening.Contains(device.ConsolePort);

            if (!vmRunning)
                device.RuntimeState = DeviceRuntimeState.Off;
            else if (portOpen)
                device.RuntimeState = DeviceRuntimeState.Ready;
            else
                device.RuntimeState = DeviceRuntimeState.Booting;

            device.RuntimeStatusText = device.RuntimeState switch
            {
                DeviceRuntimeState.Ready => "就绪",
                DeviceRuntimeState.Booting => "启动中",
                DeviceRuntimeState.Off => "未运行",
                _ => "未知"
            };
        }
    }

    // ── Section 5: Verification ─────────────────────────────────────

    [RelayCommand]
    private async Task RunVerification()
    {
        if (_session.Configs.Count == 0)
        {
            VerifyStatus = "没有可验证的配置 — 请先生成配置";
            return;
        }

        if (_session.Topology == null)
        {
            VerifyStatus = "未加载拓扑";
            return;
        }

        IsVerifying2 = true;
        VerifyStatus = "正在验证...";
        VerifyResultText = string.Empty;

        try
        {
            var renderedConfigs = _exporter.RenderAllConfigs(_session.Configs);
            var result = await _aiGenerator.VerifyAsync(
                _session.Topology, _session.Requirements,
                _session.RawRequirementText, renderedConfigs);

            if (result == null)
            {
                VerifyStatus = $"✗ 验证失败 — {_aiGenerator.LastError}";
                return;
            }

            VerifyResultText = result;

            bool passed = result.Contains("✓ 验证通过") && !result.Contains("✗ 存在问题");
            VerifyStatus = passed
                ? "✓ 验证通过 — 配置满足所有需求"
                : "⚠ 验证发现问题 — 请查看下方结果，手动修改后重新生成";
        }
        catch (Exception ex)
        {
            VerifyStatus = $"验证异常: {ex.Message}";
        }
        finally
        {
            IsVerifying2 = false;
        }
    }

    // ── Section 6: One-Click Deploy ──────────────────────────────────

    [RelayCommand]
    private async Task OneClickDeploy()
    {
        if (_session.Topology == null)
        {
            DeployStatus = "✗ 未加载拓扑 — 请先导入 .topo 文件";
            return;
        }

        if (_session.Configs.Count == 0)
        {
            DeployStatus = "✗ 没有可用的配置 — 请先在「配置生成」中生成配置";
            return;
        }

        var connected = Devices.Where(d => d.IsConnected && _connections.ContainsKey(d.Name)).ToList();
        if (connected.Count == 0)
        {
            DeployStatus = "✗ 没有已连接的设备 — 请先连接设备";
            return;
        }

        IsDeploying = true;
        int pushed = 0, skipped = 0, failed = 0;
        var failedNames = new List<string>();
        try
        {
            foreach (var device in connected)
            {
                var config = _session.Configs.Find(c =>
                    c.DeviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
                if (config == null) { skipped++; continue; }

                if (!_connections.TryGetValue(device.Name, out var conn)) { skipped++; continue; }

                var commands = config.Sections.SelectMany(s => s.Commands).ToList();
                if (commands.Count == 0) { skipped++; continue; }

                DeployStatus = $"正在推送 {device.Name} ({commands.Count} 条命令)...";

                try
                {
                    await SendCommandsAsync(conn.Telnet, device, commands);
                    pushed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    failedNames.Add($"{device.Name}({ex.Message})");
                }
            }
            var parts = new List<string>();
            if (pushed > 0) parts.Add($"{pushed} 个成功");
            if (skipped > 0) parts.Add($"{skipped} 个无匹配配置");
            if (failed > 0) parts.Add($"{failed} 个失败: {string.Join("; ", failedNames)}");
            DeployStatus = parts.Count > 0 ? string.Join("，", parts) : "✗ 没有匹配的配置 — 请先生成配置";
        }
        catch (Exception ex)
        {
            DeployStatus = $"✗ 部署异常: {ex.Message}";
        }
        finally
        {
            IsDeploying = false;
        }
    }

    // ── Timer helpers ───────────────────────────────────────────────

    private void StartRefreshTimer()
    {
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _refreshTimer.WaitForNextTickAsync(_refreshCts.Token))
                {
                    await SyncRuntimeStates();
                }
            }
            catch (OperationCanceledException) { }
        }, _refreshCts.Token);
    }

    private void StopRefreshTimer()
    {
        _refreshCts?.Cancel();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app == null) return;

        try
        {
            app.Dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException) { }
    }
}
