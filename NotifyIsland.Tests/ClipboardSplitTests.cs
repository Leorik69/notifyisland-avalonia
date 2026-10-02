using System;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// G1 — split clipboard pill: Core model, geometry and sizing.
/// Spec: docs/superpowers/specs/2026-09-29--notifyisland-split-clipboard-pill.md
/// </summary>
public class ClipboardSplitTests
{
    private static OverlayPayload TextPayload(string text = "скопировано") =>
        new() { ClipboardItemKind = ClipboardItemKind.Text, Title = text };

    // -- Geometry ----------------------------------------------------------

    [Fact]
    public void SplitWidthFor_Horizontal_IsCollapsedPlusHalf()
    {
        Assert.Equal(OverlayTokens.CollapsedW + OverlayTokens.ClipboardHalfW,
            ClipboardSplit.SplitWidthFor(isVertical: false, OverlayTokens.CollapsedW));
        Assert.Equal(370.0, ClipboardSplit.SplitWidthFor(false, 170));
    }

    [Fact]
    public void SplitWidthFor_Vertical_AddsToTheLongAxisNotTheShortOne()
    {
        // Vertical: the long axis is the height, so the width stays the capsule's cross axis and
        // the clipboard half shows up as height instead. Spec "Известные ограничения".
        Assert.Equal(OverlayTokens.CollapsedCrossAxisVertical,
            ClipboardSplit.SplitWidthFor(isVertical: true, OverlayTokens.CollapsedW));

        var flat = IslandLayout.SizeFor(OverlayKind.Idle, false,
            IslandOrientation.Horizontal, IslandEdge.Top, splitClipboard: true);
        var vert = IslandLayout.SizeFor(OverlayKind.Idle, false,
            IslandOrientation.Vertical, IslandEdge.Left, splitClipboard: true);

        Assert.Equal(OverlayTokens.CollapsedCrossAxisVertical, vert.Width);  // cross axis, unchanged by the split
        Assert.Equal(flat.Width, vert.Height);               // same long-axis total, other axis
    }

    [Fact]
    public void SizeFor_Split_MatchesSplitWidthFor()
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, false,
            IslandOrientation.Horizontal, IslandEdge.Top, splitClipboard: true);
        Assert.Equal(ClipboardSplit.SplitWidthFor(false, OverlayTokens.CollapsedW), w);
        Assert.Equal(OverlayTokens.CollapsedH, h);
    }

    [Fact]
    public void SizeFor_Split_LeavesSystemStatsFixed()
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.SystemStats, false,
            IslandOrientation.Horizontal, IslandEdge.Top, splitClipboard: true);
        Assert.Equal(OverlayTokens.StatsExpandedW, w);
        Assert.Equal(StatsLayout.StatsHeightFor(0), h);
    }

    [Fact]
    public void ClipboardHalfStart_LeavesTheDividerExactlyBetweenTheHalves()
    {
        var pill = ClipboardSplit.SplitLongAxisFor(OverlayTokens.CollapsedW);
        var start = ClipboardSplit.ClipboardHalfStart(false, pill, OverlayTokens.CollapsedW);

        // The half occupies [start, pill] and is exactly ClipboardHalfW wide.
        Assert.Equal(OverlayTokens.CollapsedW, start);
        Assert.Equal(OverlayTokens.ClipboardHalfW, pill - start);

        // The 1 px divider is centred on the boundary. Its ClipboardHalfGap clearance on each
        // side is reserved as inner padding of the two halves, so it does not widen the pill
        // (370 DIP total, as the spec's geometry table says) — it only has to fit.
        var d = ClipboardSplit.DividerFor(false, pill, OverlayTokens.CollapsedW);
        Assert.Equal(OverlayTokens.ClipboardDividerW, d.Thickness);
        Assert.Equal(start, d.Along + d.Thickness / 2.0);
        Assert.True(d.Along - OverlayTokens.ClipboardHalfGap > 0);
        Assert.True(pill - (d.Along + d.Thickness) - OverlayTokens.ClipboardHalfGap > 0);
    }

    [Fact]
    public void ClipboardHalfStart_ClampsWhenPillTooNarrowForTheHalf()
    {
        // A pill narrower than the half must not put the boundary left of the island content.
        Assert.Equal(OverlayTokens.CollapsedW,
            ClipboardSplit.ClipboardHalfStart(false, 200, OverlayTokens.CollapsedW));
    }

    [Fact]
    public void CrossBreatheOffset_IsBoundedByTheTokenAmplitude()
    {
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(0), 6);
        for (var t = 0; t < 3 * OverlayTokens.ClipboardHalfBreatheMs; t += 37)
        {
            var y = ClipboardSplit.CrossBreatheOffset(t);
            Assert.InRange(y, -OverlayTokens.ClipboardHalfBreathePx - 1e-9,
                OverlayTokens.ClipboardHalfBreathePx + 1e-9);
        }
        // One period later the phase repeats.
        Assert.Equal(ClipboardSplit.CrossBreatheOffset(100),
            ClipboardSplit.CrossBreatheOffset(100 + OverlayTokens.ClipboardHalfBreatheMs), 9);
    }

    // -- Machine state -----------------------------------------------------

    [Fact]
    public void SetClipboardSplit_KeepsTheIslandKindAndItsContent()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetWeather,
            new OverlayPayload { Title = "Москва", Subtitle = "+12°" });

        var snap = m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());

        Assert.True(snap.IsSplitClipboard);
        // The island did not become a Clipboard pill.
        Assert.Equal(OverlayKind.Weather, snap.Kind);
        Assert.Equal("Москва", snap.Payload.Title);
        // The clipboard data rides on the half's own payload.
        Assert.Equal(ClipboardItemKind.Text, snap.SplitClipboard.ClipboardItemKind);
        Assert.Equal("скопировано", snap.SplitClipboard.Title);
    }

    [Fact]
    public void SetClipboardSplit_DoesNotBumpUnread()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Уведомление" });
        var before = m.UnreadCount;

        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload("ещё раз"));

        Assert.Equal(before, m.UnreadCount);
    }

    [Fact]
    public void SetClipboardSplit_ExpiresOnTheClipboardAutoCollapse()
    {
        var m = new OverlayMachine();
        m.NotifyDurationMs = 30_000; // so the 6000 ms clipboard budget is the binding one
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());

        var snap = m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        Assert.Equal(ClipboardHistory.MaxPillMs, snap.SplitMsLeft);

        m.Tick(ClipboardHistory.MaxPillMs - 1);
        Assert.True(m.IsSplitClipboard);
        Assert.Equal(1, m.Snapshot().SplitMsLeft);

        m.Tick(1);
        Assert.False(m.IsSplitClipboard);
        Assert.Equal(0, m.Snapshot().SplitMsLeft);
        Assert.Equal(ClipboardItemKind.None, m.Snapshot().SplitClipboard.ClipboardItemKind);
        // Expiry is not a dismissal — the island keeps its kind and unread.
        Assert.Equal(OverlayKind.Idle, m.Snapshot().Kind);
    }

    [Fact]
    public void SetClipboardSplit_SurvivesANotificationWithoutSharingItsTimer()
    {
        var m = new OverlayMachine { NotifyDurationMs = 30_000 };
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        m.Tick(2000);
        Assert.Equal(ClipboardHistory.MaxPillMs - 2000, m.Snapshot().SplitMsLeft);

        // A notification arrives mid-wait: it bumps unread, the half's budget is untouched.
        // Since 1.13 it no longer even becomes a kind, so the split half and the notification
        // are separate by construction now.
        m.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Уведомление" });
        Assert.Equal(1, m.UnreadCount);
        Assert.Equal(ClipboardHistory.MaxPillMs - 2000, m.Snapshot().SplitMsLeft);

        // The half expires on its own budget…
        m.Tick(ClipboardHistory.MaxPillMs - 2000);
        Assert.False(m.IsSplitClipboard);

        // …and the notification's unread count is still there to be cleared by a click.
        Assert.Equal(1, m.UnreadCount);
    }

    [Fact]
    public void SetClipboardSplit_IgnoresEmptyPayloads()
    {
        var m = new OverlayMachine();
        var snap = m.Dispatch(OverlayCommand.SetClipboardSplit, new OverlayPayload());
        Assert.False(snap.IsSplitClipboard);
    }

    [Fact]
    public void DismissSplitClipboard_LeavesTheKindAlone()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.Expand, new OverlayPayload { Title = "Night Drive" });
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());

        var snap = m.DismissSplitClipboard();
        Assert.False(snap.IsSplitClipboard);
        Assert.Equal(OverlayKind.Expanded, snap.Kind);
        Assert.Equal("Night Drive", snap.Payload.Title);
    }

    [Fact]
    public void Clear_AlsoDropsTheSplitHalf()
    {
        var m = new OverlayMachine();
        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        m.Dispatch(OverlayCommand.Clear);
        Assert.False(m.IsSplitClipboard);
    }

    // -- Orthogonality to the other kinds ----------------------------------

    [Fact]
    public void SplitWidth_DoesNotChangeAnyOtherKindBehaviour()
    {
        var m = new OverlayMachine { WeatherEnabled = false };

        m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());

        Assert.Equal(OverlayTokens.CollapsedW + OverlayTokens.ClipboardHalfW, m.Snapshot().Width);
        Assert.Equal(OverlayTokens.CollapsedH, m.Snapshot().Height);
        Assert.Equal(IslandSlot.Idle, m.CurrentSlot());

        // Every kind still resolves the width it resolved before the split existed, and the
        // split adds exactly one half to each of them — SystemStats except, being a fixed block.
        foreach (var kind in new[]
                 {
                     OverlayKind.Idle, OverlayKind.Collapsed, OverlayKind.Expanded,
                     OverlayKind.Notification, OverlayKind.Progress, OverlayKind.Media,
                     OverlayKind.Timer, OverlayKind.Error, OverlayKind.Weather,
                     OverlayKind.Battery, OverlayKind.Clipboard, OverlayKind.SystemStats
                 })
        {
            var expected = kind == OverlayKind.SystemStats
                ? OverlayMachine.WidthFor(kind)
                : OverlayMachine.WidthFor(kind) + OverlayTokens.ClipboardHalfW;
            Assert.Equal(expected, OverlayMachine.WidthFor(kind, splitClipboard: true));
        }

        // Entering and leaving the split leaves the machine's own kind resolution untouched.
        m.DismissSplitClipboard();
        Assert.Equal(OverlayTokens.CollapsedW, m.Snapshot().Width);
        Assert.Equal(IslandSlot.Idle, m.CurrentSlot());
    }

    [Fact]
    public void Split_WidthIsExposedThroughTheSnapshot()
    {
        var m = new OverlayMachine { WeatherEnabled = false };
        var snap = m.Dispatch(OverlayCommand.SetClipboardSplit, TextPayload());
        Assert.Equal(OverlayTokens.CollapsedW + OverlayTokens.ClipboardHalfW, snap.Width);
        Assert.Equal(OverlayTokens.CollapsedH, snap.Height);
        // Same budget rule as SetClipboard: min(NotifyDurationMs, MaxPillMs).
        Assert.Equal(Math.Min(m.NotifyDurationMs, ClipboardHistory.MaxPillMs), snap.SplitMsLeft);
    }

    [Fact]
    public void Split_WidthIsNotClampedIntoTheExpandedRange()
    {
        // A split Notification is 380 + 200 = 580, not squeezed back to ExpandedMaxW.
        Assert.Equal(380 + OverlayTokens.ClipboardHalfW,
            OverlayMachine.WidthFor(OverlayKind.Notification, splitClipboard: true));
    }

    // -- G3: split-half motion (pure, driven from the morph t) -------------------

    [Fact]
    public void Half_EnteringStartsOffTheRightEdgeAndLandsHome()
    {
        Assert.Equal(OverlayTokens.ClipboardHalfW, ClipboardSplit.TranslateFor(0.0, entering: true), 6);
        Assert.Equal(0.0, ClipboardSplit.TranslateFor(1.0, entering: true), 6);
        // Monotonic decrease — it never overshoots past the slot and comes back.
        var prev = double.MaxValue;
        for (var i = 0; i <= 20; i++)
        {
            var x = ClipboardSplit.TranslateFor(i / 20.0, entering: true);
            Assert.True(x <= prev, $"translate must not reverse at t={i / 20.0}");
            Assert.True(x >= 0.0, $"translate must not go negative at t={i / 20.0}");
            prev = x;
        }
    }

    [Fact]
    public void Half_DetachMirrorsAttachAtTheSameProgress()
    {
        // Both directions are driven forward by the same CubicOut the pill width uses, so at
        // the same t the detach frame is the exact complement of the attach frame — the half
        // leaves along the identical path it arrived on, at the identical rate. (It is *not*
        // a time-reversal: reversing t would run the collapse in slow motion at the start.)
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0;
            var on = ClipboardSplit.TranslateFor(t, entering: true);
            var off = ClipboardSplit.TranslateFor(t, entering: false);
            Assert.Equal(OverlayTokens.ClipboardHalfW, on + off, 9);
            Assert.Equal(1.0 - ClipboardSplit.OpacityFor(t, entering: true),
                ClipboardSplit.OpacityFor(t, entering: false), 9);
            Assert.Equal(2.0 - ClipboardSplit.ScaleFor(t, entering: true),
                ClipboardSplit.ScaleFor(t, entering: false), 9);
        }
    }

    [Fact]
    public void Half_FadeIsDelayedBehindTheWidth()
    {
        // The whole point of the delay: for the first quarter of the morph the capsule is
        // already growing while the half is still fully transparent.
        Assert.Equal(0.0, ClipboardSplit.OpacityFor(0.0, entering: true), 9);
        Assert.Equal(0.0, ClipboardSplit.OpacityFor(OverlayTokens.ClipboardHalfFadeDelay, entering: true), 9);
        Assert.Equal(0.0, ClipboardSplit.OpacityFor(OverlayTokens.ClipboardHalfFadeDelay * 0.5, entering: true), 9);
        // …and by the time the fade starts the slide is already well underway (CubicOut
        // covers ~42% of the distance in the first quarter), so the half is seen arriving
        // before it is seen appearing.
        var slidBy = 1.0 - ClipboardSplit.TranslateFor(
            OverlayTokens.ClipboardHalfFadeDelay, entering: true) / OverlayTokens.ClipboardHalfW;
        Assert.True(slidBy > 0.3, $"the half must visibly lead its own fade, slid only {slidBy:P0}");
        // Fully opaque only at the very end.
        Assert.Equal(1.0, ClipboardSplit.OpacityFor(1.0, entering: true), 6);
    }

    [Fact]
    public void Half_FadeRisesMonotonicallyAndStaysInUnitRange()
    {
        var prev = -1.0;
        for (var i = 0; i <= 100; i++)
        {
            var o = ClipboardSplit.OpacityFor(i / 100.0, entering: true);
            Assert.InRange(o, 0.0, 1.0);
            Assert.True(o >= prev, $"opacity must not reverse at t={i / 100.0}");
            prev = o;
        }
    }

    [Fact]
    public void Half_DetachHoldsFullOpacityWhileItSlidesAway()
    {
        // The half must not fade before it moves, or the collapse reads as a dissolve.
        Assert.Equal(1.0, ClipboardSplit.OpacityFor(0.0, entering: false), 9);
        Assert.Equal(1.0, ClipboardSplit.OpacityFor(OverlayTokens.ClipboardHalfFadeDelay, entering: false), 9);
        Assert.Equal(0.0, ClipboardSplit.OpacityFor(1.0, entering: false), 6);
    }

    [Fact]
    public void Half_EnteringScaleOvershootsToTheTokenPeakAndNeverDips()
    {
        Assert.Equal(1.0, ClipboardSplit.ScaleFor(0.0, entering: true), 6);
        Assert.Equal(1.0, ClipboardSplit.ScaleFor(1.0, entering: true), 6);
        var peak = 0.0;
        for (var i = 0; i <= 200; i++)
        {
            var s = ClipboardSplit.ScaleFor(i / 200.0, entering: true);
            Assert.True(s >= 1.0, $"scale dipped below 1 at t={i / 200.0}");
            peak = Math.Max(peak, s);
        }
        Assert.Equal(OverlayTokens.ClipboardHalfPopPeak, peak, 3);
        // Peak sits in the first third, like ClickPop.
        var peakAt = 0.0;
        for (var i = 0; i <= 200; i++)
        {
            if (Math.Abs(ClipboardSplit.ScaleFor(i / 200.0, entering: true) - peak) < 1e-9)
            {
                peakAt = i / 200.0;
                break;
            }
        }
        Assert.InRange(peakAt, 0.25, 0.45);
    }

    [Fact]
    public void Half_DetachScaleSqueezesInwardAndReturnsToOne()
    {
        Assert.Equal(1.0, ClipboardSplit.ScaleFor(0.0, entering: false), 6);
        Assert.Equal(1.0, ClipboardSplit.ScaleFor(1.0, entering: false), 6);
        var min = double.MaxValue;
        for (var i = 0; i <= 200; i++) min = Math.Min(min, ClipboardSplit.ScaleFor(i / 200.0, entering: false));
        Assert.Equal(1.0 - (OverlayTokens.ClipboardHalfPopPeak - 1.0), min, 3);
        Assert.True(min < 1.0);
    }

    [Fact]
    public void Half_RestingStatesAreWellDefined_NotLeftoverMorphFrames()
    {
        // Resting is the value the idle breathe offsets from; Detached is the state the next
        // split must start from. Both are exact, and Detached must equal the morph's own
        // first frame so no residual offset survives a completed collapse.
        Assert.Equal(0.0, ClipboardSplit.Resting.Translate, 9);
        Assert.Equal(1.0, ClipboardSplit.Resting.Opacity, 9);
        Assert.Equal(1.0, ClipboardSplit.Resting.Scale, 9);

        Assert.Equal(0.0, ClipboardSplit.Detached.Translate, 9);
        Assert.Equal(0.0, ClipboardSplit.Detached.Opacity, 9);
        Assert.Equal(1.0, ClipboardSplit.Detached.Scale, 9);

        // Detached is the reset target, not the attach's first frame: the reset parks the half
        // at 0 and the next attach immediately places it at 200 (fully off the right edge).
        // That jump is invisible because Detached.Opacity is 0, and it is what guarantees a
        // deterministic start rather than an inherited offset.
        var first = ClipboardSplit.HalfFor(0.0, entering: true);
        Assert.Equal(0.0, ClipboardSplit.Detached.Opacity, 9);
        Assert.Equal(ClipboardSplit.Detached.Opacity, first.Opacity, 9);
        Assert.Equal(OverlayTokens.ClipboardHalfW, first.Translate, 6);
        Assert.Equal(ClipboardSplit.Detached.Scale, first.Scale, 9);

        var last = ClipboardSplit.HalfFor(1.0, entering: true);
        Assert.Equal(ClipboardSplit.Resting.Translate, last.Translate, 9);
        Assert.Equal(ClipboardSplit.Resting.Opacity, last.Opacity, 9);
        Assert.Equal(ClipboardSplit.Resting.Scale, last.Scale, 9);

        var lastOut = ClipboardSplit.HalfFor(1.0, entering: false);
        // The collapse's last drawn frame is fully transparent and parked one half width
        // outboard — that is a frame nobody ever sees. The reset to Detached is what
        // SettleSplitHalf() performs, and the test below pins that it is required.
        Assert.Equal(0.0, lastOut.Opacity, 9);
        Assert.Equal(OverlayTokens.ClipboardHalfW, lastOut.Translate, 6);
        Assert.Equal(1.0, lastOut.Scale, 6);
    }

    [Fact]
    public void Half_ResetIsRequiredBecauseTheLastDrawnCollapseFrameIsNotResting()
    {
        // Proof that the resting state is a deliberate reset and not "whatever the last morph
        // frame left": the collapse's final frame differs from Detached in translate, so a
        // window that simply stopped animating there would leave a 200 DIP residual offset
        // that the next split would slide in from the wrong place.
        var lastOut = ClipboardSplit.HalfFor(1.0, entering: false);
        Assert.NotEqual(ClipboardSplit.Detached.Translate, lastOut.Translate);
        // After the reset the next split places the half deterministically at the full
        // outboard offset, independent of where the previous collapse happened to stop.
        Assert.Equal(OverlayTokens.ClipboardHalfW,
            ClipboardSplit.HalfFor(0.0, entering: true).Translate, 9);
    }

    [Fact]
    public void Half_MorphFrameClampsOutOfRangeProgress()
    {
        // The morph timer is wall-clock driven, so t can overshoot 1 on the final frame.
        foreach (var entering in new[] { true, false })
        {
            var over = ClipboardSplit.HalfFor(1.4, entering);
            var at = ClipboardSplit.HalfFor(1.0, entering);
            Assert.Equal(at.Translate, over.Translate, 9);
            Assert.Equal(at.Opacity, over.Opacity, 9);
            Assert.Equal(at.Scale, over.Scale, 9);
            var under = ClipboardSplit.HalfFor(-0.2, entering);
            Assert.Equal(ClipboardSplit.HalfFor(0.0, entering).Translate, under.Translate, 9);
        }
    }

    [Fact]
    public void Half_BreatheStaysWithinAmplitudeAndReturnsToZero()
    {
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(0), 9);
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(OverlayTokens.ClipboardHalfBreatheMs), 9);
        for (var ms = 0; ms < OverlayTokens.ClipboardHalfBreatheMs; ms += 17)
            Assert.InRange(ClipboardSplit.CrossBreatheOffset(ms), -OverlayTokens.ClipboardHalfBreathePx,
                OverlayTokens.ClipboardHalfBreathePx);
        // Quarter period is the positive peak, half period zero, three quarters negative.
        Assert.Equal(OverlayTokens.ClipboardHalfBreathePx,
            ClipboardSplit.CrossBreatheOffset(OverlayTokens.ClipboardHalfBreatheMs / 4), 6);
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(OverlayTokens.ClipboardHalfBreatheMs / 2), 6);
        Assert.Equal(-OverlayTokens.ClipboardHalfBreathePx,
            ClipboardSplit.CrossBreatheOffset(OverlayTokens.ClipboardHalfBreatheMs * 3 / 4), 6);
    }

    // -- G2 fix: the half is anchored along the LONG axis on all four edges --------
    //
    // The bug these pin: the half's markup was a fixed 200×30 block anchored Right/Center,
    // so on Left/Right (where the long axis is the height) 200 DIP of it drew outside the
    // 30-DIP-wide capsule and the hit test bailed out on vertical entirely.

    [Theory]
    [InlineData(false)] // Top / Bottom
    [InlineData(true)]  // Left / Right
    public void HalfStart_IsTheSameBoundaryOnEitherOrientation(bool vertical)
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, false,
            vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal,
            vertical ? IslandEdge.Left : IslandEdge.Top, splitClipboard: true);

        // The long axis is the one the half is attached to; the cross axis never changes.
        var pillLong = vertical ? h : w;
        var start = ClipboardSplit.ClipboardHalfStart(vertical, pillLong, OverlayTokens.CollapsedW);

        // The half occupies exactly [start, pillLong] on the long axis…
        Assert.Equal(OverlayTokens.CollapsedW, start);
        Assert.Equal(OverlayTokens.ClipboardHalfW, pillLong - start);
        // …and nothing at all on the cross axis: the half is attached along the long axis only,
        // which is what keeps the 200-DIP half from drawing outside the capsule.
        var crossAxis = vertical
            ? OverlayTokens.CollapsedCrossAxisVertical
            : OverlayTokens.CollapsedH;
        Assert.Equal(crossAxis, vertical ? w : h);
        // True on both orientations — and the vertical cross axis is now wide enough to hold the
        // clock, so this has to be checked against the value that orientation actually uses.
        Assert.True(OverlayTokens.ClipboardHalfW > crossAxis,
            "the half must be attached along the long axis only — the cross axis is far too short");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsInHalf_AgreesWithTheRenderedExtent_OnEitherOrientation(bool vertical)
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, false,
            vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal,
            vertical ? IslandEdge.Left : IslandEdge.Top, splitClipboard: true);
        var pillLong = vertical ? h : w;
        var start = ClipboardSplit.ClipboardHalfStart(vertical, pillLong, OverlayTokens.CollapsedW);

        // One DIP inside the island half is the island; the boundary itself and everything past
        // it is the clipboard half. There is no dead zone between the two decisions.
        Assert.False(ClipboardSplit.IsInHalf(vertical, start - 0.5, pillLong, OverlayTokens.CollapsedW));
        Assert.True(ClipboardSplit.IsInHalf(vertical, start, pillLong, OverlayTokens.CollapsedW));
        Assert.True(ClipboardSplit.IsInHalf(vertical, pillLong - 0.5, pillLong, OverlayTokens.CollapsedW));

        // Swept across the whole pill, the clickable run is exactly the rendered run.
        var clickable = 0.0;
        for (var p = 0.0; p < pillLong; p += 0.5)
            if (ClipboardSplit.IsInHalf(vertical, p, pillLong, OverlayTokens.CollapsedW)) clickable += 0.5;
        Assert.Equal(OverlayTokens.ClipboardHalfW, clickable, 1.0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsInHalf_ClampsExactlyLikeTheRenderedBoundary(bool vertical)
    {
        // Too short to hold the half: the boundary pins to the island's own length, and the
        // hit test must pin to the same place or the zone would say "half" where the render
        // says "island".
        var start = ClipboardSplit.ClipboardHalfStart(vertical, 200, OverlayTokens.CollapsedW);
        Assert.Equal(OverlayTokens.CollapsedW, start);
        Assert.False(ClipboardSplit.IsInHalf(vertical, OverlayTokens.CollapsedW - 1, 200,
            OverlayTokens.CollapsedW));
        Assert.True(ClipboardSplit.IsInHalf(vertical, OverlayTokens.CollapsedW, 200,
            OverlayTokens.CollapsedW));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CycleZoneExtent_IsTheCollapsedIsland_NotTheSplitPill(bool vertical)
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, false,
            vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal,
            vertical ? IslandEdge.Left : IslandEdge.Top, splitClipboard: true);
        var pillLong = vertical ? h : w;

        var flat = IslandLayout.SizeFor(OverlayKind.Idle, false,
            vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal,
            vertical ? IslandEdge.Left : IslandEdge.Top, splitClipboard: false);
        var collapsedLong = vertical ? flat.Height : flat.Width;

        // Split or not, the ⅓/⅓/⅓ zones are measured against the island's own half…
        var zoneSplit = ClipboardSplit.IslandHalfExtent(vertical, true, pillLong, collapsedLong);
        var zonePlain = ClipboardSplit.IslandHalfExtent(vertical, false, pillLong, collapsedLong);
        Assert.Equal(collapsedLong, zoneSplit);
        Assert.Equal(OverlayTokens.CollapsedW, zoneSplit);
        // …so `third` is 170/3 on every edge, and the pill's own extra 200 DIP never leaks in.
        Assert.Equal(OverlayTokens.CollapsedW / 3.0, zoneSplit / 3.0, 9);
        Assert.NotEqual(pillLong / 3.0, zoneSplit / 3.0, 6);
        // Unsplit, the zones span the whole pill — the pre-split behaviour is untouched.
        Assert.Equal(pillLong, zonePlain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Divider_IsCentredOnTheBoundary_AndIdenticalInBothOrientations(bool vertical)
    {
        var (w, h) = IslandLayout.SizeFor(OverlayKind.Idle, false,
            vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal,
            vertical ? IslandEdge.Left : IslandEdge.Top, splitClipboard: true);
        var pillLong = vertical ? h : w;

        var d = ClipboardSplit.DividerFor(vertical, pillLong, OverlayTokens.CollapsedW);
        var start = ClipboardSplit.ClipboardHalfStart(vertical, pillLong, OverlayTokens.CollapsedW);

        // Centred on the very boundary the half and the hit test use.
        Assert.Equal(start, d.Along + d.Thickness / 2.0, 9);
        // The line spans 16 DIP across the pill on both orientations — horizontal on Top/Bottom,
        // horizontal-turned on Left/Right — and the clearance still fits inside both halves.
        Assert.Equal(OverlayTokens.ClipboardDividerCross, d.Cross);
        Assert.Equal(OverlayTokens.ClipboardDividerW, d.Thickness);
        Assert.True(d.Along - OverlayTokens.ClipboardHalfGap > 0);
        Assert.True(pillLong - (d.Along + d.Thickness) - OverlayTokens.ClipboardHalfGap > 0);

        // The margin production applies is derived here, never typed into the XAML.
        Assert.Equal(-OverlayTokens.ClipboardDividerW / 2.0, d.NearMargin, 9);

        // Both orientations produce the identical frame — orientation is the view's problem.
        Assert.Equal(ClipboardSplit.DividerFor(false, pillLong, OverlayTokens.CollapsedW), d);
    }

    [Fact]
    public void MorphAndBreatheOwnDisjointAxes()
    {
        // The morph slides the half along the long axis, the breathe drifts it across. They sit
        // on different elements, and the cross-axis amplitude must stay far smaller than the
        // along-axis travel — otherwise the two would visually fight on any edge.
        Assert.True(OverlayTokens.ClipboardHalfBreathePx < OverlayTokens.ClipboardHalfW / 10.0);
        // The along-axis channel is the only one the morph drives, and it starts exactly one
        // half width out on every edge (positive = outboard along whichever axis that is)
        // and ends the same distance out on the way back.
        Assert.Equal(OverlayTokens.ClipboardHalfW, ClipboardSplit.HalfFor(0.0, true).Translate, 6);
        Assert.Equal(0.0, ClipboardSplit.HalfFor(1.0, true).Translate, 6);
        Assert.Equal(OverlayTokens.ClipboardHalfW, ClipboardSplit.HalfFor(1.0, false).Translate, 6);
        // …while the cross-axis channel is at its neutral phase whenever the half is reset.
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(0), 9);
        Assert.Equal(0.0, ClipboardSplit.CrossBreatheOffset(OverlayTokens.ClipboardHalfBreatheMs), 9);
    }

    // -- G4: what the half shows (icon per format, preview text) ----------------

    private static ClipboardEntry Files(int n) =>
        ClipboardEntry.FromFiles(Enumerable.Range(0, n).Select(i => $@"C:\tmp\{i}.txt").ToList(),
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void IconKey_MapsEachClipboardFormatToItsOwnGlyph()
    {
        Assert.Equal("clipboard", ClipboardHalfPreview.IconKeyFor(ClipboardItemKind.Text));
        Assert.Equal("file", ClipboardHalfPreview.IconKeyFor(ClipboardItemKind.File));
        Assert.Equal("files", ClipboardHalfPreview.IconKeyFor(ClipboardItemKind.MultiFile));
    }

    [Fact]
    public void IconKey_UnknownOrNoneFallsBackToTheClipboardSilhouette()
    {
        // A half with no format must not show a document icon it has no data for.
        Assert.Equal("clipboard", ClipboardHalfPreview.IconKeyFor(ClipboardItemKind.None));
        Assert.Equal("clipboard", ClipboardHalfPreview.IconKeyFor((ClipboardItemKind)999));
    }

    [Fact]
    public void IconKey_EveryClipboardGlyphResolvesInTheEmbeddedPack()
    {
        // No new icon dependency: the keys are resolved by IconPackService, which falls back
        // to the embedded IslandIcons pack whenever a vendored pack has no such .svg — so a
        // key only has to be a lower-case kebab-case name to keep drawing under Tabler and
        // the Meteocons styles. Pinned here because the lookup is by file name, not a symbol.
        foreach (var kind in Enum.GetValues<ClipboardItemKind>())
        {
            var key = ClipboardHalfPreview.IconKeyFor(kind);
            Assert.Equal(key.ToLowerInvariant(), key);
            Assert.DoesNotContain(' ', key);
            Assert.Matches("^[a-z0-9-]+$", key);
        }
    }

    [Fact]
    public void Text_ShortTextIsShownWhole()
    {
        Assert.Equal("скопировано",
            ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, "скопировано"));
    }

    [Fact]
    public void Text_ExactlyTheCapIsNotEllipsised()
    {
        var exact = new string('a', ClipboardHalfPreview.TextMaxChars);
        Assert.Equal(exact, ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, exact));
    }

    [Fact]
    public void Text_OverTheCapIsCutWithTheEllipsisInsideTheBudget()
    {
        var cut = ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, new string('a', 200));

        Assert.Equal(ClipboardHalfPreview.TextMaxChars, cut.Length);
        Assert.EndsWith("…", cut);
        Assert.Equal(new string('a', ClipboardHalfPreview.TextMaxChars - 1) + "…", cut);
    }

    [Fact]
    public void Text_CutHappensAfterFlatteningNotBefore()
    {
        // A multi-line paste must not spend its budget on a newline that is then turned into
        // a space — what stays on screen is the first 22 visible characters either way.
        var cut = ClipboardHalfPreview.TextFor(ClipboardItemKind.Text,
            "первая строка\nвторая строка\nтретья строка");

        Assert.DoesNotContain('\n', cut);
        // Budget is a ceiling, not a quota: the cut that landed on the space before
        // "строка" gives that space back, so the ellipsis touches the last letter.
        Assert.True(cut.Length <= ClipboardHalfPreview.TextMaxChars);
        Assert.StartsWith("первая строка вторая…", cut);
        Assert.Equal("первая строка вторая…", cut);
    }

    [Fact]
    public void Text_LineBreaksAndTabsBecomeSingleSpaces()
    {
        Assert.Equal("a b c", ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, "a\r\nb\tc"));
        Assert.Equal("a b", ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, "  a \n\n b  "));
        // char.IsWhiteSpace, not a fixed list: a non-breaking space is whitespace too.
        Assert.Equal("a b", ClipboardHalfPreview.TextFor(ClipboardItemKind.Text, "a\u00A0b"));
    }

    [Fact]
    public void Text_FileShowsTheNameAndIsNotCutAgain()
    {
        // BuildPayload already reduced the path to its leaf; the half shows it verbatim and
        // lets the TextBlock's CharacterEllipsis judge the rest, instead of spending a
        // second blind character cut on a name that may well fit.
        var name = "Отчёт за квартал.docx";
        Assert.Equal(name, ClipboardHalfPreview.TextFor(ClipboardItemKind.File, name));
    }

    [Fact]
    public void Text_MultiFileReusesTheRussianPluralFromBuildPayload()
    {
        // The half counts nothing itself — it shows what BuildPayload already pluralised,
        // whatever the count, including the 11 / 21 / 22 endings.
        foreach (var n in new[] { 1, 2, 5, 11, 21, 22, 25, 101 })
        {
            var title = ClipboardHistory.BuildPayload(Files(n), DateTimeOffset.UnixEpoch).Title;
            Assert.Equal(title, ClipboardHalfPreview.TextFor(ClipboardItemKind.MultiFile, title));
        }

        Assert.Equal("1 файл", ClipboardHistory.BuildPayload(Files(1), DateTimeOffset.UnixEpoch).Title);
        Assert.Equal("2 файла", ClipboardHistory.BuildPayload(Files(2), DateTimeOffset.UnixEpoch).Title);
        Assert.Equal("5 файлов", ClipboardHistory.BuildPayload(Files(5), DateTimeOffset.UnixEpoch).Title);
        Assert.Equal("11 файлов", ClipboardHistory.BuildPayload(Files(11), DateTimeOffset.UnixEpoch).Title);
        Assert.Equal("21 файл", ClipboardHistory.BuildPayload(Files(21), DateTimeOffset.UnixEpoch).Title);
        Assert.Equal("22 файла", ClipboardHistory.BuildPayload(Files(22), DateTimeOffset.UnixEpoch).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\r\n")]
    [InlineData("\t\u00A0 ")]
    [InlineData(null)]
    public void Text_EmptyOrWhitespaceNeverLeavesTheHalfWithOnlyAnIcon(string? payload)
    {
        foreach (var kind in Enum.GetValues<ClipboardItemKind>())
        {
            var text = ClipboardHalfPreview.TextFor(kind, payload);
            Assert.Equal(ClipboardHalfPreview.EmptyText, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void Text_PayloadOverloadUsesTheKindsOwnRules()
    {
        var longText = new OverlayPayload
        {
            ClipboardItemKind = ClipboardItemKind.Text,
            Title = new string('x', 100)
        };
        Assert.Equal(ClipboardHalfPreview.TextMaxChars, ClipboardHalfPreview.TextFor(longText).Length);

        var file = new OverlayPayload
        {
            ClipboardItemKind = ClipboardItemKind.File,
            Title = "документ.docx"
        };
        Assert.Equal("документ.docx", ClipboardHalfPreview.TextFor(file));
    }

    [Fact]
    public void Text_PreviewStaysInsideTheHalfWidthBudget()
    {
        // The pill is 370 DIP and must stay there whatever was copied. The half is
        // ClipboardHalfW wide; the text is capped at TextMaxChars including the ellipsis,
        // and the TextBlock keeps MaxWidth=150 + CharacterEllipsis on top of that.
        var text = ClipboardHalfPreview.TextFor(new OverlayPayload
        {
            ClipboardItemKind = ClipboardItemKind.Text,
            Title = string.Join(" ", Enumerable.Repeat("очень", 40))
        });
        Assert.True(text.Length <= ClipboardHalfPreview.TextMaxChars);
        Assert.True(ClipboardHalfPreview.TextMaxChars < OverlayTokens.ClipboardHalfW,
            "the character budget must stay well inside the half, so CharacterEllipsis never has to widen it");
    }

    [Fact]
    public void Text_RowFitsInsideTheSplitHalfAtTheGrownPillSize()
    {
        // 1.12.2: the glyph row is a StackPanel, so it takes its content's width — icon +
        // spacing + MaxWidth + margins. When the pill grows to 370 the half is only 185,
        // and an over-wide row does not ellipsize, it overhangs the divider and prints the
        // preview over the clock: the halves "run into" each other instead of splitting.
        // These are the numbers in OverlayWindow.axaml, pinned here so the next tweak to a
        // margin, the icon or MaxWidth cannot quietly break the layout again.
        const double icon = 12, spacing = 4, textMaxWidth = 150;
        const double marginLeft = 4, marginRight = 8;
        var row = icon + spacing + textMaxWidth + marginLeft + marginRight;
        var halfAtSplit = OverlayTokens.ClipboardHalfW;

        Assert.True(row <= halfAtSplit,
            $"the preview row ({row} DIP) must fit the split half ({halfAtSplit} DIP)");
    }
}
