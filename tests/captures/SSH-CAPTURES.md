Captures with no `.expected` file are corpus, not fixtures. The replay runner skips them.

  real-ssh-nano.raw     nano run directly over ssh -t
  real-ssh-session.raw  a full interactive ssh session: banner, a command, nano taking the
                        alternate screen and giving it back, cat proving nano really saved,
                        then logout

Both are recorded against a real host and are the captures that prove the headline
requirement -- a full-screen editor over ssh behaving as it would in any other terminal.
Neither can be diffed against a fixture: their transcripts carry a login timestamp, the
client IP, and the line ssh prints on disconnect ("Connection to <address> closed."), all of
which change from run to run and machine to machine.

They are here to develop screen mode against. Replay them by hand:

    dotnet run --project src/BlindTerm.Cli -- replay tests/captures/real-ssh-session.raw --cols 100 --rows 25

Recorded at 100x25. The host-specific banner, login timestamp, and disconnect line are expected to differ between machines.
