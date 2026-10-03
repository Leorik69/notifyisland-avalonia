namespace NotifyIsland;

/// <summary>
/// Horizontal insets for the single-row capsule content, per orientation.
/// <para>
/// 2026-10-03, measured. The row's own margin was 22 DIP on both sides, which is right for a
/// HORIZONTAL island and ruinous for a vertical one: a vertical island is
/// <see cref="OverlayTokens.CollapsedCrossAxisVertical"/> (88) DIP across, and 22 + 22 of it is
/// almost half the width. With the app icon (26) and the unread badge (26) taken, the text
/// column came out at <b>−8 DIP</b> — negative, so the message was drawn at zero width.
/// </para>
/// <para>
/// That is a pre-existing bug, not one this workstream introduced: the same arithmetic against
/// the markup as it stood before the bell badge gives 88 − 44 − 26 − 26 = −8. It was invisible
/// because the ellipsis quietly renders an empty box rather than throwing, so the island looked
/// fine and simply never said anything.
/// </para>
/// <para>
/// The vertical layout also drops the app icon. The title of a toast is the app's name, so on a
/// 28-DIP-wide column the icon costs the message more than it tells the reader — and the bell
/// badge beside it already says «this is a notification».
/// </para>
/// </summary>
public readonly record struct RowInsets(double Inset, bool ShowAppIcon, double IconSize)
{
    /// <summary>Margin on each side of a horizontal island's row (DIP).</summary>
    public const double HorizontalInset = 22.0;

    /// <summary>
    /// Margin on each side of a vertical island's row (DIP). Small on purpose: the cross axis is
    /// only 88 DIP wide and every point here comes out of the text column.
    /// </summary>
    public const double VerticalInset = 4.0;

    /// <summary>App icon edge on a horizontal island (DIP).</summary>
    public const double HorizontalIconSize = 18.0;

    /// <summary>
    /// App icon edge on a vertical island (DIP). Used for the media artwork box so the artwork
    /// still has a frame when the icon glyph itself is hidden.
    /// </summary>
    public const double VerticalIconSize = 14.0;

    /// <summary>Width the app icon occupies including its gap to the text (DIP).</summary>
    public const double IconGap = 8.0;

    /// <summary>Width the bell badge occupies including its gap (DIP).</summary>
    public const double BellW = 22.0;

    /// <summary>
    /// Width the bell badge occupies on a vertical island (DIP): 12 DIP of badge and 3 of gap
    /// instead of 16 and 6. At 88 DIP across, six points here are six points of the message.
    /// </summary>
    public const double BellWVertical = 15.0;

    /// <summary>Width the unread badge occupies including its gap (DIP).</summary>
    public const double UnreadW = 26.0;

    /// <summary>Same, on a vertical island: a 16 DIP badge with a 3 DIP gap.</summary>
    public const double UnreadWVertical = 19.0;

    /// <summary>Title font size on a vertical island (DIP).</summary>
    public const double VerticalTitleFont = 10.0;

    /// <summary>Body font size on a vertical island (DIP).</summary>
    public const double VerticalBodyFont = 9.0;

    /// <summary>The insets for an island of the given orientation.</summary>
    public static RowInsets For(bool vertical) => vertical
        ? new RowInsets(VerticalInset, false, VerticalIconSize)
        : new RowInsets(HorizontalInset, true, HorizontalIconSize);

    /// <summary>Badge width for the orientation, gap included (DIP).</summary>
    public double BellWidthFor(bool vertical) => vertical ? BellWVertical : BellW;

    /// <summary>Unread badge width for the orientation, gap included (DIP).</summary>
    public double UnreadWidthFor(bool vertical) => vertical ? UnreadWVertical : UnreadW;

    /// <summary>
    /// Should a notification row be ONE scrolling line instead of a title and a body?
    /// <para>
    /// True for the vertical island, and for a reason that is arithmetic rather than taste. The
    /// two-text hierarchy splits the column 62/38 (see <see cref="NotificationLayout.TitleShare"/>),
    /// so on a 46 DIP column the body gets 12 — about one glyph, which no marquee can make
    /// readable. One line carrying «title · body» spends every point on the message and lets the
    /// scroll do what it is for. The two-ink hierarchy is kept on the horizontal island, where
    /// there is room for it.
    /// </para>
    /// </summary>
    public static bool SingleLineRow(bool vertical) => vertical;

    /// <summary>
    /// The width left for the title and body once the fixed furniture is placed.
    /// <para>
    /// Kept here rather than in the view so the claim «the message fits» is a testable
    /// statement. A row that returns a non-positive width is one that will draw nothing at all,
    /// and that is exactly the failure this function exists to make impossible.
    /// </para>
    /// </summary>
    public double TextWidthOn(double capsuleCross, bool bellVisible, bool unreadVisible, bool vertical)
    {
        var used = 2 * Inset;
        if (ShowAppIcon) used += IconSize + IconGap;
        if (bellVisible) used += BellWidthFor(vertical);
        if (unreadVisible) used += UnreadWidthFor(vertical);
        return capsuleCross - used;
    }
}
