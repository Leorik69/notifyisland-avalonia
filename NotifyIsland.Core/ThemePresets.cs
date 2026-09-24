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
}
