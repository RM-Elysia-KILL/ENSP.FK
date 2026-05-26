using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Models;
using ENSP.ZD.Models.Configuration;
using ENSP.ZD.Models.Topology;
using ENSP.ZD.Services;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.ViewModels.Pages;

public partial class EnspScanViewModel : ObservableObject, INavigationAware
{
    private readonly ProjectSession _session;
    private readonly VBoxDeviceService _vbox;
    private readonly DeviceStartupService _startupService;
    private readonly EnspGuiAutomationService _guiAuto;
    private readonly DeviceConnectionManager _connectionMgr;
    private readonly Dictionary<string, TelnetConnection> _connections = new();
    private readonly HashSet<string> _subscribedSessions = new();

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
    private string _statusText = "就绪 — 选择设备并连接";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPushing;

    [ObservableProperty]
    private bool _isStartingDevices;

    [ObservableProperty]
    private bool _isStoppingDevices;

    public EnspScanViewModel(ProjectSession session, VBoxDeviceService vbox, DeviceStartupService startupService, EnspGuiAutomationService guiAuto, DeviceConnectionManager connectionMgr)
    {
        _session = session;
        _vbox = vbox;
        _startupService = startupService;
        _guiAuto = guiAuto;
        _connectionMgr = connectionMgr;

        // Bridge DeviceConnectionManager terminal output → device list
        _connectionMgr.Sessions.CollectionChanged += (_, _) =>
        {
            foreach (var session in _connectionMgr.Sessions)
                SubscribeSessionOutput(session);
        };
    }

    private void SubscribeSessionOutput(DeviceSessionViewModel session)
    {
        if (!_subscribedSessions.Add(session.DeviceName)) return;

        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DeviceSessionViewModel.TerminalOutput)) return;
            PushSessionOutputToDevice(session);
        };
    }

    private void PushSessionOutputToDevice(DeviceSessionViewModel session)
    {
        var device = Devices.FirstOrDefault(d => d.Name == session.DeviceName);
        if (device != null)
        {
            device.TerminalOutput = session.TerminalOutput;
            if (SelectedDevice?.Name == device.Name)
                TerminalOutput = session.TerminalOutput;
        }
    }

    private void SyncAllSessionOutputs()
    {
        foreach (var session in _connectionMgr.Sessions)
        {
            SubscribeSessionOutput(session);
            PushSessionOutputToDevice(session);
        }
    }

    public Task OnNavigatedToAsync()
    {
        RefreshDevices();
        SyncRuntimeStates();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        foreach (var device in Devices)
            CancelDeviceStartup(device);
        DisconnectAll();
        return Task.CompletedTask;
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

    public void RefreshDevices()
    {
        Devices.Clear();
        if (_session.Topology == null)
            return;

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

        SyncAllSessionOutputs();
    }

    [RelayCommand]
    private async Task ConnectDevice(DeviceItem? device)
    {
        if (device == null || !device.HasConsole)
        {
            StatusText = "该设备没有控制台端口";
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
        StatusText = $"正在连接 {device.Name} (localhost:{device.ConsolePort})...";

        var conn = CreateConnection(device);
        try
        {
            await conn.Telnet.ConnectAsync("localhost", device.ConsolePort);
            _connections[device.Name] = conn;
            device.IsConnected = true;
            IsConnected = true;
            StatusText = $"已连接 {device.Name} (localhost:{device.ConsolePort})";
        }
        catch (Exception ex)
        {
            conn.Dispose();
            var hint = ex.Message.Contains("refused") || ex.Message.Contains("closed")
                ? " — 设备可能未启动，请在 eNSP 中启动设备后重试"
                : "";
            StatusText = $"连接失败: {ex.Message}{hint}";
        }
    }

    [RelayCommand]
    private async Task ConnectAllDevices()
    {
        var connectable = Devices.Where(d => d.HasConsole).ToList();
        if (connectable.Count == 0)
        {
            StatusText = "没有可连接的设备";
            return;
        }

        IsConnecting = true;
        StatusText = $"正在连接 {connectable.Count} 个设备...";

        try
        {
            var tasks = connectable.Select(d => ConnectDeviceAsyncInternal(d)).ToArray();
            await Task.WhenAll(tasks);

            var connected = connectable.Count(d => d.IsConnected);
            var failed = connectable.Count - connected;
            StatusText = connected > 0
                ? $"已连接 {connected} 个设备" + (failed > 0 ? $"，{failed} 个失败" : "")
                : "所有设备连接失败 — 请在 eNSP 中启动设备后重试";
        }
        finally
        {
            IsConnecting = false;
        }
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
            System.Diagnostics.Debug.WriteLine($"Connect failed for {device.Name}: {ex.Message}");
            conn.Dispose();
        }
    }

    private TelnetConnection CreateConnection(DeviceItem device)
    {
        var telnet = new TelnetService();
        var outputBuilder = new StringBuilder();

        void OnData(string data)
        {
            Dispatch(() =>
            {
                outputBuilder.Append(data);
                // Keep only last 64KB to avoid unbounded memory growth
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

    [RelayCommand]
    private void DisconnectDevice()
    {
        if (SelectedDevice == null) return;

        if (_connections.Remove(SelectedDevice.Name, out var conn))
        {
            conn.Dispose();
            StatusText = $"已断开 {SelectedDevice.Name}";
        }
        else
        {
            StatusText = $"{SelectedDevice.Name} 未连接";
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
        StatusText = "已断开所有设备";
    }

    private void DisconnectAll()
    {
        foreach (var (_, conn) in _connections.ToList())
            conn.Dispose();
        _connections.Clear();
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
            StatusText = "请先选择并连接设备";
            return;
        }

        if (!_connections.TryGetValue(SelectedDevice.Name, out var conn))
        {
            StatusText = "设备未连接";
            return;
        }

        var config = _session.Configs.Find(c =>
            c.DeviceName.Equals(SelectedDevice.Name, StringComparison.OrdinalIgnoreCase));
        if (config == null)
        {
            StatusText = $"未找到 {SelectedDevice.Name} 的配置 — 请先生成配置";
            return;
        }

        var commands = config.Sections.SelectMany(s => s.Commands).ToList();
        if (commands.Count == 0) return;

        StatusText = $"正在推送配置到 {SelectedDevice.Name} ({commands.Count} 条命令)...";
        await SendCommandsAsync(conn.Telnet, SelectedDevice, commands);
        StatusText = $"已推送 {commands.Count} 条命令到 {SelectedDevice.Name}";
    }

    [RelayCommand]
    private async Task PushConfigToAllDevices()
    {
        if (_session.Configs.Count == 0)
        {
            StatusText = "没有可用的配置 — 请先在配置输出页面生成配置";
            return;
        }

        var connected = Devices.Where(d => d.IsConnected && _connections.ContainsKey(d.Name)).ToList();
        if (connected.Count == 0)
        {
            StatusText = "没有已连接的设备 — 请先连接设备";
            return;
        }

        IsPushing = true;
        try
        {
            var pushed = 0;
            foreach (var device in connected)
            {
                var config = _session.Configs.Find(c =>
                    c.DeviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
                if (config == null) continue;

                if (!_connections.TryGetValue(device.Name, out var conn)) continue;

                var commands = config.Sections.SelectMany(s => s.Commands).ToList();
                if (commands.Count == 0) continue;

                StatusText = $"正在推送配置到 {device.Name} ({commands.Count} 条命令)...";
                await SendCommandsAsync(conn.Telnet, device, commands);
                pushed++;
            }

            StatusText = pushed > 0
                ? $"已推送配置到 {pushed} 个设备"
                : "没有匹配的配置 — 请先生成配置";
        }
        finally
        {
            IsPushing = false;
        }
    }

    private async Task SendCommandsAsync(TelnetService telnet, DeviceItem device, List<ConfigCommand> commands)
    {
        foreach (var cmd in commands)
        {
            device.TerminalOutput += $"\r\n> {cmd.Command}\r\n";
            if (SelectedDevice == device)
                TerminalOutput = device.TerminalOutput;

            await telnet.SendAsync(cmd.Command);
            await Task.Delay(80);
        }
    }

    [RelayCommand]
    private async Task StartAllDevices()
    {
        var offDevices = Devices.Where(d => d.HasConsole && d.RuntimeState == DeviceRuntimeState.Off).ToList();
        if (offDevices.Count == 0)
        {
            StatusText = "所有设备已启动或正在运行";
            return;
        }

        IsStartingDevices = true;
        var batchCts = new CancellationTokenSource();
        try
        {
            // Phase 0: Click eNSP's "start all" toolbar button once
            StatusText = "正在点击 eNSP 工具栏「启动全部设备」...";
            var error = await _guiAuto.ClickToolbarButtonAsync("start_all", batchCts.Token);

            if (error != null)
            {
                StatusText = $"启动失败: {error}";
                return;
            }

            StatusText = $"已点击启动按钮，等待设备上线...";
            await Task.Delay(3000, batchCts.Token); // Let eNSP process the command

            // Phase 1: Parallel TCP port polling for all devices
            int total = offDevices.Count;
            int ready = 0;

            var tasks = offDevices.Select(device =>
            {
                device._startupCts = CancellationTokenSource.CreateLinkedTokenSource(batchCts.Token);
                device.IsBusy = true;

                var progress = new Progress<DeviceStartupProgress>(p =>
                {
                    Dispatch(() =>
                    {
                        device.RuntimeState = p.State;
                        device.RuntimeStatusText = p.Message;
                        device.StartupPhase = p.Phase;
                        device.StartupDetail = p.Message;
                        device.StartupProgress = p.ProgressPercent;
                        if (p.State == DeviceRuntimeState.Ready || p.State == DeviceRuntimeState.Error)
                            device.IsBusy = false;
                    });
                });

                return _startupService.WaitForDeviceReadyAsync(
                    device.Name, device.ConsolePort,
                    device.CanvasX, device.CanvasY, device.Model,
                    progress, device._startupCts.Token,
                    skipGuiAutomation: true)
                    .ContinueWith(t =>
                    {
                        device._startupCts?.Dispose();
                        device._startupCts = null;
                        if (t.IsCompletedSuccessfully && t.Result)
                            Interlocked.Increment(ref ready);
                        return t.IsCompletedSuccessfully && t.Result;
                    }, TaskScheduler.Default);
            }).ToArray();

            // Report progress while waiting
            _ = Task.Run(async () =>
            {
                while (!batchCts.Token.IsCancellationRequested)
                {
                    var currentReady = offDevices.Count(d => d.RuntimeState == DeviceRuntimeState.Ready);
                    var currentBooting = offDevices.Count(d => d.RuntimeState == DeviceRuntimeState.Booting);
                    Dispatch(() => StatusText = $"设备启动中... 就绪 {currentReady}/{total}");
                    if (currentReady + offDevices.Count(d => d.RuntimeState == DeviceRuntimeState.Error) >= total)
                        break;
                    await Task.Delay(2000, batchCts.Token);
                }
            }, batchCts.Token);

            await Task.WhenAll(tasks);

            var finalReady = offDevices.Count(d => d.RuntimeState == DeviceRuntimeState.Ready);
            StatusText = $"启动完成: {finalReady}/{total} 个设备就绪";
        }
        catch (OperationCanceledException)
        {
            StatusText = "启动已取消";
        }
        finally
        {
            batchCts.Dispose();
            IsStartingDevices = false;
        }
    }

    [RelayCommand]
    private async Task StopAllDevices()
    {
        var activeDevices = Devices.Where(d => d.RuntimeState != DeviceRuntimeState.Off).ToList();
        if (activeDevices.Count == 0) return;

        IsStoppingDevices = true;
        try
        {
            foreach (var device in activeDevices)
            {
                CancelDeviceStartup(device);
                await Task.Run(() => _vbox.StopDevice(device.Name));
                device.RuntimeState = DeviceRuntimeState.Off;
                device.RuntimeStatusText = string.Empty;
                device.IsBusy = false;
            }
            StatusText = $"已停止 {activeDevices.Count} 个设备";
        }
        finally
        {
            IsStoppingDevices = false;
        }
    }

    private async Task StartDeviceAsync(DeviceItem device)
    {
        if (device.RuntimeState != DeviceRuntimeState.Off)
            return;

        if (!device.HasConsole)
        {
            device.RuntimeState = DeviceRuntimeState.Error;
            device.RuntimeStatusText = "设备无控制台端口";
            return;
        }

        device._startupCts = new CancellationTokenSource();
        device.IsBusy = true;

        var progress = new Progress<DeviceStartupProgress>(p =>
        {
            Dispatch(() =>
            {
                device.RuntimeState = p.State;
                device.RuntimeStatusText = p.Message;
                device.StartupPhase = p.Phase;
                device.StartupDetail = p.Message;
                device.StartupProgress = p.ProgressPercent;
                if (p.State == DeviceRuntimeState.Ready || p.State == DeviceRuntimeState.Error)
                    device.IsBusy = false;
            });
        });

        try
        {
            StatusText = $"正在启动 {device.Name}...";
            var success = await _startupService.WaitForDeviceReadyAsync(
                device.Name, device.ConsolePort,
                device.CanvasX, device.CanvasY, device.Model,
                progress, device._startupCts.Token);

            StatusText = success
                ? $"{device.Name} 已就绪"
                : $"{device.Name} 启动失败: {device.RuntimeStatusText}";
        }
        catch (OperationCanceledException)
        {
            Dispatch(() =>
            {
                device.RuntimeState = DeviceRuntimeState.Off;
                device.RuntimeStatusText = string.Empty;
                device.IsBusy = false;
            });
            StatusText = $"{device.Name} 启动已取消";
        }
        catch (Exception ex)
        {
            Dispatch(() =>
            {
                device.RuntimeState = DeviceRuntimeState.Error;
                device.RuntimeStatusText = $"启动异常: {ex.Message}";
                device.IsBusy = false;
            });
            StatusText = $"{device.Name} 启动异常: {ex.Message}";
        }
        finally
        {
            device._startupCts?.Dispose();
            device._startupCts = null;
        }
    }

    private static void CancelDeviceStartup(DeviceItem device)
    {
        try { device._startupCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void SyncRuntimeStates()
    {
        // Quick pre-scan: which device ports are listening?
        var listeningPorts = DeviceStartupService.ScanListeningPorts();
        System.Diagnostics.Debug.WriteLine($"[SyncRuntime] Listening ports: [{string.Join(", ", listeningPorts.OrderBy(p => p))}]");

        foreach (var device in Devices)
        {
            if (device.ConsolePort <= 0)
            {
                device.RuntimeState = DeviceRuntimeState.Off;
                device.RuntimeStatusText = "无端口";
                continue;
            }

            if (!listeningPorts.Contains(device.ConsolePort))
            {
                device.RuntimeState = DeviceRuntimeState.Off;
                device.RuntimeStatusText = "未启动";
                continue;
            }

            // Port is listening — probe deeper to confirm readiness
            _ = ProbeAndSetStateAsync(device).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"Probe state failed for {device.Name}: {t.Exception?.InnerException?.Message}");
            }, TaskScheduler.Default);
        }
    }

    private async Task ProbeAndSetStateAsync(DeviceItem device)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", device.ConsolePort);
            var timeout = Task.Delay(500);
            var completed = await Task.WhenAny(connectTask, timeout);

            Dispatch(() =>
            {
                if (completed == connectTask && client.Connected)
                {
                    device.RuntimeState = DeviceRuntimeState.Ready;
                    device.RuntimeStatusText = "设备已就绪";
                }
                else
                {
                    device.RuntimeState = DeviceRuntimeState.Booting;
                    device.RuntimeStatusText = "虚拟机运行中 (启动中...)";
                }
            });
        }
        catch
        {
            Dispatch(() =>
            {
                device.RuntimeState = DeviceRuntimeState.Booting;
                device.RuntimeStatusText = "虚拟机运行中";
            });
        }
    }

    private void UpdateStatus()
    {
        if (IsConnected && SelectedDevice != null)
            StatusText = $"已连接 {SelectedDevice.Name} (localhost:{SelectedDevice.ConsolePort})";
        else if (SelectedDevice != null)
            StatusText = $"已断开 — {SelectedDevice.Name}";
        else
            StatusText = "就绪 — 选择设备并连接";
    }

    /// <summary>
    /// Safely dispatches action to UI thread. No-ops if application is shutting down.
    /// </summary>
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

/// <summary>
/// Bundles a TelnetService with its event unsubscription delegates for clean teardown.
/// </summary>
internal sealed class TelnetConnection : IDisposable
{
    public TelnetService Telnet { get; }
    private readonly Action _unsubscribe;
    private bool _disposed;

    public TelnetConnection(TelnetService telnet, Action unsubscribe)
    {
        Telnet = telnet;
        _unsubscribe = unsubscribe;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
        Telnet.Disconnect();
        Telnet.Dispose();
    }
}

public partial class DeviceItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public int ConsolePort { get; set; }
    public bool HasConsole { get; set; }
    public string Address { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private DeviceRuntimeState _runtimeState = DeviceRuntimeState.Off;

    [ObservableProperty]
    private string _runtimeStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _startupProgress;

    [ObservableProperty]
    private string _startupPhase = string.Empty;

    [ObservableProperty]
    private string _startupDetail = string.Empty;

    internal CancellationTokenSource? _startupCts;

    public double CanvasX { get; set; }
    public double CanvasY { get; set; }
    public string Model { get; set; } = string.Empty;

    public string DeviceTypeText => DeviceType switch
    {
        DeviceType.Router => "路由器",
        DeviceType.Switch => "交换机",
        DeviceType.Firewall => "防火墙",
        DeviceType.PC => "PC",
        DeviceType.Server => "服务器",
        _ => "未知"
    };
}
