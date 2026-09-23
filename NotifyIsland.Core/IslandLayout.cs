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
    /// </summary>
    public static (double Width, double Height) SizeFor(
        OverlayKind kind,
        bool weatherEnabled,
        IslandOrientation orientation,
        IslandEdge edge)
    {
        var longAxis = OverlayMachine.WidthFor(kind, weatherEnabled);
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
