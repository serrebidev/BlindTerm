using System.Net.Sockets;
using BlindTerm.Core;
using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;

namespace BlindTerm.Cli;

/// <summary>
/// Connects to a telnet host, runs what arrives through the transcript assembly, and prints
/// it -- the same thing the window does, without a window.
///
/// This is how the claim "nothing is lost" is checked. Windows' own telnet.exe repaints its
/// console rather than writing lines, so a pseudo console can only report whatever happened
/// to be on screen at its next redraw; feeding the socket straight in should report every
/// line the host sent, and this is what counts them.
/// </summary>
internal static class Telnet
{
    public static int Run(string[] args)
    {
        int cols = 120, rows = 30, seconds = 10, waitMs = 700;
        bool quiet = false, numbered = false;
        string? address = null, outputPath = null, soundFolder = null;
        bool play = false;
        var send = new List<Step>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cols": cols = int.Parse(Next(args, ref i)); break;
                case "--rows": rows = int.Parse(Next(args, ref i)); break;
                case "--seconds": seconds = int.Parse(Next(args, ref i)); break;
                case "--wait": waitMs = int.Parse(Next(args, ref i)); break;
                case "--send": send.Add(new Step(Next(args, ref i), IsKey: false)); break;
                case "--key": send.Add(new Step(Next(args, ref i), IsKey: true)); break;
                case "--out": outputPath = Next(args, ref i); break;
                case "--play": play = true; soundFolder = Next(args, ref i); break;
                case "--numbered": numbered = true; break;
                case "--quiet": quiet = true; break;
                default:
                    if (address is null) address = args[i];
                    else
                    {
                        Console.Error.WriteLine($"telnet: unexpected argument '{args[i]}'");
                        return 2;
                    }
                    break;
            }
        }

        if (!TelnetAddress.TryParse(address, out string hostName, out int port))
        {
            Console.Error.WriteLine("telnet: give a host, as \"host\" or \"host:port\".");
            return 2;
        }

        var core = new TerminalCore(cols, rows);
        using var session = new TelnetSession();
        var closed = new ManualResetEventSlim(false);
        long total = 0;

        FileStream? file = outputPath is null
            ? null
            : new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        string live = string.Empty;
        core.Updated += update => live = update.LiveText;

        session.Output += memory =>
        {
            Interlocked.Add(ref total, memory.Length);
            file?.Write(memory.Span);
            core.Feed(memory.Span);
        };
        MspPlayer? player = play
            ? new MspPlayer(new MciSoundOutput(), new SoundLibrary(soundFolder!))
            : null;
        using var stopPlayer = player;

        var sounds = new List<MspTrigger>();
        session.SoundRequested += trigger =>
        {
            sounds.Add(trigger);
            if (player is not null)
                Console.Error.WriteLine($"telnet: played={player.Handle(trigger)}");
            Console.Error.WriteLine(
                $"telnet: {trigger.Kind.ToString().ToLowerInvariant()} {trigger.FileName} " +
                $"V={trigger.Volume} L={trigger.Loops} P={trigger.Priority}" +
                (trigger.Type is null ? "" : $" T={trigger.Type}") +
                (trigger.Url is null ? "" : $" U={trigger.Url}"));
        };
        session.Exited += _ => closed.Set();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            session.ConnectAsync(hostName, port, cols, rows, timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            Console.Error.WriteLine($"telnet: could not connect to {TelnetAddress.Format(hostName, port)}: {ex.Message}");
            file?.Dispose();
            return 1;
        }

        Console.Error.WriteLine($"telnet: {cols}x{rows} -> {TelnetAddress.Format(hostName, port)}");
        session.Begin();

        foreach (var step in send)
        {
            Thread.Sleep(waitMs);
            if (!session.IsRunning) break;

            if (step.IsKey)
            {
                byte[]? bytes = Core.Vt.KeyEncoder.Parse(step.Text);
                if (bytes is null)
                {
                    Console.Error.WriteLine($"telnet: unknown key '{step.Text}'");
                    file?.Dispose();
                    return 2;
                }
                Console.Error.WriteLine($"telnet: key {step.Text}");
                session.Write(bytes);
            }
            else
            {
                Console.Error.WriteLine($"telnet: send {step.Text}");
                session.WriteLineSplit(step.Text, session.LineTerminator, 20).GetAwaiter().GetResult();
            }
        }

        // Repeats are started again when they finish, so something has to notice that they
        // have. The window uses a timer for this; here the wait does it.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds) && !closed.IsSet)
        {
            closed.Wait(TimeSpan.FromMilliseconds(250));
            player?.Tick();
        }

        // The last read is on another thread; settle until the byte count stops moving so the
        // transcript printed is the whole transcript.
        long settled;
        do
        {
            settled = Interlocked.Read(ref total);
            Thread.Sleep(150);
        }
        while (Interlocked.Read(ref total) != settled);

        core.Flush();
        file?.Dispose();

        Transcript transcript = core.Transcript;
        Console.Error.WriteLine(
            $"telnet: {settled} bytes of text, {transcript.Count} lines, " +
            $"{sounds.Count} sound triggers, " +
            $"{(session.IsRunning ? "still connected" : "disconnected")}");

        if (quiet) return 0;

        Console.WriteLine("--- transcript ---");
        for (int i = 0; i < transcript.Count; i++)
            Console.WriteLine(numbered ? $"{i,5}  {transcript.Lines[i]}" : transcript.Lines[i]);

        Console.WriteLine("--- current line ---");
        Console.WriteLine(live);
        return 0;
    }

    private readonly record struct Step(string Text, bool IsKey);

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }
}
