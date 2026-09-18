using System;
using System.IO;

namespace NotifyIsland.Core;

public static class IslandLog
{
    public static string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "notifyisland.log");

    public static void Write(string area, string message)
    {
        try
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + area + "] " + message + Environment.NewLine;
            File.AppendAllText(Path, line);
        }
        catch
        {
        }
    }
}
