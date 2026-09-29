using Xunit;

namespace NotifyIsland.Tests;

public class OverlayMachineTests
{
    [Fact]
    public void Sanitize_TrimsAndClamps()
    {
        var bad = OverlayMachine.Sanitize(new OverlayPayload
        {
            Title = new string('x', 200),
            Progress = double.NaN,
            RemainingSeconds = -9
        });
        Assert.True(bad.Title.Length <= 80);
        Assert.Equal(0.0, bad.Progress);
        Assert.Equal(0.0, bad.RemainingSeconds);

        var hi = OverlayMachine.Sanitize(new OverlayPayload { Progress = 4.2 });
        Assert.Equal(1.0, hi.Progress);

        var inf = OverlayMachine.Sanitize(new OverlayPayload { RemainingSeconds = double.PositiveInfinity });
        Assert.Equal(0.0, inf.RemainingSeconds);
    }

    [Fact]
    public void Transitions_NotifyReturnsToPrevious()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Expand, new OverlayPayload { Title = "Hi" });
        Assert.Equal(OverlayKind.Expanded, m.Snapshot().Kind);

        // 1.13: a notification no longer hijacks the kind, so there is nothing to return
        // FROM — it fills the payload and bumps unread while the capsule stays put. This is
        // the test that used to be called "NotifyReturnsToPrevious"; the behaviour it pinned
        // is gone on purpose, and the no-takeover behaviour is pinned in its place below.
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Ping" });
        Assert.Equal(OverlayKind.Expanded, m.Snapshot().Kind);
        Assert.Equal("Ping", m.Snapshot().Payload.Title);

        m.Dispatch(OverlayCommand.Clear);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.SetProgress, new OverlayPayload { Progress = 0.5 });
        Assert.True(m.ProgressActive);

        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload());
        Assert.Equal("Без названия", m.MediaRow.Title);

        m.Dispatch(OverlayCommand.SetTimer, new OverlayPayload { RemainingSeconds = 10, Playing = true });
        m.Tick(2500);
        Assert.InRange(m.TimerRow.RemainingSeconds, 7.45, 7.55);

        m.Dispatch(OverlayCommand.SetError, new OverlayPayload());
        Assert.Equal("Ошибка", m.Snapshot().Payload.Title);

        m.Dispatch(OverlayCommand.Collapse);
        Assert.Equal(OverlayKind.Collapsed, m.Snapshot().Kind);
    }

    [Fact]
    public void Notify_CustomDuration_NoLongerDrivesTheKind()
    {
        // The old test asserted "Notification for 1000 ms, then back to Media". With the
        // takeover gone, the countdown is irrelevant to the kind — but the payload and the
        // unread bump are what the user actually perceives, so those are pinned here.
        var m = new OverlayMachine { NotifyDurationMs = 1000 };
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "A", Subtitle = "B" });
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "N" });
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.Equal(1, m.Snapshot().UnreadCount);
        Assert.True(m.MediaActive);
        m.Tick(5000);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void Sizes_FixedHeight_WidthVariesByKind()
    {
        Assert.Equal(OverlayTokens.CollapsedW, OverlayMachine.WidthFor(OverlayKind.Idle));
        Assert.Equal(OverlayTokens.CollapsedWeatherW, OverlayMachine.WidthFor(OverlayKind.Idle, weatherEnabled: true));
        Assert.Equal(OverlayTokens.CollapsedH, OverlayMachine.HeightFor(OverlayKind.Collapsed));

        foreach (OverlayKind kind in Enum.GetValues<OverlayKind>())
        {
            // 1.12.1: SystemStats is the only kind exempt from the CollapsedH height rule.
            var h = OverlayMachine.HeightFor(kind);
            Assert.Equal(kind == OverlayKind.SystemStats ? OverlayTokens.StatsExpandedH : OverlayTokens.CollapsedH, h);

            var w = OverlayMachine.WidthFor(kind, weatherEnabled: true);
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
                Assert.Equal(OverlayTokens.CollapsedWeatherW, w);
            else
                Assert.InRange(w, OverlayTokens.ExpandedMinW, OverlayTokens.ExpandedMaxW);
        }

        Assert.True(OverlayMachine.WidthFor(OverlayKind.Notification) > OverlayTokens.CollapsedW);
        Assert.True(OverlayMachine.WidthFor(OverlayKind.Weather) >= OverlayTokens.ExpandedMinW);
    }

    [Fact]
    public void Weather_SetWeather_KindAndPayload()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetWeather, WeatherCodes.ToPayload(18, 0, 0));
        var snap = m.Snapshot();
        Assert.Equal(OverlayKind.Weather, snap.Kind);
        Assert.Equal(18, snap.Payload.TemperatureC);
        Assert.Equal(0, snap.Payload.WeatherCode);
        Assert.Contains("Ясно", snap.Payload.Body);
        Assert.Equal(OverlayTokens.CollapsedH, snap.Height);
    }

    [Fact]
    public void Cycle_LeftRight_AmongEnabledSlots()
    {
        var m = new OverlayMachine { WeatherEnabled = true };
        Assert.Equal(IslandSlot.Idle, m.CurrentSlot());

        m.Dispatch(OverlayCommand.CycleNext);
        Assert.Equal(IslandSlot.Notification, m.CurrentSlot());
        // Cycle Notification seed must NOT bump unread
        Assert.Equal(0, m.UnreadCount);

        m.Dispatch(OverlayCommand.CycleNext);
        Assert.Equal(IslandSlot.Weather, m.CurrentSlot());

        m.Dispatch(OverlayCommand.CycleNext);
        Assert.Equal(IslandSlot.Media, m.CurrentSlot());

        m.Dispatch(OverlayCommand.CycleNext);
        Assert.Equal(IslandSlot.Idle, m.CurrentSlot());

        m.Dispatch(OverlayCommand.CyclePrev);
        Assert.Equal(IslandSlot.Media, m.CurrentSlot());
    }

    [Fact]
    public void WeatherEnabled_DoesNotBreakUnread()
    {
        var m = new OverlayMachine { WeatherEnabled = true };
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "A" });
        Assert.Equal(1, m.UnreadCount);
        m.Tick(OverlayTokens.DefaultNotifyMs);
        Assert.Equal(1, m.UnreadCount);

        m.WeatherEnabled = false;
        Assert.Equal(1, m.UnreadCount);
        m.Dispatch(OverlayCommand.Collapse);
        Assert.Equal(1, m.UnreadCount);

        m.WeatherEnabled = true;
        m.Dispatch(OverlayCommand.SetWeather, WeatherCodes.MockMoscow());
        Assert.Equal(1, m.UnreadCount);
        Assert.Equal(OverlayKind.Weather, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.Clear);
        Assert.Equal(0, m.UnreadCount);
    }

    [Fact]
    public void ExpandWidget_FromIdle_OpensWeatherWhenEnabled()
    {
        var m = new OverlayMachine { WeatherEnabled = true };
        m.Dispatch(OverlayCommand.ExpandWidget);
        Assert.Equal(OverlayKind.Weather, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.Collapse);
        m.WeatherEnabled = false;
        m.Dispatch(OverlayCommand.ExpandWidget);
        Assert.Equal(OverlayKind.Notification, m.Snapshot().Kind);
        Assert.Equal(0, m.UnreadCount);
    }

    [Fact]
    public void UpdateWeatherCache_KeepsIdle_UpdatesLastWeather()
    {
        var m = new OverlayMachine();
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        m.UpdateWeatherCache(WeatherCodes.ToPayload(21, 3, 10));
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.Equal(21, m.LastWeather.TemperatureC);
        Assert.Equal(3, m.LastWeather.WeatherCode);
    }

    [Fact]
    public void UnreadCount_NotifyIncrements_ClearResets_CollapseKeeps()
    {
        var m = new OverlayMachine();
        Assert.Equal(0, m.Snapshot().UnreadCount);

        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "A" });
        Assert.Equal(1, m.Snapshot().UnreadCount);

        m.Tick(OverlayTokens.DefaultNotifyMs);
        Assert.Equal(1, m.Snapshot().UnreadCount);

        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "B" });
        Assert.Equal(2, m.Snapshot().UnreadCount);

        m.Dispatch(OverlayCommand.Collapse);
        Assert.Equal(OverlayKind.Collapsed, m.Snapshot().Kind);
        Assert.Equal(2, m.Snapshot().UnreadCount);

        m.Dispatch(OverlayCommand.Clear);
        Assert.Equal(0, m.Snapshot().UnreadCount);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void Tokens_MorphMsIsPositive()
    {
        Assert.InRange(OverlayTokens.MorphMs, 400, 500);
        Assert.True(OverlayTokens.MorphMs > 0);
        Assert.Equal("#080808", OverlayTokens.FillHex);
        Assert.Equal("#3D9CF0", OverlayTokens.AccentHex);
        Assert.True(OverlayTokens.CollapsedW <= 200);
        Assert.True(OverlayTokens.CollapsedH <= 30);
        Assert.Equal(12, OverlayTokens.ClickMaxPx);
        Assert.Equal(OverlayTokens.ClickMaxPx, OverlayTokens.SwipeClickMaxPx);
    }

    [Fact]
    public void WeatherCodes_IconKeysAndLabels()
    {
        Assert.Equal("weather-clear", WeatherCodes.IconKey(0));
        Assert.Equal("weather-rain", WeatherCodes.IconKey(63));
        Assert.Equal("Ясно", WeatherCodes.LabelRu(0));
        Assert.Contains("18°", WeatherCodes.FormatExpanded(18, 0, 0));
    }

    [Fact]
    public void Sanitize_MediaFields_AndArtworkCap()
    {
        var ok = OverlayMachine.Sanitize(new OverlayPayload
        {
            Title = "Song",
            Subtitle = "Artist",
            Progress = 0.5,
            Playing = true,
            ArtworkBytes = new byte[] { 1, 2, 3 }
        });
        Assert.Equal("Song", ok.Title);
        Assert.Equal("Artist", ok.Subtitle);
        Assert.Equal(0.5, ok.Progress);
        Assert.True(ok.Playing);
        Assert.NotNull(ok.ArtworkBytes);
        Assert.Equal(3, ok.ArtworkBytes!.Length);

        var huge = new byte[2_000_001];
        var dropped = OverlayMachine.Sanitize(new OverlayPayload { ArtworkBytes = huge });
        Assert.Null(dropped.ArtworkBytes);
    }

    [Fact]
    public void SetMedia_PreservesPlayingAndProgress()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload
        {
            Title = "Track",
            Subtitle = "Band",
            Progress = 0.75,
            Playing = false,
            ArtworkBytes = new byte[] { 9 }
        });
        // 1.13: media is a monitor row, so its data lives in MediaRow and the capsule kind
        // stays put. A song starting must not replace the clock.
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.True(m.MediaActive);
        var media = m.MediaRow;
        Assert.Equal("Track", media.Title);
        Assert.Equal("Band", media.Subtitle);
        Assert.Equal(0.75, media.Progress);
        Assert.False(media.Playing);
        Assert.NotNull(media.ArtworkBytes);
        Assert.Equal(9, media.ArtworkBytes![0]);
    }

    [Fact]
    public void Media_ChangesNothingOnTheCapsuleButTheBand()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "Song", Progress = 0.4 });
        Assert.Equal(ProgressBandOwner.Media, CapsuleProgressBand.OwnerOf(m.BandState()));
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void ClearMedia_FreesTheRowAndTheBand()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "Song" });
        m.ClearMedia();
        Assert.False(m.MediaActive);
        Assert.Equal(ProgressBandOwner.None, CapsuleProgressBand.OwnerOf(m.BandState()));
    }

    [Fact]
    public void Timer_LeavesTheCapsuleAlone_AndClaimsTheBand()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(60));
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.True(m.TimerActive);
        Assert.Equal(ProgressBandOwner.Timer, CapsuleProgressBand.OwnerOf(m.BandState()));
    }

    [Fact]
    public void ClearTimer_KeepsTheCapsuleAndThePayloadAlone()
    {
        // Cancelling a row must not collapse the capsule the user was reading.
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Expand, new OverlayPayload { Title = "Hi" });
        m.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(60));
        m.ClearTimer();
        Assert.False(m.TimerActive);
        Assert.Equal(OverlayKind.Expanded, m.Snapshot().Kind);
        Assert.Equal("Hi", m.Snapshot().Payload.Title);
    }

    [Fact]
    public void Progress_LeavesTheCapsuleAlone_AndClaimsTheBand()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetProgress, new OverlayPayload { Title = "Копирование", Progress = 0.5 });
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.True(m.ProgressActive);
        Assert.Equal(ProgressBandOwner.Clipboard, CapsuleProgressBand.OwnerOf(m.BandState()));
        Assert.Equal(0.5, CapsuleProgressBand.FractionFor(m.BandState()));
    }

    [Fact]
    public void ClipboardProgress_OutranksMedia()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "Song" });
        m.Dispatch(OverlayCommand.SetProgress, new OverlayPayload { Progress = 0.2 });
        Assert.Equal(ProgressBandOwner.Clipboard, CapsuleProgressBand.OwnerOf(m.BandState()));
    }

    [Fact]
    public void Notify_LeavesTheCapsuleAlone_AndBumpsUnread()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Expand, new OverlayPayload { Title = "Hi" });
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Ping" });
        // 1.13: no takeover. The unread dot on the capsule is the whole interruption now.
        Assert.Equal(OverlayKind.Expanded, m.Snapshot().Kind);
        Assert.Equal(1, m.Snapshot().UnreadCount);
    }

    [Fact]
    public void Battery_IsStillTheOnlyCapsuleTakeover()
    {
        var m = new OverlayMachine { NotifyDurationMs = 1000 };
        m.Dispatch(OverlayCommand.SetBattery, BatteryAlertLogic.ChargePayload(55));
        Assert.Equal(OverlayKind.Battery, m.Snapshot().Kind);
        Assert.Equal("Зарядка", m.Snapshot().Payload.Title);
        Assert.Equal(0, m.Snapshot().UnreadCount);
        m.Tick(1000);
        Assert.NotEqual(OverlayKind.Battery, m.Snapshot().Kind);
    }

    [Fact]
    public void Battery_ReturnsToWhateverWasOnScreen()
    {
        var m = new OverlayMachine { NotifyDurationMs = 1000 };
        m.Dispatch(OverlayCommand.SetSystemStats, new OverlayPayload
        {
            SystemStats = SystemSnapshot.Empty
        });
        m.Dispatch(OverlayCommand.SetBattery, BatteryAlertLogic.ChargePayload(55));
        m.Tick(1000);
        Assert.Equal(OverlayKind.SystemStats, m.Snapshot().Kind);
    }

    [Fact]
    public void Timer_TickToZero_ParksInTheRow_WithoutTakingTheCapsule()
    {
        // 1.13: a finished countdown must not vanish and must not take the capsule either.
        // It parks in the timer row for TimerDoneMs and bumps unread (the capsule's dot).
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(2));
        m.Tick(2000);
        Assert.True(m.TimerActive);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
        Assert.Equal("Время вышло", m.TimerRow.Body);
        Assert.Equal(0.0, m.TimerRow.RemainingSeconds);
        Assert.Equal(1, m.Snapshot().UnreadCount);
        // The band is released: nothing is running any more.
        Assert.Equal(ProgressBandOwner.None, CapsuleProgressBand.OwnerOf(m.BandState()));
    }

    [Fact]
    public void TimerCompletionNotice_ExpiresOnItsOwn()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(2));
        m.Tick(2000);
        Assert.True(m.TimerActive);
        m.Tick(OverlayMachine.TimerDoneMs + 1);
        Assert.False(m.TimerActive);
    }

    [Fact]
    public void Countdown_RemembersItsTotalForTheBandFraction()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetTimer, IslandTimerLogic.CountdownPayload(60));
        m.Tick(20000);
        // 40 of 60 seconds left, so the band shows the remaining third.
        Assert.Equal(60.0, m.TimerTotalSeconds);
        Assert.InRange(CapsuleProgressBand.FractionFor(m.BandState()), 0.32, 0.34);
    }

    [Fact]
    public void SetBattery_AutoDismisses_WithoutUnreadBump()
    {
        var m = new OverlayMachine { NotifyDurationMs = 1000 };
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "Song" });
        Assert.Equal(0, m.UnreadCount);

        m.Dispatch(OverlayCommand.SetBattery, BatteryAlertLogic.ChargePayload(55));
        Assert.Equal(OverlayKind.Battery, m.Snapshot().Kind);
        Assert.Equal(0, m.UnreadCount);
        Assert.Equal(320, OverlayMachine.WidthFor(OverlayKind.Battery));

        m.Tick(1000);
        // 1.13: the charge pill used to return to the Media takeover; media is a row now, so
        // the capsule is simply idle again.
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void WidthFor_BatteryChip_AddsExtra()
    {
        var baseW = OverlayMachine.WidthFor(OverlayKind.Idle, weatherEnabled: false, batteryChip: false);
        var chipW = OverlayMachine.WidthFor(OverlayKind.Idle, weatherEnabled: false, batteryChip: true);
        Assert.Equal(baseW + OverlayTokens.CollapsedBatteryExtraW, chipW);
    }

    [Fact]
    public void ClipboardCycle_SetPreviews_PopulatesPayload()
    {
        var m = new OverlayMachine();
        var payload = new OverlayPayload
        {
            ClipboardCyclePreviews = new[] { "alpha", "beta", "gamma" },
            ClipboardCycleIndex = 1
        };
        m.Dispatch(OverlayCommand.SetClipboardCycle, payload);
        var snap = m.Snapshot();
        Assert.Equal(3, snap.Payload.ClipboardCycleCount);
        Assert.Equal(1, snap.Payload.ClipboardCycleIndex);
        Assert.Equal("beta", snap.Payload.ClipboardCyclePreview);
        Assert.Equal("beta", snap.Payload.Title);
        Assert.Equal("2/3", snap.Payload.Subtitle);
    }

    [Fact]
    public void ClipboardCycle_Next_WrapsAround()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = new[] { "a", "b", "c" },
            ClipboardCycleIndex = 2  // last
        });
        m.Dispatch(OverlayCommand.CycleClipboardNext);
        Assert.Equal(0, m.Snapshot().Payload.ClipboardCycleIndex);
        Assert.Equal("a", m.Snapshot().Payload.ClipboardCyclePreview);
    }

    [Fact]
    public void ClipboardCycle_Prev_WrapsAroundToLast()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = new[] { "a", "b", "c" },
            ClipboardCycleIndex = 0
        });
        m.Dispatch(OverlayCommand.CycleClipboardPrev);
        Assert.Equal(2, m.Snapshot().Payload.ClipboardCycleIndex);
        Assert.Equal("c", m.Snapshot().Payload.ClipboardCyclePreview);
    }

    [Fact]
    public void ClipboardCycle_NextIncrementsAndUpdatesPreview()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = new[] { "first", "second", "third" },
            ClipboardCycleIndex = 0
        });
        m.Dispatch(OverlayCommand.CycleClipboardNext);
        Assert.Equal(1, m.Snapshot().Payload.ClipboardCycleIndex);
        Assert.Equal("second", m.Snapshot().Payload.ClipboardCyclePreview);
        Assert.Equal("2/3", m.Snapshot().Payload.Subtitle);
    }

    [Fact]
    public void ClipboardCycle_NoPreviews_IsNoOp()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = Array.Empty<string>(),
            ClipboardCycleIndex = 0
        });
        // No exception, state stays clean.
        Assert.Equal(0, m.Snapshot().Payload.ClipboardCycleCount);

        // Subsequent CycleNext on empty list is a no-op too.
        m.Dispatch(OverlayCommand.CycleClipboardNext);
        Assert.Equal(-1, m.Snapshot().Payload.ClipboardCycleIndex);
    }

    [Fact]
    public void ClipboardCycle_IndexClampsToBounds()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardCycle, new OverlayPayload
        {
            ClipboardCyclePreviews = new[] { "x", "y" },
            ClipboardCycleIndex = 99  // way out of bounds
        });
        // Clamps to last valid index.
        Assert.Equal(1, m.Snapshot().Payload.ClipboardCycleIndex);
        Assert.Equal("y", m.Snapshot().Payload.ClipboardCyclePreview);
    }
}

