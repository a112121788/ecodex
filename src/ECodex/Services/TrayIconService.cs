using System.IO;

namespace ECodex.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Action _restoreWindow;
    private readonly System.Drawing.Icon _icon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private bool _disposed;
    private bool _exitRequested;

    public TrayIconService(Action restoreWindow)
    {
        _restoreWindow = restoreWindow ?? throw new ArgumentNullException(nameof(restoreWindow));
        _icon = LoadIcon();

        _menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("打开 ECodex");
        openItem.Click += (_, _) => _restoreWindow();
        _menu.Items.Add(openItem);
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitAndPreserveItem = new System.Windows.Forms.ToolStripMenuItem("退出并保留终端");
        exitAndPreserveItem.Click += (_, _) => ExitAndPreserve();
        _menu.Items.Add(exitAndPreserveItem);

        var exitAndTerminateItem = new System.Windows.Forms.ToolStripMenuItem("退出并终止终端");
        exitAndTerminateItem.Click += async (_, _) => await ExitAndTerminate();
        _menu.Items.Add(exitAndTerminateItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = "ECodex",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _restoreWindow();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private void ExitAndPreserve()
    {
        if (!TryBeginExit())
            return;

        App.RequestShutdownPreservingDaemonSessions(0);
    }

    private async Task ExitAndTerminate()
    {
        if (!TryBeginExit())
            return;

        await App.RequestShutdownAfterTerminatingDaemonSessionsAsync(0);
    }

    private bool TryBeginExit()
    {
        if (_exitRequested)
            return false;

        _exitRequested = true;
        _menu.Enabled = false;
        return true;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath!);
            if (associatedIcon != null)
                return associatedIcon;
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }
}
