namespace BlindTerm.Core.Net;

/// <summary>
/// The way back, remembered as the compass steps taken to get here.
///
/// Every compass move the player makes -- typed as a word, or sent by a walk -- is
/// recorded. Detours fold away as they happen: go east and straight back west and the two
/// cancel, however deep the detour was, so what is left is always the shortest way home
/// along the path actually walked.
///
/// Moves the client never sees a compass direction for never enter the trail -- a portal,
/// a teleport, an exit the MUD names in words ("enter cave"). After one of those the way
/// back is simply not in here, which is why a fresh breadcrumb gets dropped there.
/// </summary>
public sealed class BreadcrumbTrail
{
    private readonly List<string> _steps = new();

    /// <summary>How many steps from the breadcrumb, after detours have folded away.</summary>
    public int Count => _steps.Count;

    /// <summary>
    /// Remembers one step, in any of its spellings. A step that undoes the last one removes
    /// it instead: the pair was a detour, and the way home never included it.
    /// </summary>
    public void Record(string direction)
    {
        if (!Compass.TryParse(direction, out string step)) return;
        if (_steps.Count > 0 && _steps[^1] == Compass.Opposite(step))
            _steps.RemoveAt(_steps.Count - 1);
        else
            _steps.Add(step);
    }

    /// <summary>
    /// Starts measuring from here. Whatever the trail held is forgotten, because "here" is
    /// the place the player wants to be able to come back to.
    /// </summary>
    public void DropBreadcrumb() => _steps.Clear();

    /// <summary>Forgets the trail without moving: the walk home is over, one way or another.</summary>
    public void Clear() => _steps.Clear();

    /// <summary>
    /// The steps home: the trail in reverse, each one flipped to its opposite. The trail
    /// itself is left alone -- clearing it is the caller's decision, made once the steps
    /// have actually been sent.
    /// </summary>
    public IReadOnlyList<string> Retrace()
    {
        var home = new List<string>(_steps.Count);
        for (int i = _steps.Count - 1; i >= 0; i--) home.Add(Compass.Opposite(_steps[i]));
        return home;
    }
}
