using BlindTerm.Core.Speech;
using Xunit;

namespace BlindTerm.Tests;

/// <summary>
/// Screen mode follows the cursor, not the screen. These pin down the difference, because
/// getting it wrong is what makes a terminal unusable in an editor rather than merely noisy:
/// nano repaints its status and shortcut bars constantly, and none of that is what the user
/// pressed a key to hear.
/// </summary>
public class ScreenNewsTests
{
    private static string[] Screen(params string[] rows) => rows;

    [Fact]
    public void SpeaksTheCursorLineWhenTheProgramTakesTheScreen()
    {
        var news = new ScreenNews();
        var result = news.News(Screen("first", "second", "third"), cursorRow: 1, cursorColumn: 0);
        Assert.Equal("second", result.Text);
    }

    [Fact]
    public void DoesNotSpeakAnInitialBlankCursorRow()
    {
        var news = new ScreenNews();
        Assert.True(news.News(Screen("", "GNU nano 8.4 file.txt"), 0, 0).IsEmpty);
    }

    [Fact]
    public void SpeaksTheNewRowWhenTheCursorMoves()
    {
        var news = new ScreenNews();
        var screen = Screen("first", "second", "third");
        news.News(screen, 0, 0);

        Assert.Equal("second", news.News(screen, 1, 0).Text);
        Assert.Equal("third", news.News(screen, 2, 0).Text);
    }

    [Fact]
    public void StaysSilentWhenOnlyAStatusBarChanges()
    {
        // This is the case that matters. In nano, a keystroke that does nothing to the text
        // still repaints the status line, and reading it out every time is intolerable.
        var news = new ScreenNews();
        news.News(Screen("the text", "", "[ line 1/1 ]"), 0, 0);

        var result = news.News(Screen("the text", "", "[ line 1/1, col 1 ]"), 0, 0);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void SpeaksOffCursorChangesOnlyWhenAskedTo()
    {
        var news = new ScreenNews { SpeakOffCursorChanges = true };
        news.News(Screen("the text", "status one"), 0, 0);

        var result = news.News(Screen("the text", "status two"), 0, 0);
        Assert.Equal("status two", result.Text);
    }

    [Fact]
    public void LeavesCharacterEchoToTheScreenReader()
    {
        // NVDA and JAWS own keyboard echo, including the user's character/word echo setting.
        var news = new ScreenNews();
        news.News(Screen("hello worl"), 0, 10);

        Assert.True(news.News(Screen("hello world"), 0, 11).IsEmpty);
    }

    [Fact]
    public void LeavesHorizontalMovementToTheScreenReader()
    {
        var news = new ScreenNews();
        var screen = Screen("hello");
        news.News(screen, 0, 0);

        Assert.True(news.News(screen, 0, 1).IsEmpty);
        Assert.True(news.News(screen, 0, 2).IsEmpty);
    }

    [Fact]
    public void DoesNotSynthesizeWordEcho()
    {
        var news = new ScreenNews();
        var screen = Screen("alpha beta gamma");
        news.News(screen, 0, 0);

        Assert.True(news.News(screen, 0, 6).IsEmpty);
    }

    [Fact]
    public void DoesNotSynthesizeBackwardEcho()
    {
        var news = new ScreenNews();
        var screen = Screen("hello");
        news.News(screen, 0, 3);

        Assert.True(news.News(screen, 0, 2).IsEmpty);
    }

    [Fact]
    public void CursorMovementInterrupts()
    {
        // The user pressed a key and is waiting to hear where they landed; queueing that
        // behind a screenful of repaint is the same as not saying it.
        var news = new ScreenNews();
        var screen = Screen("first", "second");
        news.News(screen, 0, 0);

        Assert.Equal(SpeechPriority.Now, news.News(screen, 1, 0).Priority);
    }

    [Fact]
    public void AnnouncesBlankWhenTheCursorMovesToAnEmptyRow()
    {
        var news = new ScreenNews();
        news.News(Screen("text", ""), 0, 0);

        Assert.Equal("blank, line 2", news.News(Screen("text", ""), 1, 0).Text);
    }

    [Fact]
    public void WholeScreenDropsBlankPadding()
        => Assert.Equal("one\ntwo", ScreenNews.Whole(Screen("one", "", "   ", "two", "")));

    [Theory]
    [InlineData("GNU nano 8.4                                           ./test.txt *", "./test.txt")]
    [InlineData("GNU nano 8.4                                           New Buffer", "New Buffer")]
    public void ReadsNanoFileNameFromTitle(string title, string expected)
        => Assert.Equal(expected, ScreenNews.NanoFileName(Screen(title)));

    [Theory]
    [InlineData("Save modified buffer?", "Save modified buffer?")]
    [InlineData("File Name to Write: ./test.txt", "File Name to Write: ./test.txt")]
    public void FindsNanoAnswerPrompts(string row, string expected)
        => Assert.Equal(expected, ScreenNews.NanoPrompt(Screen("GNU nano 8.4 ./test.txt", row)));

    [Fact]
    public void IgnoresNanoStatusRowsThatDoNotAskForInput()
        => Assert.Null(ScreenNews.NanoPrompt(Screen("GNU nano 8.4 ./test.txt", "^G Help   ^O Write Out")));
}
