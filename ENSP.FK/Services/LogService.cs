using System.IO;
using System.Text;

namespace ENSP.ZD.Services;

/// <summary>
/// Thread-safe file logger. Writes to %LOCALAPPDATA%/ENSP.ZD/logs/.
/// </summary>
public class LogService
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public string? CurrentLogPath { get; private set; }

    public LogService()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ENSP.ZD", "logs");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public string[] ReadRecentLines(int count = 200)
    {
        lock (_lock)
        {
            if (CurrentLogPath == null || !File.Exists(CurrentLogPath))
                return [];
            try
            {
                var lines = File.ReadAllLines(CurrentLogPath, Encoding.UTF8);
                return lines.Length <= count ? lines : lines.Skip(lines.Length - count).ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                if (CurrentLogPath == null)
                {
                    Directory.CreateDirectory(_logDir);
                    CurrentLogPath = Path.Combine(_logDir, $"enspfk_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    // Write UTF-8 BOM so Notepad correctly detects encoding
                    File.WriteAllBytes(CurrentLogPath, new byte[] { 0xEF, 0xBB, 0xBF });
                }
                File.AppendAllText(CurrentLogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}
