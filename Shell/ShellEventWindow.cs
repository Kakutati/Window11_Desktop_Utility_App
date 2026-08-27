using System.Windows.Forms;
using RingLauncher.Interop;

namespace RingLauncher.Shell;

/// <summary>
/// 숨김 top-level 창. WM_HOTKEY와 셸 브로드캐스트(TaskbarCreated)를 받는다.
/// HWND_MESSAGE 창은 브로드캐스트를 못 받으므로 반드시 top-level.
/// </summary>
public sealed class ShellEventWindow : NativeWindow, IDisposable
{
    public event Action<int>? HotkeyPressed;

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
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
