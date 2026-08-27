using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.Speech;

namespace BlindTerm.Cli;

/// <summary>
/// Exercises the speech layer without a terminal attached: which reader is running, and does
/// speaking through it actually work.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Speak
{
    public static int Run(string[] args)
    {
        bool probe = false, braille = false, batch = false;
        var priority = SpeechPriority.Normal;
        var words = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--probe": probe = true; break;
                case "--braille": braille = true; break;
                case "--batch": batch = true; break;
                case "--now": priority = SpeechPriority.Now; break;
                case "--next": priority = SpeechPriority.Next; break;
                default: words.Add(args[i]); break;
            }
        }

        var router = new ScreenReaderRouter();

        // Report every candidate, not just the winner: "JAWS not running" is the answer to
        // most of the questions this verb gets asked.
        Console.WriteLine("readers:");
        foreach (var reader in new IScreenReader[] { new NvdaScreenReader(), new JawsScreenReader() })
            Console.WriteLine($"  {reader.Name,-6} {(reader.IsRunning ? "running" : "not running")}");

        Console.WriteLine($"secure desktop: {(SecureDesktop.IsActive() ? "yes (speech suppressed)" : "no")}");
        Console.WriteLine($"selected: {router.Name}");

        if (probe) return router.IsRunning ? 0 : 1;

        if (!router.IsRunning)
        {
            Console.Error.WriteLine("speak: no screen reader running; nothing to speak through.");
            return 1;
        }

        string text = words.Count > 0
            ? string.Join(' ', words)
            : "BlindTerm speech layer working.";

        if (braille)
        {
            bool ok = router.Braille(text);
            Console.WriteLine($"braille: {(ok ? "sent" : "not supported by this reader")}");
            return ok ? 0 : 1;
        }

        if (batch)
        {
            // The batching path, which is what streamed output actually goes through.
            using var announcer = new Announcer(router);
            var update = new TerminalUpdate { FirstNewLine = 0 };
            for (int i = 1; i <= 5; i++) update.NewLines.Add($"line {i}");

            var news = new LineNews();
            announcer.Enqueue(news.News(update));
            Console.WriteLine("queued 5 lines; they should arrive as one utterance.");

            // A second batch of the same lines says nothing: that is the news filter.
            announcer.Enqueue(news.News(update));
            Thread.Sleep(800);
            return 0;
        }

        bool spoken = router.Speak(text, priority);
        Console.WriteLine($"speak: {(spoken ? "sent" : "failed")} ({priority})");
        return spoken ? 0 : 1;
    }
}
