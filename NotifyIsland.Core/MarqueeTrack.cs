using System;

namespace NotifyIsland;

/// <summary>
/// Pure geometry for the monitor's running caption line (1.13).
/// <para>
/// The line sits at the BOTTOM of the System Stats panel, just above the capsule's progress
/// band, and scrolls its text horizontally when the text is wider than the slot. It exists
/// because the previous arrangement put the clipboard preview inside the clock row, where it
/// collided with the panel and was clipped: the preview was drawn in one place and the panel
/// grew through it. One dedicated line with one budget is the fix.
/// </para>
/// <para>
/// Deliberately not a component: the only real question is "where is the text drawn right
/// now", and that has to be answerable without an Avalonia dispatcher.
/// </para>
/// </summary>
public static class MarqueeTrack
{
    /// <summary>Height of the caption line (DIP). One FontSize 10 line box.</summary>
    public const double LineH = 14.0;

    /// <summary>Horizontal scrolling speed (DIP per second). Slow enough to read.</summary>
    public const double DipPerSecond = 26.0;

    /// <summary>
    /// Pause at each end before the text starts moving again, in ms. Without a pause the
    /// text appears to be mid-sentence on every loop, which makes it hard to read.
    /// </summary>
    public const int EdgePauseMs = 1400;

    /// <summary>
    /// Horizontal offset of the text inside its slot, negative values scroll left.
    /// Returns 0 when the text fits — a line that fits must not creep, or a short track
    /// title would slowly wander across the panel.
    /// <para>
    /// The motion is a triangle wave over [−travel, 0] with a flat pause at each end, so
    /// the speed is constant in the middle and zero at the extremes. Using a raw modulo
    /// would make the text snap back from the left edge, which reads as a glitch.
    /// </para>
    /// </summary>
    public static double OffsetFor(double elapsedMs, double textWidth, double slotWidth)
    {
        var travel = textWidth - slotWidth;
        if (travel <= 0 || slotWidth <= 0) return 0;

        var stepMs = travel / DipPerSecond * 1000.0;
        var periodMs = 2 * (stepMs + EdgePauseMs);
        if (periodMs <= 0) return 0;

        // Loop into [0, period) and keep the phase non-negative for any elapsed time.
        var t = elapsedMs % periodMs;
        if (t < 0) t += periodMs;

        if (t < EdgePauseMs) return 0;                                   // hold at the start
        if (t < EdgePauseMs + stepMs) return -travel * (t - EdgePauseMs) / stepMs;
        if (t < 2 * EdgePauseMs + stepMs) return -travel;               // hold at the far end
        var back = t - (2 * EdgePauseMs + stepMs);
        return -travel * (1 - back / stepMs);
    }

    /// <summary>True when the text is wider than its slot and therefore actually scrolls.</summary>
    public static bool ShouldScroll(double textWidth, double slotWidth) =>
        slotWidth > 0 && textWidth > slotWidth;

    /// <summary>
    /// Measure a string in a fallback metric. Real measurement happens in the Avalonia layer
    /// (the text block reports its own width once laid out); this is the headless-safe
    /// approximation used by tests and by the first frame before layout has run.
    /// <para>
    /// The factor 0.52 is the average advance of Inter/Segoe UI at FontSize 10 for mixed
    /// Cyrillic — measured once, and deliberately a bit over-wide so a long line scrolls
    /// slightly early rather than clipping its last character.
    /// </para>
    /// </summary>
    public static double EstimateWidth(string? text, double fontSize = 10.0)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length * fontSize * 0.52;
    }
}
