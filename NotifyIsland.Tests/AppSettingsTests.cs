using Xunit;

namespace NotifyIsland.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var s = new AppSettings();
        Assert.True(s.WeatherEnabled);
        Assert.True(s.ShowNowPlaying);
        Assert.True(s.ShowBatteryAlerts);
        Assert.False(s.ShowBatteryInCollapsed);
        Assert.True(s.TimerEnabled);
        Assert.Equal(IslandTimerLogic.DefaultPresetMinutes, s.TimerDefaultMinutes);
        Assert.False(s.TimerStopwatchMode);
        Assert.Equal(BatteryAlertLogic.DefaultLowPercent, s.LowBatteryPercent);
        Assert.False(s.AllowDrag);
        Assert.True(s.IslandVisible);
        Assert.True(s.SoundEnabled);
        Assert.Equal(SoundPack.Nothing, s.SoundPack);
        Assert.Equal(AnimationSpeed.Slow, s.AnimationSpeed);
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
        Assert.Equal("#080808", s.ColorCapsuleFill);
        Assert.Equal("#3D9CF0", s.ColorAccent);
        Assert.Equal("#FFFFFF", s.ColorTextPrimary);
        Assert.Equal("#C8C8CC", s.ColorTextSecondary);
        Assert.Equal("IslandIcons", s.IconPack);
        Assert.Equal(12, s.FontSize);
        Assert.Equal("System", s.FontFamily);
        Assert.Equal(AnimationSpeed.Slow, s.AnimMorphInflate);
        Assert.Equal(AnimationSpeed.Slow, s.AnimMorphCollapse);
        Assert.Equal(NotifyAppearStyle.Bounce, s.AppearStyle);
        Assert.Equal(NotifyDismissStyle.Ragged, s.DismissStyle);
        Assert.True(s.AnimPulseEnabled);
        Assert.True(s.AnimBreathEnabled);
        Assert.Equal(DateFormat.DayMonth, s.DateFormat);
        Assert.Equal(ThemePreset.Custom, s.ThemePreset);
        Assert.Equal(WeatherLocationMode.Windows, s.WeatherLocationMode);
        Assert.Equal("Москва", s.WeatherLocationName);
    }

    [Fact]
    public void Json_RoundTrip_PreservesEnumsAndNewFields()
    {
        var s = new AppSettings
        {
            WeatherEnabled = false,
            ShowNowPlaying = false,
            ShowBatteryAlerts = false,
            ShowBatteryInCollapsed = true,
            TimerEnabled = false,
            TimerDefaultMinutes = 25,
            TimerStopwatchMode = true,
            LowBatteryPercent = 12,
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
            AnimationSpeed = AnimationSpeed.Slow,
            ColorCapsuleFill = "#101010",
            ColorAccent = "#FF8800",
            ColorTextPrimary = "#EEEEEE",
            ColorTextSecondary = "#AAAAAA",
            IconPack = "Tabler",
            FontSize = 14,
            FontFamily = "SpaceGrotesk",
            AnimMorphInflate = AnimationSpeed.Fast,
            AnimMorphCollapse = AnimationSpeed.Slow,
            AnimUnreadPulse = AnimationSpeed.Fast,
            AnimIdleBreath = AnimationSpeed.Off,
            AnimHover = AnimationSpeed.Fast,
            AnimSwipeRubber = AnimationSpeed.Slow,
            AnimPulseEnabled = false,
            AnimBreathEnabled = true,
            AppearStyle = NotifyAppearStyle.Pop,
            DismissStyle = NotifyDismissStyle.Glitch,
            DateFormat = DateFormat.Numeric,
            ThemePreset = ThemePreset.Ocean,
            WeatherLocationMode = WeatherLocationMode.Manual,
            WeatherLocationName = "Санкт-Петербург",
            SettingsWindowX = 100,
            SettingsWindowY = 200,
            SettingsWindowWidth = 500,
            SettingsWindowHeight = 700
        };
        var json = s.ToJson();
        var back = AppSettings.FromJson(json);
        Assert.NotNull(back);
        Assert.False(back!.WeatherEnabled);
        Assert.False(back.ShowNowPlaying);
        Assert.False(back.ShowBatteryAlerts);
        Assert.True(back.ShowBatteryInCollapsed);
        Assert.False(back.TimerEnabled);
        Assert.Equal(25, back.TimerDefaultMinutes);
        Assert.True(back.TimerStopwatchMode);
        Assert.Equal(12, back.LowBatteryPercent);
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
        Assert.Equal(AnimationSpeed.Slow, back.AnimationSpeed);
        Assert.Equal("#101010", back.ColorCapsuleFill);
        Assert.Equal("#FF8800", back.ColorAccent);
        Assert.Equal("#EEEEEE", back.ColorTextPrimary);
        Assert.Equal("#AAAAAA", back.ColorTextSecondary);
        Assert.Equal("Tabler", back.IconPack);
        Assert.Equal(14, back.FontSize);
        Assert.Equal("SpaceGrotesk", back.FontFamily);
        Assert.Equal(AnimationSpeed.Fast, back.AnimMorphInflate);
        Assert.Equal(AnimationSpeed.Slow, back.AnimMorphCollapse);
        Assert.Equal(AnimationSpeed.Fast, back.AnimUnreadPulse);
        Assert.Equal(AnimationSpeed.Off, back.AnimIdleBreath);
        Assert.Equal(AnimationSpeed.Fast, back.AnimHover);
        Assert.Equal(AnimationSpeed.Slow, back.AnimSwipeRubber);
        Assert.False(back.AnimPulseEnabled);
        Assert.True(back.AnimBreathEnabled);
        Assert.Equal(NotifyAppearStyle.Pop, back.AppearStyle);
        Assert.Equal(NotifyDismissStyle.Glitch, back.DismissStyle);
        Assert.Equal(DateFormat.Numeric, back.DateFormat);
        Assert.Equal(ThemePreset.Ocean, back.ThemePreset);
        Assert.Equal(WeatherLocationMode.Manual, back.WeatherLocationMode);
        Assert.Equal("Санкт-Петербург", back.WeatherLocationName);
        Assert.False(back.AllowDrag);
        Assert.Equal(100, back.SettingsWindowX);
        Assert.Equal(200, back.SettingsWindowY);
        Assert.Equal(500, back.SettingsWindowWidth);
        Assert.Equal(700, back.SettingsWindowHeight);
    }

    [Fact]
    public void Normalize_ClampsOpacityAndVolume()
    {
        var s = new AppSettings { Opacity = 0.1, SoundVolume = 2.0, SoundVolHover = 3.0, FontSize = 99, LowBatteryPercent = 99 };
        s.Normalize();
        Assert.Equal(0.35, s.Opacity);
        Assert.Equal(1.0, s.SoundVolume);
        Assert.Equal(1.0, s.SoundVolHover);
        Assert.Equal(18, s.FontSize);
        Assert.Equal(50, s.LowBatteryPercent);
    }

    [Fact]
    public void Normalize_ForcesAllowDragFalse_AndHexColors()
    {
        var s = new AppSettings
        {
            AllowDrag = true,
            ColorCapsuleFill = "080808",
            ColorAccent = "notahex",
            IconPack = "  ",
            FontFamily = "space grotesk"
        };
        s.Normalize();
        Assert.False(s.AllowDrag);
        Assert.Equal("#080808", s.ColorCapsuleFill);
        Assert.Equal("#3D9CF0", s.ColorAccent);
        Assert.Equal("IslandIcons", s.IconPack);
        Assert.Equal("SpaceGrotesk", s.FontFamily);
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
            SoundVolSwipe = 0.33,
            AnimationSpeed = AnimationSpeed.Fast,
            ColorAccent = "#112233",
            IconPack = "Lucide",
            FontSize = 16,
            FontFamily = "JetBrainsMono",
            AnimHover = AnimationSpeed.Slow,
            AnimPulseEnabled = false,
            AppearStyle = NotifyAppearStyle.SlideDown,
            DismissStyle = NotifyDismissStyle.SlideUp,
            DateFormat = DateFormat.FullShort,
            ThemePreset = ThemePreset.NothingDark,
            WeatherLocationMode = WeatherLocationMode.Manual,
            WeatherLocationName = "Казань",
            ShowNowPlaying = false,
            ShowBatteryAlerts = false,
            ShowBatteryInCollapsed = true,
            TimerEnabled = false,
            TimerDefaultMinutes = 10,
            TimerStopwatchMode = true,
            LowBatteryPercent = 12,
            AllowDrag = true
        };
        var b = new AppSettings();
        a.CopyTo(b);
        Assert.Equal(IslandEdge.Bottom, b.Edge);
        Assert.Equal(0.7, b.Opacity);
        Assert.Equal(0.2, b.SoundVolume);
        Assert.Equal(SoundPack.System, b.SoundPack);
        Assert.Equal(0.33, b.SoundVolSwipe);
        Assert.Equal(AnimationSpeed.Fast, b.AnimationSpeed);
        Assert.Equal("#112233", b.ColorAccent);
        Assert.Equal("Lucide", b.IconPack);
        Assert.Equal(16, b.FontSize);
        Assert.Equal("JetBrainsMono", b.FontFamily);
        Assert.Equal(AnimationSpeed.Slow, b.AnimHover);
        Assert.False(b.AnimPulseEnabled);
        Assert.Equal(NotifyAppearStyle.SlideDown, b.AppearStyle);
        Assert.Equal(NotifyDismissStyle.SlideUp, b.DismissStyle);
        Assert.Equal(DateFormat.FullShort, b.DateFormat);
        Assert.Equal(ThemePreset.NothingDark, b.ThemePreset);
        Assert.Equal(WeatherLocationMode.Manual, b.WeatherLocationMode);
        Assert.Equal("Казань", b.WeatherLocationName);
        Assert.False(b.ShowNowPlaying);
        Assert.False(b.TimerEnabled);
        Assert.Equal(10, b.TimerDefaultMinutes);
        Assert.True(b.TimerStopwatchMode);
        Assert.False(b.AllowDrag);
    }

    [Fact]
    public void IconPack_Meteocons_RoundTrip()
    {
        foreach (var pack in MeteoconsMap.PackIds)
        {
            var s = new AppSettings { IconPack = pack };
            s.Normalize();
            Assert.Equal(pack, s.IconPack);
            var json = System.Text.Json.JsonSerializer.Serialize(s);
            var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
            back.Normalize();
            Assert.Equal(pack, back.IconPack);
        }
    }
}
