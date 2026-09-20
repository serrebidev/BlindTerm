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
