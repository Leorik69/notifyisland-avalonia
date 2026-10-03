namespace NotifyIsland;

using System;

/// <summary>
/// The first-appear wobble: a small horizontal settle when the capsule comes back after being
/// hidden (fullscreen exit, tray toggle). The spec asks for ±<see cref="OverlayTokens.FirstAppearWobblePx"/>
/// DIP over <see cref="OverlayTokens.FirstAppearWobbleMs"/>, on the same rhythm as the click pop.
///
/// In Core as a pure function of elapsed time, so the shape is testable without a window and the
/// overlay only decides WHEN to play it. It rides the shared 16 ms frame tick like every other
/// one-shot — no timer of its own.
/// </summary>
public static class FirstAppearWobble
{
    /// <summary>
    /// Horizontal offset (DIP) at <paramref name="elapsedMs"/> into a wobble of
    /// <paramref name="durationMs"/>.
    ///
    /// A damped sine: it starts at 0, swings out to the amplitude, and settles back to 0. The
    /// envelope is <c>(1 - t)^2</c> so the tail is quiet — a wobble that ends abruptly reads as a
    /// glitch, not a settle. Returns exactly 0 outside [0, duration] so the caller can leave the
    /// translate alone when the wobble is over.
    /// </summary>
    public static double OffsetX(double elapsedMs, int durationMs, double amplitude)
    {
        if (durationMs <= 0 || amplitude <= 0) return 0;
        if (elapsedMs <= 0) return 0;
        if (elapsedMs >= durationMs) return 0;

        var t = elapsedMs / durationMs;
        var envelope = (1.0 - t) * (1.0 - t);
        // One full sine period across the duration: out and back once, no visible ringing.
        return amplitude * envelope * Math.Sin(t * 2.0 * Math.PI);
    }
}
