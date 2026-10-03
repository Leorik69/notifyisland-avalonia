using System;

namespace NotifyIsland;

/// <summary>
/// Pure geometry and animation for the clipboard DRAWER (1.14, spec
/// docs/superpowers/specs/2026-09-30--notifyisland-clipboard-section.md): the clipboard history
/// list that slides out of the capsule's cross edge and stays attached to it.
///
/// <para>
/// The drawer is a drawer, not a floating panel. Three things make that true and all three are
/// expressed here so they cannot drift apart: it hangs off the capsule's edge with NO gap, it
/// spans the capsule's long axis exactly, and the two shapes that meet at the seam lose their
/// corner radius (see <see cref="CapsuleRadiiFor"/> / <see cref="DrawerRadiiFor"/>). A gap, a
/// mismatch in width or a matched radius at the seam are each, on their own, enough to make the
/// pair read as two capsules that happen to be near each other.
/// </para>
///
/// <para>
/// <b>Why this fixes the off-screen bug.</b> The window is the capsule on the long axis and
/// capsule+drawer on the cross axis — nothing more. The old ball needed the window inflated by
/// a whole <c>BlobDragMaxPx</c> drag disc in every direction to have room to be dragged, and
/// that slack is what kept pushing the visible content past the screen edge. With the drag
/// gone the slack is gone, so <see cref="IslandLayout.Place"/> clamping the capsule is enough to
/// guarantee the whole window is on screen: the drawer only ever grows towards the screen
/// interior and spans exactly the capsule's long extent.
/// </para>
/// </summary>
public static class ClipboardDrawer
{
    /// <summary>
    /// The four corner radii of one shape, in Avalonia's TopLeft/TopRight/BottomRight/BottomLeft
    /// order. Core cannot reference Avalonia's <c>CornerRadius</c>, so the seam maths is stated
    /// here as plain doubles and the view composes them — which also means the seam is a test.
    /// </summary>
    public readonly record struct Radii(double TopLeft, double TopRight, double BottomRight,
        double BottomLeft);

    /// <summary>
    /// Which way the drawer opens on the cross axis: +1 towards increasing cross coordinate
    /// (down on a Top island, right on a Left one), -1 the other way. Derived from the edge,
    /// never passed in — a caller that disagreed with this about the sign would open the drawer
    /// off-screen, and that is exactly the class of bug the edge mapping exists to prevent.
    /// </summary>
    public static int CrossDirectionFor(IslandEdge edge) => edge switch
    {
        // Bottom island → up; Right → left. Those are the two whose capsule sits at the far
        // side of the working area, so the drawer has to grow back towards the interior.
        IslandEdge.Bottom or IslandEdge.Right => -1,
        _ => +1,
    };

    /// <summary>Drawer size: its long axis IS the capsule's long axis, its cross axis is the list.</summary>
    public static (double Width, double Height) SizeFor(int rowCount, double capsuleLong)
    {
        var rows = Math.Max(1, Math.Min(rowCount, OverlayTokens.HistoryPanelMaxRows));
        return (capsuleLong,
                rows * OverlayTokens.HistoryPanelRowH + 2 * OverlayTokens.HistoryPanelPadY);
    }

    /// <summary>Cross-axis extent of the drawer for <paramref name="rowCount"/> rows (DIP).</summary>
    public static double CrossFor(int rowCount) =>
        Math.Max(1, Math.Min(rowCount, OverlayTokens.HistoryPanelMaxRows))
            * OverlayTokens.HistoryPanelRowH + 2 * OverlayTokens.HistoryPanelPadY;

    /// <summary>
    /// Window size for a capsule of <paramref name="capsuleLong"/> × <paramref
    /// name="capsuleCross"/>: the capsule alone when closed, capsule+drawer stacked on the cross
    /// axis when open. The long axis is untouched either way — that is the "the island does not
    /// move" property, stated as a size rather than as a placement rule.
    /// </summary>
    public static (double Width, double Height) WindowFor(
        bool isVertical, double capsuleLong, double capsuleCross, int rowCount, bool open)
    {
        if (!open || rowCount <= 0)
            return isVertical ? (capsuleCross, capsuleLong) : (capsuleLong, capsuleCross);
        var cross = capsuleCross + CrossFor(rowCount);
        return isVertical ? (cross, capsuleLong) : (capsuleLong, cross);
    }

    /// <summary>
    /// Where the drawer's near edge sits on the cross axis, in window coordinates. It is flush
    /// with the capsule's far edge — <c>capsuleCross</c> when opening downwards/rightwards, 0
    /// when opening upwards/leftwards (where the drawer is the part of the window nearest the
    /// origin and the capsule follows it). Zero gap, by design.
    /// </summary>
    public static double DrawerCrossStart(IslandEdge edge, double capsuleCross, int rowCount) =>
        CrossDirectionFor(edge) > 0 ? capsuleCross : 0.0;

    /// <summary>
    /// How far the window's cross origin sits from the capsule's own cross origin (DIP), signed.
    /// Zero when the drawer grows away from the capsule's edge; negative by the drawer's full
    /// extent when it grows the other way, because then the drawer occupies the part of the
    /// window nearest the origin. The view adds this to the capsule's placed position, so the
    /// CAPSULE is what <see cref="IslandLayout.Place"/> clamps and the window is arranged around
    /// it — the same two-step that kept the island still before, now with no slack to abuse.
    /// </summary>
    public static double CrossShiftDipFor(IslandEdge edge, int rowCount) =>
        CrossDirectionFor(edge) > 0 ? 0.0 : -CrossFor(rowCount);

    // -- the track ------------------------------------------------------------------
    // Pure phase/progress maths, so the drawer animation is testable without a window. The view
    // reads it off the EXISTING shared 16 ms morph tick; there is no timer of its own, because a
    // second "when is this over" is the thing the animation layer is built to avoid.

    /// <summary>
    /// Local phase progress (0 → 1) at morph progress <paramref name="t"/>. Closing runs the
    /// time-reverse of opening from the same <paramref name="t"/>, so a drawer that is
    /// interrupted half-way reverses from exactly where it is instead of restarting.
    /// </summary>
    public static double PhaseAt(double t, bool opening) =>
        opening ? Math.Clamp(t, 0.0, 1.0) : Math.Clamp(1.0 - t, 0.0, 1.0);

    /// <summary>
    /// Slide offset in DIP, signed along the cross axis. The drawer starts
    /// <see cref="OverlayTokens.ClipboardDrawerTravel"/> outside the seam and travels to 0;
    /// positive is "travelling in". The sign follows the open direction, so on a Bottom island
    /// the drawer rises out of the capsule's top edge and on a Top island it drops out of the
    /// bottom one — in both cases it comes FROM the capsule and stops AT the seam.
    /// </summary>
    public static double SlideDipAt(double t, bool opening, int crossDirection) =>
        OverlayTokens.ClipboardDrawerTravel *
        (1.0 - AnimationEasing.CubicOut(PhaseAt(t, opening))) * crossDirection;

    /// <summary>Opacity of the drawer at progress <paramref name="t"/>.</summary>
    public static double OpacityAt(double t, bool opening) =>
        PhaseAt(t, opening);

    /// <summary>Corner radius of the drawer itself (DIP) when it stands alone, i.e. never in
    /// the joined state — kept here so the standalone and joined shapes are one constant.</summary>
    public static double Radius => OverlayTokens.ClipboardDrawerRadius;

    /// <summary>
    /// Capsule corner radii while the drawer is joined to it: the two corners ON the seam go
    /// square, the other two keep the capsule's own radius. A matched radius at the seam is what
    /// made the old panel read as a second capsule; a square seam is what makes the pair read as
    /// one object with a lid.
    /// </summary>
    public static Radii CapsuleRadiiFor(double capsuleRadius, bool joined,
        int crossDirection, bool isVertical)
    {
        if (!joined) return new Radii(capsuleRadius, capsuleRadius, capsuleRadius, capsuleRadius);
        // crossDirection > 0 puts the drawer BELOW (horizontal) / RIGHT (vertical) the capsule.
        return crossDirection > 0
            ? (isVertical
                ? new Radii(capsuleRadius, 0, 0, capsuleRadius)      // right corners square
                : new Radii(capsuleRadius, capsuleRadius, 0, 0))     // bottom corners square
            : (isVertical
                ? new Radii(0, capsuleRadius, capsuleRadius, 0)      // left corners square
                : new Radii(0, 0, capsuleRadius, capsuleRadius));    // top corners square
    }

    /// <summary>
    /// Drawer corner radii, the mirror image of <see cref="CapsuleRadiiFor"/>: the corners on
    /// the seam go square, the off-seam end stays rounded so the drawer has a visible bottom of
    /// its own.
    /// </summary>
    public static Radii DrawerRadiiFor(bool joined, int crossDirection, bool isVertical)
    {
        var r = Radius;
        if (!joined) return new Radii(r, r, r, r);
        return crossDirection > 0
            ? (isVertical
                ? new Radii(0, r, r, 0)                              // left corners square
                : new Radii(0, 0, r, r))                             // top corners square
            : (isVertical
                ? new Radii(r, 0, 0, r)                              // right corners square
                : new Radii(r, r, 0, 0));                            // bottom corners square
    }

    /// <summary>All three animated channels for one morph frame.</summary>
    public readonly record struct Frame(double Slide, double Opacity);

    /// <summary>Frame for a morph progress — the one call the view makes per tick.</summary>
    public static Frame FrameAt(double t, bool opening, int crossDirection) =>
        new(SlideDipAt(t, opening, crossDirection), OpacityAt(t, opening));
}
