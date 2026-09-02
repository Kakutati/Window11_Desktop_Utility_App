using System.Runtime.InteropServices;

namespace RingLauncher.Interop;

/// <summary>
/// 공식 IVirtualDesktopManager로 "이 창이 현재 가상 데스크톱에 있는가"를 판정.
/// UWP 앱은 포커스가 없으면 DWM이 cloak하므로 cloak 여부로 거르면 실행 중인 UWP가 누락된다.
/// 대신 다른 데스크톱에 있는 창만 걸러야 한다.
/// </summary>
public static class VirtualDesktop
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }

    static IVirtualDesktopManager? _mgr;

    static IVirtualDesktopManager? Manager()
    {
        if (_mgr is not null) return _mgr;
        try
        {
            var t = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")); // CLSID_VirtualDesktopManager
            if (t is null) return null;
            _mgr = (IVirtualDesktopManager?)Activator.CreateInstance(t);
        }
        catch (Exception ex) { Log.Write($"IVirtualDesktopManager 생성 실패: {ex.Message}"); }
        return _mgr;
    }

    /// <summary>현재 데스크톱에 있으면 true. 판정 실패 시 true(과도한 제외를 피해 포함 쪽으로).</summary>
    public static bool IsOnCurrentDesktop(IntPtr hwnd)
    {
        var m = Manager();
        if (m is null) return true;
        try { return m.IsWindowOnCurrentVirtualDesktop(hwnd, out var on) != 0 ? true : on != 0; }
        catch { return true; }
    }
}
