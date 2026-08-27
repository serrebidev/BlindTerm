using System.Runtime.Versioning;

namespace BlindTerm.App;

/// <summary>
/// A native edit control containing only the line under the terminal cursor.
///
/// It is not a copy of the terminal screen. A copied screen gives NVDA a second, drifting
/// caret and makes Up/Down read padding or nano's title bar. Vertical terminal navigation is
/// intercepted by the form; this control exists only so ordinary typing uses NVDA/JAWS's own
/// keyboard-echo settings.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KeyboardEchoProxy : TextBox
{
    private bool _settingRemoteText;

    public KeyboardEchoProxy()
    {
        Multiline = false;
        WordWrap = false;
        HideSelection = false;
        ReadOnly = false;
        AccessibleRole = AccessibleRole.Text;
        Text = "\u200B";
        SelectionStart = 1;
        TabStop = false;
    }

    /// <summary>Updates the proxy to the remote cursor line and column.</summary>
    public void SetLine(string line, int column)
    {
        line = line.TrimEnd();
        int caret = Math.Clamp(column, 0, line.Length);
        if (!string.Equals(Text, line, StringComparison.Ordinal))
        {
            _settingRemoteText = true;
            try { Text = line; }
            finally { _settingRemoteText = false; }
        }
        SelectionStart = caret;
        SelectionLength = 0;
    }

    public bool SettingRemoteText => _settingRemoteText;

    /// <summary>Keys that must remain local to the terminal rather than edit this proxy.</summary>
    public static bool IsTerminalNavigation(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        bool alt = (keyData & Keys.Alt) == Keys.Alt;
        return !alt && key is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown
            or Keys.Home or Keys.End;
    }

    public static bool IsNativeEditKey(Keys keyData) => !IsTerminalNavigation(keyData);
}
