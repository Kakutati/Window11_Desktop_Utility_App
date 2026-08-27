using System.Diagnostics;
using System.Windows;
using RingLauncher.Config;
using RingLauncher.Core;
using RingLauncher.Interop;
using RingLauncher.Items;
using RingLauncher.Shell;
using RingLauncher.Triggers;
using RingLauncher.UI;

namespace RingLauncher;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--restore-taskbar"))
        {
            Native.AttachConsole(-1);
            Console.WriteLine(TaskbarController.RestoreFromFile(force: true) ? "작업 표시줄 복구 완료" : "복구할 것 없음");
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\RingLauncher.SingleInstance", out var first);
        if (!first) return 0;

        TaskbarController.RestoreFromFile(); // 이전 비정상 종료 잔여 복구

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var cfg = ConfigStore.Load();
        var source = ItemFactory.CreateSource(cfg.Items, cfg.Policy);
        using var shell = new ShellEventWindow();
        using var taskbar = new TaskbarController(cfg.Taskbar, shell);
        var win = new RingWindow(cfg.Ring);
        var toggle = cfg.Trigger.Mode.Equals("toggle", StringComparison.OrdinalIgnoreCase);
        using var hooks = new LowLevelHookHost(); // 훅 트리거가 Start()할 때만 실제 설치
        using ITrigger trigger = cfg.Trigger.Type.ToLowerInvariant() switch
        {
            "ctrldoubletap" => new CtrlDoubleTapTrigger(hooks, cfg.Trigger.DoubleTapMs, toggle),
            "middlehold" => new MiddleHoldTrigger(hooks, cfg.Trigger.HoldMs, toggle),
            _ => new HotkeyTrigger(shell, cfg.Trigger.Hotkey, toggle),
        };
        if (trigger is not HotkeyTrigger)
            Log.Write("훅 트리거: 관리자 권한 창이 활성일 때는 입력이 전달되지 않아 동작하지 않습니다(UIPI). 그 경우 핫키 트리거를 쓰세요.");
        var ctrl = new RingController(win, trigger, shell, cfg.Ring, source);

        // 어떤 경로로 죽든 작업 표시줄은 되돌린다 (강제 종료는 상태 파일 + 다음 실행 시 복구)
        app.DispatcherUnhandledException += (_, e) => { Log.Write(e.Exception); taskbar.Restore(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Log.Write(e.ExceptionObject.ToString() ?? "unknown"); taskbar.Restore(); };
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Write(e.Exception);
        app.SessionEnding += (_, _) => taskbar.Restore();

        try { ctrl.Start(); }
        catch (Exception ex)
        {
            Log.Write(ex);
            MessageBox.Show(ex.Message, "RingLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        taskbar.Apply();
        Log.Write($"시작: 트리거={cfg.Trigger.Type}/{cfg.Trigger.Hotkey} ({cfg.Trigger.Mode}), 작업 표시줄={cfg.Taskbar.Mode}, 항목={cfg.Items.Count}");

        using var tray = new TrayIcon(
            restart: () =>
            {
                ctrl.Stop();
                taskbar.Restore();
                mutex.ReleaseMutex();
                Process.Start(Environment.ProcessPath!);
                app.Shutdown();
            },
            restoreTaskbar: taskbar.Restore,
            exit: app.Shutdown);

        app.Run();
        ctrl.Stop();
        taskbar.Restore();
        return 0;
    }
}
