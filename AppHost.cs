using System.IO;
using RingLauncher.Config;
using RingLauncher.Core;
using RingLauncher.Items;
using RingLauncher.Shell;
using RingLauncher.Triggers;
using RingLauncher.UI;

namespace RingLauncher;

/// <summary>
/// 재구성 가능한 런타임을 한곳에서 소유한다. 설정 저장·외부 편집 모두 Reload() 한 경로로 반영된다.
/// shell/hooks/RingWindow는 영구(핸들·훅 재설치 비용 회피), 나머지(트리거/작업표시줄/컨트롤러)만 교체.
/// </summary>
public sealed class AppHost : IDisposable
{
    readonly ShellEventWindow _shell = new();
    readonly LowLevelHookHost _hooks = new();
    readonly FileSystemWatcher _watcher;
    readonly Action<AppConfig> _onReload; // UI(설정 창 등) 갱신용

    RingWindow _win = null!;
    TaskbarController _taskbar = null!;
    ITrigger _trigger = null!;
    RingController _ctrl = null!;
    string _lastJson = "";
    DateTime _lastReload = DateTime.MinValue;

    public AppConfig Config { get; private set; } = null!;
    public event Action<Exception>? OnCrash;

    public AppHost(Action<AppConfig> onReload)
    {
        _onReload = onReload;
        _watcher = new FileSystemWatcher(ConfigStore.Dir, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(HotReload);
    }

    public void Start()
    {
        Config = ConfigStore.Load();
        _lastJson = SafeRead();
        _win = new RingWindow(Config.Ring);
        BuildRuntime();
        Log.Write($"시작: 트리거={Config.Trigger.Type}/{Config.Trigger.Hotkey} ({Config.Trigger.Mode}), 작업 표시줄={Config.Taskbar.Mode}, 항목={Config.Items.Count}");
    }

    void BuildRuntime()
    {
        var toggle = Config.Trigger.Mode.Equals("toggle", StringComparison.OrdinalIgnoreCase);
        _trigger = Config.Trigger.Type.ToLowerInvariant() switch
        {
            "ctrldoubletap" => new CtrlDoubleTapTrigger(_hooks, Config.Trigger.DoubleTapMs, toggle),
            "middlehold" => new MiddleHoldTrigger(_hooks, Config.Trigger.HoldMs, toggle),
            _ => new HotkeyTrigger(_shell, Config.Trigger.Hotkey, toggle),
        };
        if (_trigger is not HotkeyTrigger)
            Log.Write("훅 트리거: 관리자 권한 창이 활성일 때는 UIPI로 동작하지 않습니다. 그 경우 핫키 트리거를 쓰세요.");

        _taskbar = new TaskbarController(Config.Taskbar, _shell);
        var source = ItemFactory.CreateSource(Config.Items, Config.Policy);
        _ctrl = new RingController(_win, _trigger, _shell, Config.Ring, Config.Policy, source);

        try { _ctrl.Start(); }
        catch (Exception ex) { Log.Write(ex); throw; }
        _taskbar.Apply();
    }

    void TearDownRuntime()
    {
        _ctrl.Stop();
        _trigger.Dispose();
        _taskbar.Dispose(); // 작업 표시줄 복구 후 새 전략이 다시 Apply
    }

    /// <summary>Ring(창 크기·색)은 재생성이 필요하므로 전체 리로드에 포함. 설정 저장/외부 편집 공통 경로.</summary>
    public void Reload()
    {
        var json = SafeRead();
        _lastReload = DateTime.Now;
        try
        {
            var cfg = ConfigStore.Load();
            TearDownRuntime();
            Config = cfg;
            _lastJson = json;
            _win.Close();
            _win = new RingWindow(Config.Ring);
            BuildRuntime();
            _onReload(Config);
            Log.Write("설정 리로드 완료");
        }
        catch (Exception ex) { Log.Write($"리로드 실패(이전 설정 유지): {ex.Message}"); }
    }

    void HotReload()
    {
        // 저장 직후 여러 번 오는 Changed와 우리 자신의 저장을 흡수 (내용 동일 or 500ms 이내면 무시)
        var json = SafeRead();
        if (json.Length == 0 || json == _lastJson || DateTime.Now - _lastReload < TimeSpan.FromMilliseconds(500)) return;
        Reload();
    }

    static string SafeRead()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return File.ReadAllText(ConfigStore.FilePath); }
            catch (IOException) { Thread.Sleep(50); }
        }
        return "";
    }

    /// <summary>설정 창의 저장 버튼. 검증 통과 시 저장 → 리로드, 실패 시 메시지 반환.</summary>
    public string? Save(AppConfig cfg)
    {
        try { ConfigStore.Save(cfg); }
        catch (Exception ex) { return ex.Message; }
        Reload();
        return null;
    }

    public void RestoreTaskbar() => _taskbar.Restore();
    public void ToggleTaskbar() => _taskbar.Toggle();

    public void Dispose()
    {
        _watcher.Dispose();
        TearDownRuntime();
        _hooks.Dispose();
        _shell.Dispose();
    }

    // 크래시 시 작업 표시줄 복구
    public void HandleCrash(Exception ex) { OnCrash?.Invoke(ex); Log.Write(ex); try { _taskbar.Restore(); } catch { } }
}
