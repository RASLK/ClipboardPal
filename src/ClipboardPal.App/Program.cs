using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;

namespace ClipboardPal;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, lifetime =>
                {
                    lifetime.Startup += (_, _) =>
                    {
                        Dispatcher.UIThread.UnhandledException += (_, ue) =>
                        {
                            LogFatal("UIUnhandledException", ue.Exception);
                            ue.Handled = true;
                        };
                    };
                });
        }
        catch (Exception ex)
        {
            LogFatal("Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClipboardPal");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            Debug.WriteLine(ex);
        }
        catch
        {
            // ignore logging failures
        }
    }
}
