using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// The listener hands back a flat, ordered list of text elements tagged with a content kind, not
/// a title/body pair. These pin the fold that turns that list into the two strings the island
/// shows — the mapping is positional as well as by kind, and a toast read the wrong way round
/// shows the app name as the headline and the person as the body.
/// </summary>
public class ToastTextTests
{
    private static ToastTextElement E(string kind, string text) => new(kind, text);

    [Fact]
    public void Fold_GenericToast_TitleBecomesHeadlineAndBodyTheBody()
    {
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Иван"),
            E(ToastText.KindSubtitle, "Slack"),
            E(ToastText.KindBody, "встреча в 10"),
        });

        Assert.Equal("Иван", headline);
        // The attribution is NOT thrown away: with a headline already taken it is body material.
        Assert.Equal("Slack\nвстреча в 10", body);
    }

    [Fact]
    public void Fold_SubtitleAloneBecomesTheHeadline()
    {
        // A toast with no Text element at all: the attribution is all the headline there is.
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindSubtitle, "Slack"),
            E(ToastText.KindBody, "встреча в 10"),
        });

        Assert.Equal("Slack", headline);
        Assert.Equal("встреча в 10", body);
    }

    [Fact]
    public void Fold_BodyAloneStaysTheBodyAndLeavesTheHeadlineEmpty()
    {
        // Deliberately NOT promoted to the headline. The row is app name / headline / body, so
        // a body-only toast already reads as "Slack" + its text. Promoting would move the text
        // into the headline slot and shift every other line down with it.
        var (headline, body) = ToastText.Fold(new[] { E(ToastText.KindBody, "Собрание перенесено") });

        Assert.Equal(string.Empty, headline);
        Assert.Equal("Собрание перенесено", body);
    }

    [Fact]
    public void Fold_FirstTextWinsAndLaterTextsJoinTheBody()
    {
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Ivan"),
            E(ToastText.KindText, "Channel #general"),
        });

        Assert.Equal("Ivan", headline);
        Assert.Equal("Channel #general", body);
    }

    [Fact]
    public void Fold_TextRepeatedAfterTheHeadlineIsNotEchoedIntoTheBody()
    {
        // Windows repeats the headline in the body binding of some templates. Showing it twice
        // on a one-line row reads as a stutter, not as a repeated line of real content.
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Backup finished"),
            E(ToastText.KindBody, "Backup finished"),
        });

        Assert.Equal("Backup finished", headline);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public void Fold_SameSubtitleTwiceIsAddedToTheBodyOnlyOnce()
    {
        var (_, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Ivan"),
            E(ToastText.KindSubtitle, "Slack"),
            E(ToastText.KindSubtitle, "Slack"),
        });

        Assert.Equal("Slack", body);
    }

    [Fact]
    public void Fold_EmptyAndWhitespaceElementsAreSkipped()
    {
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "   "),
            E(ToastText.KindBody, ""),
            E(ToastText.KindText, "  Запуск завершён  "),
        });

        Assert.Equal("Запуск завершён", headline);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public void Fold_UnknownKindIsKeptRatherThanDroppedSilently()
    {
        // Undefined / future kinds are real. Dropping them is how a toast ends up showing half
        // of itself with no trace of the other half.
        var (headline, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Deploy"),
            E("Undefined", "staging"),
        });

        Assert.Equal("Deploy", headline);
        Assert.Equal("staging", body);
    }

    [Fact]
    public void Fold_UnknownKindAloneStillProducesAHeadline()
    {
        var (headline, _) = ToastText.Fold(new[] { E("Undefined", "что-то") });
        Assert.Equal("что-то", headline);
    }

    [Fact]
    public void Fold_EmptyInputGivesAnEmptyPair()
    {
        // A silent toast carries no bindings at all. The empty pair is the signal the feed
        // rejects on, so it has to be empty and not null.
        var (headline, body) = ToastText.Fold(Array.Empty<ToastTextElement>());
        Assert.Equal(string.Empty, headline);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public void Fold_NullInputGivesAnEmptyPair()
    {
        var (headline, body) = ToastText.Fold(null);
        Assert.Equal(string.Empty, headline);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public void Fold_NullTextIsTreatedAsEmptyRatherThanThrowing()
    {
        var (headline, body) = ToastText.Fold(new[] { E(ToastText.KindText, null!) });
        Assert.Equal(string.Empty, headline);
        Assert.Equal(string.Empty, body);
    }

    [Fact]
    public void Fold_LongBodyIsCutAndMarked()
    {
        var (_, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Title"),
            E(ToastText.KindBody, new string('x', ToastText.MaxBodyChars + 500)),
        });

        Assert.Equal(ToastText.MaxBodyChars + 1, body.Length); // the ellipsis
        Assert.EndsWith("…", body);
    }

    [Fact]
    public void Fold_BodyExactlyAtTheLimitIsNotTouched()
    {
        // The cut must not fire one character early: a body that fits has to come through
        // byte-for-byte, or every short toast gains a pointless ellipsis.
        var exact = new string('y', ToastText.MaxBodyChars);
        var (_, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Title"),
            E(ToastText.KindBody, exact),
        });

        Assert.Equal(exact, body);
    }

    [Fact]
    public void Fold_ManyBodyElementsAreJoinedWithNewlinesInOrder()
    {
        var (_, body) = ToastText.Fold(new[]
        {
            E(ToastText.KindText, "Deploy finished"),
            E(ToastText.KindBody, "line one"),
            E(ToastText.KindBody, "line two"),
            E(ToastText.KindBody, "line three"),
        });

        Assert.Equal("line one\nline two\nline three", body);
    }
}
