using System.Runtime.Versioning;
using System.Text;
using BlindTerm.Core.Pty;

namespace BlindTerm.Cli;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0) return Usage();

        return args[0] switch
        {
            "capture" => Capture.Run(args[1..]),
            "replay" => Replay.Run(args[1..]),
            "telnet" => Telnet.Run(args[1..]),
            "speak" => Speak.Run(args[1..]),
            "-h" or "--help" or "help" => Usage(),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"blindterm: unknown command '{verb}'");
        Usage();
        return 2;
    }

    private static int Usage()
    {
        Console.WriteLine("""
            blindterm - diagnostics for the BlindTerm terminal core

            Usage:
              blindterm capture [options] -- <command line>
              blindterm replay <capture file> [options]
              blindterm telnet <host[:port]> [options]
              blindterm speak [--probe] [--braille] [--batch] [--now] [text...]

            capture options:
              --out FILE        Write raw pty bytes to FILE (default: capture.raw)
              --cols N          Terminal width  (default: 120)
              --rows N          Terminal height (default: 30)
              --send TEXT       Type TEXT then Return. Repeatable, in order.
              --wait MS         Milliseconds to wait after each --send (default: 700)
              --seconds N       Stop this long after the last --send (default: 3)
              --quiet           Do not echo decoded output to the console

            telnet options:
              --cols N          Terminal width  (default: 120)
              --rows N          Terminal height (default: 30)
              --send TEXT       Type TEXT then Return. Repeatable, in order.
              --key NAME        Send one named key, such as Up or C-]. Repeatable.
              --wait MS         Milliseconds to wait before each --send (default: 700)
              --seconds N       Stop this long after the last --send (default: 10)
              --out FILE        Also write the received text, less the protocol, to FILE
              --play FOLDER     Actually play the sounds a MUD asks for, from FOLDER
              --numbered        Prefix each transcript line with its index
              --quiet           Print only the summary

            replay options:
              --cols N          Terminal width used for assembly (default: 120)
              --rows N          Terminal height (default: 30)
              --chunk N         Feed the capture in N-byte reads (default: 16384).
                                Boundaries are not cosmetic: vary this to check that a
                                sequence split across two reads is still handled.
              --numbered        Prefix each transcript line with its index
              --updates         Report each batch as it is assembled

            Examples:
              blindterm capture --send "echo hello" -- pwsh.exe -NoLogo
              blindterm capture --out ls.raw --send "ls -la" -- wsl.exe
              blindterm telnet coremud.org:4000 --seconds 8 --numbered
              blindterm replay ls.raw --numbered
            """);
        return 0;
    }
}
