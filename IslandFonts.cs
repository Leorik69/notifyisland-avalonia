namespace NotifyIsland;

/// <summary>Resolves user font-family setting to Avalonia FontFamily (OFL vendored fonts).</summary>
public static class IslandFonts
{
    public const string SystemId = "System";
    public const string SpaceGroteskId = "SpaceGrotesk";
    public const string JetBrainsMonoId = "JetBrainsMono";

    private static readonly Avalonia.Media.FontFamily SystemFamily =
        new("Segoe UI Variable, Inter, Segoe UI, sans-serif");

    private static readonly Avalonia.Media.FontFamily SpaceGrotesk =
        new("avares://NotifyIsland/Assets/Fonts/SpaceGrotesk-Regular.ttf#Space Grotesk");

    private static readonly Avalonia.Media.FontFamily JetBrainsMono =
        new("avares://NotifyIsland/Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono");

    public static Avalonia.Media.FontFamily Resolve(string? id) => AppSettings.NormalizeFontFamily(id) switch
    {
        SpaceGroteskId => SpaceGrotesk,
        JetBrainsMonoId => JetBrainsMono,
        _ => SystemFamily
    };
}
