using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

public class PromptNewsTests
{
    [Theory]
    [InlineData("By what name is your character known?")]
    [InlineData("Please enter a name for your character:")]
    [InlineData("CORE>")]
    public void CompletePromptsAreAnnounced(string prompt)
    {
        var news = new PromptNews();

        Assert.Equal([prompt], news.News(prompt));
        Assert.Empty(news.News(prompt));
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
