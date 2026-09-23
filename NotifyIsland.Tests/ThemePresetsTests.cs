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
