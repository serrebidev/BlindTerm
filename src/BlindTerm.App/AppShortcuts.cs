namespace BlindTerm.App;

/// <summary>
/// Keys BlindTerm keeps for its own commands.
///
/// Control belongs to native edit controls and terminal programs: Ctrl+C, Ctrl+X and
/// Ctrl+Z must continue to mean copy, cut and undo where Windows users expect them. Alt is
/// the single application-command modifier. In live screen mode an Alt chord can still be
/// sent to the program with Pass Next.
/// </summary>
internal static class AppShortcuts
{
    public const Keys FocusTranscript = Keys.Alt | Keys.D1;
    public const Keys FocusCommandLine = Keys.Alt | Keys.D2;
    public const Keys ToggleReview = Keys.Alt | Keys.D3;

    public const Keys ChangeDirectory = Keys.Alt | Keys.D;
    public const Keys Interrupt = Keys.Alt | Keys.C;
    public const Keys Escape = Keys.Alt | Keys.OemOpenBrackets;
    public const Keys PassNext = Keys.Alt | Keys.P;
    public const Keys SpeakCurrentLine = Keys.Alt | Keys.L;
    public const Keys SpeakScreen = Keys.Alt | Keys.W;
    public const Keys ToggleSpeakOutput = Keys.Alt | Keys.S;
    public const Keys EndOfTranscript = Keys.Alt | Keys.End;
    public const Keys CopyAll = Keys.Alt | Keys.A;
    public const Keys CopyCommandOutput = Keys.Alt | Keys.O;
    public const Keys PreviousCommand = Keys.Alt | Keys.Up;
    public const Keys NextCommand = Keys.Alt | Keys.Down;

    public static IReadOnlyList<Keys> Assigned { get; } =
    [
        FocusTranscript,
        FocusCommandLine,
        ToggleReview,
        ChangeDirectory,
        Interrupt,
        Escape,
        PassNext,
        SpeakCurrentLine,
        SpeakScreen,
        ToggleSpeakOutput,
        EndOfTranscript,
        CopyAll,
        CopyCommandOutput,
        PreviousCommand,
        NextCommand,
    ];

    /// <summary>
    /// Alt is BlindTerm's command namespace. Reserving it as a group also keeps standard
    /// menu access (Alt+T, Alt+R, Alt+G and Alt+E) working while a full-screen program owns
    /// the keyboard.
    /// </summary>
    public static bool IsApplicationChord(Keys keyData)
        => (keyData & Keys.Alt) == Keys.Alt;
}
