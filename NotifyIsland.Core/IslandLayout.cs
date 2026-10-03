using System;

namespace NotifyIsland;

/// <summary>
/// Pure layout helpers: orientation resolution, size along/across long axis,
/// and edge+offset placement in working-area coordinates.
/// </summary>
public static class IslandLayout
{
    /// <summary>Hold threshold before left-drag becomes reposition (ms).</summary>
    public const int DragHoldMs = 200;

    /// <summary>Default inset from the chosen screen edge when OffsetY/X is 0 on that axis.</summary>
    public const int DefaultEdgeInsetPx = 8;

    public static bool IsVertical(IslandOrientation orientation, IslandEdge edge) =>
        orientation switch
        {
            IslandOrientation.Vertical => true,
            IslandOrientation.Horizontal => false,
            _ => edge is IslandEdge.Left or IslandEdge.Right
        };

    /// <summary>
    /// Logical morph length is always OverlayMachine.WidthFor (long axis).
    /// Vertical capsule: width = CollapsedH (thin), height = long axis.
    /// Horizontal: width = long axis, height = CollapsedH.
    /// SystemStats: fixed StatsExpandedW × StatsHeightFor(statsRowCount), orientation-independent.
    /// splitClipboard (1.12.2): adds ClipboardHalfW to the long axis, so on Left/Right the
    /// clipboard half grows the height and the width stays the thin CollapsedH capsule.
    /// </summary>
    public static (double Width, double Height) SizeFor(
        OverlayKind kind,
        bool weatherEnabled,
        IslandOrientation orientation,
        IslandEdge edge,
        bool batteryChip = false,
        int statsMetricCount = 0,
        int statsRowCount = 0,
        bool splitClipboard = false,
        double collapsedScale = IslandWidth.DefaultScale)
    {
        // SystemStats is a fixed-width block (1.12.1) whose height follows the resolved row
        // count; no metric-count width inflation, and the only kind exempt from the CollapsedH
        // height rule. statsRowCount 0/less = the default 5-row Full panel. It stays exempt
        // from the split half too — a fixed block does not split.
        if (kind == OverlayKind.SystemStats)
            return (OverlayTokens.StatsExpandedW, StatsLayout.StatsHeightFor(statsRowCount));

        var longAxis = OverlayMachine.WidthFor(kind, weatherEnabled, batteryChip, statsMetricCount,
                                               splitClipboard, collapsedScale);
        // The cross axis is orientation-dependent: on a horizontal capsule it is the height that
        // CollapsedH was designed around, and on a vertical one it is the width, which has to hold
        // the digital clock's digit strip. See OverlayTokens.CollapsedCrossAxisVertical.
        var shortAxis = IsVertical(orientation, edge)
            ? OverlayTokens.CollapsedCrossAxisVertical
            : OverlayTokens.CollapsedH;
        if (IsVertical(orientation, edge))
            return (shortAxis, longAxis);
        return (longAxis, shortAxis);
    }

    /// <summary>
    /// Screen-space width/height of a capsule given its long and cross extents. On a vertical
    /// island the long axis is the HEIGHT. <see cref="Place"/> takes width/height, and feeding
    /// it (long, cross) unrotated put a Right-edge island 354 px off the edge and centred its
    /// TOP rather than its middle on Left/Right.
    /// </summary>
    public static (double Width, double Height) ScreenSizeFor(double longExtent, double crossExtent, bool vertical) =>
        vertical ? (crossExtent, longExtent) : (longExtent, crossExtent);

    /// <summary>
    /// Place island in working-area pixel coords.
    /// OffsetX/OffsetY are pixels from the edge anchor:
    /// Top: X = center + OffsetX, Y = top + inset + OffsetY
    /// Bottom: X = center + OffsetX, Y = bottom - h - inset + OffsetY
    /// Left: X = left + inset + OffsetX, Y = center + OffsetY
    /// Right: X = right - w - inset + OffsetX, Y = center + OffsetY
    /// </summary>
    public static (int X, int Y) Place(
        int waX, int waY, int waW, int waH,
        int pixelW, int pixelH,
        IslandEdge edge,
        int offsetX, int offsetY)
    {
        var inset = DefaultEdgeInsetPx;
        var maxX = Math.Max(waX, waX + waW - pixelW);
        var maxY = Math.Max(waY, waY + waH - pixelH);

        int x, y;
        switch (edge)
        {
            case IslandEdge.Bottom:
                x = waX + (waW - pixelW) / 2 + offsetX;
                y = waY + waH - pixelH - inset + offsetY;
                break;
            case IslandEdge.Left:
                x = waX + inset + offsetX;
                y = waY + (waH - pixelH) / 2 + offsetY;
                break;
            case IslandEdge.Right:
                x = waX + waW - pixelW - inset + offsetX;
                y = waY + (waH - pixelH) / 2 + offsetY;
                break;
            default: // Top
                x = waX + (waW - pixelW) / 2 + offsetX;
                y = waY + inset + offsetY;
                break;
        }

        x = Math.Clamp(x, waX, maxX);
        y = Math.Clamp(y, waY, maxY);
        return (x, y);
    }

    /// <summary>
    /// Seat the window around a capsule that a clipboard drawer may be attached to (1.14).
    /// <para>
    /// The window is the capsule on the long axis and capsule+drawer on the cross axis, so the
    /// only slack that can ever exist is the drawer's own extent on the cross axis, and
    /// <paramref name="crossShiftDip"/> is exactly that. The long axis is copied verbatim: the
    /// window is never wider or taller than the island along it, so the island cannot move when
    /// the drawer opens.
    /// </para>
    /// <para>
    /// <b>No drag slack is re-introduced here.</b> The 1.12.3 version of this function existed
    /// only to re-seat a window inflated by <c>BlobDragMaxPx</c> in every direction, and that
    /// oversized transparent window is the direct cause of the off-screen bugs this workstream
    /// is fixing. Nothing is draggable any more, so the window is exactly the content it shows
    /// and clamping the capsule with <see cref="Place"/> is enough to keep it all on screen.
    /// </para>
    /// <para>
    /// <paramref name="crossShiftDip"/> MUST be zero or negative — it is
    /// <c>ClipboardDrawer.CrossShiftDipFor</c>, which is 0 when the drawer grows away from the
    /// capsule and negative by the drawer's extent when it grows the other way. The capsule sits
    /// at window offset <c>-crossShiftDip</c> on the cross axis, so a POSITIVE shift would seat
    /// the capsule outside the window it is supposed to be inside; that is a caller bug, not a
    /// case this function tries to absorb.
    /// </para>
    /// </summary>
    public static (int X, int Y) DrawerWindowFor(
        bool isVertical, int capsuleX, int capsuleY, double crossShiftDip, double scale)
    {
        var shift = (int)Math.Round(crossShiftDip * scale);
        return isVertical ? (capsuleX + shift, capsuleY) : (capsuleX, capsuleY + shift);
    }

    /// <summary>
    /// After a user drag, derive OffsetX/OffsetY so Place() reproduces the new position
    /// for the current edge (inverse of Place, ignoring clamp).
    /// </summary>
    public static (int OffsetX, int OffsetY) OffsetsFromPosition(
        int waX, int waY, int waW, int waH,
        int pixelW, int pixelH,
        IslandEdge edge,
        int posX, int posY)
    {
        var inset = DefaultEdgeInsetPx;
        return edge switch
        {
            IslandEdge.Bottom => (
                posX - (waX + (waW - pixelW) / 2),
                posY - (waY + waH - pixelH - inset)),
            IslandEdge.Left => (
                posX - (waX + inset),
                posY - (waY + (waH - pixelH) / 2)),
            IslandEdge.Right => (
                posX - (waX + waW - pixelW - inset),
                posY - (waY + (waH - pixelH) / 2)),
            _ => (
                posX - (waX + (waW - pixelW) / 2),
                posY - (waY + inset))
        };
    }
}