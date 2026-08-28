using System.ComponentModel;
using System.Media;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using BlindTerm.App.Defterm;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;
using BlindTerm.Core.Speech;
using BlindTerm.Core.Triggers;
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
    private readonly ToolStripMenuItem _mudSoundsItem = new("&MUD sounds");
    private readonly ToolStripMenuItem _downloadSoundsItem = new("&Download sounds a MUD offers");
    private readonly ToolStripMenuItem _mudStatusItem = new("MUD room and vitals in the &transcript");
    private readonly ToolStripMenuItem _speakMudStatusItem = new("Speak MUD room and &vitals");
    private readonly ToolStripMenuItem _triggersActiveItem = new("Trigg&ers are active");
    private readonly ToolStripMenuItem _checkUpdatesItem = new("Check for &updates...");
    private readonly ToolStripMenuItem _defaultTerminalItem = new("Use BlindTerm as the &default terminal");
    private readonly System.Windows.Forms.Timer _reviewFocusSpeechTimer = new() { Interval = 120 };
    // Sounds repeat by being started again when they finish, so something has to notice that
    // they have. Four times a second is under the gap anyone hears between two repeats.
    private readonly System.Windows.Forms.Timer _soundTimer = new() { Interval = 250 };
    // A shell redraws a completed line in as many pieces as it likes -- the flushed text
    // echoed back, then the completion replacing its last word. Reading the first piece would
    // announce half a command; this waits for the redrawing to stop.
    private readonly System.Windows.Forms.Timer _completionEchoTimer = new() { Interval = 120 };

    private readonly TerminalHost _host;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly UpdateClient _updates = new();
    private readonly TerminalNews _news = new();
    private readonly ScreenNews _screenNews = new();
    private readonly ForegroundProgramState _foregroundProgram;
    private readonly CommandCompletionInput _completionInput = new();
    private readonly CommandCompletionEcho _completionEcho = new();
    private readonly LatestResponse _latestResponse = new();

    private MspPlayer? _sounds;
    private SoundDownloader? _soundDownloads;

    /// <summary>
    /// What the user has asked to be told about, and what to do about each. Loaded from the
    /// settings when the window is built, and reloaded whenever the dialog changes them.
    /// </summary>
    private readonly TriggerEngine _triggers = new();

    /// <summary>Where a trigger's own sounds are played. Built the first time one asks.</summary>
    private SoundBoard? _triggerSounds;

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
        // Asked of the session rather than captured once: the window is built before the
        // shell it will show has been started, and what counts as a running program differs
        // between a shell, a handed-over console and a host on the network.
        _foregroundProgram = new ForegroundProgramState(() => _host.ProgramOwnsInput);
        _screenKeyboard = new KeyboardEchoProxy();
        _settings = settings;
        _settingsStore = settingsStore;
        ReloadTriggers();

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

        _soundTimer.Tick += (_, _) =>
        {
            _sounds?.Tick();
            _triggerSounds?.Tick();
        };
        _completionEchoTimer.Tick += (_, _) =>
        {
            _completionEchoTimer.Stop();
            SpeakCompletedLine();
        };

        _host.Updated += OnUpdated;
        _host.Bell += OnBell;
        _host.SoundRequested += OnSoundRequested;
        _host.StatusReceived += OnStatusReceived;
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
        _transcript.ShortcutsEnabled = true;
        _transcript.Font = font;
        _transcript.Dock = DockStyle.Fill;
        _transcript.AccessibleName = "Output";
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

        // Where the window opens, rather than where it lands and is then moved to. Without
        // this the output pane takes focus first because it is first in the tab order, and a
        // screen reader reads it out before the command line takes over -- so the window
        // announces itself twice before anything has happened.
        ActiveControl = _command;
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
        var triggersItem = Item("T&riggers...", AppShortcuts.Triggers, ShowTriggers);
        triggersItem.AccessibleDescription =
            "Things to watch the output for, and what to do when one of them happens: say "
            + "something, play a sound, keep a line quiet, or send a line back.";
        terminal.DropDownItems.Add(triggersItem);
        _triggersActiveItem.Checked = _settings.TriggersEnabled;
        _triggersActiveItem.ShortcutKeys = AppShortcuts.ToggleTriggers;
        _triggersActiveItem.AccessibleDescription =
            "The master switch over every trigger. Turning it off stops them all without "
            + "changing any of them.";
        _triggersActiveItem.Click += (_, _) => ToggleTriggers();
        terminal.DropDownItems.Add(_triggersActiveItem);
        terminal.DropDownItems.Add(Item("&Settings...", Keys.None, ShowSettings));
        terminal.DropDownItems.Add(Item("Connect to a telnet &host...", AppShortcuts.Connect,
            ConnectToHost));
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
        _mudSoundsItem.Checked = _settings.MudSounds;
        _mudSoundsItem.ShortcutKeys = AppShortcuts.ToggleMudSounds;
        _mudSoundsItem.AccessibleDescription =
            "Whether a MUD may play sounds. Its sound triggers are kept out of the text either way.";
        _mudSoundsItem.Click += (_, _) => ToggleMudSounds();
        read.DropDownItems.Add(_mudSoundsItem);
        // Beside the switch it is really part of. A MUD keeps its sounds on its own web
        // server, so for anyone without a sound pack already unpacked, turning sounds on and
        // leaving this off is turning on silence -- and the setting was buried in a dialog.
        _downloadSoundsItem.Checked = _settings.DownloadSounds;
        _downloadSoundsItem.AccessibleDescription =
            "Whether a sound a MUD offers may be fetched when this machine does not have it. "
            + "The address comes from the MUD.";
        _downloadSoundsItem.Click += (_, _) => ToggleDownloadSounds();
        read.DropDownItems.Add(_downloadSoundsItem);

        read.DropDownItems.Add(new ToolStripSeparator());
        read.DropDownItems.Add(Item("Speak &room and exits", AppShortcuts.SpeakRoom, SpeakRoom));
        read.DropDownItems.Add(Item("Speak &health and other pools", AppShortcuts.SpeakVitals, SpeakVitals));
        _mudStatusItem.Checked = _settings.MudStatus;
        _mudStatusItem.AccessibleDescription =
            "Whether what a MUD says about the room and the character is written into the "
            + "transcript as it happens.";
        _mudStatusItem.Click += (_, _) => ToggleMudStatus();
        read.DropDownItems.Add(_mudStatusItem);
        _speakMudStatusItem.Checked = _settings.SpeakMudStatus;
        _speakMudStatusItem.AccessibleDescription =
            "Whether those lines are also read out as they arrive, rather than only on request.";
        _speakMudStatusItem.Click += (_, _) => ToggleSpeakMudStatus();
        read.DropDownItems.Add(_speakMudStatusItem);
        read.DropDownItems.Add(Item("&Server information", ShowServerInformation));

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

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
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

        // A batch the app wrote itself carries no reading of the terminal, so the prompt the
        // shell is sitting at is whatever it already was. Applying this batch's empty live
        // text would blank the current line and drop password mode halfway through a login.
        if (!update.External)
        {
            if (_live.Text != update.LiveText) _live.Text = update.LiveText;
            CommandAccessibility.Apply(_command, update.LiveText);
            if (_completionEcho.Pending)
            {
                _completionEchoTimer.Stop();
                _completionEchoTimer.Start();
            }
        }

        // Worked out before anything is spoken, because one of the things a trigger can ask
        // for is that a line is not.
        TriggerOutcome fired = _triggers.Run(update.NewLines, SessionKind);

        IReadOnlyList<string> news = _news.News(update);
        if (fired.AnySilenced) news = [.. news.Where(line => !fired.IsSilenced(line))];
        _host.Announcer.Enqueue(news);

        Apply(fired);
    }

    /// <summary>Which kind of far end this window is showing, as a trigger asks about it.</summary>
    private TriggerWhere SessionKind
        => _host.Kind == TerminalSessionKind.Remote ? TriggerWhere.Mud : TriggerWhere.Shell;

    /// <summary>
    /// Does what a batch of lines asked the triggers for, in one order every time.
    ///
    /// Sounds first, because they are the fastest thing to hear and the one that says
    /// "something happened" before any words have arrived. Then speech, urgent lines going in
    /// at the head of the batch that is about to be spoken rather than ahead of it -- a line
    /// said now and cut off a twentieth of a second later by the output it was about is not a
    /// warning. Sending is last: it is the only one that changes anything at the far end.
    /// </summary>
    private void Apply(TriggerOutcome fired)
    {
        if (fired.IsEmpty) return;

        if (fired.Beep) SystemSounds.Exclamation.Play();
        foreach (string sound in fired.Sounds) PlayTriggerSound(sound);

        foreach (TriggerSpeech speech in fired.Speech)
        {
            if (speech.Now) _host.Announcer.Interject(speech.Text);
            else _host.Announcer.Enqueue([speech.Text]);
        }

        foreach (string line in fired.Sends) _host.SendLine(line);

        // A trigger that has been switched off for running away is a change to the list, and
        // it has to still be off tomorrow or the loop starts again on the next connection.
        if (fired.Notes.Count > 0)
        {
            TrySaveSettings();
            foreach (string note in fired.Notes) _host.Announcer.Interject(note);
        }
    }

    /// <summary>
    /// Plays a file a trigger named.
    ///
    /// The board is built the first time one is asked for rather than with the window: most
    /// sessions have no trigger with a sound on it, and there is no reason for them to open
    /// the multimedia layer at all. Nothing is said when a file will not play -- the dialog
    /// checks that when the trigger is saved, which is where it can be corrected.
    /// </summary>
    private void PlayTriggerSound(string path)
    {
        _triggerSounds ??= new SoundBoard(new MciSoundOutput());
        _triggerSounds.Volume = _settings.SoundVolume;
        if (_triggerSounds.Play(path) && !_soundTimer.Enabled) _soundTimer.Start();
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
            // A completion read back now would describe a line on a screen that is gone.
            EndCompletionEcho();
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

        // A connection dialled from a prompt ends the way a program run from a prompt ends:
        // by handing the window back to the shell that was underneath it, still where it was.
        // ReturnToShell says no when there was no shell, or when it died behind the
        // connection, and then this is an ordinary end of session after all.
        if (_host.Kind == TerminalSessionKind.Remote && _host.ReturnToShell())
        {
            _foregroundProgram.Resumed();
            _host.AppendExternal(["[Disconnected]"]);
            Text = _shellTitle ?? "BlindTerm";
            _shellTitle = null;
            // The prompt this shell is sitting at scrolled away while the connection was in
            // front of it. A bare Return makes it print another, so there is somewhere
            // visible to be.
            _host.SendLine(string.Empty);
            // Whatever the MUD last said about the room and the character was about a place
            // and a person this window is no longer showing.
            _mud.Reset();
            _command.Enabled = true;
            _live.Text = string.Empty;
            CommandAccessibility.Apply(_command, string.Empty);
            KeepFocus();
            return;
        }

        _foregroundProgram.Exited();
        EndCompletionEcho();

        string message = _host.Kind switch
        {
            // A closed socket has no exit code, and "exited with code 0" would be a lie about
            // a connection that simply ended.
            TerminalSessionKind.Remote => "[Disconnected]",
            TerminalSessionKind.Handoff when code is null => "[Program exited]",
            TerminalSessionKind.Handoff => $"[Program exited with code {code}]",
            _ when code is null => "[Shell exited]",
            _ => $"[Shell exited with code {code}]",
        };
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

    private void BeginLatestResponse()
    {
        if (_host.Kind != TerminalSessionKind.Remote) return;
        _latestResponse.Begin(_host.Transcript);
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
        var selection = new TextSelection(_transcript.SelectionStart, _transcript.SelectionLength);

        foreach (var edit in edits)
        {
            // Offsets are in UTF-16 units and the box counts newlines as two, so translate.
            int start = ToBoxOffset(edit.Start);
            int end = ToBoxOffset(edit.Start + edit.OldLength);
            if (start < 0 || end > _transcript.TextLength || end < start) continue;

            _transcript.Select(start, end - start);
            _transcript.SelectedText = edit.Text;
            selection = selection.AfterReplacement(start, end - start, edit.Text.Length);
        }

        int selectionStart = Math.Min(selection.Start, _transcript.TextLength);
        int selectionLength = Math.Min(selection.Length, _transcript.TextLength - selectionStart);
        _transcript.Select(selectionStart, selectionLength);
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

    private void FocusOutputAtLatestResponse()
    {
        if (_host.Kind != TerminalSessionKind.Remote)
        {
            FocusTranscript();
            return;
        }

        _transcript.Focus();
        MoveCaret(_latestResponse.StartOffset(_host.Transcript));
    }

    private void FocusCommandLine()
    {
        if (_command.Enabled) _command.Focus();
    }

    /// <summary>
    /// Puts focus on the command line only when it is not already somewhere the user chose to
    /// put it. A session ending underneath someone who is reading the output is not a reason
    /// to move them out of it.
    /// </summary>
    private void KeepFocus()
    {
        if (_command.Focused || _transcript.Focused) return;
        FocusCommandLine();
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

    /// <summary>
    /// Puts what the terminal completed back into the command box, and reads it out.
    ///
    /// Both halves are the point. Speaking it is the only way Tab is heard to have done
    /// anything: the completed line sits on the terminal's unfinished current line, which is
    /// announced only when it reads as a prompt, and a command ending in a file name never
    /// does. Putting it in the box is what keeps it somewhere it can be reviewed a character
    /// at a time and corrected, which is the whole reason there is a native edit in front of
    /// a terminal here. Typing after this reaches the terminal and the box alike, so the two
    /// stay in step.
    /// </summary>
    private void SpeakCompletedLine()
    {
        if (_completionEcho.Completed(_live.Text) is not { } completed) return;
        if (ScreenMode || !_completionInput.Active) return;

        _completionInput.Completed(completed);
        if (_command.Text != completed)
        {
            _command.Text = completed;
            _command.SelectionStart = _command.TextLength;
        }

        Say(completed);
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

    /// <summary>
    /// Acts on a sound a MUD asked for.
    ///
    /// The player is built the first time one arrives rather than with the window: most
    /// sessions are a shell and will never see one, and there is no reason for them to open
    /// the multimedia layer at all.
    /// </summary>
    private void OnSoundRequested(MspTrigger trigger)
    {
        if (IsDisposed || Disposing || !_settings.MudSounds) return;

        _sounds ??= BuildSoundPlayer();
        _sounds.MasterVolume = _settings.SoundVolume;
        if (_sounds.Handle(trigger) && !_soundTimer.Enabled) _soundTimer.Start();
    }

    private MspPlayer BuildSoundPlayer()
    {
        string folder = _settings.SoundDirectory.Length > 0
            ? _settings.SoundDirectory
            : SoundLibrary.DefaultDirectory;
        var library = new SoundLibrary(folder);

        // Only built when downloading is switched on, so nothing that could reach the network
        // exists in a session that has not asked for it.
        _soundDownloads = _settings.DownloadSounds ? new SoundDownloader(library) : null;
        SoundDownloader? downloads = _soundDownloads;

        var player = new MspPlayer(new MciSoundOutput(), library)
        {
            MasterVolume = _settings.SoundVolume,
            Download = downloads is null ? null : downloads.Fetch,
        };
        player.Unplayable += SaySoundProblem;
        _soundProblemSaid = null;
        return player;
    }

    /// <summary>The last thing said about sounds, so a busy MUD says it once and not per trigger.</summary>
    private MspProblem? _soundProblemSaid;

    /// <summary>
    /// Says why a sound was not heard.
    ///
    /// Once per reason, and naming what to do about it. "Attempting test sound" followed by
    /// nothing at all is the worst possible answer for someone who cannot see a settings
    /// window they were never told to open.
    /// </summary>
    private void SaySoundProblem(MspProblem problem)
    {
        if (IsDisposed || Disposing || _soundProblemSaid == problem) return;
        _soundProblemSaid = problem;
        Say(problem switch
        {
            MspProblem.NotHere =>
                "This MUD's sounds are not on this machine. Turn on Download sounds a MUD "
                + "offers, in the Read menu, to fetch them.",
            MspProblem.CouldNotFetch =>
                "This MUD's sounds could not be downloaded.",
            MspProblem.CannotPlay =>
                "Windows would not play this MUD's sound.",
            _ => "This MUD asked for something BlindTerm will not play as a sound.",
        });
    }

    private void ToggleMudSounds()
    {
        _settings.MudSounds = !_settings.MudSounds;
        _mudSoundsItem.Checked = _settings.MudSounds;

        if (!_settings.MudSounds)
        {
            // Silence means silence now, not once the current sound has finished.
            _soundTimer.Stop();
            _sounds?.Dispose();
            _sounds = null;
            _soundDownloads?.Dispose();
            _soundDownloads = null;
        }

        try { _settingsStore.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentOutOfRangeException)
        { }

        Say(_settings.MudSounds ? "MUD sounds on" : "MUD sounds off");
    }

    /// <summary>
    /// Allows or refuses fetching a sound this machine does not have.
    ///
    /// The player is rebuilt either way, because whether it may download at all is decided
    /// when it is made: a session that has not asked for this has nothing in it that could
    /// reach the network.
    /// </summary>
    private void ToggleDownloadSounds()
    {
        _settings.DownloadSounds = !_settings.DownloadSounds;
        _downloadSoundsItem.Checked = _settings.DownloadSounds;

        _soundTimer.Stop();
        _sounds?.Dispose();
        _sounds = null;
        _soundDownloads?.Dispose();
        _soundDownloads = null;

        TrySaveSettings();

        Say(_settings.DownloadSounds
            ? "Downloading MUD sounds on"
            : "Downloading MUD sounds off");
    }

    // ---- What the MUD says about itself ----

    /// <summary>
    /// The room and the character as the MUD last described them over GMCP, rather than as
    /// they were printed.
    /// </summary>
    private readonly MudStatus _mud = new();

    /// <summary>
    /// Records what a MUD has just said about the room or the character.
    ///
    /// It goes into the transcript where it happened, because that is where it will be looked
    /// for when reading back. Whether it is spoken as well is a separate choice, and off
    /// unless asked for: Core MUD sends the character's vitals after every single command.
    /// </summary>
    private void OnStatusReceived(GmcpMessage message)
    {
        if (IsDisposed || Disposing || !_settings.MudStatus) return;
        if (_mud.News(message) is not { } line) return;
        _host.AppendExternal([$"[{line}]"], quiet: !_settings.SpeakMudStatus);
    }

    /// <summary>
    /// Says where the character is and which ways out there are.
    ///
    /// This is the whole point of agreeing to GMCP. The exits are a list the MUD sent as a
    /// list, so answering "which way can I go" does not mean finding the word "Exits" in a
    /// paragraph and reading to the end of the line.
    /// </summary>
    private void SpeakRoom()
        => Say(_mud.Room ?? (_host.Kind == TerminalSessionKind.Remote
            ? "This MUD has not said what room this is."
            : "Room information comes from a MUD."));

    private void SpeakVitals()
        => Say(_mud.Vitals ?? (_host.Kind == TerminalSessionKind.Remote
            ? "This MUD has not said how the character is doing."
            : "Character information comes from a MUD."));

    private void ToggleMudStatus()
    {
        _settings.MudStatus = !_settings.MudStatus;
        _mudStatusItem.Checked = _settings.MudStatus;
        TrySaveSettings();
        Say(_settings.MudStatus
            ? "MUD room and vitals in the transcript"
            : "MUD room and vitals out of the transcript");
    }

    private void ToggleSpeakMudStatus()
    {
        _settings.SpeakMudStatus = !_settings.SpeakMudStatus;
        _speakMudStatusItem.Checked = _settings.SpeakMudStatus;
        TrySaveSettings();
        Say(_settings.SpeakMudStatus
            ? "Speaking MUD room and vitals"
            : "MUD room and vitals on request only");
    }

    /// <summary>
    /// What the connected server said about itself when it was asked, over MSSP.
    ///
    /// A dialog rather than speech: it is a page of facts, and a page of facts is something to
    /// read at your own pace with the arrow keys rather than have recited.
    /// </summary>
    private void ShowServerInformation()
    {
        IReadOnlyDictionary<string, string> status = _host.ServerStatus;
        if (status.Count == 0)
        {
            Say(_host.Kind == TerminalSessionKind.Remote
                ? "This host did not say anything about itself."
                : "Server information comes from a MUD.");
            return;
        }

        string text = string.Join(Environment.NewLine, status
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{Readable(entry.Key)}: {entry.Value}"));

        using var dialog = new Form
        {
            Text = status.TryGetValue("NAME", out string? name)
                ? $"{name} — server information"
                : "Server information",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(520, 420),
        };
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = text,
            AccessibleName = "Server information",
            HideSelection = false,
        };
        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AccessibleName = "Close server information",
        };
        dialog.Controls.Add(box);
        dialog.Controls.Add(close);
        // Escape closes it, which is what a window holding nothing to decide should do.
        dialog.CancelButton = close;
        dialog.AcceptButton = close;
        dialog.ActiveControl = box;
        dialog.ShowDialog(this);
    }

    /// <summary>MSSP names variables in shouting snake case; that is not how anyone reads.</summary>
    private static string Readable(string variable)
    {
        string spaced = variable.Replace('_', ' ').Trim();
        return spaced.Length == 0
            ? variable
            : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
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
        if (_host.Kind == TerminalSessionKind.Remote)
        {
            Say("There is no local directory to change on a remote host");
            return;
        }
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
            : _host.Transcript.Text();
        if (text.Length > 0) Clipboard.SetText(text);
        Say("Copied");
    }

    // ---- Triggers ----

    /// <summary>
    /// Compiles the trigger list as it now stands, and says what could not be compiled.
    ///
    /// Saying so matters more here than anywhere else in the app. A pattern that will not
    /// compile makes no sound, sends nothing and changes no text: it is indistinguishable
    /// from a trigger that is simply never matched, and a user who cannot see the dialog has
    /// no other way to tell the two apart.
    /// </summary>
    private void ReloadTriggers()
    {
        _triggers.Enabled = _settings.TriggersEnabled;
        _triggers.Load(_settings.Triggers);
    }

    private void ShowTriggers()
    {
        using var dialog = new TriggersForm(_settings.Triggers, _settings.TriggersEnabled);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.Triggers = [.. dialog.Triggers];
        _settings.TriggersEnabled = dialog.Active;
        _triggersActiveItem.Checked = _settings.TriggersEnabled;
        ReloadTriggers();
        TrySaveSettings();

        int on = _settings.Triggers.Count(trigger => trigger.Enabled);
        string saved = _settings.TriggersEnabled
            ? $"{_settings.Triggers.Count} triggers saved, {on} on"
            : $"{_settings.Triggers.Count} triggers saved. Triggers are off";
        Say(_triggers.Problems.Count == 0
            ? saved
            : $"{saved}. {_triggers.Problems.Count} could not be used: {string.Join(" ", _triggers.Problems)}");
    }

    private void ToggleTriggers()
    {
        _settings.TriggersEnabled = !_settings.TriggersEnabled;
        _triggersActiveItem.Checked = _settings.TriggersEnabled;
        _triggers.Enabled = _settings.TriggersEnabled;
        if (!_settings.TriggersEnabled) _triggerSounds?.StopAll();
        TrySaveSettings();

        Say(_settings.TriggersEnabled
            ? _settings.Triggers.Count == 0
                ? "Triggers on. There are none yet; Alt+Shift+T writes one"
                : $"Triggers on. {_settings.Triggers.Count(trigger => trigger.Enabled)} of "
                  + $"{_settings.Triggers.Count} are on"
            : "Triggers off");
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
            _settings.MudSounds = next.MudSounds;
            _settings.SoundDirectory = next.SoundDirectory;
            _settings.SoundVolume = next.SoundVolume;
            _settings.DownloadSounds = next.DownloadSounds;
            _mudSoundsItem.Checked = _settings.MudSounds;
            _downloadSoundsItem.Checked = _settings.DownloadSounds;
            // A trigger's own sounds are scaled by the same volume, so the board follows it.
            if (_triggerSounds is not null) _triggerSounds.Volume = _settings.SoundVolume;
            // The player holds the old folder, volume and download choice, so it is built
            // again from the new ones rather than asked to change its mind.
            _soundTimer.Stop();
            _sounds?.Dispose();
            _sounds = null;
            _soundDownloads?.Dispose();
            _soundDownloads = null;
            Say(resized ? "Settings saved and terminal resized" : "Settings saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not apply settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// The user asked for a telnet connection. The window cannot open another window, so the
    /// application does it, and a failure to connect is reported before an empty one appears.
    /// </summary>
    public event Action<string, int>? TelnetRequested;

    private void ConnectToHost()
    {
        using var dialog = new TelnetConnectForm(_settings.RecentTelnetHosts);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TelnetRequested?.Invoke(dialog.Host, dialog.Port);
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
            string latest = _host.Kind == TerminalSessionKind.Remote
                ? _latestResponse.Text(_host.Transcript)
                : _host.Transcript.Text();
            if (latest.Length > 0) Clipboard.SetText(latest);
            Say("Copied command output");
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
        // A dial in progress owns the window for the few seconds it lasts. Saying so is the
        // whole response: nothing is sent, and the line stays in the box to be sent, resent or
        // edited once there is an end to send it to.
        if (_connecting)
        {
            Say("Still connecting");
            return;
        }

        string text = _command.Text;
        BeginLatestResponse();
        bool completedLineHasText = _completionInput.HasText;
        if (_completionInput.FinishLine())
        {
            EndCompletionEcho();
            // A line the terminal's own editor completed can start a program exactly as a
            // typed one can, and BlindTerm no longer holds its text to tell. It gets the same
            // startup grace, so the first keys afterwards reach the program rather than the
            // shell that is busy starting it.
            if (completedLineHasText) _foregroundProgram.SubmittedUnknownLine();
            // The pending text and every character typed after completion already reached the
            // program. Only Return remains; resending the edit control would duplicate text.
            _host.SendLine(string.Empty);
            _command.Clear();
            return;
        }

        if (DialledOurselves(text))
        {
            if (_history.Count == 0 || _history[^1] != text) _history.Add(text);
            _historyIndex = _history.Count;
            _command.Clear();
            return;
        }

        // A line typed at a MUD is that MUD's to interpret. Rewriting "codex" into a command
        // line with flags on it would be nonsense there.
        string accessible = _host.Kind == TerminalSessionKind.Remote
            ? text
            : AccessibleAgentCommand.Adapt(text);
        _foregroundProgram.Submitted(text);
        _news.SuppressCommandEcho(accessible);
        _host.SendLine(accessible);
        if (text.Length > 0 && (_history.Count == 0 || _history[^1] != text)) _history.Add(text);
        _historyIndex = _history.Count;
        _command.Clear();
    }

    /// <summary>
    /// Takes "telnet host 4000" away from Windows' telnet.exe and dials it over BlindTerm's
    /// own telnet instead. See <see cref="TelnetCommand"/> for why: telnet.exe repaints a
    /// window rather than writing lines, so through a pseudo console every scroll reads as a
    /// whole new screenful of output and whatever went past between two repaints was never
    /// readable at all.
    ///
    /// The connection takes over this window, the way running a program from a prompt does,
    /// rather than opening another one. Only an idle shell prompt is answered this way:
    /// inside ssh, a Python prompt or a MUD, the line was typed at something else and is
    /// that program's to interpret.
    /// </summary>
    private bool DialledOurselves(string text)
    {
        if (!_host.CanConnectOver) return false;
        if (_foregroundProgram.Active) return false;
        if (TelnetCommand.Parse(text) is not var (host, port)) return false;

        // The command itself is written back the way a shell would echo it, and suppressed
        // for speech the same way, having just been read out as it was typed.
        _news.SuppressCommandEcho(text);
        _ = ConnectOver(text, host, port);
        return true;
    }

    /// <summary>How long to wait for a host to answer before giving up and saying so.</summary>
    private const int ConnectTimeoutSeconds = 20;

    /// <summary>
    /// Dials a host into this window and reports the outcome through the transcript, so that
    /// the answer to "what happened to my command" is there to read afterwards rather than
    /// having been spoken once and lost.
    /// </summary>
    private async Task ConnectOver(string typed, string host, int port)
    {
        string address = TelnetAddress.Format(host, port);
        _host.AppendExternal([typed, $"Connecting to {address}..."]);
        // Nothing here touches focus or the enabled state of the command box. Disabling the
        // focused control hands focus to the next one, which Windows announces, and taking it
        // back afterwards announces that too -- so dialling a host read as a trip through the
        // output pane and back for no reason. The dial is refused for the few seconds it runs
        // instead, and the caret stays exactly where it was typed.
        _connecting = true;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
            await _host.ConnectOverAsync(host, port, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException
                                   or IOException or ArgumentException or InvalidOperationException
                                   or ObjectDisposedException)
        {
            if (IsDisposed || Disposing) return;
            string reason = ex is OperationCanceledException
                ? $"{host} did not answer within {ConnectTimeoutSeconds} seconds."
                : ex.Message;
            // The shell is untouched and still at its prompt, so there is nothing to restore.
            _host.AppendExternal([$"Could not connect to {address}. {reason}"]);
            return;
        }
        finally
        {
            _connecting = false;
        }

        if (IsDisposed || Disposing) return;

        _settings.RememberTelnetHost(address);
        TrySaveSettings();

        // Kept so the window can say what it was showing again once the connection ends.
        _shellTitle = Text;
        Text = $"{address} — BlindTerm";
        _mud.Reset();
    }

    /// <summary>
    /// Whether a connection dialled from this window's prompt is still being opened. What is
    /// typed meanwhile belongs to neither end: the shell is about to be covered, and the host
    /// has not answered yet.
    /// </summary>
    private bool _connecting;

    /// <summary>The window title before a connection took the window over.</summary>
    private string? _shellTitle;

    /// <summary>A remembered address is a convenience; failing to write one is not an error.</summary>
    private void TrySaveSettings()
    {
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentOutOfRangeException)
        { }
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

        switch (AppShortcuts.ScreenTab(keyData, ScreenMode, _reviewing))
        {
            case AppShortcuts.ScreenTabTarget.Output:
            case AppShortcuts.ScreenTabTarget.Input:
                ToggleReview();
                return true;
        }

        switch (AppShortcuts.LineTab(
            keyData, ScreenMode, _command.Focused, _transcript.Focused))
        {
            case AppShortcuts.ScreenTabTarget.Output:
                FocusOutputAtLatestResponse();
                return true;
            case AppShortcuts.ScreenTabTarget.Input:
                FocusCommandLine();
                return true;
        }

        // Inline programs never enter alternate-screen mode. With their input field focused,
        // Ctrl+C, Ctrl+X, Ctrl+Z and other control commands belong to them until the shell's
        // completed-command marker says they exited. Output focus keeps native selection keys.
        // A handoff is itself the foreground app.
        bool foregroundLineProgram = !ScreenMode && _foregroundProgram.Active;
        bool commandFocused = _command.Focused;

        if (!CommandAccessibility.IsSecret(_command) &&
            AppShortcuts.ShouldSendCompletionTab(
                keyData, !ScreenMode && _host.IsRunning, commandFocused))
        {
            // A line the shell has not run yet is the last chance to ask an agent CLI for its
            // linear interface: once the shell's editor owns the line, Enter is all BlindTerm
            // sends and there is nothing left to rewrite. Doing it here is what lets Tab
            // complete at the prompt without completion and an accessible launch becoming a
            // choice between the two. A program reading the line, or a MUD, gets what was
            // typed -- that text is theirs to interpret.
            bool shellReadsTheLine = !foregroundLineProgram
                && _host.Kind != TerminalSessionKind.Remote;
            string pending = shellReadsTheLine
                ? AccessibleAgentCommand.Adapt(_command.Text)
                : _command.Text;

            // The unfinished line as it stands now is the prompt alone: everything typed is
            // still held here. That is the anchor the completed line is read back against.
            if (_completionInput.Active) _completionEcho.ExpectAnother();
            else _completionEcho.Expect(_live.Text);
            _host.Send(_completionInput.Begin(pending));
            // The terminal's rendered line is authoritative once it has completed the text.
            // The box is refilled from it when the redrawing stops, and mirrors typing after
            // that; Shift+Tab moves to the output.
            _command.Clear();
            _completionEchoTimer.Stop();
            _completionEchoTimer.Start();
            return true;
        }

        if (_completionInput.Active && !ScreenMode && commandFocused)
        {
            Keys key = keyData & Keys.KeyCode;
            // Every modified Tab remains focus navigation. In particular, Shift+Tab must move
            // from input to output instead of becoming a CLI-specific chord.
            if (key == Keys.Tab) return base.ProcessCmdKey(ref message, keyData);

            if (IsPasteChord(keyData))
            {
                if (Clipboard.ContainsText())
                    _host.Send(Encoding.UTF8.GetBytes(Clipboard.GetText()));
                return base.ProcessCmdKey(ref message, keyData);
            }

            if (key != Keys.Enter)
            {
                byte[]? streamed = KeyTranslator.Translate(
                    keyData, _host.Engine.ApplicationCursorKeys);
                if (streamed is not null)
                {
                    _host.Send(streamed);
                    return MirrorsNativeCommandEdit(keyData)
                        ? base.ProcessCmdKey(ref message, keyData)
                        : true;
                }
            }
        }

        // An empty command line is a remote control for whatever is running; one with text in
        // it is an edit box. Arrows drive a model picker in the first case and move the caret
        // through what has been typed in the second.
        if (AppShortcuts.ShouldPassControlChord(keyData, foregroundLineProgram, commandFocused)
            || AppShortcuts.ShouldPassNavigationKey(keyData, foregroundLineProgram, commandFocused,
                                                    _command.TextLength == 0))
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
        if (!ScreenMode && _command.Focused &&
            _completionInput.Character(e.KeyChar) is byte[] streamed)
        {
            _host.Send(streamed);
            // The native edit still receives the character for keyboard echo and local review.
            return;
        }

        if (LivePassthrough && !char.IsControl(e.KeyChar))
        {
            _host.Send(Encoding.UTF8.GetBytes(e.KeyChar.ToString()));
            // Do not mark the event handled. The proxy is a real edit control, so it updates its
            // local line and NVDA can apply the user's keyboard-echo preference normally.
            return;
        }
        base.OnKeyPress(e);
    }

    private static bool IsPasteChord(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        bool controlPaste = key == Keys.V && (keyData & Keys.Control) == Keys.Control
                            && (keyData & Keys.Alt) != Keys.Alt;
        bool insertPaste = key == Keys.Insert && (keyData & Keys.Shift) == Keys.Shift
                           && (keyData & (Keys.Control | Keys.Alt)) == Keys.None;
        return controlPaste || insertPaste;
    }

    /// <summary>Stops waiting for a completion that is no longer worth reading back.</summary>
    private void EndCompletionEcho()
    {
        _completionEchoTimer.Stop();
        _completionEcho.Cancel();
    }

    private static bool MirrorsNativeCommandEdit(Keys keyData)
        => (keyData & Keys.Alt) != Keys.Alt && (keyData & Keys.KeyCode) is
            Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Back or Keys.Delete;

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
        _soundTimer.Dispose();
        _sounds?.Dispose();
        _triggerSounds?.Dispose();
        _soundDownloads?.Dispose();
        _reviewFocusSpeechTimer.Dispose();
        _completionEchoTimer.Dispose();
        _updates.Dispose();
        _host.Dispose();
        base.OnFormClosed(e);
    }
}
