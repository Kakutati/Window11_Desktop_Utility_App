using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RingLauncher.Config;

namespace RingLauncher.UI;

public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;

    public TrayIcon(Action restart, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("설정 폴더 열기", null, (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", ConfigStore.Dir) { UseShellExecute = true }));
        menu.Items.Add("다시 시작 (설정 재적용)", null, (_, _) => restart());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RingLauncher",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
