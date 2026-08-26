# BlindTerm — Windows design

A Windows terminal for NVDA and JAWS users, following AccessTerm (macOS/VoiceOver) in
philosophy, rebuilt around two requirements AccessTerm does not yet meet: full TUI support
(nano, vim, htop, mc, over SSH) and equal quality on both screen readers.

## 1. Audit of AccessTerm

Read at commit 1c63359. ~2,600 lines of Swift, 10 files.

### What it does well — port these ideas

- **Headless VT engine, custom accessible UI.** SwiftTerm's `Terminal` + `LocalProcess` are
  used for parsing only; the grid is never drawn. The UI is three native controls. This is
  the single most important decision in the program and it transfers directly.
- **Grid to transcript conversion** (`TerminalSession.publishUpdate`). Rows become logical
  lines as the cursor passes them; wrapped rows are rejoined; a row a program *redraws*
  rewrites the line it already produced instead of appending a duplicate. `lineRows` /
  `rowToLine` maintain the row-to-line mapping, in scroll-invariant row numbers
  (`totalLinesTrimmed + topVisibleRow + y`) so numbering survives scrolling. This is the
  thing that makes a repainting program (Claude Code, a spinner) readable rather than a
  flood, and it is non-obvious work worth reusing wholesale.
- **Announce only what is news** (`Announcer.LineNews`). A line is spoken when its text is
  non-blank and differs from what was last spoken *for that line*. Batched on a 250 ms timer
  into one utterance; bursts over 30 lines collapse to "N lines of output. Last 30: ...".
  Directly portable and even more necessary on Windows, where both readers interrupt
  themselves on every new utterance.
- **OSC 133 command blocks.** Prompt / command / output / exit code, with markers anchored to
  buffer rows and resolved to transcript lines later (`pendingAnchors`), because a marker
  usually lands on a row that is not yet a line. Gives jump-to-previous-command,
  copy-output-only, and "exit code 1" spoken after a failure.
- **Turns inside a long-running command.** An OSC 133 `A` while a command is still running
  cannot be a shell prompt, so it is treated as a conversational turn marker; a Claude Code
  session becomes one block with a child block per question. Clever and worth keeping.
- **Shell integration without touching user dotfiles**, via `ZDOTDIR` redirection to a
  generated directory that sources the user's own files first and restores `ZDOTDIR`
  afterwards.
- **Split write of text and Return** (`send(text:)`), because programs infer paste-vs-typing
  from arrival size; and bracketed paste only for genuinely multi-line input, because a
  bracketed single "y" is discarded by a program waiting on one keystroke.
- **Caret is the reading position.** New output never moves the caret and only scrolls the
  view when the caret is already at the end.
- **Tooling env vars**: `CLAUDE_AX_SCREEN_READER=1`, `GH_ACCESSIBLE_PROMPTER=1`,
  `GH_ACCESSIBLE_COLORS=1`, `GH_SPINNER_DISABLED=1`.
- **Replay harness** (`--replay` over a raw PTY capture) — a repeatable test for transcript
  assembly with no window, shell or pty. Build this on day one, not later.

### What is missing or wrong — do not port

1. **No TUI support.** `publishUpdate` short-circuits on the alternate screen: it dumps all
   rows and returns. `MainViewController` diffs it against the previous screen and speaks up
   to the first 10 changed rows. That is unusable for nano — arrowing down a document changes
   the status bar and the cursor row, and "first 10 changed rows" speaks the wrong thing in
   the wrong order. The README calls this "basic in milestone 1". **This is the part that has
   to be designed properly, not ported.** See section 4.
2. **No keyboard passthrough.** Tab moves focus, arrows drive local history, function keys
   are unhandled. Their README lists direct input as milestone 3. Consequences: no shell tab
   completion, no Ctrl+R history search, no readline editing, no nano, no vim, no `less`, and
   no way to answer an `ssh` host-key or password prompt properly.
3. **Fixed 160x50, never resized.** `getWindowSize` returns constants. A remote `nano` will
   lay itself out for 160x50 regardless. Needs a real, user-settable size plumbed through
   `ResizePseudoConsole` and SIGWINCH.
4. **Return sent via `asyncAfter(0.02)`** with no serialisation. Two fast submits can
   interleave text and terminators. Use an ordered write queue with a real delay between the
   two parts of one submission.
5. **Screen-wipe detection scans the raw byte stream** (`firstScreenWipe`) for `ESC c`,
   `ESC [ 2 J`, `ESC [ 3 J`, assuming fixed lengths. Misses `ESC [ ? 2 J` and private
   parameters, and misses a sequence split across two reads (they acknowledge this and add a
   cursor-at-origin heuristic to compensate). Take the erase event from the parser instead.
6. **`rowToLine` is an unbounded dictionary** and the transcript hard-stops at 100,000 lines
   ("Restart the app for now"). Use a ring buffer and trim from the front.
7. **The VoiceOver first-line-on-focus bug** and the entire `landCaret` workaround around it
   are macOS-specific. A real Win32 EDIT control reads the caret line on focus in both NVDA
   and JAWS. Delete this whole problem; do not port the compensating high-priority
   announcement.

## 2. Recommended stack

**C# / .NET 8, WinForms, single self-contained exe.**

- **UI: WinForms, because its controls are real HWNDs.** A read-only multiline
  `TextBox`/`RichTextBox` is a genuine Win32 EDIT/RichEdit control. Both NVDA and JAWS have
  had rock-solid support for it for twenty years, and it gives us for free: arrow by
  line/word/character, say-all, braille display following the caret, the JAWS cursor,
  find, select and copy, and the user's own verbosity and punctuation settings. This is the
  exact role `NSTextView` plays in AccessTerm, and it is the reason not to use WPF (UIA-only,
  no per-control HWND, weaker and more variable JAWS behaviour, poor large-document
  performance) or anything web-based (JAWS enters virtual cursor mode and fights everything).
- **PTY: ConPTY** (`CreatePseudoConsole`, Windows 10 1809+). Microsoft ships an MIT-licensed
  C# wrapper in `microsoft/terminal` at `samples/ConPTY/GUIConsole/GUIConsole.ConPTY/`. Vendor
  it; it is ~200 lines and removes a dependency. ConPTY is what makes `ssh.exe` behave
  exactly as it does in any other terminal.
- **VT engine: XTerm.NET** (MIT, on NuGet, dual normal/alternate buffers with scrollback,
  OSC handlers, cursor tracking). Vendor the source rather than take the package reference —
  the VT engine is where "works like any other terminal" lives, and we will need to add an
  OSC 133 hook and scroll-invariant row numbering to it. Fallback if it proves incomplete:
  port SwiftTerm's `Terminal` (also MIT, and already proven against exactly this use case),
  or wrap Microsoft's own `TerminalParser` from `microsoft/terminal` as a C ABI DLL.
- **Rejected: Rust.** `portable-pty` + `wezterm-term` is the best terminal core available in
  any language, but the UI has to be AccessKit, whose UIA text-provider implementation is not
  in the same league as a real EDIT control for JAWS caret navigation, braille and the JAWS
  cursor. The terminal core is the part we can afford to be second-best at; the accessibility
  is not.
- **Rejected: Python/wxPython.** Genuinely accessible (NVDA's own GUI is wxPython) but too
  slow for a Claude Code session repainting its frame several times a second.
- **Rejected: hosting a real conhost and reading the grid with `ReadConsoleOutput`** (the way
  NVDA reads cmd.exe). Tempting, since Windows would do all VT parsing, but conhost swallows
  OSC sequences, which kills command blocks; it needs a helper process per session; and it
  requires polling for changes.

## 3. Autoreading — the critical decision

**Speak through the screen reader's own API, not through UI Automation.**

UIA notification events (`UiaRaiseNotificationEvent`) are the textbook answer and they are
wrong here. NVDA supports them well. JAWS disabled them by default because applications
abused them, and OSARA — Jamie Teh's project, and the reference implementation for this exact
problem — found JAWS did not respond to them at all and shipped a blocklist, keeping direct
screen reader APIs as the path for everything JAWS-shaped. UIA live regions are worse.

So:

- **NVDA:** `nvdaControllerClient.dll`. As of NVDA 2024.1 the client is version 2 and the DLL
  is architecture-suffix-free. Use `nvdaController_speakSsml` — it takes a symbol level and a
  **speech priority**, which is the direct equivalent of AccessTerm's
  `NSAccessibilityPriorityLevel` (high for bells and landings, medium for streamed output).
  Pair with `nvdaController_brailleMessage` and `nvdaController_cancelSpeech`.
- **JAWS:** the COM automation object, `FreedomSci.JawsApi`, `SayString(text, flush)`. No
  public braille-only API; braille follows the caret in the transcript control instead, which
  is the main reason the transcript must be a real edit control.
- **Fallback:** Narrator/none → SAPI5 via `System.Speech`, off by default.
- **Detection:** try NVDA first, then JAWS, then SAPI, re-probing when a reader starts or
  stops. Tolk does exactly this but is explicitly unmaintained ("this project is not
  currently being developed") and its SuperNova driver is 32-bit only — implement the two
  drivers we need directly, about 200 lines.

Everything AccessTerm's `Announcer` does — the 250 ms batch, the news filter, the 30-line
summary, the priority levels — sits unchanged on top of this.

## 4. Two modes

The heart of the design. AccessTerm has one mode; BlindTerm needs two, and must switch
automatically.

### Line mode (default; the shell at a prompt)

Exactly AccessTerm's window, and for the same reasons:

1. **Transcript** — read-only multiline edit control, labelled "Transcript". The screen reader
   navigates it natively. Mirrors the transcript document; appends at the end; rewrites
   redrawn lines in place; never moves the caret.
2. **Current line** — a label with whatever the program has not finished printing: the prompt,
   a partial line, a progress line, a question with no newline after it.
3. **Command line** — a real single-line edit control. Enter sends. Local history on up/down.

New output is spoken through the reader API as it arrives.

### Screen mode (a full-screen program owns the alternate screen, or entered by hand)

**All keystrokes go raw to the PTY** — arrows, Tab, function keys, Escape, Ctrl and Alt
combinations — except one reserved chord family. This is what makes nano, vim, htop, mc and
`less` behave "as if it was any other terminal", and it is also what finally gives line mode's
absent features (tab completion, Ctrl+R, readline editing) a home.

Autoread in screen mode **follows the cursor, not the screen**. This distinction is the whole
difference between usable and unusable, and it is what AccessTerm's 10-changed-rows diff gets
wrong:

- Cursor row changed → speak the new cursor row. (Arrowing through a nano document.)
- Cursor column changed within a row → speak the character or word crossed, like an edit
  field, honouring the reader's own echo settings where we can infer them.
- The cursor's own row changed under it → speak the delta. (Typing, deleting.)
- Rows away from the cursor changed → **silent by default**. nano's status bar and shortcut
  bar, vim's ruler and htop's meters must not interrupt. Reachable on demand by a chord, and
  optionally by a "speak status line" chord bound to the last row.
- Entering or leaving the alternate screen → announce it.
- Bell → high-priority alert plus the cursor row.

Plus a **review layer**: a chord snapshots the current screen into the same accessible edit
control so the whole screen can be read by line, word and character, with braille and the JAWS
cursor, *without keys reaching the program*. Another press returns to live passthrough. Blind
terminal users expect this; it is how a TUI is actually read.

Screen mode also disables bracketed paste heuristics — the program is reading raw keys.

## 5. Keyboard scheme

macOS was easy: Command never collides with VoiceOver's Ctrl+Option. Windows has no free
modifier — Ctrl, Alt, Shift and the function keys all belong to terminal programs (nano uses
Alt for meta and F1–F12 for its menu), and Insert and CapsLock belong to NVDA and JAWS.

- **Reserved leader: `Ctrl+Alt+<key>`.** Terminal programs essentially never bind Ctrl+Alt,
  and neither reader claims it by default.
- **A real menu bar** carrying every command. Both readers announce menus perfectly, so this
  is the discoverability story, and it costs nothing.
- **A pass-through chord** (`Ctrl+Alt+P`, then the next chord goes raw) for the rare collision.
- Users retain their reader's own pass-key: NVDA+F2, JAWS Insert+3.

Mapping of AccessTerm's commands: Ctrl+Alt+1 transcript, Ctrl+Alt+2 command line, Ctrl+Alt+E
end of transcript, Ctrl+Alt+L speak current line, Ctrl+Alt+S toggle speaking output,
Ctrl+Alt+C interrupt (Ctrl+C), Ctrl+Alt+Up/Down previous/next command, Ctrl+Alt+O copy block
output, Ctrl+Alt+A copy transcript, Ctrl+Alt+R toggle screen review, Ctrl+Alt+M switch mode.

## 6. Shell integration on Windows, and over SSH

AccessTerm's `ZDOTDIR` trick has per-shell equivalents:

- **PowerShell 7** — launch `pwsh -NoExit -Command ". 'blindterm-shellintegration.ps1'"`,
  which wraps `prompt` and uses `Set-PSReadLineKeyHandler` to emit OSC 133 A/B/C/D.
  VS Code's `shellIntegration.ps1` is the reference implementation.
- **bash / zsh in WSL** — `PROMPT_COMMAND` and `DEBUG` trap, or the same `ZDOTDIR` redirection
  AccessTerm already uses.
- **cmd.exe** — only via clink; otherwise degrade to one unstructured block, which AccessTerm
  already handles correctly (`commandBlocks` returns a single whole-transcript block when no
  markers ever arrive).

**Over SSH the markers must come from the remote shell.** `ssh root@serrebiradio.com` gets no
local hooks. Provide an "install shell integration on this host" helper that appends a guarded
snippet to the remote `~/.bashrc` / `~/.zshrc`, and degrade gracefully when it is absent —
command blocks disappear, everything else keeps working. Same for
`CLAUDE_AX_SCREEN_READER=1`: it has to be set remotely (`SendEnv`/`AcceptEnv`, or the
integration snippet) to reach a remote Claude Code.

Also for SSH: set `TERM=xterm-256color`, UTF-8 throughout (input and output code pages), and
handle the password and host-key prompts, which echo nothing and end in no newline. Screen
mode covers them; line mode needs a masked-entry path.

## 7. Implementation notes

### ConPTY: the child must be given null standard handles

Following Microsoft's own C# ConPTY sample exactly is not sufficient. With
`bInheritHandles: false`, the pseudo console attribute set, and `STARTF_USESTDHANDLES` left
unset -- which is what every sample does -- the child is genuinely attached to the pseudo
console (`mode con` inside it reports the size we asked for) and yet **everything it prints
goes to the parent's stdout**. The terminal receives only the pseudo console's own startup
and teardown sequences and never a byte of the child's output.

The cause is that when the parent's own standard handles are redirected -- a pipe or a file,
which is every case where BlindTerm is launched by another tool, and the case under any
harness or IDE -- CreateProcess hands those handles to the child regardless of
`bInheritHandles`. Console attachment and standard handles are decided separately, so the
child ends up attached to the pty but writing somewhere else.

The fix is to set `STARTF_USESTDHANDLES` with `hStdInput`, `hStdOutput` and `hStdError` all
null. That leaves the child no inherited handles to use, so it falls back to `CONIN$` and
`CONOUT$` -- which are the pseudo console's. See `PtySession.StartChild`.

Worth knowing: this failure is invisible in a quick test, because the output still appears on
screen. Only the raw capture shows it missing. `capture -- cmd.exe /c mode con` is the
regression test: the size reported must match the `--cols`/`--rows` asked for, and it must
arrive in the capture file rather than on the console.

### PowerShell disables PSReadLine under a screen reader

Windows PowerShell 5.1 detects a running screen reader and turns PSReadLine off, announcing
it on startup. PSReadLine is what provides tab completion, history search and the OSC 133
markers the shell integration depends on, so the command-block feature would silently never
work for exactly the users this program is for. The launcher must `Import-Module PSReadLine`
explicitly, and prefer PowerShell 7 (`pwsh`), which does not do this.

## 8. Build order

1. ConPTY + VT engine + raw logging + the `--replay` harness. No UI.
2. Transcript assembly ported from `TerminalSession.publishUpdate`, verified by replay
   against captures.
3. Line mode window, screen reader speech layer, news filter and batching.
4. **Screen mode with full passthrough and cursor-follow autoread.** Test against nano, vim,
   htop and mc, locally and over SSH to serrebiradio.com.
5. OSC 133 blocks and shell integration, local shells then remote.
6. Turns, copy-output, command navigation.
7. Resize, scrollback trimming, settings.
