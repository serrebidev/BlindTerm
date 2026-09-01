using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Holds Windows to a one-millisecond timer while output is waiting to be spoken.
///
/// Windows' default timer resolution is 15.6 ms, and a waitable timer does not fire early: a
/// fifty-millisecond wait measures sixty-two. That overshoot was a quarter of the whole delay
/// between a program printing a line and a screen reader saying it, and it was invisible --
/// the number in the source said fifty.
///
/// Raised only while a flush is actually pending, which on an idle terminal is never and
/// during output is a few milliseconds at a time. A process that held it permanently would
/// keep the scheduler awake and cost battery for the sake of a terminal saying nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SpeechTimerResolution
{
    private const uint Milliseconds = 1;

    private static readonly Lock Gate = new();
    private static int _holders;

    /// <summary>Whether Windows accepted the request. False on a system that refuses it.</summary>
    private static bool _raised;

    public static void Acquire()
    {
        lock (Gate)
        {
            if (_holders++ > 0) return;
            _raised = TryBeginPeriod();
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (_holders == 0) return;
            if (--_holders > 0) return;
            if (_raised) TryEndPeriod();
            _raised = false;
        }
    }

    /// <summary>How many callers are holding the raised resolution. For tests.</summary>
    internal static int Holders { get { lock (Gate) return _holders; } }

    // winmm is present on every Windows install, but a failure here must never stop speech:
    // the only consequence of not raising the resolution is that the wait is a little longer.
    private static bool TryBeginPeriod()
    {
        try { return timeBeginPeriod(Milliseconds) == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static void TryEndPeriod()
    {
        try { timeEndPeriod(Milliseconds); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);
}
