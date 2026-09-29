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
        bool splitClipboard = false)
    {
        // SystemStats is a fixed-width block (1.12.1) whose height follows the resolved row
        // count; no metric-count width inflation, and the only kind exempt from the CollapsedH
        // height rule. statsRowCount 0/less = the default 5-row Full panel. It stays exempt
        // from the split half too — a fixed block does not split.
        if (kind == OverlayKind.SystemStats)
            return (OverlayTokens.StatsExpandedW, StatsLayout.StatsHeightFor(statsRowCount));

        var longAxis = OverlayMachine.WidthFor(kind, weatherEnabled, batteryChip, statsMetricCount,
                                               splitClipboard);
        var shortAxis = OverlayTokens.CollapsedH;
        if (IsVertical(orientation, edge))
            return (shortAxis, longAxis);
        return (longAxis, shortAxis);
    }

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
    /// 1.12.3 (goo blob, spec docs/superpowers/specs/2026-09-29--notifyisland-goo-blob.md):
    /// shift a blob-sized window so that the capsule inside it stays exactly where
    /// <see cref="Place"/> put it. With a blob the window is much larger than the capsule —
    /// it has to hold the ball and its whole drag disc — but the island's own geometry (edge
    /// anchor, user offsets, click zones, hit test) is all defined against the capsule, and
    /// letting the window drag the capsule along with it would move the island on every
    /// attach. So Place() keeps positioning the capsule and this only re-seats the window:
    /// <list type="bullet">
    ///   <item>horizontal (Top/Bottom): the long axis is X, the window grows rightwards, so
    ///     X is kept verbatim and the cross-axis slack is split evenly above and below;</item>
    ///   <item>vertical (Left/Right): the same with the axes swapped — the long axis is Y and
    ///     the window grows downwards, so Y is kept and X is centred on the capsule.</item>
    /// </list>
    /// The capsule itself is pinned inside the window by alignment (Leading on the long axis,
    /// Center on the cross axis), so no other island code has to know the window grew.
    /// Sizes are pixels; slack can never be negative by construction, but a caller that hands
    /// in a stale measurement must not move the window backwards, hence the max(0, …).
    /// </summary>
    public static (int X, int Y) BlobWindowFor(
        bool isVertical, int homeX, int homeY, int homeW, int homeH, int windowW, int windowH)
    {
        if (isVertical)
            return (homeX - Math.Max(0, windowW - homeW) / 2, homeY);
        return (homeX, homeY - Math.Max(0, windowH - homeH) / 2);
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