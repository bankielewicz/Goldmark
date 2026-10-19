#!/usr/bin/env python3
"""The cross-run figures of a DevForgeAI project (SPEC-012 version 17, IF-04, BEH-27, BEH-28, ERR-14 to ERR-16).

Run from anywhere:
    python3 history.py --root DIR

Reads the finished runs' states, DIR/devforgeai/progress/runs/<run>/state.json, and the odometer ledger, the .jsonl files
directly in DIR/devforgeai/progress/odometer/, and prints JSON (DM-06, devforgeai-history/1) on stdout: for each skill the
runs read, the complete ones and the median of their active seconds; the ledger's token totals; and how many states were
read and left out. Both are one pass over each file, and neither reads a clock.

Exit 0 whenever it prints, with no runs or no ledger giving empty and zero figures; exit 2, with nothing on stdout and one
line on stderr, when --root is missing, isn't a folder or can't be read, an argument is unknown, or a ledger file that
exists can't be read. It follows no symbolic link, writes no file, opens no network connection and runs no other program
(QR-06). Standard library only; the same files under --root always give the same bytes (QR-05).
"""
import json
import os
import stat
import sys

FORMAT = "devforgeai-history/1"
STATE_FORMAT = "devforgeai-progress/1"
OPEN_STATES = ("pending", "current", "your-turn", "carried")  # a step in one of these was not reached or passed over
COUNTS = ("input", "output", "cacheRead", "cacheWrite")


class Fail(Exception):
    """A reason the script can't run: exit 2, with the message on stderr."""


def parse(argv):
    """The --root value; --root DIR or --root=DIR, and nothing else is accepted."""
    root, args = None, list(argv)
    while args:
        arg = args.pop(0)
        if arg == "--root" or arg.startswith("--root="):
            if root is not None:
                raise Fail("--root is given twice")
            if arg == "--root":
                if not args:
                    raise Fail("--root needs a folder")
                root = args.pop(0)
            else:
                root = arg[len("--root="):]
        else:
            raise Fail("unknown argument %s" % arg)
    if root is None:
        raise Fail("--root is required")
    return root


def folder_in(base, *names):
    """The path of a folder reached through plain folders only (no link followed), or None."""
    path = base
    for name in names:
        path = os.path.join(path, name)
        try:
            if not stat.S_ISDIR(os.lstat(path).st_mode):
                return None
        except OSError:
            return None
    return path


def by_name(entries):
    """Directory entries in the byte order of their names, never the order of a folder listing."""
    return sorted(entries, key=lambda entry: os.fsencode(entry.name))


def show(name):
    return name.encode("utf-8", "replace").decode("utf-8")


def read_state(folder):
    """(a state read as a run, or None, whether the folder's state.json is to be counted as left out): None and False
    for a folder with no state.json, which isn't a run state (ERR-15)."""
    path = os.path.join(folder, "state.json")
    try:
        info = os.lstat(path)
    except FileNotFoundError:
        return None, False
    except OSError:
        return None, True
    if not stat.S_ISREG(info.st_mode):  # a link is not followed, and a pipe or a device is never opened
        return None, True
    try:
        with open(path, "rb") as handle:
            state = json.loads(handle.read().decode("utf-8"))
    except (OSError, ValueError, RecursionError):
        return None, True
    timing = state.get("timing") if isinstance(state, dict) else None
    if not (isinstance(state, dict) and state.get("format") == STATE_FORMAT and isinstance(state.get("skill"), str)
            and isinstance(timing, dict) and type(timing.get("activeSeconds")) is int):
        return None, True
    try:
        state["skill"].encode("utf-8")  # a lone surrogate (legal JSON) can't be printed: one bad state hides nothing
    except UnicodeEncodeError:
        return None, True
    return state, False


def complete(state):
    """Whether a read run counts toward the median (BEH-27): it ended, carried no step, and no step is still pending,
    current, your-turn or carried, so every step was reached or passed over."""
    steps = state.get("steps")
    return (state.get("ended") is not None and type(state["timing"].get("stepsCarried")) is int
            and state["timing"]["stepsCarried"] == 0 and isinstance(steps, list)
            and all(isinstance(step, dict) and isinstance(step.get("state"), str) and step["state"] not in OPEN_STATES
                    for step in steps))


def median(values):
    values = sorted(values)
    middle = len(values) // 2
    return values[middle] if len(values) % 2 else (values[middle - 1] + values[middle]) // 2


def run_figures(root):
    """({skill: {runs, complete, medianActiveSeconds}}, runs read, states left out) (BEH-27, ERR-15)."""
    skills, read, skipped = {}, 0, 0
    runs = folder_in(root, "devforgeai", "progress", "runs")
    if runs is None:
        return {}, 0, 0
    try:
        entries = by_name(os.scandir(runs))
    except OSError:
        raise Fail("%s: can't be read" % show("devforgeai/progress/runs"))
    for entry in entries:
        if entry.is_symlink():  # an entry of runs/ that is a link: left out, never followed
            skipped += 1
        elif entry.is_dir(follow_symlinks=False):
            state, left_out = read_state(entry.path)
            if left_out:
                skipped += 1
            elif state is not None:
                read += 1
                seen = skills.setdefault(state["skill"], {"runs": 0, "active": []})
                seen["runs"] += 1
                if complete(state):
                    seen["active"].append(state["timing"]["activeSeconds"])
    figures = {name: {"runs": seen["runs"], "complete": len(seen["active"]),
                      "medianActiveSeconds": median(seen["active"]) if len(seen["active"]) >= 2 else None}
               for name, seen in skills.items()}
    return figures, read, skipped


def counted(line):
    """A ledger line as DM-04 gives it, or None when it isn't a counted line (BEH-28)."""
    try:
        data = json.loads(line.decode("utf-8"))
    except (ValueError, RecursionError):
        return None
    if not isinstance(data, dict):
        return None
    for key in ("session", "turn", "source"):
        if not isinstance(data.get(key), str) or not data[key]:
            return None
    if not isinstance(data.get("time"), str):
        return None
    for key in COUNTS:
        if type(data.get(key)) is not int or data[key] < 0:  # a boolean is no integer
            return None
    return data


def odometer(root):
    """The ledger's totals: every regular .jsonl file directly in devforgeai/progress/odometer/, in the byte order of
    the file names, each line once by its (session, turn, source) (BEH-28, ERR-16)."""
    totals = {key: 0 for key in COUNTS}
    totals.update(tokens=0, turns=0, duplicates=0, malformed=0)
    folder = folder_in(root, "devforgeai", "progress", "odometer")
    if folder is None:
        return totals
    try:
        entries = by_name(os.scandir(folder))
    except OSError:
        raise Fail("devforgeai/progress/odometer: can't be read")
    seen = set()
    for entry in entries:
        if not entry.name.endswith(".jsonl") or not entry.is_file(follow_symlinks=False):
            continue
        try:
            with open(entry.path, "rb") as handle:
                data = handle.read()
        except OSError as why:
            raise Fail("devforgeai/progress/odometer/%s: %s" % (show(entry.name), why.strerror or why))
        for line in data.split(b"\n"):
            if not line.strip():
                continue
            found = counted(line)
            if found is None:
                totals["malformed"] += 1
                continue
            triple = (found["session"], found["turn"], found["source"])
            if triple in seen:
                totals["duplicates"] += 1
                continue
            seen.add(triple)
            totals["turns"] += 1
            for key in COUNTS:
                totals[key] += found[key]
                totals["tokens"] += found[key]
    return totals


def main(argv=None):
    try:
        root = parse(sys.argv[1:] if argv is None else argv)
        if not os.path.isdir(root):
            raise Fail("%s: not a folder" % root)
        if not os.access(root, os.R_OK | os.X_OK):
            raise Fail("%s: can't be read" % root)
        skills, read, skipped = run_figures(root)
        figures = {"format": FORMAT, "skills": skills, "odometer": odometer(root),
                   "runs": {"read": read, "skipped": skipped}}
    except Fail as why:
        print("history: %s" % why, file=sys.stderr)
        return 2
    text = json.dumps(figures, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    sys.stdout.buffer.write(text.encode("utf-8"))
    sys.stdout.buffer.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
