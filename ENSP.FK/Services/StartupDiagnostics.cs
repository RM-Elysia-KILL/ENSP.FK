using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace ENSP.ZD.Services;

/// <summary>
/// 启动诊断日志：写入 %LocalAppData%\ENSP.ZD\logs\startup_<timestamp>.log
/// </summary>
internal static class StartupDiagnostics
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ENSP.ZD", "logs");

    private static string? _currentLogPath;
    private static readonly object _lock = new();

    public static void StartSession(string deviceName)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(LogDir);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _currentLogPath = Path.Combine(LogDir, $"startup_{deviceName}_{ts}.log");
            LogRaw($"=== 启动检测会话: {deviceName} ===");
            LogRaw($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            LogRaw($"机器: {Environment.MachineName}");
            LogEnvInfo();
        }
    }

    public static void Log(string message, [CallerMemberName] string? caller = null)
    {
        LogRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{caller}] {message}");
    }

    public static void LogPhase(string phase, string detail)
    {
        LogRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{phase}] {detail}");
    }

    public static void LogData(string label, string data)
    {
        lock (_lock)
        {
            if (_currentLogPath == null) return;
            try
            {
                var escaped = data.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
                File.AppendAllText(_currentLogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] [DATA:{label}] {escaped}\n");
                // Also write raw for readability if multiline
                if (data.Contains('\n'))
                {
                    File.AppendAllText(_currentLogPath, $"--- RAW ({label}) ---\n{data}\n--- END RAW ---\n");
                }
            }
            catch { /* never crash the app for logging */ }
        }
    }

    public static void LogFailure(string reason, string? extra = null)
    {
        lock (_lock)
        {
            LogRaw($"[{DateTime.Now:HH:mm:ss.fff}] [FAIL] {reason}");
            if (extra != null)
                LogRaw($"[{DateTime.Now:HH:mm:ss.fff}] [FAIL_DETAIL] {extra}");
            LogRaw($"=== 会话结束: 失败 ===");
        }
    }

    public static void LogSuccess(double totalSeconds)
    {
        lock (_lock)
        {
            LogRaw($"[{DateTime.Now:HH:mm:ss.fff}] [OK] 设备就绪，总耗时 {totalSeconds:F1}秒");
            LogRaw($"=== 会话结束: 成功 ===");
        }
    }

    public static string? CurrentLogPath => _currentLogPath;

    private static void LogRaw(string line)
    {
        lock (_lock)
        {
            if (_currentLogPath == null) return;
            try
            {
                File.AppendAllText(_currentLogPath, line + "\n");
                Debug.WriteLine($"[StartupDiag] {line}");
            }
            catch { /* never crash the app for logging */ }
        }
    }

    private static void LogEnvInfo()
    {
        LogRaw($"eNSP_Client 运行中: {VBoxDeviceService.IsEnspClientRunning()}");
        LogRaw($"eNSP_VBoxServer 运行中: {VBoxDeviceService.IsEnspVBoxServerRunning()}");
        LogRaw($"VBoxServer TCP 可达: {VBoxDeviceService.IsVBoxServerReachable()}");
        try
        {
            var netstatInfo = new Process
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
            netstatInfo.Start();
            var output = netstatInfo.StandardOutput.ReadToEnd();
            netstatInfo.WaitForExit(3000);
            var enspLines = output.Split('\n')
                .Where(l => l.Contains("65510") || l.Contains("ENSP") || l.Contains("ensp"))
                .Select(l => l.Trim())
                .Take(20);
            LogRaw($"netstat 相关行: {string.Join(" | ", enspLines)}");
        }
        catch { }
    }
}
