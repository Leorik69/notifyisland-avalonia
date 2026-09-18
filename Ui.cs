using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using NotifyIsland.Core;

namespace NotifyIsland;

internal static class Ui
{
    private static readonly ResourceManager Resx = new("NotifyIsland.Loc.Strings", Assembly.GetExecutingAssembly());
    private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private static CultureInfo _culture = new("ru");

    public static string Lang { get; private set; } = "ru";

    public static void Apply(string? pref)
    {
        Lang = Resolve(pref);
        _culture = new CultureInfo(Lang == "en" ? "en" : "ru");
        CultureInfo.CurrentUICulture = _culture;
        _map = Read(Lang);
        if (_map.Count == 0 && Lang != "en") _map = Read("en");
    }

    public static string T(string key)
    {
        try
        {
            var fromResx = Resx.GetString(key, _culture);
            if (!string.IsNullOrWhiteSpace(fromResx)) return fromResx;
        }
        catch (Exception ex)
        {
            IslandLog.Write("loc", ex.Message);
        }
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
        catch (Exception ex)
        {
            IslandLog.Write("loc", ex.Message);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
