using System.Runtime.Versioning;
using System.Text;
using BlindTerm.Core;
using BlindTerm.Core.Pty;
using BlindTerm.Core.Vt;

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
        int cols = 120, rows = 30, waitMs = 700, seconds = 3, startupMs = -1;
        bool quiet = false, noEnv = false;
        var send = new List<Step>();
        string? commandLine = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": output = Next(args, ref i); break;
                case "--cols": cols = int.Parse(Next(args, ref i)); break;
                case "--rows": rows = int.Parse(Next(args, ref i)); break;
                case "--send": send.Add(new Step(Next(args, ref i), IsKey: false)); break;
                case "--key": send.Add(new Step(Next(args, ref i), IsKey: true)); break;
                case "--wait": waitMs = int.Parse(Next(args, ref i)); break;
                case "--startup": startupMs = int.Parse(Next(args, ref i)); break;
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

        // A full-screen program has to be given time to put the terminal into raw mode before
        // anything is typed at it. Until it does, the line discipline is still echoing, and a
        // key sent early comes back in caret notation -- an arrow arrives as a literal "^[[B"
        // printed onto the screen -- which looks like a bug in the terminal and is not one.
        bool first = true;
        foreach (var step in send)
        {
            Thread.Sleep(first && startupMs >= 0 ? startupMs : waitMs);
            first = false;
            if (!session.IsRunning) break;

            if (step.IsKey)
            {
                byte[]? bytes = KeyEncoder.Parse(step.Text);
                if (bytes is null)
                {
                    Console.Error.WriteLine($"capture: unknown key '{step.Text}'");
                    return 2;
                }
                Console.Error.WriteLine($"capture: key {step.Text}");
                session.Write(bytes);
            }
            else
            {
                Console.Error.WriteLine($"capture: send {step.Text}");
                session.WriteLineSplit(step.Text).GetAwaiter().GetResult();
            }
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

    /// <summary>One scripted action: a line to type, or a single key to press.</summary>
    private readonly record struct Step(string Text, bool IsKey);

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
