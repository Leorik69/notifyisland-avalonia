using System;

namespace NotifyIsland;

/// <summary>
/// Geometry of the clipboard history panel (spec 1.12.3, §«Панель истории»): where it sits
/// inside the already-enlarged blob window, and how much that window has to grow to hold it.
///
/// <para>
/// The panel opens <em>towards the screen interior</em> on the short axis and <em>next to the
/// ball</em> on the long axis. "Towards the interior" is one rule for all four edges (Top → down,
/// Bottom → up, Left → right, Right → left) rather than a per-edge table, because the opposite
/// choice puts the panel off-screen — and the case that decides it, Top → down, is the default.
/// </para>
///
/// <para>
/// <b>Why the window cannot stay symmetric.</b> The resting blob window is symmetric: it splits
/// the short-axis slack evenly around the capsule, so the capsule sits dead centre
/// (<see cref="IslandLayout.BlobWindowFor"/>). The panel needs far more room on one side than on
/// the other, and a symmetric window has only one way to absorb that — by growing on both sides
/// and pushing the capsule by half the difference, which is exactly what the spec forbids
/// ("остров не двигается"). So while the panel is open the window is asymmetric: the ball keeps
/// its full drag disc on the far side, the panel takes the near side, and the capsule is pinned
/// to the window by alignment rather than by centring. <see cref="CrossOriginFor"/> is where that
/// asymmetry is expressed; the resting path keeps using <see cref="IslandLayout.BlobWindowFor"/>
/// unchanged.
/// </para>
/// </summary>
public static class ClipboardHistoryPanel
{
    /// <summary>
    /// Which way the panel opens on the short axis: +1 towards increasing cross coordinate
    /// (down on a Top island, right on a Left one), -1 the other way. Derived from the edge,
    /// never passed in — a caller that disagreed with this about the sign would put the panel
    /// off-screen, and that is exactly the class of bug the edge mapping exists to prevent.
    /// </summary>
    public static int CrossDirectionFor(IslandEdge edge) => edge switch
    {
        // Top island → down; Bottom → up. Left → right; Right → left.
        IslandEdge.Bottom or IslandEdge.Right => -1,
        _ => +1,
    };

    /// <summary>Panel size for <paramref name="rowCount"/> rows: fixed width, height per row.</summary>
    public static (double Width, double Height) SizeFor(int rowCount)
    {
        var rows = Math.Max(1, Math.Min(rowCount, OverlayTokens.HistoryPanelMaxRows));
        return (OverlayTokens.HistoryPanelW,
                rows * OverlayTokens.HistoryPanelRowH + 2 * OverlayTokens.HistoryPanelPadY);
    }

    /// <summary>
    /// Distance from the capsule's cross CENTRE to the panel's near edge.
    ///
    /// Measured from the BALL's silhouette rather than the capsule's: the ball is what the user
    /// just clicked, and it is 64 DIP across against a 30 DIP capsule, so anchoring to the
    /// capsule would push the panel up until it cut the ball in half. A list whose top edge
    /// crosses the thing it was opened from reads as a rendering bug, not as "close to".
    /// </summary>
    public static double CrossStartFromCentre() =>
        OverlayTokens.BlobD / 2 + OverlayTokens.HistoryPanelGap;

    /// <summary>
    /// How far the window must reach on the ball's side: the ball's radius plus its whole
    /// <see cref="OverlayTokens.BlobDragMaxPx"/> drag disc.
    /// </summary>
    public static double BallSideExtent() =>
        OverlayTokens.BlobD / 2 + OverlayTokens.BlobDragMaxPx;

    /// <summary>How far the window must reach on the panel's side, ball radius plus the panel.</summary>
    public static double PanelSideExtent(double panelCross) =>
        CrossStartFromCentre() + panelCross;

    /// <summary>
    /// Window size that holds the panel.
    /// <para>
    /// The long axis is the resting blob requirement (which reserves the phase-A peek) with the
    /// panel's run appended after the ball, taken as a max so the resting layout is the floor.
    /// The capsule is Leading-anchored on the long axis, so growing it there cannot move the
    /// island.
    /// </para>
    /// <para>
    /// The short axis is <see cref="BallSideExtent"/> on one side and
    /// <see cref="PanelSideExtent"/> on the other — the same two numbers on every edge, only
    /// swapped, so a Bottom island gets exactly the window a Top one does.
    /// </para>
    /// </summary>
    public static (double Width, double Height) WindowFor(
        bool isVertical, double capsuleLong, double capsuleCross, int rowCount)
    {
        var (panelLong, panelCross) = SizeFor(rowCount);
        var (blobW, blobH) = ClipboardBlob.WindowFor(isVertical, capsuleLong, capsuleCross);

        var longNeeded = PanelAlongStart(capsuleLong) + panelLong;
        var longAxis = Math.Max(isVertical ? blobH : blobW, longNeeded);

        var crossAxis = BallSideExtent() + PanelSideExtent(panelCross);
        return isVertical ? (crossAxis, longAxis) : (longAxis, crossAxis);
    }

    /// <summary>
    /// Panel's near edge on the long axis: clear of the ball, with
    /// <see cref="OverlayTokens.HistoryPanelGap"/> of daylight so the two do not read as one
    /// shape.
    /// <para>
    /// The ball's far edge is <see cref="ClipboardBlob.BlobHomeAlong"/> evaluated AT THE CAPSULE'S
    /// OWN LENGTH (that is the ball's centre) plus one radius. Passing 0 there instead looks
    /// equivalent — the home spot reads as "capsule + bridge + radius" — and silently drops the
    /// whole capsule from the panel's position, landing the panel 170 DIP back, on top of the
    /// ball it is supposed to sit behind. A test pins the two forms against each other.
    /// </para>
    /// </summary>
    public static double PanelAlongStart(double capsuleLong) =>
        capsuleLong + ClipboardBlob.BlobHomeAlong(capsuleLong) + OverlayTokens.BlobD / 2
        + OverlayTokens.HistoryPanelGap;

    /// <summary>
    /// Short-axis origin of the panel's window, in window coordinates, for a capsule whose cross
    /// centre is at <paramref name="capsuleCentre"/> relative to the capsule's own origin.
    /// <para>
    /// The window is positioned so the capsule's CENTRE lands at <see cref="BallSideExtent"/>
    /// (Top/Left) or <see cref="PanelSideExtent"/> (Bottom/Right) inside it. Both of those are
    /// distances from the window's origin to the capsule's centre, which is what makes this a
    /// one-line composition.
    /// </para>
    /// <para>
    /// <b>The island does not move</b> because the caller seats the window at
    /// <c>capsuleScreen + thisOrigin</c>: the capsule is placed first, by
    /// <see cref="IslandLayout.Place"/>, and the window is then arranged AROUND it. Whatever
    /// asymmetry the panel introduces, it is absorbed by the window's own position rather than
    /// by the capsule's — which is the same two-step the resting blob window already uses.
    /// </para>
    /// </summary>
    public static double CrossOriginFor(
        IslandEdge edge, double capsuleCentre, double panelCross)
    {
        var centreInWindow = CrossDirectionFor(edge) > 0
            ? BallSideExtent()
            : PanelSideExtent(panelCross);
        return capsuleCentre - centreInWindow;
    }

    /// <summary>
    /// Panel's near edge on the short axis, in window coordinates, measured from the window's
    /// cross origin.
    /// <para>
    /// <paramref name="capsuleCentre"/> is the capsule's cross centre RELATIVE TO THE CAPSULE'S
    /// OWN ORIGIN — i.e. its half-height — not its position inside the window. That is the whole
    /// trick behind "the island does not move": the panel's coordinates are derived from the
    /// capsule, and the window is then shifted (<see cref="CrossOriginFor"/>) so the capsule lands
    /// where it always did. Deriving the panel from the WINDOW instead would make the panel drift
    /// as the window grows around it, which is the same mistake
    /// <c>UpdateBlobVisual</c> already documents and fixed once for the ball.
    /// </para>
    /// </summary>
    public static double PanelCrossStart(IslandEdge edge, double capsuleCentre) =>
        CrossDirectionFor(edge) > 0
            ? capsuleCentre + CrossStartFromCentre()
            : capsuleCentre - CrossStartFromCentre();
}
