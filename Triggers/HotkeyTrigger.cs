using System.Windows.Threading;
using RingLauncher.Interop;
using RingLauncher.Shell;

namespace RingLauncher.Triggers;

/// <summary>
/// RegisterHotKey로 연다. 훅 불필요, 관리자 창 위에서도 동작.
/// hold: GetAsyncKeyState 폴링으로 떼는 순간 Released. toggle: 다시 누르면 Released.
/// </summary>
public sealed class HotkeyTrigger : ITrigger
{
    public const int HotkeyId = 1;

    public event Action<POINT>? Pressed;
    public event Action? Released;
    public int[] LingeringKeys => _combo.ModifierVks;

    readonly ShellEventWindow _shell;
    readonly KeyCombo _combo;
    readonly string _hotkeyText;
    readonly bool _toggle;
    // Input 우선순위: 기본 Background는 CompositionTarget.Rendering(매 프레임)에 기아 상태가 되어 릴리즈를 못 잡는다.
    readonly DispatcherTimer _poll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
    bool _held;

    public HotkeyTrigger(ShellEventWindow shell, string hotkey, bool toggle = false)
    {
        _shell = shell;
        _hotkeyText = hotkey;
        _toggle = toggle;
        _combo = KeyCombo.Parse(hotkey);
        _poll.Tick += (_, _) =>
        {
            if (Native.IsKeyDown(_combo.Vk)) return;
            _poll.Stop();
            Release();
        };
    }

    public void Start()
    {
        _shell.HotkeyPressed += OnHotkey;
        if (!Native.RegisterHotKey(_shell.Handle, HotkeyId, _combo.Modifiers | Native.MOD_NOREPEAT, (uint)_combo.Vk))
            throw new InvalidOperationException($"핫키 등록 실패: {_hotkeyText} (Win32 오류 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}; 다른 앱이 사용 중일 수 있음)");
    }

    public void Stop()
    {
        _shell.HotkeyPressed -= OnHotkey;
        Native.UnregisterHotKey(_shell.Handle, HotkeyId);
        _poll.Stop();
        _held = false;
    }

    /// <summary>컨트롤러가 다른 경로(클릭/ESC)로 닫았을 때 호출해 상태를 맞춘다.</summary>
    public void Reset()
    {
        _poll.Stop();
        _held = false;
    }

    void OnHotkey(int id)
    {
        if (id != HotkeyId) return;
        if (_held)
        {
            if (_toggle) Release();
            return;
        }
        _held = true;
        Native.GetCursorPos(out var pt);
        Pressed?.Invoke(pt);
        if (!_toggle) _poll.Start();
    }

    void Release()
    {
        _held = false;
        Released?.Invoke();
    }

    public void Dispose() => Stop();
}
