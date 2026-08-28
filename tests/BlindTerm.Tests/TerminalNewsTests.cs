using BlindTerm.Core;
using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

/// <summary>
/// What the window says as a terminal changes, decided over whole batches rather than per
/// source. These are the announcements a user actually hears, in order.
/// </summary>
public class TerminalNewsTests
{
    [Fact]
    public void APromptIsNotReadAgainWhenAConnectionTakesTheWindowOver()
    {
        // Exactly what typing "telnet host 4000" at a shell prompt does: the window writes the
        // command and a progress line itself, then the takeover sends the cursor off the
        // prompt row -- which turns the prompt into a transcript line holding the same words
        // the user was already told about.
        var core = new TerminalCore(80, 25);
        var news = new TerminalNews();
        var spoken = new List<string>();
        core.Updated += update => spoken.AddRange(news.News(update));

        core.Feed("PS C:\\> "u8);

        news.SuppressCommandEcho("telnet host 4000");
        spoken.AddRange(news.News(core.AppendExternal(["telnet host 4000", "Connecting to host:4000..."])));

        core.Feed("\r\n"u8);
        core.Feed("Welcome to Core MUD\r\n"u8);

        Assert.Equal(["PS C:\\>", "Connecting to host:4000...", "Welcome to Core MUD"], spoken);
    }

    [Fact]
    public void AnOrdinaryPromptIsStillAnnouncedOnce()
    {
        var core = new TerminalCore(80, 25);
        var news = new TerminalNews();
        var spoken = new List<string>();
        core.Updated += update => spoken.AddRange(news.News(update));

        core.Feed("hello\r\n"u8);
        core.Feed("PS C:\\> "u8);

        Assert.Equal(["hello", "PS C:\\>"], spoken);
    }

    [Fact]
    public void AMudPromptIsNotReadBackBeforeEveryReply()
    {
        // A MUD reprints its prompt after each reply, so the row the prompt was read from
        // becomes a transcript line once per command. Hearing ">" before every answer is the
        // loudest kind of nothing.
        var core = new TerminalCore(80, 25);
        var news = new TerminalNews();
        var spoken = new List<string>();
        core.Updated += update => spoken.AddRange(news.News(update));

        core.Feed("Welcome\r\n> "u8);

        // The MUD does not echo what is typed, so its reply opens with the newline that ends
        // the prompt row -- and that is exactly what turns the prompt into a transcript line
        // holding the same character the user has already been told they are sitting at.
        news.SuppressCommandEcho("look");
        core.Feed("\r\nA room.\r\n> "u8);
        news.SuppressCommandEcho("who");
        core.Feed("\r\nNobody here.\r\n> "u8);

        Assert.Equal(["Welcome", ">", "A room.", "Nobody here."], spoken);
    }

    [Fact]
    public void LinesTheAppWritesDoNotMakeThePromptNewAgain()
    {
        var news = new TerminalNews();
        Assert.Equal(["Password:"], news.News(new TerminalUpdate { LiveText = "Password:" }));

        var written = new TerminalUpdate { External = true };
        written.NewLines.Add("[Disconnected]");
        Assert.Equal(["[Disconnected]"], news.News(written));

        // The far end has not printed anything since, so there is nothing new to say about it.
        Assert.Empty(news.News(new TerminalUpdate { LiveText = "Password:" }));
    }

    [Fact]
    public void AQuietLineIsRecordedWithoutBeingRead()
    {
        // Where a MUD's account of the room and the character goes: into the transcript at the
        // moment it happened, without being read out over whatever caused it.
        var news = new TerminalNews();
        var quiet = new TerminalUpdate { External = true, Quiet = true };
        quiet.NewLines.Add("[Apartment, South Dome. Exits: north.]");

        Assert.Empty(news.News(quiet));

        // And it is not owed later either: the line has been seen, it was simply not spoken.
        var again = new TerminalUpdate { External = true };
        again.NewLines.Add("[Apartment, South Dome. Exits: north.]");
        Assert.Empty(news.News(again));
    }

    [Fact]
    public void AWrappedPromptIsSuppressedByItsLastRowOnly()
    {
        // Live text can be several rows. Only the last of them becomes the transcript line
        // that would otherwise repeat.
        var news = new TerminalNews();
        var update = new TerminalUpdate { LiveText = "Choose one of:\nWhich?" };
        Assert.Equal(["Choose one of:\nWhich?"], news.News(update));

        var committed = new TerminalUpdate();
        committed.NewLines.Add("Which?");
        Assert.Empty(news.News(committed));
    }
}
