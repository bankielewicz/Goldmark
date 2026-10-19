"""progress.mode for DevForgeAI's progress tracker: resolve it, and save the user's choice.

SPEC-013 IF-01 and IF-02, read by BEH-18. The user's entry lives in the local preference file
`.claude/devforgeai.local.md`, whose YAML frontmatter is its entire content (ADR-003 A3, ADR-006 D6):

    ---
    devforgeai_local: 1
    progress.mode: enforce
    ---

    mode --root DIR                 prints "<observe|enforce> <framework-default|local>"; exit 0, or 2
    set-mode --root DIR --value V   saves the entry; exit 0, 1 when the file isn't one it may change, or 2

An entry that can't be used is ignored and reported on stderr, never fatal, since a local preference
can't block a shared workflow. This script reads and writes only progress.mode (and adds
devforgeai_local: 1 when the file has none); the skills apply every other entry.

Standard library only; runs under python3 -S (QR-04).
"""
import argparse
import os
import re
import stat
import sys
import tempfile

LOCAL = os.path.join(".claude", "devforgeai.local.md")
SHOWN = ".claude/devforgeai.local.md"
MODES = ("observe", "enforce")
DEFAULT = "observe"
ENTRY = re.compile(r"^progress\.mode:(.*)$")
VERSION = re.compile(r"^devforgeai_local:(.*)$")
FENCE = "---"


class Fail(Exception):
    """A reason the command can't run: exit 2 with one line on stderr."""


def split_lines(text):
    """The file's lines, each with its own line ending kept."""
    return text.splitlines(keepends=True)


def bare(line):
    return line.rstrip("\r\n")


def frontmatter(lines):
    """The index of the closing fence when the file is frontmatter only, else None.

    The first line must be '---', the frontmatter ends at the next '---' line, and only blank lines may follow.
    """
    if not lines or bare(lines[0]) != FENCE:
        return None
    for i in range(1, len(lines)):
        if bare(lines[i]) == FENCE:
            if all(not bare(rest).strip() for rest in lines[i + 1:]):
                return i
            return None
    return None


def value_of(raw):
    """A scalar's value: quotes removed, or a trailing comment cut off an unquoted value."""
    raw = raw.strip()
    if len(raw) >= 2 and raw[0] in "\"'" and raw[-1] == raw[0]:
        return raw[1:-1]
    return raw.split(" #", 1)[0].strip()


def read_local(root):
    """The local file's text, or None when there is none."""
    path = os.path.join(root, LOCAL)
    if not os.path.exists(path):
        return None
    try:
        with open(path, "rb") as f:
            text = f.read().decode("utf-8")
    except (OSError, UnicodeDecodeError) as err:
        raise Fail("%s: %s" % (SHOWN, err))
    text = text[1:] if text.startswith("\ufeff") else text
    # An empty file holds no entry: the same as no file.
    return text if text.strip() else None


def check_root(root):
    if not os.path.isdir(root):
        raise Fail("%s: not a folder" % root)


def resolve(root):
    """(mode, source, reasons): the effective mode, where it came from, and why an entry was ignored."""
    check_root(root)
    text = read_local(root)
    if text is None:
        return DEFAULT, "framework-default", []
    lines = split_lines(text)
    if not any(ENTRY.match(bare(line)) for line in lines):
        return DEFAULT, "framework-default", []
    end = frontmatter(lines)
    if end is None:
        return DEFAULT, "framework-default", ["the file isn't frontmatter-only"]
    body = [bare(line) for line in lines[1:end]]
    versions = [value_of(m.group(1)) for m in (VERSION.match(line) for line in body) if m]
    entries = [value_of(m.group(1)) for m in (ENTRY.match(line) for line in body) if m]
    if not entries:
        return DEFAULT, "framework-default", []
    if not versions or versions[-1] != "1":
        return DEFAULT, "framework-default", ["devforgeai_local is not 1"]
    value = entries[-1]
    if value not in MODES:
        return DEFAULT, "framework-default", ["'%s' is not observe or enforce" % value]
    return value, "local", []


def cmd_mode(args):
    mode, source, reasons = resolve(args.root)
    for reason in reasons:
        print("ignored %s progress.mode (%s)" % (SHOWN, reason), file=sys.stderr)
    print("%s %s" % (mode, source))
    return 0


class Refused(Exception):
    """The file exists but isn't one this script may change: exit 1."""


def updated(text, value):
    """The local file's new text with progress.mode set to value."""
    if text is None:
        return "%s\ndevforgeai_local: 1\nprogress.mode: %s\n%s\n" % (FENCE, value, FENCE)
    lines = split_lines(text)
    end = frontmatter(lines)
    if end is None:
        raise Refused("%s isn't frontmatter-only; not changed" % SHOWN)
    newline = "\r\n" if lines[0].endswith("\r\n") else "\n"
    body = lines[1:end]
    versions = [value_of(m.group(1)) for m in (VERSION.match(bare(line)) for line in body) if m]
    if versions and versions[-1] != "1":
        raise Refused("%s has devforgeai_local %s, not 1; not changed" % (SHOWN, versions[-1]))
    found = False
    for i, line in enumerate(body):
        if ENTRY.match(bare(line)):
            ending = line[len(bare(line)):] or newline
            body[i] = "progress.mode: %s%s" % (value, ending)
            found = True
    if not found:
        body.append("progress.mode: %s%s" % (value, newline))
    if not versions:
        body.insert(0, "devforgeai_local: 1%s" % newline)
    return "".join([lines[0]] + body + lines[end:])


def write_replacing(path, text):
    """Write text to a temporary file beside path, then rename it over path, keeping the file's permissions
    (0644 for a new one; mkstemp's own 0600 would make it owner-only)."""
    folder = os.path.dirname(path)
    mode = stat.S_IMODE(os.stat(path).st_mode) if os.path.exists(path) else 0o644
    fd, tmp = tempfile.mkstemp(prefix=".devforgeai.local.", suffix=".tmp", dir=folder)
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(text.encode("utf-8"))
            f.flush()
            os.fsync(f.fileno())
        os.chmod(tmp, mode)
        os.replace(tmp, path)
    except BaseException:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise


def cmd_set_mode(args):
    check_root(args.root)
    text = read_local(args.root)
    try:
        new_text = updated(text, args.value)
    except Refused as why:
        print("settings: %s" % why, file=sys.stderr)
        return 1
    path = os.path.join(args.root, LOCAL)
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        write_replacing(path, new_text)
    except OSError as err:
        raise Fail("%s: %s" % (SHOWN, err))
    print("saved progress.mode=%s to %s" % (args.value, SHOWN))
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(prog="settings.py", description="progress.mode for DevForgeAI's progress tracker")
    sub = p.add_subparsers(dest="command", required=True)
    m = sub.add_parser("mode", help="resolve progress.mode for the project (IF-01)")
    m.add_argument("--root", required=True, help="the project root")
    s = sub.add_parser("set-mode", help="save progress.mode in the local preference file (IF-02)")
    s.add_argument("--root", required=True, help="the project root")
    s.add_argument("--value", required=True, choices=MODES)
    args = p.parse_args(argv)
    try:
        return cmd_mode(args) if args.command == "mode" else cmd_set_mode(args)
    except Fail as why:
        print("settings: %s" % why, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
