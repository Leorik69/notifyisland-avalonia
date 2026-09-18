namespace NotifyIsland;

internal static class TypeScale
{
    public static string Normalize(string? id) => (id ?? "").Trim().ToLowerInvariant() switch
    {
        "small" or "s" => "small",
        "large" or "l" => "large",
        _ => "medium"
    };

    public static double TextPx(string? id) => Normalize(id) switch
    {
        "small" => 11,
        "large" => 15,
        _ => 13
    };

    public static double GlyphPx(string? id) => Normalize(id) switch
    {
        "small" => 12,
        "large" => 16,
        _ => 14
    };
}
