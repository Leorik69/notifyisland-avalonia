using Avalonia;
using Avalonia.Controls;
using NotifyIsland.Core;
using NotifyIsland.Platform;

namespace NotifyIsland;

internal static class OverlayPlacement
{
    public static IScreenPlacement Current { get; } = new HostPlacement();
    public static double LastScale { get; private set; } = 1;

    public static PixelPoint Compute(Window window, double dipW, double dipH)
    {
        var screens = window.Screens.All;
        Avalonia.Platform.Screen? screen = null;
        var idx = PrefsStore.Current.ScreenIndex;
        if (idx >= 0 && idx < screens.Count)
            screen = screens[idx];
        screen ??= window.Screens.Primary ?? window.Screens.ScreenFromWindow(window);
        if (screen is null) return window.Position;
        var prefs = PrefsStore.Current;
        var wa = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
        LastScale = scale;
        var pw = (int)Math.Round(dipW * scale);
        var ph = (int)Math.Round(dipH * scale);
        var pad = (int)Math.Round(8 * scale);
        var ox = (int)Math.Round(prefs.OffsetX * scale);
        var oy = (int)Math.Round(prefs.OffsetY * scale);
        var x = prefs.AnchorH switch
        {
            "left" => wa.X + pad,
            "right" => wa.X + wa.Width - pw - pad,
            _ => wa.X + (wa.Width - pw) / 2
        } + ox;
        var y = prefs.AnchorV switch
        {
            "center" => wa.Y + (wa.Height - ph) / 2,
            "bottom" => wa.Y + wa.Height - ph - pad,
            _ => wa.Y + pad
        } + oy;
        x = Math.Clamp(x, wa.X, Math.Max(wa.X, wa.X + wa.Width - pw));
        y = Math.Clamp(y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - ph));
        return new PixelPoint(x, y);
    }

    private sealed class HostPlacement : IScreenPlacement
    {
        public PixelPoint Compute(Window window, double dipW, double dipH)
            => OverlayPlacement.Compute(window, dipW, dipH);
    }
}
