using System;
using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Geometry of the 1.14 clipboard SECTION: the compartment of the capsule that shows what was
/// copied and opens the history drawer. The invariant is the user's own description —
/// "the clock stays on the left, the right side shows a preview" — which is a claim about
/// WHERE the growth goes, not about how much of it there is.
///
/// <para>
/// The structural difference from the 1.12.3 peek is that the section is a single ramp that
/// STAYS out. The old two-phase morph ran the capsule out, then pulled it back again; these tests
/// pin the one-phase behaviour, because a section that retracts on its own would defeat the whole
/// reason the clipboard became a compartment rather than a detached ball.
/// </para>
/// </summary>
public class ClipboardSectionTrackTests
{
    private const double IslandLong = 170;

    [Fact]
    public void AtRestTheCapsuleIsExactlyTheIslandsOwnLength()
    {
        // t = 0 must be a clean, settled capsule. If this were not true, "the island does not
        // move" would be true only at some t and not others.
        Assert.Equal(IslandLong, ClipboardSectionTrack.CapsuleLongAt(0, IslandLong), 6);
    }

    [Fact]
    public void TheSectionGrowsOnTheTrailingEndAndStays()
    {
        // ONE ramp that holds. The regression this pins is the 1.12.3 two-phase morph: at t = 1
        // the capsule must be LONGER than the island, not back at the island's own length. A
        // section that retracts when the morph ends is a peek, not a section.
        Assert.Equal(IslandLong + ClipboardSectionTrack.Width,
            ClipboardSectionTrack.CapsuleLongAt(1, IslandLong), 6);
        // And it is monotonic: it never runs back out on the way.
        var previous = 0.0;
        for (var t = 0.0; t <= 1.0001; t += 0.05)
        {
            var length = ClipboardSectionTrack.CapsuleLongAt(t, IslandLong);
            Assert.True(length >= previous - 1e-9, $"the section must not retract (t={t})");
            previous = length;
        }
    }

    [Fact]
    public void GrowthIsMonotonicAndClamped()
    {
        var previous = 0.0;
        for (var t = 0.0; t <= 1.0001; t += 0.05)
        {
            var reveal = ClipboardSectionTrack.RevealAt(t);
            Assert.InRange(reveal, 0.0, 1.0);
            Assert.True(reveal >= previous - 1e-9, $"reveal went backwards at t={t}");
            previous = reveal;
        }
        Assert.Equal(0, ClipboardSectionTrack.RevealAt(-1), 6);
        Assert.Equal(1, ClipboardSectionTrack.RevealAt(2), 6);
    }

    [Fact]
    public void TheIslandIsMeasuredWithoutTheSection()
    {
        // The ⅓/⅓/⅓ clipboard-cycle zones and the section hit test are measured against the
    // ISLAND, so they must not slide outboard while a copy is showing. This is the subtraction
    // that keeps them pinned, and it has to be the exact inverse of the growth above.
        foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var capsule = ClipboardSectionTrack.CapsuleLongAt(t, IslandLong);
            var island = ClipboardSectionTrack.IslandLongAt(t, capsule);
            Assert.Equal(IslandLong, island, 6);
        }
    }

    [Fact]
    public void TheIslandNeverGoesNegative()
    {
        // A capsule smaller than the section must not produce a negative island length -- that
        // would feed a negative zone extent into the click zones.
        Assert.Equal(0, ClipboardSectionTrack.IslandLongAt(1, 10), 6);
        Assert.Equal(0, ClipboardSectionTrack.IslandLongAt(1, 0), 6);
    }

    [Fact]
    public void TheContentInkTrailsTheWidthSoItNeverSpillsOverTheCap()
    {
        // The text fades in AFTER the capsule has made room for it. Opacity leading the width is
        // how a preview ends up half-outside the growing rounded cap.
        foreach (var t in new[] { 0.0, 0.05, 0.1, 0.15 })
            Assert.True(ClipboardSectionTrack.OpacityAt(t) <= 1e-9,
                $"ink must not appear at t={t}, before the fade delay");
        // Once it is up, the capsule is fully out.
        Assert.Equal(1, ClipboardSectionTrack.OpacityAt(1), 6);
    }

    [Fact]
    public void HitTestAgreesWithTheRenderedGeometry()
    {
        // The section is the part of the long axis the island does NOT cover. A hit test that
        // disagreed with the width by even a DIP would make part of the section unclickable.
        foreach (var t in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            foreach (var vertical in new[] { false, true })
            {
                var capsule = ClipboardSectionTrack.CapsuleLongAt(t, IslandLong);
                var island = ClipboardSectionTrack.IslandLongAt(t, capsule);

                Assert.False(ClipboardSectionTrack.IsInSection(vertical, island - 1, capsule, island),
                    "a point inside the island is not the section");
                Assert.True(ClipboardSectionTrack.IsInSection(vertical, island, capsule, island),
                    "the seam itself belongs to the section");
                Assert.True(ClipboardSectionTrack.IsInSection(vertical, capsule - 0.001, capsule, island),
                    "everything past the island is the section");
            }
        }
    }

    [Fact]
    public void AtZeroRevealTheSectionIsNotHitTestable()
    {
        // Before the capsule has made any room there is no section to click. The island's own
        // click zones own the whole long axis, which is what stops a click on the clock from
        // opening the drawer.
        var capsule = ClipboardSectionTrack.CapsuleLongAt(0, IslandLong);
        var island = ClipboardSectionTrack.IslandLongAt(0, capsule);
        Assert.Equal(IslandLong, capsule);
        Assert.False(ClipboardSectionTrack.IsInSection(false, capsule - 0.001, capsule, island));
    }

    [Fact]
    public void HitTestOnBothOrientationsUsesTheSameNumber()
    {
        // A vertical island grows DOWN and a horizontal one grows RIGHT; the long axis is simply
        // read off the other dimension. If the two branches disagreed, the section would be
        // clickable in a different place from where it is drawn.
        var capsule = ClipboardSectionTrack.CapsuleLongAt(0.5, IslandLong);
        var island = ClipboardSectionTrack.IslandLongAt(0.5, capsule);

        Assert.Equal(ClipboardSectionTrack.IsInSection(false, island, capsule, island),
            ClipboardSectionTrack.IsInSection(true, island, capsule, island));
    }

    [Fact]
    public void TheSectionStopsShortOfTheRoundedCap()
    {
        // The content is inset by half the capsule's cross extent so the text stops before the
        // corner curve rather than running over it.
        Assert.True(ClipboardSectionTrack.CapInsetFor(30) > 0);
        Assert.Equal(15, ClipboardSectionTrack.CapInsetFor(30), 6);
        // A wider capsule needs a wider inset -- proportional, not a fixed 15.
        Assert.True(ClipboardSectionTrack.CapInsetFor(40) > ClipboardSectionTrack.CapInsetFor(30));
    }

    [Fact]
    public void RestingIsFullyRevealedAndFullyOpaque()
    {
        Assert.Equal(1.0, ClipboardSectionTrack.Resting.Reveal);
        Assert.Equal(1.0, ClipboardSectionTrack.Resting.Opacity);
    }
}
