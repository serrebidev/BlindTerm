namespace BlindTerm.Core.Net;

/// <summary>How a walk ended.</summary>
public enum MudWalkOutcome
{
    /// <summary>Every step was sent and none was answered with a refusal.</summary>
    Arrived,

    /// <summary>The MUD refused a step, and the rest of the route was abandoned.</summary>
    Blocked,

    /// <summary>The walk was stopped -- by the player, or by the connection ending.</summary>
    Stopped,
}

/// <summary>What a walk did, in the numbers a player would ask about.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="StepsSent">Steps actually sent, including a blocked one.</param>
/// <param name="StepsAbandoned">Steps never sent because the walk ended early.</param>
public sealed record MudWalkResult(MudWalkOutcome Outcome, int StepsSent, int StepsAbandoned);

/// <summary>
/// Walks a <see cref="MudRoute"/>, sending its steps through whatever the window gives it.
///
/// A route written with one dot is sent as fast as the connection carries it: it is
/// already on its way by the time anyone could stop it, and stopping is not offered. A
/// route written with two dots is walked carefully -- one step, a wait to arrive, then the
/// next -- and gives up where the MUD refuses a step rather than firing the rest of the
/// route into a wall.
///
/// The walker knows nothing about sockets or screens. The window hands it three things:
/// how to send a step, how to wait for the room to change (with its own idea of how long
/// is long enough on a MUD that reports nothing), and how to tell whether what arrived
/// since the last step was a refusal.
/// </summary>
public sealed class MudWalker
{
    private readonly Func<string, Task> _send;
    private readonly Func<CancellationToken, Task> _waitForArrival;
    private readonly Func<bool> _isBlocked;

    /// <param name="send">Sends one step, as a line of its own.</param>
    /// <param name="waitForArrival">
    /// Waits until the player has arrived -- a new room reported, or new text printed --
    /// or until the window's own patience runs out. Called only between steps of a careful
    /// walk, never after the last one.
    /// </param>
    /// <param name="isBlocked">
    /// Whether what arrived since the last step says the step was refused. Asked after
    /// each wait of a careful walk.
    /// </param>
    public MudWalker(Func<string, Task> send, Func<CancellationToken, Task> waitForArrival,
        Func<bool> isBlocked)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _waitForArrival = waitForArrival ?? throw new ArgumentNullException(nameof(waitForArrival));
        _isBlocked = isBlocked ?? throw new ArgumentNullException(nameof(isBlocked));
    }

    /// <summary>Walks the whole route, or as much of it as the MUD allows.</summary>
    public async Task<MudWalkResult> WalkAsync(MudRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        IReadOnlyList<string> steps = route.Steps;
        int sent = 0;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _send(steps[i]).ConfigureAwait(false);
                sent++;

                if (!route.Careful || i == steps.Count - 1) continue;

                await _waitForArrival(cancellationToken).ConfigureAwait(false);
                if (_isBlocked())
                    return new MudWalkResult(MudWalkOutcome.Blocked, sent, steps.Count - sent);
            }
        }
        catch (OperationCanceledException)
        {
            return new MudWalkResult(MudWalkOutcome.Stopped, sent, steps.Count - sent);
        }
        return new MudWalkResult(MudWalkOutcome.Arrived, sent, 0);
    }
}

/// <summary>
/// The lines a MUD answers a step it refuses with.
///
/// These are matched against what arrived while a careful walk waited, and only there:
/// read as a general rule about output they would be wrong often enough to matter -- a
/// room description can mention a closed door without the player having walked into it.
/// In the moment after a step was sent, though, a line saying the door is closed is the
/// MUD answering the step, and carrying on would fire the rest of the route into the wall.
/// </summary>
public static class MudWalkBlock
{
    private static readonly string[] Refusals =
    [
        "can't go that way",
        "can't go in that direction",
        "cannot go that way",
        "cannot go in that direction",
        "there is no exit",
        "there's no exit",
        "door is closed",
    ];

    /// <summary>Whether one line of output is a MUD refusing a step.</summary>
    public static bool IsBlockedLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        // MUDs print both apostrophes. Normalising the curly one costs nothing and is the
        // difference between noticing a refusal and walking into the wall behind it.
        string text = line.Replace('’', '\'');
        foreach (string refusal in Refusals)
        {
            if (text.Contains(refusal, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
