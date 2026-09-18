using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NotifyIsland;

internal static class Ui
{
    private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public static string Lang { get; private set; } = "ru";

    public static void Apply(string? pref)
    {
        Lang = Resolve(pref);
        _map = Read(Lang);
        if (_map.Count == 0 && Lang != "en") _map = Read("en");
    }

    public static string T(string key)
    {
        if (_map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return key;
    }

    private static string Resolve(string? pref)
    {
        var p = (pref ?? "ru").Trim().ToLowerInvariant();
        if (p is "en" or "english") return "en";
        if (p is "system" or "auto")
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        return "ru";
    }

    private static Dictionary<string, string> Read(string lang)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "loc", lang + ".json");
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(path);
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
