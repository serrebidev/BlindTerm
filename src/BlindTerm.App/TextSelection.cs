namespace BlindTerm.App;

/// <summary>A native edit-control selection adjusted as program output is replaced.</summary>
internal readonly record struct TextSelection(int Start, int Length)
{
    public TextSelection AfterReplacement(int replacementStart, int oldLength, int newLength)
    {
        int first = Move(Start, replacementStart, oldLength, newLength);
        int last = Move(Start + Length, replacementStart, oldLength, newLength);
        return new TextSelection(Math.Min(first, last), Math.Abs(last - first));
    }

    private static int Move(int position, int replacementStart, int oldLength, int newLength)
    {
        int replacementEnd = replacementStart + oldLength;
        if (position <= replacementStart) return position;
        if (position >= replacementEnd) return position + newLength - oldLength;
        return replacementStart + Math.Min(position - replacementStart, newLength);
    }
}
