using Avalonia.Controls;
using NotifyIsland.Platform;

namespace NotifyIsland.Integration.Windows;

internal sealed class Win32WindowChrome : IWindowChrome
{
    public void ApplyNoActivate(Window window) => Win32Overlay.ApplyNoActivate(window);
}

internal sealed class AvaloniaScreenPlacement : IScreenPlacement
{
    public Avalonia.PixelPoint Compute(Window window, double dipW, double dipH)
        => OverlayPlacement.Compute(window, dipW, dipH);
}
