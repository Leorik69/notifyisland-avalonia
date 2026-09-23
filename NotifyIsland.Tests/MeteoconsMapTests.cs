using Xunit;

namespace NotifyIsland.Tests;

public class MeteoconsMapTests
{
    [Theory]
    [InlineData("MeteoconsFill", true)]
    [InlineData("MeteoconsFlat", true)]
    [InlineData("MeteoconsLine", true)]
    [InlineData("MeteoconsMonochrome", true)]
    [InlineData("Tabler", false)]
    [InlineData("IslandIcons", false)]
    [InlineData(null, false)]
    public void IsMeteoconsPack_DetectsFourStyles(string? pack, bool expected) =>
        Assert.Equal(expected, MeteoconsMap.IsMeteoconsPack(pack));

    [Fact]
    public void UpstreamSlug_CoversWeatherCodesKeys()
    {
        foreach (var code in new[] { 0, 2, 3, 45, 51, 61, 71, 95 })
        {
            var key = WeatherCodes.IconKey(code);
            Assert.True(MeteoconsMap.UpstreamSlug.ContainsKey(key), $"missing slug for {key} (code {code})");
        }
        Assert.Equal("clear-day", MeteoconsMap.UpstreamSlug["weather-clear"]);
        Assert.Equal("thunderstorms", MeteoconsMap.UpstreamSlug["weather-storm"]);
        Assert.Equal("partly-cloudy-day", MeteoconsMap.UpstreamSlug["weather-partly"]);
    }

    [Fact]
    public void StyleFolder_MapsPackIds()
    {
        Assert.Equal("fill", MeteoconsMap.StyleFolder(MeteoconsMap.Fill));
        Assert.Equal("flat", MeteoconsMap.StyleFolder(MeteoconsMap.Flat));
        Assert.Equal("line", MeteoconsMap.StyleFolder(MeteoconsMap.Line));
        Assert.Equal("monochrome", MeteoconsMap.StyleFolder(MeteoconsMap.Monochrome));
        Assert.Null(MeteoconsMap.StyleFolder("Tabler"));
    }

    [Fact]
    public void PackIds_AreExactlyFour()
    {
        Assert.Equal(4, MeteoconsMap.PackIds.Length);
        Assert.Contains(MeteoconsMap.Fill, MeteoconsMap.PackIds);
        Assert.Contains(MeteoconsMap.Monochrome, MeteoconsMap.PackIds);
    }
}
