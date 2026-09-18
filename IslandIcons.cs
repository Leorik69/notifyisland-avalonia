using Avalonia.Media;

namespace NotifyIsland;

internal enum IslandGlyph
{
    Notify,
    Progress,
    MediaPlay,
    MediaPause,
    Timer,
    Error,
    Overview,
}

internal static class IslandIcons
{
    // Compact Fluent-like path data (24×24 viewbox), not emoji.
    public const string Notify = "M12 3a7 7 0 0 0-7 7v3.2L3.4 16a1 1 0 0 0 .8 1.6h15.6a1 1 0 0 0 .8-1.6L19 13.2V10a7 7 0 0 0-7-7Zm0 18a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 21Z";
    public const string Progress = "M5 4h10l4 4v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Zm9 1.5V9h3.5L14 5.5ZM7 12h10v1.6H7V12Zm0 4h7v1.6H7V16Z";
    public const string MediaPlay = "M8 5.8v12.4a1 1 0 0 0 1.54.84l9.2-6.2a1 1 0 0 0 0-1.68l-9.2-6.2A1 1 0 0 0 8 5.8Z";
    public const string MediaPause = "M7 5.5A1.5 1.5 0 0 1 8.5 4h1A1.5 1.5 0 0 1 11 5.5v13A1.5 1.5 0 0 1 9.5 20h-1A1.5 1.5 0 0 1 7 18.5v-13Zm6.5 0A1.5 1.5 0 0 1 15 4h1a1.5 1.5 0 0 1 1.5 1.5v13A1.5 1.5 0 0 1 16 20h-1a1.5 1.5 0 0 1-1.5-1.5v-13Z";
    public const string Timer = "M12 4a9 9 0 1 0 9 9A9 9 0 0 0 12 4Zm.8 5v4.1l3 1.8-.8 1.3L11 13.6V9h1.8ZM9 2h6v1.7H9V2Z";
    public const string Error = "M12 3.2 21 20H3L12 3.2ZM11 9h2v5h-2V9Zm0 6.5h2V18h-2v-2.5Z";
    public const string Overview = "M4 4h7v7H4V4Zm9 0h7v7h-7V4ZM4 13h7v7H4v-7Zm9 0h7v7h-7v-7Z";
    public const string MinimalDot = "M12 8.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7Z";

    public static StreamGeometry Geometry(IslandGlyph glyph, string iconStyle)
    {
        var data = iconStyle == "minimal"
            ? MinimalDot
            : glyph switch
            {
                IslandGlyph.Notify => Notify,
                IslandGlyph.Progress => Progress,
                IslandGlyph.MediaPlay => MediaPlay,
                IslandGlyph.MediaPause => MediaPause,
                IslandGlyph.Timer => Timer,
                IslandGlyph.Error => Error,
                _ => Overview
            };
        return StreamGeometry.Parse(data);
    }

    public static string Mdl2(IslandGlyph glyph) => glyph switch
    {
        IslandGlyph.Notify => "\uEA8F",
        IslandGlyph.Progress => "\uE8A5",
        IslandGlyph.MediaPlay => "\uE768",
        IslandGlyph.MediaPause => "\uE769",
        IslandGlyph.Timer => "\uE916",
        IslandGlyph.Error => "\uE783",
        _ => "\uE80F"
    };

    public static IslandGlyph ForKind(OverlayKind kind, bool playing = false) => kind switch
    {
        OverlayKind.Notification => IslandGlyph.Notify,
        OverlayKind.Progress => IslandGlyph.Progress,
        OverlayKind.Media => playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay,
        OverlayKind.Timer => IslandGlyph.Timer,
        OverlayKind.Error => IslandGlyph.Error,
        OverlayKind.Stack => IslandGlyph.Notify,
        _ => IslandGlyph.Overview
    };
}
