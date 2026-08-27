using System.ComponentModel;
using System.Media;
using System.Runtime.Versioning;
using System.Text;
using BlindTerm.App.Defterm;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using BlindTerm.Core.Speech;
using BlindTerm.Core.Updates;

namespace BlindTerm.App;

/// <summary>
/// Three controls, top to bottom, and two ways of using them.
///
///  1. Transcript -- a read-only multiline text box, which is a real Win32 edit control. That
///     is the whole accessibility strategy: NVDA and JAWS have read edit controls perfectly
///     for twenty years, so arrowing by line, word and character, say-all, braille following
///     the caret, the JAWS cursor, find, select and copy all work without a line of code here.
///  2. Current line -- whatever the program has not finished printing. A prompt waiting for an
///     answer never ends in a newline, so it never becomes a transcript line and this is the
///     only place it is ever seen.
///  3. Command line -- an ordinary text box. Enter sends.
///
/// When a full-screen program takes the screen the window switches to screen mode: every
/// keystroke goes to the program instead of to a control, and reading follows the cursor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MainForm : Form
{
    private readonly TextBox _transcript = new();
    private readonly ScreenSurface _screenSurface = new();
    // A native edit control gives NVDA and JAWS their normal keyboard-echo context while the
    // form routes the actual keystrokes to the full-screen program. Its text never changes.
    private readonly KeyboardEchoProxy _screenKeyboard;
    private readonly Label _live = new();
    private readonly TextBox _command = new();
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _speakOutputItem = new("Speak &output");
    private readonly ToolStripMenuItem _speakOffCursorItem = new("Speak &background changes");
    private readonly ToolStripMenuItem _checkUpdatesItem = new("Check for &updates...");
    private readonly ToolStripMenuItem _defaultTerminalItem = new("Use BlindTerm as the &default terminal");
    private readonly System.Windows.Forms.Timer _reviewFocusSpeechTimer = new() { Interval = 120 };

    private readonly TerminalHost _host;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly UpdateClient _updates = new();
    private readonly LineNews _news = new();
    private readonly ScreenNews _screenNews = new();
    private readonly ForegroundProgramState _foregroundProgram = new();

    private readonly List<string> _history = new();
    private int _historyIndex;
    private int _commandBlockIndex = -1;
    private bool _nanoAnnounced;
    private bool _askedAboutDefaultTerminal;
    private string? _nanoPrompt;
    private int _proxyNanoRow = -1;

    /// <summary>The screen a full-screen program is showing, or null in line mode.</summary>
    private string[]? _screen;

    /// <summary>
    /// While set, keys are sent to the program raw even in line mode. This is the escape
    /// hatch for a chord BlindTerm would otherwise claim.
    /// </summary>
    private bool _passThroughNext;

    /// <summary>
    /// Set while the screen is frozen for reading.
    ///
    /// In a full-screen program every key belongs to the program, which leaves nothing to
    /// read the screen *with*: an arrow key moves the editor's cursor rather than moving down
    /// a line. Review mode is the way out. It stops the frame being redrawn, hands the
    /// keyboard back to the text box, and lets the screen reader do what it does to any edit
    /// control -- line, word and character navigation, say-all, braille following the caret.
    /// The program carries on running, untouched, and nothing typed reaches it.
    /// </summary>
    private bool _reviewing;

    /// <summary>Whether keystrokes are going straight to the program.</summary>
    private bool LivePassthrough => _screen is not null && !_reviewing;

    /// <summary>Whether the caret has been parked away from the end to read something.</summary>
    private bool FollowingOutput => _screen is not null || _transcript.SelectionStart >= LastLineStart;

    private bool ScreenMode => _screen is not null;

    public MainForm(TerminalHost host, AppSettings settings, SettingsStore settingsStore)
    {
        _host = host;
        _screenKeyboard = new KeyboardEchoProxy();
        _settings = settings;
        _settingsStore = settingsStore;

        Text = "BlindTerm";
        Width = 1000;
        Height = 700;
        KeyPreview = true;

        BuildMenu();
        BuildControls();

        // Moving focus between the live surface and the review edit control makes screen
        // readers announce the control name, role and states. Speak the useful result just
        // after that focus event so it replaces that furniture instead of being replaced by
        // it. The short delay lets NVDA finish handling the key gesture and its resulting
        // focus event; without it, NVDA's later focus announcement replaces the useful line.
        _reviewFocusSpeechTimer.Tick += (_, _) =>
        {
            _reviewFocusSpeechTimer.Stop();
            if (_reviewing) SpeakCaretLine();
            else if (ScreenMode) Say("Back to the program");
        };

        _host.Updated += OnUpdated;
        _host.Bell += OnBell;
        _host.TitleChanged += title => Text = string.IsNullOrWhiteSpace(title) ? "BlindTerm" : $"{title} — BlindTerm";
        _host.Exited += OnExited;
    }

    // ---- Construction ----

    private void BuildControls()
    {
        var font = new Font("Consolas", 11f);

        _transcript.Multiline = true;
        _transcript.ReadOnly = true;
        _transcript.ScrollBars = ScrollBars.Vertical;
        _transcript.WordWrap = false;
        // Keep the selection visible when focus moves away, so a reader still reports it.
        _transcript.HideSelection = false;
        _transcript.Font = font;
        _transcript.Dock = DockStyle.Fill;
        _transcript.AccessibleName = "Transcript";
        _transcript.AccessibleRole = AccessibleRole.Text;
        _transcript.TabIndex = 0;

        _screenSurface.Dock = DockStyle.Fill;
        _screenSurface.TabIndex = 0;
        _screenSurface.Visible = false;

        _screenKeyboard.BorderStyle = BorderStyle.None;
        _screenKeyboard.BackColor = SystemColors.Window;
        _screenKeyboard.ForeColor = SystemColors.WindowText;
        _screenKeyboard.Size = new Size(2, 2);
        _screenKeyboard.Location = new Point(0, 0);
        _screenKeyboard.TabStop = false;
        _screenKeyboard.Visible = false;

        _live.AutoSize = true;
        _live.Padding = new Padding(2, 0, 2, 0);
        _live.Font = font;
        _live.Dock = DockStyle.Fill;
        _live.TabStop = false;
        _live.AccessibleName = "Current line";
        _live.TabIndex = 1;

        _command.Font = font;
        _command.Dock = DockStyle.Fill;
        _command.AccessibleName = "Command line";
        _command.TabIndex = 2;
        _command.KeyDown += OnCommandKeyDown;

        // The transcript and live screen share the large top region. Only one is visible:
        // the edit control while reading, and the non-editable surface while keys belong to
        // a full-screen program. The other two rows remain one line each.
        var terminalView = new Panel { Dock = DockStyle.Fill };
        terminalView.Controls.Add(_transcript);
        terminalView.Controls.Add(_screenSurface);
        terminalView.Controls.Add(_screenKeyboard);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(terminalView, 0, 0);
        layout.Controls.Add(_live, 0, 1);
        layout.Controls.Add(_command, 0, 2);

        Controls.Add(layout);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
    }

    /// <summary>
    /// Every command also lives in the menu. Alt is the application-command modifier, leaving
    /// ordinary Ctrl editing keys alone. The menu is how anyone finds the commands, and both
    /// readers announce menus perfectly.
    /// </summary>
    private void BuildMenu()
    {
        var terminal = new ToolStripMenuItem("&Terminal");
        terminal.DropDownItems.Add(Item("Change &directory...", AppShortcuts.ChangeDirectory,
            ChangeDirectory));
        terminal.DropDownItems.Add(Item("&Settings...", Keys.None, ShowSettings));
        _defaultTerminalItem.CheckOnClick = false;
        _defaultTerminalItem.Click += (_, _) => ToggleDefaultTerminal();
        _defaultTerminalItem.AccessibleDescription =
            "Whether Windows opens BlindTerm when a command-line program needs a terminal.";
        RefreshDefaultTerminalItem();
        terminal.DropDownItems.Add(_defaultTerminalItem);
        _checkUpdatesItem.Click += async (_, _) => await CheckForUpdates();
        terminal.DropDownItems.Add(_checkUpdatesItem);
        terminal.DropDownItems.Add(new ToolStripSeparator());
        terminal.DropDownItems.Add(Item("Send Ctrl+&C (interrupt)", AppShortcuts.Interrupt,
            () => _host.Send([0x03])));
        terminal.DropDownItems.Add(Item("Send &Escape", AppShortcuts.Escape,
            () => _host.Send([0x1b])));
        terminal.DropDownItems.Add(Item("Send Shift+&Tab", Keys.None,
            () => _host.Send([0x1b, (byte)'[', (byte)'Z'])));
        terminal.DropDownItems.Add(new ToolStripSeparator());
        terminal.DropDownItems.Add(Item("&Pass next chord to the program", AppShortcuts.PassNext,
            () => { _passThroughNext = true; Say("Pass through next key"); }));
        terminal.DropDownItems.Add(new ToolStripSeparator());
        terminal.DropDownItems.Add(Item("E&xit", Keys.None, Close));

        var read = new ToolStripMenuItem("&Read");
        read.DropDownItems.Add(Item("&Read the screen (freeze and navigate)", AppShortcuts.ToggleReview, ToggleReview));
        read.DropDownItems.Add(Item("Speak &current line", AppShortcuts.SpeakCurrentLine, SpeakCurrentLine));
        read.DropDownItems.Add(Item("Speak whole &screen", AppShortcuts.SpeakScreen, SpeakScreen));
        _speakOutputItem.Checked = true;
        _speakOutputItem.ShortcutKeys = AppShortcuts.ToggleSpeakOutput;
        _speakOutputItem.Click += (_, _) => ToggleSpeakOutput();
        read.DropDownItems.Add(_speakOutputItem);
        _speakOffCursorItem.Click += (_, _) => ToggleOffCursor();
        read.DropDownItems.Add(_speakOffCursorItem);

        var go = new ToolStripMenuItem("&Go");
        go.DropDownItems.Add(Item("&Transcript", AppShortcuts.FocusTranscript, FocusTranscript));
        go.DropDownItems.Add(Item("&Command line", AppShortcuts.FocusCommandLine, FocusCommandLine));
        go.DropDownItems.Add(Item("&End of transcript", AppShortcuts.EndOfTranscript, GoToEnd));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(Item("Copy &all", AppShortcuts.CopyAll, CopyAll));
        edit.DropDownItems.Add(Item("Copy current command &output", AppShortcuts.CopyCommandOutput, CopyCommandOutput));

        go.DropDownItems.Add(Item("&Previous command", AppShortcuts.PreviousCommand, PreviousCommand));
        go.DropDownItems.Add(Item("&Next command", AppShortcuts.NextCommand, NextCommand));

        _menu.Items.AddRange([terminal, read, go, edit]);
        _menu.Dock = DockStyle.Top;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Terminal updates ----

    private void OnUpdated(TerminalUpdate update)
    {
        // Output is read on a background thread and marshalled here, so an update can still be
        // in flight when the window closes -- most reliably when the shell is told to exit and
        // its farewell line arrives just after. Appending to a disposed text box throws on the
        // UI thread, which ends the process rather than the window.
        if (IsDisposed || Disposing) return;

        // An OSC 133 completed-command marker means the program returned control to the shell.
        // Until then its Ctrl shortcuts must keep reaching it even when it uses inline output.
        _foregroundProgram.Updated(_host.Core.CommandBlocks.Blocks.Count);

        if (update.AlternateScreen is not null)
        {
            EnterOrUpdateScreenMode(update);
            return;
        }

        if (_screen is not null)
        {
            // LeaveScreenMode restores the authoritative transcript, focus and speech. The
            // exit update must not be applied again by the normal line-mode pipeline.
            LeaveScreenMode();
            return;
        }

        MirrorEdits(update.Edits);
        MirrorAppended(update.NewLines);

        _host.Announcer.Enqueue(_news.News(update));

        if (_live.Text != update.LiveText) _live.Text = update.LiveText;
    }

    private void EnterOrUpdateScreenMode(TerminalUpdate update)
    {
        string[] rows = update.AlternateScreen
            ?? throw new ArgumentException("A screen-mode update must contain screen rows.", nameof(update));
        bool entering = _screen is null;
        _screen = rows;
        _screenSurface.SetRows(rows, update.CursorRow);
        bool nano = IsNano(rows);
        if (nano && IsNanoBodyRow(rows, update.CursorRow) && _proxyNanoRow != update.CursorRow)
        {
            _screenKeyboard.SetLine(rows[update.CursorRow], update.CursorColumn);
            _proxyNanoRow = update.CursorRow;
        }

        if (entering)
        {
            _host.Announcer.DiscardPending();
            _news.Reset();
            _screenNews.Reset();
            _reviewing = false;
            _transcript.Visible = false;
            _screenSurface.Visible = true;
            _screenSurface.BringToFront();
            _screenKeyboard.Visible = true;
            _screenKeyboard.BringToFront();
            _live.Visible = false;
            _command.Enabled = false;
            _command.Visible = false;
            // This focus target has no text value and no caret. NVDA therefore leaves arrow
            // keys alone instead of waiting for a local caret move and re-reading a hint on
            // every key; BlindTerm alone announces the program's resulting cursor movement.
            _screenKeyboard.Focus();
        }

        AnnounceNano(rows);
        AnnounceNanoPrompt(rows);

        // Frozen for reading. The program is still running and still repainting; the point of
        // review mode is that none of it disturbs the text being read.
        if (_reviewing) return;

        var announcement = _screenNews.News(_screen!, update.CursorRow, update.CursorColumn);
        if (!announcement.IsEmpty && !nano)
            _host.Announcer.AnnounceNow(announcement.Text, announcement.Priority);
    }

    /// <summary>
    /// Freezes the screen and hands the keyboard back, or gives it to the program again.
    /// </summary>
    private void ToggleReview()
    {
        if (!ScreenMode)
        {
            // In line mode the transcript is always readable; there is nothing to freeze.
            FocusTranscript();
            return;
        }

        _reviewing = !_reviewing;

        if (_reviewing)
        {
            SetTranscriptText(string.Join(Environment.NewLine, _screen!));
            _transcript.Visible = true;
            _transcript.BringToFront();
            _live.Visible = false;
            _command.Visible = false;

            // Land on the row the program's cursor is on, so reading starts where the user is
            // rather than at the top of a screenful of editor furniture.
            int row = Math.Clamp(_host.Engine.CursorScreenRow, 0, Math.Max(0, _transcript.Lines.Length - 1));
            MoveCaret(_transcript.GetFirstCharIndexFromLine(row));
            _transcript.Focus();
            _screenSurface.Visible = false;
            _screenKeyboard.Visible = false;
            RestartReviewFocusSpeechTimer();
        }
        else
        {
            _screenSurface.SetRows(_screen!, _host.Engine.CursorScreenRow);
            _screenSurface.Visible = true;
            _screenSurface.BringToFront();
            _screenKeyboard.Visible = true;
            _screenKeyboard.BringToFront();
            _live.Visible = false;
            _command.Visible = false;
            _screenKeyboard.Focus();
            _transcript.Visible = false;
            _screenNews.Reset();
            RestartReviewFocusSpeechTimer();
        }
    }

    private void RestartReviewFocusSpeechTimer()
    {
        _reviewFocusSpeechTimer.Stop();
        _reviewFocusSpeechTimer.Start();
    }

    private void LeaveScreenMode()
    {
        _screen = null;
        _reviewing = false;
        _screenNews.Reset();
        _nanoAnnounced = false;
        _nanoPrompt = null;
        _proxyNanoRow = -1;
        _screenSurface.Visible = false;
        _screenKeyboard.Visible = false;
        _transcript.Visible = true;
        _transcript.BringToFront();
        _live.Visible = true;
        _command.Enabled = true;
        _command.Visible = true;
        // The transcript is authoritative again, and it kept its text throughout.
        SetTranscriptText(_host.Transcript.Text());
        MoveCaret(LastLineStart);
        _command.Focus();
    }

    private void OnBell()
    {
        SystemSounds.Beep.Play();
        string detail = _live.Text.Trim();
        // Claude Code rings the bell when it wants input, so this is how you know it is your
        // turn. Saying only "attention" would leave you to go and look.
        _host.Announcer.AnnounceNow(detail.Length > 0 ? $"Attention. {detail}" : "Attention");
    }

    private void AnnounceNano(string[] rows)
    {
        if (_nanoAnnounced) return;
        string? file = ScreenNews.NanoFileName(rows);
        if (file is null) return;
        _nanoAnnounced = true;
        _host.Announcer.AnnounceNow($"New nano. {file}", SpeechPriority.Now);
    }

    private static bool IsNano(string[] rows)
        => rows.Any(row => row.TrimStart().StartsWith("GNU nano ", StringComparison.OrdinalIgnoreCase));

    private static bool IsNanoBodyRow(string[] rows, int row)
        => rows.Length <= 3 || row >= 1 && row < rows.Length - 2;

    private void AnnounceNanoPrompt(string[] rows)
    {
        string? prompt = ScreenNews.NanoPrompt(rows);
        if (prompt is null)
        {
            _nanoPrompt = null;
            return;
        }
        if (string.Equals(prompt, _nanoPrompt, StringComparison.Ordinal)) return;
        _nanoPrompt = prompt;
        _host.Announcer.AnnounceNow(prompt, SpeechPriority.Now);
    }

    /// <summary>
    /// Reflects the current choice in the menu, so the item reads as a state rather than as a
    /// command with an unknown effect. Both readers announce the checked state of a menu item.
    /// </summary>
    private void RefreshDefaultTerminalItem()
    {
        if (!DefaultTerminalConfig.IsSupported)
        {
            _defaultTerminalItem.Enabled = false;
            _defaultTerminalItem.ToolTipText = "Windows 11 or later is needed to choose a default terminal.";
            return;
        }

        DefaultTerminalConfig.Selection selection = DefaultTerminalConfig.Read();
        _defaultTerminalItem.Checked = DefaultTerminalConfig.IsFullyRegistered();
        _defaultTerminalItem.ToolTipText = _defaultTerminalItem.Checked
            ? "Windows opens BlindTerm for command-line programs."
            : $"Windows currently opens {DefaultTerminalConfig.Describe(selection)}.";
    }

    private void ToggleDefaultTerminal()
    {
        bool wasDefault = DefaultTerminalConfig.IsFullyRegistered();
        string message = wasDefault
            ? DefaultTerminalPrompt.Revert(this)
            : DefaultTerminalPrompt.Apply(this);

        RefreshDefaultTerminalItem();
        Say(message);
    }

    private void OnExited(int? code)
    {
        if (IsDisposed || Disposing) return;

        _foregroundProgram.Exited();

        string what = _host.IsHandoff ? "Program" : "Shell";
        string message = code is null ? $"[{what} exited]" : $"[{what} exited with code {code}]";
        _host.AppendExternal([message]);
        _live.Text = message;
        _command.Enabled = false;
        Say(message);
    }

    // ---- Mirroring the transcript into the text box ----

    private int LastLineStart
    {
        get
        {
            string text = _transcript.Text;
            int end = text.TrimEnd('\r', '\n').Length;
            int start = text.LastIndexOf('\n', Math.Max(0, end - 1));
            return start + 1;
        }
    }

    private void SetTranscriptText(string text)
    {
        _transcript.Text = text;
    }

    /// <summary>
    /// Adds lines the assembler has already put in the transcript.
    ///
    /// The caret is the reading position: if the user has moved back to read something, new
    /// output must not drag them away from it, so the selection is restored explicitly rather
    /// than trusted to survive an append.
    /// </summary>
    private void MirrorAppended(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || ScreenMode) return;

        bool follow = FollowingOutput;
        int selection = _transcript.SelectionStart;
        int length = _transcript.SelectionLength;

        var chunk = new StringBuilder();
        foreach (string line in lines) chunk.Append(line).Append(Environment.NewLine);

        _transcript.AppendText(chunk.ToString());

        // The caret goes back where it was either way. When following, the view scrolls to
        // the new output but the caret stays put: this app has already announced the lines,
        // and moving the caret would have the reader announce them again.
        _transcript.SelectionStart = selection;
        _transcript.SelectionLength = length;
        if (follow) TextBoxScroll.ToBottom(_transcript);
    }

    /// <summary>
    /// Rewrites lines whose rows a program has redrawn, in the order the assembler made them:
    /// each edit's range is the range to replace once the ones before it are in.
    /// </summary>
    private void MirrorEdits(IReadOnlyList<Transcript.Edit> edits)
    {
        if (edits.Count == 0 || ScreenMode) return;

        bool follow = FollowingOutput;
        int selection = _transcript.SelectionStart;

        foreach (var edit in edits)
        {
            // Offsets are in UTF-16 units and the box counts newlines as two, so translate.
            int start = ToBoxOffset(edit.Start);
            int end = ToBoxOffset(edit.Start + edit.OldLength);
            if (start < 0 || end > _transcript.TextLength || end < start) continue;

            _transcript.Select(start, end - start);
            _transcript.SelectedText = edit.Text;

            int delta = edit.Text.Length - edit.OldLength;
            if (selection >= end) selection += delta;
        }

        _transcript.SelectionStart = Math.Min(selection, _transcript.TextLength);
        _transcript.SelectionLength = 0;
        if (follow) TextBoxScroll.ToBottom(_transcript);
    }

    /// <summary>
    /// The assembler counts one character per line break; a multiline text box counts two.
    /// Converting is a matter of adding the number of lines that start before the offset.
    /// </summary>
    private int ToBoxOffset(int transcriptOffset)
    {
        int line = _host.Transcript.LineAtOffset(transcriptOffset);
        return transcriptOffset + line;
    }

    // ---- Commands ----

    private void Say(string text) => _host.Announcer.AnnounceNow(text);

    private void FocusTranscript()
    {
        if (ScreenMode && !_reviewing)
        {
            ToggleReview();
            return;
        }

        _transcript.Focus();
        MoveCaret(LastLineStart);
    }

    private void FocusCommandLine()
    {
        if (_command.Enabled) _command.Focus();
    }

    private void GoToEnd()
    {
        _transcript.Focus();
        MoveCaret(LastLineStart);
        SpeakCaretLine();
    }

    private void MoveCaret(int offset)
    {
        _transcript.SelectionStart = Math.Clamp(offset, 0, _transcript.TextLength);
        _transcript.SelectionLength = 0;
        _transcript.ScrollToCaret();
    }

    private void SpeakCaretLine()
    {
        int line = _transcript.GetLineFromCharIndex(_transcript.SelectionStart);
        string[] lines = _transcript.Lines;
        if (line >= 0 && line < lines.Length)
            Say(string.IsNullOrWhiteSpace(lines[line]) ? "blank" : lines[line]);
    }

    private void SpeakCurrentLine()
    {
        if (ScreenMode)
        {
            string row = _screen is not null && _host.Engine.CursorScreenRow < _screen.Length
                ? _screen[_host.Engine.CursorScreenRow]
                : string.Empty;
            Say(row.Trim().Length > 0 ? row : "Cursor line is empty");
            return;
        }
        Say(_live.Text.Trim().Length > 0 ? _live.Text : "Current line is empty");
    }

    private void SpeakScreen()
    {
        string text = _screen is not null
            ? ScreenNews.Whole(_screen)
            : string.Join("\n", _transcript.Lines.TakeLast(_host.Engine.Rows));
        Say(text.Length > 0 ? text : "Nothing on screen");
    }

    private void ToggleSpeakOutput()
    {
        _host.Announcer.Enabled = !_host.Announcer.Enabled;
        _speakOutputItem.Checked = _host.Announcer.Enabled;
        Say(_host.Announcer.Enabled ? "Speak output on" : "Speak output off");
    }

    private void ToggleOffCursor()
    {
        _screenNews.SpeakOffCursorChanges = !_screenNews.SpeakOffCursorChanges;
        _speakOffCursorItem.Checked = _screenNews.SpeakOffCursorChanges;
        Say(_screenNews.SpeakOffCursorChanges
            ? "Speaking background changes"
            : "Background changes silent");
    }

    /// <summary>
    /// Picks a folder and changes to it.
    ///
    /// Typing a long path at a prompt means either remembering it exactly or driving tab
    /// completion, and the folder picker is a dialog both readers navigate well. What it does
    /// is send an ordinary cd, so the shell's own directory is what changes and nothing here
    /// has to track where the session thinks it is.
    /// </summary>
    private void ChangeDirectory()
    {
        if (ScreenMode)
        {
            Say("Not while a full-screen program is running");
            return;
        }
        if (!_host.IsRunning)
        {
            Say("The shell has exited");
            return;
        }

        using var picker = new FolderBrowserDialog
        {
            Description = "Change the terminal's directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        string path = picker.SelectedPath;
        // Quote it: a chosen folder very often has spaces in it, and the single quotes stop
        // PowerShell expanding anything inside. A literal quote in a path is doubled to escape
        // it, which is the same in PowerShell and in POSIX shells.
        _host.SendLine($"cd '{path.Replace("'", "''")}'");
        Say($"Changing to {path}");
        FocusCommandLine();
    }

    private void CopyAll()
    {
        string text = ScreenMode && _screen is not null
            ? string.Join(Environment.NewLine, _screen)
            : _transcript.Text;
        if (text.Length > 0) Clipboard.SetText(text);
        Say("Copied");
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        AppSettings next = dialog.Settings;
        try
        {
            _settingsStore.Save(next);
            bool resized = next.Columns != _host.Engine.Columns || next.Rows != _host.Engine.Rows;
            if (resized) _host.Resize(next.Columns, next.Rows);
            _settings.Shell = next.Shell;
            _settings.Columns = next.Columns;
            _settings.Rows = next.Rows;
            Say(resized ? "Settings saved and terminal resized" : "Settings saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not apply settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PreviousCommand() => MoveCommand(-1);

    private void NextCommand() => MoveCommand(1);

    private void MoveCommand(int delta)
    {
        IReadOnlyList<CommandBlock> blocks = _host.Core.CommandBlocks.Blocks;
        if (blocks.Count == 0)
        {
            Say("No command blocks");
            return;
        }

        _commandBlockIndex = Math.Clamp(_commandBlockIndex < 0 ? (delta < 0 ? blocks.Count : -1) : _commandBlockIndex,
            -1, blocks.Count);
        int next = Math.Clamp(_commandBlockIndex + delta, 0, blocks.Count - 1);
        CommandBlock block = blocks[next];
        _commandBlockIndex = next;
        if (!block.IsResolved)
        {
            Say("Command location is not available");
            return;
        }
        FocusTranscript();
        MoveCaret(_host.Transcript.OffsetOfLine(block.StartLine));
        Say($"Command {next + 1} of {blocks.Count}");
    }

    private void CopyCommandOutput()
    {
        IReadOnlyList<CommandBlock> blocks = _host.Core.CommandBlocks.Blocks;
        if (blocks.Count == 0)
        {
            CopyAll();
            return;
        }
        int index = _commandBlockIndex < 0 ? blocks.Count - 1 : _commandBlockIndex;
        string text = _host.Core.CommandBlocks.CopyOutput(index, _host.Transcript);
        if (text.Length > 0) Clipboard.SetText(text);
        Say("Copied command output");
    }

    private async Task CheckForUpdates()
    {
        _checkUpdatesItem.Enabled = false;
        try
        {
            UpdateManifest? manifest = await _updates.CheckAsync();
            if (manifest is null)
            {
                Say($"BlindTerm {VersionInfo.Display} is up to date");
                return;
            }

            string notes = string.IsNullOrWhiteSpace(manifest.NotesSummary)
                ? $"BlindTerm {manifest.Version} is available."
                : $"BlindTerm {manifest.Version} is available. {manifest.NotesSummary}";
            if (MessageBox.Show(this, $"{notes}\n\nDownload and install it now?", "BlindTerm update",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            Say("Downloading update");
            string archive = await _updates.DownloadAsync(manifest);
            UpdateClient.LaunchApply(archive, Environment.ProcessId,
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Say("Update downloaded. BlindTerm will restart");
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                   or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Could not update BlindTerm", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Say("Update failed");
        }
        finally
        {
            if (!IsDisposed) _checkUpdatesItem.Enabled = true;
        }
    }

    // ---- Input ----

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                Submit();
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.Up:
                StepHistory(-1);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
            case Keys.Down:
                StepHistory(1);
                e.SuppressKeyPress = true;
                e.Handled = true;
                break;
        }
    }

    private void Submit()
    {
        string text = _command.Text;
        string accessible = AccessibleAgentCommand.Adapt(text);
        _foregroundProgram.Submitted(text, _host.Core.CommandBlocks.Blocks.Count);
        _news.SuppressCommandEcho(accessible);
        _host.SendLine(accessible);
        if (text.Length > 0 && (_history.Count == 0 || _history[^1] != text)) _history.Add(text);
        _historyIndex = _history.Count;
        _command.Clear();
    }

    private void StepHistory(int delta)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
        _command.Text = _historyIndex == _history.Count ? string.Empty : _history[_historyIndex];
        _command.SelectionStart = _command.TextLength;
    }

    /// <summary>
    /// Claims keys before the framework can act on them. In screen mode that is nearly all of
    /// them: Tab would move focus, arrows would move between controls, Escape would close the
    /// window, and every one of those belongs to the program instead.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_passThroughNext)
        {
            byte[]? passed = KeyTranslator.Translate(keyData, _host.Engine.ApplicationCursorKeys);
            if (passed is not null)
            {
                _passThroughNext = false;
                _host.Send(passed);
                return true;
            }
        }

        // Alt belongs to BlindTerm and its menu everywhere, so there is always a way back out.
        // Pass Next is checked first so an occasional Meta/Alt chord can still reach the program.
        if (AppShortcuts.IsApplicationChord(keyData))
            return base.ProcessCmdKey(ref message, keyData);

        // Inline programs never enter alternate-screen mode. Their Ctrl+C, Ctrl+X, Ctrl+Z and
        // other control commands still belong to them until the shell's completed-command
        // marker says the foreground command exited. A handoff is itself the foreground app.
        bool foregroundLineProgram = !ScreenMode && (_foregroundProgram.Active || _host.IsHandoff);
        if (AppShortcuts.ShouldPassControlChord(keyData, foregroundLineProgram))
        {
            byte[]? control = KeyTranslator.Translate(keyData, _host.Engine.ApplicationCursorKeys);
            if (control is not null)
            {
                _host.Send(control);
                return true;
            }
        }

        // While the screen is frozen for reading, the keyboard belongs to the text box again,
        // which is the only way to read a full-screen program line by line.
        if (!LivePassthrough) return base.ProcessCmdKey(ref message, keyData);

        if (KeyboardEchoProxy.IsTerminalNavigation(keyData))
        {
            byte[]? navigation = KeyTranslator.Translate(keyData, _host.Engine.ApplicationCursorKeys);
            if (navigation is not null) _host.Send(navigation);
            return true;
        }

        byte[]? bytes = KeyTranslator.Translate(keyData, _host.Engine.ApplicationCursorKeys);
        if (bytes is null) return base.ProcessCmdKey(ref message, keyData);

        _host.Send(bytes);
        // Let the native proxy process ordinary edit navigation and editing after the same key
        // has been sent to nano. This gives NVDA/JAWS a real caret event and preserves their
        // configured character/word/no-echo behavior. Terminal-only chords remain intercepted.
        return !KeyboardEchoProxy.IsNativeEditKey(keyData)
            || base.ProcessCmdKey(ref message, keyData);
    }

    /// <summary>
    /// Ordinary typing in screen mode. Translate handles the keys with a name; everything
    /// that is simply a character arrives here, already resolved for shift and the keyboard
    /// layout.
    /// </summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (LivePassthrough && !char.IsControl(e.KeyChar))
        {
            _host.Send(Encoding.UTF8.GetBytes(e.KeyChar.ToString()));
            // Do not mark the event handled. The proxy is a real edit control, so it updates its
            // local line and NVDA can apply the user's keyboard-echo preference normally.
            return;
        }
        base.OnKeyPress(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // A window BlindTerm was asked for arrives with the foreground already; one Windows
        // opened for a handed-over console does not, and has to take it.
        if (_host.IsHandoff) WindowActivation.TakeForeground(this, _command);
        else _command.Focus();

        OfferDefaultTerminal();
    }

    /// <summary>
    /// The startup offer, once. It runs after the window is up and focused so the dialog has
    /// an owner to be modal to, and so a screen reader announces the terminal first and the
    /// question second rather than the other way round.
    /// </summary>
    private void OfferDefaultTerminal()
    {
        if (_askedAboutDefaultTerminal || _host.IsHandoff) return;
        _askedAboutDefaultTerminal = true;
        if (!DefaultTerminalPrompt.ShouldAsk(_settings)) return;

        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            string? spoken = DefaultTerminalPrompt.AskAndApply(this, _settings, _settingsStore);
            RefreshDefaultTerminalItem();
            if (spoken is not null) Say(spoken);
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _reviewFocusSpeechTimer.Dispose();
        _updates.Dispose();
        _host.Dispose();
        base.OnFormClosed(e);
    }
}
