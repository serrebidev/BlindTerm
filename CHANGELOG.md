# Changelog

Readable release history for BlindTerm. This starts with the first build
that was complete enough to install and use, rather than pretending the
earlier prototypes were something anyone could have run.

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
