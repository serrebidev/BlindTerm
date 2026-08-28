using System.Text.Json;
using System.Text.Json.Serialization;
using BlindTerm.Core.Triggers;

namespace BlindTerm.Core;

/// <summary>User preferences that are safe to persist between sessions.</summary>
public sealed class AppSettings
{
    public string Shell { get; set; } = string.Empty;
    public int Columns { get; set; } = 120;
    public int Rows { get; set; } = 30;

    /// <summary>
    /// Whether to offer to make BlindTerm the default terminal at startup.
    ///
    /// The dialog's "Don't ask me again" box is checked when it opens, so the ordinary
    /// outcome of answering it either way is that this turns off and the question is never
    /// asked again. Unchecking the box is what keeps it coming back.
    /// </summary>
    public bool AskAboutDefaultTerminal { get; set; } = true;

    /// <summary>
    /// Telnet addresses that have been connected to, newest first, as "host", "host:port" or
    /// "ssl://host:port".
    ///
    /// A MUD address is exactly the kind of thing nobody types correctly twice, and arrowing
    /// to a remembered one reads it out. The scheme is part of the entry because a MUD that
    /// offers both puts encryption on a different port, and an address without it would come
    /// back as the wrong connection to the right machine.
    /// </summary>
    public List<string> RecentTelnetHosts { get; set; } = new();

    /// <summary>
    /// A MUDVerse API key, for browsing the directory of MUDs.
    ///
    /// Empty unless somebody puts one here. MUDVerse issues keys for servers and says plainly
    /// not to publish one, and BlindTerm is published, so there is no key inside it to share
    /// out: this is the key belonging to whoever is running this copy. Generating one is free
    /// and takes a minute at https://www.mudverse.com/api.
    /// </summary>
    public string MudDirectoryKey { get; set; } = string.Empty;

    /// <summary>
    /// Where the directory is read from. Blank means MUDVerse itself.
    ///
    /// Here so that a service standing in front of MUDVerse -- holding the key, caching the
    /// answers, and needing no key of its own from anybody -- can be pointed at without a new
    /// release of BlindTerm. See <see cref="BlindTerm.Core.Mud.MudVerseDirectory"/>.
    /// </summary>
    public string MudDirectoryEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Whether a MUD may play sounds through the MUD Sound Protocol.
    ///
    /// Turning this off silences the sounds; it does not put the triggers back into the text,
    /// which are lifted out either way. A line reading "exclamation exclamation SOUND left
    /// paren sword dot wav" is not something anyone wants read to them.
    /// </summary>
    public bool MudSounds { get; set; } = true;

    /// <summary>
    /// Whether output is spoken while the window is in the background.
    ///
    /// Off. A screen reader has one voice for the whole desktop, so a terminal that carries
    /// on talking after it has been left behind is talking over whatever the user went to
    /// read -- and with BlindTerm as the default terminal there can be several of them
    /// running at once. Triggers and the bell are heard either way, because those are things
    /// the user asked to be told about wherever they are.
    /// </summary>
    public bool SpeakInBackground { get; set; }

    /// <summary>Where sound packs live. Blank means the default folder under %APPDATA%.</summary>
    public string SoundDirectory { get; set; } = string.Empty;

    /// <summary>Scales every MUD sound, 0 to 100.</summary>
    public int SoundVolume { get; set; } = 100;

    /// <summary>
    /// Whether a sound a MUD offers may be downloaded when this machine does not have it.
    ///
    /// Off by default: the address comes from the server, and acting on it means fetching a
    /// file it chose and writing it to this disk.
    /// </summary>
    public bool DownloadSounds { get; set; }

    /// <summary>
    /// Whether what a MUD says about the room and the character over GMCP or MSDP is written
    /// into the transcript as it arrives.
    ///
    /// On by default. It is the one place a MUD states its exits as a list rather than as a
    /// sentence to be searched, and putting it in the transcript at the moment it happens is
    /// what makes reading back through a session find it where it belongs.
    /// </summary>
    public bool MudStatus { get; set; } = true;

    /// <summary>
    /// Whether those lines are also read out as they arrive.
    ///
    /// Off by default. A MUD sends the character's vitals after every command, and hearing
    /// them over the fight that is changing them is not an improvement. Alt+V and Alt+X ask
    /// for the same facts at the moment anyone actually wants them.
    /// </summary>
    public bool SpeakMudStatus { get; set; }

    /// <summary>
    /// Things to watch the output for, and what to do about each, in the order they are
    /// checked. See <see cref="BlindTerm.Core.Triggers.Trigger"/>.
    /// </summary>
    public List<Trigger> Triggers { get; set; } = new();

    /// <summary>
    /// The master switch over all of them.
    ///
    /// On by default, because an empty trigger list does nothing anyway, and because someone
    /// who has just written their first trigger should not have to find a second switch
    /// before it works. Turning it off leaves the list alone.
    /// </summary>
    public bool TriggersEnabled { get; set; } = true;

    /// <summary>How many triggers are kept. Far more than anyone lists, and a bound.</summary>
    public const int MaximumTriggers = 500;

    /// <summary>How many remembered addresses are kept.</summary>
    public const int MaximumRecentTelnetHosts = 12;

    /// <summary>Puts an address at the top of the list, without letting it appear twice.</summary>
    public void RememberTelnetHost(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        RecentTelnetHosts.RemoveAll(entry => string.Equals(entry, address, StringComparison.OrdinalIgnoreCase));
        RecentTelnetHosts.Insert(0, address);
        Trim();
    }

    private void Trim()
    {
        if (RecentTelnetHosts.Count > MaximumRecentTelnetHosts)
            RecentTelnetHosts.RemoveRange(MaximumRecentTelnetHosts,
                                          RecentTelnetHosts.Count - MaximumRecentTelnetHosts);
    }

    public void Validate()
    {
        TerminalSize.Validate(Columns, Rows);
        if (Shell is null || Shell.Length > 32_768) throw new ArgumentOutOfRangeException(nameof(Shell));
        if (SoundDirectory is null || SoundDirectory.Length > 32_768)
            throw new ArgumentOutOfRangeException(nameof(SoundDirectory));
        SoundVolume = Math.Clamp(SoundVolume, 0, 100);
        // A settings file edited by hand, or written by a later version, must not be able to
        // make the menu unusable.
        RecentTelnetHosts ??= new List<string>();
        RecentTelnetHosts.RemoveAll(entry => string.IsNullOrWhiteSpace(entry) || entry.Length > 300);
        Trim();
        MudDirectoryKey ??= string.Empty;
        MudDirectoryEndpoint ??= string.Empty;
        if (MudDirectoryKey.Length > 1_000) MudDirectoryKey = string.Empty;
        if (MudDirectoryEndpoint.Length > 1_000) MudDirectoryEndpoint = string.Empty;

        // A trigger with nothing to match is not a trigger, whatever else is on it. Anything
        // else out of range is brought back inside rather than thrown away: these are lines
        // the user wrote, and a file that has been edited by hand is not a reason to lose them.
        Triggers ??= new List<Trigger>();
        Triggers.RemoveAll(trigger => trigger is null || string.IsNullOrWhiteSpace(trigger.Pattern));
        foreach (Trigger trigger in Triggers) trigger.Clamp();
        if (Triggers.Count > MaximumTriggers) Triggers.RemoveRange(MaximumTriggers, Triggers.Count - MaximumTriggers);
    }

    public AppSettings Copy() => new()
    {
        Shell = Shell,
        Columns = Columns,
        Rows = Rows,
        AskAboutDefaultTerminal = AskAboutDefaultTerminal,
        RecentTelnetHosts = new List<string>(RecentTelnetHosts),
        MudDirectoryKey = MudDirectoryKey,
        MudDirectoryEndpoint = MudDirectoryEndpoint,
        SpeakInBackground = SpeakInBackground,
        MudSounds = MudSounds,
        SoundDirectory = SoundDirectory,
        SoundVolume = SoundVolume,
        DownloadSounds = DownloadSounds,
        MudStatus = MudStatus,
        SpeakMudStatus = SpeakMudStatus,
        Triggers = [.. Triggers.Select(trigger => trigger.Copy())],
        TriggersEnabled = TriggersEnabled,
    };
}

/// <summary>Loads and saves BlindTerm's settings in %APPDATA%\BlindTerm.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlindTerm", "settings.json");

    public AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? new AppSettings();
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            // A corrupt or inaccessible preference file must not prevent the terminal starting.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        path ??= DefaultPath;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

}
