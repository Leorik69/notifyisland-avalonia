namespace NotifyIsland;

using System;

/// <summary>
/// Whether a size change should be allowed to start a new morph (1.13.1).
///
/// <para>
/// The overlay's 200 ms tick reaches <c>ApplySize</c> twice for a single hover change: once
/// through <c>TickHoverPin → ApplyHoverExpandedState</c>, which calls <c>ApplySize</c> itself, and
/// again through the tick's own <c>if (hoverChanged) ApplySize()</c>. The second call re-entered
/// <c>StartMorph</c> a millisecond later and re-ran <c>PrepareMorphVisualStart</c> plus
/// <c>_morphWatch.Restart()</c> over a morph that had not ticked yet, so the track restarted from
/// the pre-morph width and the blob's phase was reseeded. The runtime log showed it plainly —
/// every hover morph logged twice, 1 ms apart:
/// <c>Morph 240×30 → 300×68</c> followed immediately by the same line again.
/// </para>
///
/// <para>
/// This lives in Core as a pure comparison so the rule is testable without a window. The
/// tolerance mirrors the <c>same</c> test in <c>ApplySize</c>: a quarter of a pixel of drift in a
/// layout-driven value must not read as a new target, or the guard would fire on its own frame
/// noise and the real restart would slip through.
/// </para>
/// </summary>
public static class MorphRestart
{
    /// <summary>Sub-pixel tolerance, in DIP. Matches the existing "same size" comparison.</summary>
    public const double Epsilon = 0.5;

    /// <summary>
    /// True when a morph is already in flight to this exact target and must not be restarted.
    /// A morph that is not running, or one heading somewhere else, is not blocked — retargeting
    /// mid-flight is legitimate and is what a kind change during a morph should do.
    /// </summary>
    public static bool WouldRestartSameTarget(
        bool morphActive,
        double toW, double toH, double winToW, double winToH,
        double newW, double newH, double newWinToW, double newWinToH) =>
        morphActive
        && Math.Abs(toW - newW) < Epsilon
        && Math.Abs(toH - newH) < Epsilon
        && Math.Abs(winToW - newWinToW) < Epsilon
        && Math.Abs(winToH - newWinToH) < Epsilon;
}
