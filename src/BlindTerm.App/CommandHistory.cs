namespace BlindTerm.App;

/// <summary>A small, local history of complete lines sent through one kind of session.</summary>
internal sealed class CommandHistory
{
    private readonly List<string> _lines = new();
    private int _index;

    // The last line this history put in the command line. While the line still holds it,
    // the arrows continue whatever they were doing -- stepping, or searching. The moment
    // it holds anything else, what is there now was typed, and the arrows start afresh
    // from it. Without this, a line recalled by stepping would be mistaken for one typed,
    // and the next Up would search for lines beginning with the whole of it.
    private string? _placed;

    // A search by what was typed: the prefix it began with, and where in the lines it is
    // standing. Standing at the end of the lines means standing on the typed prefix
    // itself, before the first step into the matches.
    private string? _searchPrefix;
    private int _searchIndex;

    public int Count => _lines.Count;

    /// <summary>The newest line sent, or null when nothing has been sent yet.</summary>
    public string? Last => _lines.Count == 0 ? null : _lines[^1];

    public void Remember(string text)
    {
        EndSearch();
        if (text.Length == 0) return;
        if (_lines.Count == 0 || _lines[^1] != text) _lines.Add(text);
        _index = _lines.Count;
    }

    /// <summary>The recalled line, an empty line after the newest entry, or null if empty.</summary>
    public string? Step(int delta)
    {
        EndSearch();
        if (_lines.Count == 0) return null;
        _index = Math.Clamp(_index + delta, 0, _lines.Count);
        return _placed = _index == _lines.Count ? string.Empty : _lines[_index];
    }

    /// <summary>
    /// Steps with what is in the command line now. An empty line steps the whole history,
    /// as <see cref="Step(int)"/> does. A line with typed text in it searches instead: Up
    /// finds the newest line that begins with what is typed, Up again the next older one,
    /// and Down walks back towards the newest until it gives back what was typed.
    ///
    /// This is how Mudlet's command line behaves, and it is the difference between
    /// arrowing through forty commands and typing two letters of the one meant. Nothing
    /// is filtered out of the history itself: clear the line and the whole of it is there.
    /// </summary>
    public string? Step(int delta, string currentText)
    {
        bool untouched = _placed is not null && currentText == _placed;
        if (_searchPrefix is not null && untouched) return StepSearch(delta);
        if (untouched) return Step(delta);

        EndSearch();
        if (currentText.Length == 0 || delta >= 0) return Step(delta);

        if (_lines.Count == 0) return null;
        _searchPrefix = currentText;
        _searchIndex = _lines.Count;
        return StepSearch(delta);
    }

    private string? StepSearch(int delta)
    {
        if (delta < 0)
        {
            for (int i = _searchIndex - 1; i >= 0; i--)
            {
                if (!Matches(i)) continue;
                _searchIndex = i;
                return _placed = _lines[i];
            }
            return null;
        }

        if (delta > 0)
        {
            for (int i = _searchIndex + 1; i < _lines.Count; i++)
            {
                if (!Matches(i)) continue;
                _searchIndex = i;
                return _placed = _lines[i];
            }

            // Past the newest match is where the search began: what was typed, back
            // again, and the search over. It is not this history's placing -- the next
            // Up treats it as typed again, and searches with it once more.
            string typed = _searchPrefix!;
            EndSearch();
            _index = _lines.Count;
            return typed;
        }

        return null;
    }

    private bool Matches(int index)
        => _lines[index].StartsWith(_searchPrefix!, StringComparison.OrdinalIgnoreCase);

    private void EndSearch()
    {
        _searchPrefix = null;
        _placed = null;
        _searchIndex = 0;
    }

    public void Clear()
    {
        EndSearch();
        _lines.Clear();
        _index = 0;
    }
}
