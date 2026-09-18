using System;
using System.IO;

namespace NotifyIsland.Core;

public static class IslandLog
{
    public const long MaxBytes = 512 * 1024;

    public static string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "notifyisland.log");

    public static void Write(string area, string message)
    {
        try
        {
            Rotate();
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + area + "] " + message + Environment.NewLine;
            File.AppendAllText(Path, line, System.Text.Encoding.UTF8);
        }
        catch
        {
            // last-resort: logging must not throw
        }
    }

    public static void Rotate()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var info = new FileInfo(Path);
            if (info.Length < MaxBytes) return;
            var bak = Path + ".1";
            if (File.Exists(bak)) File.Delete(bak);
            File.Move(Path, bak);
        }
        catch
        {
        }
    }
}
