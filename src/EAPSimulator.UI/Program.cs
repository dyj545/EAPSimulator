using System.IO;
using Avalonia;

namespace EAPSimulator.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        InstallGlobalExceptionHooks();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Last-resort logging for crashes that don't go through Serilog — fire-and-forget
    /// failures (HSMS control replies, scenario engine background tasks) and unhandled
    /// AppDomain exceptions land here so they show up in logs/crash.log instead of vanishing.
    /// </summary>
    private static void InstallGlobalExceptionHooks()
    {
        var crashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        try { Directory.CreateDirectory(crashDir); } catch { /* best-effort */ }
        var crashLog = Path.Combine(crashDir, "crash.log");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            TryAppend(crashLog, $"{DateTime.Now:O} UNHANDLED IsTerminating={e.IsTerminating}: {e.ExceptionObject}\n\n");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            TryAppend(crashLog, $"{DateTime.Now:O} UNOBSERVED: {e.Exception}\n\n");
        };
    }

    private static void TryAppend(string path, string text)
    {
        try { File.AppendAllText(path, text); } catch { /* swallow — we're already on the error path */ }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
