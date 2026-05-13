using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace ENSP.FK.Services;

/// <summary>
/// Manages eNSP device VMs via VBoxManage and VBoxServer communication.
/// eNSP architecture: eNSP_VBoxServer.exe (TCP 62077) manages linked clones from base templates.
/// VBoxManage is a best-effort fallback for starting/stopping devices.
/// </summary>
public class VBoxDeviceService
{
    private readonly string? _vboxManage;

    // Snapshot names used by eNSP for linked clones
    private const string SnapshotRouter = "AR_Base_Link";

    public VBoxDeviceService()
    {
        _vboxManage = FindVBoxManage();
    }

    public bool IsAvailable => _vboxManage != null;

    /// <summary>
    /// Checks if eNSP_VBoxServer.exe is running.
    /// </summary>
    public static bool IsEnspVBoxServerRunning()
    {
        return Process.GetProcessesByName("eNSP_VBoxServer").Length > 0;
    }

    /// <summary>
    /// Checks if eNSP_Client.exe is running.
    /// </summary>
    public static bool IsEnspClientRunning()
    {
        return Process.GetProcessesByName("eNSP_Client").Length > 0;
    }

    /// <summary>
    /// Tries to ping the eNSP VBoxServer on port 62077.
    /// </summary>
    public static bool IsVBoxServerReachable()
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", 62077);
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

    public bool StartDevice(string deviceName, string baseTemplate = "AR_Base")
    {
        if (_vboxManage == null) return false;

        // Check if VM already exists and is running
        if (IsDeviceRunning(deviceName))
            return true;

        var vms = ListDeviceVms();
        var match = vms.FirstOrDefault(vm =>
            vm.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            // Clone from base template using eNSP's snapshot for linked clones
            var snapshot = baseTemplate == "AR_Base" ? SnapshotRouter : null;

            string cloneResult;
            if (snapshot != null)
            {
                // Try linked clone from snapshot first (what eNSP does)
                cloneResult = RunVBoxManage(
                    $"clonevm \"{baseTemplate}\" --snapshot \"{snapshot}\" --name=\"{deviceName}\" --register --mode machine --options link");
            }
            else
            {
                // Full clone for templates without snapshots
                cloneResult = RunVBoxManage(
                    $"clonevm \"{baseTemplate}\" --name=\"{deviceName}\" --register --mode machine");
            }

            // If linked clone failed, try full clone as fallback
            if (!IsSuccess(cloneResult) && snapshot != null)
            {
                cloneResult = RunVBoxManage(
                    $"clonevm \"{baseTemplate}\" --name=\"{deviceName}\" --register --mode machine");
            }

            if (!IsSuccess(cloneResult))
                return false;

            // Verify the clone registered
            vms = ListDeviceVms();
            match = vms.FirstOrDefault(vm =>
                vm.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return false;
        }

        // Start headless
        var startOutput = RunVBoxManage($"startvm \"{deviceName}\" --type headless");
        return IsSuccess(startOutput);
    }

    public bool StopDevice(string deviceName)
    {
        if (_vboxManage == null) return false;

        var output = RunVBoxManage($"controlvm \"{deviceName}\" poweroff");
        return IsSuccess(output);
    }

    public bool IsDeviceRunning(string deviceName)
    {
        if (_vboxManage == null) return false;

        var result = RunVBoxManage("list runningvms");
        return result.Contains($"\"{deviceName}\"", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetBaseTemplate(Models.Topology.DeviceType deviceType)
    {
        return deviceType switch
        {
            Models.Topology.DeviceType.Router => "AR_Base",
            Models.Topology.DeviceType.Switch => "LSW",
            Models.Topology.DeviceType.Firewall => "FW",
            _ => null
        };
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

    private static bool IsSuccess(string output)
    {
        return output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("has been cloned", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("has been started", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("100%", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("powered off", StringComparison.OrdinalIgnoreCase);
    }
}
