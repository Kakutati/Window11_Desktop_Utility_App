using System.Windows.Forms;
using RingLauncher.Interop;

namespace RingLauncher.Shell;

/// <summary>
/// 숨김 top-level 창. WM_HOTKEY와 셸 브로드캐스트(TaskbarCreated)를 받는다.
/// HWND_MESSAGE 창은 브로드캐스트를 못 받으므로 반드시 top-level.
/// </summary>
public sealed class ShellEventWindow : NativeWindow, IDisposable
{
    static readonly int WM_TASKBARCREATED = (int)Native.RegisterWindowMessage("TaskbarCreated");

    public event Action<int>? HotkeyPressed;
    /// <summary>Explorer(작업 표시줄) 재시작 후. 작업 표시줄 상태 재적용 필요.</summary>
    public event Action? TaskbarCreated;
    /// <summary>해상도/모니터 구성 변경.</summary>
    public event Action? DisplayChanged;
    /// <summary>세션 잠금(Win+L). 열린 링은 닫아야 한다.</summary>
    public event Action? SessionLocked;

    public ShellEventWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "RingLauncher.Shell",
            ExStyle = (int)Native.WS_EX_TOOLWINDOW,
            Style = 0, // WS_OVERLAPPED, WS_VISIBLE 없음
        });
        Native.WTSRegisterSessionNotification(Handle, Native.NOTIFY_FOR_THIS_SESSION);
    }

    protected override void WndProc(ref Message m)
    {
        try
        {
            if (m.Msg == Native.WM_HOTKEY)
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
            else if (m.Msg == WM_TASKBARCREATED)
                TaskbarCreated?.Invoke();
            else if (m.Msg == Native.WM_DISPLAYCHANGE)
                DisplayChanged?.Invoke();
            else if (m.Msg == Native.WM_WTSSESSION_CHANGE && m.WParam.ToInt64() == Native.WTS_SESSION_LOCK)
                SessionLocked?.Invoke();
        }
        catch (Exception ex)
        {
            // WinForms NativeWindow는 WndProc 예외를 삼키므로 직접 남긴다
            Log.Write(ex);
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Native.WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }
}
