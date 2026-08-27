# Changelog

Readable release history for BlindTerm. This starts with the first complete,
installable build rather than pretending the early prototype releases were
something users could have installed.

## v1.0.0 - 2026-08-27

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
