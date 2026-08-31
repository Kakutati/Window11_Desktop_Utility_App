using System.Windows;
using RingLauncher.Config;
using RingLauncher.Interop;
using RingLauncher.Shell;
using RingLauncher.UI;

namespace RingLauncher;

static class Program
{
    static SettingsWindow? _settings;

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
        // 리로드 후 설정 창은 열린 채 둔다(저장 직후 사라지면 불편). 외부 편집 반영만 로그.
        var host = new AppHost(_ => { });

        app.DispatcherUnhandledException += (_, e) => host.HandleCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => host.HandleCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Write(e.Exception);
        app.SessionEnding += (_, _) => host.RestoreTaskbar();

        try { host.Start(); }
        catch (Exception ex)
        {
            Log.Write(ex);
            MessageBox.Show(ex.Message, "RingLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            host.RestoreTaskbar();
            return 1;
        }

        // 링 항목이 설정/작업 표시줄에 접근할 수 있게 (작업 표시줄 숨김 시 트레이가 안 보이는 문제 대응)
        AppActions.OpenSettings = () => ShowSettings(host);
        AppActions.ToggleTaskbar = host.ToggleTaskbar;

        using var tray = new TrayIcon(
            openSettings: () => ShowSettings(host),
            restoreTaskbar: host.RestoreTaskbar,
            exit: app.Shutdown);

        if (args.Contains("--settings")) ShowSettings(host);
        if (args.Contains("--search")) UI.SearchWindow.Open();

        app.Run();
        host.Dispose();
        return 0;
    }

    static void ShowSettings(AppHost host)
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); return; }
        _settings = new SettingsWindow(host.Config, host.Save);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }
}
