using BlindTerm.Core;

namespace BlindTerm.Tests;

public class SessionLogTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 10, 18, 10, 30, 45, TimeSpan.Zero);

    private static string FreshDirectory()
        => Path.Combine(Path.GetTempPath(), $"blindterm-log-{Guid.NewGuid():N}");

    [Fact]
    public void ALogIsNamedForTheSessionAndTheMoment()
    {
        string directory = FreshDirectory();
        try
        {
            Assert.True(SessionLog.TryStart(directory, "coremud.org 4000", Started,
                out SessionLog? log));
            Assert.NotNull(log);
            Assert.Equal("coremud.org-4000-20261018-103045.log",
                Path.GetFileName(log.FilePath));

            log.WriteLine("A large troll blocks the bridge.");
            log.Write(["You say: hello", "The troll grunts."]);
            log.Dispose();

            string[] lines = File.ReadAllLines(log.FilePath);
            Assert.Equal(
                ["A large troll blocks the bridge.", "You say: hello", "The troll grunts."],
                lines);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AnUnnamedSessionIsStillLogged()
    {
        string directory = FreshDirectory();
        try
        {
            Assert.True(SessionLog.TryStart(directory, "  ", Started, out SessionLog? log));
            Assert.NotNull(log);
            Assert.StartsWith("session-", Path.GetFileName(log.FilePath));
            log.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AFolderThatIsAFileFailsQuietly()
    {
        string file = Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}");
        File.WriteAllText(file, "not a folder");
        try
        {
            Assert.False(SessionLog.TryStart(file, "mud", Started, out SessionLog? log));
            Assert.Null(log);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void WritingAfterTheLogIsClosedDoesNotThrow()
    {
        string directory = FreshDirectory();
        try
        {
            Assert.True(SessionLog.TryStart(directory, "mud", Started, out SessionLog? log));
            Assert.NotNull(log);
            log.Dispose();
            log.WriteLine("too late");
            log.Write(["also too late"]);
            log.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
