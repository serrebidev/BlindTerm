using BlindTerm.Core;

namespace BlindTerm.Cli;

/// <summary>
/// Runs a raw capture through the transcript assembly and prints what it produced, with no
/// window, no shell and no pty.
///
/// This is how a program that comes out wrong becomes a repeatable check. The capture is fed
/// in chunks, as the pty would deliver it, because chunk boundaries are not cosmetic: a
/// screen wipe arriving in the same read as the output it is about to destroy is the case the
/// assembly has to get right.
/// </summary>
internal static class Replay
{
    public static int Run(string[] args)
    {
        string? path = null;
        int cols = 120, rows = 30, chunk = 16 * 1024;
        bool numbered = false, showUpdates = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cols": cols = int.Parse(Next(args, ref i)); break;
                case "--rows": rows = int.Parse(Next(args, ref i)); break;
                case "--chunk": chunk = int.Parse(Next(args, ref i)); break;
                case "--numbered": numbered = true; break;
                case "--updates": showUpdates = true; break;
                default:
                    if (path is null) path = args[i];
                    else
                    {
                        Console.Error.WriteLine($"replay: unexpected argument '{args[i]}'");
                        return 2;
                    }
                    break;
            }
        }

        if (path is null)
        {
            Console.Error.WriteLine("replay: no capture file given.");
            return 2;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"replay: no such file: {path}");
            return 1;
        }
        if (chunk <= 0)
        {
            Console.Error.WriteLine("replay: --chunk must be positive.");
            return 2;
        }

        byte[] data = File.ReadAllBytes(path);
        var core = new TerminalCore(cols, rows);

        int batches = 0, appended = 0, revised = 0;
        string live = string.Empty;
        string[]? screen = null;

        core.Updated += update =>
        {
            batches++;
            appended += update.NewLines.Count;
            revised += update.Edits.Count;
            live = update.LiveText;
            screen = update.AlternateScreen ?? screen;

            if (!showUpdates) return;
            if (update.AlternateScreen is not null)
            {
                Console.Error.WriteLine($"  [batch {batches}] full-screen program, {update.AlternateScreen.Length} rows");
                return;
            }
            if (update.NewLines.Count > 0 || update.Edits.Count > 0)
                Console.Error.WriteLine(
                    $"  [batch {batches}] +{update.NewLines.Count} lines, {update.Edits.Count} rewritten");
        };

        for (int offset = 0; offset < data.Length; offset += chunk)
            core.Feed(data.AsSpan(offset, Math.Min(chunk, data.Length - offset)));
        core.Flush();

        var transcript = core.Transcript;

        Console.Error.WriteLine(
            $"replay: {data.Length} bytes in {(data.Length + chunk - 1) / chunk} reads, {cols}x{rows}");
        Console.Error.WriteLine(
            $"replay: {batches} batches, {appended} lines appended, {revised} rewritten, {transcript.Count} lines final");
        if (core.Engine.IsAlternateScreen)
            Console.Error.WriteLine("replay: ended inside a full-screen program");

        Console.WriteLine("--- transcript ---");
        for (int i = 0; i < transcript.Count; i++)
            Console.WriteLine(numbered ? $"{i,5}  {transcript.Lines[i]}" : transcript.Lines[i]);

        Console.WriteLine("--- current line ---");
        Console.WriteLine(live);

        if (core.Engine.IsAlternateScreen && screen is not null)
        {
            Console.WriteLine("--- screen ---");
            foreach (string line in screen) Console.WriteLine(line);
        }
        return 0;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
