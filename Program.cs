using System.Diagnostics;
using System.Windows;
using RingLauncher.Config;
using RingLauncher.Core;
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

        using var mutex = new Mutex(true, @"Local\RingLauncher.SingleInstance", out var first);
        if (!first) return 0;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => Log.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject.ToString() ?? "unknown");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Write(e.Exception);

        var cfg = ConfigStore.Load();
        var items = ItemFactory.Create(cfg.Items);
        using var shell = new ShellEventWindow();
        var win = new RingWindow(cfg.Ring);
        using var trigger = new HotkeyTrigger(shell, cfg.Trigger.Hotkey, toggle: cfg.Trigger.Mode.Equals("toggle", StringComparison.OrdinalIgnoreCase));
        var ctrl = new RingController(win, trigger, shell, cfg.Ring, items);

        try { ctrl.Start(); }
        catch (Exception ex)
        {
            Log.Write(ex);
            MessageBox.Show(ex.Message, "RingLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        Log.Write($"시작: 트리거={cfg.Trigger.Hotkey} ({cfg.Trigger.Mode}), 항목={items.Count}");

        using var tray = new TrayIcon(
            restart: () =>
            {
                ctrl.Stop();
                mutex.ReleaseMutex();
                Process.Start(Environment.ProcessPath!);
                app.Shutdown();
            },
            exit: app.Shutdown);

        app.Run();
        ctrl.Stop();
        return 0;
    }
}
