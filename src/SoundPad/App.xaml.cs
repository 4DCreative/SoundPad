using System.Diagnostics;
using System.Windows;
using SoundPad.Diagnostics;
namespace SoundPad;
public partial class App : Application
{
    private readonly Stopwatch startup = Stopwatch.StartNew();
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Initialize();
        DispatcherUnhandledException += (_, a) => AppLog.Write("Fatal", "Application.UIError", exception: a.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => AppLog.Write("Fatal", "Application.UnhandledError", new { a.IsTerminating }, a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => AppLog.Write("Error", "Application.TaskError", exception: a.Exception);
        AppLog.Write("Information", "Application.Start", new { runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), logDirectory = AppLog.DirectoryPath });
        base.OnStartup(e);
    }
    internal void WindowReady() => AppLog.Write("Information", "Application.WindowReady", new { elapsedMs = startup.Elapsed.TotalMilliseconds });
    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("Information", "Application.Exit", new { e.ApplicationExitCode });
        base.OnExit(e);
    }
}
