using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the first-appear wobble shape. The tokens and the AppSettings key shipped in 1.12.0 with
/// no implementation behind them; the audit flagged the pair as dead and this implements what the
/// spec (2026-09-28--notifyisland-system-stats-polish.md §FirstAppearWobble) actually asked for:
/// "translate X oscillates within FirstAppearWobblePx over FirstAppearWobbleMs".
/// </summary>
public class FirstAppearWobbleTests
{
    private const int Ms = OverlayTokens.FirstAppearWobbleMs;
    private const double Px = OverlayTokens.FirstAppearWobblePx;

    [Fact]
    public void Tokens_AreTheOnesTheSpecPromised()
    {
        // Same rhythm as the click pop, which is what the spec asked for.
        Assert.Equal(OverlayTokens.ClickPopMs, Ms);
        Assert.Equal(1.0, Px);
    }

    [Fact]
    public void OutsideTheWindow_ItIsExactlyZero()
    {
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(-1, Ms, Px));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(0, Ms, Px));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(Ms, Ms, Px));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(Ms * 3, Ms, Px));
    }

    [Fact]
    public void DegenerateInputs_DoNotDivideByZero()
    {
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(10, 0, Px));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(10, Ms, 0));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(10, -5, Px));
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(10, Ms, -1));
    }

    [Fact]
    public void ItStartsAtRest()
    {
        // The capsule must not jump on the frame it appears. t=0 is exactly 0 and the curve rises
        // from there, so the first painted frame matches where it was hidden.
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(0, Ms, Px));
    }

    [Fact]
    public void ItSettlesBackToRest()
    {
        Assert.Equal(0.0, FirstAppearWobble.OffsetX(Ms, Ms, Px));
    }

    [Fact]
    public void ItActuallyMoves()
    {
        // Guard against a curve that "works" but is flat — the whole point is a visible wobble.
        var peak = PeakMagnitude();
        Assert.True(peak > Px * 0.5, $"expected a visible wobble, peak was {peak:0.###} DIP");
    }

    [Fact]
    public void ItStaysWithinTheAmplitude()
    {
        // Amplitude is the ceiling. The damped envelope means the sine peak is scaled below 1, so
        // the curve can never exceed it — but pin it anyway, since a future envelope change that
        // overshoots would read as a glitch.
        for (var ms = 0; ms <= Ms; ms += 1)
            Assert.True(Math.Abs(FirstAppearWobble.OffsetX(ms, Ms, Px)) <= Px);
    }

    [Fact]
    public void ItChangesSign_OutAndBack()
    {
        // A wobble that never crosses zero is a lean, not a wobble.
        var signs = new List<double>();
        for (var ms = 0; ms <= Ms; ms += 2)
            signs.Add(Math.Sign(FirstAppearWobble.OffsetX(ms, Ms, Px)));

        Assert.Contains(1.0, signs);
        Assert.Contains(-1.0, signs);
    }

    [Fact]
    public void TheTailIsQuieterThanTheHead()
    {
        // The (1-t)^2 envelope is the point: a wobble that ends abruptly reads as a glitch rather
        // than a settle. Compare the envelope near the start against the same phase near the end.
        var early = Math.Abs(FirstAppearWobble.OffsetX(Ms * 0.05, Ms, Px));
        var late = Math.Abs(FirstAppearWobble.OffsetX(Ms * 0.95, Ms, Px));
        Assert.True(late < early, $"tail {late:0.###} should be quieter than head {early:0.###}");
    }

    [Fact]
    public void ItIsContinuousAcrossTheBoundary()
    {
        // Just before the end the curve is already ~0, so the hand-off back to "leave the
        // translate alone" cannot show a step.
        Assert.True(Math.Abs(FirstAppearWobble.OffsetX(Ms - 1, Ms, Px)) < Px * 0.05);
    }

    [Fact]
    public void AmplitudeScalesLinearly()
    {
        var a = FirstAppearWobble.OffsetX(Ms * 0.25, Ms, Px);
        var b = FirstAppearWobble.OffsetX(Ms * 0.25, Ms, Px * 2);
        Assert.Equal(a * 2.0, b, 10);
    }

    private static double PeakMagnitude()
    {
        var peak = 0.0;
        for (var ms = 0; ms <= Ms; ms += 1)
            peak = Math.Max(peak, Math.Abs(FirstAppearWobble.OffsetX(ms, Ms, Px)));
        return peak;
    }
}
