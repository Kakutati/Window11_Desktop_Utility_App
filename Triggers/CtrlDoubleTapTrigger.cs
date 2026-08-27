using System.Windows.Threading;
using RingLauncher.Interop;

namespace RingLauncher.Triggers;

/// <summary>
/// Ctrl을 doubleTapMs 안에 두 번 누르면(사이에 다른 키 없이) 2번째 누름에서 Pressed, 그 Ctrl을 떼면 Released.
/// 2번째 Ctrl의 down/up은 삼켜서 다른 앱에 Ctrl 조합으로 새지 않게 한다. toggle: 떼도 유지, 다음 더블탭이 Released.
/// 상태 판단은 훅 스레드, 이벤트 발행은 UI 스레드.
/// </summary>
public sealed class CtrlDoubleTapTrigger : ITrigger
{
    public event Action<POINT>? Pressed;
    public event Action? Released;
    public int[] LingeringKeys { get; } = { Native.VK_CONTROL };

    readonly LowLevelHookHost _host;
    readonly TimeSpan _window;
    readonly bool _toggle;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    // 훅 스레드 전용 상태. _dirty: 직전 Ctrl 누름 이후 다른 키가 눌렸는가 (Ctrl+C 뒤의 Ctrl은 더블탭이 아니다)
    DateTime _lastCtrlUp = DateTime.MinValue;
    bool _ctrlDown, _dirty = true, _active;

    public CtrlDoubleTapTrigger(LowLevelHookHost host, int doubleTapMs, bool toggle)
    {
        _host = host;
        _window = TimeSpan.FromMilliseconds(doubleTapMs);
        _toggle = toggle;
    }

    public void Start()
    {
        _host.Keyboard = OnKey;
        _host.EnsureKeyboard();
    }

    public void Stop() { _host.Keyboard = null; Reset(); }
    public void Reset() { _active = false; }
    public void Dispose() => Stop();

    bool OnKey(LowLevelHookHost.KeyEvent e)
    {
        var isCtrl = e.Vk is Native.VK_LCONTROL or Native.VK_RCONTROL or Native.VK_CONTROL;
        if (!isCtrl)
        {
            if (e.Down) _dirty = true;
            return false;
        }
        if (e.Down)
        {
            if (_ctrlDown) return _active; // 자동 반복
            _ctrlDown = true;
            var isDouble = !_dirty && DateTime.Now - _lastCtrlUp <= _window;
            _dirty = false; // 새 탭 후보 시작
            if (_active)
            {
                if (!_toggle) return true;
                if (!isDouble) return false;
                _active = false; // toggle: 더블탭으로 닫기
                _ui.BeginInvoke(() => Released?.Invoke());
                return true;
            }
            if (!isDouble) return false;
            _active = true;
            Native.GetCursorPos(out var pt);
            _ui.BeginInvoke(() => Pressed?.Invoke(pt));
            return true;
        }
        // up
        _ctrlDown = false;
        _lastCtrlUp = DateTime.Now;
        if (!_active) return false;
        if (_toggle) return true;
        _active = false;
        _ui.BeginInvoke(() => Released?.Invoke());
        return true;
    }
}
