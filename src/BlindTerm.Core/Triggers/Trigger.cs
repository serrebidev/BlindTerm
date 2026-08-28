using System.Text;
using System.Text.Json.Serialization;

namespace BlindTerm.Core.Triggers;

/// <summary>Which kind of session a trigger is watching.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerWhere
{
    /// <summary>Every session: a shell, a handed-over console and a host on the network.</summary>
    Anywhere,

    /// <summary>A shell or a program running in one, and never a MUD.</summary>
    Shell,

    /// <summary>A telnet connection, and never the local shell.</summary>
    Mud,
}

/// <summary>
/// One thing to watch for, and what to do when it happens.
///
/// This is the part of a MUD client a terminal has never had and a blind user needs most: the
/// screen reader reads what arrives in the order it arrives, so the one line that mattered --
/// the build finishing, the health warning, someone saying your name -- goes past in the
/// middle of forty that did not. A trigger is how that line gets to sound different from the
/// rest, or be the only one that makes a sound at all.
///
/// Every action is optional and they all happen together, because the useful ones combine:
/// the line that plays an alarm is usually the same line that should be said in fewer words,
/// and the line worth silencing is the one arriving two hundred times a minute.
/// </summary>
public sealed class Trigger
{
    /// <summary>What this is called in the list. Blank means the pattern is the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What to watch for. Read according to <see cref="Match"/>.</summary>
    public string Pattern { get; set; } = string.Empty;

    public TriggerMatch Match { get; set; } = TriggerMatch.Contains;

    /// <summary>
    /// Whether capitals have to line up. Off by default: nobody remembers whether the MUD
    /// wrote "You are hungry" or "you are hungry", and being wrong means silence.
    /// </summary>
    public bool CaseSensitive { get; set; }

    public bool Enabled { get; set; } = true;

    public TriggerWhere Where { get; set; } = TriggerWhere.Anywhere;

    /// <summary>
    /// What to say when the line matches, with <c>$0</c> for the whole line and <c>$1</c>
    /// onwards for the wildcards. Blank says nothing beyond whatever the line itself was
    /// going to say.
    /// </summary>
    public string Speak { get; set; } = string.Empty;

    /// <summary>
    /// Whether that goes to the front of what is waiting to be spoken, and is spoken at once.
    ///
    /// This is the difference between a warning and a remark. Output is read in batches a
    /// twentieth of a second apart, so a line that matters can otherwise be sitting behind
    /// thirty lines of a build that does not.
    /// </summary>
    public bool SpeakNow { get; set; }

    /// <summary>
    /// Whether the matching line itself is kept out of the speech.
    ///
    /// The line stays in the transcript and can still be read back; it is only not read out
    /// as it arrives. That is the whole of it -- what a MUD calls a gag, and the only way to
    /// keep a channel of chatter from burying everything said to you.
    /// </summary>
    public bool Silence { get; set; }

    /// <summary>A sound file to play, or blank. Any file this machine can play.</summary>
    public string Sound { get; set; } = string.Empty;

    /// <summary>Whether to play the system alert sound. Costs no file and no folder.</summary>
    public bool Beep { get; set; }

    /// <summary>
    /// A line to send back, with the same <c>$1</c> substitutions. This is the automatic half
    /// of a trigger: answering a prompt, or firing back at whatever just hit you.
    /// </summary>
    public string Send { get; set; } = string.Empty;

    /// <summary>
    /// Whether triggers listed after this one are skipped for a line this one matched. The
    /// way to write "everything from this channel, except when it mentions me" is a trigger
    /// for your name above one for the channel.
    /// </summary>
    public bool StopProcessing { get; set; }

    /// <summary>
    /// The shortest gap between two firings, in milliseconds. Zero lets it fire on every
    /// matching line; a few seconds is how an alarm stays an alarm rather than a drone.
    /// </summary>
    public int RepeatAfterMilliseconds { get; set; }

    /// <summary>Longer than any of these fields wants to be, and short enough to store.</summary>
    public const int MaximumTextLength = 2000;

    /// <summary>The longest gap that can be asked for: an hour, well past any sensible one.</summary>
    public const int MaximumRepeatAfterMilliseconds = 60 * 60 * 1000;

    /// <summary>Whether anything at all would happen when this matched.</summary>
    [JsonIgnore]
    public bool DoesSomething =>
        Speak.Length > 0 || Silence || Sound.Length > 0 || Beep || Send.Length > 0;

    /// <summary>What to call this in a list: its name, or the pattern when it has none.</summary>
    [JsonIgnore]
    public string DisplayName => Name.Trim().Length > 0 ? Name.Trim() : Pattern;

    /// <summary>
    /// One sentence describing the whole trigger, for the list a screen reader reads.
    ///
    /// A list of names alone is a list of things whose effect you have to open a dialog to
    /// find out. Everything that matters is said here instead, in the order it is asked
    /// about: what it is, whether it is on, what it watches for, and what it does.
    /// </summary>
    public string Describe()
    {
        var actions = new List<string>();
        if (Speak.Length > 0) actions.Add(SpeakNow ? "says something at once" : "says something");
        if (Silence) actions.Add("keeps the line quiet");
        if (Sound.Length > 0) actions.Add($"plays {Path.GetFileName(Sound)}");
        if (Beep) actions.Add("beeps");
        if (Send.Length > 0) actions.Add($"sends {Send}");
        if (StopProcessing) actions.Add("stops later triggers");

        var text = new StringBuilder(DisplayName);
        text.Append(Enabled ? ". On. " : ". Off. ");
        text.Append(Match switch
        {
            TriggerMatch.Wildcard => "Wildcard ",
            TriggerMatch.Regex => "Regular expression ",
            _ => "Contains ",
        });
        text.Append(Pattern);
        if (Where != TriggerWhere.Anywhere)
            text.Append(Where == TriggerWhere.Mud ? ", on a MUD only" : ", in a shell only");
        text.Append(". ");
        if (actions.Count == 0)
        {
            text.Append("Does nothing.");
        }
        else
        {
            string joined = string.Join(", ", actions);
            text.Append(char.ToUpperInvariant(joined[0])).Append(joined[1..]).Append('.');
        }
        return text.ToString();
    }

    /// <summary>Whether this trigger is one to run in the kind of session now in the window.</summary>
    public bool AppliesTo(TriggerWhere session)
        => Where == TriggerWhere.Anywhere || session == TriggerWhere.Anywhere || Where == session;

    public Trigger Copy() => new()
    {
        Name = Name,
        Pattern = Pattern,
        Match = Match,
        CaseSensitive = CaseSensitive,
        Enabled = Enabled,
        Where = Where,
        Speak = Speak,
        SpeakNow = SpeakNow,
        Silence = Silence,
        Sound = Sound,
        Beep = Beep,
        Send = Send,
        StopProcessing = StopProcessing,
        RepeatAfterMilliseconds = RepeatAfterMilliseconds,
    };

    /// <summary>
    /// Brings a trigger back within its limits rather than refusing it.
    ///
    /// This runs over a file that may have been edited by hand or written by a later version.
    /// A trigger with a field too long in it is a trigger to trim, not a reason for every
    /// preference in the file to be thrown away and the terminal to start as though it had
    /// never been configured.
    /// </summary>
    public void Clamp()
    {
        Name = Trim(Name);
        // Shorter than the rest: past this a pattern is refused when it is compiled, so
        // trimming it to anything longer would only store something that can never match.
        Pattern = Trim(Pattern, TriggerPattern.MaximumLength);
        Speak = Trim(Speak);
        Sound = Trim(Sound);
        Send = Trim(Send);
        if (!Enum.IsDefined(Match)) Match = TriggerMatch.Contains;
        if (!Enum.IsDefined(Where)) Where = TriggerWhere.Anywhere;
        RepeatAfterMilliseconds = Math.Clamp(RepeatAfterMilliseconds, 0, MaximumRepeatAfterMilliseconds);
    }

    private static string Trim(string? text, int limit = MaximumTextLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > limit ? text[..limit] : text;
    }
}
