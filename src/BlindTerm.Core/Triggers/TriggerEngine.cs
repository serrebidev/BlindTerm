namespace BlindTerm.Core.Triggers;

/// <summary>Something a trigger asked to have said.</summary>
/// <param name="Text">The words, with the wildcards already filled in.</param>
/// <param name="Now">Whether it goes ahead of everything waiting and is spoken at once.</param>
public readonly record struct TriggerSpeech(string Text, bool Now);

/// <summary>
/// Everything a batch of lines asked for, gathered up before any of it happens.
///
/// Deciding and doing are separate so that the deciding can be tested without a sound card, a
/// screen reader or a terminal on the other end -- and so that the window applies a whole
/// batch in one known order rather than in whatever order the lines arrived.
/// </summary>
public sealed class TriggerOutcome
{
    private readonly HashSet<string> _silenced = new(StringComparer.Ordinal);

    /// <summary>What to say, in the order the lines that asked for it arrived.</summary>
    public List<TriggerSpeech> Speech { get; } = new();

    /// <summary>Sound files to play.</summary>
    public List<string> Sounds { get; } = new();

    /// <summary>Lines to send back to the far end.</summary>
    public List<string> Sends { get; } = new();

    /// <summary>How many alert sounds to play. One, however many triggers asked.</summary>
    public bool Beep { get; set; }

    /// <summary>
    /// Anything the user needs telling about the triggers themselves, rather than about the
    /// output: a pattern that will not compile, or one that has been paused for running away.
    /// </summary>
    public List<string> Notes { get; } = new();

    /// <summary>Whether any line in this batch is to be kept out of the speech.</summary>
    public bool AnySilenced => _silenced.Count > 0;

    internal void Silence(string line) => _silenced.Add(line.Trim());

    /// <summary>
    /// Whether a line about to be spoken was silenced by a trigger.
    ///
    /// Compared on the trimmed text rather than by position: by the time this is asked, the
    /// line has been through the parts that decide what is worth saying, and it is the words
    /// that are recognisable, not where they came from.
    /// </summary>
    public bool IsSilenced(string? line)
        => line is not null && _silenced.Count > 0 && _silenced.Contains(line.Trim());

    /// <summary>Whether this batch asked for anything at all.</summary>
    public bool IsEmpty => Speech.Count == 0 && Sounds.Count == 0 && Sends.Count == 0
                           && !Beep && Notes.Count == 0 && _silenced.Count == 0;
}

/// <summary>
/// Watches lines go past and works out what the user's triggers want done about them.
///
/// The engine owns three things worth keeping in one place: the compiled patterns, so a
/// pattern is turned into a matcher once rather than once per line; the time each trigger
/// last fired, so a trigger asked to wait between firings can be told when it may go again;
/// and the run-away guard, without which one trigger that sends a line the far end echoes
/// back is an endless loop between this terminal and a MUD.
/// </summary>
public sealed class TriggerEngine
{
    /// <summary>
    /// How many times a trigger may fire in <see cref="BurstWindow"/> before it is paused.
    ///
    /// A trigger that sends something is the dangerous one: a MUD echoes what it is sent, the
    /// echo matches the pattern again, and the two ends spend the evening shouting at each
    /// other. High enough that a busy fight does not trip it, low enough that a loop is
    /// stopped within a second of starting.
    /// </summary>
    public const int BurstLimit = 20;

    /// <summary>The window the burst is counted over.</summary>
    public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(2);

    private sealed class Compiled
    {
        public required Trigger Trigger { get; init; }
        public required TriggerPattern? Pattern { get; init; }
        public DateTimeOffset LastFired { get; set; }
        public DateTimeOffset BurstStarted { get; set; }
        public int BurstCount { get; set; }
        public bool Paused { get; set; }
    }

    private readonly TimeProvider _time;
    private readonly List<Compiled> _triggers = new();
    private readonly List<string> _problems = new();

    public TriggerEngine(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>
    /// Whether triggers run at all. The master switch: one place to go when a trigger set is
    /// getting in the way, without having to find and turn off the one that is doing it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Patterns that would not compile, named so the user can go and fix them.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>How many triggers are loaded, whether or not each one is switched on.</summary>
    public int Count => _triggers.Count;

    /// <summary>
    /// Replaces the whole set, compiling every pattern once.
    ///
    /// A trigger whose pattern will not compile is kept in the list -- it is still the user's
    /// trigger, and it is still in the dialog to be corrected -- but it never matches, and it
    /// is named in <see cref="Problems"/> so that saying so is possible.
    /// </summary>
    public void Load(IEnumerable<Trigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        _triggers.Clear();
        _problems.Clear();

        foreach (Trigger trigger in triggers)
        {
            bool compiled = TriggerPattern.TryCompile(
                trigger.Pattern, trigger.Match, trigger.CaseSensitive,
                out TriggerPattern? pattern, out string? problem);
            if (!compiled) _problems.Add($"{trigger.DisplayName}: {problem}");
            _triggers.Add(new Compiled { Trigger = trigger, Pattern = pattern });
        }
    }

    /// <summary>
    /// Runs a batch of finished lines past every trigger and returns what they asked for.
    ///
    /// Lines in the order they arrived, and triggers in the order the user listed them: that
    /// is what makes "stop checking later triggers" mean something, and it is the only
    /// ordering anyone can reason about when a set grows past a handful.
    /// </summary>
    /// <param name="lines">Lines the terminal has finished. A line still being printed is not one.</param>
    /// <param name="session">What kind of far end the window is showing.</param>
    public TriggerOutcome Run(IReadOnlyList<string> lines, TriggerWhere session)
    {
        var outcome = new TriggerOutcome();
        if (!Enabled || _triggers.Count == 0 || lines is null || lines.Count == 0) return outcome;

        DateTimeOffset now = _time.GetUtcNow();
        foreach (string line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            foreach (Compiled compiled in _triggers)
            {
                Trigger trigger = compiled.Trigger;
                if (!trigger.Enabled || compiled.Pattern is null) continue;
                if (!trigger.AppliesTo(session)) continue;
                if (compiled.Pattern.Match(line) is not { } capture) continue;

                if (!MayFire(compiled, now, outcome)) continue;

                Fire(trigger, capture, outcome);
                if (trigger.StopProcessing) break;
            }
        }

        return outcome;
    }

    /// <summary>
    /// Whether a trigger that matched is allowed to act, given how recently it last did.
    ///
    /// Two separate limits, because they answer different questions. The wait the user asked
    /// for is a preference: an alarm every ten seconds instead of on every line. The burst
    /// limit is a safety catch nobody asked for and everybody needs, and when it trips the
    /// trigger is switched off for the rest of the session and said so, rather than silently
    /// throttled -- a trigger that has stopped working for a reason the user cannot hear is
    /// worse than one that never ran.
    /// </summary>
    private bool MayFire(Compiled compiled, DateTimeOffset now, TriggerOutcome outcome)
    {
        if (compiled.Paused) return false;

        int wait = compiled.Trigger.RepeatAfterMilliseconds;
        if (wait > 0 && compiled.LastFired != default
            && now - compiled.LastFired < TimeSpan.FromMilliseconds(wait))
            return false;

        if (compiled.BurstStarted == default || now - compiled.BurstStarted > BurstWindow)
        {
            compiled.BurstStarted = now;
            compiled.BurstCount = 0;
        }

        if (++compiled.BurstCount > BurstLimit)
        {
            compiled.Paused = true;
            compiled.Trigger.Enabled = false;
            outcome.Notes.Add(
                $"The trigger {compiled.Trigger.DisplayName} fired {BurstLimit} times in "
                + "two seconds and has been switched off. Open Triggers to look at it.");
            return false;
        }

        compiled.LastFired = now;
        return true;
    }

    private static void Fire(Trigger trigger, TriggerCapture capture, TriggerOutcome outcome)
    {
        if (trigger.Silence) outcome.Silence(capture.Line);

        if (trigger.Speak.Length > 0)
        {
            string spoken = capture.Expand(trigger.Speak).Trim();
            if (spoken.Length > 0) outcome.Speech.Add(new TriggerSpeech(spoken, trigger.SpeakNow));
        }

        if (trigger.Sound.Length > 0) outcome.Sounds.Add(trigger.Sound);
        if (trigger.Beep) outcome.Beep = true;

        if (trigger.Send.Length > 0)
        {
            string sent = capture.Expand(trigger.Send);
            // A line with a newline in it would be several commands, one of which the user
            // did not write down and cannot see. One line is one line.
            outcome.Sends.Add(sent.Replace("\r", string.Empty).Replace("\n", " "));
        }
    }
}
