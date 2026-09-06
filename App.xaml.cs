using System.Windows;

namespace ZoomItToolbar;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        // Show() is required once so the HWND is created and styled, but
        // MainWindow immediately hides itself in OnSourceInitialized.
        _mainWindow.Show();

        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        // Reuse the exe's own embedded icon (set via <ApplicationIcon> in the
        // .csproj) rather than shipping/loading a second copy of the asset.
        var appIcon = Environment.ProcessPath is { } exePath
            ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
            : null;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "ZoomIt Toolbar"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Toggle draw mode", null, (_, _) => _mainWindow?.ToggleDrawSession());
        menu.Items.Add("Settings...", null, (_, _) => _mainWindow?.OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.FlushSettings();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}
