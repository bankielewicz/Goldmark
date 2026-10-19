#!/usr/bin/env python3
"""Read-only JSON report of a git repository's state for the devforgeai git skill (SPEC-007 §4).

Usage:
    python3 repo_state.py [-C PATH] [--remote NAME] [--default-branch NAME]
                          [--idle-days 14] [--stale-days 30] [--prs -]

Prints one JSON object on stdout. It never uses the network and never writes: every git call runs
with GIT_OPTIONAL_LOCKS=0, and it avoids commands that refresh the index (such as a plain
`git diff`). Remote-tracking refs are only as fresh as the last fetch, so fetch before trusting
`default_branch`. Pass --default-branch with the name `git ls-remote --symref <remote> HEAD`
reported when refs/remotes/<remote>/HEAD is missing. With `--prs -` it reads
`gh pr list --state all --json number,state,headRefName,headRefOid` on stdin and adds each
worktree's `pr` and `nothing_unpushed` (SPEC-007 BEH-16). Paths need not be UTF-8: git's output is
decoded with surrogateescape, and JSON escapes what isn't. Exit status: 0 with a report; 2 with
{"error": ...} when git is missing, the directory is not a git repository, or anything else fails
(the report didn't run).
"""
import argparse
import datetime as dt
import json
import os
import re
import shutil
import stat
import subprocess
import sys

ENV = dict(os.environ, GIT_OPTIONAL_LOCKS="0", GIT_LITERAL_PATHSPECS="1", LC_ALL="C",
           GIT_TERMINAL_PROMPT="0")
SPEC_DOC = re.compile(r"^docs/specs/[^/]+/[^/]+$")
IGNORED_LIST_MAX = 100


def git(args, cwd, check=False, stdin=None, env=None):
    """Run git and return (returncode, stdout). Output and stdin are UTF-8 with surrogateescape, so a
    path that isn't UTF-8 round-trips to the file system and back to git."""
    r = subprocess.run(["git", *args], cwd=cwd, env=env or ENV, input=stdin, capture_output=True,
                       encoding="utf-8", errors="surrogateescape")
    if check and r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)}: {r.stderr.strip()}")
    return r.returncode, r.stdout


def out(args, cwd):
    """stdout of a git command stripped, or None when it fails."""
    rc, o = git(args, cwd)
    return o.strip() if rc == 0 else None


def rev(ref, cwd):
    return out(["rev-parse", "-q", "--verify", f"{ref}^{{commit}}"], cwd)


def iso(ts):
    return dt.datetime.fromtimestamp(ts, dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------------------------------------
# Repository basics


def operation_in_progress(git_dir):
    def has(name):
        return os.path.exists(os.path.join(git_dir, name))
    if has("rebase-merge") or has("rebase-apply"):
        return "rebase"
    if has("MERGE_HEAD"):
        return "merge"
    if has("CHERRY_PICK_HEAD"):
        return "cherry-pick"
    if has("REVERT_HEAD"):
        return "revert"
    if has("BISECT_LOG") or has("BISECT_START"):
        return "bisect"
    return None


def identity(cwd):
    def resolved(key, envs):
        return out(["config", "--get", key], cwd) not in (None, "") or any(os.environ.get(e) for e in envs)
    return {"name_set": resolved("user.name", ("GIT_AUTHOR_NAME", "GIT_COMMITTER_NAME")),
            "email_set": resolved("user.email", ("GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL", "EMAIL"))}


def remote_info(cwd, remote, default_arg, git_dir, common_dir):
    remotes = (out(["remote"], cwd) or "").split()
    if remote not in remotes:
        return None, remotes
    url = out(["remote", "get-url", remote], cwd)
    default = default_arg
    if not default:
        sym = out(["symbolic-ref", "-q", f"refs/remotes/{remote}/HEAD"], cwd)
        if sym and sym.startswith(f"refs/remotes/{remote}/"):
            default = sym[len(f"refs/remotes/{remote}/"):]
    tracking = (out(["for-each-ref", "--format=%(refname)", f"refs/remotes/{remote}/"], cwd) or "").split()
    tracking = [t for t in tracking if t != f"refs/remotes/{remote}/HEAD"]
    # FETCH_HEAD is per worktree: a fetch run in a linked worktree writes that worktree's own copy.
    fetch_head = next((p for p in (os.path.join(git_dir, "FETCH_HEAD"), os.path.join(common_dir, "FETCH_HEAD"))
                       if os.path.isfile(p)), None)
    empty = not tracking and fetch_head is not None and os.path.getsize(fetch_head) == 0
    return {"name": remote, "url": url, "default_branch": default, "fetched_empty": empty}, remotes


def default_branch_state(cwd, remote, info, worktrees):
    name = info["default_branch"] if info else None
    local = rev(f"refs/heads/{name}", cwd) if name else None
    remote_sha = rev(f"refs/remotes/{remote}/{name}", cwd) if (info and name) else None
    res = {"name": name, "local": local, "remote": remote_sha, "ahead": None, "behind": None,
           "state": None, "checked_out_at": None}
    if name:
        for w in worktrees:
            if w.get("branch") == name:
                res["checked_out_at"] = w["path"]
    if info is None:
        res["state"] = "no_remote"
        return res
    if info["fetched_empty"]:
        res["state"] = "empty_remote"
        return res
    if not name or not remote_sha:
        res["state"] = "unknown"
        return res
    if not local:
        rc, o = git(["rev-list", "--count", remote_sha], cwd)
        res.update(ahead=0, behind=int(o.strip()) if rc == 0 else None, state="behind")
        return res
    if out(["merge-base", local, remote_sha], cwd) is None:
        res["state"] = "unrelated"
        return res
    rc, o = git(["rev-list", "--left-right", "--count", f"{local}...{remote_sha}"], cwd)
    ahead, behind = (int(x) for x in o.split())
    res.update(ahead=ahead, behind=behind)
    res["state"] = ("up_to_date" if not ahead and not behind else "behind" if not ahead
                    else "ahead" if not behind else "diverged")
    return res


# ---------------------------------------------------------------------------------------------
# Working-tree changes


def parse_status(raw):
    """Parse `git status --porcelain=v2 -z` into [{path, status, staged, unstaged, orig_path}]."""
    names = {"M": "modified", "T": "type_changed", "A": "added", "D": "deleted", "R": "renamed",
             "C": "copied", "U": "unmerged"}
    items = raw.split("\0")
    res, i = [], 0
    while i < len(items):
        e = items[i]
        i += 1
        if not e or e.startswith("#"):
            continue
        kind = e[0]
        if kind == "?":
            res.append({"path": e[2:], "status": "untracked", "staged": False, "unstaged": True})
        elif kind == "!":
            continue
        elif kind in "12u":
            parts = e.split(" ")
            xy = parts[1]
            if kind == "1":
                path = " ".join(parts[8:])
                orig = None
            elif kind == "2":
                path = " ".join(parts[9:])
                orig = items[i]
                i += 1
            else:
                path = " ".join(parts[10:])
                orig = None
            x, y = xy[0], xy[1]
            if kind == "u":
                status = "unmerged"
            else:
                status = names.get(x if x != "." else y, "modified")
            entry = {"path": path, "status": status, "staged": x != ".", "unstaged": y != "."}
            if orig:
                entry["orig_path"] = orig
            res.append(entry)
    return res


def tree_entries(cwd, treeish, paths):
    """{path: (mode, blob)} for the given paths in treeish (absent paths are missing)."""
    if not treeish or not paths:
        return {}
    res = {}
    for chunk in range(0, len(paths), 200):
        rc, o = git(["ls-tree", "-r", "-z", "--full-tree", treeish, "--", *paths[chunk:chunk + 200]], cwd)
        if rc != 0:
            continue
        for rec in o.split("\0"):
            if not rec:
                continue
            meta, path = rec.split("\t", 1)
            mode, _type, blob = meta.split()
            res[path] = (mode, blob)
    return res


def index_entries(cwd, paths):
    """{path: (mode, blob)} for the index's stage-0 entries (ls-files -s doesn't refresh the index); a
    path with conflict stages maps to ("unmerged", None)."""
    res = {}
    for chunk in range(0, len(paths), 200):
        rc, o = git(["ls-files", "-s", "-z", "--", *paths[chunk:chunk + 200]], cwd)
        for rec in o.split("\0") if rc == 0 else ():
            if not rec:
                continue
            meta, path = rec.split("\t", 1)
            mode, blob, stage = meta.split()
            res[path] = (mode, blob) if stage == "0" else ("unmerged", None)
    return res


UNHASHED = ("unhashed", None)  # a file git couldn't hash: never equal to any version, so never identical


def worktree_entries(root, paths):
    """{path: (mode, blob)} for the working-tree versions of paths (absent paths are missing). A file
    git can't hash (unreadable, or gone mid-run) maps to UNHASHED rather than looking absent."""
    res, files = {}, []
    for p in paths:
        full = os.path.join(root, p)
        try:
            st = os.lstat(full)
        except OSError:
            continue
        if stat.S_ISLNK(st.st_mode):
            rc, o = git(["hash-object", "--stdin"], root, stdin=os.readlink(full))
            if rc == 0:
                res[p] = ("120000", o.strip())
        elif stat.S_ISREG(st.st_mode):
            files.append((p, "100755" if st.st_mode & 0o111 else "100644"))
        elif stat.S_ISDIR(st.st_mode):
            res[p] = ("160000", None)  # a submodule or nested repository
    if files:
        rc, o = git(["hash-object", "--stdin-paths"], root, stdin="\n".join(p for p, _ in files) + "\n")
        blobs = o.split() if rc == 0 else []
        if len(blobs) == len(files) and not any("\n" in p for p, _ in files):
            for (p, mode), blob in zip(files, blobs):
                res[p] = (mode, blob)
        else:  # one file failed the batch: hash each on its own, so the rest keep their answer
            for p, mode in files:
                rc, o = git(["hash-object", "--", p], root)
                res[p] = (mode, o.strip()) if rc == 0 and o.strip() else UNHASHED
    return res


def is_sandbox_mask(full_path):
    """Claude Code's sandbox shows write masks as character devices, in every checkout of the repo."""
    try:
        return stat.S_ISCHR(os.lstat(full_path).st_mode)
    except OSError:
        return False


def changes(root, head, incoming_ref):
    rc, raw = git(["status", "--porcelain=v2", "-z", "--untracked-files=all"], root)
    items = parse_status(raw) if rc == 0 else []
    paths = sorted({c["path"] for c in items} | {c["orig_path"] for c in items if c.get("orig_path")})
    if incoming_ref:
        at_head = tree_entries(root, head, paths)
        at_incoming = tree_entries(root, incoming_ref, paths)
        in_tree = worktree_entries(root, paths)
        in_index = index_entries(root, paths)
    for c in items:
        if c["status"] == "untracked" and is_sandbox_mask(os.path.join(root, c["path"])):
            c["sandbox_mask"] = True  # an empty character device the sandbox shows; never stage it
        if not incoming_ref:
            c["incoming"] = None
            continue
        p = c["path"]
        if at_head.get(p) == at_incoming.get(p):
            c["incoming"] = "untouched"
        elif in_tree.get(p) == at_incoming.get(p) and in_index.get(p) in (at_head.get(p), at_incoming.get(p)):
            # The working copy is upstream already, and the index holds nothing found only there.
            c["incoming"] = "identical"
        else:
            c["incoming"] = "differs"
    return items


# ---------------------------------------------------------------------------------------------
# Worktrees


def parse_worktrees(raw):
    res, cur = [], None
    for line in raw.split("\n"):
        if not line:
            if cur:
                res.append(cur)
            cur = None
            continue
        key, _, val = line.partition(" ")
        if key == "worktree":
            cur = {"abs": val, "branch": None, "head": None, "bare": False, "detached": False,
                   "locked": None, "prunable": False}
        elif cur is None:
            continue
        elif key == "HEAD":
            cur["head"] = val
        elif key == "branch":
            cur["branch"] = val[len("refs/heads/"):] if val.startswith("refs/heads/") else val
        elif key == "bare":
            cur["bare"] = True
        elif key == "detached":
            cur["detached"] = True
        elif key == "locked":
            cur["locked"] = val or "locked"
        elif key == "prunable":
            cur["prunable"] = True
    if cur:
        res.append(cur)
    return res


def newest_mtime(path):
    """Latest mtime among a worktree's tracked and untracked files (ignored files don't count)."""
    rc, o = git(["ls-files", "-z", "--cached", "--others", "--exclude-standard"], path)
    newest = None
    for p in set(o.split("\0")) if rc == 0 else ():
        if not p:
            continue
        try:
            m = os.lstat(os.path.join(path, p)).st_mtime
        except OSError:
            continue
        newest = m if newest is None or m > newest else newest
    return newest


def commits_since_created(cwd, branch, head):
    """Commits on branch since it was created, from its reflog's oldest entry ("branch: Created from
    ..."); None when that entry has expired or the branch was made another way (a clone, a rename)."""
    rc, o = git(["log", "-g", "--format=%H%x09%gs", f"refs/heads/{branch}", "--"], cwd)
    lines = [ln for ln in o.split("\n") if ln] if rc == 0 else []
    if not lines or not head:
        return None
    created, _, subject = lines[-1].partition("\t")
    if not subject.startswith("branch: Created from"):
        return None
    rc, o = git(["rev-list", "--count", f"{created}..{head}"], cwd)
    return int(o.strip()) if rc == 0 else None


def branch_pr(prs, branch, head):
    """The branch's PR from `gh pr list` data: the one whose head is the tip, else the newest."""
    mine = [p for p in prs if p.get("headRefName") == branch]
    if not mine:
        return None
    pr = next((p for p in mine if p.get("headRefOid") == head), None) or max(mine, key=lambda p: p.get("number") or 0)
    return {"number": pr.get("number"), "state": pr.get("state"), "head_matches": pr.get("headRefOid") == head}


def dir_size(path):
    total = 0
    for base, dirs, files in os.walk(path):
        for n in files + [d for d in dirs if os.path.islink(os.path.join(base, d))]:
            try:
                total += os.lstat(os.path.join(base, n)).st_size
            except OSError:
                pass
    return total


def worktree_report(w, main_root, remote, default, has_remote, now, idle_days, stale_days, current_root, prs):
    path = w["abs"]
    rel = os.path.relpath(path, main_root)
    entry = {"path": rel if not rel.startswith("..") else path, "branch": w["branch"], "head": w["head"],
             "main": w.get("main", False), "detached": w["detached"], "locked": w["locked"],
             "prunable": w["prunable"], "current": os.path.realpath(path) == os.path.realpath(current_root)}
    if w["prunable"] or not os.path.isdir(path):
        entry["missing"] = True
        return entry
    rc, raw = git(["status", "--porcelain=v2", "-z", "--untracked-files=all"], path)
    items = parse_status(raw) if rc == 0 else []
    masks = [c for c in items if c["status"] == "untracked" and is_sandbox_mask(os.path.join(path, c["path"]))]
    items = [c for c in items if c not in masks]   # masks aren't the user's files: they don't block removal
    entry["uncommitted"] = len(items)
    entry["untracked"] = sum(1 for c in items if c["status"] == "untracked")
    entry["sandbox_masks"] = len(masks)
    if w.get("main"):
        entry["ignored"] = None  # the main checkout is never a prune candidate
    else:
        rc, o = git(["ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--directory"], path)
        ignored = sorted(p for p in o.split("\0") if p) if rc == 0 else []
        entry["ignored"] = ignored[:IGNORED_LIST_MAX]
        if len(ignored) > IGNORED_LIST_MAX:
            entry["ignored_truncated"] = len(ignored)
    head = w["head"]
    if head and has_remote:
        rc, o = git(["rev-list", "--count", head, "--not", f"--remotes={remote}"], path)
        entry["unpushed"] = int(o.strip()) if rc == 0 else None
    else:
        entry["unpushed"] = None
    target = rev(f"refs/remotes/{remote}/{default}", path) if (default and has_remote) else None
    if head and target:
        rc, _ = git(["merge-base", "--is-ancestor", head, target], path)
        entry["merged_by_ancestry"] = rc == 0
    else:
        entry["merged_by_ancestry"] = None
    branch = w["branch"]
    entry["commits_since_created"] = (commits_since_created(path, branch, head)
                                      if branch and not w.get("main") else None)
    entry["pr"] = branch_pr(prs, branch, head) if (prs is not None and branch) else None
    # BEH-16: nothing unpushed when every commit is on a remote ref, or the tip is a MERGED PR's head.
    merged_head = bool(entry["pr"] and entry["pr"]["state"] == "MERGED" and entry["pr"]["head_matches"])
    entry["nothing_unpushed"] = (True if entry["unpushed"] == 0 or merged_head
                                 else None if entry["unpushed"] is None else False)
    commit_ts = None
    if head:
        ts = out(["log", "-1", "--format=%ct", head], path)
        commit_ts = int(ts) if ts else None
    entry["last_commit"] = iso(commit_ts)[:10] if commit_ts else None
    file_ts = newest_mtime(path)
    last = max(t for t in (commit_ts, file_ts) if t is not None) if (commit_ts or file_ts) else None
    entry["last_activity"] = iso(last) if last else None
    if last is None:
        entry["activity"] = None
    else:
        age_days = (now - last) / 86400
        entry["activity"] = "stale" if age_days >= stale_days else "idle" if age_days >= idle_days else "active"
    entry["modified_within_hour"] = file_ts is not None and now - file_ts < 3600
    entry["size_bytes"] = None if w.get("main") else dir_size(path)
    return entry


# ---------------------------------------------------------------------------------------------
# docs/specs ID collisions


def id_collisions(root, remote_ref):
    if not remote_ref:
        return []
    base = out(["merge-base", "HEAD", remote_ref], root)
    if not base:
        return []
    rc, o = git(["diff", "--name-only", "-z", "--no-renames", "--diff-filter=A", base, "HEAD", "--", "docs/specs"], root)
    added = [p for p in o.split("\0") if p and SPEC_DOC.match(p)] if rc == 0 else []
    res = []
    ours = tree_entries(root, "HEAD", added)
    theirs = tree_entries(root, remote_ref, added)
    for p in added:
        if p in theirs and theirs[p][1] != ours.get(p, (None, None))[1]:
            res.append({"path": p, "id": os.path.splitext(os.path.basename(p))[0],
                        "branch_blob": ours.get(p, (None, None))[1], "base_blob": theirs[p][1]})
    return res


# ---------------------------------------------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("-C", dest="cwd", default=".", help="run as if started in this directory")
    ap.add_argument("--remote", default="origin")
    ap.add_argument("--default-branch", default=None)
    ap.add_argument("--idle-days", type=float, default=14)
    ap.add_argument("--stale-days", type=float, default=30)
    ap.add_argument("--prs", choices=["-"], default=None,
                    help="read `gh pr list --state all --json number,state,headRefName,headRefOid` on stdin")
    ap.add_argument("--now", default=None, help=argparse.SUPPRESS)  # ISO time, for tests
    a = ap.parse_args(argv)
    try:
        return report(a)
    except Exception as e:  # any failure is "the report didn't run", never a partial report
        print(json.dumps({"error": f"{type(e).__name__}: {e}"}))
        return 2


def read_prs():
    prs = json.load(sys.stdin)
    if not isinstance(prs, list) or not all(isinstance(p, dict) for p in prs):
        raise ValueError("--prs expects gh pr list --json output: a list of objects")
    return prs


def report(a):
    if shutil.which("git") is None:
        print(json.dumps({"error": "git_not_found"}))
        return 2
    cwd = os.path.abspath(a.cwd)
    root = out(["rev-parse", "--show-toplevel"], cwd)
    if root is None:
        print(json.dumps({"error": "not_a_git_repository", "path": cwd}))
        return 2
    git_dir = os.path.realpath(os.path.join(root, out(["rev-parse", "--git-dir"], root)))
    common_dir = os.path.realpath(os.path.join(root, out(["rev-parse", "--git-common-dir"], root)))
    now = (dt.datetime.fromisoformat(a.now.replace("Z", "+00:00")).timestamp() if a.now
           else dt.datetime.now(dt.timezone.utc).timestamp())
    prs = read_prs() if a.prs else None

    branch = out(["symbolic-ref", "-q", "--short", "HEAD"], root)
    head = rev("HEAD", root)
    info, remotes = remote_info(root, a.remote, a.default_branch, git_dir, common_dir)
    default = info["default_branch"] if info else None
    incoming_ref = rev(f"refs/remotes/{a.remote}/{default}", root) if (info and default) else None

    rc, raw = git(["worktree", "list", "--porcelain"], root)
    wts = parse_worktrees(raw) if rc == 0 else []
    if wts:
        wts[0]["main"] = True
    # A bare common directory has no main checkout: report null, and keep paths relative to this one.
    main_checkout = wts[0]["abs"] if wts and not wts[0]["bare"] else None
    main_root = main_checkout or root
    # `current` is the checkout the caller works in, even with -C (sync reports on the checkout holding
    # the default branch from inside another worktree); a caller outside this repository falls back to -C.
    here = out(["rev-parse", "--show-toplevel"], os.getcwd())
    here_common = out(["rev-parse", "--git-common-dir"], here) if here else None
    same_repo = here_common and os.path.realpath(os.path.join(here, here_common)) == common_dir
    current_root = here if same_repo else root

    lock = os.path.join(common_dir, "config.lock")
    try:
        masked = stat.S_ISCHR(os.lstat(lock).st_mode)
    except OSError:
        masked = False

    result = {
        "root": root,
        "main_checkout": main_checkout,
        "in_linked_worktree": git_dir != common_dir,
        "branch": branch,
        "head": head,
        "detached": branch is None and head is not None,
        "unborn": head is None,
        "operation_in_progress": operation_in_progress(git_dir),
        "identity": identity(root),
        "remote": ({k: v for k, v in info.items() if k != "fetched_empty"} if info else None),
        "other_remotes": [r for r in remotes if r != a.remote],
        # checked_out_at is absolute: it is passed to -C, which resolves against the caller's directory.
        "default_branch": default_branch_state(root, a.remote, info, [
            {"branch": w["branch"], "path": w["abs"]} for w in wts]),
        "changes": changes(root, head, incoming_ref),
        "worktrees": [worktree_report(w, main_root, a.remote, default, info is not None, now,
                                      a.idle_days, a.stale_days, current_root, prs)
                      for w in wts if not w["bare"]],
        # check-ignore rejects GIT_LITERAL_PATHSPECS (exit 128), so it runs without it.
        "claude_worktrees_ignored": git(["check-ignore", "-q", "--no-index", ".claude/worktrees/"], main_root,
                                        env={k: v for k, v in ENV.items() if k != "GIT_LITERAL_PATHSPECS"})[0] == 0,
        "sandbox": {"git_config_write_masked": masked},
        "id_collisions": id_collisions(root, incoming_ref) if branch != default else [],
    }
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
