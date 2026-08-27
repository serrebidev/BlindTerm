# BlindTerm

A screen-reader-friendly Windows terminal for NVDA and JAWS, built for ordinary shell work and the full-screen programs that usually make terminals inaccessible.

[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

BlindTerm keeps ordinary output as a readable transcript in a native Windows edit control. NVDA and JAWS can navigate it by line, word, and character, follow it in braille, and use their normal reading commands. When nano, vim, htop, less, or an editor over SSH takes the alternate screen, BlindTerm changes mode: keys go to the program and speech follows the cursor instead of reading every repaint.

## Features

- Runs Windows shells through ConPTY, with UTF-8 and xterm-compatible terminal behavior.
- Presents shell output as logical lines instead of a guessed terminal grid.
- Keeps prompts and unfinished output in a separately labelled current-line control.
- Sends ordinary typed commands through a real command-line edit control with history.
- Passes arrows, Tab, Escape, function keys, modifiers, and typing through to full-screen programs.
- Reads the line the cursor moved onto instead of repeating status bars and screen furniture.
- Freezes a full-screen frame for normal line, word, character, say-all, and braille navigation.
- Speaks through NVDA's controller client and JAWS' documented COM interface, using the reader's own voice and settings.
- Handles redraws, spinners, wrapped lines, screen wipes, alternate screens, and split UTF-8 input.
- Can be set as the Windows 11 default terminal, so command-line programs open in BlindTerm on their own.
- Tells the shells it starts that they are being read, so tools that can render plainly do: `ACCESSIBLE`, `TERM_A11Y`, and the flags GitHub CLI and Claude Code already understand.
- Starts simple Codex, Claude Code, and OpenCode commands in the least repainting interface each CLI provides.
- Sends the arrow keys, Escape and the Ctrl chords to whatever the shell is running, so an agent's model picker, level adjustment and menus can all be driven from the command line.
- Speaks telnet itself, so a MUD loses none of its output and is told that a screen reader is reading it.
- Plays MUD sounds through the MUD Sound Protocol, and keeps its triggers out of the text whether sounds are on or off.
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

BlindTerm keeps its own commands under Alt. With the command line focused while a foreground command or application is running, Ctrl chords—including `Ctrl+C`, `Ctrl+X`, and `Ctrl+Z`—go directly to that program whether it uses inline output or a full-screen interface. `Ctrl+V` is the exception and always pastes into the command line, because BlindTerm owns the line being typed and that is the only way to get a pasted path into the program at all. `Alt+C` sends the interrupt when `Ctrl+C` is wanted for something else. After the program exits and the shell prompt returns, those keys resume their ordinary copy, cut, undo, and paste behavior in BlindTerm's native controls.

### Driving a program from the command line

Codex, Claude Code, OpenCode, Freebuff and a MUD over telnet all ask questions no line of text can answer: a model list chosen with Up and Down, a reasoning level adjusted with Left and Right, a picker dismissed with Escape.

**While a program is running, an empty command line is a remote control for it.** Up, Down, Left, Right, Home, End, Page Up, Page Down and Escape are sent straight to the program, and you hear what it does rather than a caret that has nowhere to go.

**As soon as there is text in the command line, it is an ordinary edit box again.** Arrows move the caret through what you have typed, so a typo in a long prompt can still be fixed. Clear the line to get the remote control back, or use `Alt+P` to pass one key either way.

**Tab asks the running program to complete what you typed.** In Claude Code, Codex, OpenCode, and similar inline programs, BlindTerm sends the pending edit text followed by Tab, then keeps new typing and editing synchronized with the program until Enter. The program's rendered line is authoritative after completion. Press `Shift+Tab` to move from input to the readable output, and press `Tab` in output to return to input. Freebuff and other full-screen programs use the same focus pair between live input and frozen review output. If a program itself needs `Shift+Tab`, press `Alt+P` first or choose **Terminal** &rarr; **Send Shift+Tab**.

Whether a program is running is decided by whether the shell has actually started one, so this turns itself on when you launch `codex` and off again the moment it exits—no shell configuration, and it works the same in PowerShell, `cmd.exe` and a handed-over console.

Focus decides which surface owns editing keys. In the transcript/output, standard Windows selection and clipboard commands remain local even while a program runs: `Ctrl+A`, `Ctrl+C`, `Ctrl+Shift+Home`, `Ctrl+Shift+End`, Shift with arrows or page keys, and the rest of the native edit-control behavior work with NVDA's system caret. Move to the command line with `Alt+2` when Ctrl chords should go to the program; return to output with `Alt+1` when you want to read, select, or copy.

For a telnet session, output remains the complete transcript. BlindTerm records where each submitted command's response begins. `Shift+Tab` moves from the command line into that full output field with its caret at the first line of the latest response; earlier rooms and the login history remain immediately available by moving upward. `Tab` returns to input, and `Alt+1` moves to the end of the full transcript.

- `Alt+1`: focus the transcript.
- `Alt+2`: focus the command line.
- `Alt+3`: freeze or resume full-screen review.
- `Alt+End`: move to the end of the transcript.
- `Alt+L`: speak the current line.
- `Alt+W`: speak the visible screen.
- `Alt+S`: turn automatic output speech on or off.
- `Alt+M`: turn MUD sounds on or off.
- `Alt+C`: send Ctrl+C to interrupt the program.
- `Alt+[`: send Escape.
- `Alt+P`: pass the next supported key to the program, including an Alt chord.
- `Alt+A`: copy the transcript or current screen.
- `Alt+O`: copy the current command's output.
- `Alt+Up` and `Alt+Down`: move through command output blocks.
- `Alt+D`: change directory.
- `Alt+N`: connect to a telnet host.

The complete command list is also available from the menu bar. `Alt+T`, `Alt+R`, `Alt+G`, and `Alt+E` open its Terminal, Read, Go, and Edit menus, including while a full-screen program is running.

## Telnet and MUDs

BlindTerm dials telnet hosts itself. **Terminal** &rarr; **Connect to a telnet host...** (`Alt+N`) asks for a host and port, remembers the addresses you have used, and opens the connection in its own window. From a shortcut or a script:

```
BlindTerm.App.exe --telnet coremud.org:4000
```

It is a real connection rather than a wrapper around Windows' `telnet.exe`, and that is not a detail. `telnet.exe` repaints its window through the console API instead of writing a stream of lines, and a Windows pseudo console can only report what is on that window when it next redraws. Anything that scrolls past in between is overwritten before it can be reported: sending 200 lines to it and reading the transcript gives back 30, with the last one cut off mid-word. Those lines reach no terminal at all, whichever one you use. Reading the socket directly gives back all 200.

Two other things follow from speaking the protocol rather than driving a program that speaks it:

- **The host is told a screen reader is in use.** When it asks what terminal this is, BlindTerm answers with the MUD convention of a client name, then `ANSI`, then an MTTS bit vector — and bit 64 of MTTS means SCREEN READER. A server that honours it drops its room maps and ASCII art without anyone having to find the setting. The window width is sent too, so text wraps to the width being read.
- **Nothing but text reaches the transcript.** Compression and the out-of-band data channels (MSDP, GMCP, ATCP, MSSP, MXP) are declined, so no markup ever lands in the middle of a sentence a screen reader is speaking.

Core MUD sends its opening ASCII logo before any client has time to answer that negotiation. BlindTerm recognizes that one unavoidable opening and rewrites it as ordinary prose, preserving the welcome, setting, story, server version, and login instructions without making NVDA read rows of dots, slashes, and bars. Later text remains byte-for-byte server output. BlindTerm also accepts a host's UTF-8 character-set offer, automatically speaks complete prompts that do not end in a newline, and changes the command line into a protected password field while a password, passphrase, passcode, or PIN is requested.

Typing works as it does anywhere else in BlindTerm, and with an empty command line the arrow keys, `Escape` and `Ctrl+]` reach the host, so a MUD's own history and menus behave. Nothing is sent on connect, so a plain TCP service — a mail server, a web server — can still be poked at the way people use a telnet client for.

### MUD sounds

BlindTerm speaks the MUD Sound Protocol, the way clients like Portal did: a MUD asks for a sound and BlindTerm plays it.

**Sound packs go in `%APPDATA%\BlindTerm\sounds`**, or wherever you point **Sound folder** in **Terminal** &rarr; **Settings**. A MUD's `T=` parameter names a subfolder, so a pack that arrives with a `combat` folder in it should be unpacked keeping that shape. `Alt+M` turns sounds off and on, and **Sound volume** scales everything the MUD asks for. WAV, MP3, MIDI, WMA, AU and AIFF all play, several at once.

The whole protocol is here: `V` volume, `L` loops (`-1` for as long as nothing stops it), `P` priority, `C` continue, `T` subfolder, `U` address, `!!SOUND(Off)` and `!!MUSIC(Off)`, and wildcards — a MUD asking for `hit*.wav` gets one of your hit sounds at random. Music plays one at a time and is left alone rather than restarted when the MUD names the same piece again. Eight sound effects play at once; past that, a trigger only interrupts something already playing if it arrives with a higher priority.

Triggers are lifted out of the text **whether or not sounds are switched on**. That is the part that matters even to someone who never turns them on: left in, a trigger is a line read aloud as "exclamation exclamation SOUND left paren sword dot wav" in the middle of a fight. They are only recognised at the start of a line, which is also what stops a player typing `!!SOUND(scream.wav)` into a chat channel from making a noise on your machine — what the MUD echoes of that arrives after a name and a colon, and stays ordinary text.

Some MUDs put their triggers in the text and some send them out of band, inside the telnet option, so that clients which do not speak the protocol never see them. Core MUD does the second. Both work.

**Sounds are never downloaded unless you ask.** A trigger's `U` parameter is an address chosen by the server, so **Download sounds a MUD offers** is off by default. Turned on, the rules are narrow: an ordinary web address only, a plain sound file name with a playable extension, a destination inside your sound folder and nowhere else, a size cap, one attempt per address, and a file you already have is never overwritten.

## Diagnostic CLI

The core can be exercised without opening a window:

```powershell
dotnet run --project src\BlindTerm.Cli -- capture --out session.raw --send "Get-ChildItem" --send "exit" -- pwsh.exe -NoLogo
dotnet run --project src\BlindTerm.Cli -- replay session.raw --numbered
dotnet run --project src\BlindTerm.Cli -- speak --probe
dotnet run --project src\BlindTerm.Cli -- telnet coremud.org:4000 --seconds 8 --numbered
```

The `telnet` verb runs a real connection through the same transcript assembly the window uses, and prints what it produced. That is how "nothing is lost" is checked: point it at a host that will send more lines than the terminal is tall and count them. Add `--updates` to print each response batch as it arrives.

Replay tests deliberately feed captures at 16384, 7, and 1 byte chunks. Escape sequences split across reads are not an edge case in a real PTY; they are the test.

## Testing

```powershell
dotnet test
pwsh -File tests\run-replay-tests.ps1
```

The test suite covers the VT engine, transcript assembly, screen speech, key translation and encoding, ConPTY captures, screen wipes, redraws, wrapping, alternate-screen programs, and the whole default-terminal path: the registry values Windows parses, the marshalling registration, the COM wrappers, and a complete inbound console handoff driven with real pipes and process handles. SSH captures are included as development corpus and are replayed by hand because their login banners and disconnect text are host-specific.

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
