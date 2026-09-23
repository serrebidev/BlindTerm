using System.Reflection;
using BlindTerm.App;
using BlindTerm.Core;
using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

/// <summary>
/// The two dialogs, built but never shown.
///
/// A window nothing can click is a window nothing checks, and the things that make these
/// usable without sight -- an accessible name on every control, a label beside it, a mnemonic
/// on every button -- are exactly the things that go missing without anyone noticing.
/// </summary>
public class MudBrowserFormTests
{
    [Fact]
    public void TheConnectDialogOffersToBrowseAndToEncrypt()
        => OnAWindowThread(() =>
        {
            using var dialog = new TelnetConnectForm(new AppSettings());
            List<Control> controls = [.. Walk(dialog)];

            Assert.Contains(controls.OfType<Button>(), button => button.Text == "&Browse for MUDs...");
            Assert.Contains(controls.OfType<CheckBox>(),
                box => box.Text.Contains("TLS", StringComparison.Ordinal));
        });

    [Fact]
    public void ARememberedEncryptedAddressComesBackEncrypted()
        => OnAWindowThread(() =>
        {
            var settings = new AppSettings();
            settings.RememberTelnetHost("ssl://coremud.org:4022");

            using var dialog = new TelnetConnectForm(settings);
            List<Control> controls = [.. Walk(dialog)];
            var host = controls.OfType<ComboBox>().Single();
            var secure = controls.OfType<CheckBox>().Single();
            var port = controls.OfType<NumericUpDown>().Single();

            // The scheme is not left in the host box: it is the checkbox, and typing it back
            // into the field would make the host name wrong.
            Assert.Equal("coremud.org", host.Text);
            Assert.Equal(4022, (int)port.Value);
            Assert.True(secure.Checked);
        });

    [Fact]
    public void APastedSchemeIsUnderstoodInTheHostBox()
        => OnAWindowThread(() =>
        {
            using var dialog = new TelnetConnectForm(new AppSettings());
            var host = Walk(dialog).OfType<ComboBox>().Single();
            host.Text = "ssl://coremud.org:4022";

            // What the dialog does with the fields when Connect is pressed.
            typeof(Form).GetProperty("DialogResult")!.SetValue(dialog, DialogResult.OK);
            typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(dialog, [new FormClosingEventArgs(CloseReason.None, false)]);

            Assert.Equal("coremud.org", dialog.Target.Host);
            Assert.Equal(4022, dialog.Target.Port);
            Assert.True(dialog.Target.UseTls);
        });

    [Fact]
    public void TheBrowserIsBuiltFromControlsAScreenReaderCanName()
        => OnAWindowThread(() =>
        {
            using var browser = new MudBrowserForm(new AppSettings());
            List<Control> controls = [.. Walk(browser)];

            // Nothing is fetched until the window is shown, so this builds without a key.
            Assert.Single(controls.OfType<ListBox>());
            Assert.Equal(4, controls.OfType<ComboBox>().Count());

            foreach (Control control in controls)
            {
                if (control is Label or Panel or Form) continue;
                Assert.False(string.IsNullOrWhiteSpace(control.AccessibleName),
                    $"{control.GetType().Name} '{control.Text}' has no accessible name");
            }

            // Every button is reachable without a mouse.
            foreach (Button button in controls.OfType<Button>())
                Assert.True(button.Text.Contains('&') || button.DialogResult != DialogResult.None,
                    $"button '{button.Text}' has no mnemonic and no dialog result");
        });

    [Fact]
    public void TheBrowserDoesNotTakeEnterAwayFromWhateverHasFocus()
        => OnAWindowThread(() =>
        {
            using var browser = new MudBrowserForm(new AppSettings());

            // A default button would make Enter close the window from the results list and
            // from the search box, which are the two places it has to mean something else.
            Assert.Null(browser.AcceptButton);
            Assert.NotNull(browser.CancelButton);
        });

    /// <summary>
    /// The browser's status reaches the reader, even though opening the dialog deactivated the
    /// terminal it was opened from. Every outcome a fetch can have -- it started, what came
    /// back, it timed out, the directory could not be read -- was written to a label and
    /// nowhere else, and a label is a place words sit for somebody to look at. A blind user
    /// pressed Show MUDs and had no way to tell a slow fetch from a failed one from nothing
    /// matching.
    /// </summary>
    [Fact]
    public void TheBrowserSpeaksItsStatusAlthoughTheTerminalIsNotInFront()
        => OnAWindowThread(() =>
        {
            var reader = new RecordingReader();
            using var announcer = new Announcer(reader);
            // What opening a modal dialog does to the terminal it was opened from.
            announcer.Attended = false;

            using var browser = new MudBrowserForm(new AppSettings(), null, announcer);
            typeof(MudBrowserForm)
                .GetMethod("Announce", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(browser, ["No MUDs matched. Try a wider genre."]);

            Assert.Equal(["No MUDs matched. Try a wider genre."], reader.Spoken);
            // And to braille, which is the only channel a braille user has for this: braille
            // follows the caret, and a caret in the terminal does not move for a fetch that
            // timed out or a search that matched nothing.
            Assert.Equal(reader.Spoken, reader.Brailled);
        });

    private sealed class RecordingReader : IScreenReader
    {
        public List<string> Spoken { get; } = [];
        public List<string> Brailled { get; } = [];

        public string Name => "recording";
        public bool IsRunning => true;

        public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            Spoken.Add(text);
            return true;
        }

        public bool Braille(string text)
        {
            Brailled.Add(text);
            return true;
        }

        public bool Silence() => true;
    }

    private static IEnumerable<Control> Walk(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Walk(child)) yield return descendant;
        }
    }

    private static void OnAWindowThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
