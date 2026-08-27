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

internal static class Native
{
    public const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    public const int SW_HIDE = 0, SW_SHOWNA = 8;
    public const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0;
    public const int OBJID_WINDOW = 0;

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
