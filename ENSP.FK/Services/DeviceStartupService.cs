using ENSP.ZD.Models;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ENSP.ZD.Services;

/// <summary>
/// 设备启动 + 检测。通过 eNSP 窗口图像识别自动点击右键菜单"启动"，
/// 然后轮询 TCP 端口 + 读取初始输出直到匹配到启动完成标志。
/// </summary>
public partial class DeviceStartupService
{
    private const int PortPollTimeoutSeconds = 180;
    private const int Phase2MinTimeoutSeconds = 300;
    private const int TotalStartupTimeoutSeconds = 600;
    private const int PortConnectTimeoutMs = 500;
    private const int PollIntervalMs = 2000;

    private readonly EnspGuiAutomationService _guiAutomation;

    public DeviceStartupService(EnspGuiAutomationService guiAutomation)
    {
        _guiAutomation = guiAutomation;
    }

    /// <summary>
    /// 扫描 127.0.0.1 上所有正在监听的 TCP 端口。返回端口号集合。
    /// </summary>
    public static HashSet<int> ScanListeningPorts()
    {
        var ports = new HashSet<int>();
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                // 匹配: TCP    127.0.0.1:2003         0.0.0.0:0              LISTENING       12345
                if (line.Contains("LISTENING") && line.Contains("127.0.0.1"))
                {
                    var colonIdx = line.IndexOf("127.0.0.1:");
                    if (colonIdx >= 0)
                    {
                        var portStart = colonIdx + 10; // "127.0.0.1:".Length
                        var portEnd = portStart;
                        while (portEnd < line.Length && char.IsDigit(line[portEnd]))
                            portEnd++;
                        if (portEnd > portStart && int.TryParse(line[portStart..portEnd], out int port))
                            ports.Add(port);
                    }
                }
            }
        }
        catch
        {
            // netstat unavailable — return empty set
        }
        return ports;
    }

    public async Task<bool> WaitForDeviceReadyAsync(
        string deviceName,
        int consolePort,
        double canvasX,
        double canvasY,
        string deviceModel,
        IProgress<DeviceStartupProgress>? progress,
        CancellationToken ct,
        bool skipGuiAutomation = false)
    {
        StartupDiagnostics.StartSession(deviceName);
        StartupDiagnostics.LogPhase("INIT", $"device={deviceName} model={deviceModel} port=127.0.0.1:{consolePort} canvas=({canvasX},{canvasY})");

        // Pre-scan: what ports are actually listening right now?
        var listeningPorts = ScanListeningPorts();
        StartupDiagnostics.Log("INIT", $"当前监听端口: [{string.Join(", ", listeningPorts.OrderBy(p => p))}]");

        if (consolePort <= 0)
        {
            StartupDiagnostics.LogFailure("无效端口", $"consolePort={consolePort}");
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Error,
                Phase = "错误",
                Message = $"设备 {deviceName} 无控制台端口 (ConsolePort={consolePort})"
            });
            return false;
        }

        // Phase 0: eNSP GUI automation — 图像识别找到设备 → 右键 → ↓+Enter 启动 (进度 0-10%)
        if (!skipGuiAutomation)
        {
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Booting,
                Phase = "图像识别启动",
                Message = $"正在通过 eNSP 窗口图像识别定位 {deviceName} 并点击启动...",
                ProgressPercent = 2
            });

            var guiStarted = await _guiAutomation.StartDeviceViaGuiAsync(deviceName, deviceModel, canvasX, canvasY, ct);

            if (!guiStarted)
            {
                StartupDiagnostics.LogFailure("GUI 自动化失败", "未找到 eNSP 窗口或无法定位设备");
                progress?.Report(new DeviceStartupProgress
                {
                    State = DeviceRuntimeState.Error,
                    Phase = "启动失败",
                    Message = $"自动启动失败 — 请在 eNSP 中手动右键点击 {deviceName} 并选择「启动」\n诊断日志: {StartupDiagnostics.CurrentLogPath}",
                    ProgressPercent = 10
                });
                return false;
            }

            StartupDiagnostics.LogPhase("PHASE0", "GUI 自动化完成: 右键菜单已点击启动");
        }
        else
        {
            StartupDiagnostics.LogPhase("PHASE0", "跳过 GUI 自动化 (批量启动模式)");
        }
        ct.ThrowIfCancellationRequested();

        // Phase 1: TCP port polling (进度 10-70%) — 开机方案：最多等 3 分钟
        var sw = Stopwatch.StartNew();
        StartupDiagnostics.LogPhase("PHASE1", $"开始 TCP 端口轮询, 目标 127.0.0.1:{consolePort}, 总超时 {PortPollTimeoutSeconds}s");

        progress?.Report(new DeviceStartupProgress
        {
            State = DeviceRuntimeState.Booting,
            Phase = "等待端口",
            Message = $"等待 {deviceName} COM 端口开放 (localhost:{consolePort})...",
            ElapsedSeconds = 0,
            ProgressPercent = 10
        });

        var portOpen = false;
        int pollAttempts = 0;
        while (sw.Elapsed.TotalSeconds < PortPollTimeoutSeconds)
        {
            ct.ThrowIfCancellationRequested();
            pollAttempts++;

            if (await TryConnectTcpPortAsync("127.0.0.1", consolePort, ct))
            {
                portOpen = true;
                StartupDiagnostics.LogPhase("PHASE1", $"端口开放! 尝试次数={pollAttempts} 耗时={sw.Elapsed.TotalSeconds:F1}s");
                break;
            }

            // Log every 10th attempt to keep log clean
            if (pollAttempts % 10 == 1)
                StartupDiagnostics.Log("PHASE1", $"轮询中... 尝试#{pollAttempts} 耗时={sw.Elapsed.TotalSeconds:F0}s");

            double pct = 10 + (sw.Elapsed.TotalSeconds / PortPollTimeoutSeconds) * 60;
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Booting,
                Phase = "等待端口",
                Message = $"等待 {deviceName} COM 端口开放... ({sw.Elapsed.TotalSeconds:F0}秒 / {PortPollTimeoutSeconds}秒)",
                ElapsedSeconds = (int)sw.Elapsed.TotalSeconds,
                ProgressPercent = pct
            });

            await Task.Delay(PollIntervalMs, ct);
        }

        if (!portOpen)
        {
            // Gather diagnostic info
            var diagInfo = GatherPortDiagnostics(consolePort);
            var onlinePorts = listeningPorts.OrderBy(p => p).ToList();
            var portSummary = onlinePorts.Count > 0
                ? $"当前在线端口: [{string.Join(", ", onlinePorts)}]"
                : "未检测到任何在线端口 — eNSP 可能没有已启动的设备";

            StartupDiagnostics.LogFailure(
                $"端口轮询超时: {PortPollTimeoutSeconds}s",
                $"尝试{pollAttempts}次 | 端口始终未开放 | {portSummary} | {diagInfo}");
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Error,
                Phase = "超时",
                Message = $"{deviceName} 端口 {consolePort} 未开放 ({PortPollTimeoutSeconds}秒超时)\n{portSummary}\n诊断日志: {StartupDiagnostics.CurrentLogPath}",
                ElapsedSeconds = (int)sw.Elapsed.TotalSeconds,
                ProgressPercent = 70
            });
            return false;
        }

        ct.ThrowIfCancellationRequested();

        // Phase 2: Startup signature detection (进度 70-95%) — 开机方案 D
        // First boot of linked clone can be very slow (filesystem init), allow up to 10 min total
        int elapsed = (int)sw.Elapsed.TotalSeconds;
        int phaseBudget = Math.Max(Phase2MinTimeoutSeconds, TotalStartupTimeoutSeconds - elapsed);
        StartupDiagnostics.LogPhase("PHASE2", $"开始启动特征检测, budget={phaseBudget}s (total budget={TotalStartupTimeoutSeconds}s, elapsed={elapsed}s)");

        progress?.Report(new DeviceStartupProgress
        {
            State = DeviceRuntimeState.Booting,
            Phase = "检测启动特征",
            Message = $"COM 端口已开放 (耗时{elapsed}秒)，正在通过 Telnet 检测 {deviceName} 启动完成... (最长等待{TotalStartupTimeoutSeconds}秒)",
            ElapsedSeconds = elapsed,
            ProgressPercent = 70
        });

        var ready = await ReadStartupOutputAsync(consolePort, phaseBudget,
            (pct, msg) =>
            {
                double overallPct = 70 + pct * 0.25;
                progress?.Report(new DeviceStartupProgress
                {
                    State = DeviceRuntimeState.Booting,
                    Phase = "检测启动特征",
                    Message = msg,
                    ElapsedSeconds = (int)sw.Elapsed.TotalSeconds,
                    ProgressPercent = overallPct
                });
            },
            ct);

        if (ready)
        {
            StartupDiagnostics.LogSuccess(sw.Elapsed.TotalSeconds);
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Ready,
                Phase = "已就绪",
                Message = $"{deviceName} 已就绪 (总耗时 {(int)sw.Elapsed.TotalSeconds}秒)",
                ProgressPercent = 100
            });
            return true;
        }
        else
        {
            StartupDiagnostics.LogFailure("启动特征检测失败", $"等待{phaseBudget}s | 端口开放但未匹配到启动完成标志");
            progress?.Report(new DeviceStartupProgress
            {
                State = DeviceRuntimeState.Error,
                Phase = "检测失败",
                Message = $"启动特征检测失败 — {deviceName} 端口已开放但未检测到登录提示符或 VRP 提示符 (等待了{phaseBudget}秒)\n诊断日志: {StartupDiagnostics.CurrentLogPath}",
                ElapsedSeconds = (int)sw.Elapsed.TotalSeconds,
                ProgressPercent = 95
            });
            return false;
        }
    }

    public static async Task<bool> TryConnectTcpPortAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port, ct).AsTask();
            var timeout = Task.Delay(PortConnectTimeoutMs, ct);
            var completed = await Task.WhenAny(connectTask, timeout);
            return completed == connectTask && client.Connected;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("TryConnectTcp", $"异常: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 开机方案 D 核心实现：连接设备控制台端口，主动读取初始输出直到匹配启动完成特征。
    /// </summary>
    private static async Task<bool> ReadStartupOutputAsync(
        int consolePort, int timeoutSeconds,
        Action<double, string>? progressCallback,
        CancellationToken ct)
    {
        using var telnet = new TelnetService();
        var accumulated = new StringBuilder();
        var tcs = new TaskCompletionSource<bool>();
        var startedAt = Stopwatch.StartNew();

        void OnData(string data)
        {
            accumulated.Append(data);
            StartupDiagnostics.LogData("RECV", data);
            if (IsStartupComplete(accumulated.ToString()))
            {
                StartupDiagnostics.LogPhase("PHASE2", "启动特征匹配成功!");
                tcs.TrySetResult(true);
            }
        }

        telnet.DataReceived += OnData;

        try
        {
            StartupDiagnostics.LogPhase("PHASE2", $"Telnet 连接 127.0.0.1:{consolePort}...");
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedConnect = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);
            await telnet.ConnectAsync("127.0.0.1", consolePort, linkedConnect.Token);
            StartupDiagnostics.LogPhase("PHASE2", "Telnet 连接成功");

            progressCallback?.Invoke(5, "Telnet 已连接，正在读取初始输出...");

            // 开机方案: "连接成功后读取初始输出" — 发送 \r\n 触发设备输出
            await Task.Delay(300, ct);
            await telnet.SendAsync("\r\n", ct);
            StartupDiagnostics.LogPhase("PHASE2", "已发送初始 \\r\\n");

            // 等待启动特征出现，期间每 2s 重新发送 \r\n
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, waitCts.Token);

            int tick = 0;
            while (!tcs.Task.IsCompleted)
            {
                try
                {
                    await Task.Delay(2000, combined.Token);
                    tick++;
                    if (!tcs.Task.IsCompleted)
                    {
                        double pct = Math.Min(95, tick * 2000.0 / (timeoutSeconds * 1000.0) * 100);
                        int remaining = Math.Max(0, timeoutSeconds - (int)startedAt.Elapsed.TotalSeconds);
                        var lastLine = accumulated.Length > 0
                            ? accumulated.ToString().Split('\n').LastOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? ""
                            : "";
                        var preview = lastLine.Length > 60 ? lastLine[..60] + "..." : lastLine;
                        progressCallback?.Invoke(pct, $"等待启动特征... 剩余{remaining}s{(!string.IsNullOrEmpty(preview) ? $" | 最新: {preview}" : " (设备启动中，暂无输出)")}");
                        await telnet.SendAsync("\r\n", combined.Token);

                        if (tick % 5 == 0)
                            StartupDiagnostics.Log("PHASE2", $"tick={tick} elapsed={startedAt.Elapsed.TotalSeconds:F0}s accumulatedLen={accumulated.Length} matched={tcs.Task.IsCompleted}");
                    }
                }
                catch (OperationCanceledException)
                {
                    StartupDiagnostics.Log("PHASE2", $"超时取消, tick={tick}");
                    break;
                }
            }

            bool result = tcs.Task.IsCompletedSuccessfully && tcs.Task.Result;
            if (!result)
            {
                StartupDiagnostics.LogData("FINAL_OUTPUT", accumulated.ToString());
                StartupDiagnostics.Log("PHASE2", $"检测结束: 匹配失败, 累积输出 {accumulated.Length} 字符, {accumulated.ToString().Split('\n').Length} 行");
                LogDiagnosticPatternCheck(accumulated.ToString());
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            StartupDiagnostics.Log("PHASE2", "OperationCanceledException");
            bool result = tcs.Task.IsCompletedSuccessfully && tcs.Task.Result;
            if (!result)
            {
                StartupDiagnostics.LogData("FINAL_OUTPUT", accumulated.ToString());
                LogDiagnosticPatternCheck(accumulated.ToString());
            }
            return result;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("PHASE2", $"异常: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
        finally
        {
            telnet.DataReceived -= OnData;
            telnet.Disconnect();
        }
    }

    private static void LogDiagnosticPatternCheck(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            StartupDiagnostics.Log("DIAG", "输出为空 — 设备没有任何响应");
            return;
        }

        var lines = output.Split('\n');
        StartupDiagnostics.Log("DIAG", $"共 {lines.Length} 行输出");

        // Check each pattern individually
        bool hasEnterPrompt = output.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase);
        bool hasAnyKey = output.Contains("Press any key to continue", StringComparison.OrdinalIgnoreCase);
        bool hasVrpUser = VrpUserViewRegex().IsMatch(output);
        bool hasVrpSystem = VrpSystemViewRegex().IsMatch(output);
        bool hasLogin = Regex.IsMatch(output, @"(login|Username|Password)\s*:\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        bool hasInitComplete = output.Contains("initialization", StringComparison.OrdinalIgnoreCase) &&
                              (output.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                               output.Contains("finished", StringComparison.OrdinalIgnoreCase));

        StartupDiagnostics.Log("DIAG", $"模式检查: EnterPrompt={hasEnterPrompt} AnyKey={hasAnyKey} VrpUser={hasVrpUser} VrpSystem={hasVrpSystem} Login={hasLogin} InitComplete={hasInitComplete}");

        // Show last 5 lines for human inspection
        var lastLines = lines.Skip(Math.Max(0, lines.Length - 5)).Take(5);
        int i = Math.Max(0, lines.Length - 5) + 1;
        foreach (var line in lastLines)
        {
            var escaped = line.Replace("\r", "\\r").TrimEnd();
            StartupDiagnostics.Log("DIAG", $"  行{i}: [{escaped}]");
            i++;
        }
    }

    private static string GatherPortDiagnostics(int port)
    {
        try
        {
            // Check if anything is listening on this port
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var portLines = output.Split('\n')
                .Where(l => l.Contains($":{port}") || l.Contains($"127.0.0.1:{port}"))
                .Select(l => l.Trim())
                .ToList();

            return portLines.Count > 0
                ? $"端口 {port} 监听状态: {string.Join(" | ", portLines)}"
                : $"端口 {port} 无监听 (netstat 未找到)";
        }
        catch (Exception ex)
        {
            return $"netstat 检查失败: {ex.Message}";
        }
    }

    // VRP user view: <DeviceName> — e.g. <Huawei>, <R1>, <AR1220>
    [GeneratedRegex(@"<[A-Za-z0-9_-]+>\s*$", RegexOptions.Multiline)]
    private static partial Regex VrpUserViewRegex();

    // VRP system view: [DeviceName] or [~DeviceName] — e.g. [Huawei], [~R1]
    [GeneratedRegex(@"\[~?[A-Za-z0-9_-]+\]\s*$", RegexOptions.Multiline)]
    private static partial Regex VrpSystemViewRegex();

    internal static bool IsStartupComplete(string output)
    {
        if (string.IsNullOrEmpty(output))
            return false;

        if (output.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase))
            return true;

        if (output.Contains("Press any key to continue", StringComparison.OrdinalIgnoreCase))
            return true;

        if (VrpUserViewRegex().IsMatch(output))
            return true;

        if (VrpSystemViewRegex().IsMatch(output))
            return true;

        if (Regex.IsMatch(output, @"(login|Username|Password)\s*:\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
            return true;

        if (output.Contains("initialization", StringComparison.OrdinalIgnoreCase) &&
            (output.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("finished", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}
