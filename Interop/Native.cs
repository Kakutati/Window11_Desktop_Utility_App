using System.Runtime.InteropServices;

namespace RingLauncher.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Explicit)]
public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

[StructLayout(LayoutKind.Sequential)]
public struct INPUT { public uint type; public INPUTUNION u; }

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

internal static class Native
{
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_DISPLAYCHANGE = 0x7E, WM_WTSSESSION_CHANGE = 0x2B1, WTS_SESSION_LOCK = 7, NOTIFY_FOR_THIS_SESSION = 0;
    // SHQueryUserNotificationState
    public const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4, QUNS_ACCEPTS_NOTIFICATIONS = 5;

    [DllImport("shell32")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("wtsapi32")] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
    [DllImport("wtsapi32")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    /// <summary>전체화면(D3D 독점, 테두리 없는 전체화면 창, 프레젠테이션 모드)인지.</summary>
    public static bool IsFullscreenState()
    {
        if (SHQueryUserNotificationState(out var s) != 0) return false;
        return s is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }
    public const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    public const uint WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_QUIT = 0x12;
    public const uint LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x1;
    public const uint INPUT_MOUSE = 0, MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40;
    public const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    /// <summary>우리가 재생한 입력 표식. 훅에서 이 값이면 손대지 않는다.</summary>
    public static readonly IntPtr InjectMagic = new(0x52494E47); // 'RING'

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32")] public static extern uint GetCurrentThreadId();

    public static void SendMiddleClick()
    {
        var arr = new[]
        {
            new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_MIDDLEDOWN, dwExtraInfo = InjectMagic } } },
            new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_MIDDLEUP, dwExtraInfo = InjectMagic } } },
        };
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    public const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    public const int SW_HIDE = 0, SW_SHOWNA = 8;
    public const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0;
    public const int OBJID_WINDOW = 0;

    public const long WS_EX_APPWINDOW = 0x40000;
    public const uint GW_OWNER = 4, DWMWA_CLOAKED = 14, WM_GETICON = 0x7F, ICON_SMALL2 = 2, ICON_BIG = 1;
    public const uint SMTO_ABORTIFHUNG = 2, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int GCLP_HICON = -14, SW_RESTORE = 9;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("shell32")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32")] public static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
    [DllImport("user32")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32")] public static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attr, out int value, int size);
    [DllImport("kernel32")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr proc, uint flags, System.Text.StringBuilder sb, ref int size);
    [DllImport("kernel32")] public static extern bool CloseHandle(IntPtr h);

    public static string WindowTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var v, sizeof(int)) == 0 && v != 0;

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION은 상승 프로세스에도 허용된다.</summary>
    public static string? ProcessPath(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static uint GetAppBarState()
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        return (uint)SHAppBarMessage(ABM_GETSTATE, ref d);
    }

    public static void SetAppBarState(uint state)
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), lParam = new IntPtr(state) };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    public static List<IntPtr> FindWindowsByClass(string cls)
    {
        var list = new List<IntPtr>();
        var sb = new System.Text.StringBuilder(64);
        EnumWindows((h, _) =>
        {
            sb.Clear();
            GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() == cls) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    public const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, SM_SWAPBUTTON = 23;
    public const int VK_ESCAPE = 0x1B, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("shcore")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32")] public static extern bool AttachConsole(int pid);

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static void AddExStyle(IntPtr hwnd, long flags)
    {
        var cur = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((cur & flags) != flags) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(cur | flags));
    }

    /// <summary>커서가 있는 모니터의 물리 영역과 DPI.</summary>
    public static (RECT bounds, uint dpi) MonitorAt(POINT pt)
    {
        var h = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(h, ref mi);
        if (GetDpiForMonitor(h, 0, out var dpi, out _) != 0) dpi = 96;
        return (mi.rcMonitor, dpi);
    }

    public static void SendKeys(int[] modifierVks, int vk)
    {
        var list = new List<INPUT>();
        foreach (var m in modifierVks) list.Add(Key(m, false));
        list.Add(Key(vk, false));
        list.Add(Key(vk, true));
        foreach (var m in modifierVks.Reverse()) list.Add(Key(m, true));
        var arr = list.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());

        static INPUT Key(int vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
        };
    }
}
