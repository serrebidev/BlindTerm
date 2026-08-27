# BlindTerm design

BlindTerm is a Windows terminal for people who use NVDA or JAWS. The design is deliberately less clever than a visual terminal in one place and more deliberate in another: ordinary output becomes a document, while a full-screen program keeps the terminal contract it expects.

## The accessibility decision

The transcript is a real Win32 edit control. That gives the screen reader line, word, and character navigation, say-all, braille caret tracking, selection, copy, and the user's own punctuation settings without reimplementing any of them.

Focus is also the input boundary in line mode. When the transcript has focus, BlindTerm leaves standard Windows caret, selection, and clipboard chords local, even while a foreground command is running. When the command field has focus, Ctrl chords are terminal input for that foreground program.

The painted full-screen surface is not the reader's focus target while keys belong to nano, vim, htop, or another TUI. BlindTerm gives focus to a native multiline edit proxy containing only the editor body. That gives NVDA and JAWS a real caret and their normal keyboard echo/caret events without exposing nano's title or shortcut bars as document lines. `Alt+3` freezes a frame into the transcript control for detailed reading.

## Two modes

### Line mode

Line mode has three controls:

- Transcript: completed logical lines, read natively.
- Current line: the prompt or unfinished output that has not ended in a newline.
- Command line: a normal edit control whose text and Return are sent as separate PTY writes.

Remote output is always the complete transcript. BlindTerm records its line count when a
command is submitted; Shift+Tab focuses the full output control with the caret at that line,
which makes the newest response the starting point without hiding any earlier output. Tab
returns to input. Go, Transcript moves to the end of the same permanent history.

The command line is buffered until Enter so ordinary shell commands retain native editing and
can be adapted for an accessible agent interface before launch. Once an active inline program
receives Tab for completion, the pending text is flushed and that program owns the live line:
new characters and editing keys are streamed until Enter. Shift+Tab stays local and moves to
output; Tab in output returns to input. Full-screen mode uses the same focus
contract between live input and frozen review output.

New output is filtered by line and batched before it is sent to the reader. A redraw of an existing row revises the corresponding transcript line instead of appending another stale frame.

### Screen mode

When the VT engine enters the alternate screen, BlindTerm hides the transcript and makes the screen surface the keyboard target. Every supported key is translated to terminal bytes. Cursor movement speaks the row or text crossed; status bars, clocks, and other rows are silent by default.

`Alt+3` switches to review mode. The current screen is copied into the native edit control, with the caret parked on the program's cursor row. Keys remain local until the same command returns to live passthrough.

## Speech

BlindTerm calls the screen reader directly instead of using UI Automation notifications. NVDA uses `nvdaControllerClient.dll`, including its speech priority and braille APIs. JAWS uses the late-bound `FreedomSci.JawsApi` COM object. The router checks NVDA first, then JAWS, and can re-probe when a reader starts or stops.

Speech is never muted as part of normal testing or application operation. Secure-desktop protection is separate: when the Windows lock or credential desktop is active, output is withheld so a terminal session is not spoken to someone standing at a locked machine.

## Terminal core

ConPTY owns the child process boundary. The VT engine parses bytes into normal and alternate buffers. The transcript builder gives rows stable scroll-invariant numbers, joins wrapped rows, observes screen wipes, and maps redraws back to existing lines.

The same `TerminalCore` receives live PTY bytes and replayed capture bytes. That is why a capture can become a regression test without a shell, window, or pseudo console.

## Being the default terminal

Windows 11 can hand a newly created console to a terminal of the user's choosing instead of hosting it itself. Two CLSIDs in `HKCU\Console\%%Startup` name the halves: a *delegation console*, which owns the console driver connection and turns API calls into a pseudo console, and a *delegation terminal*, which is given that pseudo console to show. The inbox `conhost.exe` calls `IConsoleHandoff::EstablishHandoff` on the first; the first calls `ITerminalHandoff3::EstablishPtyHandoff` on the second.

BlindTerm implements the terminal half only. The console half needs a console API server, and Windows Terminal already ships one -- `OpenConsole.exe`, signed by Microsoft and already registered -- so BlindTerm names it rather than redistributing a console host of its own.

Answering the call means creating a pipe each way, taking a copy of the session's signal, reference and process handles, and returning the far ends before the call returns. The program that wanted a terminal is blocked until it does, so the window is built afterwards, on the next turn of the message loop. From there the session is indistinguishable from one BlindTerm started: the same bytes arrive on the same event and the same code assembles the transcript. Only resizing differs, because the pseudo console lives in another process and the signal pipe is the way to reach it.

Two things about that call are not obvious and are both covered by tests, because each one fails silently:

- .NET builds a COM callable wrapper only for public types. An internal class or interface leaves the object answering nothing but `IUnknown`, and Windows abandons the handoff without a word.
- Windows delivers the call on an RPC worker thread, not on the thread that registered the class. A window built there is created, titled, and then never hears from anyone again, because nothing pumps that thread. The window thread's synchronisation context is captured at startup for exactly this reason.

The interface passes pipe and process handles, which no automatic marshaller can carry -- each has to be duplicated into the receiving process. Windows Terminal ships the generated proxy for it, registered inside its own app package where the COM runtime finds it for packaged processes and nowhere else. BlindTerm is not packaged, so it stages a copy of that library where it can be loaded from and registers it per-user. Loading it in place is not possible: `WindowsApps` refuses to load its contents into a process outside the package.

Every part of this is per-user and needs no elevation, and none of it can leave the machine without a terminal. If the registration is wrong, stale, or missing, the console host logs the failure and keeps the session, which is what it did before BlindTerm was installed. That is what makes the offer safe to put in a dialog at startup.

## Updating and packaging

The app's update client reads a future GitHub Releases manifest named `BlindTerm-update.json`. It verifies the package hash before staging it. A separate `BlindTerm.Update.exe` waits for the main process to exit, replaces program files while preserving settings, and restarts the app.

The current workflow is local only. It publishes a self-contained `win-x64` build, creates a ZIP and an Inno Setup installer, and writes a manifest to `dist\`. GitHub publishing is intentionally not part of the local install command.

## What is verified

The current machine verifies NVDA speech, NVDA braille, ConPTY, command and screen-mode behavior, nano, vim, htop, replayed SSH captures, and the regression suite. The default-terminal path is verified end to end on Windows 11: `cmd.exe` started with no terminal opens in BlindTerm, its output reaches the transcript, typing reaches the program, and the window takes the foreground. JAWS and a live remote SSH session still need a machine equipped for those tests.
