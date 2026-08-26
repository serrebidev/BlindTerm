using System.Runtime.Versioning;
using System.Text;
using BlindTerm.Core;
using BlindTerm.Core.Pty;

namespace BlindTerm.Cli;

/// <summary>
/// Runs a command under a pseudo console and writes every byte it produces to a file,
/// exactly as it arrived, escape sequences and all.
///
/// This is the ground truth the transcript assembly is checked against: a program whose
/// output comes out wrong becomes a capture, and the capture becomes a repeatable test that
/// needs no window, no shell and no pty.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Capture
{
    public static int Run(string[] args)
    {
        string output = "capture.raw";
        int cols = 120, rows = 30, waitMs = 700, seconds = 3;
        bool quiet = false, noEnv = false;
        var send = new List<string>();
        string? commandLine = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": output = Next(args, ref i); break;
                case "--cols": cols = int.Parse(Next(args, ref i)); break;
                case "--rows": rows = int.Parse(Next(args, ref i)); break;
                case "--send": send.Add(Next(args, ref i)); break;
                case "--wait": waitMs = int.Parse(Next(args, ref i)); break;
                case "--seconds": seconds = int.Parse(Next(args, ref i)); break;
                case "--quiet": quiet = true; break;
                case "--noenv": noEnv = true; break;
                case "--":
                    commandLine = string.Join(' ', args[(i + 1)..]);
                    i = args.Length;
                    break;
                default:
                    Console.Error.WriteLine($"capture: unexpected argument '{args[i]}'");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            Console.Error.WriteLine("capture: no command given. Put it after '--'.");
            return 2;
        }

        long total = 0;
        var exited = new ManualResetEventSlim(false);
        int? exitCode = null;

        using var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var session = new PtySession();

        // Decoded only for the operator's benefit; the file gets the raw bytes.
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[16 * 1024];

        session.Output += memory =>
        {
            var span = memory.Span;
            file.Write(span);
            Interlocked.Add(ref total, span.Length);

            if (quiet) return;
            int n = decoder.GetChars(span.ToArray(), 0, span.Length, chars, 0, flush: false);
            Console.Write(Printable(chars.AsSpan(0, n)));
        };
        session.Exited += code => { exitCode = code; exited.Set(); };

        var environment = noEnv ? null : TerminalEnvironment.ForChild();

        Console.Error.WriteLine($"capture: {cols}x{rows} -> {output}");
        Console.Error.WriteLine($"capture: running {commandLine}");
        session.Start(commandLine!, cols, rows, environment);

        foreach (string line in send)
        {
            Thread.Sleep(waitMs);
            if (!session.IsRunning) break;
            Console.Error.WriteLine($"capture: send {line}");
            session.WriteLineSplit(line).GetAwaiter().GetResult();
        }

        exited.Wait(TimeSpan.FromSeconds(seconds));

        // The child exiting does not mean its last output has been read: the pseudo console
        // writes its teardown sequence after the child is gone, and reads are on another
        // thread. Settle until the byte count stops moving, so the count reported is the
        // count written.
        long settled;
        do
        {
            settled = Interlocked.Read(ref total);
            Thread.Sleep(150);
        } while (Interlocked.Read(ref total) != settled);

        file.Flush();

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"capture: {settled} bytes, child {(exitCode is null ? "still running" : $"exited {exitCode}")}");
        return 0;
    }

    /// <summary>Escape sequences shown as visible text, so the console stays readable.</summary>
    private static string Printable(ReadOnlySpan<char> text)
    {
        const char Esc = (char)0x1b;
        const char Bel = (char)0x07;

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case Esc: builder.Append("<ESC>"); break;
                case Bel: builder.Append("<BEL>"); break;
                case '\r': builder.Append("<CR>"); break;
                case '\n': builder.Append("<LF>\n"); break;
                default:
                    if (c < 0x20) builder.Append($"<{(int)c:x2}>");
                    else builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
