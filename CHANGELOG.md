# Changelog

Readable release history for BlindTerm. This starts with the first build
that was complete enough to install and use, rather than pretending the
earlier prototypes were something anyone could have run.

## v0.7.20 - 2026-09-23

- Read the MUD browser out loud. Every outcome a fetch could have -- that a search had started,
  how many MUDs came back, that the directory did not answer in time, that it could not be read
  at all -- was written to a status label and nowhere else, and a label is a place words sit for
  somebody to look at: it raises nothing a reader announces. Pressing Show MUDs therefore gave a
  blind user no way to tell a slow fetch from a failed one from nothing matching, and a network
  that never answered meant up to two minutes of silence and then a line nobody heard. The
  status line is spoken as well as shown.
- Say it from a dialog, which needed a door of its own. Speech is gated on this window being the
  one in front -- right for a terminal somebody has walked away from, wrong for a dialog the
  program opened itself, because a modal dialog deactivates the terminal behind it and every
  word the browser tried to say was dropped by that rule before it reached anyone. A dialog is
  by definition the window the user is in, and `AnnounceHere` is the one place that is decided.
- Name the control a full-screen program hands the keyboard to. It had no name at all, so the
  moment nano or vim took over and focus moved there, the reader said a bare "edit" beside the
  line under the program's cursor -- at the moment a blind user most needs to be told they are in
  the program and not at the shell's command line. It reads "Full-screen program" now. Its whole
  text is a zero-width space, which is how it went unnoticed: anything that only asked whether
  the text was empty saw a name.
- Say the choice back when the browser fills the connect dialog in. Focus lands on Connect, and
  what was about to be dialled was put in that button's *description* -- the place the code said
  the choice was "confirmed out loud", and not a place either reader reads when focus arrives. A
  wrong port was discovered by failing to connect some seconds later. It is the button's name
  now, which both readers do say.
- Let Enter test the trigger instead of saving it. In the trigger editor's "Try a line" box,
  Enter went to the dialog's Save button: the editor closed, the report that box exists to
  produce never appeared, and the trigger was written from a form nobody had finished checking.
  The MUD browser's search box was already wired against exactly this trap, and the editor now
  is too.
- Say why the command line is not there, and stop the painted screen taking focus. Alt+2 while a
  full-screen program held the keyboard, or while a frozen screen was being read, did nothing
  and said nothing -- a disabled control and a hidden one both refuse focus in silence. It now
  says which of the two it is and how to get back. The painted surface is no longer selectable,
  so a click can no longer take the keyboard off the proxy and onto an object with no name, role
  or value; and the disabled "use as the default terminal" item carries its reason in its text
  rather than in a tooltip, which only a mouse ever reads.
- Take Pass Next back. Holding the bare modifier is what lets an Alt chord still arrive, and that
  is also the key that opens the menu bar -- so an arming that was not meant left Alt swallowed,
  the menu out of reach, and nothing said about either. The command that armed it cancels it,
  and arming now says so.
- Check the names, window by window. A new test walks the main window and every dialog and fails
  on any control a reader can land on that has nothing to say for itself, counting a name made
  of a zero-width space as no name at all -- which is what the proxy's was.

## v0.7.19 - 2026-09-23

- Stop a MUD directory pairing one machine's encrypted port with another machine's address.
  Grapevine is the only source here that names the secure port outright, which is why it is read
  at all, but the port was taken from whichever connection named one and then kept against the
  plain address whichever connection named that -- so a listing whose two halves sit on
  different hosts was published as *"plain.example, port 4000, or port 7443 with TLS"*, where
  7443 had been published by a server that was never this game's. Nothing about dialling that
  can connect. An encrypted port is now kept only when the machine that published it is the
  machine being dialled, which is the rule the join between directories already follows -- two
  directories are not allowed to lend each other an encrypted port either, and a source is not
  allowed to do it to itself.
- Read a telnet subnegotiation's escaped 255 as the byte it is. An MSDP string carries a literal
  255 as a doubled IAC, and the telnet layer collapses that pair before the MSDP parser is
  handed the payload, so a lone 255 there is ordinary data. It was refused -- and refused
  fatally, failing the whole subnegotiation rather than the one string that held it. One such
  byte inside a room name discarded HEALTH, the room, the exits and every other variable in that
  update, silently, every time the server sent it.
- Skip a Big List row that has no address instead of giving it the next row's. The Mud
  Connector's rows are read by one pattern each, and that pattern's lazy runs were free to cross
  the row's own `</tr>` to reach the address they were looking for. A game with no telnet link
  therefore took the next game's host, port, website and connect status, and the game that owned
  them was dropped from the list altogether -- a listing you cannot connect with, and a
  connection you cannot find, from one row that simply had nothing in it. The pattern can no
  longer leave the row it started in, which is the failure the row reader always documented:
  losing one listing beats inventing one.
- Let Pass Next actually pass an Alt chord. Alt arrives as a press of its own before the chord it
  belongs to, and that bare Alt spent the arming and then opened the menu bar, which swallowed
  the chord behind it. So the one case the command exists for -- sending an Alt chord to a
  full-screen program -- was the one case it could not do, in either mode. A modifier on its own
  now holds the arming and is swallowed, so the chord that follows still finds it armed.
- Bound the command block tracker's row map. It kept one entry for every buffer row the session
  ever read, for the whole life of the session, while only the rows near the bottom can ever be
  asked about again -- the same unbounded growth the block list itself had to be given a ceiling
  for, at one entry per row rather than one per command.
- Refuse an update helper argument that names a path rather than a file. The name was reduced to
  its basename before it was checked, so the check compared a value with itself and could never
  fail. The refusal now happens before the staging directory is created or the archive is
  unpacked, so an invocation that cannot be right leaves everything where it was.
- Serialise the capture tool's writes to its output file. The pseudo console's read thread wrote
  the capture while the main thread flushed it, and a child that is still painting when the
  settle window expires reaches both at once.

## v0.7.18 - 2026-09-20

- Run the command you recalled. Once Tab has handed the line to the shell's own editor, that
  editor owns it until Return: BlindTerm streams typing to it and sends the terminator alone at
  the end. Recalling a history line then put text in the box that the far end had never seen,
  while the far end went on owning an empty line -- so Return sent the terminator by itself and
  ran that empty line, and a command that had been typed and then recalled simply did not run.
  Worse quietly: every arrow was still forwarded to an editor whose cursor was somewhere else,
  which is what a shell answers with its bell. A recalled line belongs to this window, and the
  terminal's editor is told it no longer owns the line before anything is put there. This held
  for a local shell as much as for one over SSH; it was found by running it, not by reading it.
- Stop reading a shell's keyboard feedback out as "Attention". While the terminal's own line
  editor owns the line, every keystroke is being sent to it, so a bell there is that editor
  answering one of them -- a completion that matched nothing, or an arrow with nowhere to go --
  and a shell rings each time somebody arrows into the end of a line. It was announced as
  "Attention" followed by the prompt, on every single press, over the top of the character the
  caret had actually moved to. The bell is still sounded; it is no longer described. A bell
  worth hearing is rung when nobody is typing, which is what Claude Code does when it wants a
  turn.
- Ask for the line back in one place. The recall rule and the local shell's own arrow handling
  are two doors onto the same history, and putting the hand-back at either of them would have
  left the other one broken.

## v0.7.17 - 2026-09-20

- Stop the arrow keys at an SSH prompt from ringing the far end's bell and reading out nothing.
  The window works out who a keystroke belongs to from the session's kind, and a connection
  somebody *typed* is not one of the kinds that says where it goes: `--ssh` and the Terminal
  menu are. A client typed at a prompt leaves the window a local shell with a child process,
  which is exactly what a nested `cmd` or a Python prompt looks like, so every rule that asked
  the kind answered "local" -- and at an empty command line all four arrows, Left and Right
  included, were handed to the far end. A bash prompt answers those with its own history when it
  has one and its bell when it has not, and BlindTerm reads a bell out as "Attention" beside the
  prompt, so pressing an arrow said the prompt back again while the caret the reader was
  standing in never moved: the "blank" and the "Caret didn't move before timeout" that came with
  every press. The line that started the client is now remembered for as long as that client
  runs, and it is asked again each time a line is submitted at an idle prompt, because starting
  a program is the only thing such a line can do. Up and Down at an SSH prompt walk back through
  the lines this window has sent, readably, in the command line; Left, Right, Home and End are
  ordinary caret keys; and nothing is sent over the wire merely to be answered with a bell.
- Ask one question about where a session's far end is, and ask it in one place. The rules that
  needed the answer had been working it out separately -- which is how an SSH session came out
  local to the key rules and remote to the history rule, and why neither of them could see a
  client typed at a prompt at all. v0.7.16 chased this same bell by making the history rule ask
  the session kind rather than a flag, which was the right shape and the wrong question: the
  kind is Shell for a connection typed at a prompt, so it fixed the symptom only for
  connections BlindTerm had opened itself.

## v0.7.16 - 2026-09-20

- Recall what you sent at an SSH prompt with Up and Down. A window connected to a host decides
  whether a program is running by asking whether it owns the input, and over SSH the answer is
  always yes -- there is no local process tree to look at -- so the arrows were handed to the
  far end instead of the box being typed in. A bash prompt answers those with its own history
  when it has one and its bell when it has not, and BlindTerm reads a bell out as "Attention"
  beside the prompt, so the only thing heard on every press was the prompt read back again,
  while the line that had actually been recalled was nowhere to go and read. Up and Down at an
  SSH prompt now do what they do at a local one: they walk back through the lines this window
  has sent, in the command line, where a reader can sit in them and edit them. A telnet
  connection has worked that way from the beginning, and SSH was left out by a question asked
  as "is this telnet", which is a different question with a different answer.
- Ask one question about where a session's far end is. Nothing the user sees changes; what
  changes is that a shell reached through the Windows OpenSSH client is stated to be a remote
  host in one place rather than worked out at each place that needs to know, which is how the
  window's key rules and the history rule came to disagree about the same session.

## v0.7.15 - 2026-09-20

- Put Home and End where they belong in a full-screen editor. nano's Home and End moved nano's
  cursor and left the caret the reader was standing in exactly where it was, because those two
  keys were held back from the native edit control along with Up and Down -- the control exists
  to give NVDA and JAWS a real caret, and it was never told about them, so NVDA waited the tenth
  of a second it allows for a caret move that was never coming, timed out, and read out the
  character it had been sitting on. Pressing Home at the start of a line's worth of "and" said
  "a", "n", "d", "d", "d" and moved nowhere. Movement along a line is the one kind that control
  can express, since it holds the line the cursor is on, so Home and End now reach it exactly as
  Left and Right always have: the program is still sent the key, and the caret moves to where
  the program put its cursor.
- Land End on the last character of the line rather than past it. A native edit puts the caret
  after the final character, where there is nothing to read, so End answered "blank" on a line
  that was not empty. A terminal's cursor is always on a character, and the control the reader
  is in is standing for the terminal's cursor.

## v0.7.14 - 2026-09-20

- Bring the thirty-day player averages back. The published list has been carrying none of them,
  and the reason was one number: MUDStats' table is read by asking it for five thousand rows at
  once, which it used to serve and now answers with a 500. A failure out of that reader is not
  one field lost on some listings, it is every figure on all of them -- the thirty-day average,
  the peak, the minimum, the monthly trend, the year a game opened, its codebase, its database
  size, whether it charges, and who measured any of it -- so the whole of the directory's most
  valuable half arrived as one line saying "MUDStats unavailable, publishing without activity
  figures", twice an hour, while the list went on looking fine. It is asked for a thousand rows
  at a time and paged now, which is three requests for two thousand two hundred and sixty-one
  worlds, and the ceiling that makes that necessary has a test beside it. The two orderings
  that only those figures make possible, busiest on average over thirty days and highest peak
  in thirty days, and **Oldest** by the year a game opened, have something to sort by for the
  first time.
- Read the genres that come with them. Carrying MUDStats' figures across carried its game type
  and not its genre, and MUDVerse is the only other directory that names one -- on the forty
  listings its shallow pages reach. So seven hundred and nineteen of the seven hundred and
  fifty-nine published listings had no genre at all, the filter that narrows the list by one
  matched five per cent of it, and the two hundred genres the activity half publishes
  described nothing. Genres now come from whichever directory has one.
- Stop reading The Mud Connector as ISO-8859-1. Its Big List is served with
  `charset=ISO-8859-1` in the header and UTF-8 in the body, which its own meta tag three lines
  down says out loud, and the header was believed: "Los años oscuros" was published as "Los
  aÃ±os oscuros". Anything that takes the eye past an accent, an em dash or an apostrophe came
  out as two wrong characters, and a name is what the list is sorted by, searched with, and
  spoken on every arrow press. The bytes are decoded as UTF-8 whatever the header claims.
- Fold two directories' listings of one server into one row. The join between directories is on
  the name, which is the only field they agree on and mean the same thing by, and they disagree
  often enough that one machine arrives twice: "Aarchon" from one directory and "Aarchon MUD"
  from another, "Dune" and "DuneMUD", "Arctic" and "ArcticMUD". Neither name is wrong and they
  are never alike enough to key on, so the same game was published twice with the two rows free
  to contradict each other -- one directory dialled the host while it built its page and says
  so, the other did not check and says nothing, and one machine was offered as up and as not
  answering in two adjacent rows. A host and port is one server now. A group whose listings all
  come from one directory is left alone, because The Mud Connector really does list two
  different games on one host and port.
- Stop offering an address that cannot be dialled. One row of the Big List carries a stray
  number inside its telnet link -- `216.136.9.8 126` on port 1260 -- and nothing noticed,
  because the host was not empty, so it was published, offered in the browser, and could only
  fail with a name that does not resolve. A host is one word; one with a space in it is not a
  host.
- Stop calling a rank that nobody is counting this month's. The Mud Connector's ranking has not
  moved since 2021 and it publishes no votes with it, so the details line read "Ranked 312 this
  month, on 0 votes" about five hundred and sixty-eight of the listings -- and **Top voted**
  ordered by that rank first, which put every game nobody has voted for above every game
  somebody has. Votes come first, the rank only breaks a tie between games that have some, and
  the sentence is only said when there are votes behind it.
- Take off the spaces a listing's own page wrapped it in. Four parsers write into one shape and
  each reads a different kind of page, so what arrived was a name ending in a space and read out
  with a pause on the end, a blurb with the source's own line endings still inside it, and a
  website address with two trailing spaces or an annotation after it -- which, offered whole as
  a link, fails at the far end with nothing to tell that from a site that has gone. A name is
  trimmed, a blurb is one line, and an address is what comes before the first space.
- Credit every directory that contributed, not only the ones a listing came from. MUDStats'
  worlds are merged into listings that keep MUDVerse's name, so it could never appear in the
  list the browser reads out as "From ..." -- which named three directories while the README
  above the file named four, and while the two that were missing are where every player count
  in it comes from.
- Ask for the published list by its tag. The note beside the six-hour refresh window has always
  said an unchanged list costs one 304 and no download; nothing implemented it, so every copy
  of the program pulled four hundred kilobytes down again every six hours for a file that is
  rebuilt twice an hour and rarely different. The copy on disk is kept with the tag the server
  gave it and the next ask sends it, so an unchanged list costs the headers and nothing else.
- Say what the published file's own fields mean. `Source` is the directory a listing came from
  before anything was merged into it and `Attribution` is the home page of the format's
  original owner -- neither of them is the credit for the data, which is what `Sources` is for,
  and one of the two said in its own comment that it was read out in the browser when nothing
  reads it at all.

## v0.7.13 - 2026-09-20

- Stop copying the transcript from taking the window down. Alt+A and Alt+O read the transcript's
  lines on the window thread while the session's reader thread was still adding to them, and
  walking a list that is being appended to throws "Collection was modified" -- out of the window
  thread, where nothing catches it, which is the end of the program. The reads that walk the whole
  document now take a copy under the same lock the reader writes under, so asking to copy a
  command's output in the middle of a build is an ordinary thing to do again.
- Read the transcript a line at a time again after leaving a full-screen program. Coming back from
  vim, htop or an editor over SSH refilled the output box from the transcript's own text, which
  ends a line with a single newline -- and a Win32 edit control does not take that for a line break
  at all. It reported one line, `GetFirstCharIndexFromLine` answered -1, and the session had to be
  arrowed through as one paragraph until something else arrived. The box is given lines the way
  Windows counts them now, and so is the clipboard.
- Put the caret where it says it is putting it. The transcript counts one character per line ending
  and the edit control holds two, and two of the three places a caret was moved to a line used the
  first counting. Alt+Up and Alt+Down over command blocks, and `Shift+Tab` into a remote session's
  latest response, both landed one character per line early -- a screenful out, in a session long
  enough to have scrolled. One offset now answers all three.
- Wait for a trigger whose row was wrapped. A shell integration marker that landed on a
  continuation row was never told its row had become a line, so a command whose prompt or output
  ran past the window's width reported "Command location is not available", and copying its output
  took the wrong range. Every row of a wrapped line is reported now, which is what the mapping
  beside it always claimed.
- Ask the MUD what it is, rather than agreeing and waiting. MSSP is a request, not a subscription,
  and BlindTerm only ever agreed to it: **Read** &rarr; **Server information** answered "This host
  did not say anything about itself" about hosts that had a name, an uptime, a codebase and a room
  count ready to send. The report is asked for when the option is agreed.
- Keep two sounds from wearing each other's name. MCI keeps one table of open devices for the whole
  process, and the aliases were counted from one sound output rather than from the process -- so a
  window with a trigger sound and a MUD sound both switched on had two of them handing out the same
  names. The second sound to open was refused and played nothing, and stopping one could stop the
  other. The count belongs to the process now.
- Count a listing's encrypted port as belonging to its host. The merge between directories refuses
  to lend an encrypted port from a game with no address to one that has, but it only asked whether
  the first had an address at all, not whether it was the same machine. A game listed in two
  directories with different hosts took the other's TLS port and offered a connection to a host
  that never published it.
- Read a published listing that is missing a key. Source, SourceId and Name were declared as
  required, which makes the JSON reader throw before the loop written to discard an unusable
  listing ever sees it -- so one listing edited by hand cost the whole list rather than one row. An
  absent key is an empty word now, and a listing with no address is dropped, which is what that
  loop was always for.
- Read a table cell that is not text. MUDStats' list is a rendered table, and every cell in it is a
  piece of HTML until a column moves and a number arrives where markup was: asking a number for its
  string threw out of the reader and stopped the whole published list being built. A Grapevine link
  that arrives as a number rather than a word threw in the same place, where a link that cannot be
  read should simply be ignored.
- Work a page number out in a number that cannot overflow. The MUDVerse path sliced a page with the
  32-bit arithmetic the shared one had already given up: a page number large enough to wrap
  returned the first page instead of nothing, which is the failure the shared one carries a test
  for.
- Stop a hand-typed directory address from ending the program. The MUD browser's fetch is an
  `async void`, so an address saved without a scheme -- or a window closed while a fetch was in
  flight -- threw something that was not a directory failure out of an unhandled handler on the
  window thread. It says so in the status line instead.
- Keep the port typed beside an IPv6 address. `--telnet ::1 4000` read the colons of the literal
  for an address that already carried a port and dialled 23. The brackets decide it now, the way
  the address parser always has.
- Take a bare carriage return for a line ending. A host that ends its lines with a carriage return
  on its own had its sound triggers left in the text, read aloud as punctuation in the middle of a
  fight, because only a line feed counted as the start of a line.
- Let go of what a failure and a close leave behind. If the second `CreatePipe` failed, the first
  pair was never closed, and a session that never started is never disposed by anybody; a write
  arriving as the window closes no longer throws out of the queue that has been disposed under it;
  a certificate copied out of a handshake that then succeeded is released rather than waiting for
  the collector; a wildcard in a sound's subfolder no longer builds an address that means a
  different page; two windows can no longer publish each other's half-written list of MUDs; a
  cancelled update check stops instead of asking again; and a sound staged under a name whose hash
  has no positive form is staged under its hash anyway.

## v0.7.12 - 2026-09-20

- Ask GitHub for the newest release instead of for the most convenient address to it. The
  updater read `releases/latest/download/BlindTerm-update.json`, which is a redirect GitHub
  caches at the edge, and for several minutes after v0.7.11 was published that cache was still
  answering with v0.7.10's manifest. The release was live, the tag was live, the API knew about
  it -- and the one address BlindTerm trusted said "you are up to date", which is why v0.7.11
  was not offered. It now asks the API for the newest tag and builds the manifest address from
  that, falling back to the redirect only when the API cannot be reached or is rate-limiting
  the machine. Look for updates where the answer cannot be yesterday's.
- Say what the update list actually said. "BlindTerm is up to date" was the same sentence
  whether the list named a newer release, named this one, or could not be read at all, so
  after publishing something there was no way to tell those apart. A check you ask for now
  names the release the list gave and the version this copy is, so a stale answer says so.
- Stop a trigger from typing a control character. A wildcard can carry text the far end chose,
  and a Send action puts it straight back into the line being typed -- so a MUD could have a
  trigger press Ctrl+C at a shell or Ctrl+D at a login. Anything a prompt would read as a
  command rather than as text is now dropped, and a newline is still turned into a space.
- Read wildcard numbers past nine. `$10` was the first wildcard with a zero after it rather
  than the tenth, which a regular expression with ten capturing groups can legitimately ask
  for.
- Refuse a trigger too long to be one before copying it. The scanner has always bounded this
  and the parser it hands the line to did not, so a caller reaching that directly allocated in
  proportion to a line that arrived from the far end.
- Skip a hole in a directory's listings rather than dereferencing it. Four parsers feed the one
  merge, and a missing element is not a game with no name.
- Remove `PtySession.Kill`, which nothing called: a killed child was reported as an ordinary
  exit with code 1, so the API promised a distinction it never made.

## v0.7.11 - 2026-09-20

- Stop a release manifest from taking the whole program down. Every field in it was declared as
  a string that is always there, and JSON writes a null where the publisher wrote one, so a
  single `"version": null` in the file BlindTerm downloads to look for updates became a
  NullReferenceException thrown from a background timer where nothing was listening for it and
  nothing caught it. The fields are read as what they are, the address has to be https, and the
  check catches everything: looking for a newer version must never be able to end the terminal.
- Keep every setting when one of them is wrong. A hand-edited number outside what a terminal
  can be -- `"Columns": 0` -- was refused with an exception, and the whole file was answered by
  starting again from the defaults: every trigger, every remembered address, every preference,
  gone, and made permanent by the next save. Values are brought inside their range instead, a
  file that will not parse is copied to `settings.json.corrupt` before the defaults replace it,
  and the temporary file a save writes through is named after the process, so two windows open
  at once cannot move each other's half-written settings into place.
- Keep the colours that were chosen. The settings dialog saves a copy of the settings, and the
  window copied every field back from it except the theme -- so choosing dark worked until the
  next thing that saved anything, connecting to a MUD or turning MUD sounds off, at which point
  the old value was written back over it.
- Count a flood by its own clock. The run of output the flood detector measures was ended by any
  gap longer than the batch window, which is 25 milliseconds, and Windows resolves a program's
  own delay to 15.6 -- so a build printing sixty lines a second reset the count on every line
  and was never treated as flooding at all. Speech queued behind it exactly as it had before
  that feature existed. This is the one thing here that was visible in the test suite: two of
  the flood tests failed on every run.
- Say what a trigger was told to say even while it is waiting. A trigger with a wait between
  firings also stopped the triggers listed below it from running, because the cooldown and
  "stop checking later triggers" were answered by the same branch -- so "everything from this
  channel, except when it mentions me" let the channel through for as long as the mention was
  still being throttled.
- Stop joining two games into one. The join took the first three letters off any name that
  began with them, so a game called "Thera" keyed the same as one called "Ra" and the two shared
  each other's player counts -- the exact failure the merge is written to avoid, and the one the
  README promises cannot happen. An article is now removed only when it is the whole word, and a
  listing with no address of its own no longer lends its encrypted port to the game that has one.
- Never wait for a sound to download. A missing sound was fetched on the window's thread with
  the sound board's lock held, so a MUD naming an address that never answers froze the terminal
  for the whole of the twenty-second timeout. The fetch happens in the background now and the
  sound plays from the next trigger, which for the MUDs that use sound packs is the next room
  description.
- Bound three things a server chooses. A telnet subnegotiation that was never closed grew for
  the life of the connection and swallowed every byte after it, leaving a terminal permanently
  deaf with nothing to say why. A sound named "NUL.wav" or "COM1.wav" reached the device Windows
  answers with rather than a file, and a wildcard in the sound folder was a pattern rather than
  a folder. A loop count of two billion was a sound that outlived the session that started it.
- Send one line at a time. The gap between a submitted line and its Return is an await, and two
  submissions that overlapped in it put their texts next to each other and their Returns after
  both: one command made of two, followed by two blank lines. A trigger that sends is how that
  happened without anybody typing.
- Tidy up after a shell that would not start. A mistyped shell name threw out of the window's
  own startup, taking the process with it and leaving the pseudo console and its attribute block
  behind -- raw handles with no finalizer, belonging to a session the window had already
  attached and would never dispose. It now names the program it could not find, closes the
  window, and lets go of everything it had built.
- Keep a reader that has stopped from stopping the terminal. JAWS is bound late and called from
  two threads at once, and one thread dropping a dead object between another thread's check and
  its call was a NullReferenceException out of a timer thread, which is the end of the process.
  The object is read once, under a lock, and called through that.
- Keep the reader's place when a connection ends half-way. A telnet session that ended for any
  reason other than a closed socket or a dropped connection never announced it and went on
  reporting itself as live, and asking it to read again after that started fresh threads against
  a closed stream.
- Stop two things that grew with the length of a session. Every marker of a shell-integration
  session was looked through for every row the transcript read, and every command that had ever
  finished was re-checked with them, so a long session cost more the longer it ran; and a MUD's
  own variables were remembered for every name the server felt like sending.
- Let go of the caret, the clipboard and the timer. Pass Next waited for a key it could
  translate rather than passing the one that was pressed; a copy taken while another program
  held the clipboard open ended the process; and a sound timer waked four times a second for the
  rest of a session that had played one sound.
- Stop reading addresses and numbers as things they are not. "http://mud.example.com:80" was
  taken for an unbracketed IPv6 address and dialled as a host by that name; a parameter long
  enough to wrap a 32-bit accumulator was read as a screen wipe, and a wipe behind another wipe
  as one; a MUD's hit points of 1e300 were read out as a figure from nowhere; and a page number
  far past the end of the list returned the first page rather than nothing.

## v0.7.10 - 2026-09-02

- Close a window itself once the program it was opened for has finished cleanly. A console
  Windows pops up for a one-shot program -- a script, an installer, a build -- stayed open
  after the run ended, saying "Program exited with code 0" and waiting to be dismissed by
  hand. The window now says that line and closes itself a moment later, the way the console
  it stands in for would have. A run that failed still keeps its window, because that is
  where the error can be heard or read back from, and an unknown exit code or a dropped
  connection keeps it too. A window somebody is reading is never closed under them: a
  full-screen view that has not been cleared, or a caret parked back through the
  transcript, cancels the close. The same applies when a shell window is closed by typing
  exit, or an SSH window by the connection ending.

## v0.7.9 - 2026-08-31

- Speak output about three times sooner after a command finishes. The wait between a program
  printing and a screen reader saying it was measured at 132 ms and is now 42 ms. Two things
  were behind it. The pause after output stops was written as 50 ms but took 62: Windows runs
  its timers at 15.6 ms by default and a wait never ends early, so every number here was
  quietly rounded up. BlindTerm now holds the clock to a millisecond while output is waiting
  to be spoken, and lets it go again afterwards rather than keeping the machine awake for a
  terminal that is saying nothing. The pause itself is also shorter, because the reason it was
  long no longer held: streamed output **queues** in both NVDA and JAWS rather than cutting
  off what they were saying, so waiting to gather a burst was never what kept a line from
  being talked over.
- Stop making every Return wait a quarter of a second while a program is running. The pause
  that lets Codex tell a typed line from a pasted one was being charged to every child
  process, so running anything at all -- a nested `cmd`, an `ssh` session, a Python prompt --
  put 250 ms in front of every command for as long as it was open. It now applies only when
  one of the agent CLIs is actually reading the line, or when Windows handed over a console
  whose program BlindTerm cannot identify. A line shorter than three characters never waits
  at all: no composer calls two characters a paste, so a bare Return or a one-key answer had
  nothing to wait for.
- Read an ordinary command's output instead of describing it. Any batch over thirty lines was
  replaced with "41 lines of output. Last 30:", which a directory listing, a git log or a
  short test run trips easily. The threshold is now high enough that ordinary commands are
  read whole, and a genuine flood is still summarised with its tail.
- Keep up with a program that floods the terminal. Speech ran further and further behind a
  steady printer -- still reading the start of a build after it had finished -- because
  neither reader discards queued speech to make room: a higher priority interrupts and then
  the backlog resumes. Once output has been arriving for a second, BlindTerm now drops what
  is queued and says what is being printed now, at a cadence slow enough to be worth hearing.
  The end of a flood is still spoken promptly, and everything remains in the transcript to
  review.
- Count a shell's children with a Windows job object instead of enumerating every process on
  the machine. Deciding who a keystroke belongs to no longer walks the whole process list on
  the window thread.
- Stop copying the entire transcript out of the output control to find where its last line
  starts. That cost grew with the length of the session, on every terminal update; it is now
  a constant-time question asked of the native control in place.
- Coalesce rapid repaints into one output-control update, skip redrawing a full-screen view
  whose rows have not changed, and return early from line news and trigger matching when a
  batch changed nothing.

## v0.7.8 - 2026-08-31

- Speak nothing from a background window, triggers and the bell included. Speech is now
  only for when someone is in the app: the trigger and bell exceptions that used to talk
  over whatever the user went to read are gone, and **Read** &rarr; **Speak output in the
  background** is the only way to hear a background window.

## v0.7.7 - 2026-08-31

- Preserve complete Codex conversations after long inline responses. Codex can move a
  completed response through its visible viewport by repainting rows from the top; BlindTerm
  treated those moved rows as replacements for older transcript lines, so earlier output
  disappeared and a completed turn could leave only its final message. Repainted viewports
  are now aligned with the lines already in scrollback, independent of how ConPTY splits the
  escape sequences into reads.
- Coalesce rapid terminal repaints into one native output-control update every 40 milliseconds.
  Long Codex turns no longer make the wrapping edit control continuously recalculate and send
  accessibility events, keeping the BlindTerm window and NVDA responsive while output streams.

## v0.7.6 - 2026-08-30

- Wrap long output lines instead of hiding what runs off the right edge. Codex, Claude Code
  and OpenCode write a whole paragraph to one terminal row, and BlindTerm joined the wrapped
  rows back into one logical line but left it unwrapped in the output control, so everything
  past the window's edge was invisible and a long response read as truncated. Long lines now
  wrap to the window width, the way they do in any other terminal.

## v0.7.5 - 2026-08-29

- Answer an agent's numbered question with a plain digit. Codex and Claude Code ask questions
  whose answers are numbered -- a model list, a permission level -- and BlindTerm buffered the
  digit in its own edit box, so the answer never reached the picker and the question sat there
  unanswered. A bare unmodified 0 through 9 on an empty command line now goes straight to the
  program when a numbered choice is on screen. A digit inside text remains ordinary prompt
  text, and Alt+1, Alt+2 and Alt+3 stay BlindTerm's output, input and review commands.
- Paste into a full-screen program the way a terminal does. A paste in vim or nano went in as
  the keystrokes that spell Ctrl+V rather than as the pasted text, and a program that had asked
  for bracketed paste -- vim does, to switch auto-indent off for a pasted block -- was never
  told where the paste began and ended. Pasted text now reaches the program whole, wrapped in
  the bracketed-paste markers when the program has asked for them, and raw when it has not.

## v0.7.4 - 2026-08-29

- Send what you type at an agent CLI. Codex counted a whole line arriving in one write as a
  paste, and a Return inside a paste is a newline rather than "send this": typing "test" and
  pressing Enter put the word in Codex's composer, added a blank line under it, and sent
  nothing, forever. BlindTerm now leaves the Return long enough after the line to be
  unmistakably a keypress whenever a program rather than a shell prompt is reading it. A MUD
  is unaffected -- nothing on the far end of a connection guesses at pasting, and commands
  there are still sent at once.
- Start the same program a shell would when two tools share a name. The search for a command
  went extension by extension -- every directory on PATH for a .exe, then every directory for
  a .cmd -- so an unrelated opencode.exe further down PATH beat the opencode shim that every
  shell runs. It printed a usage error and exited before anything could be typed at it. The
  search now goes directory by directory, the way a shell does.
- Run PowerShell scripts. A .ps1 typed by name or by path now runs through PowerShell 7, or
  Windows PowerShell where 7 is not installed, the way .cmd and .bat files already ran through
  cmd.exe. Named by itself it is found even though the stock PATHEXT never mentions .PS1,
  because that is what happens at a PowerShell prompt.

## v0.7.3 - 2026-08-29

- Add a dark mode. BlindTerm's windows can follow Windows' own light or dark setting, or be
  held to one of them, from the new Colours box in settings. Following Windows is the default,
  and a change applies the next time BlindTerm starts.
- Start command line tools that are installed as shims rather than as programs. Everything npm
  installs -- codex, claude and opencode among them -- is a .cmd file with no .exe beside it,
  and CreateProcess cannot run one: opening an agent closed the window with an unhandled "The
  system cannot find the file specified". BlindTerm now does the search a shell would do, and
  runs a shim the way a shell runs one.
- Name the program that could not be found when a command does not exist. "The system cannot
  find the file specified" never said which file, and it is read out rather than looked at.

## v0.7.2 - 2026-08-29

- Move the automatically refreshed MUD directory into its own repository, so its twice-hourly
  generated commits no longer flood BlindTerm's Telegram group while the list stays current.

## v0.7.1 - 2026-08-28

- Stop Left, Right, Home and End from being forwarded to a remote shell at an empty command
  line. A remote shell has no local model picker or effort selector to drive, and answering
  those keys with a bell made BlindTerm announce the prompt as an "Attention" event on every
  press. They are ordinary edit-box caret keys again, exactly as at a local shell prompt.

## v0.7.0 - 2026-08-28

- Connect to an SSH host through Windows OpenSSH, so a remote shell that expects its own
  terminal (such as PuTTY or `ssh.exe`) still runs inside BlindTerm and is read aloud. Choose
  **Terminal** &rarr; **Connect to an SSH host**, or start BlindTerm with `--ssh user@host[:port]`.
- Reorganise the menu bar into **Terminal**, **Edit**, **Tools** and **Help**. Settings, Triggers
  and the Reading commands now live under **Tools**, where they belong.
- Add a **Help** &rarr; **About BlindTerm** page with links to the GitHub repository, a Follow
  button for the author, and a button to join the Serrebi Projects Telegram channel.
- Move **Check for updates** into the **Help** menu, and add a setting to check automatically on
  startup and again on a regular interval, set to once an hour by default.

## v0.6.1 - 2026-08-28

- Run triggers against terminal lines rewritten in place as well as newly appended lines, so
  prompt-prefixed MUD events such as Core MUD's mining progress can send their configured action.
- Make Up and Down in a telnet command line recall BlindTerm's local sent-line history, with
  Enter sending the recalled line again. Telnet and shell histories are separate, and protected
  password input is never remembered.
- Start typing directly from line-mode output: a printable character moves focus to the command
  line and is kept as its first character. Up, Down, Left and Right remain in the output for
  reading and never trigger that focus change.

## v0.6.0 - 2026-08-28

- Browse hundreds of MUDs in one fetch instead of pressing **Load more** for every twenty-five.
  The published directory is already one cached file, so it now hands the whole matching list
  to the browser at once; a live paged source gathers up to two hundred per press.
- Find a known MUD by choosing **By name, A to Z** and typing its first letters in the results
  list. The search follows game names rather than the longer spoken result lines, repeated
  letters cycle between matches, and a short pause starts a new prefix.
- Make the browser's filters describe the list that is actually present. Genres and game types
  include their match counts, empty categories are disabled, changes rebuild the cached list
  without moving focus, codebases are searchable, and **Leave out the ones that are not
  answering** removes listings no directory has reached lately.
- Support both GMCP and native MSDP as accessible structured MUD data. MSDP option 69 is
  negotiated, its reportable variables are discovered, and BlindTerm subscribes only to the
  available room, exits, character and vitals facts. Scalars, arrays and nested tables are
  parsed without letting protocol bytes enter the transcript.
- Turn MSDP room and vitals packets into the same concise, change-only transcript lines and
  `Alt+X`/`Alt+V` answers used for GMCP. Related values are announced together, repeated state
  stays quiet, and abbreviated exits such as `n` and `sw` are spoken as north and southwest.

## v0.5.0 - 2026-08-28

- Speak output only while the window is the one you are in. Reported from a session with
  BlindTerm as the default terminal and TTCom left running in another window: it read its
  status updates out over whatever the user had gone to do, and the only way to stop it was
  to turn output speech off entirely. A screen reader has one voice for the whole desktop, so
  a background window that talks is not informing anyone, it is interrupting them -- and as
  the default terminal there can be several of them at once. Anything already queued is
  dropped the moment the window is left, so leaving a busy terminal is not followed by one
  last sentence over the top.
- The bell and any trigger you wrote are still heard from a background window, because both
  are things you asked to be told about wherever you happen to be. **Read** &rarr; **Speak
  output in the background** puts the old behaviour back for anyone waiting on a build in
  another workspace, and is remembered.
- **Terminal** &rarr; **Browse for MUDs...** (`Alt+Shift+B`), because the browser was
  reachable only as a button inside the connect dialog, and "which MUD" is a different
  question from "what address": somebody who has not got an address has no reason to open a
  dialog that asks for one. Choosing a game there connects to it, taking its encrypted port
  when it publishes one. The button in the connect dialog stays, for filling the fields in
  rather than connecting straight away.

- Browsing needs no account and no API key. A scheduled job in this repository holds one
  MUDVerse key, rebuilds the whole list every half hour and publishes it; BlindTerm downloads
  that file. There is no key in the repository or in the program: MUDVerse issues keys for
  servers and asks that they are not published, so only the answer is. A key stays available,
  optionally, for reading MUDVerse live to the minute.
- Shipping the whole list rather than proxying each query makes every sort and filter instant
  and local, keeps a copy on disk so the browser opens straight away and works offline, and
  means nothing typed into the search box goes to anybody's server.
- Read four directories rather than one, because each has something the others lack, and
  because four sites that can be having a bad day are better than one that must not.
  [Grapevine](https://grapevine.haus) is the only source that states an encrypted port
  outright instead of leaving it to be guessed; its games page serves clean paginated JSON to
  `Accept: application/json`, though its documented API is a WebSocket one needing an account.
  [The Mud Connector](https://mudconnect.com), listing MUDs since 1994, has the most addresses
  by a wide margin -- six hundred and sixty in a single request -- each with a website and a
  connect status it checked while building the page. Its ranking has been inactive since 2021,
  so the rank is kept but nothing sorts by it.
- Fill in gaps rather than overwrite: the richest source goes first and each one after supplies
  only what is still blank, so a game listed everywhere ends up with everybody's half and one
  listed only in the last still ends up connectable. Any source failing makes the list smaller,
  never absent.
- Merge in [MUDStats](https://mudstats.com), which has been sampling player counts for twenty
  years. It brings the thirty-day average, peak, minimum and monthly trend, the year a game
  opened, its codebase and database size, whether it charges to play, and two hundred genres.
  Nothing else publishes the averages.
- Two new orderings that only MUDStats makes possible: **Busiest on average over thirty days**
  and **Highest peak in thirty days**. Both are a different question from "most players online
  now", which only says whether a game is busy at this hour in your timezone. Also **Oldest, by
  the year they opened**.
- Join the two directories on the name, past punctuation and articles. Where two games share a
  name, neither gets the other's figures: a missing statistic goes unread, a wrong one is a lie
  about a real game.
- MUDStats publishes no API, so that half is a scrape and is treated as one. It runs only in
  the scheduled job, never in anybody's client; every field is optional; and if it breaks, the
  list still publishes without the activity figures. `blindterm directory --mudstats-only`
  reports in one line whether it still works.

- Connect to MUDs over TLS. **Secure connection (TLS)** in the connect dialog, an `ssl://`
  address anywhere an address is accepted (`telnet ssl://coremud.org 4022`,
  `--telnet ssl://coremud.org:4022`, a remembered address), and `--tls` in the diagnostic CLI.
  `tls://` and `telnets://` are read as the same thing. Remembered addresses keep the scheme,
  because a MUD offering both puts them on different ports and an address without it comes
  back as the wrong service. The encryption that was actually negotiated is written into the
  transcript when the connection opens, rather than being implied by a checkbox.
- Explain a certificate that does not verify instead of failing with a code. What is wrong
  with it, who issued it, when it expires and its fingerprint in readable groups, then a
  question with **No** as the default. A MUD on a certificate it signed itself is ordinary,
  and that decision belongs to whoever is dialling it.
- **Browse for MUDs...**, in the connect dialog: a directory of MUDs as a list to arrow
  through, ordered by players online, votes this month, reviews, or how recently a game was
  seen, and narrowed by genre, game type, roleplaying policy or a search. Each result reads
  as one line; the details below carry the whole entry. Choosing one fills in the address,
  and ticks the encryption box when the listing publishes an encrypted port. Web-only games
  are left out, being nothing a terminal can open.
- Sort by players online locally, because the directory does not publish that ordering.
  Votes measure who campaigned; players measure who is there. The result is kept for a
  quarter of an hour instead of being re-fetched on every keystroke.
- Read the listings from MUDVerse, over a provider interface (`IMudDirectory`) rather than
  wired in, so a second source is a class rather than a rewrite. **There is no API key inside
  BlindTerm**: MUDVerse issues keys for servers and asks that they are not published, so
  BlindTerm asks for yours once and opens the page where a free one is generated. A
  **Directory address** setting points at a service holding a key on everybody's behalf
  instead, and then no key is needed at this end at all.

## v0.4.3 - 2026-08-28

- Put a complete unfinished prompt into output history as soon as it is spoken. This makes an
  SSH question such as `Do you want to upgrade Ruby? (y/N)` available when moving back to the
  transcript before it has been answered. Its eventual answer or redraw updates the same
  history entry instead of adding a duplicate, and ordinary progress remains live until its
  newline arrives.

## v0.4.2 - 2026-08-28

- Announce Bash questions over SSH when their unfinished prompt ends with a parenthesized answer hint such as `(y/N)` or `(default: no)`.
  Ordinary progress such as
  `Downloading package (1/4)` still waits for its newline instead of being mistaken for a
  question.

## v0.4.1 - 2026-08-27

- Open the selected menu with Down Arrow as soon as the menu bar is activated. The terminal's
  global key routing no longer takes the arrow away first, including while a full-screen
  program is running or pass-through is armed; Enter continues to open it too.

## v0.4.0 - 2026-08-27

- Watch the output for a pattern, and do something when it arrives. **Terminal** -> **Triggers...** (`Alt+Shift+T`) is the list, and `Alt+Shift+G` is the master switch over it. A screen reader reads what a terminal sends in the order it sends it, so the one line that mattered -- the build finishing, the health warning, someone saying your name -- goes past in the middle of forty that did not. A trigger is how that line gets to sound different from the rest, or be the only one that makes a sound at all.
- Match the way the line was written, not the way a programmer writes. A pattern can be plain text anywhere in the line; a wildcard, where `*` is any run of characters and `?` is one, and the whole line has to line up; or a regular expression, matched as written. Whatever a wildcard or capturing group stood for comes back as `$1` onwards, to be dropped into what the trigger says or sends -- so `* arrives from *` can say "$1 from the $2".
- Give every action to one trigger, because the useful ones combine: say something, or say it at once ahead of everything waiting; keep the matching line itself out of the speech; play a sound file; play the system alert; send a line back as though it had been typed; and stop checking the triggers listed after this one, which is how "everything from this channel, except when it mentions me" is written.
- Test a pattern before it has to work. The editor's Try a line box takes a line the way it would arrive and says whether the pattern matches, what each wildcard stood for, and exactly what would be said, played and sent. The editor refuses to save a trigger that could not do anything, and names the control to go back to.
- Keep a trigger from talking to itself. A trigger that sends is the dangerous one: a MUD echoes what it is sent, the echo matches the pattern, and the two ends spend the evening shouting at each other. Twenty firings in two seconds and the trigger is switched off for the session and announced, because a trigger that has stopped for a reason nobody can hear is worse than one that never ran. A wait between firings, in milliseconds, is there for the alarms that would otherwise become drones.
- Order is the user's and it matters, so the list moves up and down and each item is a sentence that says what it watches for and what it does, not a name to open a dialog about. Space turns one on and off, Enter opens it, Delete removes it, and Duplicate is there because the second trigger is usually the first one with one thing changed.

## v0.3.1 - 2026-08-27

- Complete with `Tab` at the shell prompt, not only inside an inline program. This was the
  one place completion did not reach, and it is the place it is used most: `Tab` fell through
  to window tab order instead, which moved the reader off to the transcript and left the typed
  line behind in a box it was no longer standing in -- no completion, and the command
  apparently gone.
- Say what the completion produced, and put it back in the command box. A completed line is
  written to the terminal's unfinished current line, which is only ever spoken when it reads
  as a prompt -- and a command ending in a file name never does -- so `Tab` was silent even
  when it worked. The completed command is now read out once the shell has stopped redrawing
  it, and lands in the edit box where it can be reviewed a character at a time, corrected, and
  sent.
- Keep an accessible agent launch accessible when its line is completed. `claude`, `codex` and
  `opencode` are still started in their linear interfaces when `Tab` hands the line to the
  shell, and a program started from a completed line owns the keyboard immediately rather than
  losing its first keys to the shell.

## v0.3.0 - 2026-08-27

- Read a MUD's own account of the room, its exits and your health, over GMCP. A MUD that
  supports it states the things its text only implies, and BlindTerm now asks for them and
  turns them into plain sentences. `Alt+X` says the room, the area and
  the exits -- as a list, because the MUD sent a list, so "which way can I go" stops meaning
  finding the word "Exits" in a paragraph. `Alt+V` says health and the other pools, naming
  conditions like poison only while they apply.
- Write those sentences into the transcript at the moment they arrive, in square brackets, so
  reading back through a session finds where you were and how you were doing in the right
  place. A line is recorded only when something changed: a MUD repeats this after every
  command. Moving between two rooms that read alike still counts as moving.
- Keep them out of your ear unless you ask. Read, then Speak MUD room and vitals, reads them
  aloud as they arrive; it is off, because hearing your remaining hit points spoken over the
  fight taking them is not an improvement. Read, then MUD room and vitals in the transcript,
  turns the whole thing off.
- Read MSSP, and add Read, then Server information: what the host says about itself -- name,
  uptime, codebase, rooms, areas, website, Discord -- as a page to arrow through.
- Still refuse the options that would put something in the text which text cannot carry: the
  compression options, whose stream this terminal cannot read, and MXP, whose markup is spoken
  mid-sentence by a client that does not render it.

## v0.2.7 - 2026-08-27

- Play the sounds a MUD asks for when it keeps them in folders. A sound name may carry a
  path relative to the sound folder, which BlindTerm refused outright -- so Core MUD's own
  "setsound" test, which asks for "mp3/msptest.mp3", played nothing at all. Names still may
  not leave the sound folder, name a drive, use a backslash, or point a wildcard at a folder.
- Say why a sound was not heard instead of nothing at all: that it is not on this machine and
  downloading is off, that it could not be downloaded, that Windows would not play it, or that
  the MUD named something that is not a sound. Once per reason, not once per trigger.
- Add "Download sounds a MUD offers" to the Read menu, beside "MUD sounds". A MUD keeps its
  sound pack on its own web server, so for anyone without one already unpacked, turning sounds
  on while this stayed off in a settings dialog was turning on silence.

## v0.2.6 - 2026-08-27

- Keep the caret where you put it. Dialling a host, answering a password prompt and opening
  the window no longer move focus off the command line and back, so a screen reader stops
  reading the output pane and the whole field over the top of what you were doing.
- Stop announcing the shell prompt a second time when a connection takes the window over. The
  prompt is read while the cursor is still sitting on it; the transcript line it turns into
  afterwards holds the same words and is no longer read again.
- Stop reading a MUD's prompt back before every reply, for the same reason.
- Read only the new question when a login writes several onto one unfinished line. "Password:"
  after "By what name is your character known?" is announced by itself instead of repeating
  everything already asked and answered on that line.
- Hide password input without recreating the command box. Windows treated the old way of
  switching as destroying the focused control and making another, which readers announce as a
  focus change in the middle of a login.
- Refuse a typed line while a connection is still being dialled, with "Still connecting",
  rather than disabling the command box and handing focus to the output pane.

## v0.2.5 - 2026-08-27

- Keep `telnet host port` in the BlindTerm window where it was typed. The direct telnet
  connection now takes over the current shell window and carries on in the same transcript;
  when the host disconnects, the live shell and its command line return in that window.
- Serialize the shell and network reader threads while the connection takes over or returns.
  Output from the waiting shell cannot be spliced into the MUD conversation, and simultaneous
  terminal updates cannot corrupt the parser or transcript.

## v0.2.4 - 2026-08-27

- Dial `telnet host port` typed at the command line with BlindTerm's own telnet. Windows'
  `telnet.exe` paints a window through the console API rather
  than writing lines, so through a pseudo console every scroll rewrites every row on screen:
  the whole visible screen reads as new output and the last screenful is announced again from
  the top on each line a MUD sends, while anything that went past between two repaints was
  never anywhere to be read. The connection now opens in its own window over a real socket,
  with the accessible terminal-type negotiation, the prompt and password handling, and the MUD
  Sound Protocol that the Terminal menu's connection has always had. The shell it was typed at
  stays at its prompt, and the transcript records where the connection went.
- Leave `telnet.exe` in charge of everything BlindTerm cannot dial for itself: its switches, a
  service name in place of a port, a bare `telnet` and its interactive prompt, and any line the
  shell would act on for itself. A line typed at a MUD, at `ssh`, or at any other running
  program is still that program's to interpret.

## v0.2.3 - 2026-08-27

- Keep remote output as the complete transcript. `Shift+Tab` focuses that full output at the first line of the latest command response, so the newest result is ready to read without making older output unavailable.
- Correct the v0.2.2 behavior that replaced the output document with only the latest response.

## v0.2.2 - 2026-08-27

- Telnet output now shows only the complete response to the latest submitted command during ordinary use. Shift+Tab opens that response, Tab returns to input, and the full session history remains available through Go, Transcript.
- The Telnet diagnostic command accepts `--updates` to report live response boundaries.

## v0.2.1 - 2026-08-27

- Send an unmodified `Tab` from the input field to an active Claude Code, Codex, OpenCode,
  Freebuff, or other inline program for native command, file, and mention completion. Text held
  in BlindTerm's native edit is flushed once before Tab; subsequent typing and editing reach
  the program live until Enter, without duplicating the completed line.
- Keep focus navigation unambiguous around completion. `Shift+Tab` moves from input to readable
  output, and `Tab` in output returns to input. Full-screen programs such as Freebuff use the
  same contract between live input and frozen review output; `Alt+P`, then `Shift+Tab`, remains
  the escape hatch when the program itself needs that chord.
- Stop the raw-capture diagnostic from waiting forever for an animated TUI's output to become
  completely still. Its final settling period is now bounded.

- Rewrite Core MUD's unavoidable opening ASCII logo as readable prose. The server sends that
  logo in the same packet as its first telnet negotiation, before a client can report the MTTS
  screen-reader capability, so BlindTerm now removes only the visual fragments while keeping
  the welcome, setting, connection details, story, version, and login instructions.
- Speak complete prompts that remain on the terminal's unfinished current line, including Core
  MUD's name and character-creation questions. Prompts have no newline and previously appeared
  visually without reaching automatic NVDA or JAWS speech.
- Mark the native command edit as a protected password field while a terminal asks for a
  password, passphrase, passcode, or PIN, preventing screen-reader keyboard echo and braille
  from exposing the secret. Ordinary command entry is restored with the next prompt.
- Accept telnet `CHARSET` negotiation and select UTF-8 when a host offers it. Core MUD offers
  this explicitly, and accepting it keeps non-ASCII text consistent with the UTF-8 capability
  BlindTerm already reports through MTTS.

## v0.2.0 - 2026-08-27

- Send the arrow keys, `Home`, `End`, `Page Up`, `Page Down`, and `Escape` to the running program while the command line is empty. Codex's `/model` list and reasoning level, Claude Code's and OpenCode's pickers, Freebuff's menus, and a MUD's own history are all driven from the command line now instead of moving a caret that has nowhere to go. Type anything into the command line and it is an ordinary edit box again, so a typo in a long prompt can still be corrected.
- Decide whether a program is running by asking whether the shell has started one, rather than by waiting for a shell-integration marker. A stock PowerShell 7 prompt emits no OSC 133 markers at all, so every session treated its first command as still running from then on: Ctrl chords went to a program that had exited long before, and the shell prompt never got its own editing keys back.
- Keep `Ctrl+V` in the command line rather than handing it to the running program. BlindTerm owns the line being typed, so passing paste through removed the only way to get a pasted path into the program. `Alt+C` still sends the interrupt.
- Start Codex in its supported raw scrollback mode, out of the alternate screen, with animations disabled. Interactive pickers such as `/model`, `/permissions`, `/keymap`, `/statusline`, `/theme`, `/usage`, and `/resume` remain linear and selectable instead of interleaving cursor-positioned repaint fragments with the transcript.
- Speak telnet directly instead of running Windows' `telnet.exe`. **Terminal** -> **Connect to a telnet host...** (`Alt+N`) or `BlindTerm.App.exe --telnet host:port` opens a real connection, and `telnet.exe` is no longer in the path at all. It could not be: it repaints its window through the console API rather than writing lines, and a pseudo console can only report what is on that window when it next redraws, so 200 lines sent to it arrive as 30 with the last cut off mid-word. The same 200 arrive whole now.
- Tell a telnet host that a screen reader is reading it. BlindTerm answers the terminal-type question with the MUD convention of a client name, then `ANSI`, then an MTTS bit vector whose bit 64 means SCREEN READER, so a server that honours it drops its room maps and ASCII art unasked. The window width is sent as well, and compression and the out-of-band data channels (MSDP, GMCP, ATCP, MSSP, MXP) are declined so that nothing but text ever reaches the transcript.
- Play MUD sounds, through the MUD Sound Protocol that clients like Portal used. Sound packs go in `%APPDATA%\BlindTerm\sounds` or a folder of your choosing, `Alt+M` turns them off and on, and WAV, MP3, MIDI, WMA, AU and AIFF all play, up to eight at once. The whole protocol is supported: volume, loops, priority, continue, subfolders, and wildcards so that a MUD asking for `hit*.wav` gets one of your hit sounds at random.
- Keep sound triggers out of the text whether or not sounds are switched on. Left in, `!!SOUND(sword.wav)` is a line read aloud as "exclamation exclamation SOUND left paren sword dot wav" in the middle of a fight. Triggers only count at the start of a line, which is also what stops a player typing one into a chat channel from making a noise on your machine, and they are recognised both in the text and inside the telnet option, which is where Core MUD sends its own.
- Never download a sound unless asked to. A trigger's address comes from the server, so fetching is off by default; turned on, it accepts only an ordinary web address and a plain sound file name, writes only inside the sound folder, caps the size, tries each address once, and never overwrites a file you already have.
- Add `blindterm telnet <host[:port]>` to the diagnostic CLI, which runs a connection through the same transcript assembly the window uses and prints the result.
- Build the transcript whether or not anything is listening for updates. A `TerminalCore` with no subscriber quietly assembled nothing, so reading its transcript afterwards returned an empty one with no sign of why.

## v0.1.5 - 2026-08-27

- Keep standard selection, navigation, and clipboard shortcuts local whenever the transcript/output has focus, even while a foreground program is running. This includes `Ctrl+A`, `Ctrl+C`, `Ctrl+Shift+Home`, `Ctrl+Shift+End`, and the usual Shift navigation. Ctrl chords still reach the program when its command input has focus.

## v0.1.4 - 2026-08-27

- Pass Ctrl chords, including `Ctrl+C`, `Ctrl+X`, `Ctrl+Z`, and `Ctrl+V`, to any active foreground program even when it uses inline terminal output. The same keys retain their native copy, cut, undo, and paste behavior after the program exits and the shell prompt returns.

## v0.1.3 - 2026-08-27

- Start simple `claude`, `codex`, and `opencode` commands in the least repainting interface each installed CLI supports. Claude gets its screen-reader renderer, Codex keeps output in inline scrollback with animations disabled, and OpenCode gets its minimal interface without history replay. Freebuff does not currently expose a comparable mode, so it continues through BlindTerm's full-screen speech and review support without unsupported arguments.
- Move every assigned BlindTerm command to an Alt chord. `Alt+1`, `Alt+2`, and `Alt+3` focus the transcript, focus the command line, and freeze or resume full-screen review. Standard `Ctrl+C`, `Ctrl+X`, `Ctrl+Z`, and `Ctrl+V` remain native editing keys instead of sharing BlindTerm's shortcut modifier.

## v0.1.2 - 2026-08-27

- Set `ACCESSIBLE=1` and `TERM_A11Y=1` for shells BlindTerm starts, so command-line tools built with term-a11y, and anything else following the GNOME and Debian convention, render spinners, progress bars and tables as plain text without being configured to. This cannot apply to a console handed over by Windows, because that program was started before BlindTerm was involved.

## v0.1.1 - 2026-08-27

- Make the default terminal setting take effect after BlindTerm is installed over a copy that was being run from somewhere else, or moved. Windows goes on opening the executable it last used, whatever the setting now says, until the registration is replaced rather than edited.

## v0.1.0 - 2026-08-27

- Add a Windows terminal window built for NVDA and JAWS.
- Read ordinary output as a logical transcript in a native edit control.
- Add full-screen mode for nano, vim, htop, and terminal programs over SSH.
- Pass arrows, function keys, modifiers, Tab, Escape, and ordinary typing through to TUI programs.
- Add cursor-following screen speech and a review mode for line, word, character, and braille navigation.
- Add NVDA controller-client speech and braille support.
- Add JAWS COM speech support when JAWS is installed.
- Add ConPTY, UTF-8 handling, shell environment setup, and split text/Return writes.
- Add replayable raw PTY captures and regression coverage for redraws, screen wipes, wrapping, tabs, and alternate screens.
- Add a self-contained Windows publish, ZIP package, Inno Setup installer, and in-app update foundation.
- Add support for being the Windows 11 default terminal, so a command-line program started without one opens in BlindTerm.
- Ask once at startup whether to become the default terminal, with Yes and No buttons and a "Don't ask me again" checkbox that starts ticked.
- Add a Terminal menu item that turns the default-terminal setting on and off and shows which is in effect.
- Bring a window opened for a handed-over console to the front and put the caret on the command line.
- Add `--set-default-terminal` and `--reset-default-terminal`, so the setting can be changed without a terminal to type into.
- Speak output as soon as it stops arriving instead of after a fixed quarter-second, so an answer to a typed command is no longer noticeably late. A burst of lines is still gathered into one utterance, and long-running output is still spoken as it goes.
- Stop reading the whole transcript twice for every batch of output, which was most of BlindTerm's processor time and enough garbage to make the screen reader itself feel slow.
- Ask which screen reader is running at most once every two seconds rather than once per line whenever none is, which mattered most while a reader was restarting.
- Stop sending output to a screen reader that has just refused an utterance until it is worth asking again.
