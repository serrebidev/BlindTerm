namespace BlindTerm.App;

/// <summary>A small, local history of complete lines sent through one kind of session.</summary>
internal sealed class CommandHistory
{
    private readonly List<string> _lines = new();
    private int _index;

    public int Count => _lines.Count;

    public void Remember(string text)
    {
        if (text.Length == 0) return;
        if (_lines.Count == 0 || _lines[^1] != text) _lines.Add(text);
        _index = _lines.Count;
    }

    /// <summary>The recalled line, an empty line after the newest entry, or null if empty.</summary>
    public string? Step(int delta)
    {
        if (_lines.Count == 0) return null;
        _index = Math.Clamp(_index + delta, 0, _lines.Count);
        return _index == _lines.Count ? string.Empty : _lines[_index];
    }

    public void Clear()
    {
        _lines.Clear();
        _index = 0;
    }
}
