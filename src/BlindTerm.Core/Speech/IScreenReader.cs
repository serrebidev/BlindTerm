namespace BlindTerm.Core.Speech;

/// <summary>
/// How urgent an utterance is, and therefore what it does to whatever is being said.
///
/// These are NVDA's own three levels, because they are the finest-grained of the readers
/// supported and the others map onto them without losing anything.
/// </summary>
public enum SpeechPriority
{
    /// <summary>Queued behind whatever is speaking. Streamed output.</summary>
    Normal = 0,

    /// <summary>Said next, before anything still queued, but does not cut off the current utterance.</summary>
    Next = 1,

    /// <summary>Interrupts. A bell, or the line the caret has just been moved to.</summary>
    Now = 2,
}

/// <summary>
/// A screen reader BlindTerm can speak through.
///
/// Speaking through the user's own reader rather than a speech synthesiser of our own is the
/// whole point: it is their voice, their rate, their punctuation level, their braille display,
/// and it does not fight the thing already reading their screen.
/// </summary>
public interface IScreenReader
{
    /// <summary>Name for the UI and for diagnostics: "NVDA", "JAWS", "SAPI".</summary>
    string Name { get; }

    /// <summary>Whether this reader is running right now. Cheap; called before every utterance.</summary>
    bool IsRunning { get; }

    /// <summary>Speaks text. Returns false if the reader could not be reached.</summary>
    bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal);

    /// <summary>Puts a message on the braille display, where the reader supports it.</summary>
    bool Braille(string text);

    /// <summary>Stops whatever is being said.</summary>
    bool Silence();
}
