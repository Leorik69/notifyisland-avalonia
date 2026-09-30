using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the width-customisation limits (1.14).
///
/// The limits are the deliverable, not an afterthought. A width control with no bounds lets the
/// island grow until it no longer fits on screen, and the placement / window-seating code around
/// it is built for a 30 DIP-high capsule rather than an arbitrary one. These tests exist so that
/// widening the range later is a deliberate, visible act — and so a NaN or a negative from a
/// hand-edited settings.json cannot produce a nonsense width.
/// </summary>
public class IslandWidthTests
{
    [Fact]
    public void Default_IsExactlyTheTokenWidths()
    {
        // Scale 1 must be a no-op, or shipping the setting would silently resize every existing
        // install's island.
        Assert.Equal(OverlayTokens.CollapsedW, IslandWidth.CollapsedLongAxis(1.0, weatherEnabled: false));
        Assert.Equal(OverlayTokens.CollapsedWeatherW, IslandWidth.CollapsedLongAxis(1.0, weatherEnabled: true));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(0.74)]
    [InlineData(99.0)]
    [InlineData(double.NaN)]
    public void OutOfRangeAndNonsense_ClampToTheSupportedBand(double scale)
    {
        var clamped = IslandWidth.ClampScale(scale);
        Assert.InRange(clamped, IslandWidth.MinScale, IslandWidth.MaxScale);
    }

    [Fact]
    public void BoundariesThemselvesAreAllowed()
    {
        Assert.Equal(IslandWidth.MinScale, IslandWidth.ClampScale(IslandWidth.MinScale));
        Assert.Equal(IslandWidth.MaxScale, IslandWidth.ClampScale(IslandWidth.MaxScale));
    }

    [Fact]
    public void WeatherCapsuleStaysWiderThanThePlainOne_AtEveryScale()
    {
        // The weather chip has to keep its extra room; scaling must not be applied to the
        // difference between the two widths by accident.
        for (var s = IslandWidth.MinScale; s <= IslandWidth.MaxScale; s += 0.05)
            Assert.True(IslandWidth.CollapsedLongAxis(s, true) > IslandWidth.CollapsedLongAxis(s, false));
    }

    [Fact]
    public void LongerNeverShorter()
    {
        var prev = 0.0;
        for (var s = IslandWidth.MinScale; s <= IslandWidth.MaxScale + 0.001; s += 0.05)
        {
            var w = IslandWidth.CollapsedLongAxis(s, weatherEnabled: false);
            Assert.True(w >= prev, $"width went backwards at scale {s:0.00}");
            prev = w;
        }
    }

    [Fact]
    public void TheRangeIsTheOneTheDocsClaim()
    {
        // The upper bound is set by a 13" laptop: 170 x 1.6 = 272 DIP of mostly-empty capsule.
        Assert.Equal(272.0, IslandWidth.CollapsedLongAxis(IslandWidth.MaxScale, weatherEnabled: false), 6);
        // The lower bound is set by content fitting, not by taste.
        Assert.Equal(127.5, IslandWidth.CollapsedLongAxis(IslandWidth.MinScale, weatherEnabled: false), 6);
    }

    [Fact]
    public void SnapToStep_ProducesRoundNumbers()
    {
        Assert.Equal(1.25, IslandWidth.SnapToStep(1.24), 6);
        Assert.Equal(1.25, IslandWidth.SnapToStep(1.26), 6);
        Assert.Equal(1.00, IslandWidth.SnapToStep(1.01), 6);
    }

    [Fact]
    public void SnapToStep_StillClampsFirst()
    {
        // A step-snapped value must never escape the band, or a slider at the end could produce
        // a scale the clamp would then have to undo.
        Assert.Equal(IslandWidth.MinScale, IslandWidth.SnapToStep(-5.0), 6);
        Assert.Equal(IslandWidth.MaxScale, IslandWidth.SnapToStep(50.0), 6);
    }

    [Fact]
    public void Describe_ShowsAConcreteDipValue()
    {
        Assert.Equal("170 DIP", IslandWidth.Describe(1.0, weatherEnabled: false));
        Assert.Equal("240 DIP", IslandWidth.Describe(1.0, weatherEnabled: true));
        Assert.Equal("272 DIP", IslandWidth.Describe(IslandWidth.MaxScale, weatherEnabled: false));
    }
}
