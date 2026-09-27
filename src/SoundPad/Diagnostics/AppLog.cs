using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace SoundPad.Diagnostics;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string Session = Guid.NewGuid().ToString("N");
    public static string DirectoryPath { get; private set; } = "";
    public static string? LastWriteError { get; private set; }
    public static void Initialize()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SoundPad.sln"))) root = root.Parent;
        DirectoryPath = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "logs");
        Write("Information", "Logging.Initialized");
        if (LastWriteError != null)
        {
            var failure = LastWriteError;
            DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundPad", "logs");
            Write("Warning", "Logging.Fallback", new { failure });
        }
    }
    // For low-volume application events only; never call from an audio callback.
    public static void Write(string level, string action, object? details = null, Exception? exception = null)
    {
        lock (Gate)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var line = JsonSerializer.Serialize(new { timestamp = now, level, action, session = Session,
                    processId = Environment.ProcessId, threadId = Environment.CurrentManagedThreadId,
                    details, exception = exception?.ToString() });
                Directory.CreateDirectory(DirectoryPath);
                var stem = Path.Combine(DirectoryPath, $"soundpad-{now:yyyy-MM-dd}-{Session[..8]}");
                var path = stem + ".jsonl";
                for (var part = 1; File.Exists(path) && new FileInfo(path).Length >= 10 * 1024 * 1024; part++)
                    path = $"{stem}-{part}.jsonl";
                File.AppendAllText(path, line + Environment.NewLine);
                LastWriteError = null;
            }
            catch (Exception error) { LastWriteError = error.Message; Debug.WriteLine(error); }
        }
    }
    public static IDisposable Measure(string action) => new Measurement(action);
    private sealed class Measurement(string action) : IDisposable
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Write("Information", action, new { elapsedMs = timer.Elapsed.TotalMilliseconds });
        }
    }
}
