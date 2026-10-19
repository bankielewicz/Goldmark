"""Remove old working files of DevForgeAI's progress tracker and a skill's work files: SPEC-013 IF-04 and IF-05, run
by the adapter (BEH-19, BEH-32).

    prune  --root DIR --days N [--keep-session ID] [--keep-run ID ...] [--manifests DIR]
    remove --root DIR --manifests DIR --file=PATH [--file=PATH ...]

Under DIR/devforgeai/progress/ it removes each folder runs/<name> whose name is a run ID (SPEC-012 §4) and each
sessions/<name> whose name is a session ID (a UUID), when the newest entry in it, the folder included, was last
modified more than N days ago, other than the kept session's and run's folders. A session ID of another shape is
left alone. It never follows a symbolic link: every folder is opened by a descriptor without following one and
everything beneath it is reached through that descriptor, so a folder swapped for a link meanwhile is never entered;
it skips a link, and leaves any folder that holds one. From version 28 it also removes the project's diagnostic log
(SPEC-013 BEH-44): the regular files adapter.log and adapter.1.log to adapter.10.log directly under devforgeai/progress/,
together, when adapter.log was last modified more than N days ago (a rollover writes every rolled file again, so their own
ages say nothing); with no adapter.log, each rolled file by its own age. It deletes nothing else. A folder that changes or vanishes
while it works, as another session's prune or an active run makes it, is skipped. A platform without descriptor
support (Windows) is refused rather than walked by path.

Prints "pruned <r> runs, <s> sessions"; exit 0, also when there is no devforgeai/progress/; exit 2 with one line on
stderr when DIR isn't a folder, N isn't a whole number of at least 1, the platform lacks descriptor support, or a
deletion fails (after both kinds were tried, naming the first failure).

With --manifests, prune also removes work files by age (version 20): each regular file under DIR/devforgeai/drafts/
whose project-relative path matches a `workFiles` pattern of the plugin's manifests, whose newest modification is more
than N days ago, and which no runs/<name>/state.json still standing after the folder pass lists in workFiles.files
(untrusted, as the model can write it: it can only keep a file, never choose one). It removes no folder and follows no
link. It prints "pruned <r> runs, <s> sessions, <w> work files".

remove deletes only the regular files named by --file (a project-relative path with no empty, . or .. segment) that
match a pattern, each folder on the way opened by descriptor without following a link. The patterns are those of the
`*.json` manifests in the one --manifests folder; a manifest holding a pattern that is no string, doesn't begin
devforgeai/drafts/ or holds a .. segment is ignored as a whole and named on the output line ("; ignored <file>:
<reason>"). A path it won't remove, or a file that vanished or changed meanwhile, is skipped and counted. It prints
"removed <n> work files, skipped <k>"; exit 2 with one line on stderr when DIR isn't a folder, no --file is given, the
platform lacks descriptor support, or an unlink fails with any other error (after the other paths were tried).

The adapter starts it as a process because the mods API can't delete files ($.fs has no delete). Standard library
only; runs under python3 -S (QR-04).
"""
import argparse
import errno
import json
import os
import re
import stat
import sys
from fnmatch import fnmatchcase
import time

DAY = 86400
DRAFTS = "devforgeai/drafts/"
STATE_LIMIT = 4 * 1024 * 1024
RUN = re.compile(r"[0-9]{8}T[0-9]{6}Z-[a-z][a-z0-9-]*-[0-9a-f]{8}")
SESSION = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")


class Fail(Exception):
    """A reason the command can't run or finish: exit 2 with one line on stderr."""


# Each folder is opened by a descriptor, without following a link, and everything beneath it is reached through that
# descriptor: a path string resolved again later could meet a folder swapped for a link meanwhile, and prune runs
# outside Claude Code's sandbox (SPEC-013 §9, the plugin-validator's review of version 3).
FOLDER = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)


def supported():
    """Whether this platform can open, walk and delete through descriptors without following links."""
    needed = (os.open, os.stat, os.unlink, os.rmdir)
    return (hasattr(os, "fwalk") and hasattr(os, "O_NOFOLLOW") and hasattr(os, "O_DIRECTORY")
            and all(f in os.supports_dir_fd for f in needed) and os.scandir in os.supports_fd)


def open_folder(name, dir_fd):
    """A plain folder's descriptor; OSError when the name is missing, a link or not a folder."""
    return os.open(name, FOLDER, dir_fd=dir_fd)


def is_link(name, dir_fd):
    return stat.S_ISLNK(os.stat(name, dir_fd=dir_fd, follow_symlinks=False).st_mode)


def newest(name, dir_fd):
    """The newest modification time in a folder, the folder included; None when it holds a symbolic link."""
    latest = os.stat(name, dir_fd=dir_fd, follow_symlinks=False).st_mtime
    fd = open_folder(name, dir_fd)
    try:
        for _, dirs, files, here in os.fwalk(".", dir_fd=fd, follow_symlinks=False):
            for entry in dirs + files:
                info = os.stat(entry, dir_fd=here, follow_symlinks=False)
                if stat.S_ISLNK(info.st_mode):
                    return None
                latest = max(latest, info.st_mtime)
    finally:
        os.close(fd)
    return latest


def remove(name, dir_fd):
    """Delete a folder's contents bottom-up through descriptors, then the folder. A link that appeared since the check
    is removed as a link, never followed."""
    fd = open_folder(name, dir_fd)
    try:
        for _, dirs, files, here in os.fwalk(".", dir_fd=fd, topdown=False, follow_symlinks=False):
            for entry in files:
                os.unlink(entry, dir_fd=here)
            for entry in dirs:
                if is_link(entry, here):
                    os.unlink(entry, dir_fd=here)
                else:
                    os.rmdir(entry, dir_fd=here)
    finally:
        os.close(fd)
    os.rmdir(name, dir_fd=dir_fd)


def prune_kind(kind_fd, kind, pattern, keep, cutoff):
    """Remove one kind's old folders, except those named in the collection keep; (removed, the first failure or None).
    A folder that changes or vanishes meanwhile, as another prune or an active run makes it, is skipped."""
    removed, failure = 0, None
    names = sorted(entry.name for entry in os.scandir(kind_fd))
    for name in names:
        if name in keep or not pattern.fullmatch(name):
            continue
        try:
            if is_link(name, kind_fd):
                continue
            latest = newest(name, kind_fd)
            if latest is None or latest >= cutoff:
                continue
            remove(name, kind_fd)
            removed += 1
        except FileNotFoundError:
            continue
        except NotADirectoryError:
            continue
        except OSError as err:
            failure = failure or "%s/%s: %s" % (kind, name, err.strerror or err)
    return removed, failure


LOG_FILE = re.compile(r"adapter\.(?:[1-9]|10)\.log")


def regular_age(name, dir_fd):
    """A regular file's last modification time, or None when it is missing, a link or something else."""
    try:
        info = os.stat(name, dir_fd=dir_fd, follow_symlinks=False)
    except OSError:
        return None
    return info.st_mtime if stat.S_ISREG(info.st_mode) else None


def prune_logs(progress_fd, cutoff):
    """IF-04 (version 28): (removed, the first failure or None). adapter.log with adapter.1.log to adapter.10.log goes as a
    set once adapter.log itself is older than cutoff; with no live adapter.log each rolled file goes by its own age. The
    files are regular files of this folder only, reached through its descriptor: a link, a folder or another name stays."""
    live = regular_age("adapter.log", progress_fd)
    names = sorted(entry.name for entry in os.scandir(progress_fd) if LOG_FILE.fullmatch(entry.name))
    if live is not None:
        if live >= cutoff:
            return 0, None
        names.append("adapter.log")
    removed, failure = 0, None
    for name in names:
        when = regular_age(name, progress_fd)
        if when is None or (live is None and when >= cutoff):
            continue
        try:
            os.unlink(name, dir_fd=progress_fd)
            removed += 1
        except FileNotFoundError:
            continue
        except OSError as err:
            failure = failure or "%s: %s" % (name, err.strerror or err)
    return removed, failure


def days_of(text):
    if not re.fullmatch(r"[0-9]+", text) or int(text) < 1:
        raise Fail("--days must be a whole number of at least 1, not %r" % text)
    return int(text)


def one_line(text):
    return " ".join(str(text).split())


# Work files (SPEC-013 version 20): the patterns come from the plugin's own manifests folder only.

def manifest_patterns(path):
    """(patterns, reason): a manifest's workFiles patterns, or none and the reason the manifest is ignored. A file that
    isn't JSON, isn't an object or has no workFiles key adds none and no reason."""
    try:
        with open(path, "rb") as handle:
            data = json.loads(handle.read(STATE_LIMIT).decode("utf-8"))
    except (OSError, ValueError):
        return [], None
    if not isinstance(data, dict) or "workFiles" not in data:
        return [], None
    patterns = data["workFiles"]
    if not isinstance(patterns, list) or not patterns:
        return [], "workFiles is not a non-empty list of patterns"
    for pattern in patterns:
        if not isinstance(pattern, str):
            return [], "a pattern is not a string"
        if not pattern.startswith(DRAFTS):
            return [], "a pattern does not begin %s" % DRAFTS
        if ".." in pattern.split("/"):
            return [], "a pattern holds a .. segment"
    return patterns, None


def work_patterns(folder):
    """(patterns, ignored): the union of the workFiles of the *.json manifests in that one folder, and a
    "; ignored <file>: <reason>" for each manifest that was ignored as a whole, in file-name order."""
    try:
        names = sorted(n for n in os.listdir(folder) if n.endswith(".json"))
    except OSError:
        return [], []
    patterns, ignored = [], []
    for name in names:
        found, reason = manifest_patterns(os.path.join(folder, name))
        patterns += found
        if reason:
            ignored.append("ignored %s: %s" % (one_line(name), reason))
    return patterns, ignored


def is_work_file(path, patterns):
    """Whether a project-relative path is a work file: no empty, . or .. segment, and a pattern matches it as SPEC-012
    BEH-21 reads one (as text with fnmatch, and a pattern ending in / meaning anything inside that folder)."""
    if any(part in ("", ".", "..") for part in path.split("/")):
        return False
    for pattern in patterns:
        if pattern.endswith("/"):
            if path.startswith(pattern):
                return True
        elif fnmatchcase(path, pattern):
            return True
    return False


def unlink_file(name, dir_fd):
    os.unlink(name, dir_fd=dir_fd)


def is_regular(name, dir_fd):
    return stat.S_ISREG(os.stat(name, dir_fd=dir_fd, follow_symlinks=False).st_mode)


def remove_file(name, dir_fd):
    """Unlink a regular file in an open folder. True when removed; False when it vanished or became something else
    since the check (a folder, a link, a pipe), which is not removed; OSError for any other failure."""
    try:
        if not is_regular(name, dir_fd):
            return False
        unlink_file(name, dir_fd)
        return True
    except (FileNotFoundError, IsADirectoryError, NotADirectoryError):
        return False
    except OSError:
        # EPERM, as some systems answer for a folder: skipped only when the entry is no longer a regular file.
        try:
            if is_regular(name, dir_fd):
                raise
        except FileNotFoundError:
            return False
        return False


def remove_work(root, manifests, paths):
    """IF-05: (removed, skipped, ignored manifests, the first failure or None)."""
    if not os.path.isdir(root):
        raise Fail("%s: not a folder" % root)
    if not supported():
        raise Fail("this platform can't remove files without following links; nothing removed")
    patterns, ignored = work_patterns(manifests)
    removed = skipped = 0
    failure = None
    root_fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY)
    try:
        for path in paths:
            if not is_work_file(path, patterns):
                skipped += 1
                continue
            *folders, name = path.split("/")
            fds = []
            try:
                here = root_fd
                for folder in folders:
                    here = open_folder(folder, here)
                    fds.append(here)
                if remove_file(name, here):
                    removed += 1
                else:
                    skipped += 1
            except OSError as err:
                if isinstance(err, (FileNotFoundError, NotADirectoryError)) or err.errno == errno.ELOOP:
                    skipped += 1
                    continue
                failure = failure or "%s: %s" % (path, err.strerror or err)
            finally:
                for fd in reversed(fds):
                    os.close(fd)
    finally:
        os.close(root_fd)
    return removed, skipped, ignored, failure


def kept_by_runs(runs_fd):
    """The paths the run folders still standing list in their state.json workFiles.files. The files are the model's to
    write, so each is read as untrusted and only ever keeps a path: anything unreadable, oversized or of another shape
    keeps nothing. A folder or a state.json that is a link is not followed."""
    kept = set()
    for entry in os.scandir(runs_fd):
        try:
            run_fd = open_folder(entry.name, runs_fd)
        except OSError:
            continue
        try:
            fd = os.open("state.json", os.O_RDONLY | os.O_NOFOLLOW | getattr(os, "O_NONBLOCK", 0), dir_fd=run_fd)
            try:
                if not stat.S_ISREG(os.fstat(fd).st_mode):
                    continue
                with os.fdopen(fd, "rb", closefd=False) as handle:
                    data = handle.read(STATE_LIMIT + 1)
            finally:
                os.close(fd)
            if len(data) > STATE_LIMIT:
                continue
            files = json.loads(data.decode("utf-8"))["workFiles"]["files"]
        except (OSError, ValueError, KeyError, TypeError):
            continue
        finally:
            os.close(run_fd)
        if isinstance(files, list):
            kept.update(f for f in files if isinstance(f, str))
    return kept


def age_work_files(root_fd, patterns, cutoff):
    """IF-04's age pass: (removed, the first failure or None). Walks devforgeai/drafts/ by descriptor, following no
    link, and removes each regular file that a pattern matches, older than cutoff, which no standing run lists."""
    try:
        devforgeai_fd = open_folder("devforgeai", root_fd)
    except OSError:
        return 0, None
    fds = [devforgeai_fd]
    removed, failure = 0, None
    try:
        try:
            drafts_fd = open_folder("drafts", devforgeai_fd)
        except OSError:
            return 0, None
        fds.append(drafts_fd)
        kept = None
        try:
            progress_fd = open_folder("progress", devforgeai_fd)
            fds.append(progress_fd)
            runs_fd = open_folder("runs", progress_fd)
            fds.append(runs_fd)
            kept = kept_by_runs(runs_fd)
        except OSError:
            kept = set()
        for dirpath, _, files, here in os.fwalk(".", dir_fd=drafts_fd, follow_symlinks=False):
            base = os.path.normpath(dirpath)
            for name in files:
                rel = DRAFTS + (name if base == "." else base + "/" + name)
                try:
                    info = os.stat(name, dir_fd=here, follow_symlinks=False)
                    if (not stat.S_ISREG(info.st_mode) or info.st_mtime >= cutoff or rel in kept
                            or not is_work_file(rel, patterns)):
                        continue
                    if remove_file(name, here):
                        removed += 1
                except FileNotFoundError:
                    continue
                except OSError as err:
                    failure = failure or "%s: %s" % (rel, err.strerror or err)
    finally:
        for fd in reversed(fds):
            os.close(fd)
    return removed, failure


class Result:
    """What prune_all did: runs, sessions and work files removed, the manifests ignored, the first failure or None."""

    def __init__(self, runs=0, sessions=0, work=0, ignored=(), failure=None):
        self.runs, self.sessions, self.work, self.ignored, self.failure = runs, sessions, work, list(ignored), failure


def keeps_of(keep_run):
    if keep_run is None:
        return set()
    return {keep_run} if isinstance(keep_run, str) else set(keep_run)


def prune_all(root, days, keep_session, keep_run, now, manifests=None):
    """IF-04: the run and session folders, then, with a manifests folder, the work files. keep_run is a run ID, a
    collection of them, or None."""
    if not os.path.isdir(root):
        raise Fail("%s: not a folder" % root)
    if not supported():
        raise Fail("this platform can't remove folders without following links; nothing pruned")
    result, cutoff = Result(), now - days * DAY
    keep_runs = keeps_of(keep_run)
    root_fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY)
    fds = []
    try:
        try:
            fds.append(open_folder("devforgeai", root_fd))
            fds.append(open_folder("progress", fds[-1]))
        except OSError:
            # Missing, a link or not a folder: nothing of the tracker's to prune.
            fds = []
        if fds:
            for kind, pattern, keep in (("runs", RUN, keep_runs), ("sessions", SESSION, keeps_of(keep_session))):
                try:
                    kind_fd = open_folder(kind, fds[-1])
                except OSError:
                    continue
                try:
                    removed, failed = prune_kind(kind_fd, kind, pattern, keep, cutoff)
                finally:
                    os.close(kind_fd)
                setattr(result, kind, removed)
                result.failure = result.failure or failed
            _, failed = prune_logs(fds[-1], cutoff)
            result.failure = result.failure or failed
        if manifests is not None:
            patterns, result.ignored = work_patterns(manifests)
            if patterns:
                result.work, failed = age_work_files(root_fd, patterns, cutoff)
                result.failure = result.failure or failed
        return result
    finally:
        for fd in reversed(fds):
            os.close(fd)
        os.close(root_fd)


def prune(root, days, keep_session, keep_run, now):
    """(runs removed, sessions removed, the first failure or None): the folder pass alone."""
    result = prune_all(root, days, keep_session, keep_run, now)
    return result.runs, result.sessions, result.failure


def report(args, text):
    print("prune: %s" % text, file=sys.stderr)
    return 2


def main(argv=None):
    p = argparse.ArgumentParser(prog="prune.py", description="remove old run and session folders and a skill's work "
                                "files (SPEC-013 IF-04, IF-05)")
    sub = p.add_subparsers(dest="command", required=True)
    c = sub.add_parser("prune", help="remove run and session folders older than --days, and with --manifests work files")
    c.add_argument("--root", required=True, help="the project root")
    c.add_argument("--days", required=True, help="the retention period, a whole number of days of at least 1")
    c.add_argument("--keep-session", default=None, help="a session ID whose folder stays")
    c.add_argument("--keep-run", action="append", default=[], help="a run ID whose folder stays; may repeat")
    c.add_argument("--manifests", default=None, help="the plugin's manifests folder: also remove old work files")
    r = sub.add_parser("remove", help="remove the work files named by --file")
    r.add_argument("--root", required=True, help="the project root")
    r.add_argument("--manifests", required=True, help="the plugin's manifests folder, which gives the patterns")
    r.add_argument("--file", action="append", default=[], help="a project-relative path; use --file=PATH")
    args = p.parse_args(argv)
    try:
        if args.command == "remove":
            if not args.file:
                raise Fail("no --file given")
            removed, skipped, ignored, failure = remove_work(args.root, args.manifests, args.file)
            line = "".join(["removed %d work files, skipped %d" % (removed, skipped)] + ["; " + i for i in ignored])
        else:
            result = prune_all(args.root, days_of(args.days), args.keep_session, args.keep_run, time.time(),
                               args.manifests)
            failure = result.failure
            line = "pruned %d runs, %d sessions" % (result.runs, result.sessions)
            if args.manifests is not None:
                line += ", %d work files" % result.work
            line += "".join("; " + i for i in result.ignored)
    except Fail as why:
        return report(args, why)
    except OSError as err:
        return report(args, err)
    if failure is not None:
        return report(args, "%s; %s" % (line, failure))
    print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
