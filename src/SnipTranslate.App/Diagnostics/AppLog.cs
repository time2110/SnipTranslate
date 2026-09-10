using System.IO;

namespace SnipTranslate.Diagnostics;

internal static class AppLog
{
    private static readonly object Sync = new();

    internal static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "SnipTranslate.log");

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Error(string message, Exception exception) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                var detail = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{detail}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never stop the capture path.
        }
    }
}
