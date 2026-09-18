using NotifyIsland.Core;

namespace NotifyIsland.Platform;

// Windows seams for tests and swapping Win32 hosts. Not a second OS.

public interface IIpcEndpoint : IDisposable
{
    string Name { get; }
    string Status { get; }
    event Action<NotificationRequest>? NotifyReceived;
    event Action<NotificationId>? DismissReceived;
    void Start();
}

public interface IScreenPlacement
{
    Avalonia.PixelPoint Compute(Avalonia.Controls.Window window, double dipW, double dipH);
}

public interface IWindowChrome
{
    void ApplyNoActivate(Avalonia.Controls.Window window);
}
