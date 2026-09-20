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

    /// <summary>
    /// The bound COM object, or null when there is none.
    ///
    /// Every use of it is under <see cref="_gate"/> and through a local copy, because this is
    /// reached from two threads at once: the window thread saying something the user asked to
    /// hear, and the announcer's own timer saying a batch of streamed output. Binding,
    /// calling and dropping are three separate reads of this field otherwise, and a JAWS that
    /// restarts in the middle of them leaves one thread calling a method on nothing.
    /// </summary>
    private object? _api;
    private readonly Lock _gate = new();

    public string Name => "JAWS";

    public bool IsRunning
    {
        get
        {
            // The COM object is registered by the installer and only answers while JAWS runs,
            // so a successful bind is the test.
            lock (_gate)
            {
                if (_api is not null) return true;
                return TryBind();
            }
        }
    }

    /// <summary>
    /// Binds the automation object, or says there is none. Called under <see cref="_gate"/>.
    ///
    /// The catch list is everything a COM activation can raise on a machine where JAWS is
    /// half-installed, the wrong bitness, or being upgraded while this runs. None of those is
    /// a reason for a terminal to stop working, and "bound late by ProgID" is only worth
    /// anything if failing to bind is quiet.
    /// </summary>
    private bool TryBind()
    {
        try
        {
            Type? type = Type.GetTypeFromProgID(ProgId, throwOnError: false);
            if (type is null) return false;

            _api = Activator.CreateInstance(type);
            return _api is not null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException
                                   or NotSupportedException or TypeLoadException
                                   or MissingMethodException or MemberAccessException
                                   or FileNotFoundException or FileLoadException
                                   or BadImageFormatException or InvalidComObjectException)
        {
            _api = null;
            return false;
        }
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
        // The local copy is the whole point: whatever happens to _api while this call is in
        // flight, this thread is holding something and running on it rather than reading the
        // field a second time and finding it gone.
        object? api;
        lock (_gate)
        {
            if (_api is null && !TryBind()) return false;
            api = _api;
        }
        if (api is null) return false;

        try
        {
            api.GetType().InvokeMember(
                method,
                BindingFlags.InvokeMethod,
                binder: null,
                target: api,
                args: arguments);
            return true;
        }
        catch (COMException)
        {
            // JAWS has gone since we bound. Drop it, so the next call re-probes rather than
            // failing forever against a dead object.
            Forget(api);
            return false;
        }
        catch (TargetInvocationException) { Forget(api); return false; }
        catch (MissingMethodException) { return false; }
        // Anything else a dying COM object can raise. This is called from the announcer's
        // timer, where an unhandled exception is not a lost utterance but a lost process.
        catch (Exception) { Forget(api); return false; }
    }

    /// <summary>
    /// Drops the bound object, but only if it is still the one that failed -- a JAWS that came
    /// back and re-bound while this call was out must not have the new object thrown away.
    /// </summary>
    private void Forget(object failed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_api, failed)) _api = null;
        }
    }
}
