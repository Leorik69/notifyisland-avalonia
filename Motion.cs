namespace NotifyIsland;

internal static class Motion
{
    public static int MorphMs => PrefsStore.Current.AnimSpeed == "fast" ? 180 : OverlayTokens.MorphMs;
    public static int FadeMs => PrefsStore.Current.AnimSpeed == "fast" ? 160 : 220;
    public static int PulseMs => PrefsStore.Current.AnimSpeed == "fast" ? 180 : 240;
    public static int BreatheMs => PrefsStore.Current.AnimSpeed == "fast" ? 720 : 960;
    public static int ProgressMs => PrefsStore.Current.AnimSpeed == "fast" ? 180 : 240;
}
