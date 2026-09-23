using System;
using System.IO;

namespace NotifyIsland;

/// <summary>Append-only log under %TEMP%/notifyisland.log. Never throws to callers.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "notifyisland.log");

    public static void Warn(string message, Exception? ex = null)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} WARN {message}";
            if (ex is not null)
                line += $" | {ex.GetType().Name}: {ex.Message}";
            lock (Gate)
                File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the overlay.
        }
    }
}
