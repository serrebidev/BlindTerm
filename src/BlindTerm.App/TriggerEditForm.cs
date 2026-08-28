using System.Runtime.Versioning;
using BlindTerm.Core.Sound;
using BlindTerm.Core.Triggers;

namespace BlindTerm.App;

/// <summary>
/// One trigger, written out in full.
///
/// Every field is a labelled control in a two-column grid, which is the arrangement both
/// readers move through with Tab and read the label of without being asked. Nothing here is
/// conveyed by layout, colour or position, and every control carries a description saying
/// what it is for rather than only what it is called.
///
/// The Test box at the bottom is the part that matters most. A pattern is a guess until
/// something has been matched against it, and looking at a wildcard to see whether it lines
/// up with a line of MUD output is exactly the check that is not available here. Typing the
/// line in and being told "it matches, and the first star is Fred" replaces it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TriggerEditForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _pattern = new();
    private readonly ComboBox _match = new();
    private readonly CheckBox _caseSensitive = new();
    private readonly ComboBox _where = new();
    private readonly CheckBox _enabled = new();

    private readonly TextBox _speak = new();
    private readonly CheckBox _speakNow = new();
    private readonly CheckBox _silence = new();
    private readonly TextBox _sound = new();
    private readonly Button _browse = new();
    private readonly CheckBox _beep = new();
    private readonly TextBox _send = new();
    private readonly CheckBox _stop = new();
    private readonly NumericUpDown _repeatAfter = new();

    private readonly TextBox _test = new();
    private readonly Button _tryIt = new();

    /// <summary>The trigger as edited. Only meaningful once the dialog returned OK.</summary>
    public Trigger Trigger { get; private set; }

    public TriggerEditForm(Trigger trigger, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        Trigger = trigger.Copy();

        Text = isNew ? "New trigger" : $"Edit trigger — {Trigger.DisplayName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        BuildWhatToWatchFor(fields);
        BuildWhatToDo(fields);
        BuildTest(fields);
        BuildButtons(fields);

        Controls.Add(fields);
        ActiveControl = _pattern;
    }

    // ---- What to watch for ----

    private void BuildWhatToWatchFor(TableLayoutPanel fields)
    {
        _name.Text = Trigger.Name;
        _name.Width = 380;
        AddField(fields, "&Name", _name,
            "What this trigger is called in the list. Leave it blank to be called by its pattern.");

        _pattern.Text = Trigger.Pattern;
        _pattern.Width = 380;
        AddField(fields, "&Watch for", _pattern,
            "The text to look for in each line of output.");

        _match.DropDownStyle = ComboBoxStyle.DropDownList;
        _match.Width = 260;
        _match.Items.AddRange([
            "Contains the text",
            "Wildcard: star is any text, question mark is one character",
            "Regular expression",
        ]);
        _match.SelectedIndex = (int)Trigger.Match;
        AddField(fields, "&How to read it", _match,
            "Contains looks for the text anywhere in the line. A wildcard has to match the "
            + "whole line, so put a star at each end to match part of one; what each star "
            + "stood for comes back as dollar one, dollar two and so on. A regular expression "
            + "is matched as written, and its groups come back the same way.");

        _caseSensitive.Text = "Capitals have to match";
        _caseSensitive.AutoSize = true;
        _caseSensitive.Checked = Trigger.CaseSensitive;
        AddField(fields, "&Capitals", _caseSensitive,
            "Whether upper and lower case have to line up. Off means they do not.");

        _where.DropDownStyle = ComboBoxStyle.DropDownList;
        _where.Width = 260;
        _where.Items.AddRange(["Anywhere", "Only in a shell", "Only on a MUD or telnet host"]);
        _where.SelectedIndex = (int)Trigger.Where;
        AddField(fields, "Where it app&lies", _where,
            "Which kind of session this watches. A trigger written for a MUD firing on a "
            + "build log is noise, and the other way round.");

        _enabled.Text = "This trigger is on";
        _enabled.AutoSize = true;
        _enabled.Checked = Trigger.Enabled;
        AddField(fields, "Turned &on", _enabled,
            "Whether this trigger runs. Turning it off keeps it in the list.");
    }

    // ---- What to do about it ----

    private void BuildWhatToDo(TableLayoutPanel fields)
    {
        _speak.Text = Trigger.Speak;
        _speak.Width = 380;
        AddField(fields, "&Say this", _speak,
            "What to read out when the line matches. Leave it blank to say nothing extra. "
            + "Dollar zero is the whole line, and dollar one onwards are what the wildcards "
            + "stood for, so a pattern of star arrives can say dollar one is here.");

        _speakNow.Text = "Say it at once, ahead of everything waiting";
        _speakNow.AutoSize = true;
        _speakNow.Checked = Trigger.SpeakNow;
        AddField(fields, "&Urgent", _speakNow,
            "Whether this jumps the queue. Output is read in batches, so without this a "
            + "warning can be sitting behind thirty lines that do not matter.");

        _silence.Text = "Do not read the matching line out";
        _silence.AutoSize = true;
        _silence.Checked = Trigger.Silence;
        AddField(fields, "&Quiet", _silence,
            "Whether the line itself is kept out of the speech. It stays in the transcript "
            + "and can still be read back; it is only not read out as it arrives.");

        _sound.Text = Trigger.Sound;
        _sound.Width = 300;
        _sound.AccessibleName = "Play a sound";
        _sound.AccessibleDescription =
            "A sound file to play when the line matches. Leave it blank for no sound.";
        _browse.Text = "&Browse...";
        _browse.AutoSize = true;
        _browse.AccessibleName = "Browse for a sound file";
        _browse.Click += (_, _) => BrowseForSound();
        var soundRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
        };
        soundRow.Controls.Add(_sound);
        soundRow.Controls.Add(_browse);
        AddField(fields, "Play a soun&d", soundRow,
            "A sound file to play when the line matches. Leave it blank for no sound.");

        _beep.Text = "Play the system alert sound";
        _beep.AutoSize = true;
        _beep.Checked = Trigger.Beep;
        AddField(fields, "Ale&rt", _beep,
            "Whether to play the alert sound Windows already has. This needs no file, which "
            + "makes it the quickest way to mark a line worth noticing.");

        _send.Text = Trigger.Send;
        _send.Width = 380;
        AddField(fields, "S&end this", _send,
            "A line to send back when the trigger fires, as though it had been typed. The "
            + "same dollar one substitutions apply. Leave it blank to send nothing.");

        _stop.Text = "Skip the triggers listed after this one";
        _stop.AutoSize = true;
        _stop.Checked = Trigger.StopProcessing;
        AddField(fields, "Stop &after this", _stop,
            "Whether a line this matched is kept from the triggers below it. Put a trigger "
            + "for your own name above one that silences a channel, and the channel stays "
            + "quiet except when it is talking to you.");

        _repeatAfter.Minimum = 0;
        _repeatAfter.Maximum = Core.Triggers.Trigger.MaximumRepeatAfterMilliseconds;
        _repeatAfter.Increment = 250;
        _repeatAfter.Value = Math.Clamp(Trigger.RepeatAfterMilliseconds, 0,
                                        Core.Triggers.Trigger.MaximumRepeatAfterMilliseconds);
        _repeatAfter.Width = 120;
        AddField(fields, "Wait before &firing again", _repeatAfter,
            "The shortest gap between two firings, in milliseconds. Zero fires on every "
            + "matching line. A few thousand is how an alarm stays an alarm rather than "
            + "becoming a drone.");
    }

    // ---- Trying it out ----

    private void BuildTest(TableLayoutPanel fields)
    {
        _test.Width = 300;
        _test.AccessibleName = "Try a line";
        _test.AccessibleDescription =
            "Type a line the way it would arrive and press Test. It says whether the pattern "
            + "matches, what each wildcard stood for, and exactly what would be said and sent.";
        _tryIt.Text = "&Test";
        _tryIt.AutoSize = true;
        _tryIt.AccessibleName = "Test the pattern against that line";
        _tryIt.Click += (_, _) => RunTest();
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
        };
        row.Controls.Add(_test);
        row.Controls.Add(_tryIt);
        AddField(fields, "Tr&y a line", row,
            "Type a line the way it would arrive and press Test. It says whether the pattern "
            + "matches, what each wildcard stood for, and exactly what would be said and sent.");
    }

    private void BuildButtons(TableLayoutPanel fields)
    {
        var save = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AccessibleName = "Save this trigger",
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AccessibleName = "Cancel and leave this trigger as it was",
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        fields.Controls.Add(buttons, 1, fields.RowCount);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel panel, string label, Control control, string description)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // A real Label immediately before the control is what gives both readers the name to
        // announce, and its ampersand puts the keyboard on the control it names.
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 12, 0),
            AccessibleName = label.Replace("&", string.Empty),
        }, 0, row);
        control.AccessibleDescription = description;
        if (string.IsNullOrEmpty(control.AccessibleName))
            control.AccessibleName = label.Replace("&", string.Empty);
        panel.Controls.Add(control, 1, row);
    }

    private void BrowseForSound()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Choose a sound to play",
            Filter = "Sounds|*.wav;*.mp3;*.mid;*.midi;*.wma;*.au;*.aif;*.aiff|All files|*.*",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(_sound.Text))
        {
            try { picker.InitialDirectory = Path.GetDirectoryName(_sound.Text) ?? string.Empty; }
            catch (ArgumentException) { }
        }
        if (picker.ShowDialog(this) == DialogResult.OK) _sound.Text = picker.FileName;
    }

    /// <summary>
    /// Matches the pattern against a line typed by hand and says, in a dialog, exactly what
    /// happened.
    ///
    /// A dialog rather than speech: the answer is several facts, and several facts are
    /// something to read at your own pace with the arrow keys rather than have recited once.
    /// </summary>
    private void RunTest()
    {
        Trigger candidate = Read();
        if (!TriggerPattern.TryCompile(candidate.Pattern, candidate.Match, candidate.CaseSensitive,
                                       out TriggerPattern? pattern, out string? problem))
        {
            Tell(problem, "That pattern cannot be used", MessageBoxIcon.Warning);
            return;
        }

        string line = _test.Text;
        if (line.Length == 0)
        {
            Tell("Type a line the way it would arrive from the terminal, then press Test.",
                 "Nothing to test", MessageBoxIcon.Information);
            return;
        }

        if (pattern.Match(line) is not { } capture)
        {
            Tell($"That line does not match.{Environment.NewLine}{Environment.NewLine}"
                 + WildcardHint(candidate), "No match", MessageBoxIcon.Information);
            return;
        }

        var report = new List<string> { "That line matches." };
        for (int i = 0; i < capture.Groups.Count && i < 9; i++)
            report.Add($"Dollar {i + 1} is: {Describe(capture.Groups[i])}");
        if (capture.Groups.Count == 0) report.Add("The pattern has no wildcards, so there is nothing to fill in.");

        if (candidate.Speak.Length > 0) report.Add($"It would say: {capture.Expand(candidate.Speak)}");
        if (candidate.Silence) report.Add("The line itself would not be read out.");
        if (candidate.Sound.Length > 0)
            report.Add(File.Exists(candidate.Sound)
                ? $"It would play: {candidate.Sound}"
                : $"It would try to play {candidate.Sound}, which is not on this machine.");
        if (candidate.Beep) report.Add("It would play the system alert sound.");
        if (candidate.Send.Length > 0) report.Add($"It would send: {capture.Expand(candidate.Send)}");
        if (candidate.StopProcessing) report.Add("Triggers listed after this one would be skipped.");
        if (!candidate.DoesSomething) report.Add("Nothing would happen: no action is set on this trigger.");

        Tell(string.Join(Environment.NewLine, report), "It matches", MessageBoxIcon.Information);
    }

    /// <summary>The single mistake everyone makes with wildcards, said before it is made.</summary>
    private static string WildcardHint(Trigger candidate)
        => candidate.Match == TriggerMatch.Wildcard
            && !(candidate.Pattern.StartsWith('*') && candidate.Pattern.EndsWith('*'))
            ? "A wildcard has to match the whole line. To match part of one, put a star at "
              + "each end of the pattern."
            : "Check the spelling, and whether Capitals have to match is set the way you meant.";

    private static string Describe(string value)
        => value.Length == 0 ? "nothing" : value;

    private void Tell(string text, string caption, MessageBoxIcon icon)
        => MessageBox.Show(this, text, caption, MessageBoxButtons.OK, icon);

    /// <summary>The trigger as the controls now stand.</summary>
    private Trigger Read()
    {
        Trigger edited = Trigger.Copy();
        edited.Name = _name.Text.Trim();
        edited.Pattern = _pattern.Text;
        edited.Match = (TriggerMatch)Math.Max(0, _match.SelectedIndex);
        edited.CaseSensitive = _caseSensitive.Checked;
        edited.Where = (TriggerWhere)Math.Max(0, _where.SelectedIndex);
        edited.Enabled = _enabled.Checked;
        edited.Speak = _speak.Text.Trim();
        edited.SpeakNow = _speakNow.Checked;
        edited.Silence = _silence.Checked;
        edited.Sound = _sound.Text.Trim();
        edited.Beep = _beep.Checked;
        edited.Send = _send.Text.Trim();
        edited.StopProcessing = _stop.Checked;
        edited.RepeatAfterMilliseconds = (int)_repeatAfter.Value;
        edited.Clamp();
        return edited;
    }

    /// <summary>
    /// Refuses to save a trigger that cannot work, and says which control to go to.
    ///
    /// Everything refused here is something that would otherwise fail silently later: a
    /// pattern that will not compile never matches, and a trigger with no action does nothing
    /// however well it matches. Neither has any sound, so neither can be noticed.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            base.OnFormClosing(e);
            return;
        }

        Trigger edited = Read();

        if (!TriggerPattern.TryCompile(edited.Pattern, edited.Match, edited.CaseSensitive,
                                       out _, out string? problem))
        {
            Tell(problem, "That pattern cannot be used", MessageBoxIcon.Error);
            e.Cancel = true;
            ActiveControl = _pattern;
            return;
        }

        if (!edited.DoesSomething)
        {
            Tell("This trigger has nothing to do. Give it something to say, a sound, an "
                 + "alert, a line to send, or turn on Quiet so it keeps the line out of the "
                 + "speech.", "Nothing would happen", MessageBoxIcon.Warning);
            e.Cancel = true;
            ActiveControl = _speak;
            return;
        }

        if (edited.Sound.Length > 0 && !File.Exists(edited.Sound))
        {
            var answer = MessageBox.Show(this,
                $"There is no file at {edited.Sound}, so nothing would be heard. Save it anyway?",
                "That sound is not there", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                ActiveControl = _sound;
                return;
            }
        }
        else if (edited.Sound.Length > 0
                 && !SoundLibrary.PlayableExtensions.Contains(Path.GetExtension(edited.Sound)))
        {
            Tell("That is not a kind of file this can play. Choose a WAV, MP3 or MIDI file.",
                 "Not a sound", MessageBoxIcon.Error);
            e.Cancel = true;
            ActiveControl = _sound;
            return;
        }

        Trigger = edited;
        base.OnFormClosing(e);
    }
}
