using Avalonia;
using Avalonia.Controls;

namespace NotifyIsland;

internal static class OverlayPlacement
{
    public static PixelPoint Compute(Window window, double dipW, double dipH)
    {
        var screen = window.Screens.Primary ?? window.Screens.ScreenFromWindow(window);
        if (screen is null) return window.Position;
        var prefs = PrefsStore.Current;
        var wa = screen.WorkingArea;
        var scale = window.RenderScaling;
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
}
