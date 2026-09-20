using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>
/// Identifies the transcript lines produced after the most recently submitted command.
/// The transcript remains the permanent history; this is only the smaller document exposed
/// as a remote session's ordinary output field.
/// </summary>
internal sealed class LatestResponse
{
    private int _firstLine;

    public int FirstLine => _firstLine;

    public void Begin(Transcript transcript) => _firstLine = transcript.Count;

    /// <summary>
    /// Where the caret goes to start reading the response: the control-space offset of the
    /// first line of it, or the end of the document while there is not one yet.
    /// </summary>
    public int StartOffset(Transcript transcript) => transcript.CaretOffset(_firstLine);

    public IReadOnlyList<string> Lines(Transcript transcript) => transcript.Snapshot(_firstLine);

    public string Text(Transcript transcript)
        => string.Join(Environment.NewLine, Lines(transcript));
}
