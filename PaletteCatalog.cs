using Avalonia.Media;

namespace NotifyIsland;

internal sealed record Palette(
    string Id,
    string Name,
    Color Background,
    Color Border,
    Color Accent,
    Color Glow,
    Color Text,
    Color TextSecondary,
    Color Error);

internal static class PaletteCatalog
{
    public static readonly Palette[] All =
    {
        Make("midnight", "Midnight", "#080808", "#3D9CF0", "#3D9CF0", "#FFFFFF", "#C8C8CC", "#E8A0A0"),
        Make("slate", "Slate", "#12141A", "#7AA2F7", "#89B4FA", "#EEF0F4", "#A6ADC8", "#F38BA8"),
        Make("forest", "Forest", "#0E1612", "#3DDC97", "#5EEAD4", "#ECFDF5", "#A7F3D0", "#FCA5A5"),
        Make("ember", "Ember", "#140E0C", "#F97316", "#FB923C", "#FFF7ED", "#FDBA74", "#FCA5A5"),
        Make("violet", "Violet", "#120E18", "#A78BFA", "#C4B5FD", "#F5F3FF", "#DDD6FE", "#F9A8D4"),
        Make("ocean", "Ocean", "#071018", "#38BDF8", "#7DD3FC", "#F0F9FF", "#BAE6FD", "#FCA5A5"),
        Make("sand", "Sand", "#16110C", "#D6A15A", "#E7C27A", "#FFF8EC", "#E8D5B5", "#E8A0A0"),
        Make("graphite", "Graphite", "#1A1A1C", "#9AA0A6", "#C0C4C8", "#F2F2F3", "#B8B8BC", "#E8A0A0"),
        Make("hicontrast", "High contrast", "#000000", "#3D9CF0", "#FFFFFF", "#FFFFFF", "#E0E0E0", "#FF8080"),
        Make("mica-dark", "Mica dark", "#1C1C1C", "#60CDFF", "#60CDFF", "#FFFFFF", "#C8C8CC", "#E8A0A0"),
    };

    public static Palette Default => All[0];

    public static bool TryGet(string id, out Palette palette)
    {
        foreach (var p in All)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                palette = p;
                return true;
            }
        }
        palette = Default;
        return false;
    }

    public static Palette Get(string? id) => TryGet(id ?? "", out var p) ? p : Default;

    public static Color AccentOf(UserPrefs prefs)
    {
        var hex = prefs.AccentHex?.Trim() ?? "";
        if (hex.Length >= 7 && Color.TryParse(hex, out var c)) return c;
        return Get(prefs.PaletteId).Accent;
    }

    private static Palette Make(string id, string name, string bg, string accent, string glow, string text, string text2, string err)
    {
        var background = Color.Parse(bg);
        var accentC = Color.Parse(accent);
        var border = Color.FromArgb(90, accentC.R, accentC.G, accentC.B);
        return new Palette(id, name, background, border, accentC, Color.Parse(glow), Color.Parse(text), Color.Parse(text2), Color.Parse(err));
    }
}

internal sealed record FontChoice(string Id, string Name, string Family);

internal static class FontCatalog
{
    public static readonly FontChoice[] All =
    {
        new("segoe-variable", "Segoe UI Variable", "Segoe UI Variable Display, Segoe UI Variable, Segoe UI"),
        new("segoe", "Segoe UI", "Segoe UI"),
        new("cascadia", "Cascadia Code", "Cascadia Code, Cascadia Mono, Consolas"),
        new("consolas", "Consolas", "Consolas, Cascadia Mono, Segoe UI"),
        new("inter", "Inter", "Inter, Segoe UI Variable, Segoe UI"),
    };

    public static bool TryGet(string id, out FontChoice font)
    {
        foreach (var f in All)
        {
            if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                font = f;
                return true;
            }
        }
        font = All[0];
        return false;
    }

    public static FontChoice Get(string? id) => TryGet(id ?? "", out var f) ? f : All[0];
}
