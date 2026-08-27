namespace BlindTerm.App;

/// <summary>
/// Tracks a line-mode foreground command from submission until the shell reports its completed
/// OSC 133 block. Input submitted while that command owns the terminal is input for the same
/// program and must not move the completion boundary.
/// </summary>
internal sealed class ForegroundProgramState
{
    private int _completedBlocksAtStart;

    public bool Active { get; private set; }

    public void Submitted(string text, int completedBlocks)
    {
        if (Active || text.Length == 0) return;
        Active = true;
        _completedBlocksAtStart = completedBlocks;
    }

    public void Updated(int completedBlocks)
    {
        if (Active && completedBlocks > _completedBlocksAtStart) Active = false;
    }

    public void Exited() => Active = false;
}
