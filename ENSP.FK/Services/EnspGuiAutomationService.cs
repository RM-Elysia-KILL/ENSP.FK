using ENSP.ZD.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ENSP.ZD.Services;

/// <summary>
/// Automates eNSP GUI to start devices via image recognition + PostMessage input.
/// Does NOT steal focus, move the cursor, or require the window to be in the foreground.
/// </summary>
public class EnspGuiAutomationService
{
    private readonly ImageRecognitionService _imageRec;
    private readonly DeviceIconService _iconService;
    private readonly LogService _log;

    public EnspGuiAutomationService(ApiConfig config, ImageRecognitionService imageRec, DeviceIconService iconService, LogService log)
    {
        _imageRec = imageRec;
        _iconService = iconService;
        _log = log;
    }

    private void Log(string msg)
    {
        Debug.WriteLine(msg);
        _log.Info(msg);
    }

    // ── eNSP process / window management ──────────────────────────────

    public static bool IsEnspRunning()
    {
        var procs = Process.GetProcessesByName("eNSP_Client");
        return procs.Length > 0;
    }

    public static void LaunchEnsp(string topoFilePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = topoFilePath,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Poll the eNSP main window until it's enabled and its message pump is responsive.
    /// Returns the window handle, or IntPtr.Zero on timeout.
    /// </summary>
    public static async Task<IntPtr> WaitForEnspWindowReadyAsync(int maxWaitSeconds = 30)
    {
        const uint WM_NULL = 0x0000;
        const uint SMTO_NORMAL = 0x0000;

        // Phase 1: wait for window handle to exist
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < maxWaitSeconds; i++)
        {
            await Task.Delay(1000);
            hwnd = FindEnspMainWindow();
            if (hwnd != IntPtr.Zero) break;
        }

        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        // Phase 2: wait for window to be enabled + message pump responsive
        for (int i = 0; i < maxWaitSeconds; i++)
        {
            await Task.Delay(500);

            if (!Win32Interop.IsWindowEnabled(hwnd))
                continue;

            IntPtr result;
            IntPtr ret = Win32Interop.SendMessageTimeout(
                hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero,
                SMTO_NORMAL, 2000, out result);

            if (ret != IntPtr.Zero)
                return hwnd;
        }

        return hwnd;
    }

    private static IntPtr FindEnspMainWindow()
    {
        var processes = Process.GetProcessesByName("eNSP_Client");
        if (processes.Length == 0) return IntPtr.Zero;

        IntPtr hwnd = processes[0].MainWindowHandle;
        if (hwnd != IntPtr.Zero) return hwnd;

        IntPtr result = IntPtr.Zero;
        foreach (var proc in processes)
        {
            Win32Interop.EnumWindows((h, _) =>
            {
                Win32Interop.GetWindowThreadProcessId(h, out uint pid);
                if (pid == proc.Id) { result = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (result != IntPtr.Zero) break;
        }
        return result;
    }

    public async Task<bool> StartDeviceViaGuiAsync(
        string deviceName, string deviceModel,
        double canvasX, double canvasY,
        CancellationToken ct)
    {
        Log($"[GuiAuto] StartDeviceViaGui: name={deviceName} model={deviceModel} cx={canvasX} cy={canvasY}");

        // Step 1: Find eNSP window
        var hwnd = FindEnspWindow();
        if (hwnd == IntPtr.Zero)
        {
            Log("[GuiAuto] eNSP window not found");
            return false;
        }

        // Step 2: Ensure window is restored (not minimized) but don't activate it
        if (Win32Interop.IsIconic(hwnd))
            Win32Interop.ShowWindow(hwnd, Win32Interop.SW_SHOWNOACTIVATE);

        await Task.Delay(200, ct);

        // Step 3: Reset canvas scroll via PostMessage (no focus steal)
        Win32Interop.PostCtrlHome(hwnd);
        await Task.Delay(200, ct);

        // Step 4: Try image recognition, fall back to canvas coords
        System.Drawing.Point? clientPoint = null;

        var iconPath = _iconService.ResolveIconPath(deviceModel);
        if (iconPath != null)
        {
            clientPoint = await LocateDeviceByTemplateAsync(hwnd, iconPath, canvasX, canvasY, ct);
        }

        if (clientPoint == null)
        {
            Log($"[GuiAuto] Template matching failed, falling back to canvas coords");
            clientPoint = CanvasToClient(canvasX, canvasY);
        }

        if (clientPoint == null)
        {
            Log("[GuiAuto] Could not determine click position");
            return false;
        }

        // Step 5: Right-click → Down → Enter via PostMessage (no cursor move, no focus steal)
        Win32Interop.PostRightClick(hwnd, clientPoint.Value.X, clientPoint.Value.Y);
        await Task.Delay(500, ct); // Wait for context menu

        Win32Interop.PostKey(hwnd, Win32Interop.VK_DOWN);
        await Task.Delay(80, ct);
        Win32Interop.PostKey(hwnd, Win32Interop.VK_RETURN);

        Log($"[GuiAuto] PostMessage right-click+Down+Enter at client ({clientPoint.Value.X}, {clientPoint.Value.Y})");
        return true;
    }

    public IntPtr FindEnspWindow()
    {
        // Try exact window title match first
        foreach (var title in new[] { "eNSP", "eNSP_Client" })
        {
            var hwnd = Win32Interop.FindWindow(null, title);
            if (hwnd != IntPtr.Zero)
            {
                Log($"[GuiAuto] Found eNSP window by title '{title}': 0x{hwnd:X}");
                return hwnd;
            }
        }

        // Enumerate: match by process name — only eNSP_Client, exclude ourselves
        var myPid = Environment.ProcessId;
        IntPtr found = IntPtr.Zero;
        Win32Interop.EnumWindows((h, _) =>
        {
            Win32Interop.GetWindowThreadProcessId(h, out uint pid);
            if (pid == myPid)
                return true;

            try
            {
                var proc = Process.GetProcessById((int)pid);
                var procName = proc.ProcessName;
                // Must be eNSP_Client (or eNSP.exe), not ENSP.ZD or other eNSP* variants
                if (procName.Equals("eNSP_Client", StringComparison.OrdinalIgnoreCase)
                    || procName.Equals("eNSP", StringComparison.OrdinalIgnoreCase))
                {
                    found = h;
                    Log($"[GuiAuto] Found eNSP window by process '{procName}': 0x{h:X}");
                    return false;
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Capture the eNSP window client area (works even when behind other windows),
    /// then template-match within a restricted region around the expected canvas position.
    /// Runs heavy matching on a background thread. Returns client coordinates.
    /// </summary>
    private async Task<System.Drawing.Point?> LocateDeviceByTemplateAsync(
        IntPtr hwnd, string iconPath, double canvasX, double canvasY, CancellationToken ct)
    {
        var template = _imageRec.LoadTemplate(iconPath);
        if (template == null)
        {
            Log($"[GuiAuto] Failed to load template: {iconPath}");
            return null;
        }

        // Capture window without bringing it to foreground (PrintWindow), fall back to CopyFromScreen
        var screenshot = _imageRec.CaptureWindow(hwnd);
        if (screenshot == null)
        {
            Log("[GuiAuto] PrintWindow failed, trying CopyFromScreen fallback...");
            Win32Interop.GetClientRect(hwnd, out var cr);
            var cs = new Win32Interop.POINT();
            Win32Interop.ClientToScreen(hwnd, ref cs);
            screenshot = _imageRec.CaptureScreenRegion(cs.X, cs.Y, cr.Width, cr.Height);
        }
        if (screenshot == null)
            return null;

        using (screenshot)
        {
            // Expected client position from canvas coords (toolbar offset ~60px)
            const int toolbarHeight = 60;
            int expectedX = (int)canvasX;
            int expectedY = (int)canvasY + toolbarHeight;

            // Restrict search to a 400×400 region around expected position
            const int searchRadius = 200;
            var searchRegion = new System.Drawing.Rectangle(
                expectedX - searchRadius,
                expectedY - searchRadius,
                searchRadius * 2,
                searchRadius * 2);

            Log($"[GuiAuto] Screenshot: {screenshot.Width}x{screenshot.Height} search: {searchRegion}");

            // Run matching on background thread
            var result = await Task.Run(() =>
                _imageRec.FindTemplate(screenshot, template, minConfidence: 0.6, searchRegion: searchRegion, step: 4),
                ct);

            if (result == null)
            {
                Log("[GuiAuto] Restricted search failed, trying full-area...");
                result = await Task.Run(() =>
                    _imageRec.FindTemplate(screenshot, template, minConfidence: 0.55, step: 4),
                    ct);
            }

            if (result == null)
                return null;

            var (loc, confidence) = result.Value;
            Log($"[GuiAuto] Matched at client ({loc.X},{loc.Y}) confidence={confidence:F3}");

            // Return client coordinates (center of template)
            return new System.Drawing.Point(loc.X + template.Width / 2, loc.Y + template.Height / 2);
        }
    }

    /// <summary>
    /// Find and left-click a toolbar button. Uses hardcoded coordinates + DPI scaling
    /// (exact match of reference ReNSP ClickStartButton pattern).
    /// </summary>
    public async Task<string?> ClickToolbarButtonAsync(string templateName, CancellationToken ct)
    {
        Log($"[GuiAuto] ClickToolbarButton: template={templateName}");

        // 1. Find eNSP window
        var hwnd = FindEnspWindow();
        if (hwnd == IntPtr.Zero)
            return "未找到 eNSP 窗口 (eNSP_Client.exe 是否已启动?)";

        Log($"[GuiAuto] eNSP hwnd=0x{hwnd:X}");

        // 2. Ensure window is restored (not minimized)
        if (Win32Interop.IsIconic(hwnd))
        {
            Log("[GuiAuto] Window is minimized, restoring...");
            Win32Interop.ShowWindow(hwnd, Win32Interop.SW_RESTORE);
            Thread.Sleep(300);
        }

        // 3. Reference ReNSP pattern: SetForegroundWindow → GetWindowRect + DPI → SendInput → restore
        IntPtr currentForeground = Win32Interop.GetForegroundWindow();
        Win32Interop.SetForegroundWindow(hwnd);
        Thread.Sleep(300);

        // 4. Hardcoded coordinates + DPI scaling (exact match of reference ClickStartButton)
        if (!ToolbarButtonCoords.TryGetValue(templateName, out var coords))
            return $"按钮 '{templateName}' 无预设坐标";

        double dpiScale = GetDpiScale();
        Win32Interop.GetWindowRect(hwnd, out var wr);
        int screenX = wr.Left + (int)(coords.X * dpiScale);
        int screenY = wr.Top + (int)(coords.Y * dpiScale);
        Log($"[GuiAuto] button={templateName} raw=({coords.X},{coords.Y}) dpiScale={dpiScale:F2} window=({wr.Left},{wr.Top}) screen=({screenX},{screenY})");

        // 5. SendInput click — exactly matching reference ReNSP pattern
        int screenWidth = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
        int screenHeight = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);

        int absX = (int)((long)screenX * 65535 / screenWidth);
        int absY = (int)((long)screenY * 65535 / screenHeight);

        var inputs = new Win32Interop.INPUT[3];
        inputs[0] = new Win32Interop.INPUT
        {
            type = Win32Interop.INPUT_MOUSE,
            u = new Win32Interop.MOUSEKEYBDHARDWAREUNION
            {
                mi = new Win32Interop.MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = Win32Interop.MOUSEEVENTF_ABSOLUTE | Win32Interop.MOUSEEVENTF_MOVE
                }
            }
        };
        inputs[1] = new Win32Interop.INPUT
        {
            type = Win32Interop.INPUT_MOUSE,
            u = new Win32Interop.MOUSEKEYBDHARDWAREUNION { mi = new Win32Interop.MOUSEINPUT { dwFlags = Win32Interop.MOUSEEVENTF_LEFTDOWN } }
        };
        inputs[2] = new Win32Interop.INPUT
        {
            type = Win32Interop.INPUT_MOUSE,
            u = new Win32Interop.MOUSEKEYBDHARDWAREUNION { mi = new Win32Interop.MOUSEINPUT { dwFlags = Win32Interop.MOUSEEVENTF_LEFTUP } }
        };

        uint sent = Win32Interop.SendInput(3, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32Interop.INPUT>());
        Log($"[GuiAuto] SendInput target=({screenX},{screenY}) abs=({absX},{absY}) sent={sent}");

        Thread.Sleep(200);

        // 6. Restore previous foreground window
        if (currentForeground != IntPtr.Zero && currentForeground != hwnd)
            Win32Interop.SetForegroundWindow(currentForeground);

        Log("[GuiAuto] ClickToolbarButton done");
        return null; // success
    }

    /// <summary>
    /// DPI scale factor from system DPI (reference ReNSP pattern: Graphics.FromHwnd(IntPtr.Zero)).
    /// </summary>
    private static double GetDpiScale()
    {
        using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX / 96.0;
    }

    /// <summary>
    /// Toolbar button coordinates relative to window top-left (includes title bar).
    /// Measured on eNSP v1.2.0.500 at 96 DPI. DPI scaling is applied at runtime.
    /// Exact match of reference ReNSP ClickStartButton pattern.
    /// </summary>
    private static readonly Dictionary<string, (int X, int Y)> ToolbarButtonCoords = new()
    {
        ["start_all"] = (660, 50),
        ["stop_all"]  = (710, 50),
    };

    /// <summary>
    /// Convert eNSP canvas coordinates (from .topo cx/cy) to client coordinates.
    /// </summary>
    private static System.Drawing.Point? CanvasToClient(double canvasX, double canvasY)
    {
        const int toolbarHeight = 60;
        return new System.Drawing.Point((int)canvasX, (int)canvasY + toolbarHeight);
    }
}
