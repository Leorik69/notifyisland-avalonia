using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Pins the fix for the doubled morph that made every animation stutter (1.13.1).
///
/// The bug was not a slow animation, it was a second <c>StartMorph</c> landing a millisecond
/// after the first: <c>TickHoverPin → ApplyHoverExpandedState</c> calls <c>ApplySize</c>, and the
/// 200 ms tick then calls <c>ApplySize</c> again for the same hover change. The second call
/// re-ran <c>PrepareMorphVisualStart</c> and <c>_morphWatch.Restart()</c> over a morph that had
/// not ticked yet, so it restarted from the pre-morph width and reseeded the blob's phase. The
/// log showed every hover morph twice, 1 ms apart.
/// </summary>
public class MorphRestartTests
{
    // A SystemStats peek, the exact case the log captured: 240×30 capsule growing to a 300×68
    // monitor surface, with the window following it.
    private const double IdleW = 240, IdleH = 30, StatsW = 300, StatsH = 68;
    private const double IdleWinW = 352, IdleWinH = 264, StatsWinW = 412, StatsWinH = 302;

    [Fact]
    public void SameTargetWhileActive_IsBlocked()
    {
        // The doubled call. This is the whole bug.
        Assert.True(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: StatsW, toH: StatsH, winToW: StatsWinW, winToH: StatsWinH,
            newW: StatsW, newH: StatsH, newWinToW: StatsWinW, newWinToH: StatsWinH));
    }

    [Fact]
    public void SameTargetWhileIdle_IsAllowed()
    {
        // Nothing is in flight, so this is a genuine fresh start (the hover peek expanding from
        // rest). Blocking here would freeze the monitor open forever.
        Assert.False(MorphRestart.WouldRestartSameTarget(
            morphActive: false,
            toW: IdleW, toH: IdleH, winToW: IdleWinW, winToH: IdleWinH,
            newW: StatsW, newH: StatsH, newWinToW: StatsWinW, newWinToH: StatsWinH));
    }

    [Fact]
    public void DifferentTargetWhileActive_IsAllowed()
    {
        // Retargeting mid-morph is legitimate: a kind change while a morph is still running
        // should redirect it, not be swallowed.
        Assert.False(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: StatsW, toH: StatsH, winToW: StatsWinW, winToH: StatsWinH,
            newW: IdleW, newH: IdleH, newWinToW: IdleWinW, newWinToH: IdleWinH));
    }

    [Theory]
    [InlineData(StatsW + 1.0, StatsH, StatsWinW, StatsWinH)]
    [InlineData(StatsW, StatsH + 1.0, StatsWinW, StatsWinH)]
    [InlineData(StatsW, StatsH, StatsWinW + 1.0, StatsWinH)]
    [InlineData(StatsW, StatsH, StatsWinW, StatsWinH + 1.0)]
    public void AnyDimensionMoving_StopsTheGuard(double w, double h, double ww, double wh)
    {
        // The guard is on the whole target, not on the capsule alone: a blob attach is a
        // window-only morph where the capsule does not move at all. Keying on the capsule size
        // would have swallowed those.
        Assert.False(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: StatsW, toH: StatsH, winToW: StatsWinW, winToH: StatsWinH,
            newW: w, newH: h, newWinToW: ww, newWinToH: wh));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.49)]
    [InlineData(0.0)]
    public void SubPixelDrift_IsTreatedAsTheSameTarget(double drift)
    {
        // A quarter-pixel of layout drift must not read as a new target, or the guard fires on
        // its own frame noise and the real restart slips through.
        Assert.True(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: StatsW, toH: StatsH, winToW: StatsWinW, winToH: StatsWinH,
            newW: StatsW + drift, newH: StatsH, newWinToW: StatsWinW, newWinToH: StatsWinH));
    }

    [Fact]
    public void WindowOnlyMorph_BlocksTheDuplicateToo()
    {
        // The clipboard ball attaches without moving the capsule: the window grows from 352 to
        // 512 while the capsule stays 240×30. This is the case the user saw as «шарик не
        // отделяется плавно» — and it is precisely why the guard cannot key on the capsule alone.
        //
        // The in-flight morph is already heading to the grown window, so its stored target is
        // (240, 30, 512, 264) and the duplicate call computes the same thing.
        Assert.True(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: IdleW, toH: IdleH, winToW: 512, winToH: IdleWinH,
            newW: IdleW, newH: IdleH, newWinToW: 512, newWinToH: IdleWinH));

        // A different window target while that morph runs is a genuine retarget and is allowed.
        Assert.False(MorphRestart.WouldRestartSameTarget(
            morphActive: true,
            toW: IdleW, toH: IdleH, winToW: 512, winToH: IdleWinH,
            newW: IdleW, newH: IdleH, newWinToW: 513, newWinToH: IdleWinH));
    }
}
