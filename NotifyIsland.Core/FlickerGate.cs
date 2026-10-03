namespace NotifyIsland;

/// <summary>
/// Reduced-motion policy for the two per-second *flickers* in the capsule.
///
/// Track B of docs/superpowers/plans/2026-09-30--animation-audit.md called for both the
/// blinking colon and the seconds dot strip to freeze under reduced motion. On inspection those
/// two are not the same kind of thing, and the difference is worth stating rather than papering
/// over:
///
/// <list type="bullet">
/// <item>The colon blink is decoration. It carries no information — the digits beside it already
/// say the time — so dropping it costs the user nothing and removes a 1 Hz flicker from the
/// centre of their field of view. Under reduced motion the colon is pinned lit.</item>
///
/// <item>The seconds strip is information. Its lit count IS the elapsed-seconds readout; freezing
/// it would show a stale count for the rest of the minute, which is worse than the flicker it
/// removes. It also never moves: the dots stay put and only their opacity steps, on a
/// 5-pixel-tall element. Reduced motion targets movement and large-area flashing, so the strip
/// keeps running.</item>
/// </list>
///
/// Kept in Core, next to <see cref="SecondsStripLogic"/>, so the policy is testable without an
/// Avalonia window and cannot drift between the two call sites.
/// </summary>
public static class FlickerGate
{
    /// <summary>Colon opacity when lit.</summary>
    public const double ColonLitOpacity = 1.0;

    /// <summary>Colon opacity on the off half of the blink. Matches the historical value so the
    /// non-reduced look is unchanged.</summary>
    public const double ColonDimOpacity = 0.28;

    /// <summary>
    /// Whether the blinking colon should be lit right now.
    ///
    /// <paramref name="reducedMotion"/> wins over the clock: under reduced motion the colon is
    /// always lit, which is the same one state the eye would settle on anyway. Without the gate
    /// the blink continues at 1 Hz, and a 1 Hz change in the middle of a 30 DIP capsule is exactly
    /// the kind of persistent flicker reduced motion is asking us to stop.
    /// </summary>
    public static bool ColonLit(bool reducedMotion, DateTime now) =>
        reducedMotion || (now.Second % 2) == 0;

    /// <summary>Colon opacity for a given lit state.</summary>
    public static double ColonOpacity(bool lit) => lit ? ColonLitOpacity : ColonDimOpacity;

    /// <summary>
    /// Whether the seconds dot strip may keep updating.
    ///
    /// Returns <c>true</c> unconditionally today, and exists as a named decision rather than an
    /// inline <c>if</c> so that a future change has one obvious place to land — and so the
    /// reasoning above travels with the code instead of dissolving into a git blame.
    /// </summary>
    public static bool SecondsStripUpdates(bool reducedMotion) => true;
}
