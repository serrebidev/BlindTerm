using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlindTerm.Core.Net;

/// <summary>
/// What a MUD has said about the room and the character, kept as the plain sentences a player
/// would want read to them.
///
/// This exists because the same facts are otherwise only available by reading the room
/// description and finding the word "Exits" somewhere in it. A MUD that speaks GMCP has
/// already said which ways out there are, as a list; turning that back into prose to be
/// searched would throw away the one thing that made it worth having.
///
/// Everything here is tolerant. Key names vary between MUDs and between codebases, and a key
/// that is missing or is not the shape it usually is means one fact unavailable, never a
/// broken session.
/// </summary>
public sealed class MudStatus
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Where the character is, as one readable sentence, or null if unknown.</summary>
    public string? Room { get; private set; }

    /// <summary>How the character is, as one readable sentence, or null if unknown.</summary>
    public string? Vitals { get; private set; }

    /// <summary>The character's name, if the MUD has said.</summary>
    public string? CharacterName { get; private set; }

    /// <summary>The ways out of the current room, in the order the MUD listed them.</summary>
    public IReadOnlyList<string> Exits { get; private set; } = [];

    /// <summary>Forgets everything. A new connection is a different character somewhere else.</summary>
    public void Reset()
    {
        Room = null;
        Vitals = null;
        CharacterName = null;
        Exits = [];
        _roomId = null;
    }

    /// <summary>
    /// Takes one message in and returns the line worth recording, or null.
    ///
    /// A MUD repeats these: Core MUD sends the character's vitals after every command, whether
    /// or not a point of anything has changed. Only a change is news.
    /// </summary>
    public string? News(GmcpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.IsIn("Room")) return NoteRoom(message.Payload);
        if (message.Package.Equals("Char.Vitals", StringComparison.OrdinalIgnoreCase))
            return NoteVitals(message.Payload);
        if (message.Package.Equals("Char.Status", StringComparison.OrdinalIgnoreCase))
        {
            NoteName(message.Payload);
            return null;
        }
        return null;
    }

    private string? NoteRoom(string payload)
    {
        if (Read(payload) is not { } room || room.ValueKind != JsonValueKind.Object) return null;

        string? name = Text(room, "short", "name", "title");
        string? area = Text(room, "area", "zone");
        string[] exits = ExitNames(room);

        var said = new StringBuilder();
        said.Append(name ?? "Unknown room");
        if (area is not null) said.Append(", ").Append(area);
        said.Append(". ");
        said.Append(exits.Length == 0
            ? "No obvious exits."
            : $"Exits: {string.Join(", ", exits)}.");

        string sentence = said.ToString();
        string? id = Text(room, "id", "num", "vnum", "number");

        // A MUD repeats the room after every command, so the same room again is not news. Two
        // adjacent rooms can read identically, though -- one corridor is much like the next --
        // so where the MUD gives a room an identity, moving is what counts rather than the
        // description changing.
        bool moved = id is not null || _roomId is not null
            ? id != _roomId
            : sentence != Room;

        _roomId = id;
        Room = sentence;
        Exits = exits;
        return moved ? sentence : null;
    }

    private string? _roomId;

    private string? NoteVitals(string payload)
    {
        if (Read(payload) is not { } vitals || vitals.ValueKind != JsonValueKind.Object) return null;

        var parts = new List<string>();
        Pool(vitals, parts, "HP", "hp", "maxhp");
        Pool(vitals, parts, "SP", "sp", "maxsp");
        Pool(vitals, parts, "MP", "mp", "maxmp");
        Pool(vitals, parts, "EP", "ep", "maxep");
        Pool(vitals, parts, "Willpower", "wp", "maxwp");

        // Conditions. A MUD reports these as an empty string when they do not apply, which is
        // why an empty one is not worth a word.
        foreach (string key in Conditions)
        {
            if (Text(vitals, key) is { Length: > 0 } value) parts.Add($"{Spoken(key)} {value}");
        }

        if (parts.Count == 0) return null;

        string sentence = string.Join(". ", parts) + ".";
        if (sentence == Vitals) return null;
        Vitals = sentence;
        return sentence;
    }

    private static readonly string[] Conditions =
        ["poison", "intox", "stuffed", "bloat", "damaged_limb"];

    private void NoteName(string payload)
    {
        if (Read(payload) is not { } status || status.ValueKind != JsonValueKind.Object) return;
        if (Text(status, "name", "fullname") is { Length: > 0 } name) CharacterName = name;
    }

    /// <summary>"HP 240 of 280", when both halves are there. One half alone is still worth saying.</summary>
    private static void Pool(JsonElement from, List<string> into, string label,
                             string current, string maximum)
    {
        if (Number(from, current) is not { } now) return;
        into.Add(Number(from, maximum) is { } most
            ? $"{label} {now} of {most}"
            : $"{label} {now}");
    }

    private static string[] ExitNames(JsonElement room)
    {
        if (!room.TryGetProperty("exits", out JsonElement exits)
            // Some MUDs send a list of directions rather than a map of them to room ids.
            && !room.TryGetProperty("exit", out exits))
        {
            return [];
        }

        return exits.ValueKind switch
        {
            JsonValueKind.Object => [.. exits.EnumerateObject().Select(e => e.Name)],
            JsonValueKind.Array => [.. exits.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)],
            JsonValueKind.String => [.. exits.GetString()!
                .Split([',', ' '],
                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            _ => [],
        };
    }

    private static JsonElement? Read(string payload)
    {
        if (payload.Length == 0) return null;
        try
        {
            using var document = JsonDocument.Parse(payload, Lenient);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A server that sends something which is not JSON has said nothing this understands.
            return null;
        }
    }

    /// <summary>The first of these keys the object has, matched without regard to case.</summary>
    private static string? Text(JsonElement from, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (!Find(from, key, out JsonElement value)) continue;
            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            };
            if (text is { Length: > 0 }) return text.Trim();
        }
        return null;
    }

    /// <summary>A number, whether the MUD sent it as one or as a string containing one.</summary>
    private static long? Number(JsonElement from, string key)
    {
        if (!Find(from, key, out JsonElement value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out long number) => number,
            JsonValueKind.Number when value.TryGetDouble(out double number) => (long)number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                                                    CultureInfo.InvariantCulture,
                                                    out long number) => number,
            _ => null,
        };
    }

    private static bool Find(JsonElement from, string key, out JsonElement value)
    {
        if (from.TryGetProperty(key, out value)) return true;
        foreach (JsonProperty property in from.EnumerateObject())
        {
            if (!property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static string Spoken(string key) => key switch
    {
        "intox" => "Intoxication",
        "damaged_limb" => "Damaged limb",
        _ => char.ToUpperInvariant(key[0]) + key[1..],
    };
}
