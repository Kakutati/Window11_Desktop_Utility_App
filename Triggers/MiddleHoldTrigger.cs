using System.Windows.Threading;
using RingLauncher.Interop;

namespace RingLauncher.Triggers;

/// <summary>
/// 가운데 버튼을 holdMs 이상 누르면 Pressed, 떼면 Released. 홀드 판정 전까지 down을 삼키고,
/// holdMs 안에 떼면 짧은 클릭이었으므로 down+up을 재생한다(InjectMagic 표식으로 훅이 무시).
/// toggle: 떼도 유지, 다음 가운데 버튼 누름이 Released.
/// </summary>
public sealed class MiddleHoldTrigger : ITrigger
{
    public event Action<POINT>? Pressed;
    public event Action? Released;
    public int[] LingeringKeys { get; } = Array.Empty<int>();

    readonly LowLevelHookHost _host;
    readonly int _holdMs;
    readonly bool _toggle;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly object _lock = new();

    // _lock 보호
    Timer? _holdTimer;
    bool _pendingDown, _active;
    POINT _downPoint;

    public MiddleHoldTrigger(LowLevelHookHost host, int holdMs, bool toggle)
    {
        _host = host;
        _holdMs = holdMs;
        _toggle = toggle;
    }

    public void Start()
    {
        _host.Mouse = OnMouse;
        _host.EnsureMouse();
    }

    public void Stop() { _host.Mouse = null; Reset(); }

    public void Reset()
    {
        lock (_lock) { _active = false; _pendingDown = false; _holdTimer?.Dispose(); _holdTimer = null; }
    }

    public void Dispose() => Stop();

    bool OnMouse(LowLevelHookHost.MouseEvent e)
    {
        if (e.Message == Native.WM_MBUTTONDOWN)
        {
            lock (_lock)
            {
                if (_active)
                {
                    if (!_toggle) return true;
                    _active = false;
                    _ui.BeginInvoke(() => Released?.Invoke());
                    return true;
                }
                _pendingDown = true;
                _downPoint = e.Point;
                _holdTimer?.Dispose();
                _holdTimer = new Timer(_ => OnHoldElapsed(), null, _holdMs, Timeout.Infinite);
            }
            return true;
        }
        if (e.Message == Native.WM_MBUTTONUP)
        {
            lock (_lock)
            {
                if (_pendingDown)
                {
                    // 짧은 클릭: 삼킨 down과 이 up을 재생
                    _pendingDown = false;
                    _holdTimer?.Dispose();
                    _holdTimer = null;
                    _ui.BeginInvoke(Native.SendMiddleClick);
                    return true;
                }
                if (!_active) return false;
                if (_toggle) return true;
                _active = false;
            }
            _ui.BeginInvoke(() => Released?.Invoke());
            return true;
        }
        return false;
    }

    void OnHoldElapsed()
    {
        POINT pt;
        lock (_lock)
        {
            if (!_pendingDown) return;
            _pendingDown = false;
            _active = true;
            pt = _downPoint;
        }
        _ui.BeginInvoke(() => Pressed?.Invoke(pt));
    }
}
