using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ENSP.ZD.Services;

/// <summary>
/// Win32 P/Invoke declarations for window management, input simulation, and screen capture.
/// Pure static declarations — no business logic.
/// </summary>
internal static class Win32Interop
{
    #region Constants

    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SRCCOPY = 0x00CC0020;

    // Window messages (PostMessage)
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;

    // wParam for WM_ACTIVATE
    public const int WA_ACTIVE = 1;

    // wParam for WM_MOUSEACTIVATE
    public const int MA_ACTIVATE = 1;

    // PrintWindow flags
    public const uint PW_CLIENTONLY = 0x00000001;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    public const int VK_CONTROL = 0x11;
    public const int VK_HOME = 0x24;
    public const int VK_S = 0x53;
    public const int VK_RETURN = 0x0D;
    public const int VK_DOWN = 0x28;

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEKEYBDHARDWAREUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct MOUSEKEYBDHARDWAREUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    #region Window Management (user32.dll)

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    #endregion

    #region DPI (user32.dll)

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    #endregion

    #region Input Simulation (user32.dll)

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    #endregion

    #region Screen Capture (gdi32.dll)

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    #endregion

    #region Helpers — Foreground & Input

    /// <summary>
    /// Bring a window to the foreground reliably (even from a non-foreground process).
    /// Uses AttachThreadInput trick to bypass Windows foreground lock.
    /// </summary>
    public static void BringToForeground(IntPtr hwnd)
    {
        uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint targetThread = GetWindowThreadProcessId(hwnd, IntPtr.Zero);

        if (foreThread != targetThread)
        {
            AttachThreadInput(targetThread, foreThread, true);
        }

        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        else
            ShowWindow(hwnd, SW_SHOW);

        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);

        if (foreThread != targetThread)
        {
            AttachThreadInput(targetThread, foreThread, false);
        }
    }

    /// <summary>
    /// Post a right-click at client coordinates via PostMessage.
    /// Does NOT move the cursor or bring the window to foreground.
    /// </summary>
    public static void PostRightClick(IntPtr hwnd, int clientX, int clientY)
    {
        IntPtr lParam = (IntPtr)((clientY << 16) | (clientX & 0xFFFF));
        PostMessage(hwnd, WM_RBUTTONDOWN, IntPtr.Zero, lParam);
        PostMessage(hwnd, WM_RBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// Post WM_ACTIVATE + WM_SETFOCUS + mouse click via PostMessage.
    /// Tells the window it's active so Qt processes the mouse events,
    /// without actually stealing focus or moving the cursor.
    /// </summary>
    public static void PostActivateAndLeftClick(IntPtr hwnd, int clientX, int clientY)
    {
        IntPtr lParam = (IntPtr)((clientY << 16) | (clientX & 0xFFFF));

        // Pre-activate: tell the window it's becoming active (required for Qt)
        PostMessage(hwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
        PostMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        PostMessage(hwnd, WM_MOUSEACTIVATE, (IntPtr)MA_ACTIVATE, (IntPtr)((1 << 16) | 1)); // HTCLIENT

        // Mouse click
        PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, lParam); // wParam=MK_LBUTTON
        PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// Post a left-click at client coordinates via PostMessage.
    /// </summary>
    public static void PostLeftClick(IntPtr hwnd, int clientX, int clientY)
    {
        IntPtr lParam = (IntPtr)((clientY << 16) | (clientX & 0xFFFF));
        PostMessage(hwnd, WM_LBUTTONDOWN, IntPtr.Zero, lParam);
        PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// Post a key down + up via PostMessage.
    /// </summary>
    public static void PostKey(IntPtr hwnd, int vk)
    {
        uint scan = MapVirtualKey((uint)vk, 0);
        IntPtr lParamDown = (IntPtr)((scan << 16) | 0x00000001);
        IntPtr lParamUp = (IntPtr)((scan << 16) | 0xC0000001);

        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, lParamDown);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, lParamUp);
    }

    /// <summary>
    /// Post Ctrl+Home (resets scroll in eNSP canvas) via PostMessage.
    /// </summary>
    public static void PostCtrlHome(IntPtr hwnd)
    {
        uint ctrlScan = MapVirtualKey(VK_CONTROL, 0);
        uint homeScan = MapVirtualKey(VK_HOME, 0);

        // Ctrl down
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, (IntPtr)((ctrlScan << 16) | 0x00000001));
        // Home down
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_HOME, (IntPtr)((homeScan << 16) | 0x00000001));
        // Home up
        PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_HOME, (IntPtr)((homeScan << 16) | 0xC0000001));
        // Ctrl up
        PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_CONTROL, (IntPtr)((ctrlScan << 16) | 0xC0000001));
    }

    /// <summary>
    /// Capture the client area of a window as a GDI bitmap, even when it's not in the foreground.
    /// Uses PrintWindow which works on Windows 10+.
    /// </summary>
    public static IntPtr CaptureWindowBitmap(IntPtr hwnd, out int width, out int height)
    {
        GetClientRect(hwnd, out var cr);
        width = cr.Width;
        height = cr.Height;

        if (width <= 0 || height <= 0)
            return IntPtr.Zero;

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        PrintWindow(hwnd, hdcMem, PW_CLIENTONLY);

        SelectObject(hdcMem, hOld);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        return hBitmap;
    }

    /// <summary>
    /// Click at screen coordinates via SendInput. Moves cursor to target, clicks, leaves it there.
    /// Matches the reference ReNSP project's proven pattern (SetForegroundWindow → SendInput → restore foreground).
    /// </summary>
    public static void SendInputLeftClick(int screenX, int screenY)
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        int absX = (int)((long)screenX * 65535 / screenWidth);
        int absY = (int)((long)screenY * 65535 / screenHeight);

        var inputs = new INPUT[3];
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE
                }
            }
        };
        inputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new MOUSEKEYBDHARDWAREUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } }
        };
        inputs[2] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new MOUSEKEYBDHARDWAREUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } }
        };

        uint sent = SendInput(3, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[Win32Interop] SendInput returned 0 (UIPI block?), GetLastError={err}");
        }
    }

    internal const int SM_CXSCREEN = 0;
    internal const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    #endregion
}
