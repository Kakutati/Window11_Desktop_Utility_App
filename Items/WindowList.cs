using System.Diagnostics;
using System.Windows.Media;
using RingLauncher.Interop;

namespace RingLauncher.Items;

/// <summary>실행 중인 창 하나. 실행 = 포그라운드로 전환.</summary>
public sealed class WindowItem(IntPtr hwnd, string title, ImageSource? icon) : IRingItem
{
    public IntPtr Hwnd => hwnd;
    public string Label => title;
    public ImageSource? Icon => icon;
    public void Execute() => ForegroundHelper.Activate(hwnd);
}

public static class WindowList
{
    // ponytail: hwnd별 아이콘 캐시. 창이 닫히면 항목이 사라질 뿐이라 정리 로직 없음. 수천 개 창을 여닫는 세션에서만 커진다.
    static readonly Dictionary<IntPtr, ImageSource?> IconCache = new();
    static readonly uint OwnPid = (uint)Environment.ProcessId;

    /// <summary>Z 순서(위→아래)로 사용자 창을 최대 max개. 현재 포그라운드 창은 전환 대상이 아니므로 제외.</summary>
    public static List<IRingItem> Enumerate(int max)
    {
        var list = new List<IRingItem>();
        var fg = Native.GetForegroundWindow();
        Native.EnumWindows((h, _) =>
        {
            if (list.Count >= max) return false;
            if (h == fg || !IsUserWindow(h)) return true;
            list.Add(new WindowItem(h, Native.WindowTitle(h), IconOf(h)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static bool IsUserWindow(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;
        var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
        if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.IsCloaked(h)) return false; // 다른 가상 데스크톱, UWP 유령 창
        Native.GetWindowThreadProcessId(h, out var pid);
        if (pid == OwnPid) return false;
        return Native.WindowTitle(h).Length > 0;
    }

    /// <summary>WM_GETICON(200ms 타임아웃, 상승 창은 차단됨) → 클래스 아이콘 → exe 아이콘.</summary>
    static ImageSource? IconOf(IntPtr h)
    {
        if (IconCache.TryGetValue(h, out var cached)) return cached;
        IntPtr hIcon = IntPtr.Zero;
        foreach (var kind in new[] { Native.ICON_SMALL2, Native.ICON_BIG })
        {
            Native.SendMessageTimeout(h, Native.WM_GETICON, new IntPtr(kind), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 200, out hIcon);
            if (hIcon != IntPtr.Zero) break;
        }
        if (hIcon == IntPtr.Zero) hIcon = Native.GetClassLongPtr(h, Native.GCLP_HICON);
        var icon = IconLoader.FromHIcon(hIcon) ?? IconLoader.Load(Native.ProcessPath(h));
        IconCache[h] = icon;
        return icon;
    }
}

public static class ForegroundHelper
{
    /// <summary>
    /// 포그라운드 잠금 규칙 때문에 SetForegroundWindow가 거부될 수 있다.
    /// 실패 시 Alt 탭 주입(마지막 입력을 우리 프로세스로 만드는 관용 기법) 후 재시도, 최후에 SwitchToThisWindow.
    /// </summary>
    public static void Activate(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd)) return;
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        if (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd) return;

        Native.SendKeys(Array.Empty<int>(), Native.VK_MENU);
        if (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd) return;

        Native.SwitchToThisWindow(hwnd, true);
        if (Native.GetForegroundWindow() != hwnd)
            Log.Write($"창 전환 실패 (관리자 권한 창일 수 있음): {Native.WindowTitle(hwnd)}");
    }
}
