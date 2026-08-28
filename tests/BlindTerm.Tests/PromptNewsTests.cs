using BlindTerm.Core;
using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

public class PromptNewsTests
{
    [Theory]
    [InlineData("By what name is your character known?")]
    [InlineData("Please enter a name for your character:")]
    [InlineData("CORE>")]
    [InlineData("Do you want to upgrade Ruby? (y/N)")]
    [InlineData("Do you want to compile assets? (default: no)")]
    public void CompletePromptsAreAnnounced(string prompt)
    {
        var news = new PromptNews();

        Assert.Equal([prompt], news.News(prompt));
        Assert.Empty(news.News(prompt));
    }

    [Fact]
    public void ABashChoicePromptIsAnnouncedWithoutANewline()
    {
        var core = new TerminalCore(80, 25);
        var news = new TerminalNews();
        var spoken = new List<string>();
        core.Updated += update => spoken.AddRange(news.News(update));

        core.Feed("Updating packages...\r\n"u8);
        core.Feed("Do you want to compile assets? (y/N) "u8);

        Assert.Equal(["Updating packages...", "Do you want to compile assets? (y/N)"], spoken);
    }

    [Fact]
    public void APartialPromptWaitsForItsPunctuation()
    {
        var news = new PromptNews();

        Assert.Empty(news.News("By what name is your"));
        Assert.Equal(["By what name is your character known?"],
            news.News("By what name is your character known? "));
    }

    [Fact]
    public void ParenthesizedProgressIsNotMistakenForAChoicePrompt()
    {
        var news = new PromptNews();

        Assert.Empty(news.News("Downloading package (1/4)"));
        Assert.Empty(news.News("Checking Ruby (yes/no)"));
    }

    [Fact]
    public void AFurtherQuestionOnTheSameLineIsAnnouncedByItself()
    {
        // A MUD login writes its questions one after another onto the same unfinished line.
        // Reading the whole line each time makes every answer replay the conversation.
        var news = new PromptNews();

        Assert.Equal(["By what name is your character known?"],
            news.News("By what name is your character known?"));
        Assert.Equal(["Password:"],
            news.News("By what name is your character known? Password:"));
        Assert.Equal(["Reconnected."],
            news.News("By what name is your character known? Password: Reconnected."));
    }

    [Fact]
    public void AReplacedPromptIsAnnouncedWhole()
    {
        var news = new PromptNews();

        Assert.Equal(["PS C:\\Windows>"], news.News("PS C:\\Windows>"));
        Assert.Equal(["PS C:\\Users>"], news.News("PS C:\\Users>"));
    }

    [Fact]
    public void TheSamePromptCanBeAnnouncedAgainAfterTheCurrentLineClears()
    {
        var news = new PromptNews();

        Assert.Single(news.News("Password:"));
        Assert.Empty(news.News(string.Empty));
        Assert.Single(news.News("Password:"));
    }

    [Theory]
    [InlineData("Password:")]
    [InlineData("Enter your passphrase")]
    [InlineData("PIN? ")]
    public void SecretPromptsAreRecognized(string prompt)
        => Assert.True(PromptNews.RequestsSecret(prompt));

    [Fact]
    public void OrdinaryPromptsDoNotRequestPasswordEntry()
        => Assert.False(PromptNews.RequestsSecret("Please enter a name for your character:"));
}
