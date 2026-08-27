using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BlindTerm.Core.Speech;

/// <summary>
/// JAWS, through its COM automation object.
///
/// There is no controller DLL to link against and no notification API worth using -- JAWS
/// turned UI Automation notifications off by default because applications abused them -- so
/// the COM object is the route that actually works, and it is the one long-standing accessible
/// Windows applications have always used.
///
/// Bound late, by ProgID, so that a machine without JAWS needs no reference and pays nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class JawsScreenReader : IScreenReader
{
    private const string ProgId = "FreedomSci.JawsApi";

    private object? _api;

    public string Name => "JAWS";

    public bool IsRunning
    {
        get
        {
            // The COM object is registered by the installer and only answers while JAWS runs,
            // so a successful bind is the test.
            if (_api is not null) return true;
            return TryBind();
        }
    }

    private bool TryBind()
    {
        try
        {
            Type? type = Type.GetTypeFromProgID(ProgId, throwOnError: false);
            if (type is null) return false;

            _api = Activator.CreateInstance(type);
            return _api is not null;
        }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (TypeLoadException) { return false; }
    }

    /// <summary>
    /// JAWS has one useful verb, SayString, and a flag for whether to interrupt. Its three
    /// levels of urgency therefore collapse to two: anything at or above <see
    /// cref="SpeechPriority.Next"/> stops what is being said, everything else queues.
    /// </summary>
    public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
    {
        if (string.IsNullOrEmpty(text)) return true;
        return Invoke("SayString", text, priority >= SpeechPriority.Next);
    }

    /// <summary>
    /// No braille-only API is exposed. Braille follows the caret in the transcript control
    /// instead, which is the main reason that control has to be a real edit control rather
    /// than something custom-drawn.
    /// </summary>
    public bool Braille(string text) => false;

    public bool Silence() => Invoke("StopSpeech");

    private bool Invoke(string method, params object[] arguments)
    {
        if (_api is null && !TryBind()) return false;

        try
        {
            _api!.GetType().InvokeMember(
                method,
                BindingFlags.InvokeMethod,
                binder: null,
                target: _api,
                args: arguments);
            return true;
        }
        catch (COMException)
        {
            // JAWS has gone since we bound. Drop it, so the next call re-probes rather than
            // failing forever against a dead object.
            _api = null;
            return false;
        }
        catch (MissingMethodException) { return false; }
        catch (TargetInvocationException) { _api = null; return false; }
    }
}
