using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace ENSP.ZD.Services;

/// <summary>
/// VBoxManage utilities for stopping and listing eNSP VMs.
/// Device startup is handled by EnspGuiAutomationService (image recognition + GUI automation).
/// </summary>
public class VBoxDeviceService
{
    private readonly string? _vboxManage;

    public VBoxDeviceService()
    {
        _vboxManage = FindVBoxManage();
    }

    public bool IsAvailable => _vboxManage != null;

    public static bool IsEnspClientRunning()
    {
        return Process.GetProcessesByName("eNSP_Client").Length > 0;
    }

    public static bool IsEnspVBoxServerRunning()
    {
        return Process.GetProcessesByName("eNSP_VBoxServer").Length > 0;
    }

    public static bool IsVBoxServerReachable()
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", 65510);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<string> ListDeviceVms()
    {
        if (_vboxManage == null) return new List<string>();

        var result = RunVBoxManage("list vms");
        var vms = new List<string>();
        foreach (var line in result.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("<")) continue;
            var nameEnd = trimmed.IndexOf('"', 1);
            if (nameEnd > 1)
                vms.Add(trimmed[1..nameEnd]);
        }
        return vms;
    }

    public List<string> ListRunningVms()
    {
        if (_vboxManage == null) return new List<string>();

        var result = RunVBoxManage("list runningvms");
        var vms = new List<string>();
        foreach (var line in result.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var nameEnd = trimmed.IndexOf('"', 1);
            if (nameEnd > 1)
                vms.Add(trimmed[1..nameEnd]);
        }
        return vms;
    }

    public bool StopDevice(string deviceName)
    {
        if (_vboxManage == null) return false;

        var output = RunVBoxManage($"controlvm \"{deviceName}\" poweroff");
        return output.Contains("powered off", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsDeviceRunning(string deviceName)
    {
        if (_vboxManage == null) return false;

        var result = RunVBoxManage("list runningvms");
        return result.Contains($"\"{deviceName}\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindVBoxManage()
    {
        var knownPaths = new[]
        {
            @"C:\Program Files\Oracle\VirtualBox\VBoxManage.exe",
            @"C:\Program Files (x86)\Oracle\VirtualBox\VBoxManage.exe",
        };

        foreach (var p in knownPaths)
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private string RunVBoxManage(string arguments)
    {
        if (_vboxManage == null) return string.Empty;

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _vboxManage,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);
            return output + err;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VBoxManage failed: {ex.Message}");
            return string.Empty;
        }
    }
}
