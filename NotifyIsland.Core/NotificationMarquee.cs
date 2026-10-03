using System;

namespace NotifyIsland;

/// <summary>
/// Presentation rules for the two things a notification adds on top of the plain row: the bell
/// badge on the leading edge, and the scrolling body.
/// <para>
/// Both live here, as pure functions, because both are geometry decisions that are easy to get
/// subtly wrong in a 5 000-line code-behind and impossible to test there: whether a string is
/// wider than its column depends on the FONT, so the code-behind has to read it back off the
/// control after layout. All that is left to do here is to be told the two numbers and answer a
/// question.
/// </para>
/// <para>
/// The bell badge is deliberately not in here — an icon and a border are view concerns. What IS
/// here is its pulse, because the pulse is a function of elapsed time and reduced motion, which is
/// exactly the kind of thing that silently keeps animating when the user asked for reduced motion.
/// </para>
/// </summary>
public static class NotificationMarquee
{
    /// <summary>
    /// Share of the body column the text must fill before scrolling is worth starting.
    /// <para>
    /// Without a dead zone, a body one pixel wider than its column would crawl for four seconds
    /// and read as a glitch rather than as a message. A tenth of the column is wide enough to
    /// make "it really does not fit" unambiguous and narrow enough that an ordinary long body
    /// still scrolls instead of being cut at the last character.
    /// </para>
    /// </summary>
    public const double ScrollThresholdShare = 1.10;

    /// <summary>
    /// Should the body scroll in its column right now?
    /// <para>
    /// The column width is the star column's share of the text row, i.e. what is LEFT after the
    /// title. A body that fits, or fits within <see cref="ScrollThresholdShare"/>, is left to the
    /// ordinary ellipsis — scrolling is only for text the user would otherwise not see at all.
    /// </para>
    /// </summary>
    public static bool ShouldScroll(double bodyWidth, double columnWidth, bool enabled)
    {
        if (!enabled) return false;
        if (columnWidth <= 0 || bodyWidth <= 0) return false;
        return bodyWidth > columnWidth * ScrollThresholdShare;
    }

    /// <summary>
    /// Horizontal offset of the body text inside its column. Delegates to
    /// <see cref="MarqueeTrack.OffsetFor"/> so the scrolling motion is literally the same one the
    /// monitor's running caption uses — same speed, same pause at each end, same triangle wave.
    /// Two scroll implementations drifting apart is the failure mode this indirection prevents.
    /// </summary>
    public static double OffsetFor(double elapsedMs, double bodyWidth, double columnWidth) =>
        MarqueeTrack.OffsetFor(elapsedMs, bodyWidth, columnWidth);

    // -- Bell badge pulse -------------------------------------------------------------------
    // A notification that arrives silently is a notification the user did not receive. The badge
    // breathes while the notification is on the capsule and is completely still otherwise — the
    // same discipline the accent flash follows (one run, with a start and an end, not a lamp).

    /// <summary>One full breathe of the bell badge, at Normal speed (ms).</summary>
    public const int BellPulsePeriodMs = 1400;

    /// <summary>Smallest scale the badge's ring drops to at the bottom of the breathe.</summary>
    public const double BellRingMinScale = 0.86;

    /// <summary>
    /// Radius multiplier of the ring at <paramref name="elapsedMs"/>, as a triangle wave between
    /// <see cref="BellRingMinScale"/> and 1.0.
    /// <para>
    /// Triangle, not sine: a sine spends most of its time near the extremes, which reads as two
    /// distinct sizes with a pause at each. A triangle spends the time travelling, which is what
    /// a pulse should look like. Reduced motion is handled by the caller passing
    /// <c>enabled: false</c> and getting a flat 1.0 — the ring then simply is not drawn, and
    /// nothing here needs to know about the setting.
    /// </para>
    /// </summary>
    public static double BellRingScale(double elapsedMs, int periodMs, bool enabled)
    {
        if (!enabled || periodMs <= 0) return 1.0;
        // Keep the phase non-negative for any elapsed time, including a clock that was started
        // before the notification arrived.
        var t = elapsedMs % periodMs;
        if (t < 0) t += periodMs;
        var phase = 2.0 * t / periodMs;                 // 0…2, up on the first half
        var tri = phase <= 1.0 ? phase : 2.0 - phase;  // 0…1…0
        return BellRingMinScale + (1.0 - BellRingMinScale) * tri;
    }

    /// <summary>
    /// Whether the badge's ring is drawn at all for this notification kind.
    /// <para>
    /// Only the kinds that mean "something happened and you have not acted on it": a toast and
    /// an error. Weather, battery, media and the clipboard are readings the user went looking for,
    /// and a badge that keeps breathing at a reading they are already looking at is noise.
    /// </para>
    /// </summary>
    public static bool BellVisible(OverlayKind kind) =>
        kind is OverlayKind.Notification or OverlayKind.Error;
}
