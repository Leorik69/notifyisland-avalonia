using Xunit;

namespace NotifyIsland.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var s = new AppSettings();
        Assert.True(s.WeatherEnabled);
        Assert.True(s.AllowDrag);
        Assert.True(s.IslandVisible);
        Assert.True(s.SoundEnabled);
        Assert.Equal(WeatherSide.Right, s.WeatherSide);
        Assert.Equal(ZOrderMode.Topmost, s.ZOrderMode);
        Assert.Equal(IslandEdge.Top, s.Edge);
        Assert.Equal(IslandOrientation.Auto, s.Orientation);
        Assert.Equal(0, s.OffsetX);
        Assert.Equal(0, s.OffsetY);
        Assert.Equal(1.0, s.Opacity);
        Assert.InRange(s.SoundVolume, 0.0, 1.0);
        Assert.Equal(55.75, s.Latitude);
        Assert.Equal(37.62, s.Longitude);
    }

    [Fact]
    public void Json_RoundTrip_PreservesEnumsAndNewFields()
    {
        var s = new AppSettings
        {
            WeatherEnabled = false,
            WeatherSide = WeatherSide.Left,
            ZOrderMode = ZOrderMode.Desktop,
            Edge = IslandEdge.Right,
            OffsetX = 12,
            OffsetY = -4,
            Orientation = IslandOrientation.Vertical,
            AllowDrag = false,
            IslandVisible = false,
            Opacity = 0.55,
            SoundEnabled = false,
            SoundVolume = 0.8,
            SettingsWindowX = 100,
            SettingsWindowY = 200,
            SettingsWindowWidth = 500,
            SettingsWindowHeight = 700
        };
        var json = s.ToJson();
        var back = AppSettings.FromJson(json);
        Assert.NotNull(back);
        Assert.False(back!.WeatherEnabled);
        Assert.Equal(WeatherSide.Left, back.WeatherSide);
        Assert.Equal(ZOrderMode.Desktop, back.ZOrderMode);
        Assert.Equal(IslandEdge.Right, back.Edge);
        Assert.Equal(12, back.OffsetX);
        Assert.Equal(-4, back.OffsetY);
        Assert.Equal(IslandOrientation.Vertical, back.Orientation);
        Assert.False(back.AllowDrag);
        Assert.False(back.IslandVisible);
        Assert.Equal(0.55, back.Opacity);
        Assert.False(back.SoundEnabled);
        Assert.Equal(0.8, back.SoundVolume);
        Assert.Equal(100, back.SettingsWindowX);
        Assert.Equal(200, back.SettingsWindowY);
        Assert.Equal(500, back.SettingsWindowWidth);
        Assert.Equal(700, back.SettingsWindowHeight);
    }

    [Fact]
    public void Normalize_ClampsOpacityAndVolume()
    {
        var s = new AppSettings { Opacity = 0.1, SoundVolume = 2.0 };
        s.Normalize();
        Assert.Equal(0.35, s.Opacity);
        Assert.Equal(1.0, s.SoundVolume);
    }

    [Fact]
    public void CopyTo_CopiesAll()
    {
        var a = new AppSettings { Edge = IslandEdge.Bottom, Opacity = 0.7, SoundVolume = 0.2 };
        var b = new AppSettings();
        a.CopyTo(b);
        Assert.Equal(IslandEdge.Bottom, b.Edge);
        Assert.Equal(0.7, b.Opacity);
        Assert.Equal(0.2, b.SoundVolume);
    }
}
