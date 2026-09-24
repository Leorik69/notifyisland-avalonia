using Xunit;

namespace NotifyIsland.Tests;

public class HoverPinMachineTests
{
    private static HoverPinMachine Fresh(
        bool hover = true, bool pin = true, int delay = 250, int grace = 500)
    {
        var m = new HoverPinMachine();
        m.Configure(hover, pin, delay, grace);
        return m;
    }

    [Fact]
    public void Defaults_Collapsed_NotExpanded()
    {
        var m = new HoverPinMachine();
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
        Assert.False(m.IsContentExpanded);
        Assert.False(m.IsPinned);
    }

    [Fact]
    public void Hover_AfterDelay_Expands()
    {
        var m = Fresh(delay: 250);
        m.PointerEnter();
        Assert.Equal(HoverPinPhase.HoverPending, m.Phase);
        Assert.False(m.IsContentExpanded);

        Assert.False(m.Tick(100));
        Assert.Equal(HoverPinPhase.HoverPending, m.Phase);

        Assert.True(m.Tick(200));
        Assert.Equal(HoverPinPhase.HoverExpanded, m.Phase);
        Assert.True(m.IsContentExpanded);
    }

    [Fact]
    public void Hover_LeaveBeforeDelay_StaysCollapsed()
    {
        var m = Fresh(delay: 250);
        m.PointerEnter();
        m.Tick(100);
        m.PointerLeave();
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
        Assert.False(m.IsContentExpanded);
    }

    [Fact]
    public void Hover_Leave_GraceThenCollapse_ReenterCancels()
    {
        var m = Fresh(delay: 50, grace: 500);
        m.PointerEnter();
        m.Tick(50);
        Assert.Equal(HoverPinPhase.HoverExpanded, m.Phase);

        m.PointerLeave();
        Assert.Equal(HoverPinPhase.CollapsePending, m.Phase);
        Assert.True(m.IsContentExpanded); // still showing during grace

        m.PointerEnter();
        Assert.Equal(HoverPinPhase.HoverExpanded, m.Phase);

        m.PointerLeave();
        m.Tick(500);
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
        Assert.False(m.IsContentExpanded);
    }

    [Fact]
    public void ClickPin_SurvivesLeave_UntilClickOrEscape()
    {
        var m = Fresh();
        m.PointerEnter();
        m.Tick(250);
        Assert.True(m.ClickTogglePin());
        Assert.Equal(HoverPinPhase.Pinned, m.Phase);
        Assert.True(m.IsPinned);

        m.PointerLeave();
        m.Tick(2000);
        Assert.Equal(HoverPinPhase.Pinned, m.Phase);
        Assert.True(m.IsContentExpanded);

        Assert.True(m.ClickTogglePin());
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);

        m.PointerEnter();
        m.Tick(250);
        m.ClickTogglePin();
        m.EscapeOrUnpin();
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
    }

    [Fact]
    public void ClickPin_Disabled_ReturnsFalse()
    {
        var m = Fresh(pin: false);
        Assert.False(m.ClickTogglePin());
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
    }

    [Fact]
    public void Hover_Disabled_IgnoresEnter()
    {
        var m = Fresh(hover: false);
        m.PointerEnter();
        m.Tick(1000);
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
    }

    [Fact]
    public void InstantDelay_Zero_ExpandsOnNextTick()
    {
        var m = Fresh(delay: 0);
        m.PointerEnter();
        Assert.Equal(HoverPinPhase.HoverPending, m.Phase);
        Assert.True(m.Tick(1));
        Assert.Equal(HoverPinPhase.HoverExpanded, m.Phase);
    }

    [Fact]
    public void Configure_DisablingPin_Unpins()
    {
        var m = Fresh();
        m.ClickTogglePin();
        Assert.True(m.IsPinned);
        m.Configure(hoverEnabled: true, pinEnabled: false, hoverDelayMs: 250, collapseGraceMs: 500);
        Assert.Equal(HoverPinPhase.Collapsed, m.Phase);
    }
}
