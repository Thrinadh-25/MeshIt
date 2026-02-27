using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Catches unhandled exceptions and writes crash logs to %APPDATA%\meshIt\crashes\.
/// </summary>
public class CrashReporter
{
    private static readonly string CrashDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "meshIt", "crashes");

    public void Initialize()
    {
        Directory.CreateDirectory(CrashDir);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception, "UnhandledException");

        if (Application.Current is not null)
        {
            Application.Current.DispatcherUnhandledException += (_, e) =>
            {
                LogCrash(e.Exception, "DispatcherUnhandledException");
                e.Handled = true; // Prevent app crash
            };
        }

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };

        Log.Information("CrashReporter initialized");
    }

    private void LogCrash(Exception? ex, string source)
    {
        if (ex is null) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"CLR: {Environment.Version}");
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Stack Trace:");
            sb.AppendLine(ex.StackTrace);

            if (ex.InnerException is not null)
            {
                sb.AppendLine($"\nInner Exception: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"Inner Message: {ex.InnerException.Message}");
                sb.AppendLine(ex.InnerException.StackTrace);
            }

            var logPath = Path.Combine(CrashDir,
                $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N8}.txt");
            File.WriteAllText(logPath, sb.ToString());

            Log.Fatal(ex, "Crash logged to {Path}", logPath);
        }
        catch
        {
            // If crash logging itself fails, we can't do much
        }
    }

    /// <summary>Get all crash log paths.</summary>
    public string[] GetCrashLogs() =>
        Directory.Exists(CrashDir) ? Directory.GetFiles(CrashDir, "*.txt") : Array.Empty<string>();

    /// <summary>Clear old crash logs.</summary>
    public void ClearOldLogs(int keepDays = 30)
    {
        if (!Directory.Exists(CrashDir)) return;

        foreach (var file in Directory.GetFiles(CrashDir, "*.txt"))
        {
            if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-keepDays))
                File.Delete(file);
        }
    }
}
