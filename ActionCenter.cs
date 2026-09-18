using System;
using System.Diagnostics;

namespace NotifyIsland;

internal static class ActionCenter
{
    public static bool TryOpen()
    {
        foreach (var target in new[] { "ms-actioncenter:", "ms-actioncenter://", "explorer.exe" })
        {
            try
            {
                if (target == "explorer.exe")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "ms-actioncenter:",
                        UseShellExecute = true
                    });
                    return true;
                }
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                return true;
            }
            catch
            {
            }
        }
        return false;
    }
}
