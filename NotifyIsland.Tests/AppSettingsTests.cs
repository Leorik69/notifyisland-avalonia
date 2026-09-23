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
        Assert.Equal(SoundPack.Nothing, s.SoundPack);
        Assert.Equal(WeatherSide.Right, s.WeatherSide);
        Assert.Equal(ZOrderMode.Topmost, s.ZOrderMode);
        Assert.Equal(IslandEdge.Top, s.Edge);
        Assert.Equal(IslandOrientation.Auto, s.Orientation);
        Assert.Equal(0, s.OffsetX);
        Assert.Equal(0, s.OffsetY);
        Assert.Equal(1.0, s.Opacity);
        Assert.InRange(s.SoundVolume, 0.0, 1.0);
        Assert.Equal(1.0, s.SoundVolNotify);
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
            SoundPack = SoundPack.Ios,
            SoundVolume = 0.8,
            SoundVolNotify = 0.9,
            SoundVolExpand = 0.6,
            SoundVolCollapse = 0.5,
            SoundVolSwipe = 0.4,
            SoundVolError = 0.95,
            SoundVolHover = 0.2,
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
        Assert.Equal(SoundPack.Ios, back.SoundPack);
        Assert.Equal(0.8, back.SoundVolume);
        Assert.Equal(0.9, back.SoundVolNotify);
        Assert.Equal(0.6, back.SoundVolExpand);
        Assert.Equal(0.5, back.SoundVolCollapse);
        Assert.Equal(0.4, back.SoundVolSwipe);
        Assert.Equal(0.95, back.SoundVolError);
        Assert.Equal(0.2, back.SoundVolHover);
        Assert.Equal(100, back.SettingsWindowX);
        Assert.Equal(200, back.SettingsWindowY);
        Assert.Equal(500, back.SettingsWindowWidth);
        Assert.Equal(700, back.SettingsWindowHeight);
    }

    [Fact]
    public void Normalize_ClampsOpacityAndVolume()
    {
        var s = new AppSettings { Opacity = 0.1, SoundVolume = 2.0, SoundVolHover = 3.0 };
        s.Normalize();
        Assert.Equal(0.35, s.Opacity);
        Assert.Equal(1.0, s.SoundVolume);
        Assert.Equal(1.0, s.SoundVolHover);
    }

    [Fact]
    public void CopyTo_CopiesAll()
    {
        var a = new AppSettings
        {
            Edge = IslandEdge.Bottom,
            Opacity = 0.7,
            SoundVolume = 0.2,
            SoundPack = SoundPack.System,
            SoundVolSwipe = 0.33
        };
        var b = new AppSettings();
        a.CopyTo(b);
        Assert.Equal(IslandEdge.Bottom, b.Edge);
        Assert.Equal(0.7, b.Opacity);
        Assert.Equal(0.2, b.SoundVolume);
        Assert.Equal(SoundPack.System, b.SoundPack);
        Assert.Equal(0.33, b.SoundVolSwipe);
    }
}
