using System;

namespace NotifyIsland;

/// <summary>
/// Compatibility shim for the pre-1.14 clipboard history panel geometry (1.12.3).
///
/// <para>
/// <b>This geometry is gone.</b> The panel used to sit NEXT TO the ball on a window inflated by
/// a whole drag disc, which is what made it asymmetric, what needed
/// <c>BallSideExtent</c>/<c>CrossOriginFor</c> to express the asymmetry, and what let the
/// visible content walk past the screen edge. 1.14 replaces all of it with
/// <see cref="ClipboardDrawer"/>: the list hangs off the capsule's cross edge, spans the
/// capsule's long axis exactly, and needs no slack.
/// </para>
///
/// <para>
/// Only the <see cref="SizeFor"/> row-count rule survives, and it now delegates to the drawer so
/// there is one definition of "how tall is a list of N rows". The type is kept — not deleted —
/// because it is referenced by the app's row-building path and by the tray, both of which are
/// outside this workstream's scope.
/// </para>
/// </summary>
public static class ClipboardHistoryPanel
{
    /// <summary>
    /// Which way the drawer opens on the cross axis. Kept as a forwarding member so existing
    /// call sites keep compiling; the rule itself now lives with the drawer it belongs to.
    /// </summary>
    public static int CrossDirectionFor(IslandEdge edge) => ClipboardDrawer.CrossDirectionFor(edge);

    /// <summary>
    /// Size of a clipboard history list for <paramref name="rowCount"/> rows. The width is the
    /// drawer's <em>cross</em> extent — the list's own height in the drawer model — and the
    /// height is the fixed <see cref="OverlayTokens.HistoryPanelW"/> row-block width, because
    /// this member predates the drawer and its callers lay the rows out on that axis.
    /// </summary>
    public static (double Width, double Height) SizeFor(int rowCount) =>
        (OverlayTokens.HistoryPanelW, ClipboardDrawer.CrossFor(rowCount));
}
