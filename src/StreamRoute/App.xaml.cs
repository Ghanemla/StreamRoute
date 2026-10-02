using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace StreamRoute;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamRoute");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}\n\n");
        }
        catch
        {
            // Best effort only.
        }

        MessageBox.Show(
            "StreamRoute hit an unexpected error. A crash log was written to %LocalAppData%\\StreamRoute\\crash.log.",
            "StreamRoute",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
