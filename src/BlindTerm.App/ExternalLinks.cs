using System.Diagnostics;

namespace BlindTerm.App;

/// <summary>Opens public project links through the user's chosen web browser.</summary>
internal static class ExternalLinks
{
    public static void Open(IWin32Window owner, string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                   or InvalidOperationException)
        {
            MessageBox.Show(owner, "Could not open a browser. The address is " + url,
                "BlindTerm", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
