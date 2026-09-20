using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using BlindTerm.Core;
using BlindTerm.Core.Mud;
using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;
using BlindTerm.Core.Triggers;
using BlindTerm.Core.Updates;
using Xunit;

namespace BlindTerm.Tests;

/// <summary>
/// The bugs found in a pass over the whole program, one test each.
///
/// Kept together because they have nothing in common but that: each is a small, specific
/// thing that was wrong, in a different corner, and each would go wrong again quietly if the
/// reasoning behind the fix were lost. Every one of them failed before the fix.
/// </summary>
public class AuditFixTests
{
    // ---- Names two directories are joined on ----

    [Theory]
    // An article is a word. Stripping the letters "the" off the front of a name that merely
    // begins with them sets two unrelated games up to share each other's player counts, which
    // is the one thing this join is written to avoid.
    [InlineData("Thera", "Ra")]
    [InlineData("Threshold RPG", "RPG")]
    [InlineData("Them", "M")]
    public void AnArticleIsOnlyTakenOffWhenItIsTheWholeWord(string one, string other)
        => Assert.NotEqual(MudMerge.Key(one), MudMerge.Key(other));

    [Fact]
    public void AListingWithNoAddressDoesNotLendItsEncryptedPort()
    {
        // This one publishes an encrypted port and no way to reach it: a web-only listing.
        var offline = Game("Webby", host: "", port: 0, tlsPort: 4000);
        var real = Game("Real", host: "mud.example", port: 23, tlsPort: null);

        MudGame merged = MudMerge.Fill(offline, real);

        Assert.Equal("mud.example", merged.Host);
        Assert.Equal(23, merged.Port);
        // The address came from the second game, and so must anything that belongs to it. A
        // port kept from the first offers a connection to a server that was never about this.
        Assert.Null(merged.TlsPort);
    }

    [Fact]
    public void AnEncryptedPortIsNotBorrowedFromADifferentHost()
    {
        // Two directories naming the same game do not always agree on which machine serves it.
        var one = Game("Alter Aeon", "alteraeon.com", 23, null);
        var other = Game("Alter Aeon", "ae.example.net", 4000, 7443);

        MudGame merged = MudMerge.Fill(one, other);

        Assert.Equal("alteraeon.com", merged.Host);
        Assert.Equal(23, merged.Port);
        // 7443 was published for ae.example.net, which is not the host this listing dials.
        Assert.Null(merged.TlsPort);
    }

    [Fact]
    public void AnEncryptedPortIsKeptWhenItIsTheSameHost()
    {
        var one = Game("Alter Aeon", "alteraeon.com", 23, null);
        var other = Game("Alter Aeon", "alteraeon.com", 4000, 7443);

        Assert.Equal(7443, MudMerge.Fill(one, other).TlsPort);
    }

    // ---- Addresses ----

    [Theory]
    [InlineData("http://mud.example.com:80")]
    [InlineData("https://mud.example.com")]
    [InlineData("ftp://mud.example.com:21")]
    public void ASchemeThisDoesNotSpeakIsRefusedRatherThanBecomeAHost(string written)
        => Assert.False(TelnetAddress.TryParse(written, out _, out _));

    [Theory]
    [InlineData("ssl://mud.example.com:4000", "mud.example.com", 4000, true)]
    [InlineData("mud.example.com:4000", "mud.example.com", 4000, false)]
    [InlineData("[::1]:4000", "::1", 4000, false)]
    [InlineData("::1", "::1", 23, false)]
    public void TheAddressesThatAreAddressesStillParse(string written, string host, int port, bool secure)
    {
        Assert.True(TelnetAddress.TryParse(written, out string actualHost, out int actualPort,
                                           out bool actualSecure));
        Assert.Equal(host, actualHost);
        Assert.Equal(port, actualPort);
        Assert.Equal(secure, actualSecure);
    }

    // ---- What a server may name ----

    [Theory]
    [InlineData("NUL.wav")]
    [InlineData("nul.wav")]
    [InlineData("COM1.wav")]
    [InlineData("LPT1.mp3")]
    [InlineData("CON.wav")]
    public void ADeviceNameIsNotASound(string name)
        => Assert.False(SoundLibrary.IsSafeName(name));

    [Theory]
    [InlineData("sword.wav")]
    [InlineData("combat/hit.wav")]
    [InlineData("mp3/msptest.mp3")]
    public void OrdinarySoundNamesAreStillSounds(string name)
        => Assert.True(SoundLibrary.IsSafeName(name));

    [Fact]
    public void AWildcardIsNotAllowedToNameASubfolder()
    {
        var library = new SoundLibrary(Path.GetTempPath());

        // A wildcard in the folder would be the disk walked rather than a folder read, and
        // Windows will not create a directory with one in its name either.
        Assert.Null(library.DestinationFor(Trigger("x.wav", type: "a?b")));
        Assert.NotNull(library.DestinationFor(Trigger("x.wav", type: "combat")));
    }

    // ---- Numbers a server sends ----

    [Fact]
    public void AnAbsurdLoopCountIsBroughtInsideWhatASoundCanOutlive()
    {
        Assert.True(MspTrigger.TryParse(MspKind.Sound, "x.wav L=2147483647", out MspTrigger? trigger));
        Assert.Equal(MspTrigger.MaximumLoops, trigger.Loops);
    }

    [Fact]
    public void TheOneNegativeThatMeansSomethingStillMeansIt()
    {
        Assert.True(MspTrigger.TryParse(MspKind.Sound, "x.wav L=-5", out MspTrigger? trigger));
        Assert.Equal(MspTrigger.Forever, trigger.Loops);
    }

    // ---- The published list ----

    [Fact]
    public void AListingWithANullFieldIsReadRatherThanThrownOver()
    {
        const string json = """
        {"Version":1,"Games":[
          {"Source":"a","SourceId":"1","Name":"Good","Host":"mud.example","Port":23,"Genre":null},
          {"Source":"b","SourceId":"2","Name":"Nowhere","Host":null,"Port":23}
        ]}
        """;

        MudFeed feed = MudFeed.FromJson(json);

        // The one with an address is kept, with the missing word filled in rather than left
        // as null for something downstream to read with .Length.
        MudGame kept = Assert.Single(feed.Games);
        Assert.Equal("Good", kept.Name);
        Assert.Equal(string.Empty, kept.Genre);
    }

    [Fact]
    public void AListingMissingAWholeKeyIsDroppedRatherThanTakingTheListWithIt()
    {
        // Declared as required, an absent key threw out of the deserializer itself -- before
        // the loop written to discard an unusable listing ever saw it -- so one listing edited
        // by hand cost the whole list rather than one row.
        const string json = """
        {"Version":1,"Games":[
          {"Host":"mud.example","Port":23,"Name":"No source named"},
          {"Source":"b","SourceId":"2","Name":"Good","Host":"other.example","Port":23}
        ]}
        """;

        MudFeed feed = MudFeed.FromJson(json);

        Assert.Equal(2, feed.Games.Count);
        Assert.Equal(string.Empty, feed.Games[0].Source);
        Assert.Equal("Good", feed.Games[1].Name);
    }

    // ---- Orders of magnitude ----

    [Fact]
    public void APageFarPastTheEndIsEmptyRatherThanTheFirstPage()
    {
        MudGame[] games = [Game("One", "a.example", 23, null), Game("Two", "b.example", 23, null)];

        MudDirectoryPage page = MudSorting.Page(games, int.MaxValue, 50);

        Assert.Empty(page.Games);
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public void TheFirstPageIsStillTheFirstPage()
    {
        MudGame[] games = [Game("One", "a.example", 23, null), Game("Two", "b.example", 23, null)];

        MudDirectoryPage page = MudSorting.Page(games, 1, 1);

        Assert.Single(page.Games);
        Assert.Equal("One", page.Games[0].Name);
        Assert.True(page.HasMore);
    }

    // ---- The update manifest ----

    [Fact]
    public void AManifestWithNothingInItIsNotNewer()
    {
        // Every one of these used to be a NullReferenceException in Normalize, thrown from a
        // background timer where nothing catches it.
        Assert.False(UpdateClient.IsNewer(null, "0.7.10"));
        Assert.False(UpdateClient.IsNewer(string.Empty, "0.7.10"));
        Assert.False(UpdateClient.IsNewer("not a version", "0.7.10"));
    }

    [Fact]
    public void ARealVersionIsStillComparedAsOne()
    {
        Assert.True(UpdateClient.IsNewer("v0.7.11", "0.7.10"));
        Assert.False(UpdateClient.IsNewer("v0.7.9", "0.7.10"));
    }

    // ---- What a trigger puts back into the line ----

    [Fact]
    public void ANumberPastNineIsTheTenthWildcardRatherThanTheFirstWithAZeroAfterIt()
    {
        Match match = Regex.Match("a-b-c-d-e-f-g-h-i-j-k", @"(\w)-(\w)-(\w)-(\w)-(\w)-(\w)-(\w)-(\w)-(\w)-(\w)-(\w)");
        var capture = new TriggerCapture(match.Value, match);

        Assert.Equal("a", capture.Expand("$1"));
        Assert.Equal("j", capture.Expand("$10"));
        // Still nothing when there is nothing there, and still a literal dollar sign.
        Assert.Equal(string.Empty, capture.Expand("$40"));
        Assert.Equal("$", capture.Expand("$$"));
    }

    [Fact]
    public void ATriggerCannotPostAControlCharacterIntoTheTerminal()
    {
        var engine = new TriggerEngine();
        // The wildcard carries whatever the far end wrote, and $1 puts it straight back into
        // the line being typed. Ctrl+C at a shell and Ctrl+D at a login are both one byte away.
        engine.Load([new Trigger { Pattern = "*", Match = TriggerMatch.Wildcard, Send = "$1" }]);

        TriggerOutcome outcome = engine.Run(["run\u0003this\u0007now"], TriggerWhere.Shell);

        Assert.Equal("runthisnow", Assert.Single(outcome.Sends));
    }

    [Fact]
    public void ANewlineInASentLineIsStillASpaceRatherThanASecondCommand()
    {
        var engine = new TriggerEngine();
        // A line about to be typed can carry a newline in it: the Send box is a box, and two
        // lines written in it are two commands. What is sent is one line.
        engine.Load([new Trigger { Pattern = "*", Match = TriggerMatch.Wildcard, Send = "look\nnorth" }]);

        TriggerOutcome outcome = engine.Run(["anything at all"], TriggerWhere.Shell);

        Assert.Equal("look north", Assert.Single(outcome.Sends));
    }

    [Fact]
    public void AProtectiveTriggerStillProtectsWhileItIsWaiting()
    {
        var engine = new TriggerEngine();
        // "Everyone in this channel, except when it mentions me." The protection is the
        // StopProcessing on the name trigger, and a wait between firings must not quietly
        // hand the line to the trigger below it.
        engine.Load(
        [
            new Trigger
            {
                Pattern = "Karia",
                Match = TriggerMatch.Contains,
                Speak = "your name was mentioned",
                RepeatAfterMilliseconds = 60_000,
                StopProcessing = true,
            },
            new Trigger { Pattern = "[gossip]", Match = TriggerMatch.Contains, Silence = true },
        ]);

        TriggerOutcome named = engine.Run(["[gossip] Karia did the thing"], TriggerWhere.Mud);
        Assert.Single(named.Speech);
        Assert.False(named.IsSilenced("[gossip] Karia did the thing"));

        // Inside the wait, so the name trigger has nothing to say -- but the line is still one
        // it matched, and the channel trigger below it must not run and silence it.
        TriggerOutcome repeated = engine.Run(["[gossip] Karia again"], TriggerWhere.Mud);
        Assert.Empty(repeated.Speech);
        Assert.False(repeated.IsSilenced("[gossip] Karia again"));

        // A line the name trigger does not match is the channel's, and is silenced.
        TriggerOutcome other = engine.Run(["[gossip] somebody else entirely"], TriggerWhere.Mud);
        Assert.True(other.IsSilenced("[gossip] somebody else entirely"));
    }

    // ---- What a server is allowed to send ----

    [Fact]
    public void ATriggerLongEnoughToBeRefusedIsRefusedBeforeItIsCopied()
    {
        string huge = new string('x', MspScanner.MaximumTriggerLength + 20);

        Assert.False(MspTrigger.TryParseLine("!!SOUND(" + huge + ")", out _));
        Assert.False(MspTrigger.TryParse(MspKind.Sound, huge, out _));
        // And the cap is not so tight that an ordinary one stops working.
        Assert.True(MspTrigger.TryParseLine("!!SOUND(sword.wav)", out MspTrigger? ordinary));
        Assert.Equal("sword.wav", ordinary.FileName);
    }

    [Fact]
    public void ATriggerAfterACarriageReturnOnlyLineIsStillATrigger()
    {
        // A host that ends its lines with a bare carriage return has not written the trigger
        // in the middle of a sentence: it is the start of a line, and a trigger left there is
        // read out as punctuation in the middle of a fight.
        var scanner = new MspScanner();
        byte[] received = "hello\r!!SOUND(sword.wav)\r"u8.ToArray();
        var text = new byte[received.Length + MspScanner.Headroom];
        var triggers = new List<MspTrigger>();

        int written = scanner.Scan(received, text, triggers);

        Assert.Equal("sword.wav", Assert.Single(triggers).FileName);
        Assert.Equal("hello\r", Encoding.UTF8.GetString(text, 0, written));
    }

    [Fact]
    public void AHoleInADirectoryIsNotAGameWithNoName()
    {
        IEnumerable<MudGame> withHole = new MudGame[] { Game("One", "a.example", 23, null), null! };
        IEnumerable<MudGame> measuredHole = new MudGame[] { null! };

        Assert.Single(MudMerge.Describe(withHole));
        (IReadOnlyList<MudGame> games, _) = MudMerge.Combine(withHole, measuredHole);
        Assert.Single(games);
    }

    // ---- The transcript the window mirrors ----

    [Fact]
    public void TheTranscriptTextEndsLinesTheWayAnEditControlCountsThem()
    {
        var transcript = new Transcript();
        transcript.Append("one");
        transcript.Append("two");
        transcript.Append("three");

        // A Win32 edit control takes "one\ntwo" as no line break at all: it reports one line,
        // and GetFirstCharIndexFromLine(1) is -1. The mirror is filled from this, so giving it
        // the transcript's own separators left the whole session as a single line to arrow
        // through after coming back from a full-screen program.
        string expected = "one" + Environment.NewLine + "two" + Environment.NewLine
                          + "three" + Environment.NewLine;
        Assert.Equal(expected, transcript.Text());
    }

    [Fact]
    public void ACaretOffsetAllowsForTheSeparatorTheControlCountsTwice()
    {
        var transcript = new Transcript();
        transcript.Append("one");
        transcript.Append("two");

        // The transcript counts one separator per line; the control holds two characters of it.
        Assert.Equal(4, transcript.OffsetOfLine(1));
        Assert.Equal(5, transcript.CaretOffset(1));
        // The end of the document is past the last line's ending, not at the last line's start.
        Assert.Equal(transcript.Text().Length, transcript.CaretLength);
        Assert.Equal(transcript.CaretLength, transcript.CaretOffset(transcript.Count));
    }

    [Fact]
    public void WalkingTheTranscriptGivesACopyRatherThanTheListBeingWritten()
    {
        // The window thread walks the transcript to copy it while the reader thread is still
        // appending to it, and walking a list that is being appended to throws "Collection was
        // modified" -- out of the window thread, which is the end of the program.
        var transcript = new Transcript();
        transcript.Append("one");

        string[] taken = transcript.Snapshot(0);
        transcript.Append("two");

        Assert.Equal(["one"], taken);
        Assert.Equal(["one", "two"], transcript.Snapshot(0));
        // Past the end is empty rather than out of range.
        Assert.Empty(transcript.Snapshot(transcript.Count + 5));
    }

    [Fact]
    public void EveryRowOfAWrappedLineIsReportedAndNotOnlyTheFirst()
    {
        // A shell integration marker can land on a continuation row, and the command block
        // tracker waits on its own row being reported. Told only about the row a wrapped group
        // starts on, a marker there waits for ever and its command reads as "location is not
        // available".
        var core = new TerminalCore(columns: 20, rows: 10);
        var rows = new List<int>();
        var lines = new List<int>();
        core.Builder.RowBecameLine += (row, line) => { rows.Add(row); lines.Add(line); };

        // Forty-five characters in twenty columns wrap twice, and the newline after them is
        // what makes the cursor leave the wrapped run -- while it sits on a continuation row
        // the run is not read at all.
        core.Feed(Encoding.UTF8.GetBytes(new string('a', 45) + "\r\nbb"));

        int top = core.Engine.ScreenTop;
        Assert.True(core.Engine.IsWrapped(top + 1));
        Assert.True(core.Engine.IsWrapped(top + 2));
        Assert.Equal(new[] { top, top + 1, top + 2 }, rows);
        // One line out of three rows, so every one of them has to be told about it.
        Assert.All(lines, line => Assert.Equal(lines[0], line));
    }

    // ---- The published directory ----

    [Fact]
    public async Task TheMudStatsTableIsPagedRatherThanAskedForAllAtOnce()
    {
        // Five thousand rows is what this used to ask for, and the endpoint now answers that
        // with a 500 -- so the entire contribution of the directory that has the player
        // counts arrived as an exception, every run, and the published list carried a thirty
        // day average on none of its listings. A thousand rows is served, so that is what is
        // asked for, and the offset moves.
        var pages = new Pages(StatsPage(0, 1000), StatsPage(1000, 1));
        using var directory = new MudStatsDirectory("https://mudstats.example", new HttpClient(pages));

        IReadOnlyList<MudGame> worlds = await directory.WorldsAsync();

        Assert.Equal(1001, worlds.Count);
        Assert.Equal("World 1000", worlds[^1].Name);
        Assert.Equal(2, pages.Asked.Count);
        Assert.Contains("iDisplayLength=1000", pages.Asked[0].Query, StringComparison.Ordinal);
        Assert.DoesNotContain("iDisplayLength=5000", pages.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains("iDisplayStart=0&", pages.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains("iDisplayStart=1000&", pages.Asked[1].Query, StringComparison.Ordinal);
        // The sort has to keep being asked for by name: a request without it is a 500 as well,
        // which is a thing this endpoint does that DataTables itself never did.
        Assert.Contains("iSortCol_0=7", pages.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains("sSortDir_0=desc", pages.Asked[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBigListIsReadAsUtf8WhateverItsHeaderClaims()
    {
        // The Mud Connector answers with "charset=ISO-8859-1" and then sends UTF-8; its own
        // page says utf-8 in a meta tag three lines in. Taking the header at its word turned
        // every accented character in six hundred listings into two wrong ones, and the name
        // is what the list is sorted by, searched with, and spoken on every arrow press.
        const string html = """
        <table><tbody>
        <tr> <td>29</td>
        <td><a href='https://www.mudconnect.com/cgi-bin/search.cgi?mode=mud_listing&mud=Reinos' data-tooltip='View'>Reinos De Leyenda - Los años oscuros</a></td>
        <td><a href='https://www.mudconnect.com/cgi-bin/telnet.cgi?mud=Reinos&url=telnet://reinos.example.com:4000' data-tooltip='Connect'>reinos.example.com 4000</a></td>
        <td><a href='http://www.mudportal.com/play?host=reinos.example.com&port=4000' target='MudPortal'><i class='play icon'></i></a></td>
        <td><a href='https://www.mudconnect.com/cgi-bin/redirect.cgi?mud=Reinos&url=http://reinos.example.com/' data-tooltip='Website'>http://reinos.example.com/</a></td>
        <td>Connected</td> </tr>
        </tbody></table>
        """;

        using var directory = new MudConnectorDirectory("https://mudconnect.example",
            new HttpClient(new Mislabeled(html)));

        MudGame game = Assert.Single(await directory.GamesAsync());

        Assert.Equal("Reinos De Leyenda - Los años oscuros", game.Name);
        // The mojibake, exactly as it used to arrive: two characters where one belongs.
        Assert.DoesNotContain("Ã", game.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("±", game.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AListingArrivesWithoutTheSpacesItsOwnPageWrappedItIn()
    {
        MudGame trailing = Game("NuclearWarMud ", "nuclearwarmud.example", 4000, null) with
        {
            Intro = "Nuke \r\n",
            Website = "http://nuclearwarmud.example/index.php  ",
        };

        MudGame clean = Assert.Single(MudMerge.Describe([trailing]));

        Assert.Equal("NuclearWarMud", clean.Name);
        Assert.Equal("Nuke", clean.Intro);
        Assert.Equal("http://nuclearwarmud.example/index.php", clean.Website);

        // A website field one directory annotates in place. No address contains a space, so
        // the address is what comes before the first one; offered whole it fails at the far
        // end, with nothing to tell that from a site that has gone.
        MudGame annotated = Game("HexOnyx", "mud.hexonyx.example", 4000, null) with
        {
            Website = "http://mud.hexonyx.example (Needs updating)",
        };
        Assert.Equal("http://mud.hexonyx.example",
            Assert.Single(MudMerge.Describe([annotated])).Website);

        // A blurb written over three lines, which the arrow keys read as one line whatever it
        // says -- so the line endings it arrived with are only ever a stray pause.
        MudGame lines = Game("End of the Line", "eotl.example", 4000, null) with
        {
            Intro = "Two Rules:\r\n1) have fun\r\n2) don't",
        };
        Assert.Equal("Two Rules: 1) have fun 2) don't",
            Assert.Single(MudMerge.Describe([lines])).Intro);
    }

    [Fact]
    public void TheGenreComesWithTheFiguresFromWhoeverHasOne()
    {
        MudGame described = Game("The Lands of Draknor", "draknor.example", 4000, null);
        MudGame measured = Game("The Lands of Draknor", string.Empty, 0, null) with
        {
            Source = "MUDStats",
            Genre = "Wheel of Time",
            StatisticsSource = "MUDStats",
        };

        // Only the game type was carried over from the directory that measures. That left
        // seven hundred and nineteen of seven hundred and fifty-nine published listings with
        // no genre at all -- so the filter that narrows the list by one matched five per cent
        // of it, and the two hundred genres MUDStats publishes arrived as a taxonomy nothing
        // referred to.
        Assert.Equal("Wheel of Time", MudMerge.Enrich(described, measured).Genre);
        // The directory that described the game still has the last word on what it is.
        Assert.Equal("Fantasy", MudMerge.Enrich(described with { Genre = "Fantasy" }, measured).Genre);
    }

    [Fact]
    public void TwoDirectoriesNamingOneMachineAreOneListing()
    {
        // The join on the name cannot hold these together: neither name is wrong and they are
        // not alike enough to key on, so the same server was published twice under two names.
        MudGame fromConnector = Game("Aarchon", "aarchonmud.example", 7000, null) with
        {
            Source = "The Mud Connector",
            Availability = MudAvailability.Online,
            ConfirmedOnline = true,
        };
        MudGame fromGrapevine = Game("Aarchon MUD", "aarchonmud.example", 7000, null) with
        {
            Source = "Grapevine",
            Availability = MudAvailability.Unknown,
        };

        MudGame merged = Assert.Single(MudMerge.Collapse([fromConnector, fromGrapevine]));

        Assert.Equal("Aarchon", merged.Name);
        // And the row a reader lands on says what both directories know, instead of one saying
        // the host answered and the next saying nothing reached it.
        Assert.True(merged.ConfirmedOnline);
        Assert.Equal(MudAvailability.Online, merged.Availability);
    }

    [Fact]
    public void OneDirectoryNamingOneMachineTwiceIsStillTwoListings()
    {
        // The Mud Connector lists two different games on one host and port. Those are its own
        // two games, not a duplicate of anything, and collapsing them would delete a listing.
        MudGame one = Game("Legends of Hatred", "godwars.example", 3500, null) with
        {
            Source = "The Mud Connector",
        };
        MudGame two = Game("Moments of Hatred", "godwars.example", 3500, null) with
        {
            Source = "The Mud Connector",
        };

        Assert.Equal(2, MudMerge.Collapse([one, two]).Count);
    }

    [Fact]
    public void AHostWithASpaceInItIsNotSomethingToDial()
    {
        // One row of the Big List has a stray number inside its telnet link. Nothing else
        // noticed: the host was not empty, so it was published and offered as a connection,
        // and dialling it can only fail with a name that does not resolve.
        MudGame broken = Game("WeyrPast", "216.136.9.8 126", 1260, null);
        Assert.False(broken.CanConnect);
        // Nothing to dial is nowhere to be offered.
        Assert.Equal(string.Empty, broken.Address);

        // And a listing nobody can dial is dropped reading the file, as any other addressless
        // listing is.
        var feed = new MudFeed { Games = [broken, Game("Good", "mud.example.com", 23, null)] };
        Assert.Equal("Good", Assert.Single(MudFeed.FromJson(feed.ToJson()).Games).Name);

        Assert.True(Game("Fine", "mud.example.com", 23, null).CanConnect);
        // An address made of numbers is still an address.
        Assert.True(Game("By number", "216.136.9.8", 1260, null).CanConnect);
    }

    [Fact]
    public void ARankNobodyIsVotingInIsNotSaidToBeThisMonths()
    {
        MudGame frozen = Game("3-Kingdoms", "3k.example", 3000, null) with { Rank = 38, MonthlyVotes = 0 };
        MudGame counted = Game("Quiet", "quiet.example", 4000, null) with { Rank = 1, MonthlyVotes = 200 };

        // "Ranked 38 this month, on 0 votes" was read out about five hundred and sixty-eight
        // listings whose directory stopped counting five years ago: a claim about this month
        // that nothing in the data supports, said as fact to somebody choosing where to play.
        Assert.DoesNotContain("Ranked", frozen.Details, StringComparison.Ordinal);
        Assert.Contains("Ranked 1 this month, on 200 votes", counted.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void TopVotedPutsTheVotesBeforeAStaleRank()
    {
        MudGame stale = Game("Frozen", "frozen.example", 4000, null) with { Rank = 1, MonthlyVotes = 0 };
        MudGame voted = Game("Voted", "voted.example", 4000, null) with { Rank = 900, MonthlyVotes = 5 };

        // Ordering by the rank first answered a question about 2021 while being read as an
        // answer about now, and put every game nobody has voted for above every game somebody
        // has, because only one of the directories is still counting.
        Assert.Equal(["Voted", "Frozen"],
            MudSorting.Apply([stale, voted], MudDirectorySort.TopVoted)
                .Select(game => game.Name).ToArray());
    }

    [Fact]
    public async Task TheListIsAskedForByItsTagSoAnUnchangedOneIsNotDownloadedAgain()
    {
        string folder = Path.Combine(Path.GetTempPath(), "blindterm-tag-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(folder, "mud-directory.json");
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            var cached = new MudFeed
            {
                Generated = DateTimeOffset.UtcNow.AddDays(-2),
                Games = [Game("Busy", "busy.example.com", 4000, null)],
            };
            File.WriteAllText(cache, cached.ToJson());
            File.WriteAllText(cache + ".etag", "\"v1\"");

            var handler = new NotModified();
            using var directory = new MudFeedDirectory("https://feed.example/mud-directory.json",
                cache, new HttpClient(handler));

            MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery());

            // The copy on disk is what a 304 means, and it is used rather than reported.
            Assert.Equal("Busy", Assert.Single(page.Games).Name);
            Assert.Equal("\"v1\"", Assert.Single(handler.Tags));
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task TheTagOfAListThatWasJustFetchedIsKeptForNextTime()
    {
        string folder = Path.Combine(Path.GetTempPath(), "blindterm-tag-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(folder, "mud-directory.json");
        try
        {
            var served = new MudFeed
            {
                Generated = DateTimeOffset.UtcNow,
                Games = [Game("Busy", "busy.example.com", 4000, null)],
            };
            var handler = new Once(served.ToJson(), "\"v1\"");
            using var directory = new MudFeedDirectory("https://feed.example/mud-directory.json",
                cache, new HttpClient(handler));

            await directory.SearchAsync(new MudDirectoryQuery());

            // The tag is only worth keeping beside the copy it belongs to, or the next reader
            // would be told "nothing has changed" about content it has never seen.
            Assert.Equal("\"v1\"", File.ReadAllText(cache + ".etag").Trim());
            Assert.Empty(handler.Tags);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// One page of MUDStats' table, in the shape the endpoint sends it: eleven cells of HTML,
    /// with none of it filled in that this test is not about. The backslashes are JSON's.
    /// </summary>
    private static string StatsPage(int from, int count)
        => "{\"aaData\":[" + string.Join(",", Enumerable.Range(from, count).Select(index =>
            $"""["<a href=\"/World/W{index}\">World {index}</a>","","","","","","","","","",""]"""))
           + "]}";

    /// <summary>Answers each request with the next page, and remembers what was asked for.</summary>
    private sealed class Pages(params string[] bodies) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            string body = bodies[Math.Min(Asked.Count, bodies.Length) - 1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>A page sent as UTF-8 under a header that says it is something else.</summary>
    private sealed class Mislabeled(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "ISO-8859-1" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>Answers "not modified", recording the tag the request carried.</summary>
    private sealed class NotModified : HttpMessageHandler
    {
        public List<string> Tags { get; } = [];
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            foreach (EntityTagHeaderValue tag in request.Headers.IfNoneMatch) Tags.Add(tag.Tag);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        }
    }

    /// <summary>Answers once with a body and an ETag, recording the tag the request carried.</summary>
    private sealed class Once(string body, string tag) : HttpMessageHandler
    {
        public List<string> Tags { get; } = [];
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            foreach (EntityTagHeaderValue carried in request.Headers.IfNoneMatch) Tags.Add(carried.Tag);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new EntityTagHeaderValue(tag);
            return Task.FromResult(response);
        }
    }

    private static MudGame Game(string name, string host, int port, int? tlsPort) => new()
    {
        Source = "a-source",
        SourceId = name,
        Name = name,
        Host = host,
        Port = port,
        TlsPort = tlsPort,
    };

    private static MspTrigger Trigger(string fileName, string? type = null) => new(
        MspKind.Sound,
        fileName,
        Volume: 100,
        Loops: 1,
        Priority: 50,
        Continue: false,
        Type: type,
        Url: null);
}
