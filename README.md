# BlindTerm

A screen-reader-friendly Windows terminal for NVDA and JAWS, built for ordinary shell work and the full-screen programs that usually make terminals inaccessible.

[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

BlindTerm keeps ordinary output as a readable transcript in a native Windows edit control. NVDA and JAWS can navigate it by line, word, and character, follow it in braille, and use their normal reading commands. When nano, vim, htop, less, or an editor over SSH takes the alternate screen, BlindTerm changes mode: keys go to the program and speech follows the cursor instead of reading every repaint.

## Features

- Runs Windows shells through ConPTY, with UTF-8 and xterm-compatible terminal behavior.
- Presents shell output as logical lines instead of a guessed terminal grid.
- Keeps unfinished output in a separately labelled current-line control, and records a
  complete prompt in the transcript as soon as it is spoken so it can be reviewed before it
  is answered.
- Sends ordinary typed commands through a real command-line edit control with history.
- Passes arrows, Tab, Escape, function keys, modifiers, and typing through to full-screen programs.
- Reads the line the cursor moved onto instead of repeating status bars and screen furniture.
- Freezes a full-screen frame for normal line, word, character, say-all, and braille navigation.
- Speaks through NVDA's controller client and JAWS' documented COM interface, using the reader's own voice and settings.
- Speaks only the window you are actually in, so a program left running in another terminal does not read itself out over whatever you went to do.
- Handles redraws, spinners, wrapped lines, screen wipes, alternate screens, and split UTF-8 input.
- Can be set as the Windows 11 default terminal, so command-line programs open in BlindTerm on their own.
- Tells the shells it starts that they are being read, so tools that can render plainly do: `ACCESSIBLE`, `TERM_A11Y`, and the flags GitHub CLI and Claude Code already understand.
- Starts simple Codex, Claude Code, and OpenCode commands in the least repainting interface each CLI provides.
- Sends the arrow keys, Escape and the Ctrl chords to whatever the shell is running, so an agent's model picker, level adjustment and menus can all be driven from the command line.
- Speaks telnet itself, so a MUD loses none of its output and is told that a screen reader is reading it — including when the connection was asked for by typing `telnet host port` at the command line.
- Connects over TLS to the MUDs that offer it, and explains a certificate it cannot verify instead of failing with a code.
- Connects to an SSH host through Windows OpenSSH, so a remote shell that expects its own terminal runs inside BlindTerm and is read aloud.
- Browses a directory of MUDs by genre, players online, thirty-day average, rating or name, so finding one to play does not mean reading a website full of banners and vote buttons.
- Needs no account and no API key to do it: the list is published for everyone and rebuilt every half hour.
- Plays MUD sounds through the MUD Sound Protocol, and keeps its triggers out of the text whether sounds are on or off.
- Reads a MUD's own account of the room, its exits and the character's health over GMCP or MSDP, so the way out is a list rather than a word to be found in a paragraph.
- Includes a replay harness that turns raw PTY captures into repeatable regression tests.
- Includes a self-contained Windows build, an Inno Setup installer, and a hash-verified update foundation.

## Download and install

Download the installer from the [latest release](https://github.com/serrebidev/BlindTerm/releases/latest) and run it, or download the ZIP and unpack it anywhere.

To build and install from source on Windows instead:

```powershell
.\build.bat install
```

The installer places BlindTerm in `C:\Program Files\BlindTerm` and adds a Start Menu entry. It leaves settings in `%APPDATA%\BlindTerm` so an application update does not remove them.

To make a package without installing it:

```powershell
.\build.bat build
```

The ZIP, installer, and update manifest are written to `dist\`.

## Run from source

Install the .NET 9 SDK, then run:

```powershell
dotnet run --project src\BlindTerm.App
```

BlindTerm prefers PowerShell 7 when `pwsh.exe` is available and falls back to Windows PowerShell. To launch another program directly:

```powershell
dotnet run --project src\BlindTerm.App -- wsl.exe
```

### Coding-agent interfaces

When a command line consists of a simple `codex`, `claude`, or `opencode` launch, BlindTerm selects that program's accessible interface automatically:

- Claude Code receives `--ax-screen-reader`, producing labelled, flat text without decorative borders or animations.
- Codex receives `--no-alt-screen -c tui.raw_output_mode=true -c tui.animations=false`, using its copy-friendly scrollback renderer, keeping it out of the alternate screen, and stopping visual animations. This keeps `/model`, `/permissions`, `/keymap`, and the other interactive pickers readable as plain numbered text while retaining direct text selection. Codex does not currently provide a dedicated screen-reader renderer.
- OpenCode receives `--mini --no-replay`, selecting its smaller interactive interface and preventing old sessions from repainting on startup and resize.
- Freebuff currently provides no screen-reader or minimal-interface switch. BlindTerm invents no unsupported argument for it, so Freebuff uses the terminal's full-screen speech and frozen review support.

While one of these agents is running locally or over BlindTerm's SSH connection, a plain `0`
through `9` from either the number row or numpad is sent immediately to a numbered question
when the command line is empty. A number inside text remains ordinary editable prompt text.
`Alt+1`, `Alt+2`, and `Alt+3` remain BlindTerm's output, input, and review commands.

Explicit flags are respected, and subcommands such as `codex exec` or `opencode run` keep working. Compound shell expressions and executable paths are left exactly as typed rather than guessed at.

## Making BlindTerm your default terminal

Windows 11 lets you choose which terminal opens when a command-line program is started without one, so that `cmd.exe` from the Run dialog, a `.bat` file from File Explorer, or a tool that launches a console all open in BlindTerm.

BlindTerm offers this once, in a dialog at startup, with Yes and No buttons and a **Don't ask me again** checkbox that is already ticked. Answering either way is final unless you clear the checkbox. You can change your mind at any time from **Terminal** &rarr; **Use BlindTerm as the default terminal**, which shows a check mark when it is on.

A window opened this way brings itself to the front and puts the caret on the command line, because a terminal that appears in the background is a terminal a screen reader never mentions.

One thing does not carry over. BlindTerm normally tells the shell it starts that it is being read, by setting `ACCESSIBLE`, `TERM_A11Y` and the flags GitHub CLI and Claude Code understand. A handed-over console cannot be told: Windows started that program before BlindTerm was involved, and a running process's environment is not ours to change. Everything else -- the transcript, screen mode, speech, the reading commands -- works the same either way. If you want those variables in a default-terminal session, set them for your account and every program will inherit them.

This needs Windows 11 and Windows Terminal installed. BlindTerm uses Windows Terminal's console host, which is the Microsoft-signed component that receives the console from Windows and hands it on; BlindTerm does not redistribute a copy of it. Nothing here needs administrator rights, and nothing is written outside your own user account.

If a terminal ever fails to open, you can put the setting back without a working terminal to type into:

```
BlindTerm.App.exe --reset-default-terminal
```

That restores whatever Windows would have chosen. In fact BlindTerm cannot lock you out: if the handoff fails for any reason, Windows falls back to the console host and the program still gets a terminal.

## Reading and keyboard commands

**Output is spoken only while the window is the one you are in.** A screen reader has one voice for the whole desktop, so a terminal that carries on talking after you have switched to something else is not telling you anything — it is talking over whatever you went to read. This matters most when BlindTerm is your default terminal and something long-running is sitting in a window nobody is looking at. Two things are still heard from a background window, because both are things you asked to be told about wherever you are: the bell, and any trigger you wrote. If you do want the rest of it — waiting on a build in another workspace, say — **Read** &rarr; **Speak output in the background** turns it back on and is remembered.

BlindTerm keeps its own commands under Alt. With the command line focused while a foreground command or application is running, Ctrl chords—including `Ctrl+C`, `Ctrl+X`, and `Ctrl+Z`—go directly to that program whether it uses inline output or a full-screen interface. `Ctrl+V` is the exception and always pastes into the command line, because BlindTerm owns the line being typed and that is the only way to get a pasted path into the program at all. `Alt+C` sends the interrupt when `Ctrl+C` is wanted for something else. After the program exits and the shell prompt returns, those keys resume their ordinary copy, cut, undo, and paste behavior in BlindTerm's native controls.

In a full-screen program a paste (`Ctrl+V` or `Shift+Insert`) goes to the program as pasted text, wrapped in bracketed-paste markers when the program has asked for them — vim does, to switch auto-indent off for a pasted block — so a block lands as one chunk instead of as keys to be re-indented one at a time.

### Driving a program from the command line

Codex, Claude Code, OpenCode and Freebuff all ask questions no line of text can answer: a model list chosen with Up and Down, a reasoning level adjusted with Left and Right, a picker dismissed with Escape.

**While a program is running, an empty command line is a remote control for it.** Up, Down, Left, Right, Home, End, Page Up, Page Down and Escape are sent straight to the program, and you hear what it does rather than a caret that has nowhere to go.

**As soon as there is text in the command line, it is an ordinary edit box again.** Arrows move the caret through what you have typed, so a typo in a long prompt can still be fixed. Clear the line to get the remote control back, or use `Alt+P` to pass one key either way.

Telnet keeps its own sent-line history in BlindTerm: Up and Down in the command line recall what you sent, and Enter sends the recalled line again. This history is separate from shell commands and never records protected password input.

**Tab asks whatever is reading the line to complete what you typed.** That is the shell's own completion at an idle prompt -- file names, commands, parameters -- and the program's own completion in Claude Code, Codex, OpenCode and similar inline programs. BlindTerm sends the pending edit text followed by Tab, reads the completed command aloud once the terminal has stopped redrawing it, and puts it back in the command box, where it can be reviewed a character at a time, corrected and sent. New typing and editing stay synchronized with the terminal until Enter, and its rendered line is authoritative after completion. Press `Shift+Tab` to move from input to the readable output, and press `Tab` in output to return to input. Freebuff and other full-screen programs use the same focus pair between live input and frozen review output. If a program itself needs `Shift+Tab`, press `Alt+P` first or choose **Terminal** &rarr; **Send Shift+Tab**.

Whether a program is running is decided by whether the shell has actually started one, so this turns itself on when you launch `codex` and off again the moment it exits—no shell configuration, and it works the same in PowerShell, `cmd.exe` and a handed-over console.

Focus decides which surface owns editing keys. In the transcript/output, standard Windows selection and clipboard commands remain local even while a program runs: `Ctrl+A`, `Ctrl+C`, `Ctrl+Shift+Home`, `Ctrl+Shift+End`, Shift with arrows or page keys, and the rest of the native edit-control behavior work with NVDA's system caret. Up, Down, Left and Right stay in the output for reading. Typing a printable character there moves to the command line and keeps that first character, so starting the next command does not require a separate focus shortcut. Move to the command line explicitly with `Alt+2` when Ctrl chords should go to the program; return to output with `Alt+1` when you want to read, select, or copy.

For a telnet session, output remains the complete transcript. BlindTerm records where each submitted command's response begins. `Shift+Tab` moves from the command line into that full output field with its caret at the first line of the latest response; earlier rooms and the login history remain immediately available by moving upward. `Tab` returns to input, and `Alt+1` moves to the end of the full transcript.

- `Alt+1`: focus the transcript.
- `Alt+2`: focus the command line.
- `Alt+3`: freeze or resume full-screen review.
- `Alt+End`: move to the end of the transcript.
- `Alt+L`: speak the current line.
- `Alt+W`: speak the visible screen.
- `Alt+S`: turn automatic output speech on or off.
- `Alt+M`: turn MUD sounds on or off.
- `Alt+X`: speak the room and its exits.
- `Alt+V`: speak health and the other pools.
- `Alt+C`: send Ctrl+C to interrupt the program.
- `Alt+[`: send Escape.
- `Alt+P`: pass the next supported key to the program, including an Alt chord.
- `Alt+A`: copy the transcript or current screen.
- `Alt+O`: copy the current command's output.
- `Alt+Up` and `Alt+Down`: move through command output blocks.
- `Alt+D`: change directory.
- `Alt+N`: connect to a telnet host.
- `Alt+Shift+B`: browse for MUDs.

The complete command list is also available from the menu bar. `Alt+T`, `Alt+R`, `Alt+G`, and `Alt+E` open its Terminal, Read, Go, and Edit menus, including while a full-screen program is running.

## Telnet and MUDs

BlindTerm dials telnet hosts itself. **Terminal** &rarr; **Connect to a telnet host...** (`Alt+N`) asks for a host and port, remembers the addresses you have used, and opens the connection in its own window. If you do not have an address in mind, **Terminal** &rarr; **Browse for MUDs...** (`Alt+Shift+B`) finds one; the same browser is also a button inside the connect dialog, for filling the fields in rather than connecting straight away. From a shortcut or a script:

```
BlindTerm.App.exe --telnet coremud.org:4000
BlindTerm.App.exe --telnet ssl://coremud.org:4022
```

Typing `telnet coremud.org 4000` at the command line dials the same kind of connection without opening another window. A plain `telnet host`, `telnet host port` or `telnet host:port` takes over the current BlindTerm window instead of being handed to Windows' `telnet.exe`; the transcript carries on into the conversation, and the live shell returns in that window when the host disconnects. Anything `telnet.exe` understands and BlindTerm does not — its switches, its service names, a bare `telnet` and its interactive prompt — still runs `telnet.exe`, as does a line the shell would act on for itself, such as one with a pipe or a redirection in it.

It is a real connection rather than a wrapper around Windows' `telnet.exe`, and that is not a detail. `telnet.exe` repaints its window through the console API instead of writing a stream of lines, and a Windows pseudo console can only report what is on that window when it next redraws. Anything that scrolls past in between is overwritten before it can be reported: sending 200 lines to it and reading the transcript gives back 30, with the last one cut off mid-word. Those lines reach no terminal at all, whichever one you use. Reading the socket directly gives back all 200.

Two other things follow from speaking the protocol rather than driving a program that speaks it:

- **The host is told a screen reader is in use.** When it asks what terminal this is, BlindTerm answers with the MUD convention of a client name, then `ANSI`, then an MTTS bit vector — and bit 64 of MTTS means SCREEN READER. A server that honours it drops its room maps and ASCII art without anyone having to find the setting. The window width is sent too, so text wraps to the width being read.
- **Nothing but readable text reaches the transcript.** GMCP, MSDP and MSSP are accepted, removed from the wire text, and exposed as accessible room, character and server facts. Compression and markup-oriented channels such as MXP are declined, so protocol bytes or tags never land in the middle of a sentence a screen reader is speaking.

Core MUD sends its opening ASCII logo before any client has time to answer that negotiation. BlindTerm recognizes that one unavoidable opening and rewrites it as ordinary prose, preserving the welcome, setting, story, server version, and login instructions without making NVDA read rows of dots, slashes, and bars. Later text remains byte-for-byte server output. BlindTerm also accepts a host's UTF-8 character-set offer, automatically speaks complete prompts that do not end in a newline, and changes the command line into a protected password field while a password, passphrase, passcode, or PIN is requested.

Typing works as it does anywhere else in BlindTerm. Up and Down recall lines sent during the telnet session, so Enter can send one again; Left and Right edit the recalled line. `Escape`, `Ctrl+]`, and other terminal controls can still reach the host. Nothing is sent on connect, so a plain TCP service — a mail server, a web server — can still be poked at the way people use a telnet client for.

### Encrypted connections

Telnet is a protocol from before anyone encrypted anything, and a MUD login sends a password down it in the clear. A growing number of MUDs now offer TLS as well, on a second port beside the plain one — Core MUD's is `4022` against `4000`. Tick **Secure connection (TLS)** in the connect dialog, or write the address the way every other client spells it:

```
telnet ssl://coremud.org 4022
BlindTerm.App.exe --telnet ssl://coremud.org:4022
```

`ssl://`, `tls://` and `telnets://` all mean the same thing and are all accepted. It is the same TLS a browser speaks, wrapped under telnet rather than under HTTP, so the option negotiation, the login name and the password are all inside it. Remembered addresses keep the scheme, because a MUD that offers both has a different port for each and an address without it comes back as the wrong service.

Certificates are verified. When one does not verify, BlindTerm says what is wrong with it — self-signed, expired, issued to another name — along with who issued it, when it expires and its fingerprint in readable groups, and asks whether to connect anyway. A MUD running on a certificate it signed itself is ordinary rather than sinister, and answering that is yours to do; the default is no. Whichever encryption was actually negotiated is written into the transcript when the connection opens, so *"Connected to coremud.org over TLS 1.3"* is a line to read back rather than a claim from a checkbox.

### Finding a MUD

**Browse for MUDs...**, in the connect dialog, is a directory of MUDs as a list to arrow through. Nothing is asked of you first — no account, no API key, no signing up anywhere.

Choose what to order it by:

- **Most players online now** — who is there this minute.
- **Busiest on average over thirty days** — who has people in it. These are different questions, and the second is the honest answer to "is anybody actually playing this": a count taken now only tells you whether a game is busy at this hour in your timezone.
- **Highest peak in thirty days**, **Top voted this month**, **Most reviewed**, **Recently online**, **Recently updated**, **Newest listings**, and **Oldest, by the year they opened** — some of these opened in 1989.

Narrow it by genre, game type or roleplaying policy, or type words into **Search**; every word has to match, so two words narrow the list rather than widening it. Each result reads as one line: *"Alter Aeon. 41 players. Fantasy. rated 4.6 from 12 reviews."* **Details** below carries the whole entry — address, the month's figures, codebase, year opened, website. Choosing one fills in the host and port, and ticks **Secure connection** when the listing publishes an encrypted port.

Only games with a real host and port are listed, because a game that is played on a web page is not something a terminal can open.

#### Where the list comes from

Four directories, merged, because each is good at something the others are not:

- **[MUDVerse](https://www.mudverse.com)** has the genre and game type, the roleplaying policy, and what players voted and reviewed.
- **[Grapevine](https://grapevine.haus)** has the cleanest connection data of any of them, and is the only one that states an **encrypted port** outright rather than leaving it to be guessed. Also taglines, homepages and Discord invites.
- **[The Mud Connector](https://mudconnect.com)**, listing MUDs since 1994, has the most addresses by a wide margin — six hundred and sixty in one request — each with a website and a connect status it checked while building the page.
- **[MUDStats](https://mudstats.com)** has been sampling player counts for twenty years and knows how busy games actually *are*: thirty-day average, peak and minimum, this month's trend, the year a game opened, its codebase, its database size, whether it charges to play, and over two hundred genres down to *Dresden Files* and *ARPANet Simulation*. Nothing else publishes the averages, and they are the most useful thing here.

They are joined on the name, with the punctuation and the articles taken out. The richest source goes first and each one after fills in only what is still blank, so nobody overwrites anybody: a game listed in all four ends up with all four halves, and one listed only in the last still ends up connectable. Where two games share a name, neither gets the other's figures — a missing statistic is a line that goes unread, a wrong one is a lie about a real game.

Four sources also means four sites that can be having a bad day, so any of them failing makes the list smaller rather than absent. That is not theoretical: MUDVerse's API cannot currently be paged past its third page, and several of its orderings time out on the first, which is precisely why the other three are there.

**You need no key for any of this.** A scheduled job in this repository holds one MUDVerse key, reads both directories every half hour, and publishes a single file that BlindTerm downloads. That arrangement exists because MUDVerse issues keys for servers and asks that they are not published — BlindTerm's source *is* published, so a key compiled in here would be a key handed to everyone who downloads it. There is no key in this repository either: it is a repository secret, and only the answer is published.

Publishing the whole list rather than proxying each query is also the better shape for a screen reader. Every sort and filter is instant and local, the copy is kept on disk so the browser opens straight away and works on a train, and nothing you type into the search box goes to anybody's server.

**MUDVerse key...**, in the browser, is optional and for one thing: reading MUDVerse live, so counts are current to the minute rather than to the half hour. **Directory address** beside it points BlindTerm at a different published list. The provider is an interface rather than either site wired in — `IMudDirectory` in `BlindTerm.Core.Mud`, normalising into one `MudGame` — so a third source is a class, not a rewrite.

A note on the sources without an API. MUDStats publishes none, so that half reads the endpoint its own browse page reads; The Mud Connector publishes none either, so its Big List is parsed as the single table it is; and Grapevine documents only a WebSocket API needing an account, but its games page answers to `Accept: application/json` with a clean paginated list. All three are read on sufferance and treated accordingly: only in the scheduled job, so each site sees one visitor twice an hour rather than one per user per keystroke; every field optional, so a moved column loses one figure rather than throwing; and any of them failing leaves the rest to publish. `blindterm directory --mudstats-only` says in one line whether the MUDStats half still works.

### MUD sounds

BlindTerm speaks the MUD Sound Protocol, the way clients like Portal did: a MUD asks for a sound and BlindTerm plays it.

**Sound packs go in `%APPDATA%\BlindTerm\sounds`**, or wherever you point **Sound folder** in **Terminal** &rarr; **Settings**. A MUD's `T=` parameter names a subfolder, so a pack that arrives with a `combat` folder in it should be unpacked keeping that shape. `Alt+M` turns sounds off and on, and **Sound volume** scales everything the MUD asks for. WAV, MP3, MIDI, WMA, AU and AIFF all play, several at once.

The whole protocol is here: `V` volume, `L` loops (`-1` for as long as nothing stops it), `P` priority, `C` continue, `T` subfolder, `U` address, `!!SOUND(Off)` and `!!MUSIC(Off)`, and wildcards — a MUD asking for `hit*.wav` gets one of your hit sounds at random. Music plays one at a time and is left alone rather than restarted when the MUD names the same piece again. Eight sound effects play at once; past that, a trigger only interrupts something already playing if it arrives with a higher priority.

Triggers are lifted out of the text **whether or not sounds are switched on**. That is the part that matters even to someone who never turns them on: left in, a trigger is a line read aloud as "exclamation exclamation SOUND left paren sword dot wav" in the middle of a fight. They are only recognised at the start of a line, which is also what stops a player typing `!!SOUND(scream.wav)` into a chat channel from making a noise on your machine — what the MUD echoes of that arrives after a name and a colon, and stays ordinary text.

Some MUDs put their triggers in the text and some send them out of band, inside the telnet option, so that clients which do not speak the protocol never see them. Core MUD does the second. Both work.

**Sounds are never downloaded unless you ask.** A trigger's `U` parameter is an address chosen by the server, so **Download sounds a MUD offers** is off by default. Turned on, the rules are narrow: an ordinary web address only, a plain sound file name with a playable extension, a destination inside your sound folder and nowhere else, a size cap, one attempt per address, and a file you already have is never overwritten.

### What the MUD says about itself

A MUD that speaks GMCP or MSDP states the things its text only implies. BlindTerm negotiates either protocol, asks for the room and character facts worth having, and turns them into plain sentences. With MSDP it first discovers what that particular server can report, then subscribes only to the relevant supported variables:

- **`Alt+X`** &rarr; *"Apartment of Karia, South Dome. Exits: north."* The exits are a list because the MUD sent a list. Answering "which way can I go" stops meaning finding the word `Exits` somewhere in a paragraph and reading to the end of the line.
- **`Alt+V`** &rarr; *"HP 240 of 280. SP 154 of 154. Poison venom."* Conditions are named only while they apply.

The same sentences go into the transcript **at the moment they arrive**, in square brackets, so reading back through a session finds where you were and how you were doing in the right place rather than at the end. A MUD repeats these constantly &mdash; Core MUD sends the character's vitals after every command &mdash; so a line is recorded only when something actually changed. Moving between two rooms that read alike still counts as moving, because where a MUD gives a room an identity that is what decides it.

They are not read out as they arrive unless you ask for that: hearing your remaining hit points spoken over the fight taking them is not an improvement. **Read** &rarr; **Speak MUD room and vitals** turns that on, and **MUD room and vitals in the transcript** turns the whole thing off.

**Read** &rarr; **Server information** shows what the host said about itself over MSSP &mdash; name, uptime, codebase, room and area counts, website, Discord &mdash; as a page to arrow through rather than a recital.

BlindTerm still refuses the options that would put something in the text which text cannot carry: the compression options, whose stream this terminal cannot read, and MXP, whose markup ends up spoken mid-sentence by a client that does not render it.

## Diagnostic CLI

The core can be exercised without opening a window:

```powershell
dotnet run --project src\BlindTerm.Cli -- capture --out session.raw --send "Get-ChildItem" --send "exit" -- pwsh.exe -NoLogo
dotnet run --project src\BlindTerm.Cli -- replay session.raw --numbered
dotnet run --project src\BlindTerm.Cli -- speak --probe
dotnet run --project src\BlindTerm.Cli -- telnet coremud.org:4000 --seconds 8 --numbered
dotnet run --project src\BlindTerm.Cli -- telnet ssl://coremud.org:4022 --seconds 8
dotnet run --project src\BlindTerm.Cli -- directory --mudstats-only
```

The `directory` verb builds the list of MUDs described above. `--mudstats-only` reads MUDStats and reports what came back: it needs no key, changes nothing, and exits non-zero when the scrape has stopped working. That is the command to run when the activity figures go missing.

The `telnet` verb runs a real connection through the same transcript assembly the window uses, and prints what it produced. That is how "nothing is lost" is checked: point it at a host that will send more lines than the terminal is tall and count them. Add `--updates` to print each response batch as it arrives. An `ssl://` address, or `--tls`, encrypts it and reports which version was negotiated; `--insecure` also accepts a certificate that does not verify, which the window asks about instead.

Replay tests deliberately feed captures at 16384, 7, and 1 byte chunks. Escape sequences split across reads are not an edge case in a real PTY; they are the test.

## Testing

```powershell
dotnet test
pwsh -File tests\run-replay-tests.ps1
```

The test suite covers the VT engine, transcript assembly, screen speech, key translation and encoding, GMCP and MSDP negotiation and parsing, ConPTY captures, screen wipes, redraws, wrapping, alternate-screen programs, and the whole default-terminal path: the registry values Windows parses, the marshalling registration, the COM wrappers, and a complete inbound console handoff driven with real pipes and process handles. SSH captures are included as development corpus and are replayed by hand because their login banners and disconnect text are host-specific.

## Updating

The app contains an original update client designed for GitHub release manifests. It downloads `BlindTerm-update.json`, compares semantic versions, verifies the SHA-256 of the package, and hands the replacement to a short-lived update worker so the running executable is never overwritten in place.

Releases are published on GitHub, and the manifest format is documented in [`docs/BUILD.md`](docs/BUILD.md).

## Documentation

- [`docs/DESIGN.md`](docs/DESIGN.md): architecture and accessibility decisions.
- [`docs/BUILD.md`](docs/BUILD.md): local build, package, installer, and future release workflow.
- [`tests/captures/SSH-CAPTURES.md`](tests/captures/SSH-CAPTURES.md): the real SSH capture corpus.
- [`CHANGELOG.md`](CHANGELOG.md): readable release history.

## Contributing

Pull requests and accessibility testing are welcome. If something reads incorrectly under NVDA or JAWS, include the exact keystrokes and, when possible, the reader's speech or debug log. A short capture is much more useful than a description of a terminal repaint that happened once.

## License

BlindTerm is under the [MIT license](LICENSE). The bundled NVDA controller client remains under its own LGPL license; its license travels in `native\nvdaControllerClient-LICENSE.txt`.
