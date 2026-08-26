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

            capture options:
              --out FILE        Write raw pty bytes to FILE (default: capture.raw)
              --cols N          Terminal width  (default: 120)
              --rows N          Terminal height (default: 30)
              --send TEXT       Type TEXT then Return. Repeatable, in order.
              --wait MS         Milliseconds to wait after each --send (default: 700)
              --seconds N       Stop this long after the last --send (default: 3)
              --quiet           Do not echo decoded output to the console

            Examples:
              blindterm capture --send "echo hello" -- powershell.exe -NoLogo
              blindterm capture --out ls.raw --send "ls -la" -- wsl.exe
            """);
        return 0;
    }
}
