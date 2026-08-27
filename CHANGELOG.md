# Changelog

Readable release history for BlindTerm. This starts with the first build
that was complete enough to install and use, rather than pretending the
earlier prototypes were something anyone could have run.

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
