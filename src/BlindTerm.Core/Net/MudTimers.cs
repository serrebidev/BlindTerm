namespace BlindTerm.Core.Net;

/// <summary>
/// One named timer at a MUD: after an interval, send a line, and -- unless it was written
/// as a one-shot -- keep doing that until it is taken away.
///
/// Timers are the other half of what a MUD client is expected to do unprompted. A trigger
/// answers something the MUD said; a timer answers the clock: drink before thirst lands,
/// recast before a spell fades, save before the hour the MUD reboots. Mudlet's Timers
/// dialog is where its players keep them. BlindTerm's live where the need turns up, in the
/// command line itself: "/timer sip 45 = drink water" writes one, "/untimer sip" takes it
/// away, "/timers" reads them back.
/// </summary>
public sealed class MudTimer
{
    /// <summary>What it is called: one word, the handle "/untimer" takes.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Seconds between one sending and the next.</summary>
    public int Seconds { get; set; } = 60;

    /// <summary>The line sent when it fires, filled in and split like any typed line.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// True to fire again and again. False to fire once in a session, a little after
    /// connecting -- which is what makes a one-shot a way to say "when I arrive, do this":
    /// it is spent for the session that fired it and fresh again in the next one.
    /// </summary>
    public bool Repeat { get; set; } = true;

    /// <summary>The shortest interval a timer may have. Faster than this is a flood, not a timer.</summary>
    public const int MinimumSeconds = 1;

    /// <summary>The longest: a day.</summary>
    public const int MaximumSeconds = 24 * 60 * 60;

    /// <summary>The longest name that can be stored.</summary>
    public const int MaximumNameLength = 40;

    /// <summary>The longest line a timer can send, the same bound an alias lives under.</summary>
    public const int MaximumCommandLength = MudAlias.MaximumReplacementLength;

    public MudTimer Copy() => new()
    {
        Name = Name,
        Seconds = Seconds,
        Command = Command,
        Repeat = Repeat,
    };

    /// <summary>Brings a timer back within its limits, as <see cref="MudAlias.Clamp"/> does.</summary>
    public void Clamp()
    {
        Name = Trim(Name, MaximumNameLength);
        Command = Trim(Command, MaximumCommandLength);
        Seconds = Math.Clamp(Seconds, MinimumSeconds, MaximumSeconds);
    }

    /// <summary>How an interval reads back: "45 seconds", "2 minutes", "1 hour".</summary>
    public static string DescribeInterval(int seconds)
    {
        if (seconds % 3600 == 0)
            return seconds == 3600 ? "1 hour" : $"{seconds / 3600} hours";
        if (seconds % 60 == 0)
            return seconds == 60 ? "1 minute" : $"{seconds / 60} minutes";
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    private static string Trim(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string trimmed = text.Trim();
        return trimmed.Length > limit ? trimmed[..limit] : trimmed;
    }
}

/// <summary>
/// The timers in force, in the order they were written.
///
/// The book wraps the settings' own list rather than copying it, the way
/// <see cref="MudAliasBook"/> does: what is added or removed here is what gets saved.
/// </summary>
public sealed class MudTimerBook
{
    private readonly List<MudTimer> _timers;

    public MudTimerBook(List<MudTimer> timers)
        => _timers = timers ?? throw new ArgumentNullException(nameof(timers));

    /// <summary>Every timer, in the order they were written.</summary>
    public IReadOnlyList<MudTimer> All => _timers;

    /// <summary>
    /// Writes a timer, replacing the one with the same name if there is one. Two timers
    /// with one name could not both be told apart by "/untimer", so the second writing is
    /// the one that stands.
    /// </summary>
    public void Set(string name, int seconds, string command, bool repeat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var timer = new MudTimer
        {
            Name = name.Trim(),
            Seconds = seconds,
            Command = command.Trim(),
            Repeat = repeat,
        };
        timer.Clamp();
        int existing = _timers.FindIndex(other =>
            string.Equals(other.Name, timer.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _timers[existing] = timer;
        else _timers.Add(timer);
    }

    /// <summary>Takes a timer away. Says whether there was one to take.</summary>
    public bool Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _timers.RemoveAll(other =>
            string.Equals(other.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
    }
}

/// <summary>
/// When each timer next wants to fire, and what is owed when it does.
///
/// The schedule is a snapshot of the book: it is restarted when a session connects and
/// whenever a timer is written or taken away, so a change starts every countdown again
/// rather than leaving some timers standing part-way through an interval nobody can see.
/// Restarting on a change is the honest behaviour for something with no display: the
/// alternative is timers firing at moments worked out from edits the player has forgotten.
///
/// A repeating timer that falls behind -- the machine slept, the window was busy -- fires
/// once and counts its next interval from now. The commands it missed are not owed: a
/// timer that sends "drink" three times at once on waking has not caught anyone up.
/// </summary>
public sealed class MudTimerSchedule
{
    private sealed class Due
    {
        public required MudTimer Timer { get; init; }
        public DateTimeOffset Next { get; set; }
        public bool Spent { get; set; }
    }

    private readonly MudTimerBook _book;
    private readonly List<Due> _due = new();

    public MudTimerSchedule(MudTimerBook book)
        => _book = book ?? throw new ArgumentNullException(nameof(book));

    /// <summary>How many timers can still fire: repeating ones, and one-shots not yet spent.</summary>
    public int ArmedCount => _due.Count(due => !due.Spent);

    /// <summary>Starts every countdown in the book again from <paramref name="now"/>.</summary>
    public void Restart(DateTimeOffset now)
    {
        _due.Clear();
        foreach (MudTimer timer in _book.All)
            _due.Add(new Due { Timer = timer, Next = now.AddSeconds(timer.Seconds) });
    }

    /// <summary>
    /// The commands owed at <paramref name="now"/>, in the order their timers were
    /// written. Firing is the caller's to do: what a timer sends goes through the same
    /// path as a typed line, and that path is the window's, not the schedule's.
    /// </summary>
    public IReadOnlyList<string> CollectDue(DateTimeOffset now)
    {
        var commands = new List<string>();
        foreach (Due due in _due)
        {
            if (due.Spent || due.Next > now) continue;
            commands.Add(due.Timer.Command);
            if (due.Timer.Repeat) due.Next = now.AddSeconds(due.Timer.Seconds);
            else due.Spent = true;
        }
        return commands;
    }
}
