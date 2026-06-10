using ENSP.ZD.Models.Configuration;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ENSP.ZD.Services;

public enum DeviceConnectionState
{
    Idle,
    TcpConnecting,
    WaitingPrompt,
    PromptReady,
    FetchingConfig,
    ConfigReady,
    PushingConfig,
    Failed,
    Timeout,
    Disconnected
}

public class DeviceSessionViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public string DeviceName { get; init; } = string.Empty;
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; }

    private DeviceConnectionState _state = DeviceConnectionState.Idle;
    public DeviceConnectionState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(IsConnected)); }
    }

    public string StateText => State switch
    {
        DeviceConnectionState.Idle => "离线",
        DeviceConnectionState.TcpConnecting => "连接中",
        DeviceConnectionState.WaitingPrompt => "等待就绪",
        DeviceConnectionState.PromptReady => "CLI就绪",
        DeviceConnectionState.FetchingConfig => "获取配置",
        DeviceConnectionState.ConfigReady => "完成",
        DeviceConnectionState.PushingConfig => "推送配置",
        DeviceConnectionState.Failed => "失败",
        DeviceConnectionState.Timeout => "超时",
        DeviceConnectionState.Disconnected => "已断开",
        _ => ""
    };

    public bool IsConnected => State is DeviceConnectionState.PromptReady
        or DeviceConnectionState.FetchingConfig
        or DeviceConnectionState.ConfigReady
        or DeviceConnectionState.PushingConfig;

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); }
    }

    private string _terminalOutput = string.Empty;
    public string TerminalOutput
    {
        get => _terminalOutput;
        set { _terminalOutput = value; OnPropertyChanged(nameof(TerminalOutput)); }
    }

    private ParsedDeviceConfig? _parsedConfig;
    public ParsedDeviceConfig? ParsedConfig
    {
        get => _parsedConfig;
        set { _parsedConfig = value; OnPropertyChanged(nameof(ParsedConfig)); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public partial class DeviceConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, (TcpClient Client, NetworkStream Stream)> _connections = new();
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;

    public ObservableCollection<DeviceSessionViewModel> Sessions { get; } = new();

    public void Initialize(IEnumerable<(string DeviceName, int Port)> devices)
    {
        CancelAll();
        Sessions.Clear();
        foreach (var (name, port) in devices.Where(d => d.Port > 0))
        {
            Sessions.Add(new DeviceSessionViewModel
            {
                DeviceName = name,
                Port = port
            });
        }
    }

    public void AddSession(string deviceName, int port)
    {
        if (Sessions.Any(s => s.DeviceName == deviceName)) return;
        Sessions.Add(new DeviceSessionViewModel
        {
            DeviceName = deviceName,
            Port = port
        });
    }

    public async Task ConnectAsync(string deviceName, CancellationToken ct = default)
    {
        var session = Sessions.FirstOrDefault(s => s.DeviceName == deviceName);
        if (session == null) return;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromMinutes(3));
        await ConnectAndFetchAsync(session, linkedCts.Token);
    }

    public void StartAll()
    {
        // Cancel any previous StartAll invocation to avoid duplicate tasks competing
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        foreach (var session in Sessions)
        {
            var s = session;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConnectAndFetchAsync(s, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    s.State = DeviceConnectionState.Failed;
                    s.ErrorMessage = ex.Message;
                    Debug.WriteLine($"[{s.DeviceName}] 异常: {ex.Message}");
                }
            }, token);
        }
    }

    public void Disconnect(string deviceName)
    {
        if (_connections.TryRemove(deviceName, out var conn))
        {
            conn.Stream?.Dispose();
            conn.Client?.Dispose();
        }

        var session = Sessions.FirstOrDefault(s => s.DeviceName == deviceName);
        if (session != null)
            session.State = DeviceConnectionState.Disconnected;
    }

    public void DisconnectAll()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        foreach (var (_, (client, stream)) in _connections)
        {
            stream?.Dispose();
            client?.Dispose();
        }
        _connections.Clear();

        foreach (var s in Sessions)
            s.State = DeviceConnectionState.Disconnected;
    }

    public void CancelAll()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        foreach (var (_, (client, stream)) in _connections)
        {
            stream?.Dispose();
            client?.Dispose();
        }
        _connections.Clear();
    }

    /// <summary>Returns true when the connection is fully stored and ready for SendCommands.</summary>
    public bool IsStored(string deviceName) => _connections.ContainsKey(deviceName);

    public async Task<(bool Success, string Message)> SendCommandsAsync(string deviceName, IEnumerable<string> commands, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(deviceName, out var conn))
            return (false, "设备未连接");

        var session = Sessions.FirstOrDefault(s => s.DeviceName == deviceName);
        if (session == null)
            return (false, "未找到设备会话");

        try
        {
            var stream = conn.Stream;
            session.State = DeviceConnectionState.PushingConfig;

            // Wake
            byte[] enter = Encoding.ASCII.GetBytes("\r\n");
            await stream.WriteAsync(enter, ct);
            await Task.Delay(200, ct);
            await FlushStreamAsync(stream, ct);

            // Enter system-view and wait for bracket prompt to confirm
            byte[] sysCmd = Encoding.ASCII.GetBytes("sys\r\n");
            await stream.WriteAsync(sysCmd, ct);
            await Task.Delay(500, ct);

            // Drain response and confirm system-view prompt [Huawei] appears
            var sysSb = new StringBuilder();
            byte[] sysBuf = new byte[4096];
            var sysPromptPattern = new Regex(@"\[[^\]]+\]", RegexOptions.Compiled);
            DateTime sysStart = DateTime.Now;
            while ((DateTime.Now - sysStart).TotalSeconds < 5)
            {
                if (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(sysBuf, 0, sysBuf.Length, ct);
                    if (n > 0) sysSb.Append(Encoding.ASCII.GetString(sysBuf, 0, n));
                }
                else if (sysSb.Length > 0)
                {
                    await Task.Delay(200, ct);
                    if (!stream.DataAvailable) break;
                }
                else
                {
                    await Task.Delay(200, ct);
                }
            }
            if (!sysPromptPattern.IsMatch(sysSb.ToString()))
            {
                // Not in system-view yet — flush and continue (some devices may be slow)
                await Task.Delay(500, ct);
                await FlushStreamAsync(stream, ct);
            }

            // Send each command
            int sent = 0;
            foreach (var cmd in commands)
            {
                string trimmed = cmd.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('!') || trimmed.StartsWith('#'))
                    continue;

                session.TerminalOutput += $"\r\n> {trimmed}\r\n";

                byte[] cmdBytes = Encoding.ASCII.GetBytes($"{trimmed}\r\n");
                await stream.WriteAsync(cmdBytes, ct);
                await Task.Delay(50, ct);

                // Drain immediate response and append
                if (stream.DataAvailable)
                {
                    try
                    {
                        byte[] respBuf = new byte[4096];
                        int n = await stream.ReadAsync(respBuf, 0, respBuf.Length, ct);
                        if (n > 0)
                            session.TerminalOutput += Encoding.ASCII.GetString(respBuf, 0, n);
                    }
                    catch { /* best-effort */ }
                }

                sent++;
            }

            // Return to user view before save
            byte[] retCmd = Encoding.ASCII.GetBytes("return\r\n");
            await stream.WriteAsync(retCmd, ct);
            await Task.Delay(500, ct);
            await FlushStreamAsync(stream, ct);

            // Save configuration
            byte[] saveCmd = Encoding.ASCII.GetBytes("save\r\n");
            await stream.WriteAsync(saveCmd, ct);
            await Task.Delay(800, ct);
            byte[] confirm = Encoding.ASCII.GetBytes("y\r\n");
            await stream.WriteAsync(confirm, ct);
            await Task.Delay(800, ct);
            sent++;

            // Drain response
            await Task.Delay(500, ct);
            await FlushStreamAsync(stream, ct);

            session.State = DeviceConnectionState.PromptReady;
            return (true, $"已发送 {sent} 条命令（已保存）");
        }
        catch (Exception ex)
        {
            session.State = DeviceConnectionState.Failed;
            session.ErrorMessage = ex.Message;
            return (false, $"发送失败: {ex.Message}");
        }
    }

    public async Task<string?> SendCommandAndReadAsync(string deviceName, string command, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(deviceName, out var conn))
            return null;

        try
        {
            var stream = conn.Stream;
            byte[] cmdBytes = Encoding.ASCII.GetBytes($"{command.Trim()}\r\n");
            await stream.WriteAsync(cmdBytes, ct);
            await Task.Delay(300, ct);
            return await DrainResponseAsync(stream, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{deviceName}] 命令响应读取失败: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> DrainResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        byte[] buffer = new byte[4096];
        try
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < 2)
            {
                if (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(buffer, ct);
                    if (n > 0) sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }
                else if (sb.Length > 0)
                {
                    await Task.Delay(200, ct);
                    if (!stream.DataAvailable) break;
                }
                else
                {
                    await Task.Delay(100, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"DrainResponseAsync: {ex.Message}"); }
        return sb.ToString();
    }

    public async Task<DeviceConfigSnapshot?> FetchConfigAsync(string deviceName, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(deviceName, out var conn))
            return null;

        var session = Sessions.FirstOrDefault(s => s.DeviceName == deviceName);
        if (session == null) return null;

        try
        {
            session.State = DeviceConnectionState.FetchingConfig;
            var stream = conn.Stream;

            string rawConfig = await FetchDisplayConfigAsync(stream, ct);
            var parsed = DeviceConfigParser.Parse(rawConfig);
            session.ParsedConfig = parsed;

            var snapshot = new DeviceConfigSnapshot
            {
                DeviceName = deviceName,
                ConsolePort = session.Port,
                LastFetchTime = DateTime.Now,
                RawConfig = rawConfig,
                ParsedConfig = parsed
            };

            session.State = DeviceConnectionState.ConfigReady;
            return snapshot;
        }
        catch (Exception ex)
        {
            session.State = DeviceConnectionState.Failed;
            session.ErrorMessage = ex.Message;
            return null;
        }
    }

    private async Task ConnectAndFetchAsync(DeviceSessionViewModel session, CancellationToken ct)
    {
        // Phase 1: TCP connect
        session.State = DeviceConnectionState.TcpConnecting;
        var client = new TcpClient();
        NetworkStream stream;
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(5000);
            await client.ConnectAsync(session.Host, session.Port, connectCts.Token);
            stream = client.GetStream();
            stream.ReadTimeout = 500;
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            session.State = DeviceConnectionState.Timeout;
            session.ErrorMessage = "TCP 连接超时 (5s)";
            client.Dispose();
            return;
        }
        catch (Exception ex)
        {
            session.State = DeviceConnectionState.Failed;
            session.ErrorMessage = ex.Message;
            client.Dispose();
            return;
        }

        try
        {
            // Phase 2: Wake device and wait for CLI prompt (exact ReNSP pattern)
            session.State = DeviceConnectionState.WaitingPrompt;
            var promptPattern = new Regex(@"<[^>]+>|\[[^\]]+\]", RegexOptions.Compiled);
            var sb = new StringBuilder();
            byte[] buffer = new byte[4096];
            byte[] enter = Encoding.ASCII.GetBytes("\r\n");
            DateTime promptStart = DateTime.Now;

            // Send initial wake-up — already-booted devices respond immediately
            for (int i = 0; i < 2; i++)
            {
                await stream.WriteAsync(enter, 0, enter.Length, ct);
                await Task.Delay(300, ct);
            }

            // Drain initial response and check for prompt
            while (stream.DataAvailable)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (n > 0)
                {
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    if (promptPattern.IsMatch(sb.ToString()))
                        goto promptFound;
                }
            }

            // Loop: stimulate → wait → read, up to 2 minutes (ReNSP: 2min / 1.5s interval)
            while ((DateTime.Now - promptStart).TotalMinutes < 2)
            {
                ct.ThrowIfCancellationRequested();

                await stream.WriteAsync(enter, 0, enter.Length, ct);
                await Task.Delay(1500, ct);

                while (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (n > 0)
                    {
                        sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                        if (promptPattern.IsMatch(sb.ToString()))
                            goto promptFound;
                    }
                }
            }

            session.State = DeviceConnectionState.Timeout;
            session.ErrorMessage = "等待 CLI 提示符超时 (2min)";
            return;

        promptFound:
            // Wait for device to fully stabilize after prompt first appears.
            // Sending commands too early (while device is still booting) can freeze the device.
            await Task.Delay(5000, ct);

            // Flush any boot output that arrived during stabilization
            await FlushStreamAsync(stream, ct);

            // Store connection first, then mark ready — so callers checking
            // PromptReady can rely on the connection being available in _connections.
            lock (_connections)
            {
                if (_connections.TryGetValue(session.DeviceName, out var old))
                {
                    old.Stream.Dispose();
                    old.Client.Dispose();
                }
                _connections[session.DeviceName] = (client, stream);
            }

            session.State = DeviceConnectionState.PromptReady;
        }
        catch (OperationCanceledException)
        {
            session.State = DeviceConnectionState.Timeout;
            session.ErrorMessage = "操作已取消";
            stream.Dispose();
            client.Dispose();
        }
        catch
        {
            session.State = DeviceConnectionState.Failed;
            session.ErrorMessage = "内部错误";
            stream.Dispose();
            client.Dispose();
            throw;
        }
    }

    private static async Task<string> FetchDisplayConfigAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] cmd = Encoding.ASCII.GetBytes("display current-configuration\r\n");
        await stream.WriteAsync(cmd, ct);

        // Give the emulated device time to start generating output
        await Task.Delay(2000, ct);

        var sb = new StringBuilder();
        byte[] buffer = new byte[4096];
        var morePattern = new Regex(@"----\s*More\s*----", RegexOptions.Compiled);
        var returnPattern = new Regex(@"^\s*return\s*$", RegexOptions.Multiline);
        byte[] space = Encoding.ASCII.GetBytes(" ");
        DateTime lastData = DateTime.Now;
        int lastMorePos = 0;

        while ((DateTime.Now - lastData).TotalSeconds < 60)
        {
            ct.ThrowIfCancellationRequested();

            if (stream.DataAvailable)
            {
                int n = await stream.ReadAsync(buffer, ct);
                if (n > 0)
                {
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    lastData = DateTime.Now;
                }
            }
            else if (sb.Length > 0)
            {
                await Task.Delay(500, ct);
                if (stream.DataAvailable)
                    continue;

                string content = sb.ToString();
                string newPart = content[lastMorePos..];

                if (morePattern.IsMatch(newPart))
                {
                    await stream.WriteAsync(space, ct);
                    await Task.Delay(500, ct);
                    lastMorePos = sb.Length;
                    lastData = DateTime.Now;
                    continue;
                }

                if (returnPattern.IsMatch(content))
                    break;

                break;
            }
            else
            {
                await Task.Delay(200, ct);
            }
        }

        // Strip ANSI and More markers
        string result = ConfigMoreStripRegex().Replace(sb.ToString(), "");
        result = DeviceConfigParser.AnsiStripRegex().Replace(result, "");
        return result;
    }

    [GeneratedRegex(@"\s*-+\s*More\s*-+\s*", RegexOptions.Compiled)]
    private static partial Regex ConfigMoreStripRegex();

    private static async Task FlushStreamAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] flush = new byte[4096];
        try
        {
            while (stream.DataAvailable)
            {
                await stream.ReadAsync(flush, ct);
            }
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAll();
    }
}
