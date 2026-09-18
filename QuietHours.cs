using System;
using System.Runtime.InteropServices;
using NotifyIsland.Core;

namespace NotifyIsland;

internal static class QuietHours
{
    public const int NotPresent = 1;
    public const int Busy = 2;
    public const int D3dFullScreen = 3;
    public const int Presentation = 4;
    public const int Accepts = 5;
    public const int QuietTime = 6;
    public const int App = 7;

    public static int State { get; private set; } = Accepts;
    public static string Label => State switch
    {
        Busy => "Busy / Focus Assist",
        D3dFullScreen => "Fullscreen",
        Presentation => "Presentation",
        QuietTime => "Quiet hours",
        Accepts => "Accepts notifications",
        App => "App",
        _ => "Unknown " + State
    };

    public static bool ShouldSuppressNotify()
    {
        try
        {
            if (SHQueryUserNotificationState(out var s) == 0)
                State = s;
        }
        catch (Exception ex)
        {
            IslandLog.Write("quiet", ex.Message);
        }
        var p = PrefsStore.Current;
        if (p.SuppressFullscreen && State is D3dFullScreen or Presentation)
            return true;
        if (p.SuppressFocusAssist && State is Busy or QuietTime)
            return true;
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);
}
