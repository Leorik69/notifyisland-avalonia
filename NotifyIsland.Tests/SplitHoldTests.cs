using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The clipboard ball must not expire while the user is holding it (1.13.1).
///
/// The split's 6 s lifetime used to count down unconditionally, so a ball picked up and held
/// still fell off the capsule out from under the pointer: the drag kept working, but the thing
/// being dragged disappeared mid-gesture. The lifetime is an IDLE timeout, and an interaction is
/// the opposite of idle.
/// </summary>
public class SplitHoldTests
{
    private static OverlayPayload TextPayload() =>
        new() { ClipboardItemKind = ClipboardItemKind.Text, Title = "скопировано" };

    private static OverlayMachine WithBall(int holdMs = ClipboardHistory.MaxPillMs)
    {
        var m = new OverlayMachine { SplitHoldMs = holdMs };
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        return m;
    }

    [Fact]
    public void WithoutHold_TheBallStillExpires()
    {
        // The idle timeout must still work — otherwise the ball would never leave on its own.
        var m = WithBall();
        m.Tick(ClipboardHistory.MaxPillMs + 100);
        Assert.False(m.IsSplitClipboard);
    }

    [Fact]
    public void Held_BeyondItsWholeLifetime_AndItStays()
    {
        var m = WithBall();
        m.SplitHold = true;

        // Far past the 6 s budget. Ticking in slices because the overlay ticks every 200 ms.
        for (var i = 0; i < 200; i++) m.Tick(200);

        Assert.True(m.IsSplitClipboard);
    }

    [Fact]
    public void Held_TheRemainingTimeNeverDrains()
    {
        var m = WithBall();
        m.SplitHold = true;
        m.Tick(5000);
        // Ticked more than the full budget while held; releasing must not find it already at 0.
        Assert.True(m.Snapshot().SplitMsLeft > 0);
    }

    [Fact]
    public void Released_AfterALongHold_TheBallGetsAFullLifetimeAgain()
    {
        // The user-visible part: letting go of a ball you held for a minute must not make it
        // vanish on that same frame. Holding tops the budget back up every tick, so release
        // hands the ball a fresh lifetime and it settles away on its own time.
        var m = WithBall();
        m.SplitHold = true;
        for (var i = 0; i < 300; i++) m.Tick(200);   // 60 s of holding

        m.SplitHold = false;
        m.Tick(ClipboardHistory.MaxPillMs / 2);

        Assert.True(m.IsSplitClipboard);
    }

    [Fact]
    public void Released_ItStillGoesAwayOnItsOwnEventually()
    {
        var m = WithBall();
        m.SplitHold = true;
        for (var i = 0; i < 50; i++) m.Tick(200);
        m.SplitHold = false;

        m.Tick(ClipboardHistory.MaxPillMs + 200);
        Assert.False(m.IsSplitClipboard);
    }

    [Fact]
    public void ClearSplit_ReleasesTheHold_SoTheNextCopyIsNotImmortal()
    {
        // The latch case, and the reason ClearSplit has to clear the flag itself: while the hold
        // is on, the ball can never expire on its own, so the ONLY way a held ball goes away is
        // a command clearing the split. If that route left the hold latched, the next copy would
        // be frozen from the instant it appeared and the ball would never leave again.
        var m = WithBall();
        m.SplitHold = true;
        m.Dispatch(OverlayCommand.Collapse);
        Assert.False(m.IsSplitClipboard);
        Assert.False(m.SplitHold);

        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        m.Tick(ClipboardHistory.MaxPillMs + 200);
        Assert.False(m.IsSplitClipboard);
    }

    [Fact]
    public void AFreshCopyAfterAHeldOne_StillExpiresNormally()
    {
        var m = WithBall();
        m.SplitHold = true;
        m.Tick(2000);
        m.SplitHold = false;

        // Copying again while the previous ball is still up restarts the lifetime, and that new
        // ball must still be a normal 6 s ball.
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        m.Tick(ClipboardHistory.MaxPillMs + 200);
        Assert.False(m.IsSplitClipboard);
    }

    [Fact]
    public void Hold_DoesNotResurrectAClearedBall()
    {
        var m = WithBall();
        m.Tick(ClipboardHistory.MaxPillMs + 200);
        Assert.False(m.IsSplitClipboard);

        // Holding a ball that is already gone must not bring it back.
        m.SplitHold = true;
        m.Tick(1000);
        Assert.False(m.IsSplitClipboard);
    }
}
