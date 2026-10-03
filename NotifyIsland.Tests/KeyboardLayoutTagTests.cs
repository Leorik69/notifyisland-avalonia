using Xunit;

namespace NotifyIsland.Tests;

public class KeyboardLayoutTagTests
{
    [Theory]
    [InlineData("RU", "🇷🇺")]
    [InlineData("ru", "🇷🇺")]
    [InlineData("US", "🇺🇸")]
    [InlineData("DE", "🇩🇪")]
    [InlineData("KR", "🇰🇷")]
    public void FlagFor_TwoLetterCountryCode_YieldsRegionalIndicators(string code, string expected)
    {
        Assert.Equal(expected, KeyboardLayoutTag.FlagFor(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("R")]
    [InlineData("RUS")]
    [InlineData("R1")]
    public void FlagFor_AnythingUnusable_IsEmptyRatherThanWrong(string? code)
    {
        // A wrong flag is worse than no flag: the user is told which country they are typing in.
        Assert.Equal(string.Empty, KeyboardLayoutTag.FlagFor(code));
    }

    [Fact]
    public void Describe_BuildsFlagAndLanguageName()
    {
        var info = KeyboardLayoutTag.Describe("ru-RU");
        Assert.NotNull(info);
        Assert.Equal("ru-RU", info!.LocaleTag);
        Assert.Equal("🇷🇺", info.Flag);
        Assert.Equal("RU", info.Region);
        Assert.NotEqual(string.Empty, info.Language);
    }

    [Fact]
    public void Describe_UsesTheShortNativeNameNotTheFullDisplayName()
    {
        // "English (United States)" is a sentence, in a 9 DIP badge, beside a flag that already
        // says US. The badge wants the language's own name and nothing else.
        var en = KeyboardLayoutTag.Describe("en-US");
        Assert.NotNull(en);
        Assert.Equal("English", en!.Language);
        Assert.DoesNotContain("(", en.Language);

        var de = KeyboardLayoutTag.Describe("de-DE");
        Assert.NotNull(de);
        Assert.Equal("Deutsch", de!.Language);
    }

    [Fact]
    public void Describe_TagWithoutRegion_GivesALanguageAndNoFlag()
    {
        var info = KeyboardLayoutTag.Describe("ru");
        Assert.NotNull(info);
        Assert.Equal(string.Empty, info!.Flag);
        Assert.NotEqual(string.Empty, info.Language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-RU")]
    [InlineData("abcdefghijkl")]
    public void Describe_NothingToSay_ReturnsNull(string? tag)
    {
        Assert.Null(KeyboardLayoutTag.Describe(tag));
    }

    [Fact]
    public void Describe_UnknownTag_StillNamesSomething()
    {
        // A tag this runtime cannot resolve must degrade to the raw subtag, not to nothing: a
        // blank chip teaches the user nothing and looks like a bug.
        var info = KeyboardLayoutTag.Describe("zz-ZZ");
        Assert.NotNull(info);
        Assert.Equal("zz-ZZ", info!.LocaleTag);
    }

    [Fact]
    public void Gate_FirstObservation_PrimesSilently()
    {
        // Priming is the reason this class exists: without it, starting the island would announce
        // whatever layout is already active, every single time.
        var gate = new KeyboardLayoutTag.Gate();
        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0));
    }

    [Fact]
    public void Gate_LayoutChangesWhileTyping_ShowsIt()
    {
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);

        var shown = gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 500);
        Assert.NotNull(shown);
        Assert.Equal("en-US", shown!.LocaleTag);
        Assert.Equal("🇺🇸", shown.Flag);
        Assert.Equal("US", shown.Region);
    }

    [Fact]
    public void Gate_LayoutChangesWithNoTyping_ShowsNothing()
    {
        // A language-bar hover changes the layout without the user typing. Announcing that is noise.
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);

        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 30_000));
    }

    [Fact]
    public void Gate_UnknownIdleTime_IsTreatedAsNotTyping()
    {
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);
        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: -1));
    }

    [Fact]
    public void Gate_SameLayoutAgain_ShowsNothing()
    {
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);
        Assert.NotNull(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 100));
        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 100));
    }

    [Fact]
    public void Gate_SwallowingAChangeWhileSilent_DoesNotReannounceItLater()
    {
        // The layout moved while nobody was typing. When the user finally starts typing, that
        // stale layout is what they are on, and re-announcing it as brand new would be wrong —
        // they never switched to it, it was already there when they came back.
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);
        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 60_000));

        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 100));
    }

    [Fact]
    public void Gate_UnreadableTick_DoesNotDisturbThePrimedLayout()
    {
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);

        Assert.Null(gate.Observe(null, idleMs: 100));
        Assert.Equal("ru-RU", gate.Current);

        // …and the next real change is still recognised.
        Assert.NotNull(gate.Observe(KeyboardLayoutTag.Describe("de-DE"), idleMs: 100));
    }

    [Fact]
    public void Gate_Reset_MakesTheNextObservationPrimeAgain()
    {
        var gate = new KeyboardLayoutTag.Gate();
        gate.Observe(KeyboardLayoutTag.Describe("ru-RU"), idleMs: 0);
        gate.Reset();
        Assert.Equal(string.Empty, gate.Current);
        Assert.Null(gate.Observe(KeyboardLayoutTag.Describe("en-US"), idleMs: 0));
    }
}
