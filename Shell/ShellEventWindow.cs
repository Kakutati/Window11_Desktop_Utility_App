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

    public ShellEventWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "RingLauncher.Shell",
            ExStyle = (int)Native.WS_EX_TOOLWINDOW,
            Style = 0, // WS_OVERLAPPED, WS_VISIBLE 없음
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
        else if (m.Msg == WM_TASKBARCREATED)
            TaskbarCreated?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
