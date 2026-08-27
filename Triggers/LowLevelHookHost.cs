using System.Runtime.InteropServices;
using RingLauncher.Interop;

namespace RingLauncher.Triggers;

/// <summary>
/// WH_KEYBOARD_LL / WH_MOUSE_LL을 전용 스레드에 설치한다. 핸들러는 훅 스레드에서 동기 호출되며
/// true를 돌려주면 이벤트를 삼킨다. 핸들러는 즉시 반환해야 한다(LowLevelHooksTimeout 초과가 누적되면 Windows가 훅을 조용히 제거).
/// 우리가 재생한 입력(dwExtraInfo == InjectMagic)은 핸들러에 전달하지 않는다.
/// </summary>
public sealed class LowLevelHookHost : IDisposable
{
    public readonly record struct KeyEvent(int Vk, bool Down);
    public readonly record struct MouseEvent(uint Message, POINT Point);

    /// <summary>훅 스레드에서 호출. true = 삼킴.</summary>
    public Func<KeyEvent, bool>? Keyboard;
    public Func<MouseEvent, bool>? Mouse;

    Thread? _thread;
    uint _threadId;
    IntPtr _kbHook, _mouseHook;
    Native.HookProc? _kbProc, _mouseProc; // GC 방지
    bool _wantKeyboard, _wantMouse;

    public void EnsureKeyboard() { _wantKeyboard = true; EnsureThread(); }
    public void EnsureMouse() { _wantMouse = true; EnsureThread(); }

    void EnsureThread()
    {
        if (_thread is not null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            var mod = Native.GetModuleHandle(null);
            if (_wantKeyboard) _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc = KbProc, mod, 0);
            if (_wantMouse) _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc = MouseProc, mod, 0);
            if ((_wantKeyboard && _kbHook == IntPtr.Zero) || (_wantMouse && _mouseHook == IntPtr.Zero))
                Log.Write($"LL 훅 설치 실패 (Win32 오류 {Marshal.GetLastWin32Error()})");
            ready.Set();
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { /* LL 훅은 메시지 루프만 있으면 된다 */ }
            if (_kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_kbHook);
            if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        }) { IsBackground = true, Name = "RingLauncher.LLHook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        // ponytail: 훅 생존 감시 없음. 콜백이 사실상 상수 시간이라 타임아웃 제거는 실사용에서 확인되면 30초 주기 재설치로 대응.
    }

    IntPtr KbProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Keyboard is { } h)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (k.dwExtraInfo != Native.InjectMagic)
            {
                var msg = (uint)wParam;
                var down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                if (h(new KeyEvent((int)k.vkCode, down))) return new IntPtr(1);
            }
        }
        return Native.CallNextHookEx(_kbHook, code, wParam, lParam);
    }

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Mouse is { } h)
        {
            var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (m.dwExtraInfo != Native.InjectMagic && h(new MouseEvent((uint)wParam, m.pt))) return new IntPtr(1);
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
