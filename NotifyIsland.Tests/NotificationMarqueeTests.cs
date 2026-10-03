using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class NotificationMarqueeTests
{
    // -- ShouldScroll ---------------------------------------------------------------------

    [Fact]
    public void Text_that_fits_does_not_scroll()
    {
        Assert.False(NotificationMarquee.ShouldScroll(80, 200, enabled: true));
    }

    [Fact]
    public void Text_exactly_as_wide_as_the_column_does_not_scroll()
    {
        // 1.00 is not "too wide" — the text fits, and the ellipsis is not engaged either.
        Assert.False(NotificationMarquee.ShouldScroll(200, 200, enabled: true));
    }

    [Fact]
    public void Text_just_over_the_column_does_not_scroll_yet()
    {
        // The dead zone: 5 % over is not worth a four-second crawl.
        Assert.False(NotificationMarquee.ShouldScroll(205, 200, enabled: true));
    }

    [Fact]
    public void Text_past_the_dead_zone_scrolls()
    {
        Assert.True(NotificationMarquee.ShouldScroll(230, 200, enabled: true));
    }

    [Fact]
    public void Disabled_never_scrolls_however_wide_the_text()
    {
        Assert.False(NotificationMarquee.ShouldScroll(1000, 200, enabled: false));
    }

    [Fact]
    public void Empty_or_unmeasured_widths_never_scroll()
    {
        // 0 is what the control reports before layout has run. Scrolling on a zero width would
        // divide by it.
        Assert.False(NotificationMarquee.ShouldScroll(0, 200, enabled: true));
        Assert.False(NotificationMarquee.ShouldScroll(300, 0, enabled: true));
        Assert.False(NotificationMarquee.ShouldScroll(0, 0, enabled: true));
    }

    [Fact]
    public void Dead_zone_is_exactly_ten_percent()
    {
        // Pin the constant so a change to it is a deliberate one.
        Assert.Equal(1.10, NotificationMarquee.ScrollThresholdShare, 6);
        Assert.False(NotificationMarquee.ShouldScroll(219, 200, enabled: true));
        Assert.True(NotificationMarquee.ShouldScroll(221, 200, enabled: true));
    }

    // -- OffsetFor ------------------------------------------------------------------------

    [Fact]
    public void Offset_is_zero_at_the_start_of_the_run()
    {
        // The text must be readable where it was written before it moves.
        Assert.Equal(0, NotificationMarquee.OffsetFor(0, 300, 200), 6);
    }

    [Fact]
    public void Offset_never_exceeds_the_travel()
    {
        for (var t = 0.0; t < 8000; t += 37)
        {
            var off = NotificationMarquee.OffsetFor(t, 300, 200);
            Assert.InRange(off, -100.0001, 0.0001);
        }
    }

    [Fact]
    public void Offset_is_the_same_motion_the_monitor_caption_uses()
    {
        // If these two ever differ, two scroll implementations have drifted.
        for (var t = 0.0; t < 6000; t += 91)
            Assert.Equal(MarqueeTrack.OffsetFor(t, 300, 200),
                NotificationMarquee.OffsetFor(t, 300, 200), 6);
    }

    [Fact]
    public void Fits_means_no_movement_at_all()
    {
        for (var t = 0.0; t < 5000; t += 53)
            Assert.Equal(0, NotificationMarquee.OffsetFor(t, 150, 200), 6);
    }

    // -- Bell ring ------------------------------------------------------------------------

    [Fact]
    public void Ring_is_at_its_smallest_at_the_start_of_the_period()
    {
        Assert.Equal(NotificationMarquee.BellRingMinScale,
            NotificationMarquee.BellRingScale(0, 1000, enabled: true), 6);
    }

    [Fact]
    public void Ring_reaches_full_size_halfway_through_the_period()
    {
        Assert.Equal(1.0, NotificationMarquee.BellRingScale(500, 1000, enabled: true), 6);
    }

    [Fact]
    public void Ring_returns_to_small_at_the_end_of_the_period()
    {
        // The wave must be continuous across the wrap, or the ring snaps once per period.
        Assert.Equal(NotificationMarquee.BellRingMinScale,
            NotificationMarquee.BellRingScale(1000, 1000, enabled: true), 6);
    }

    [Fact]
    public void Ring_stays_inside_its_bounds_over_a_long_run()
    {
        for (var t = 0.0; t < 30_000; t += 13)
        {
            var s = NotificationMarquee.BellRingScale(t, NotificationMarquee.BellPulsePeriodMs,
                enabled: true);
            Assert.InRange(s, NotificationMarquee.BellRingMinScale - 1e-9, 1.0 + 1e-9);
        }
    }

    [Fact]
    public void Disabled_ring_does_not_move()
    {
        // Reduced motion removes the movement. A flat 1.0 with the ring simply not drawn is the
        // caller's job; what matters here is that no elapsed time produces a scale.
        for (var t = 0.0; t < 5000; t += 71)
            Assert.Equal(1.0, NotificationMarquee.BellRingScale(t, 1000, enabled: false), 6);
    }

    [Fact]
    public void A_zero_period_is_survivable()
    {
        Assert.Equal(1.0, NotificationMarquee.BellRingScale(123, 0, enabled: true), 6);
    }

    [Fact]
    public void A_negative_elapsed_clock_does_not_produce_a_negative_scale()
    {
        Assert.InRange(NotificationMarquee.BellRingScale(-250, 1000, enabled: true),
            NotificationMarquee.BellRingMinScale - 1e-9, 1.0 + 1e-9);
    }

    [Fact]
    public void Ring_is_a_triangle_not_a_sine()
    {
        // A sine at the quarter point sits near its minimum; a triangle is on the way up. This is
        // what makes the pulse read as travelling rather than as two sizes with a pause.
        var quarter = NotificationMarquee.BellRingScale(250, 1000, enabled: true);
        var expected = NotificationMarquee.BellRingMinScale
            + (1.0 - NotificationMarquee.BellRingMinScale) * 0.5;
        Assert.Equal(expected, quarter, 6);
    }

    // -- BellVisible ----------------------------------------------------------------------

    [Theory]
    [InlineData(OverlayKind.Notification, true)]
    [InlineData(OverlayKind.Error, true)]
    [InlineData(OverlayKind.Weather, false)]
    [InlineData(OverlayKind.Battery, false)]
    [InlineData(OverlayKind.Media, false)]
    [InlineData(OverlayKind.Clipboard, false)]
    [InlineData(OverlayKind.Idle, false)]
    public void Bell_shows_only_for_kinds_that_need_attention(OverlayKind kind, bool expected)
    {
        Assert.Equal(expected, NotificationMarquee.BellVisible(kind));
    }
}
