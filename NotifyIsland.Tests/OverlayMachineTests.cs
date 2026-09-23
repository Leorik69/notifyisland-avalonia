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
    public void Sizes_RespectExpandedTokenBounds()
    {
        Assert.Equal(OverlayTokens.CollapsedW, OverlayMachine.WidthFor(OverlayKind.Idle));
        Assert.Equal(OverlayTokens.CollapsedH, OverlayMachine.HeightFor(OverlayKind.Collapsed));

        foreach (OverlayKind kind in Enum.GetValues<OverlayKind>())
        {
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed) continue;
            var w = OverlayMachine.WidthFor(kind);
            var h = OverlayMachine.HeightFor(kind);
            Assert.InRange(w, OverlayTokens.ExpandedMinW, OverlayTokens.ExpandedMaxW);
            Assert.InRange(h, OverlayTokens.ExpandedMinH, OverlayTokens.ExpandedMaxH);
        }
    }

    [Fact]
    public void Tokens_MorphMsIsPositive()
    {
        Assert.Equal(260, OverlayTokens.MorphMs);
        Assert.True(OverlayTokens.MorphMs > 0);
        Assert.Equal("#080808", OverlayTokens.FillHex);
        Assert.Equal("#3D9CF0", OverlayTokens.AccentHex);
    }
}
