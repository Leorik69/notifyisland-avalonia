using Xunit;

namespace NotifyIsland.Tests;

public class ThemePresetsTests
{
    [Theory]
    [InlineData(ThemePreset.NothingDark)]
    [InlineData(ThemePreset.AppleQuiet)]
    [InlineData(ThemePreset.Ocean)]
    public void Apply_ThenMatches(ThemePreset preset)
    {
        var s = new AppSettings();
        ThemePresets.Apply(preset, s);
        Assert.Equal(preset, s.ThemePreset);
        Assert.True(ThemePresets.Matches(preset, s));
    }

    [Fact]
    public void NothingDark_ExpectedBundle()
    {
        var s = new AppSettings();
        ThemePresets.Apply(ThemePreset.NothingDark, s);
        Assert.Equal("#080808", s.ColorCapsuleFill);
        Assert.Equal("#3D9CF0", s.ColorAccent);
        Assert.Equal("SpaceGrotesk", s.FontFamily);
        Assert.Equal("MeteoconsFill", s.IconPack);
        Assert.Equal(DateFormat.DayMonth, s.DateFormat);
        Assert.Equal(SoundPack.Nothing, s.SoundPack);
        Assert.Equal(AnimationSpeed.Slow, s.AnimationSpeed);
    }

    [Fact]
    public void AppleQuiet_ExpectedBundle()
    {
        var s = new AppSettings();
        ThemePresets.Apply(ThemePreset.AppleQuiet, s);
        Assert.Equal("#1C1C1E", s.ColorCapsuleFill);
        Assert.Equal("#0A84FF", s.ColorAccent);
        Assert.Equal("System", s.FontFamily);
        Assert.Equal("MeteoconsLine", s.IconPack);
        Assert.Equal(DateFormat.WeekdayShort, s.DateFormat);
        Assert.Equal(SoundPack.Ios, s.SoundPack);
    }

    [Fact]
    public void Ocean_ExpectedBundle()
    {
        var s = new AppSettings();
        ThemePresets.Apply(ThemePreset.Ocean, s);
        Assert.Equal("#0A1628", s.ColorCapsuleFill);
        Assert.Equal("#00C2A8", s.ColorAccent);
        Assert.Equal("JetBrainsMono", s.FontFamily);
        Assert.Equal("MeteoconsFlat", s.IconPack);
        Assert.Equal(DateFormat.Numeric, s.DateFormat);
    }

    [Fact]
    public void AutodetectCustom_OnDiverge()
    {
        var s = new AppSettings();
        ThemePresets.Apply(ThemePreset.NothingDark, s);
        s.ColorAccent = "#FF0000";
        ThemePresets.AutodetectCustom(s);
        Assert.Equal(ThemePreset.Custom, s.ThemePreset);
    }

    [Fact]
    public void AutodetectCustom_KeepsStockWhenMatch()
    {
        var s = new AppSettings();
        ThemePresets.Apply(ThemePreset.Ocean, s);
        ThemePresets.AutodetectCustom(s);
        Assert.Equal(ThemePreset.Ocean, s.ThemePreset);
    }

    [Fact]
    public void Custom_ApplyIsNoOp()
    {
        var s = new AppSettings { ColorAccent = "#112233", ThemePreset = ThemePreset.Custom };
        ThemePresets.Apply(ThemePreset.Custom, s);
        Assert.Equal("#112233", s.ColorAccent);
        Assert.Equal(ThemePreset.Custom, s.ThemePreset);
    }

    // -- Material (stage 7) --------------------------------------------------------------
    // A preset used to be four colours and nothing else, so every theme rendered the same white
    // hairline and the same wall. These pin the material rules that replaced that.

    [Fact]
    public void Material_AppleQuietIsTheLighterWall_AndSofterHairline()
    {
        var calm = ThemePresets.MaterialFor(ThemePreset.NothingDark);
        var quiet = ThemePresets.MaterialFor(ThemePreset.AppleQuiet);
        Assert.True(quiet.FillAlphaScale < calm.FillAlphaScale, "AppleQuiet must read lighter than NothingDark");
        Assert.True(quiet.BorderAlpha < calm.BorderAlpha, "AppleQuiet's hairline must be softer");
        Assert.True(quiet.BorderHoverAlpha < calm.BorderHoverAlpha);
        Assert.True(quiet.BorderPinnedAlpha < calm.BorderPinnedAlpha);
    }

    [Fact]
    public void Material_OceanKeepsTheFullWall_AndShowsTheEdgeMore()
    {
        var calm = ThemePresets.MaterialFor(ThemePreset.NothingDark);
        var ocean = ThemePresets.MaterialFor(ThemePreset.Ocean);
        Assert.Equal(1.0, ocean.FillAlphaScale, 3);
        Assert.True(ocean.BorderAlpha > calm.BorderAlpha, "Ocean's edge must stay readable while expanded");
    }

    [Fact]
    public void Material_CustomGetsTheNeutralRule()
    {
        var calm = ThemePresets.MaterialFor(ThemePreset.NothingDark);
        var custom = ThemePresets.MaterialFor(ThemePreset.Custom);
        Assert.Equal(calm.FillAlphaScale, custom.FillAlphaScale, 3);
        Assert.Equal(calm.BorderAlpha, custom.BorderAlpha, 3);
    }

    [Theory]
    [InlineData(ThemePreset.NothingDark)]
    [InlineData(ThemePreset.AppleQuiet)]
    [InlineData(ThemePreset.Ocean)]
    public void Material_HairlineNeverOutshinesItsOwnInk(ThemePreset preset)
    {
        var s = new AppSettings();
        ThemePresets.Apply(preset, s);
        var m = ThemePresets.MaterialFor(preset);
        Assert.InRange(m.BorderAlpha, 0.0, m.BorderHoverAlpha);
        Assert.InRange(m.BorderHoverAlpha, 0.0, m.BorderPinnedAlpha);
        // A hairline is decoration on top of the capsule, not a light source: the spec caps its
        // presence well below anything the text itself uses (spec §7, "не ярче текста").
        Assert.True(m.BorderPinnedAlpha <= 0.5, $"pinned hairline alpha {m.BorderPinnedAlpha} is too loud");
    }

    [Theory]
    [InlineData(ThemePreset.NothingDark)]
    [InlineData(ThemePreset.AppleQuiet)]
    [InlineData(ThemePreset.Ocean)]
    public void Safeguard_StockPalettesAreNeverRewritten(ThemePreset preset)
    {
        // The guard must be inert for the shipped bundles — if it fired on stock colours, a theme
        // would silently render something the user never asked for.
        var s = new AppSettings();
        ThemePresets.Apply(preset, s);
        Assert.Equal(s.ColorTextPrimary, ThemePresets.GuardedPrimaryInk(s.ColorCapsuleFill, s.ColorTextPrimary));
        Assert.Equal(
            s.ColorTextSecondary,
            ThemePresets.GuardedSecondaryInk(s.ColorCapsuleFill, s.ColorTextSecondary, s.ColorTextPrimary));
    }

    [Fact]
    public void Safeguard_InkOnItsOwnWallFallsBackToDarkOnALightSurface()
    {
        // A user who picks a white capsule and forgets the text is not allowed an invisible island:
        // white ink on a white wall is "fixed" with the dark token, because the surface is the
        // light one. Falling back to more white here is the bug this rule exists to prevent.
        var guarded = ThemePresets.GuardedPrimaryInk("#FFFFFF", "#FFFFFF");
        Assert.NotEqual("#FFFFFF", guarded);
        Assert.True(ThemePresets.ContrastRatio("#FFFFFF", guarded) >= ThemePresets.ReadabilityFloor);
        Assert.Equal(OverlayTokens.FillHex, guarded);
    }

    [Fact]
    public void Safeguard_InkOnItsOwnDarkWallFallsBackToLight()
    {
        var guarded = ThemePresets.GuardedPrimaryInk("#080808", "#080808");
        Assert.Equal(OverlayTokens.TextHex, guarded);
    }

    [Fact]
    public void Safeguard_ReadableSecondaryIsKept()
    {
        Assert.Equal("#C8C8CC", ThemePresets.GuardedSecondaryInk("#080808", "#C8C8CC", "#FFFFFF"));
    }

    [Fact]
    public void Safeguard_SecondaryThatMergedWithTheSurfaceFallsBackToPrimary()
    {
        // The date and the temperature are information: quiet must not become invisible.
        var guarded = ThemePresets.GuardedSecondaryInk("#080808", "#0A0A0A", "#FFFFFF");
        Assert.Equal("#FFFFFF", guarded);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    public void ContrastRatio_KnownPairs(string a, string b, double expected)
    {
        Assert.Equal(expected, ThemePresets.ContrastRatio(a, b), 2);
    }

    [Fact]
    public void ContrastRatio_ShortAndAlphaHexesParse()
    {
        // settings.json is hand-editable, so #RGB and #AARRGGBB have to be understood, not crash.
        Assert.Equal(ThemePresets.ContrastRatio("#000", "#FFF"), ThemePresets.ContrastRatio("#000000", "#FFFFFF"), 2);
        Assert.True(ThemePresets.ContrastRatio("#FF101010", "#FFFFFFFF") > 10.0);
    }

    [Fact]
    public void ContrastRatio_UnparseableInputIsTreatedAsBroken()
    {
        // "Assume broken" makes the safeguard do something, which is the safe direction: a garbage
        // colour must not silently pass as readable.
        Assert.True(ThemePresets.ContrastRatio("nonsense", "#FFFFFF") < ThemePresets.ReadabilityFloor);
    }

    [Fact]
    public void Json_RoundTrip_ThemeAndDateAndLocation()
    {
        var s = new AppSettings
        {
            ThemePreset = ThemePreset.AppleQuiet,
            DateFormat = DateFormat.FullShort,
            WeatherLocationMode = WeatherLocationMode.Manual,
            WeatherLocationName = "Казань",
            Latitude = 55.8,
            Longitude = 49.11
        };
        ThemePresets.Apply(ThemePreset.AppleQuiet, s);
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.Equal(ThemePreset.AppleQuiet, back!.ThemePreset);
        Assert.Equal(DateFormat.WeekdayShort, back.DateFormat); // Apply set WeekdayShort
        Assert.Equal(WeatherLocationMode.Manual, back.WeatherLocationMode);
        Assert.Equal("Казань", back.WeatherLocationName);
    }
}
