using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The tray tooltip is the only place that tells the user the island stopped receiving Windows
/// notifications, so what it is allowed to say — and when it is allowed to say it — is worth
/// pinning down.
/// </summary>
public class TrayTooltipTextTests
{
    [Fact]
    public void Quiet_ShowsJustTheName()
    {
        Assert.Equal("NotifyIsland", TrayTooltipText.For(0, 0, false, false));
    }

    [Fact]
    public void Unread_IsCappedAtTheOldLimit()
    {
        Assert.Equal("NotifyIsland (7)", TrayTooltipText.For(7, 0, false, false));
        Assert.Equal("NotifyIsland (99)", TrayTooltipText.For(99, 0, false, false));
        Assert.Equal("NotifyIsland (99)", TrayTooltipText.For(1000, 0, false, false));
    }

    [Fact]
    public void Pause_IsShownOnlyWhileItIsActuallyPaused()
    {
        Assert.Equal("NotifyIsland", TrayTooltipText.For(0, 0, false, false));
        Assert.Equal("NotifyIsland — пауза 30 мин", TrayTooltipText.For(0, 30, false, false));
    }

    [Fact]
    public void APendingAnswerIsNeverRenderedAsDenied()
    {
        // The regression this guards: during the first second of every launch the platform has
        // not answered, and saying "уведомления выкл" then would be a lie about a state the
        // user has not been asked about yet.
        Assert.DoesNotContain("выкл", TrayTooltipText.For(0, 0, listenerKnown: false, listenerDenied: false));
    }

    [Fact]
    public void DeniedAndUnavailableAreWordedDifferently()
    {
        Assert.Equal("NotifyIsland — уведомления Windows выкл",
            TrayTooltipText.For(0, 0, listenerKnown: true, listenerDenied: true));
        Assert.Equal("NotifyIsland — уведомления Windows недоступны",
            TrayTooltipText.For(0, 0, listenerKnown: true, listenerDenied: false));
    }

    [Fact]
    public void AllThreeStatesComposeAtOnce()
    {
        // The point of having one owner: the two tray classes used to overwrite the whole string,
        // so the unread count and the pause could never both be visible.
        var text = TrayTooltipText.For(3, 12, listenerKnown: true, listenerDenied: true);
        Assert.Equal("NotifyIsland (3) — пауза 12 мин — уведомления Windows выкл", text);
    }

    [Fact]
    public void StaysInsideTheWinFormsTooltipCap()
    {
        // NotifyIcon.Text is 127 characters. Worst case here is far shorter, but the cap is why
        // the count is clamped at 99 in the first place.
        var text = TrayTooltipText.For(99, 999, listenerKnown: true, listenerDenied: true);
        Assert.True(text.Length <= 127, $"tooltip too long: {text.Length}");
    }
}
