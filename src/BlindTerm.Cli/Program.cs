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
            "directory" => Directory.Run(args[1..]),
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
              blindterm telnet <[ssl://]host[:port]> [options]
              blindterm speak [--probe] [--braille] [--batch] [--now] [text...]
              blindterm directory [--out FILE] [--previous URL] [--quiet]
              blindterm directory --mudstats-only

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
              --updates         Report each live transcript batch as it arrives
              --quiet           Print only the summary
              --tls             Encrypt the connection. Same as an ssl:// address.
              --insecure        Encrypt, and accept a certificate that does not verify.
                                The window asks first; a diagnostic run has nobody to ask.

            directory options:
              --out FILE        Where to write the list (default: mud-directory.json)
              --previous URL    The last list published, so addresses already looked up on
                                MUDStats are carried over instead of fetched again
              --endpoint URL    A MUDVerse API base other than the published one
              --mudstats URL    A MUDStats other than mudstats.com
              --grapevine URL   A Grapevine other than grapevine.haus
              --mudconnector URL  A Mud Connector other than mudconnect.com
              --no-mudstats     Publish the listings without activity figures
              --mudstats-only   Read MUDStats and report what came back, changing nothing.
                                Needs no key. Exits non-zero if the scrape has stopped
                                working, which is what to run when the figures go missing.
              --quiet           Do not report each page as it is fetched

              Builds the list of MUDs that BlindTerm downloads, so that browsing needs no
              API key from anybody using it. Merges four directories: MUDVerse (genre,
              ratings), Grapevine (encrypted ports, descriptions), The Mud Connector (the
              most addresses) and MUDStats (how busy each one has been). Any of them being
              down makes the list smaller, never absent. Reads the key from
              MUDVERSE_API_KEY, never from an argument -- an argument ends up in a shell
              history and a CI log.

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
              blindterm telnet ssl://coremud.org:4022 --seconds 8
              blindterm replay ls.raw --numbered
              blindterm directory --out mud-directory.json
              blindterm directory --mudstats-only
            """);
        return 0;
    }
}
