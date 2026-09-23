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

        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Ping" });
        Assert.Equal(OverlayKind.Notification, m.Snapshot().Kind);

        m.Tick(OverlayTokens.DefaultNotifyMs);
        Assert.Equal(OverlayKind.Expanded, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.Clear);
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.SetProgress, new OverlayPayload { Progress = 0.5 });
        Assert.Equal(OverlayKind.Progress, m.Snapshot().Kind);

        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload());
        Assert.Equal("Без названия", m.Snapshot().Payload.Title);

        m.Dispatch(OverlayCommand.SetTimer, new OverlayPayload { RemainingSeconds = 10 });
        m.Tick(2500);
        Assert.InRange(m.Snapshot().Payload.RemainingSeconds, 7.45, 7.55);

        m.Dispatch(OverlayCommand.SetError, new OverlayPayload());
        Assert.Equal("Ошибка", m.Snapshot().Payload.Title);

        m.Dispatch(OverlayCommand.Collapse);
        Assert.Equal(OverlayKind.Collapsed, m.Snapshot().Kind);
    }

    [Fact]
    public void Notify_CustomDuration_ReturnsToMedia()
    {
        var m = new OverlayMachine { NotifyDurationMs = 1000 };
        m.Dispatch(OverlayCommand.SetMedia, new OverlayPayload { Title = "A", Subtitle = "B" });
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "N" });
        m.Tick(500);
        Assert.Equal(OverlayKind.Notification, m.Snapshot().Kind);
        m.Tick(500);
        Assert.Equal(OverlayKind.Media, m.Snapshot().Kind);
    }

    [Fact]
    public void Sizes_FixedHeight_WidthVariesByKind()
    {
        Assert.Equal(OverlayTokens.CollapsedW, OverlayMachine.WidthFor(OverlayKind.Idle));
        Assert.Equal(OverlayTokens.CollapsedWeatherW, OverlayMachine.WidthFor(OverlayKind.Idle, weatherEnabled: true));
        Assert.Equal(OverlayTokens.CollapsedH, OverlayMachine.HeightFor(OverlayKind.Collapsed));

        foreach (OverlayKind kind in Enum.GetValues<OverlayKind>())
        {
            var h = OverlayMachine.HeightFor(kind);
            Assert.Equal(OverlayTokens.CollapsedH, h);

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
        Assert.InRange(OverlayTokens.MorphMs, 260, 320);
        Assert.True(OverlayTokens.MorphMs > 0);
        Assert.Equal("#080808", OverlayTokens.FillHex);
        Assert.Equal("#3D9CF0", OverlayTokens.AccentHex);
        Assert.True(OverlayTokens.CollapsedW <= 160);
        Assert.True(OverlayTokens.CollapsedH <= 30);
        Assert.Equal(48, OverlayTokens.SwipeFirePx);
        Assert.Equal(12, OverlayTokens.SwipeClickMaxPx);
        Assert.Equal(180, OverlayTokens.SwipeRubberMs);
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
    public void DemoNext_IncludesSetWeather()
    {
        var m = new OverlayMachine();
        OverlayKind? sawWeather = null;
        for (var i = 0; i < 16; i++)
        {
            m.Dispatch(OverlayCommand.DemoNext);
            if (m.Snapshot().Kind == OverlayKind.Weather)
                sawWeather = OverlayKind.Weather;
        }
        Assert.Equal(OverlayKind.Weather, sawWeather);
    }
}
