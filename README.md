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

BlindTerm keeps its reserved commands under `Ctrl+Alt`, because Ctrl, Alt, function keys, Insert, and Caps Lock already belong to shells, terminal programs, and screen readers.

- `Ctrl+Alt+1`: focus the transcript.
- `Ctrl+Alt+2`: focus the command line.
- `Ctrl+Alt+E`: move to the end of the transcript.
- `F5`: freeze or resume full-screen review.
- `Ctrl+Alt+L`: speak the current line.
- `Ctrl+Alt+W`: speak the visible screen.
- `Ctrl+Alt+C`: send Ctrl+C.
- `Ctrl+Alt+P`: pass the next supported key to the program.
- `Ctrl+Alt+A`: copy the transcript or current screen.

The complete command list is also available from the menu bar, which is the discoverability path for NVDA and JAWS users.

## Diagnostic CLI

The core can be exercised without opening a window:

```powershell
dotnet run --project src\BlindTerm.Cli -- capture --out session.raw --send "Get-ChildItem" --send "exit" -- pwsh.exe -NoLogo
dotnet run --project src\BlindTerm.Cli -- replay session.raw --numbered
dotnet run --project src\BlindTerm.Cli -- speak --probe
```

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
