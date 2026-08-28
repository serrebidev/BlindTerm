using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// Telnet addresses that have been connected to, newest first, as "host" or "host:port".
    ///
    /// A MUD address is exactly the kind of thing nobody types correctly twice, and arrowing
    /// to a remembered one reads it out.
    /// </summary>
    public List<string> RecentTelnetHosts { get; set; } = new();

    /// <summary>
    /// Whether a MUD may play sounds through the MUD Sound Protocol.
    ///
    /// Turning this off silences the sounds; it does not put the triggers back into the text,
    /// which are lifted out either way. A line reading "exclamation exclamation SOUND left
    /// paren sword dot wav" is not something anyone wants read to them.
    /// </summary>
    public bool MudSounds { get; set; } = true;

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
    /// Whether what a MUD says about the room and the character over GMCP is written into the
    /// transcript as it arrives.
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
    }

    public AppSettings Copy() => new()
    {
        Shell = Shell,
        Columns = Columns,
        Rows = Rows,
        AskAboutDefaultTerminal = AskAboutDefaultTerminal,
        RecentTelnetHosts = new List<string>(RecentTelnetHosts),
        MudSounds = MudSounds,
        SoundDirectory = SoundDirectory,
        SoundVolume = SoundVolume,
        DownloadSounds = DownloadSounds,
        MudStatus = MudStatus,
        SpeakMudStatus = SpeakMudStatus,
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
