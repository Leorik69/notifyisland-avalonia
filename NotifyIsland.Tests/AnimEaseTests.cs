using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NotifyIsland.Tests;

public class AnimEaseTests
{
    private static readonly string[] Core =
    {
        "linear",
        "power1.in", "power1.out", "power1.inOut",
        "power2.in", "power2.out", "power2.inOut",
        "power3.in", "power3.out", "power3.inOut",
        "power4.in", "power4.out", "power4.inOut",
        "sine.inOut", "expo.out", "circ.out", "back.out",
        "clickPop",
    };

    [Theory]
    [InlineData("linear")]
    [InlineData("power1.in")]
    [InlineData("power1.out")]
    [InlineData("power1.inOut")]
    [InlineData("power2.in")]
    [InlineData("power2.out")]
    [InlineData("power2.inOut")]
    [InlineData("power3.in")]
    [InlineData("power3.out")]
    [InlineData("power3.inOut")]
    [InlineData("power4.in")]
    [InlineData("power4.out")]
    [InlineData("power4.inOut")]
    [InlineData("sine.in")]
    [InlineData("sine.out")]
    [InlineData("sine.inOut")]
    [InlineData("expo.in")]
    [InlineData("expo.out")]
    [InlineData("expo.inOut")]
    [InlineData("circ.in")]
    [InlineData("circ.out")]
    [InlineData("circ.inOut")]
    [InlineData("back.in")]
    [InlineData("back.out")]
    [InlineData("back.inOut")]
    public void MonotonicCurves_StartAtZeroAndEndAtOne(string name)
    {
        // clickPop is deliberately excluded: it is a scale curve around 1.0 (1 → peak → 1), it has
        // its own tests below and in AnimationEasingTests.
        Assert.Equal(0.0, AnimEase.Ease(name, 0.0), 6);
        Assert.Equal(1.0, AnimEase.Ease(name, 1.0), 6);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("power1.in")]
    [InlineData("power1.out")]
    [InlineData("power1.inOut")]
    [InlineData("power2.in")]
    [InlineData("power2.out")]
    [InlineData("power2.inOut")]
    [InlineData("power3.in")]
    [InlineData("power3.out")]
    [InlineData("power3.inOut")]
    [InlineData("power4.in")]
    [InlineData("power4.out")]
    [InlineData("power4.inOut")]
    [InlineData("sine.inOut")]
    [InlineData("expo.out")]
    [InlineData("circ.out")]
    public void Curves_AreMonotonicInTime(string name)
    {
        var prev = AnimEase.Ease(name, 0.0);
        for (var i = 1; i <= 200; i++)
        {
            var v = AnimEase.Ease(name, i / 200.0);
            Assert.True(v >= prev - 1e-9, $"{name} went backwards at t={i / 200.0}");
            prev = v;
        }
    }

    [Theory]
    [InlineData("power1.in")]
    [InlineData("power2.in")]
    [InlineData("power3.in")]
    [InlineData("power4.in")]
    public void PowerIn_AreRealPowersOfIncreasingDegree(string name)
    {
        // GSAP semantics: powerN is degree N+1. Scaling one curve "by eye" would pass a start/end
        // test but not this one — the point of real degrees is that power4 must be visibly sharper.
        for (var i = 1; i < 100; i++)
        {
            var t = i / 100.0;
            var expected = Math.Pow(t, double.Parse(name.Substring(5, 1)) + 1);
            Assert.Equal(expected, AnimEase.Ease(name, t), 9);
        }
    }

    [Theory]
    [InlineData("power1.out")]
    [InlineData("power2.out")]
    [InlineData("power3.out")]
    [InlineData("power4.out")]
    public void PowerOut_AreRealPowersOfIncreasingDegree(string name)
    {
        for (var i = 1; i < 100; i++)
        {
            var t = i / 100.0;
            var expected = 1 - Math.Pow(1 - t, double.Parse(name.Substring(5, 1)) + 1);
            Assert.Equal(expected, AnimEase.Ease(name, t), 9);
        }
    }

    [Fact]
    public void HigherPower_IsSharperThanLowerPower()
    {
        for (var i = 5; i < 95; i++)
        {
            var t = i / 100.0;
            Assert.True(
                AnimEase.Ease("power4.in", t) < AnimEase.Ease("power3.in", t),
                $"power4.in should lag power3.in at t={t}");
            Assert.True(
                AnimEase.Ease("power4.out", t) > AnimEase.Ease("power3.out", t),
                $"power4.out should lead power3.out at t={t}");
        }
    }

    [Theory]
    [InlineData("power1.inOut")]
    [InlineData("power2.inOut")]
    [InlineData("power3.inOut")]
    [InlineData("power4.inOut")]
    [InlineData("sine.inOut")]
    [InlineData("expo.inOut")]
    [InlineData("circ.inOut")]
    [InlineData("back.inOut")]
    public void InOutCurves_AreSymmetricAroundTheMidpoint(string name)
    {
        // The property that actually makes a curve feel balanced: f(0.5) = 0.5 and f(1-t) = 1-f(t).
        Assert.Equal(0.5, AnimEase.Ease(name, 0.5), 6);
        for (var i = 1; i < 100; i++)
        {
            var t = i / 100.0;
            var mirrored = AnimEase.Ease(name, 1.0 - t);
            Assert.Equal(1.0 - AnimEase.Ease(name, t), mirrored, 6);
        }
    }

    [Fact]
    public void BackOut_OvershootsOne_ThenSettles()
    {
        var peak = Enumerable.Range(0, 501).Select(i => AnimEase.Ease("back.out", i / 500.0)).Max();
        Assert.True(peak > 1.0, "back.out is supposed to fly past the target and come back");
        Assert.Equal(1.0, AnimEase.Ease("back.out", 1.0), 6);
    }

    [Fact]
    public void BackIn_StartsSlowAndOvershootsBackwards()
    {
        Assert.True(AnimEase.Ease("back.in", 0.2) < 0.0, "back.in dips below 0 before rising");
    }

    [Fact]
    public void ClickPop_GoesThroughTheDictionary_AndIsTheExistingCurve()
    {
        // The dictionary must reach the live curve instead of carrying a second copy of it.
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            Assert.Equal(AnimationEasing.ClickPop(t), AnimEase.Ease("clickPop", t), 12);
        }
    }

    [Fact]
    public void ClickPop_NeverDipsBelowOne()
    {
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            Assert.True(AnimEase.Ease("clickPop", t) >= 1.0, $"clickPop dipped below 1.0 at t={t}");
        }
    }

    [Fact]
    public void ClickPop_PeaksAtClickPopPeak()
    {
        var peak = Enumerable.Range(0, 1001).Select(i => AnimEase.Ease("clickPop", i / 1000.0)).Max();
        Assert.Equal(OverlayTokens.ClickPopPeak, peak, 3);
    }

    [Fact]
    public void Ease_ClampsT()
    {
        Assert.Equal(AnimEase.Ease("power2.out", 0.0), AnimEase.Ease("power2.out", -3.0), 12);
        Assert.Equal(AnimEase.Ease("power2.out", 1.0), AnimEase.Ease("power2.out", 7.0), 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("power9.out")]
    [InlineData("back.out(1.7)")] // the overshoot is a constant, not part of the name
    [InlineData("  power2.out  ")]
    public void UnknownName_FallsBackInsteadOfThrowing(string name)
    {
        // Documented decision: Ease() runs inside a per-frame morph tick, where a throw would kill the
        // loop and freeze the island mid-size. A sane curve is a much smaller bug than a hang.
        var fallback = AnimEase.Ease(OverlayTokens.EaseFallbackName, 0.42);
        Assert.Equal(fallback, AnimEase.Ease(name, 0.42), 12);
    }

    [Fact]
    public void NameLookup_IsCaseInsensitive()
    {
        Assert.Equal(AnimEase.Ease("power2.out", 0.42), AnimEase.Ease("POWER2.OUT", 0.42), 12);
        Assert.Equal(AnimEase.Ease("clickPop", 0.42), AnimEase.Ease("clickpop", 0.42), 12);
    }

    [Fact]
    public void UnknownName_FallsBackForNullAndMissing()
    {
        var fallback = AnimEase.Ease(OverlayTokens.EaseFallbackName, 0.31);
        Assert.Equal(fallback, AnimEase.Ease(null, 0.31), 12);
        Assert.Equal(fallback, AnimEase.Ease("nope", 0.31), 12);
    }

    [Fact]
    public void Has_IsFalseForUnknownNames()
    {
        Assert.True(AnimEase.Has("power3.inOut"));
        Assert.True(AnimEase.Has("CLICKPOP"));
        Assert.False(AnimEase.Has("power5.out"));
        Assert.False(AnimEase.Has(null));
    }

    [Fact]
    public void Names_ContainTheDocumentedVocabulary()
    {
        foreach (var name in Core)
        {
            Assert.True(AnimEase.Has(name), $"missing curve '{name}'");
        }

        // 1.12.4: "ragged" and "spring.out" arrived with the morph migration. "ragged" is the
        // jitter's decay ENVELOPE, not the jitter (that stays Random per tick — see
        // CapsuleMorphTrack.Ragged), and "spring.out" is the Bounce style's damped spring.
        foreach (var name in new[] { "ragged", "spring.out" })
        {
            Assert.True(AnimEase.Has(name), $"missing curve '{name}'");
        }

        Assert.Equal(1.0 - 0.4, AnimEase.Ease("ragged", 0.4), 12);
        Assert.Equal(AnimationEasing.SpringOut(0.4), AnimEase.Ease("spring.out", 0.4), 12);
    }

    [Fact]
    public void WithReducedMotion_ReturnsEndState_WhenReduced()
    {
        // 1.0, not the eased value: reduced motion removes the movement, it does not shorten it.
        Assert.Equal(1.0, AnimEase.WithReducedMotion(0.0, true), 12);
        Assert.Equal(1.0, AnimEase.WithReducedMotion(0.37, true), 12);
        Assert.Equal(1.0, AnimEase.WithReducedMotion(1.4, true), 12);
    }

    [Fact]
    public void WithReducedMotion_PassesValueThrough_WhenNotReduced()
    {
        Assert.Equal(0.37, AnimEase.WithReducedMotion(0.37, false), 12);
    }

    [Fact]
    public void WithReducedMotion_IsIdempotent()
    {
        var once = AnimEase.WithReducedMotion(AnimEase.Ease("power3.out", 0.2), true);
        Assert.Equal(once, AnimEase.WithReducedMotion(once, true), 12);
    }

    [Fact]
    public void UnknownFallbackToken_IsResolvable()
    {
        // The fallback constant lives in OverlayTokens, so a rename there must not silently become
        // a second fallback path.
        Assert.True(AnimEase.Has(OverlayTokens.EaseFallbackName));
    }
}
