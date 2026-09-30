using System;

namespace NotifyIsland;

/// <summary>Stock visual themes + Custom (user-tweaked).</summary>
public enum ThemePreset
{
    /// <summary>Dark Nothing-inspired: near-black fill, blue accent, Space Grotesk, Meteocons Fill.</summary>
    NothingDark,
    /// <summary>Quiet Apple-like: dark gray fill, soft blue, System font, Meteocons Line, slow morph.</summary>
    AppleQuiet,
    /// <summary>Ocean: deep navy, teal accent, JetBrains Mono, Meteocons Flat, faster lean.</summary>
    Ocean,
    /// <summary>User-customized; Вид / Анимации / Иконки stay independently editable.</summary>
    Custom
}

/// <summary>Applies / matches stock theme bundles onto <see cref="AppSettings"/>.</summary>
public static class ThemePresets
{
    public static void Apply(ThemePreset preset, AppSettings target)
    {
        if (preset == ThemePreset.Custom) return;
        target.ThemePreset = preset;
        switch (preset)
        {
            case ThemePreset.NothingDark:
                target.ColorCapsuleFill = "#080808";
                target.ColorAccent = "#3D9CF0";
                target.ColorTextPrimary = "#FFFFFF";
                target.ColorTextSecondary = "#C8C8CC";
                target.FontFamily = "SpaceGrotesk";
                target.FontSize = 12;
                target.IconPack = "MeteoconsFill";
                target.AnimationSpeed = AnimationSpeed.Slow;
                target.AnimMorphInflate = AnimationSpeed.Slow;
                target.AnimMorphCollapse = AnimationSpeed.Slow;
                target.AppearStyle = NotifyAppearStyle.Bounce;
                target.DismissStyle = NotifyDismissStyle.Ragged;
                target.DateFormat = DateFormat.DayMonth;
                target.SoundPack = SoundPack.Nothing;
                break;
            case ThemePreset.AppleQuiet:
                target.ColorCapsuleFill = "#1C1C1E";
                target.ColorAccent = "#0A84FF";
                target.ColorTextPrimary = "#F5F5F7";
                target.ColorTextSecondary = "#A1A1A6";
                target.FontFamily = "System";
                target.FontSize = 12;
                target.IconPack = "MeteoconsLine";
                target.AnimationSpeed = AnimationSpeed.Slow;
                target.AnimMorphInflate = AnimationSpeed.Slow;
                target.AnimMorphCollapse = AnimationSpeed.Normal;
                target.AppearStyle = NotifyAppearStyle.FadeScale;
                target.DismissStyle = NotifyDismissStyle.FadeScaleOut;
                target.DateFormat = DateFormat.WeekdayShort;
                target.SoundPack = SoundPack.Ios;
                break;
            case ThemePreset.Ocean:
                target.ColorCapsuleFill = "#0A1628";
                target.ColorAccent = "#00C2A8";
                target.ColorTextPrimary = "#E8F4FF";
                target.ColorTextSecondary = "#7EB8D4";
                target.FontFamily = "JetBrainsMono";
                target.FontSize = 12;
                target.IconPack = "MeteoconsFlat";
                target.AnimationSpeed = AnimationSpeed.Normal;
                target.AnimMorphInflate = AnimationSpeed.Normal;
                target.AnimMorphCollapse = AnimationSpeed.Fast;
                target.AppearStyle = NotifyAppearStyle.SlideDown;
                target.DismissStyle = NotifyDismissStyle.SlideUp;
                target.DateFormat = DateFormat.Numeric;
                target.SoundPack = SoundPack.Nothing;
                break;
        }
    }

    /// <summary>True when themed fields still match the stock bundle.</summary>
    public static bool Matches(ThemePreset preset, AppSettings s)
    {
        if (preset == ThemePreset.Custom) return true;
        var probe = new AppSettings();
        s.CopyTo(probe);
        Apply(preset, probe);
        return HexEq(s.ColorCapsuleFill, probe.ColorCapsuleFill)
            && HexEq(s.ColorAccent, probe.ColorAccent)
            && HexEq(s.ColorTextPrimary, probe.ColorTextPrimary)
            && HexEq(s.ColorTextSecondary, probe.ColorTextSecondary)
            && string.Equals(s.FontFamily, probe.FontFamily, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(s.FontSize - probe.FontSize) < 0.01
            && string.Equals(s.IconPack, probe.IconPack, StringComparison.OrdinalIgnoreCase)
            && s.AnimationSpeed == probe.AnimationSpeed
            && s.AnimMorphInflate == probe.AnimMorphInflate
            && s.AnimMorphCollapse == probe.AnimMorphCollapse
            && s.AppearStyle == probe.AppearStyle
            && s.DismissStyle == probe.DismissStyle
            && s.DateFormat == probe.DateFormat
            && s.SoundPack == probe.SoundPack;
    }

    /// <summary>If current ThemePreset is stock but fields diverged → Custom.</summary>
    public static void AutodetectCustom(AppSettings s)
    {
        if (s.ThemePreset == ThemePreset.Custom) return;
        if (!Matches(s.ThemePreset, s))
            s.ThemePreset = ThemePreset.Custom;
    }

    private static bool HexEq(string? a, string? b) =>
        string.Equals(
            AppSettings.NormalizeHex(a, ""),
            AppSettings.NormalizeHex(b, ""),
            StringComparison.OrdinalIgnoreCase);

    // -- Material --------------------------------------------------------------------------------
    // A preset used to be nothing but four colours: with ColorCapsuleFill as the only lever,
    // NothingDark, AppleQuiet and Ocean differed by one hex and nothing else. Everything below
    // is derived from what the user already has — no new AppSettings field, no new theme — so the
    // character of a theme lives in how the SAME palette is materialised rather than in extra
    // knobs nobody asked for.

    /// <summary>How a preset materialises one palette: surface weight and hairline presence.</summary>
    /// <param name="FillAlphaScale">Multiplier on the user's Opacity, applied to the surface fill
    /// only. AppleQuiet reads as a lighter material because its wall is more transparent, not
    /// because its slider stops working: the slider value itself is untouched.</param>
    /// <param name="BorderAlpha">Idle hairline, as an alpha over the primary text colour. Taking
    /// the border from the text instead of from white is what makes one hairline rule correct on
    /// a near-black capsule AND on a white one: white-on-white was invisible, white-on-Ocean fought
    /// the accent.</param>
    /// <param name="BorderHoverAlpha">Hover hairline.</param>
    /// <param name="BorderPinnedAlpha">Pinned hairline — the state that must stay legible when
    /// the pointer is gone.</param>
    public readonly record struct SurfaceMaterial(
        double FillAlphaScale,
        double BorderAlpha,
        double BorderHoverAlpha,
        double BorderPinnedAlpha);

    /// <summary>Calm and near-invisible: the resting look of the island.</summary>
    private static readonly SurfaceMaterial Calm = new(1.00, 0.09, 0.22, 0.38);

    /// <summary>Material for a preset. Custom and anything unknown fall back to <see cref="Calm"/>,
    /// because the user picking their own colours gets the neutral rule, not a guess at a style.</summary>
    public static SurfaceMaterial MaterialFor(ThemePreset preset) => preset switch
    {
        // Lighter wall, softer hairline: the same palette reads as a quiet translucent object.
        ThemePreset.AppleQuiet => new SurfaceMaterial(0.90, 0.07, 0.17, 0.30),
        // A touch more present: Ocean's job is to stay recognisable while expanded.
        ThemePreset.Ocean => new SurfaceMaterial(1.00, 0.11, 0.26, 0.44),
        _ => Calm,
    };

    /// <summary>
    /// Contrast ratio (WCAG relative luminance) between two #RRGGBB colours, 1…21.
    /// Parses #RGB / #RRGGBB / #AARRGGBB. If EITHER side cannot be parsed the result is 1 — "no
    /// contrast" — not a guess: mapping garbage to black would score 21 against white and wave a
    /// broken colour through the safeguard below as perfectly readable.
    /// </summary>
    public static double ContrastRatio(string? a, string? b)
    {
        if (!TryRgb(a, out var c1) || !TryRgb(b, out var c2)) return 1.0;
        var l1 = Lum(c1.R, c1.G, c1.B);
        var l2 = Lum(c2.R, c2.G, c2.B);
        var hi = Math.Max(l1, l2);
        var lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>
    /// Contrast below which ink is considered to have merged into the surface. 2.0 is well under
    /// the WCAG AA figure for body text on purpose: the island's ink is 10–13 DIP, and this floor
    /// exists to catch a genuinely broken pair (ink the same colour as its own wall), not to
    /// grade taste. Every stock preset clears it by a wide margin, so it only ever fires on
    /// hand-picked Custom colours.
    /// </summary>
    public const double ReadabilityFloor = 2.0;

    /// <summary>
    /// Safeguard for Custom: returns <paramref name="ink"/> when it stays readable on the given
    /// surface, otherwise an existing token that does. This is the whole of the "protection" —
    /// a single fallback decision against the surface's own lightness, not a colour-correction
    /// pass. Nothing is nudged, brightened or re-mixed; the user's colours are used as given
    /// unless they cannot be read at all.
    /// </summary>
    public static string ReadableInk(string? surfaceHex, string? inkHex, string fallbackHex) =>
        ContrastRatio(surfaceHex, inkHex) >= ReadabilityFloor
            ? AppSettings.NormalizeHex(inkHex, fallbackHex)
            : fallbackHex;

    /// <summary>
    /// Primary ink for a palette, protected. If the primary is readable it is used unchanged;
    /// if not, the fallback is the token that contrasts with THIS surface — dark ink on a light
    /// wall, light ink on a dark one. Deriving the direction from the surface is what keeps a
    /// white capsule from being "fixed" with more white.
    /// </summary>
    public static string GuardedPrimaryInk(string? surfaceHex, string? primaryHex)
    {
        var surface = AppSettings.NormalizeHex(surfaceHex, OverlayTokens.FillHex);
        var ink = AppSettings.NormalizeHex(primaryHex, OverlayTokens.TextHex);
        if (ContrastRatio(surface, ink) >= ReadabilityFloor) return ink;
        return Luminance(surface) > 0.5 ? OverlayTokens.FillHex : OverlayTokens.TextHex;
    }

    /// <summary>
    /// Secondary ink, protected the same way. A secondary that has merged into the surface falls
    /// back to the guarded primary rather than disappearing: the date and the temperature are
    /// information, and "quiet" must not become "gone".
    /// </summary>
    public static string GuardedSecondaryInk(string? surfaceHex, string? secondaryHex, string guardedPrimary)
    {
        var surface = AppSettings.NormalizeHex(surfaceHex, OverlayTokens.FillHex);
        var ink = AppSettings.NormalizeHex(secondaryHex, OverlayTokens.TextSecondaryHex);
        return ContrastRatio(surface, ink) >= ReadabilityFloor ? ink : guardedPrimary;
    }

    /// <summary>
    /// Guarded ink for a tinted icon. The clipboard's per-format tints (#9CC4FF / #C8C8CC /
    /// #7AA8FF) are calibrated for a dark capsule: on a light Custom wall a light-gray outline
    /// is simply not there any more (spec §10). The format tint is kept whenever it stays
    /// readable, and otherwise falls back to the same secondary ink the rest of the capsule uses —
    /// a fallback, not a re-colouring pass: the format is still told apart by its ICON SHAPE.
    /// </summary>
    public static string GuardedIconInk(string? surfaceHex, string? iconHex, string fallbackInk) =>
        ContrastRatio(surfaceHex, iconHex) >= ReadabilityFloor
            ? AppSettings.NormalizeHex(iconHex, fallbackInk)
            : AppSettings.NormalizeHex(fallbackInk, OverlayTokens.TextSecondaryHex);

    /// <summary>Relative luminance 0…1 of a #RRGGBB colour; 0 when it cannot be parsed.</summary>
    public static double Luminance(string? hex) =>
        TryRgb(hex, out var c) ? Lum(c.R, c.G, c.B) : 0.0;

    private static double Lum(double r, double g, double b) =>
        0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);

    private static double Lin(double c)
    {
        c /= 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static bool TryRgb(string? hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        var v = AppSettings.NormalizeHex(hex, "");
        // NormalizeHex keeps #AARRGGBB as-is, so an 8-digit value has to lose its alpha first.
        if (v.Length == 9) v = "#" + v.Substring(3);
        if (v.Length == 4) v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        if (v.Length != 7) return false;
        if (!int.TryParse(v.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!int.TryParse(v.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!int.TryParse(v.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        rgb = (r, g, b);
        return true;
    }
}
