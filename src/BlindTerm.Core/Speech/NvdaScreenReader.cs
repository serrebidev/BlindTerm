using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace BlindTerm.Core.Speech;

/// <summary>
/// NVDA, through the controller client DLL it ships for exactly this purpose.
///
/// The DLL is LGPL 2.1 and is loaded unmodified, by architecture, from the native folder
/// beside the assembly. Its licence is in native/nvdaControllerClient-LICENSE.txt.
///
/// Deliberately not UI Automation. Notification events are the textbook answer and they are
/// the wrong one here: NVDA supports them well, JAWS does not, and a terminal that reads
/// beautifully under one reader and says nothing under the other is not the program this is
/// meant to be. Going through each reader's own API is what makes the two behave alike.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NvdaScreenReader : IScreenReader
{
    private const string Library = "nvdaControllerClient";

    /// <summary>NVDA 2024.1 and newer. Older versions answer 1717 and we fall back.</summary>
    private const int RpcUnknownInterface = 1717;

    /// <summary>Leaves symbol verbosity as the user has set it, which is the only polite choice.</summary>
    private const int SymbolLevelUnchanged = -1;

    private bool _ssmlUnsupported;

    static NvdaScreenReader()
    {
        // The DLL sits under native/<architecture>/, so that one build runs on x64 and arm64
        // without a per-architecture package.
        NativeLibrary.SetDllImportResolver(typeof(NvdaScreenReader).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library) return IntPtr.Zero;

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };

        string beside = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        string full = Path.Combine(beside, "native", architecture, Library + ".dll");

        if (File.Exists(full) && NativeLibrary.TryLoad(full, out IntPtr handle)) return handle;

        // Fall back to the ordinary search order, so a copy alongside the exe also works.
        return NativeLibrary.TryLoad(Library + ".dll", out handle) ? handle : IntPtr.Zero;
    }

    public string Name => "NVDA";

    public bool IsRunning
    {
        get
        {
            try { return nvdaController_testIfRunning() == 0; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (SEHException) { return false; }
        }
    }

    public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
    {
        if (string.IsNullOrEmpty(text)) return true;

        try
        {
            // speakSsml is the only route that carries a priority, which is what keeps a bell
            // from waiting behind a screenful of output. It arrived in NVDA 2024.1; older
            // versions report an unknown interface once and are then spoken to the old way.
            if (!_ssmlUnsupported)
            {
                int status = nvdaController_speakSsml(
                    Ssml(text), SymbolLevelUnchanged, (int)priority, asynchronous: true);

                if (status == 0) return true;
                if (status != RpcUnknownInterface) return false;
                _ssmlUnsupported = true;
            }

            // Without priorities the best approximation of "say this now" is to stop first.
            if (priority == SpeechPriority.Now) nvdaController_cancelSpeech();
            return nvdaController_speakText(text) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (SEHException) { return false; }
    }

    public bool Braille(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        try { return nvdaController_brailleMessage(text) == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (SEHException) { return false; }
    }

    public bool Silence()
    {
        try { return nvdaController_cancelSpeech() == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (SEHException) { return false; }
    }

    /// <summary>
    /// Wraps text as SSML. Terminal output is arbitrary bytes, and an unescaped &amp; or &lt;
    /// would make the whole utterance invalid and silently say nothing at all.
    /// </summary>
    internal static string Ssml(string text)
    {
        var builder = new StringBuilder(text.Length + 32);
        builder.Append("<speak>");
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&apos;"); break;
                default:
                    // Characters XML forbids outright. A terminal produces them; dropping them
                    // is better than losing the utterance they appear in.
                    if (c is '\t' or '\n' or '\r' || c >= 0x20) builder.Append(c);
                    break;
            }
        }
        builder.Append("</speak>");
        return builder.ToString();
    }

    [DllImport(Library, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_testIfRunning();

    [DllImport(Library, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_speakText(string text);

    [DllImport(Library, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_cancelSpeech();

    [DllImport(Library, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_brailleMessage(string message);

    [DllImport(Library, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_speakSsml(
        string ssml, int symbolLevel, int priority, [MarshalAs(UnmanagedType.I1)] bool asynchronous);
}
