using System;

namespace NotifyIsland;

/// <summary>
/// User control over how wide the collapsed island is (1.14).
///
/// <para>
/// The capsule has been a fixed <see cref="OverlayTokens.CollapsedW"/> since long before the
/// customisation existed, which is a reasonable default and a poor answer for everyone else: 170
/// DIP is cramped on a 27&quot; monitor with a large font set, and wasteful on a 13&quot; laptop where
/// the capsule swallows a chunk of the desktop. This is a scale rather than an absolute width on
/// purpose — it survives the base width changing for any other reason (weather chip, seconds,
/// clipboard section) instead of going stale.
/// </para>
///
/// <para>
/// The limits are the whole point of the file. A width setting with no bounds is a way for the
/// island to grow until it no longer fits on screen, and the window sizing around it (the working
/// area clamp, the placement on the short axis) is built for a 30 DIP-high capsule, not an
/// arbitrary one. So the range is deliberately narrow, and both ends are argued rather than
/// guessed:
/// </para>
///
/// <list type="bullet">
/// <item><b>Lower bound 0.75</b> — below this the digital clock, the date and the weather chip
/// stop fitting inside the capsule and the layout starts trimming content, which is a worse
/// answer than a slightly tight capsule.</item>
/// <item><b>Upper bound 1.60</b> — past this the capsule is mostly empty fill, and on a laptop
/// screen the island is wide enough to cover a meaningful part of the desktop. The 13&quot; case is
/// the binding one, not the aesthetic one.</item>
/// </list>
///
/// <para>
/// Only the <b>collapsed</b> island is scaled. <c>SystemStats</c> is a fixed block sized by its
/// own row layout, and the notification kinds size themselves from their content — scaling those
/// would decouple the capsule from the rows inside it.
/// </para>
/// </summary>
public static class IslandWidth
{
    /// <summary>Neutral: exactly the token widths.</summary>
    public const double DefaultScale = 1.0;

    /// <summary>Below this the content stops fitting and starts trimming. See the class notes.</summary>
    public const double MinScale = 0.75;

    /// <summary>Above this the capsule is mostly empty and crowds a laptop screen.</summary>
    public const double MaxScale = 1.60;

    /// <summary>Step used by the settings slider.</summary>
    public const double Step = 0.05;

    /// <summary>Clamp a user-supplied scale into the supported range.</summary>
    public static double ClampScale(double scale) =>
        double.IsNaN(scale) ? DefaultScale : Math.Clamp(scale, MinScale, MaxScale);

    /// <summary>Snap a scale to the slider's step, so the number in the UI is a round one.</summary>
    public static double SnapToStep(double scale) =>
        Math.Round(ClampScale(scale) / Step) * Step;

    /// <summary>
    /// The collapsed capsule's long axis at a given scale, weather chip included.
    /// </summary>
    public static double CollapsedLongAxis(double scale, bool weatherEnabled)
    {
        var s = ClampScale(scale);
        var baseW = weatherEnabled ? OverlayTokens.CollapsedWeatherW : OverlayTokens.CollapsedW;
        return baseW * s;
    }

    /// <summary>
    /// A short human-readable size for the settings UI, e.g. &quot;240 DIP&quot;. Shown next to the
    /// slider so the user sees a number rather than an abstract multiplier.
    /// </summary>
    public static string Describe(double scale, bool weatherEnabled) =>
        $"{Math.Round(CollapsedLongAxis(scale, weatherEnabled)):0} DIP";
}
