using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RingLauncher.Config;

namespace RingLauncher.UI;

public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;

    public TrayIcon(Action openSettings, Action restoreTaskbar, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("설정…", null, (_, _) => openSettings());
        menu.Items.Add("작업 표시줄 복구", null, (_, _) => restoreTaskbar());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = TryLoadAppIcon() ?? SystemIcons.Application,
            Text = "RingLauncher",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    static Icon? TryLoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { return null; }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
