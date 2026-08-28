using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BlindTerm.Core.Net;

/// <summary>The shape of one value carried by the MUD Server Data Protocol.</summary>
public enum MsdpValueKind
{
    Text,
    Array,
    Table,
}

/// <summary>
/// One MSDP value. MSDP can carry text, an ordered array, or a table of named values.
/// </summary>
public sealed class MsdpValue
{
    private static readonly IReadOnlyList<MsdpValue> NoItems = Array.Empty<MsdpValue>();
    private static readonly IReadOnlyDictionary<string, MsdpValue> NoFields =
        new Dictionary<string, MsdpValue>();

    private MsdpValue(string text)
    {
        Kind = MsdpValueKind.Text;
        Text = text;
    }

    private MsdpValue(IReadOnlyList<MsdpValue> items)
    {
        Kind = MsdpValueKind.Array;
        Items = items;
    }

    private MsdpValue(IReadOnlyDictionary<string, MsdpValue> fields)
    {
        Kind = MsdpValueKind.Table;
        Fields = fields;
    }

    public MsdpValueKind Kind { get; }
    public string Text { get; } = string.Empty;
    public IReadOnlyList<MsdpValue> Items { get; } = NoItems;
    public IReadOnlyDictionary<string, MsdpValue> Fields { get; } = NoFields;

    internal static MsdpValue FromText(string value) => new(value);
    internal static MsdpValue FromArray(IReadOnlyList<MsdpValue> values) => new(values);
    internal static MsdpValue FromTable(IReadOnlyDictionary<string, MsdpValue> values) => new(values);

    /// <summary>The scalar strings in this value, flattening arrays but not table keys.</summary>
    public IEnumerable<string> ScalarValues()
    {
        if (Kind == MsdpValueKind.Text)
        {
            yield return Text;
            yield break;
        }

        if (Kind != MsdpValueKind.Array) yield break;
        foreach (MsdpValue item in Items)
        {
            foreach (string value in item.ScalarValues()) yield return value;
        }
    }

    /// <summary>Finds a table field without depending on a server's choice of letter case.</summary>
    public bool TryGetField(string name, [NotNullWhen(true)] out MsdpValue? value)
    {
        foreach ((string key, MsdpValue candidate) in Fields)
        {
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = candidate;
            return true;
        }

        value = null;
        return false;
    }
}

/// <summary>One complete MSDP subnegotiation, which may contain several variables.</summary>
public sealed class MsdpMessage
{
    // Enough for unusually large room tables without letting a broken host consume memory
    // indefinitely on the terminal's reading thread.
    public const int MaximumLength = 64 * 1024;

    private MsdpMessage(IReadOnlyList<KeyValuePair<string, MsdpValue>> variables)
        => Variables = variables;

    public IReadOnlyList<KeyValuePair<string, MsdpValue>> Variables { get; }

    /// <summary>All occurrences of a variable, compared tolerantly for real-world MUDs.</summary>
    public IEnumerable<MsdpValue> Find(string name)
        => Variables.Where(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value);

    /// <summary>Parses the bytes after the MSDP option byte and before IAC SE.</summary>
    public static bool TryParse(ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out MsdpMessage? message)
    {
        message = null;
        if (payload.IsEmpty || payload.Length > MaximumLength) return false;

        var parser = new Parser(payload.ToArray());
        if (!parser.TryMessage(out IReadOnlyList<KeyValuePair<string, MsdpValue>>? variables))
            return false;

        message = new MsdpMessage(variables);
        return true;
    }

    private sealed class Parser(byte[] bytes)
    {
        private const byte Variable = 1;
        private const byte Value = 2;
        private const byte TableOpen = 3;
        private const byte TableClose = 4;
        private const byte ArrayOpen = 5;
        private const byte ArrayClose = 6;
        private const int MaximumDepth = 32;

        private int _at;

        public bool TryMessage(
            [NotNullWhen(true)] out IReadOnlyList<KeyValuePair<string, MsdpValue>>? variables)
        {
            var found = new List<KeyValuePair<string, MsdpValue>>();
            while (_at < bytes.Length)
            {
                if (bytes[_at++] != Variable || !TryName(out string? name)
                    || !TryValues(0, out MsdpValue? value))
                {
                    variables = null;
                    return false;
                }
                found.Add(new(name, value));
            }

            variables = found.Count > 0 ? found : null;
            return variables is not null;
        }

        private bool TryName([NotNullWhen(true)] out string? name)
        {
            if (!TryText(out name) || string.IsNullOrEmpty(name)
                || char.IsDigit(name[0])
                || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            {
                name = null;
                return false;
            }
            return true;
        }

        private bool TryValues(int depth, [NotNullWhen(true)] out MsdpValue? value)
        {
            var values = new List<MsdpValue>();
            while (_at < bytes.Length && bytes[_at] == Value)
            {
                _at++;
                if (!TryValue(depth + 1, out MsdpValue? item))
                {
                    value = null;
                    return false;
                }
                values.Add(item);
            }

            if (values.Count == 0)
            {
                value = null;
                return false;
            }

            value = values.Count == 1 ? values[0] : MsdpValue.FromArray(values);
            return true;
        }

        private bool TryValue(int depth, [NotNullWhen(true)] out MsdpValue? value)
        {
            if (depth > MaximumDepth)
            {
                value = null;
                return false;
            }

            if (_at < bytes.Length && bytes[_at] == TableOpen)
                return TryTable(depth, out value);
            if (_at < bytes.Length && bytes[_at] == ArrayOpen)
                return TryArray(depth, out value);
            if (!TryText(out string? text))
            {
                value = null;
                return false;
            }

            value = MsdpValue.FromText(text);
            return true;
        }

        private bool TryTable(int depth, [NotNullWhen(true)] out MsdpValue? value)
        {
            _at++;
            var fields = new Dictionary<string, MsdpValue>(StringComparer.Ordinal);
            while (_at < bytes.Length && bytes[_at] != TableClose)
            {
                if (bytes[_at++] != Variable || !TryName(out string? name)
                    || !TryValues(depth, out MsdpValue? field))
                {
                    value = null;
                    return false;
                }
                fields[name] = field;
            }

            if (_at >= bytes.Length || bytes[_at++] != TableClose)
            {
                value = null;
                return false;
            }
            value = MsdpValue.FromTable(fields);
            return true;
        }

        private bool TryArray(int depth, [NotNullWhen(true)] out MsdpValue? value)
        {
            _at++;
            var items = new List<MsdpValue>();
            while (_at < bytes.Length && bytes[_at] != ArrayClose)
            {
                if (bytes[_at++] != Value || !TryValue(depth + 1, out MsdpValue? item))
                {
                    value = null;
                    return false;
                }
                items.Add(item);
            }

            if (_at >= bytes.Length || bytes[_at++] != ArrayClose)
            {
                value = null;
                return false;
            }
            value = MsdpValue.FromArray(items);
            return true;
        }

        private bool TryText([NotNullWhen(true)] out string? text)
        {
            int start = _at;
            while (_at < bytes.Length && bytes[_at] is not (>= Variable and <= ArrayClose))
            {
                // NUL and IAC are forbidden inside MSDP strings by the protocol.
                if (bytes[_at] is 0 or 255)
                {
                    text = null;
                    return false;
                }
                _at++;
            }
            text = Encoding.UTF8.GetString(bytes, start, _at - start);
            return true;
        }
    }
}
