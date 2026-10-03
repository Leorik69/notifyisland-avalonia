using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class RowInsetsTests
{
    [Fact]
    public void A_horizontal_island_keeps_the_original_insets()
    {
        var i = RowInsets.For(vertical: false);
        Assert.Equal(22.0, i.Inset, 6);
        Assert.True(i.ShowAppIcon);
        Assert.Equal(18.0, i.IconSize, 6);
    }

    [Fact]
    public void A_vertical_island_uses_a_much_smaller_inset()
    {
        var i = RowInsets.For(vertical: true);
        Assert.Equal(4.0, i.Inset, 6);
        Assert.False(i.ShowAppIcon);
    }

    [Fact]
    public void The_text_fits_on_a_vertical_island_with_everything_on()
    {
        // The bug this file exists for: with the old 22 DIP insets and the icon kept, the text
        // column came out NEGATIVE and the message was drawn at zero width.
        var i = RowInsets.For(vertical: true);
        var w = i.TextWidthOn(OverlayTokens.CollapsedCrossAxisVertical,
            bellVisible: true, unreadVisible: true, vertical: true);
        Assert.True(w > 40, $"text column was only {w} DIP on a vertical island");
    }

    [Fact]
    public void The_old_insets_produced_a_negative_column_and_are_pinned_as_such()
    {
        // Kept as a test on purpose: it is the arithmetic that was wrong, written down so the
        // regression cannot come back unnoticed.
        var old = 88.0 - 2 * RowInsets.HorizontalInset
                        - (RowInsets.HorizontalIconSize + RowInsets.IconGap)
                        - RowInsets.UnreadW;
        Assert.True(old <= 0, $"expected the old layout to be non-positive, got {old}");
    }

    [Fact]
    public void A_vertical_island_never_draws_the_app_icon()
    {
        // The toast's title IS the app's name, so at this width the icon costs more than it says.
        Assert.False(RowInsets.For(vertical: true).ShowAppIcon);
    }

    [Fact]
    public void A_horizontal_island_has_room_to_everything()
    {
        // ExpandedMinW is the narrowest a horizontal capsule gets; the row must still show text.
        var i = RowInsets.For(vertical: false);
        var w = i.TextWidthOn(OverlayTokens.ExpandedMinW, bellVisible: true, unreadVisible: true,
            vertical: false);
        Assert.True(w > 60, $"only {w} DIP for the message on the narrowest horizontal capsule");
    }

    [Fact]
    public void Turning_the_bell_on_costs_exactly_its_own_width()
    {
        var i = RowInsets.For(vertical: true);
        var off = i.TextWidthOn(88, false, true, true);
        var on = i.TextWidthOn(88, true, true, true);
        Assert.Equal(i.BellWidthFor(true), off - on, 6);
    }

    [Fact]
    public void Turning_the_unread_badge_on_costs_exactly_its_own_width()
    {
        var i = RowInsets.For(vertical: true);
        var off = i.TextWidthOn(88, true, false, true);
        var on = i.TextWidthOn(88, true, true, true);
        Assert.Equal(i.UnreadWidthFor(true), off - on, 6);
    }

    [Fact]
    public void A_vertical_island_gives_the_message_more_room_than_a_horizontal_one_would()
    {
        // The point of the whole file: at 88 DIP across, the same 22 DIP margins that look right
        // on a wide island would leave nothing at all.
        var v = RowInsets.For(vertical: true);
        var vw = v.TextWidthOn(OverlayTokens.CollapsedCrossAxisVertical, true, true, true);
        var hw = RowInsets.For(vertical: false)
            .TextWidthOn(OverlayTokens.CollapsedCrossAxisVertical, true, true, false);
        Assert.True(vw > hw, $"vertical {vw} should beat horizontal {hw} on an 88 DIP island");
    }

    [Fact]
    public void The_badges_are_physically_smaller_on_a_vertical_island()
    {
        var i = RowInsets.For(vertical: true);
        Assert.True(i.BellWidthFor(true) < i.BellWidthFor(false));
        Assert.True(i.UnreadWidthFor(true) < i.UnreadWidthFor(false));
    }

    [Fact]
    public void The_vertical_font_is_smaller_than_the_horizontal_one()
    {
        // A fixed font size that fits a wide row does not fit a 40 DIP column, and the marquee
        // cannot help if even one glyph is wider than the slot.
        Assert.True(RowInsets.VerticalTitleFont < 12.0);
        Assert.True(RowInsets.VerticalBodyFont < 11.0);
    }

    [Fact]
    public void A_narrower_capsule_gives_a_narrower_column()
    {
        var i = RowInsets.For(vertical: true);
        Assert.True(i.TextWidthOn(60, true, true, true) < i.TextWidthOn(88, true, true, true));
    }

    [Fact]
    public void Hiding_the_badge_gives_the_text_its_room_back()
    {
        var i = RowInsets.For(vertical: true);
        Assert.True(i.TextWidthOn(88, true, false, true) > i.TextWidthOn(88, true, true, true));
    }
}
