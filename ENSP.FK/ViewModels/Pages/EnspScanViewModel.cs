using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.FK.Models.Configuration;
using ENSP.FK.Models.Topology;
using ENSP.FK.Services;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages;

public partial class EnspScanViewModel : ObservableObject, INavigationAware
{
    private readonly ProjectSession _session;
    private readonly Dictionary<string, TelnetConnection> _connections = new();

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

    public EnspScanViewModel(ProjectSession session)
    {
        _session = session;
    }

    public Task OnNavigatedToAsync()
    {
        RefreshDevices();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
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
                Address = hasConsole ? $"localhost:{dev.ConsolePort}" : "(无端口)"
            });
        }
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
        catch
        {
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

    private static async Task SendCommandsAsync(TelnetService telnet, DeviceItem device, List<ConfigCommand> commands)
    {
        foreach (var cmd in commands)
        {
            await telnet.SendAsync(cmd.Command);
            await Task.Delay(80);
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

    public string DeviceTypeText => DeviceType switch
    {
        DeviceType.Router => "路由器",
        DeviceType.Switch => "交换机",
        DeviceType.Firewall => "防火墙",
        _ => "未知"
    };
}
