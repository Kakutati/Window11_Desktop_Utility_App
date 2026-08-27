using System.IO;
using System.Text.Json;
using RingLauncher.Config;
using RingLauncher.Interop;

namespace RingLauncher.Shell;

/// <summary>Apply 직전의 원래 상태. 크래시 복구를 위해 파일로도 남긴다.</summary>
public sealed class TaskbarState
{
    public string Mode { get; set; } = "";
    public uint AppBarState { get; set; }
}

/// <summary>
/// 작업 표시줄 숨김/복구. mode: autohide(ABM_SETSTATE) | hideWindow(autohide + Shell_TrayWnd SW_HIDE, 가장자리에서도 안 나옴) | none.
/// SPI_SETWORKAREA는 쓰지 않는다: Explorer가 앱바 영역을 1초 내 되돌리고, SPIF_SENDCHANGE 브로드캐스트가 메인 스레드를 멈출 수 있다.
/// 상태 파일이 남아 있으면 이전 실행이 비정상 종료한 것 → 시작 시 RestoreFromFile()로 먼저 복구.
/// </summary>
public sealed class TaskbarController : IDisposable
{
    static readonly string StatePath = Path.Combine(ConfigStore.Dir, "taskbar-state.json");

    readonly TaskbarConfig _cfg;
    readonly ShellEventWindow _shell;
    TaskbarState? _saved;
    List<IntPtr> _trayWnds = new();
    IntPtr _showHook;
    Native.WinEventProc? _showHookProc; // GC 방지

    public TaskbarController(TaskbarConfig cfg, ShellEventWindow shell)
    {
        _cfg = cfg;
        _shell = shell;
        _shell.TaskbarCreated += Reapply;
    }

    bool IsHideWindow => _cfg.Mode.Equals("hideWindow", StringComparison.OrdinalIgnoreCase);
    bool IsAutoHide => _cfg.Mode.Equals("autohide", StringComparison.OrdinalIgnoreCase);

    public void Apply()
    {
        if (!IsAutoHide && !IsHideWindow) return;
        _saved = Capture(_cfg.Mode);
        Save(_saved);
        ApplyCore();
        Log.Write($"작업 표시줄 적용: {_cfg.Mode} (원래 ABM 상태={_saved.AppBarState})");
    }

    /// <summary>Explorer 재시작 후. 원래 상태는 이미 저장돼 있으므로 적용만 반복.</summary>
    void Reapply()
    {
        if (_saved is null) return;
        Log.Write("TaskbarCreated → 작업 표시줄 재적용");
        // Explorer 초기화가 몇 초 더 이어지며 트레이 창을 다시 보이게 하므로 몇 번 반복 적용
        foreach (var delayMs in new[] { 500, 2000, 5000 })
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            t.Tick += (_, _) => { t.Stop(); if (_saved is not null) ApplyCore(); };
            t.Start();
        }
    }

    void ApplyCore()
    {
        Native.SetAppBarState(Native.ABS_AUTOHIDE | Native.ABS_ALWAYSONTOP); // Explorer가 작업 영역을 스스로 반환
        if (IsAutoHide) return;
        _trayWnds = TrayWindows();
        foreach (var h in _trayWnds) Native.ShowWindow(h, Native.SW_HIDE);
        InstallShowHook();
    }

    /// <summary>Explorer가 Win 키/알림 등으로 트레이 창을 다시 보이면 즉시 재숨김.</summary>
    void InstallShowHook()
    {
        if (_showHook != IntPtr.Zero) return;
        _showHookProc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != Native.OBJID_WINDOW || !_trayWnds.Contains(hwnd)) return;
            Native.ShowWindow(hwnd, Native.SW_HIDE);
            Log.Write("트레이 창 재표시 감지 → 재숨김");
        };
        _showHook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, _showHookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    public void Restore()
    {
        if (_showHook != IntPtr.Zero) { Native.UnhookWinEvent(_showHook); _showHook = IntPtr.Zero; }
        var s = _saved ?? Load();
        _saved = null;
        if (s is null) return;
        RestoreState(s);
        File.Delete(StatePath);
        Log.Write("작업 표시줄 복구 완료");
    }

    /// <summary>시작 시 / --restore-taskbar. 파일이 있으면 그 상태로, 없으면 강제로 보이게. 복구했으면 true.</summary>
    public static bool RestoreFromFile(bool force = false)
    {
        var s = Load();
        if (s is not null)
        {
            RestoreState(s);
            File.Delete(StatePath);
            Log.Write("이전 비정상 종료 상태에서 작업 표시줄 복구");
            return true;
        }
        if (!force) return false;
        Native.SetAppBarState(Native.GetAppBarState() & ~Native.ABS_AUTOHIDE);
        foreach (var h in TrayWindows()) Native.ShowWindow(h, Native.SW_SHOWNA);
        return true;
    }

    static void RestoreState(TaskbarState s)
    {
        if (!s.Mode.Equals("autohide", StringComparison.OrdinalIgnoreCase))
            foreach (var h in TrayWindows()) Native.ShowWindow(h, Native.SW_SHOWNA);
        Native.SetAppBarState(s.AppBarState);
    }

    static List<IntPtr> TrayWindows()
    {
        var list = Native.FindWindowsByClass("Shell_SecondaryTrayWnd");
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Insert(0, main);
        return list;
    }

    static TaskbarState Capture(string mode) => new() { Mode = mode, AppBarState = Native.GetAppBarState() };

    static void Save(TaskbarState s)
    {
        Directory.CreateDirectory(ConfigStore.Dir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
    }

    static TaskbarState? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<TaskbarState>(File.ReadAllText(StatePath)) : null; }
        catch (Exception ex) { Log.Write($"taskbar-state.json 읽기 실패: {ex.Message}"); return null; }
    }

    public void Dispose()
    {
        _shell.TaskbarCreated -= Reapply;
        Restore();
    }
}
