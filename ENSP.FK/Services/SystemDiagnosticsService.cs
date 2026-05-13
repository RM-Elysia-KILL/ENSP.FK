using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ENSP.FK.Services;

public class InstallStatusToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DismissCircle24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class InstallStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10))  // green
            : new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)); // red
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class AppStatus
{
    public string Name { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public string Version { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public class SystemDiagnosticsService
{
    public List<AppStatus> CheckAll()
    {
        return new List<AppStatus> { CheckEnsp(), CheckVirtualBox(), CheckWinPcap(), CheckHyperV() };
    }

    public static string? FindEnspExePath(string? configuredPath = null)
    {
        // 1. Check user-configured path first
        if (!string.IsNullOrEmpty(configuredPath))
        {
            if (File.Exists(configuredPath))
                return configuredPath;
            var exeInCfg = Path.Combine(configuredPath, "eNSP_Client.exe");
            if (File.Exists(exeInCfg))
                return exeInCfg;
        }

        // 2. Registry search
        var result = CheckEnsp();
        if (result.Installed && !string.IsNullOrEmpty(result.InstallPath))
        {
            if (File.Exists(result.InstallPath))
                return result.InstallPath;

            var exeInDir = Path.Combine(result.InstallPath, "eNSP_Client.exe");
            if (File.Exists(exeInDir))
                return exeInDir;
        }

        // 3. Known default paths
        var knownPaths = new[]
        {
            @"C:\Program Files\Huawei\eNSP\eNSP_Client.exe",
            @"C:\Program Files (x86)\Huawei\eNSP\eNSP_Client.exe",
        };
        foreach (var p in knownPaths)
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private static AppStatus CheckEnsp()
    {
        // Search registry for eNSP
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var subkey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var key = root.OpenSubKey(subkey);
            if (key == null) continue;

            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var display = sub?.GetValue("DisplayName") as string;
                if (display != null && display.Contains("eNSP", StringComparison.OrdinalIgnoreCase))
                {
                    return new AppStatus
                    {
                        Name = "eNSP",
                        Installed = true,
                        Version = sub.GetValue("DisplayVersion") as string ?? "",
                        InstallPath = sub.GetValue("InstallLocation") as string ?? "",
                        Detail = $"注册表找到: {display}"
                    };
                }
            }
        }

        // Fallback: check known file paths
        var knownPaths = new[]
        {
            @"C:\Program Files\Huawei\eNSP\eNSP_Client.exe",
            @"C:\Program Files (x86)\Huawei\eNSP\eNSP_Client.exe",
        };

        foreach (var p in knownPaths)
        {
            if (File.Exists(p))
                return new AppStatus { Name = "eNSP", Installed = true, InstallPath = p, Detail = "通过文件路径检测到" };
        }

        return new AppStatus { Name = "eNSP", Installed = false, Detail = "未在注册表或默认路径中找到 eNSP" };
    }

    private static AppStatus CheckVirtualBox()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var subkey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var key = root.OpenSubKey(subkey);
            if (key == null) continue;

            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var display = sub?.GetValue("DisplayName") as string;
                if (display == null || !display.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                    continue;

                var ver = sub.GetValue("DisplayVersion") as string ?? "";
                var path = sub.GetValue("InstallLocation") as string ?? "";

                var is52 = ver.StartsWith("5.2");
                var detail = is52
                    ? "已安装 5.2 版本 ✓"
                    : $"版本为 {ver}，推荐 5.2.x";

                return new AppStatus
                {
                    Name = "Oracle VM VirtualBox",
                    Installed = true,
                    Version = ver,
                    InstallPath = path,
                    Detail = detail
                };
            }
        }

        // Fallback: check known paths
        var knownPaths = new[]
        {
            @"C:\Program Files\Oracle\VirtualBox\VBoxManage.exe",
            @"C:\Program Files (x86)\Oracle\VirtualBox\VBoxManage.exe",
        };

        foreach (var p in knownPaths)
        {
            if (!File.Exists(p)) continue;

            try
            {
                var dir = Path.GetDirectoryName(p)!;
                return new AppStatus { Name = "Oracle VM VirtualBox", Installed = true, InstallPath = dir, Detail = "通过文件路径检测到（无法确认版本）" };
            }
            catch { }
        }

        return new AppStatus { Name = "Oracle VM VirtualBox", Installed = false, Detail = "未找到 VirtualBox，eNSP 依赖 VirtualBox 5.2" };
    }

    private static AppStatus CheckWinPcap()
    {
        // Registry: check for WinPcap uninstall entry
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var subkey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var key = root.OpenSubKey(subkey);
            if (key == null) continue;

            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var display = sub?.GetValue("DisplayName") as string;
                if (display != null && display.Contains("WinPcap", StringComparison.OrdinalIgnoreCase))
                {
                    var ver = sub.GetValue("DisplayVersion") as string ?? "";
                    var is413 = ver.StartsWith("4.1.3");
                    var detail = is413
                        ? "已安装 4.1.3 版本 ✓"
                        : $"版本为 {ver}，推荐 4.1.3";

                    return new AppStatus
                    {
                        Name = "WinPcap",
                        Installed = true,
                        Version = ver,
                        Detail = detail
                    };
                }
            }
        }

        // Check known file paths and extract version
        var dllPaths = new[]
        {
            @"C:\Windows\System32\wpcap.dll",
            @"C:\Windows\SysWOW64\wpcap.dll",
        };

        foreach (var p in dllPaths)
        {
            if (!File.Exists(p)) continue;

            try
            {
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                var ver = fvi.FileVersion ?? "";
                var is413 = ver.StartsWith("4.1.3");
                var detail = is413
                    ? "已安装 4.1.3 版本 ✓（通过文件版本检测）"
                    : $"文件版本 {ver}，推荐 4.1.3";

                return new AppStatus
                {
                    Name = "WinPcap",
                    Installed = true,
                    Version = ver,
                    InstallPath = p,
                    Detail = detail
                };
            }
            catch { }
        }

        // Also check npf.sys driver
        var driverPath = @"C:\Windows\System32\drivers\npf.sys";
        if (File.Exists(driverPath))
        {
            return new AppStatus
            {
                Name = "WinPcap",
                Installed = true,
                InstallPath = driverPath,
                Detail = "找到 npf.sys 驱动（无法确认版本）"
            };
        }

        return new AppStatus { Name = "WinPcap", Installed = false, Detail = "未找到 WinPcap 4.1.3，eNSP 依赖 WinPcap" };
    }

    private static AppStatus CheckHyperV()
    {
        var issues = new List<string>();
        bool hasProblem = false;

        // 1. Check BCD hypervisorlaunchtype via bcdedit
        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "bcdedit",
                    Arguments = "/enum {current}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            if (output.Contains("hypervisorlaunchtype") && !output.Contains("hypervisorlaunchtype    Off"))
            {
                hasProblem = true;
                issues.Add("Hyper-V 虚拟化平台未关闭 (hypervisorlaunchtype ≠ Off)");
            }
        }
        catch
        {
            // bcdedit not available — skip
        }

        // 2. Check Memory Integrity (HVCI) via registry
        try
        {
            using var hvciKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            var enabled = hvciKey?.GetValue("Enabled") as int?;
            if (enabled == 1)
            {
                hasProblem = true;
                issues.Add("内存完整性 (HVCI) 已开启");
            }
        }
        catch { }

        // 3. Check Virtualization Based Security via Device Guard policy
        try
        {
            using var dgKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard");
            var vbs = dgKey?.GetValue("EnableVirtualizationBasedSecurity") as int?;
            if (vbs == 1)
            {
                hasProblem = true;
                issues.Add("基于虚拟化的安全性 (VBS) 已开启");
            }
        }
        catch { }

        // 4. Check Credential Guard
        try
        {
            using var lsaKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Lsa");
            var lsaCfg = lsaKey?.GetValue("LsaCfgFlags") as int?;
            if (lsaCfg >= 1)
            {
                hasProblem = true;
                issues.Add("Credential Guard 已开启");
            }
        }
        catch { }

        if (!hasProblem)
            return new AppStatus { Name = "Windows 虚拟化", Installed = true, Detail = "Hyper-V 相关虚拟化已关闭 ✓ — 不会与 VirtualBox 冲突" };

        return new AppStatus
        {
            Name = "Windows 虚拟化",
            Installed = false,
            Detail = "检测到虚拟化功能开启，可能与 VirtualBox 冲突:\n  • " + string.Join("\n  • ", issues)
        };
    }
}
