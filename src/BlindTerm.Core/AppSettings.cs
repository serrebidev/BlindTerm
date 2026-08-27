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

    public void Validate()
    {
        TerminalSize.Validate(Columns, Rows);
        if (Shell is null || Shell.Length > 32_768) throw new ArgumentOutOfRangeException(nameof(Shell));
    }

    public AppSettings Copy() => new()
    {
        Shell = Shell,
        Columns = Columns,
        Rows = Rows,
        AskAboutDefaultTerminal = AskAboutDefaultTerminal,
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
