using Xunit;

namespace NotifyIsland.Tests;

public class DigitalClockGlyphsTests
{
    [Theory]
    [InlineData('0', "digital0")]
    [InlineData('1', "digital1")]
    [InlineData('2', "digital2")]
    [InlineData('3', "digital3")]
    [InlineData('4', "digital4")]
    [InlineData('5', "digital5")]
    [InlineData('6', "digital6")]
    [InlineData('7', "digital7")]
    [InlineData('8', "digital8")]
    [InlineData('9', "digital9")]
    [InlineData(':', "digital-colon")]
    [InlineData('.', "digital-dot")]
    public void AssetKey_MapsDigitsColonDot(char c, string key) =>
        Assert.Equal(key, DigitalClockGlyphs.AssetKey(c));

    [Theory]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('H')]
    public void AssetKey_Unknown_ReturnsNull(char c) =>
        Assert.Null(DigitalClockGlyphs.AssetKey(c));

    [Fact]
    public void FormatTime_WithoutSeconds_IsHHmm()
    {
        var dt = new DateTime(2026, 9, 24, 14, 5, 9);
        Assert.Equal("14:05", DigitalClockGlyphs.FormatTime(dt, includeSeconds: false));
    }

    [Fact]
    public void FormatTime_WithSeconds_IsHHmmss()
    {
        var dt = new DateTime(2026, 9, 24, 3, 7, 8);
        Assert.Equal("03:07:08", DigitalClockGlyphs.FormatTime(dt, includeSeconds: true));
    }

    [Fact]
    public void GlyphChars_ExpandsFormattedTime()
    {
        var chars = DigitalClockGlyphs.GlyphChars("14:32");
        Assert.Equal(5, chars.Count);
        Assert.Equal(new[] { '1', '4', ':', '3', '2' }, chars);
        Assert.All(chars, c => Assert.NotNull(DigitalClockGlyphs.AssetKey(c)));
    }

    [Fact]
    public void GlyphChars_WithSeconds_HasTwoColons()
    {
        var chars = DigitalClockGlyphs.GlyphChars("09:08:07");
        Assert.Equal(8, chars.Count);
        Assert.Equal(2, chars.Count(DigitalClockGlyphs.IsColon));
    }

    [Fact]
    public void WidthFactor_ColonIsHalf()
    {
        Assert.Equal(1.0, DigitalClockGlyphs.WidthFactor('5'));
        Assert.Equal(0.5, DigitalClockGlyphs.WidthFactor(':'));
        Assert.Equal(0.5, DigitalClockGlyphs.WidthFactor('.'));
    }

    [Fact]
    public void PackFolder_IsFontAudio() =>
        Assert.Equal("FontAudio", DigitalClockGlyphs.PackFolder);
}
