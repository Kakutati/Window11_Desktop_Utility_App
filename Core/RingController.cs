using System.Windows.Media;
using System.Windows.Threading;
using RingLauncher.Config;
using RingLauncher.Interop;
using RingLauncher.Items;
using RingLauncher.Shell;
using RingLauncher.Triggers;
using RingLauncher.UI;

namespace RingLauncher.Core;

/// <summary>
/// Idle → Open(프레임마다 히트 테스트 + 마우스 버튼 폴링) → 실행/취소 → Idle.
/// 열린 동안: 트리거 Released → 하이라이트 항목 실행. 주클릭: 섹터면 실행, 아니면 이탈. 보조클릭/ESC: 이탈.
/// </summary>
public sealed class RingController
{
    const int EscHotkeyId = 2;

    readonly RingWindow _win;
    readonly ITrigger _trigger;
    readonly ShellEventWindow _shell;
    readonly RingConfig _cfg;
    readonly Func<List<IRingItem>> _source;
    readonly int _primaryButton, _secondaryButton;
    IReadOnlyList<IRingItem> _items = Array.Empty<IRingItem>();

    bool _open, _primaryDown, _secondaryDown;
    POINT _center;
    double _scale = 1;
    Hit _hit = Hit.None;

    public RingController(RingWindow win, ITrigger trigger, ShellEventWindow shell, RingConfig cfg, Func<List<IRingItem>> source)
    {
        _win = win; _trigger = trigger; _shell = shell; _cfg = cfg; _source = source;
        // GetAsyncKeyState는 물리 버튼 기준이라 좌우 바꿈 설정을 직접 반영
        var swapped = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0;
        _primaryButton = swapped ? Native.VK_RBUTTON : Native.VK_LBUTTON;
        _secondaryButton = swapped ? Native.VK_LBUTTON : Native.VK_RBUTTON;
    }

    public void Start()
    {
        _trigger.Pressed += OnPressed;
        _trigger.Released += OnReleased;
        _shell.HotkeyPressed += OnShellHotkey;
        _trigger.Start();
    }

    public void Stop()
    {
        Close();
        _trigger.Stop();
        _trigger.Pressed -= OnPressed;
        _trigger.Released -= OnReleased;
        _shell.HotkeyPressed -= OnShellHotkey;
    }

    void OnShellHotkey(int id) { if (id == EscHotkeyId) Close(); }

    void OnPressed(POINT cursor)
    {
        if (_open) return;
        _items = _source(); // 창 목록 등 동적 항목은 열릴 때마다 갱신
        if (_items.Count == 0) return;
        Log.Write("Open: " + string.Join(" | ", _items.Select(i => i.Label)));

        var (mon, dpi) = Native.MonitorAt(cursor);
        _scale = dpi / 96.0;
        int half = (int)Math.Round(_cfg.Diameter * _scale / 2);
        _center = new POINT
        {
            X = Math.Clamp(cursor.X, mon.Left + half, Math.Max(mon.Left + half, mon.Right - half)),
            Y = Math.Clamp(cursor.Y, mon.Top + half, Math.Max(mon.Top + half, mon.Bottom - half)),
        };
        _hit = Hit.None;
        _primaryDown = Native.IsKeyDown(_primaryButton);
        _secondaryDown = Native.IsKeyDown(_secondaryButton);

        _win.SetItems(_items);
        _win.ShowAt(_center, dpi);
        Native.RegisterHotKey(_shell.Handle, EscHotkeyId, 0, Native.VK_ESCAPE);
        CompositionTarget.Rendering += OnFrame;
        _open = true;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        Native.GetCursorPos(out var p);
        double dx = (p.X - _center.X) / _scale, dy = (p.Y - _center.Y) / _scale;
        var hit = HitTester.Test(dx, dy, _cfg.DeadZone, _cfg.OuterRadius, _cfg.StartAngle, _items.Count);
        if (hit != _hit)
        {
            _hit = hit;
            _win.Highlight(hit.Zone == HitZone.Inner ? hit.Index : -1);
        }

        // 버튼 눌림 전이(up→down)만 클릭으로 본다. 링 밖 클릭은 아래 앱에도 전달된다(모달 아님).
        // 트리거 릴리즈는 방향 제스처라 링 바깥 거리도 허용하지만, 클릭은 링 안(outerRadius 이내)만 선택으로 인정.
        var primary = Native.IsKeyDown(_primaryButton);
        var secondary = Native.IsKeyDown(_secondaryButton);
        if (primary && !_primaryDown)
        {
            if (dx * dx + dy * dy <= _cfg.OuterRadius * _cfg.OuterRadius) Commit();
            else Close();
        }
        else if (secondary && !_secondaryDown) Close();
        _primaryDown = primary;
        _secondaryDown = secondary;
    }

    void OnReleased() => Commit();

    /// <summary>하이라이트된 항목이 있으면 실행, 없으면 이탈. 창을 먼저 숨겨야 실행된 앱이 포그라운드를 가져갈 수 있다.</summary>
    void Commit()
    {
        if (!_open) return;
        var hit = _hit;
        Close();
        if (hit.Zone == HitZone.Inner) ExecuteWhenKeysUp(_items[hit.Index]);
    }

    /// <summary>트리거 수정자(Ctrl/Alt)가 아직 눌려 있으면 실행할 키 시퀀스와 섞이므로 떼어질 때까지 대기.</summary>
    void ExecuteWhenKeysUp(IRingItem item)
    {
        var deadline = DateTime.Now.AddSeconds(1);
        var t = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
        t.Tick += (_, _) =>
        {
            if (DateTime.Now < deadline && _trigger.LingeringKeys.Any(Native.IsKeyDown)) return;
            t.Stop();
            try { item.Execute(); }
            catch (Exception ex) { Log.Write($"실행 실패 ({item.Label}): {ex.Message}"); }
        };
        t.Start();
    }

    void Close()
    {
        if (!_open) return;
        _open = false;
        CompositionTarget.Rendering -= OnFrame;
        Native.UnregisterHotKey(_shell.Handle, EscHotkeyId);
        _win.HideRing();
        _trigger.Reset();
    }
}
