using System.Windows;

namespace DynamicIsland;

public partial class App : Application
{
    // Held for the app's lifetime so a second launch can see the island is already running.
    Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--uninstall"))
        {
            Installer.Uninstall();
            MessageBox.Show("Dynamic Island kaldırıldı.", "Dynamic Island", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // A downloaded exe installs itself and hands over to the installed copy.
        if (Installer.InstallIfNeeded())
        {
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, @"Local\DynamicIsland.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
