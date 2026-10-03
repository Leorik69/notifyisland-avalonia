using System;
using System.IO;

namespace NotifyIsland;

/// <summary>Append-only log under %TEMP%/notifyisland.log. Never throws to callers.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "notifyisland.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    /// <summary>Errors and the process-dying paths, so a crash always leaves a trace.</summary>
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
            if (ex is not null)
                line += $" | {ex.GetType().Name}: {ex.Message}";
            // A fatal line with only the exception type and message is close to useless: a
            // NullReferenceException in a 4000-line constructor tells you nothing about which
            // call site, and re-deriving it means re-running under a debugger. So Error lines
            // carry the top frames of the stack and any inner exception too. Bounded, because a
            // deep recursion stack is not a useful thing to write to a one-line-per-event log.
            if (ex is not null && level == "ERROR")
            {
                const int MaxFrames = 12;
                var frames = 0;
                for (var e = ex; e is not null && frames < MaxFrames; e = e.InnerException)
                {
                    if (e.StackTrace is not null)
                        foreach (var frame in e.StackTrace.Split('\n'))
                        {
                            line += Environment.NewLine + "    " + frame.Trim();
                            if (++frames >= MaxFrames) break;
                        }
                    if (e.InnerException is not null)
                        line += Environment.NewLine + $"  -- inner: {e.InnerException.GetType().Name}: {e.InnerException.Message}";
                }
            }
            lock (Gate)
                File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the overlay.
        }
    }
}
