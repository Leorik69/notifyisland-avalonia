using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Stage 8. The notification row used to be a single TextBlock holding "title · body" in one
/// weight and one ink, and nothing constrained its width — a horizontal StackPanel measures
/// children with infinite width, so TextTrimming never engaged. These pin the two rules that
/// replaced that: which text goes where, and how the available width is divided.
/// </summary>
public class NotificationLayoutTests
{
    [Fact]
    public void Split_TitleOnlyLeavesTheBodyEmpty()
    {
        var (title, body) = NotificationLayout.Split(
            new OverlayPayload { Title = "Visual Studio Code" }, "Уведомление");
        Assert.Equal("Visual Studio Code", title);
        Assert.Equal("", body);
    }

    [Fact]
    public void Split_BodyIsTakenFromThePayloadBody()
    {
        var (title, body) = NotificationLayout.Split(
            new OverlayPayload { Title = "Сборка", Body = "Проект собран" }, "Уведомление");
        Assert.Equal("Сборка", title);
        Assert.Equal("Проект собран", body);
    }

    [Fact]
    public void Split_FallsBackToSubtitleWhenThereIsNoBody()
    {
        // This is the fallback the overlay already applied inline — it is where most toasts get
        // their second line from, so losing it would silently blank half the notifications.
        var (title, body) = NotificationLayout.Split(
            new OverlayPayload { Title = "Почта", Subtitle = "Новое письмо" }, "Уведомление");
        Assert.Equal("Почта", title);
        Assert.Equal("Новое письмо", body);
    }

    [Fact]
    public void Split_BlankTitleFallsBackToTheKindName()
    {
        var (title, _) = NotificationLayout.Split(new OverlayPayload { Title = "   " }, "Ошибка");
        Assert.Equal("Ошибка", title);
    }

    [Fact]
    public void Split_TitleIsTrimmed()
    {
        var (title, _) = NotificationLayout.Split(new OverlayPayload { Title = "  Сборка  " }, "Уведомление");
        Assert.Equal("Сборка", title);
    }

    [Theory]
    [InlineData("Одна\nдве", "Одна две")]
    [InlineData("много   пробелов", "много пробелов")]
    [InlineData("табы\tи\nпереносы", "табы и переносы")]
    public void Collapse_FoldsWhitespaceRunsIntoOneSpace(string input, string expected)
    {
        Assert.Equal(expected, NotificationLayout.Collapse(input));
    }

    [Fact]
    public void Collapse_NewlineCannotBecomeASecondLine()
    {
        // The island is 30 DIP tall and morphs on one axis only, so a toast body that carried a
        // newline would ask for a line the pill cannot show. The body is folded to one line.
        var body = NotificationLayout.Collapse("Первая строка\nВторая строка");
        Assert.DoesNotContain("\n", body);
        Assert.Equal("Первая строка Вторая строка", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void Collapse_BlankBecomesEmpty(string? input)
    {
        Assert.Equal("", NotificationLayout.Collapse(input));
    }

    [Fact]
    public void SplitWidths_TitleGetsTheLargerShare()
    {
        var (title, body) = NotificationLayout.SplitWidths(300, actionsVisible: false, hasBody: true);
        Assert.True(title > body, $"title {title} must outrank body {body}");
        Assert.True(title + NotificationLayout.TitleBodyGap + body <= 300);
    }

    [Fact]
    public void SplitWidths_NoBodyGivesTheTitleEverything()
    {
        var (title, body) = NotificationLayout.SplitWidths(300, actionsVisible: false, hasBody: false);
        Assert.Equal(300, title);
        Assert.Equal(0, body);
    }

    [Fact]
    public void SplitWidths_ActionNeverStealsFromTheTitle()
    {
        // Stage 8 §6: actions must appear without shoving the message sideways. The title keeps
        // the width it had; the body is what gives ground.
        var (titleBefore, bodyBefore) = NotificationLayout.SplitWidths(300, false, true);
        var (titleAfter, bodyAfter) = NotificationLayout.SplitWidths(300, true, true);
        Assert.Equal(titleBefore, titleAfter, 3);
        Assert.True(bodyAfter < bodyBefore);
    }

    [Fact]
    public void SplitWidths_TextStillFitsWithTheActionVisible()
    {
        var (title, body) = NotificationLayout.SplitWidths(300, actionsVisible: true, hasBody: true);
        Assert.True(title + NotificationLayout.TitleBodyGap + body
                    <= 300 - OverlayTokens.NotifActionW + 0.001,
            $"title {title} + gap + body {body} must fit beside the {OverlayTokens.NotifActionW} DIP action");
    }

    [Fact]
    public void SplitWidths_TinyWidthNeverGoesNegative()
    {
        var (title, body) = NotificationLayout.SplitWidths(20, actionsVisible: true, hasBody: true);
        Assert.True(title >= 0 && body >= 0);
    }

    [Fact]
    public void SplitWidths_ZeroWidthIsSafe()
    {
        var (title, body) = NotificationLayout.SplitWidths(0, actionsVisible: false, hasBody: true);
        Assert.True(title >= 0 && body >= 0);
    }

    [Fact]
    public void SplitWidths_RussianAndEnglishTakesTheSamePath()
    {
        // The rule is a width share, not a character count, so a longer Russian word cannot push
        // the body out of the row the way a per-character cap would.
        var ru = NotificationLayout.Split(new OverlayPayload { Title = "Уведомлений", Body = "Очень длинный текст" }, "Уведомление");
        var en = NotificationLayout.Split(new OverlayPayload { Title = "Notification", Body = "A very long body text" }, "Notification");
        Assert.NotEqual(ru.Title, en.Title);
        var (ruTitle, ruBody) = NotificationLayout.SplitWidths(280, false, true);
        var (enTitle, enBody) = NotificationLayout.SplitWidths(280, false, true);
        Assert.Equal(ruTitle, enTitle, 3);
        Assert.Equal(ruBody, enBody, 3);
    }

    [Fact]
    public void Split_ToastHeadlineLeadsTheBody()
    {
        var (title, body) = NotificationLayout.Split(
            new OverlayPayload { Title = "Windows PowerShell", Subtitle = "Проверка сигнала", Body = "Остров должен показать" },
            "Уведомление", joinHeadline: true);
        Assert.Equal("Windows PowerShell", title);
        Assert.Equal("Проверка сигнала · Остров должен показать", body);
    }

    [Fact]
    public void Split_WithoutJoinTheBodyStaysAlone()
    {
        var (_, body) = NotificationLayout.Split(
            new OverlayPayload { Title = "Батарея", Subtitle = "15%", Body = "Подключите зарядку" }, "Уведомление");
        Assert.Equal("Подключите зарядку", body);
    }

    [Fact]
    public void Split_HeadlineEqualToBodyOrTitleIsNotRepeated()
    {
        var same = NotificationLayout.Split(new OverlayPayload { Title = "App", Subtitle = "Готово", Body = "Готово" }, "x", joinHeadline: true);
        Assert.Equal("Готово", same.Body);
        var dup = NotificationLayout.Split(new OverlayPayload { Title = "Готово", Subtitle = "Готово", Body = "3 файла" }, "x", joinHeadline: true);
        Assert.Equal("3 файла", dup.Body);
    }
}
