namespace BlindTerm.Core;

/// <summary>Validates dimensions shared by the terminal parser and ConPTY.</summary>
public readonly record struct TerminalSize(int Columns, int Rows)
{
    public const int MinimumColumns = 1;
    public const int MinimumRows = 1;
    public const int MaximumColumns = short.MaxValue;
    public const int MaximumRows = short.MaxValue;

    public static TerminalSize Validate(int columns, int rows)
    {
        if (columns is < MinimumColumns or > MaximumColumns)
            throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows is < MinimumRows or > MaximumRows)
            throw new ArgumentOutOfRangeException(nameof(rows));
        return new TerminalSize(columns, rows);
    }
}
