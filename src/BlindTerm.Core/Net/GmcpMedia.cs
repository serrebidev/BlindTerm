using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BlindTerm.Core.Net;

/// <summary>Which of the Client.Media messages a GMCP message is.</summary>
public enum GmcpMediaKind
{
    /// <summary>Play a sound or a piece of music.</summary>
    Play,

    /// <summary>Say where this MUD's media lives, so what is played later can be fetched.</summary>
    Load,

    /// <summary>Stop what is playing.</summary>
    Stop,
}

/// <summary>
/// A Client.Media message read into what BlindTerm's sound player already understands.
///
/// This is the MUD Client Media Protocol -- the way of asking for sounds that travels in
/// GMCP rather than in the text. Mudlet speaks it, and MUDs that have moved their sounds
/// into it send "Client.Media.Play" with a name, an address, a volume and a count of
/// plays, where an older MUD prints "!!SOUND". The two say the same things, so what
/// arrives here becomes an <see cref="MspTrigger"/> and is played by the same player under
/// the same rules: a name is only ever a plain file name, a download happens only if the
/// user has allowed downloads, and music is one piece at a time.
/// </summary>
public sealed record GmcpMediaRequest(
    GmcpMediaKind Kind,
    MspTrigger? Trigger,
    string? Url,
    bool BothKinds)
{
    /// <summary>The volume a Play asks for when it does not say: the protocol's own middle.</summary>
    public const int DefaultVolume = 50;

    /// <summary>
    /// Reads a GMCP message as a media request. False for anything that is not one, and
    /// for one that does not read: a message from a server is not a reason to throw.
    /// </summary>
    public static bool TryParse(GmcpMessage message, [NotNullWhen(true)] out GmcpMediaRequest? request)
    {
        request = null;
        ArgumentNullException.ThrowIfNull(message);

        GmcpMediaKind kind;
        if (message.Package.Equals("Client.Media.Play", StringComparison.OrdinalIgnoreCase)) kind = GmcpMediaKind.Play;
        else if (message.Package.Equals("Client.Media.Load", StringComparison.OrdinalIgnoreCase)) kind = GmcpMediaKind.Load;
        else if (message.Package.Equals("Client.Media.Stop", StringComparison.OrdinalIgnoreCase)) kind = GmcpMediaKind.Stop;
        else return false;

        // A Stop may arrive with no payload at all and still means something: stop
        // everything. The others have nothing to say without their object.
        JsonDocument? document = null;
        JsonElement root = default;
        bool hasObject = false;
        if (!string.IsNullOrWhiteSpace(message.Payload))
        {
            try { document = JsonDocument.Parse(message.Payload); }
            catch (JsonException) { return false; }
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                root = document.RootElement;
                hasObject = true;
            }
        }

        using (document)
        {
            switch (kind)
            {
                case GmcpMediaKind.Play:
                    if (!hasObject) return false;
                    return TryReadPlay(root, out request);

                case GmcpMediaKind.Load:
                    // Nothing is fetched for a Load: BlindTerm downloads a sound when it is
                    // asked to play it and does not already have it. What a Load carries
                    // that matters is the address, learned now so that a later Play with a
                    // bare file name can be fetched from the right place.
                    request = new GmcpMediaRequest(kind, null,
                        hasObject ? Text(root, "url") : null, false);
                    return true;

                case GmcpMediaKind.Stop:
                {
                    string? type = hasObject ? Text(root, "type") : null;
                    bool music = type is not null
                                 && type.Equals("music", StringComparison.OrdinalIgnoreCase);
                    var stop = new MspTrigger(
                        music ? MspKind.Music : MspKind.Sound,
                        "Off", 0, 1, 0, false, null, null);
                    // No type named means both kinds stop. A named sound that should stop
                    // alone cannot be picked out of the voices playing -- the MUD Sound
                    // Protocol has the same limit, and its "Off" is by kind as well.
                    request = new GmcpMediaRequest(kind, stop, null, type is null);
                    return true;
                }

                default:
                    return false;
            }
        }
    }

    private static bool TryReadPlay(JsonElement root, [NotNullWhen(true)] out GmcpMediaRequest? request)
    {
        request = null;

        string? name = Text(root, "name");
        if (string.IsNullOrWhiteSpace(name)) return false;

        bool music = Text(root, "type")?.Equals("music", StringComparison.OrdinalIgnoreCase) == true;
        int volume = Number(root, "volume", DefaultVolume);
        int loops = Number(root, "loops", 1);
        int priority = Number(root, "priority", MspTrigger.DefaultPriority);
        bool keepPlaying = Boolean(root, "continue", true);

        var trigger = new MspTrigger(
            music ? MspKind.Music : MspKind.Sound,
            name.Trim(),
            Math.Clamp(volume, 0, 100),
            loops == MspTrigger.Forever
                ? MspTrigger.Forever
                : Math.Clamp(loops, 1, MspTrigger.MaximumLoops),
            Math.Clamp(priority, 0, 1000),
            keepPlaying,
            // The protocol's "tag" names a group a later Stop can aim at. It is not
            // carried into the trigger's Type: that field is the MUD Sound Protocol's
            // subfolder, and a tag read as a folder would send the library looking for
            // the sound in a place it was never put.
            null,
            Text(root, "url"));

        request = new GmcpMediaRequest(GmcpMediaKind.Play, trigger, null, false);
        return true;
    }

    /// <summary>A JSON string field, or null when it is absent or not a string.</summary>
    private static string? Text(JsonElement root, string field)
        => root.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A JSON number field, whole or not, or a default when it is absent or not a number.</summary>
    private static int Number(JsonElement root, string field, int fallback)
    {
        if (!root.TryGetProperty(field, out JsonElement value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return (int)Math.Round(number);
        return fallback;
    }

    /// <summary>A JSON truth field -- true or false, or 1 or 0 -- or a default.</summary>
    private static bool Boolean(JsonElement root, string field, bool fallback)
    {
        if (!root.TryGetProperty(field, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetDouble(out double number) && number != 0,
            _ => fallback,
        };
    }
}
