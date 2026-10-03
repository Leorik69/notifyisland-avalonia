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
        Assert.Equal(DateFormat.DayMonth, s.DateFormat);
        Assert.True(s.DigitalClockEnabled);
        Assert.False(s.ShowClockSeconds);
        Assert.True(s.ShowSecondsStrip);
        Assert.True(s.HoverExpandEnabled);
        Assert.Equal(OverlayTokens.HoverExpandDelayMs, s.HoverExpandDelayMs);
        Assert.Equal(OverlayTokens.HoverCollapseGraceMs, s.HoverCollapseGraceMs);
        Assert.True(s.ClickPinEnabled);
        Assert.True(s.HideOnFullscreen);
        Assert.False(s.ClickThroughOnFullscreen);
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
            AnimHover = AnimationSpeed.Fast,
            AnimSwipeRubber = AnimationSpeed.Slow,
            AnimPulseEnabled = false,
            AppearStyle = NotifyAppearStyle.Pop,
            DismissStyle = NotifyDismissStyle.Glitch,
            DateFormat = DateFormat.Numeric,
            DigitalClockEnabled = false,
            ShowClockSeconds = true,
            ShowSecondsStrip = false,
            HoverExpandEnabled = false,
            HoverExpandDelayMs = 100,
            HoverCollapseGraceMs = 5000,
            ClickPinEnabled = false,
            HideOnFullscreen = false,
            ClickThroughOnFullscreen = true,
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
        Assert.Equal(AnimationSpeed.Fast, back.AnimHover);
        Assert.Equal(AnimationSpeed.Slow, back.AnimSwipeRubber);
        Assert.False(back.AnimPulseEnabled);
        Assert.Equal(NotifyAppearStyle.Pop, back.AppearStyle);
        Assert.Equal(NotifyDismissStyle.Glitch, back.DismissStyle);
        Assert.Equal(DateFormat.Numeric, back.DateFormat);
        Assert.False(back.DigitalClockEnabled);
        Assert.True(back.ShowClockSeconds);
        Assert.False(back.ShowSecondsStrip);
        Assert.False(back.HoverExpandEnabled);
        Assert.Equal(100, back.HoverExpandDelayMs);
        // 1.13.1: this was 300 and the round-trip clamped 5000 down to it. The layer between
        // the token (5000) and the machine (10000) was silently deciding the real value, and it
        // decided 3 s — so the promised 5 s never reached the user.
        Assert.Equal(5000, back.HoverCollapseGraceMs);
        Assert.False(back.ClickPinEnabled);
        Assert.False(back.HideOnFullscreen);
        Assert.True(back.ClickThroughOnFullscreen);
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
        var s = new AppSettings { Opacity = 0.1, SoundVolume = 2.0, SoundVolHover = 3.0, FontSize = 99, LowBatteryPercent = 99, HoverExpandDelayMs = 9999, HoverCollapseGraceMs = -5 };
        s.Normalize();
        Assert.Equal(0.35, s.Opacity);
        Assert.Equal(1.0, s.SoundVolume);
        Assert.Equal(1.0, s.SoundVolHover);
        Assert.Equal(18, s.FontSize);
        Assert.Equal(50, s.LowBatteryPercent);
        Assert.Equal(2000, s.HoverExpandDelayMs);
        Assert.Equal(0, s.HoverCollapseGraceMs);
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
            AnimClickPop = AnimationSpeed.Fast,
            AnimFirstAppearWobble = AnimationSpeed.Off,
            ReducedMotion = true,
            AnimPulseEnabled = false,
            AppearStyle = NotifyAppearStyle.SlideDown,
            DismissStyle = NotifyDismissStyle.SlideUp,
            DateFormat = DateFormat.FullShort,
            DigitalClockEnabled = false,
            ShowClockSeconds = true,
            ShowSecondsStrip = false,
            HoverExpandEnabled = false,
            HoverExpandDelayMs = 80,
            ClickPinEnabled = false,
            HideOnFullscreen = false,
            ClickThroughOnFullscreen = true,
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
        Assert.Equal(AnimationSpeed.Fast, b.AnimClickPop);
        Assert.Equal(AnimationSpeed.Off, b.AnimFirstAppearWobble);
        Assert.True(b.ReducedMotion);
        Assert.False(b.AnimPulseEnabled);
        Assert.Equal(NotifyAppearStyle.SlideDown, b.AppearStyle);
        Assert.Equal(NotifyDismissStyle.SlideUp, b.DismissStyle);
        Assert.Equal(DateFormat.FullShort, b.DateFormat);
        Assert.False(b.DigitalClockEnabled);
        Assert.True(b.ShowClockSeconds);
        Assert.False(b.ShowSecondsStrip);
        Assert.False(b.HoverExpandEnabled);
        Assert.Equal(80, b.HoverExpandDelayMs);
        Assert.False(b.ClickPinEnabled);
        Assert.False(b.HideOnFullscreen);
        Assert.True(b.ClickThroughOnFullscreen);
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

    /// <summary>
    /// Guard against CP1251-mojibake regressions: AppSettings.Load / Save use
    /// File.ReadAllText / File.WriteAllText with no explicit encoding (UTF-8
    /// default). If any text file in the app ever ships as CP1251 / Latin1
    /// (a stale 1.3.x artifact, a PowerShell Get-Content without -Encoding
    /// UTF8, or a manual Notepad save on a non-UTF8 system), the round-trip
    /// silently breaks. Pin UTF-8 for every script NotifyIsland users might
    /// type in WeatherLocationName.
    /// </summary>
    [Fact]
    public void Json_RoundTrip_PreservesUtf8ThroughFileIo()
    {
        var samples = new[]
        {
            "Москва",                                   // Cyrillic (default)
            "Санкт-Петербург",                          // Cyrillic + dash
            "Токио / 東京",                              // Cyrillic + CJK
            "القاهرة",                                  // RTL Arabic
            "Αθήνα",                                    // Greek
            "🎉 שלום 北京",                              // Emoji + Hebrew + CJK
            "café — naïve — résumé",                    // Latin-1 + em-dash
            "👨‍👩‍👧‍👦",                                    // ZWJ family
            "हिन्दी",                                    // Devanagari
            "🇷🇺"                                        // Regional indicator
        };

        foreach (var name in samples)
        {
            var s = new AppSettings { WeatherLocationName = name };
            var json = s.ToJson();

            var dir = Path.Combine(Path.GetTempPath(), "notifyisland-utf8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");

            try
            {
                File.WriteAllText(path, json);
                var bytes = File.ReadAllBytes(path);

                // No UTF-16 BOMs (would indicate someone re-encoded as UTF-16).
                Assert.False(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE,
                              $"UTF-16 BE BOM in {path}");
                Assert.False(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF,
                              $"UTF-16 LE BOM in {path}");
                // No UTF-8 BOM (File.WriteAllText default is no BOM; intentional).
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                              $"UTF-8 BOM in {path} (default WriteAllText writes no BOM)");

                // Body must decode as UTF-8 (the only encoding AppSettings.Load supports).
                try
                {
                    System.Text.Encoding.UTF8.GetCharCount(bytes);
                }
                catch (System.Text.DecoderFallbackException)
                {
                    Assert.Fail($"Not valid UTF-8: {path}");
                }

                // Round-trip via the same code path the app uses:
                // File.ReadAllText + AppSettings.FromJson.
                var readBack = File.ReadAllText(path);
                var back = AppSettings.FromJson(readBack);
                Assert.NotNull(back);
                Assert.Equal(name, back!.WeatherLocationName);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Normalize_ClampsSystemStatsRefreshMs()
    {
        var low = new AppSettings { SystemStatsRefreshMs = 10 };
        low.Normalize();
        Assert.Equal(OverlayTokens.StatsRefreshMinMs, low.SystemStatsRefreshMs);

        var high = new AppSettings { SystemStatsRefreshMs = 99_999 };
        high.Normalize();
        Assert.Equal(OverlayTokens.StatsRefreshMaxMs, high.SystemStatsRefreshMs);

        var mid = new AppSettings { SystemStatsRefreshMs = 750 };
        mid.Normalize();
        Assert.Equal(750, mid.SystemStatsRefreshMs);
    }

    [Fact]
    public void Default_StatsKeys_AreTrue()
    {
        var s = new AppSettings();
        s.Normalize();
        Assert.True(s.SystemStatsEnabled);
        Assert.True(s.SystemStatsHoverPeek);
        Assert.True(s.SystemStatsAllInterfaces);
        Assert.True(s.SettingsSearchEnabled);
    }

    [Fact]
    public void Defaults_StatsKeys_AreTrueForExistingInstalls()
    {
        // A settings.json written before 1.12.0 has none of the new keys.
        var json = "{\"fontSize\":14,\"dateFormat\":\"DayMonth\"}";
        // FromJson is the real load path (camelCase policy + enum converter + Normalize).
        var s = AppSettings.FromJson(json)!;
        s.Normalize();
        Assert.True(s.SystemStatsEnabled);
        Assert.True(s.SystemStatsHoverPeek);
        Assert.True(s.SystemStatsAllInterfaces);
        Assert.True(s.SettingsSearchEnabled);
        Assert.Equal(14, s.FontSize);
    }

    [Fact]
    public void Legacy_SystemStatsAutoCollapseKey_IsIgnoredOnLoad()
    {
        // 1.12.1 removed the key and its 30 s wall-clock behaviour. Old settings.json files still
        // carry it; the deserializer must ignore the unknown member instead of throwing.
        var json = "{\"fontSize\":14,\"systemStatsAutoCollapse\":false,\"systemStatsHoverPeek\":true}";
        var s = AppSettings.FromJson(json)!;
        s.Normalize();
        Assert.True(s.SystemStatsHoverPeek);
        Assert.Equal(14, s.FontSize);
    }

    // --- clipboard privacy pause (1.13, kept in 1.14) ---------------------------
    //
    // 1.14 removed the ball pin (isBlobPinned / clipboardBlobPinnedOffsetX/Y) and every test
    // that only covered it. The privacy pause SURVIVED: it is a listener-level switch and
    // nothing about it depended on the ball existing.

    [Fact]
    public void Defaults_PrivacyPauseIsNull()
    {
        // New settings on an old install deserialise to null because the key is missing;
        // the listener must read this as "not paused", not crash.
        var s = new AppSettings();
        Assert.Null(s.ClipboardPrivacyPauseUntilUtc);
    }

    [Fact]
    public void CopyTo_PropagatesPrivacyPause()
    {
        var src = new AppSettings
        {
            ClipboardPrivacyPauseUntilUtc = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
        };
        var dst = new AppSettings();
        src.CopyTo(dst);
        Assert.Equal(src.ClipboardPrivacyPauseUntilUtc, dst.ClipboardPrivacyPauseUntilUtc);
    }

    [Fact]
    public void Json_RoundTrip_PreservesPrivacyPause()
    {
        var s = new AppSettings
        {
            ClipboardPrivacyPauseUntilUtc = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
        };
        var json = s.ToJson();
        var back = AppSettings.FromJson(json);
        Assert.NotNull(back);
        Assert.Equal(s.ClipboardPrivacyPauseUntilUtc, back!.ClipboardPrivacyPauseUntilUtc);
    }

    [Fact]
    public void Legacy_BlobPinKeys_AreIgnoredOnLoad()
    {
        // 1.14 removed the keys, but existing settings.json files still carry them. The
        // deserializer must ignore the unknown members instead of throwing -- this is the
        // regression that would make the app fail to start for every existing user.
        var json = "{\"clipboardBlobPinnedOffsetX\":12,\"clipboardBlobPinnedOffsetY\":-7,\"isBlobPinned\":true}";
        var s = AppSettings.FromJson(json);
        Assert.NotNull(s);
        s!.Normalize();
        Assert.Null(s.ClipboardPrivacyPauseUntilUtc);
    }

    // -- Hover grace must survive a settings round trip (1.13.1) -------------

    /// <summary>
    /// The 5 s hover grace was never actually reaching the user.
    /// <para>
    /// <c>OverlayTokens.HoverCollapseGraceMs</c> was raised to 5000 and <c>HoverPinMachine</c>
    /// clamps to 10000, but <c>AppSettings</c> clamped the same value to <b>3000</b> in both
    /// <c>Normalize</c> and <c>CopyTo</c>. The settings layer sat between the token and the machine
    /// and silently truncated the default to 3 s on every single load — which is exactly the
    /// "it still hides too early" symptom.
    /// </para>
    /// </summary>
    [Fact]
    public void HoverGrace_DefaultIsNotTruncatedByTheSettingsClamp()
    {
        var s = new AppSettings();
        s.Normalize();

        Assert.Equal(OverlayTokens.HoverCollapseGraceMs, s.HoverCollapseGraceMs);
        Assert.Equal(5000, s.HoverCollapseGraceMs);
    }

    [Fact]
    public void HoverGrace_SurvivesACopyToRoundTrip()
    {
        var src = new AppSettings { HoverCollapseGraceMs = 5000 };
        var dst = new AppSettings();
        src.CopyTo(dst);

        Assert.Equal(5000, dst.HoverCollapseGraceMs);
    }

    [Fact]
    public void HoverGrace_StillClampsAbsurdValues()
    {
        // Raising the ceiling must not remove the guard: a settings.json edited by hand could
        // otherwise set a grace of an hour and the panel would never come back on its own.
        var s = new AppSettings { HoverCollapseGraceMs = 999_999 };
        s.Normalize();
        Assert.Equal(10_000, s.HoverCollapseGraceMs);

        var t = new AppSettings { HoverCollapseGraceMs = -50 };
        t.Normalize();
        Assert.Equal(0, t.HoverCollapseGraceMs);
    }

    [Fact]
    public void HoverGrace_CeilingAgreesWithTheMachine()
    {
        // One number, two owners. If these drift apart again the settings layer wins silently,
        // so the agreement is worth pinning.
        var machineCeiling = 10_000;
        var s = new AppSettings { HoverCollapseGraceMs = 60_000 };
        s.Normalize();
        Assert.Equal(machineCeiling, s.HoverCollapseGraceMs);

        var m = new HoverPinMachine();
        m.Configure(true, true, 250, 60_000);
        Assert.Equal(s.HoverCollapseGraceMs, m.CollapseGraceMs);
    }

    /// <summary>
    /// The 500 in a real settings.json was the OLD SHIPPED DEFAULT, persisted by every save.
    /// Fixing the clamp could never help: 500 was already inside the new range, so it sat there
    /// and the 5 s default was dead on arrival for every existing install. Only a migration can
    /// move it.
    /// </summary>
    [Fact]
    public void HoverGrace_LegacyStoredDefault_MigratesUp()
    {
        var s = new AppSettings { HoverCollapseGraceMs = AppSettings.LegacyHoverCollapseGraceMs };
        s.Normalize();
        Assert.Equal(OverlayTokens.HoverCollapseGraceMs, s.HoverCollapseGraceMs);
        Assert.Equal(5000, s.HoverCollapseGraceMs);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1000)]
    [InlineData(2500)]
    [InlineData(7500)]
    [InlineData(10000)]
    public void HoverGrace_AnyOtherValueIsLeftAlone(int stored)
    {
        // The migration must not stomp on a value the user did choose. Only the old shipped
        // default is special-cased.
        var s = new AppSettings { HoverCollapseGraceMs = stored };
        s.Normalize();
        Assert.Equal(stored, s.HoverCollapseGraceMs);
    }

    [Fact]
    public void HoverGrace_ZeroStaysZero()
    {
        // Zero is a legitimate deliberate choice — "hide the instant I leave" — and must survive.
        var s = new AppSettings { HoverCollapseGraceMs = 0 };
        s.Normalize();
        Assert.Equal(0, s.HoverCollapseGraceMs);
    }

    [Fact]
    public void NotifyJumpToTop_IsOnByDefaultAndSurvivesARoundTrip()
    {
        // On by default: a toast drawn behind the window you are reading is not a toast you
        // asked for. It is a checkbox, so a user who wants a strictly desktop-level island can
        // turn it off without changing ZOrderMode.
        var fresh = new AppSettings();
        Assert.True(fresh.NotifyJumpToTop);

        var s = new AppSettings { NotifyJumpToTop = false };
        var json = s.ToJson();
        var back = AppSettings.FromJson(json);
        Assert.NotNull(back);
        Assert.False(back!.NotifyJumpToTop);

        // CopyTo is the live-preview path; forgetting the field there would make the checkbox
        // appear to do nothing until the window is reopened.
        var target = new AppSettings();
        s.CopyTo(target);
        Assert.False(target.NotifyJumpToTop);
    }

    [Fact]
    public void NotifyBodyMarquee_IsOnByDefaultAndSurvivesARoundTrip()
    {
        // On by default: the body is the half of a toast that carries the message, and cutting it
        // at the ellipsis leaves the user with nothing but the app name.
        var fresh = new AppSettings();
        Assert.True(fresh.NotifyBodyMarquee);

        var s = new AppSettings { NotifyBodyMarquee = false };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.False(back!.NotifyBodyMarquee);

        var target = new AppSettings();
        s.CopyTo(target);
        Assert.False(target.NotifyBodyMarquee);
    }

    [Fact]
    public void RecordingIndicator_IsOnByDefaultAndSurvivesARoundTrip()
    {
        var fresh = new AppSettings();
        Assert.True(fresh.RecordingIndicatorEnabled);

        var s = new AppSettings { RecordingIndicatorEnabled = false };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.False(back!.RecordingIndicatorEnabled);

        var target = new AppSettings();
        s.CopyTo(target);
        Assert.False(target.RecordingIndicatorEnabled);
    }

    [Fact]
    public void RecentWindows_IsOnByDefaultAndSurvivesARoundTrip()
    {
        var fresh = new AppSettings();
        Assert.True(fresh.RecentWindowsEnabled);

        var s = new AppSettings { RecentWindowsEnabled = false };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.NotNull(back);
        Assert.False(back!.RecentWindowsEnabled);

        var target = new AppSettings();
        s.CopyTo(target);
        Assert.False(target.RecentWindowsEnabled);
    }

    [Fact]
    public void HoverGrace_AFreshInstallAlreadyGetsTheNewDefault()
    {
        var s = new AppSettings();
        s.Normalize();
        Assert.Equal(5000, s.HoverCollapseGraceMs);
    }

    // --- an unreadable settings file must not be destroyed by the next Save ---------

    private static string NewSettingsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "notifyisland-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void TryLoadFrom_MissingFile_IsNotAFailure()
    {
        // First run. An absent file must be indistinguishable from a clean start, otherwise the
        // app would warn about a problem the user cannot have.
        var dir = NewSettingsDir();
        try
        {
            var ok = AppSettings.TryLoadFrom(Path.Combine(dir, "settings.json"), out var s, out var error);
            Assert.False(ok);
            Assert.Null(s);
            Assert.Equal(string.Empty, error);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TryLoadFrom_ValidFile_LoadsIt()
    {
        var dir = NewSettingsDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, new AppSettings { FontSize = 17 }.ToJson());

            var ok = AppSettings.TryLoadFrom(path, out var s, out var error);
            Assert.True(ok);
            Assert.Equal(string.Empty, error);
            Assert.NotNull(s);
            Assert.Equal(17, s!.FontSize);
            Assert.True(File.Exists(path));           // a good file is left exactly where it was
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TryLoadFrom_CorruptFile_KeepsTheOriginalAsideAndSaysWhy()
    {
        // The whole point: Load falls back to defaults, and the very next Save writes over
        // settings.json. Without the quarantine the user's file is gone, silently.
        var dir = NewSettingsDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            const string broken = "{ this is not json at all";
            File.WriteAllText(path, broken);

            var ok = AppSettings.TryLoadFrom(path, out var s, out var error);
            Assert.False(ok);
            Assert.Null(s);
            Assert.NotEqual(string.Empty, error);

            Assert.False(File.Exists(path));          // moved out of the way
            var kept = Directory.GetFiles(dir, "settings.corrupt-*.json");
            Assert.Single(kept);
            Assert.Equal(broken, File.ReadAllText(kept[0]));   // content intact, not just deleted
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TryLoadFrom_ValidJsonThatIsNotSettings_IsAlsoQuarantined()
    {
        // A JSON document of the wrong shape must not sit where Save will overwrite it either.
        var dir = NewSettingsDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "[1, 2, 3]");

            var ok = AppSettings.TryLoadFrom(path, out _, out var error);
            Assert.False(ok);
            Assert.NotEqual(string.Empty, error);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(dir, "settings.corrupt-*.json"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TryLoadFrom_DoesNotTouchLastLoadError()
    {
        // LastLoadError belongs to Load alone. The split-out helper is also called directly (and
        // by tests), and it must not publish a reason that Load never actually saw.
        var dir = NewSettingsDir();
        try
        {
            var before = AppSettings.LastLoadError;
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ broken");

            AppSettings.TryLoadFrom(path, out _, out _);

            Assert.Equal(before, AppSettings.LastLoadError);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
