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
/// 서브메뉴 섹터는 바깥쪽으로 밀거나 150ms 머무르면 바깥 링으로 펼쳐지고, 다른 안쪽 섹터/dead zone으로 가면 접힌다.
/// </summary>
public sealed class RingController
{
    const int EscHotkeyId = 2;
    static readonly TimeSpan DwellToExpand = TimeSpan.FromMilliseconds(150);

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

    // 바깥 링(서브메뉴) 상태
    int _expanded = -1;
    OuterRing? _outer;
    IReadOnlyList<IRingItem> _outerItems = Array.Empty<IRingItem>();
    int _hoverIndex = -1;
    DateTime _hoverSince;

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
        _hoverIndex = -1;
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
        double dx = (p.X - _center.X) / _scale, dy = (p.Y - _center.Y) / _scale, r2 = dx * dx + dy * dy;
        var hit = HitTester.Test(dx, dy, _cfg.DeadZone, _cfg.OuterRadius, _cfg.StartAngle, _items.Count, _outer);
        UpdateExpansion(hit, r2);
        if (hit != _hit)
        {
            _hit = hit;
            _win.Highlight(
                inner: hit.Zone == HitZone.Inner ? hit.Index : _expanded,
                outer: hit.Zone == HitZone.Outer ? hit.Index : -1);
        }

        // 버튼 눌림 전이(up→down)만 클릭으로 본다. 링 밖 클릭은 아래 앱에도 전달된다(모달 아님).
        // 트리거 릴리즈는 방향 제스처라 링 바깥 거리도 허용하지만, 클릭은 링 안(펼쳐졌으면 바깥 링까지)만 선택으로 인정.
        var primary = Native.IsKeyDown(_primaryButton);
        var secondary = Native.IsKeyDown(_secondaryButton);
        if (primary && !_primaryDown)
        {
            var limit = _expanded >= 0 ? _cfg.SubRadius : _cfg.OuterRadius;
            if (r2 <= limit * limit) Commit();
            else Close();
        }
        else if (secondary && !_secondaryDown) Close();
        _primaryDown = primary;
        _secondaryDown = secondary;
    }

    void UpdateExpansion(Hit hit, double r2)
    {
        switch (hit.Zone)
        {
            case HitZone.Inner:
                if (_expanded >= 0 && hit.Index != _expanded) Collapse();
                if (hit.Index != _hoverIndex) { _hoverIndex = hit.Index; _hoverSince = DateTime.Now; }
                var pushedOut = r2 > 0.64 * _cfg.OuterRadius * _cfg.OuterRadius; // r > 0.8·outerRadius
                if (_expanded < 0 && (pushedOut || DateTime.Now - _hoverSince >= DwellToExpand)) TryExpand(hit.Index);
                break;
            case HitZone.Dead:
                if (_expanded >= 0) Collapse();
                _hoverIndex = -1;
                break;
            default: // Outer / None: 펼침 유지
                _hoverIndex = -1;
                break;
        }
    }

    bool TryExpand(int index)
    {
        var children = _items[index].GetChildren();
        if (children is not { Count: > 0 }) return false;
        _expanded = index;
        _outerItems = children;
        _outer = HitTester.Outer(HitTester.SectorCenter(_cfg.StartAngle, 360, _items.Count, index), children.Count);
        _win.ShowOuter(children, _outer.Value);
        Log.Write($"Expand: {_items[index].Label} ({children.Count})");
        return true;
    }

    void Collapse()
    {
        if (_expanded < 0) return;
        _expanded = -1;
        _outer = null;
        _outerItems = Array.Empty<IRingItem>();
        _win.HideOuter();
        Log.Write("Collapse");
    }

    void OnReleased() => Commit();

    /// <summary>하이라이트된 항목 실행. 서브메뉴 부모면 펼친 채 유지(클릭/ESC로 마무리). 창을 먼저 숨겨야 실행된 앱이 포그라운드를 가져갈 수 있다.</summary>
    void Commit()
    {
        if (!_open) return;
        var hit = _hit;
        var item = hit.Zone switch
        {
            HitZone.Inner => _items[hit.Index],
            HitZone.Outer => _outerItems[hit.Index],
            _ => null,
        };
        if (item is not null && hit.Zone == HitZone.Inner && item.GetChildren() is { Count: > 0 })
        {
            if (_expanded != hit.Index) TryExpand(hit.Index);
            return;
        }
        Close();
        if (item is not null) ExecuteWhenKeysUp(item);
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
        Collapse();
        _win.HideRing();
        _trigger.Reset();
    }
}
