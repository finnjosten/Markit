using System.IO;
using System.Windows;

namespace Markit;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, "AppDomain.UnhandledException");

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
            Text = "Markit"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Toggle draw mode", null, (_, _) => _mainWindow?.ToggleDrawSession());
        menu.Items.Add("Settings...", null, (_, _) => _mainWindow?.OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "DispatcherUnhandledException");
        e.Handled = true; // keep the app alive so this is recoverable instead of a silent crash
    }

    private static void LogCrash(Exception? ex, string source)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Markit");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:u}] {source}\n{ex}\n\n");
        }
        catch
        {
            // If we can't even log the crash, there's nothing more we can do here.
        }
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
