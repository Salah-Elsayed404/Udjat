using Udjat.Windows;
using Udjat.Windows.Storage;

namespace Udjat;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = StartOptions.Parse(args);

        SingleInstance? instance;
        try
        {
            instance = SingleInstance.TryBecomeFirst();
        }
        catch (InvalidOperationException ex)
        {
            return options.Exit ? 1 : Fail(ex.Message);  // never a message box for --exit: the installer waits for it
        }

        using (instance)
        {
            if (instance is null)
            {
                // Another Udjat is running: ask it to close (--exit) or to show itself, and stop here.
                SingleInstance.Signal(options.Exit ? SingleInstance.ExitEventName() : SingleInstance.ShowEventName());
                return 0;
            }

            if (options.Exit)
                return 0;  // nothing was running

            return TryPrepareDataFolder(options) ? Run(options, instance) : 1;
        }
    }

    private static int Run(StartOptions options, SingleInstance instance)
    {
        // No App.xaml: the tray icon is the app, and windows are opened from code when needed.
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        using var host = new AppHost(app, options, instance);
        host.Start();
        return app.Run();
    }

    /// <summary>Makes sure the settings and log folder is safe for an administrator to write to. If it is not, says so and does not start.</summary>
    private static bool TryPrepareDataFolder(StartOptions options)
    {
        if (options.DataDir is not null)
            return true;  // chosen by hand, for testing

        try
        {
            DataFolder.Prepare(DataFolder.DefaultPath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Fail(ex.Message);
            return false;
        }
    }

    private static int Fail(string message)
    {
        System.Windows.MessageBox.Show(message, "Udjat cannot start", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        return 1;
    }
}
