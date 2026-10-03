namespace NotifyIsland;

/// <summary>What the island shows for a keyboard layout: a country flag and a language name.</summary>
/// <param name="LocaleTag">BCP-47 tag as Windows reports it, e.g. "ru-RU".</param>
/// <param name="Flag">The country's flag as a pair of regional indicator symbols, or "" when the
/// tag carries no usable region. Kept for surfaces that can shape emoji properly.</param>
/// <param name="Region">The two-letter region code, or "" — what the island's badge actually draws,
/// because this Avalonia build does not shape the indicator pair into a flag (verified: a plain
/// emoji renders in colour, the pair falls back to the letters "US" / "RU").</param>
/// <param name="Language">The language's own short name, e.g. "русский".</param>
public sealed record KeyboardLayoutInfo(string LocaleTag, string Flag, string Region, string Language);

/// <summary>
/// Keyboard-layout description and the decision of when to interrupt with it. Pure and in Core
/// so both are testable without a Win32 call: the source only has to hand over a locale tag and
/// an idle time.
/// <para>
/// Why the gate exists at all: the layout changes on every Alt+Shift, and also when the user
/// simply moves the mouse over a language bar. Announcing a language on a hover is noise, and
/// announcing it when nothing is being typed is noise too. The rule is therefore narrow — say
/// something only when the layout ACTUALLY changed while the user is actually typing.
/// </para>
/// </summary>
public static class KeyboardLayoutTag
{
    /// <summary>Regional indicator symbol for an ASCII letter.</summary>
    private const int RegionalIndicatorA = 0x1F1E6;

    /// <summary>
    /// How recently the user must have touched the keyboard for a layout change to be worth
    /// announcing. Long enough to catch a slow switch, short enough that a layout left alone
    /// since yesterday is not "just typed".
    /// </summary>
    public const int TypingWindowMs = 4000;

    /// <summary>
    /// The flag for a two-letter country code, as two regional indicator symbols.
    /// Returns "" for anything that is not two ASCII letters — a layout with no region (or a tag
    /// this code does not understand) must show no flag rather than a wrong one.
    /// </summary>
    public static string FlagFor(string? countryCode)
    {
        if (countryCode is null || countryCode.Length != 2) return string.Empty;
        var a = char.ToUpperInvariant(countryCode[0]);
        var b = char.ToUpperInvariant(countryCode[1]);
        if (a is < 'A' or > 'Z' || b is < 'A' or > 'Z') return string.Empty;

        // U+1F1E6..U+1F1FF is above the BMP, so each indicator is a SURROGATE PAIR: two letters
        // make four chars, not two. char.ConvertFromUtf32 therefore returns a string, and writing
        // it into a two-char span is the bug this shape avoids.
        var left = char.ConvertFromUtf32(RegionalIndicatorA + (a - 'A'));
        var right = char.ConvertFromUtf32(RegionalIndicatorA + (b - 'A'));
        return string.Concat(left, right);
    }

    /// <summary>
    /// Turn a Windows locale tag into a flag and a language name. Returns null when there is
    /// nothing to say, so the caller can leave the island alone rather than showing a blank chip.
    /// </summary>
    public static KeyboardLayoutInfo? Describe(string? localeTag)
    {
        if (string.IsNullOrWhiteSpace(localeTag)) return null;
        var tag = localeTag.Trim();

        // "ru-RU" -> language "ru", region "RU". Windows can also hand back a bare "ru" with no
        // region; that still deserves a language name, just no flag.
        var dash = tag.IndexOf('-');
        var language = dash > 0 ? tag[..dash] : dash == 0 ? string.Empty : tag;
        var region = dash > 0 && tag.Length > dash + 1 ? tag[(dash + 1)..] : string.Empty;

        // The language subtag must be letters. A leading "-" is malformed, and anything with
        // digits or punctuation in it is not a tag we should turn into a language name.
        if (language.Length is 0 or > 8) return null;
        foreach (var ch in language)
            if (!char.IsAsciiLetter(ch)) return null;

        var display = language;
        try
        {
            // NativeName, trimmed of a trailing " (Region)". NativeName keeps the user in their
            // own language — "русский" for ru-RU, which EnglishName would not — but on this
            // machine ICU hands back "English (United States)" for en-US rather than "English".
            // A badge beside a flag that already says US does not need the region spelled out,
            // and the parenthetical is the one part that is always safe to drop.
            var culture = new System.Globalization.CultureInfo(tag);
            display = TrimRegionSuffix(culture.NativeName);
            if (display.Length == 0) display = TrimRegionSuffix(culture.EnglishName);
            if (display.Length == 0) display = language;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // A tag this runtime does not know. The raw subtag is still more useful than nothing.
        }

        return new KeyboardLayoutInfo(tag, FlagFor(region), NormalizeRegion(region), display);
    }

    /// <summary>Upper-case the region when it is really a country code, "" otherwise.</summary>
    private static string NormalizeRegion(string region) =>
        FlagFor(region).Length == 0 ? string.Empty : region.ToUpperInvariant();

    /// <summary>
    /// Drop a trailing " (…)" from a culture name. "English (United States)" becomes "English",
    /// "Deutsch (Deutschland)" becomes "Deutsch", and "русский" — which has no such tail — is
    /// returned untouched. Only a SUFFIX is removed, so a name that legitimately contains
    /// parentheses elsewhere is not mangled.
    /// </summary>
    private static string TrimRegionSuffix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var text = name.Trim();
        if (!text.EndsWith(')')) return text;
        var open = text.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 ? text[..open].TrimEnd() : text;
    }

    /// <summary>
    /// Decides when a layout change is worth showing, and refuses to show the same one twice.
    /// <para>
    /// Priming matters: the very first observation is the layout that is already active, not a
    /// change, and announcing it would put a flag on screen every time the island starts.
    /// </para>
    /// </summary>
    public sealed class Gate
    {
        private string _current = "";

        /// <summary>The layout the gate last reported or accepted, "" before the first call.</summary>
        public string Current => _current;

        /// <summary>
        /// Feed the active layout and how long the user has been idle. Returns what to show, or
        /// null to show nothing.
        /// </summary>
        /// <param name="observed">The layout now, or null if it could not be read this tick.</param>
        /// <param name="idleMs">Milliseconds since the last keyboard input, or int.MaxValue when
        /// unknown. An unknown idle time is treated as "not typing" — showing a language on a
        /// guess is worse than showing it half a second late.</param>
        public KeyboardLayoutInfo? Observe(KeyboardLayoutInfo? observed, int idleMs)
        {
            if (observed is null) return null;

            var tag = observed.LocaleTag;
            if (_current.Length == 0)
            {
                // Priming: adopt silently.
                _current = tag;
                return null;
            }

            if (string.Equals(tag, _current, StringComparison.OrdinalIgnoreCase))
                return null;   // nothing moved

            // A layout that moves with no typing is a hover, or a switch made by a script.
            if (idleMs < 0 || idleMs > TypingWindowMs)
            {
                _current = tag;
                return null;
            }

            _current = tag;
            return observed;
        }

        /// <summary>Forget the primed layout, so the next observation primes again.</summary>
        public void Reset() => _current = "";
    }
}
