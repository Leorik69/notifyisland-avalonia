using Avalonia.Animation.Easings;

namespace NotifyIsland;

internal static class Motion
{
    public const int FadeMs = 83;
    public const int FastMs = 167;
    public const int NormalMs = 250;
    public const int BreatheMs = 960;

    public static int ProgressMs => NormalMs;
    public static int InvokeMs => FastMs;
    public static int DismissMs => FastMs;
    public static int PulseMs => FastMs;

    public static int WidthMorphMs(double deltaPx)
    {
        if (PrefsStore.Current.AnimSpeed == "fast") return FastMs;
        return Math.Abs(deltaPx) < 80 ? FastMs : NormalMs;
    }

    public static int MorphMs => WidthMorphMs(80);

    public static Easing FastIn { get; } = new SplineEasing(0, 0, 0, 1);
    public static Easing PointToPoint { get; } = new SplineEasing(0.55, 0.55, 0, 1);
    public static Easing SoftOut { get; } = new SplineEasing(1, 0, 1, 1);
    public static Easing Linear { get; } = new LinearEasing();
}
