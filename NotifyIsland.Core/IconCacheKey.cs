using System;

namespace NotifyIsland;

/// <summary>
/// Cache key for a parsed icon source in <c>IconPackService</c>.
///
/// The <see cref="SvgSource"/> cache itself lives in the app layer because it needs Avalonia's
/// Skia-backed <c>SvgSource</c>, which Core must not reference. The key is the part that carries a
/// decision, so it is here where a test can reach it.
///
/// The decision: the tint hex is baked into the SVG XML before parsing, but ONLY for the
/// Meteocons monochrome pack. The other three Meteocons packs use <c>currentColor</c>, which Skia
/// resolves as a paint-time tint that does not alter the parsed source — so including the hex
/// there would just grow the cache with duplicate entries for every theme the user cycles through.
/// Outline packs (Tabler / Lucide) do substitute <c>currentColor</c> into the XML, so for those
/// the hex must stay in the key.
/// </summary>
public static class IconCacheKey
{
    /// <summary>
    /// Whether the tint hex has to be part of the key for this pack.
    ///
    /// <c>true</c> for the outline packs and for Meteocons monochrome: all of them substitute
    /// <c>currentColor</c> (or a baked black fill) into the XML before parsing, so two tints
    /// really are two different parsed sources. <c>false</c> for the other three Meteocons packs,
    /// which leave <c>currentColor</c> for Skia to resolve as a paint-time tint — their parsed
    /// source is identical for every theme, so keying on the hex would store the same SVG again
    /// for each theme the user cycles through.
    /// </summary>
    public static bool HexAffectsSource(string? pack) =>
        !MeteoconsMap.IsMeteoconsPack(pack)
        || string.Equals(pack, MeteoconsMap.Monochrome, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Build the cache key. <paramref name="hex"/> is ignored for Meteocons non-monochrome packs.
    /// Size is rounded to two decimals so a caller passing 15.999999 does not miss the entry
    /// another caller populated at 16.
    /// </summary>
    public static string For(string pack, string key, string? hex, double size) =>
        $"{pack}|{key}|{(HexAffectsSource(pack) ? hex ?? string.Empty : string.Empty)}|{size:0.##}";
}
