"""Generate raw captures that exercise the transcript assembly directly.

These are hand-built rather than recorded, so each one isolates a single behaviour and the
expected transcript is written down next to it. Recorded captures of real programs go beside
them; both are replayed the same way.
"""
import os

ESC = b"\x1b"
HERE = os.path.dirname(os.path.abspath(__file__))


def write(name, payload, expected):
    path = os.path.join(HERE, name + ".raw")
    with open(path, "wb") as f:
        f.write(payload)
    with open(os.path.join(HERE, name + ".expected"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(expected) + "\n")
    print("wrote %-22s %5d bytes  %d expected lines" % (name + ".raw", len(payload), len(expected)))


# A program that reprints a line it already printed. The redrawn row must rewrite the line it
# produced rather than add a second copy of it -- this is the whole reason the transcript is
# addressed by row rather than appended to.
redraw = (
    b"one\r\n"
    b"two\r\n"
    b"three\r\n"
    + ESC + b"[2A"      # up two rows, onto "two"
    + b"\r"
    + b"TWO REWRITTEN"
    + ESC + b"[K"       # clear to end of line
    + ESC + b"[2B\r"    # back down to where we were
    + b"four\r\n"
)
write("redraw", redraw, ["one", "TWO REWRITTEN", "three", "four"])


# A spinner: the same row rewritten many times, then finished. The transcript must end with
# one line, not one line per frame.
frames = b"".join(b"\rWorking " + bytes([c]) + b"   " for c in b"|/-\\" * 8)
spinner = frames + b"\rDone.        \r\n" + b"next\r\n"
write("spinner", spinner, ["Done.", "next"])


# A line longer than the terminal is wide wraps across rows, and has to be joined back into
# the one logical line it is. Replay this one with --cols 20.
long_line = b"A" * 25 + b"-END\r\n" + b"after\r\n"
write("wrapped", long_line, ["A" * 25 + "-END", "after"])


# Clearing the screen must not take the transcript with it: it is a transcript, not a screen.
# What was already read keeps its text, and reading starts again at the top of the fresh
# screen.
cleared = (
    b"before the wipe\r\n"
    + ESC + b"[2J" + ESC + b"[H"
    + b"after the wipe\r\n"
)
write("cleared", cleared, ["before the wipe", "after the wipe"])


# The private form of the same erase, which a fixed-length scanner mistakes for something
# else. Same expectation as above.
cleared_private = (
    b"before the wipe\r\n"
    + ESC + b"[?2J" + ESC + b"[H"
    + b"after the wipe\r\n"
)
write("cleared-private", cleared_private, ["before the wipe", "after the wipe"])


# A full-screen program takes the alternate screen, paints itself, and leaves. None of what
# it drew belongs in the transcript -- it was a screen, not output -- and the transcript it
# was launched from has to still be there afterwards, with reading resuming after it.
alt = (
    b"before vim\r\n"
    + ESC + b"[?1049h"                       # enter the alternate screen
    + ESC + b"[H" + b"~ editor line 1\r\n"
    + b"~ editor line 2\r\n"
    + ESC + b"[?1049l"                       # leave it again
    + b"after vim\r\n"
)
write("altscreen", alt, ["before vim", "after vim"])


# Columns separated by tabs, as ls does. Cells the program never wrote hold nothing at all,
# so taking them at face value runs the names together.
tabs = b"alpha\tbeta\tgamma\r\n" + b"delta\tepsilon\r\n"
write("tabs", tabs, ["alpha   beta    gamma", "delta   epsilon"])
