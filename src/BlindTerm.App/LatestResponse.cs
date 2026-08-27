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

    public void Begin(Transcript transcript) => _firstLine = transcript.Count;

    public IReadOnlyList<string> Lines(Transcript transcript)
    {
        int first = Math.Clamp(_firstLine, 0, transcript.Count);
        return transcript.Lines.Skip(first).ToArray();
    }

    public string Text(Transcript transcript)
        => string.Join(Environment.NewLine, Lines(transcript));
}
