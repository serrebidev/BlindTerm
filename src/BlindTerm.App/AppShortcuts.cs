namespace BlindTerm.App;

/// <summary>
/// Keys BlindTerm keeps for its own commands.
///
/// Control belongs to the foreground terminal program while one is active, and to native edit
/// controls at the idle shell prompt. Alt is the single BlindTerm-command modifier. In live
/// screen mode an Alt chord can still be sent to the program with Pass Next.
/// </summary>
internal static class AppShortcuts
{
    public enum ScreenTabTarget { None, Output, Input }

    public const Keys FocusTranscript = Keys.Alt | Keys.D1;
    public const Keys FocusCommandLine = Keys.Alt | Keys.D2;
    public const Keys ToggleReview = Keys.Alt | Keys.D3;

    public const Keys ChangeDirectory = Keys.Alt | Keys.D;
    // Shift, because plain Alt+T is how the Terminal menu itself is opened and a menu
    // mnemonic must keep working. Alt+Shift is still inside BlindTerm's Alt namespace.
    public const Keys Triggers = Keys.Alt | Keys.Shift | Keys.T;
    public const Keys ToggleTriggers = Keys.Alt | Keys.Shift | Keys.G;
    public const Keys Connect = Keys.Alt | Keys.N;
    public const Keys ConnectSsh = Keys.Alt | Keys.Shift | Keys.N;
    // Shift, because plain Alt+B is not free: it is how a menu mnemonic would be reached.
    public const Keys BrowseMuds = Keys.Alt | Keys.Shift | Keys.B;
    public const Keys Interrupt = Keys.Alt | Keys.C;
    public const Keys Escape = Keys.Alt | Keys.OemOpenBrackets;
    public const Keys PassNext = Keys.Alt | Keys.P;
    public const Keys SpeakCurrentLine = Keys.Alt | Keys.L;
    public const Keys SpeakScreen = Keys.Alt | Keys.W;
    public const Keys ToggleSpeakOutput = Keys.Alt | Keys.S;
    public const Keys ToggleMudSounds = Keys.Alt | Keys.M;
    public const Keys SpeakVitals = Keys.Alt | Keys.V;
    public const Keys SpeakRoom = Keys.Alt | Keys.X;
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
        Triggers,
        ToggleTriggers,
        Connect,
        ConnectSsh,
        BrowseMuds,
        Interrupt,
        Escape,
        PassNext,
        SpeakCurrentLine,
        SpeakScreen,
        ToggleSpeakOutput,
        ToggleMudSounds,
        SpeakVitals,
        SpeakRoom,
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

    /// <summary>
    /// Ctrl chords belong to an interactive line-mode program while its input control has
    /// focus. When the transcript has focus, Windows keeps its native caret and selection
    /// commands for NVDA and JAWS. Ctrl+Alt never bypasses BlindTerm's Alt command namespace.
    ///
    /// Paste is the exception. BlindTerm owns the line being typed, so a program can never
    /// receive a pasted path any way other than through this box, and handing Ctrl+V to the
    /// program would take away the only way to put one there. Alt+C sends the interrupt when
    /// Ctrl+C is wanted for something else.
    /// </summary>
    public static bool ShouldPassControlChord(Keys keyData, bool foregroundProgramActive,
        bool terminalInputFocused)
        => foregroundProgramActive
            && terminalInputFocused
            && (keyData & Keys.Control) == Keys.Control
            && (keyData & Keys.Alt) != Keys.Alt
            && (keyData & Keys.KeyCode) != Keys.V;

    /// <summary>
    /// An unmodified Tab in the terminal's input asks whatever is reading the line to
    /// complete what was typed: the shell's own editor at an idle prompt, or an inline
    /// program that has taken the keyboard.
    ///
    /// The shell prompt is where completion is used most, and it was the one place this did
    /// not reach. A Tab nothing claimed fell through to window tab order, which moved the
    /// reader from the input box round to the transcript and left the typed line behind in a
    /// box the user was no longer standing in -- the text looked lost and no completion had
    /// happened. Nobody pressing Tab at a terminal prompt is asking for that.
    ///
    /// Shift+Tab remains reverse focus navigation to the transcript, and a Tab pressed in the
    /// transcript remains forward focus navigation to the input field.
    /// </summary>
    public static bool ShouldSendCompletionTab(Keys keyData, bool terminalLineLive,
        bool terminalInputFocused)
        => terminalLineLive
            && terminalInputFocused
            && (keyData & Keys.KeyCode) == Keys.Tab
            && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.None;

    /// <summary>
    /// Full-screen programs use the keyboard proxy as their input field and review mode as
    /// their output field. They keep the same focus contract as inline programs: Shift+Tab
    /// moves from live input to readable output, and Tab returns from output to live input.
    /// </summary>
    public static ScreenTabTarget ScreenTab(Keys keyData, bool screenMode, bool reviewing)
    {
        if (!screenMode || (keyData & Keys.KeyCode) != Keys.Tab
                        || (keyData & (Keys.Control | Keys.Alt)) != Keys.None)
            return ScreenTabTarget.None;

        bool shift = (keyData & Keys.Shift) == Keys.Shift;
        if (!reviewing && shift) return ScreenTabTarget.Output;
        if (reviewing && !shift) return ScreenTabTarget.Input;
        return ScreenTabTarget.None;
    }

    /// <summary>The equivalent focus movement for the native line-mode controls.</summary>
    public static ScreenTabTarget LineTab(Keys keyData, bool screenMode, bool inputFocused,
        bool outputFocused)
    {
        if (screenMode || (keyData & Keys.KeyCode) != Keys.Tab
                       || (keyData & (Keys.Control | Keys.Alt)) != Keys.None)
            return ScreenTabTarget.None;

        bool shift = (keyData & Keys.Shift) == Keys.Shift;
        if (inputFocused && shift) return ScreenTabTarget.Output;
        if (outputFocused && !shift) return ScreenTabTarget.Input;
        return ScreenTabTarget.None;
    }

    /// <summary>
    /// Whether a navigation key typed at the command line is the running program's rather
    /// than the edit box's.
    ///
    /// Codex, Claude Code, OpenCode and Freebuff all ask questions no line of text can answer:
    /// a model list chosen with Up and Down, a reasoning level adjusted with Left and Right,
    /// a picker dismissed with Escape. Those keys have to reach the program. Telnet is the
    /// exception handled by <see cref="ShouldRecallTelnetHistory"/>: BlindTerm owns its sent
    /// line history because servers do not consistently provide one.
    ///
    /// An empty command line is the boundary. With nothing typed there is no text to move
    /// through and every one of these keys is dead weight, so the program gets them. The
    /// moment there is something typed the box is an ordinary edit box again -- otherwise a
    /// typo in a long prompt could never be corrected, which is a far worse trade. Alt+P
    /// passes a single key either way.
    ///
    /// A remote session has no local model picker or effort selector to drive, so Left, Right,
    /// Home and End stay as ordinary edit-box caret keys. On an empty remote prompt a shell
    /// answers those with a bell, which BlindTerm would read out as "Attention" together with
    /// the prompt. Up, Down, Escape and the page keys are still passed through.
    /// </summary>
    public static bool ShouldPassNavigationKey(Keys keyData, bool foregroundProgramActive,
        bool terminalInputFocused, bool commandLineEmpty, bool remoteSession = false)
    {
        if (!foregroundProgramActive || !terminalInputFocused || !commandLineEmpty) return false;
        if ((keyData & Keys.Alt) == Keys.Alt) return false;
        // A Ctrl chord is the other rule's business, and answering here as well would send it
        // twice.
        if ((keyData & Keys.Control) == Keys.Control) return false;

        Keys key = keyData & Keys.KeyCode;
        if (key is not (Keys.Up or Keys.Down or Keys.Left or Keys.Right
            or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Escape))
            return false;

        if (remoteSession && key is Keys.Left or Keys.Right or Keys.Home or Keys.End)
            return false;

        return true;
    }

    /// <summary>
    /// Whether an arrow in a telnet command line recalls BlindTerm's local sent-line history.
    /// Only unmodified Up and Down do this. Left and Right remain editing/navigation keys, and
    /// arrows in the output stay in the output so somebody reading never gets moved away.
    /// </summary>
    public static bool ShouldRecallTelnetHistory(Keys keyData, bool remoteSession,
        bool terminalInputFocused)
        => remoteSession
            && terminalInputFocused
            && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.None
            && (keyData & Keys.KeyCode) is Keys.Up or Keys.Down;

    /// <summary>
    /// Whether a typed character in line-mode output should start typing in the command line.
    /// Navigation and control characters are deliberately excluded, so arrows keep reading the
    /// output exactly where its caret already is.
    /// </summary>
    public static bool ShouldMoveTypingToInput(char character, bool screenMode,
        bool outputFocused, bool inputEnabled)
        => !screenMode && outputFocused && inputEnabled && !char.IsControl(character);
}
