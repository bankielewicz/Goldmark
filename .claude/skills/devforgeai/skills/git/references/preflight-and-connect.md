# Preflight, connect and start

## Contents

- Preflight (step 1)
- The state report
- Claude Code's sandbox
- connect (step 3)
- start: branch and worktree (step 4)
- Carrying pending work onto the branch

## Preflight (step 1)

Run only read-only commands until the phases are chosen.

1. **Root and checkout.** `git rev-parse --show-toplevel`; compare `git rev-parse --git-dir` with
   `--git-common-dir` to tell the main checkout from a linked worktree. Keep every command's working
   directory at the root of the checkout it targets.
2. **Instructions.** Read `CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING*` and any ADR or runbook about
   branches, worktrees, commits, PRs, merges or deploys. Note what they set: worktree location,
   branch names, commit format, required checks and how to run them, merge method, post-merge steps,
   label names. Those win over this skill's defaults, except the no-data-loss and authorization
   rules.
3. **Tools and identity.** `git --version`; `git config user.name` and `user.email` must resolve
   before a commit (otherwise report it; never set them globally). ERR-01 when git is missing, or the
   directory isn't a repository and the request doesn't ask to initialize one: change nothing, say
   what is missing and that `connect` would initialize it, and report `blocked`.
4. **HEAD and operations.** A detached HEAD, or a merge, rebase, cherry-pick, revert or bisect in
   progress (the report's `operation_in_progress`), stops any phase that needs a branch (ERR-05).
   Never continue or abort the user's operation: describe it, give the commands that would finish
   or abort it (`git merge --continue` / `--abort`, `git rebase --continue` / `--abort`,
   `git cherry-pick --abort`, `git revert --abort`, `git bisect reset`), and report `blocked`.
5. **Remote.** For phases that use the remote: `git remote -v`, then `git ls-remote --symref origin HEAD`
   for reachability and the default branch (the `ref: refs/heads/<name>` line). Never assume `main`.
   An empty result from a reachable remote means the remote is empty.
6. **gh**, only before creating or updating a PR, reading PR state or merging: `gh --version` and
   `gh auth status`. The remote must be hosted on GitHub (`github.com` in its URL); another host is
   unsupported. Fetch, rebase and push need only git (ERR-02 below).
7. **Fetch** (`git fetch origin`, `--prune` for sync), then run the state report and base every
   later decision on it.

**ERR-02.** Without `gh`, not signed in, or a remote not on GitHub: everything that needs only git
still runs (commit, rebase, push, sync, prune by ancestry), and the run stops before creating or
updating a PR, reading PR state or merging. Put the command the user must run (`gh auth login`) in
Action required. With the remote unreachable, finish the local steps from already fetched refs and
stop before the first fetch or push. Never try another credential or host.

## The state report

```bash
python3 "${CLAUDE_SKILL_DIR}/scripts/repo_state.py" --default-branch <name from ls-remote>
```

Pass `--default-branch` whenever `refs/remotes/origin/HEAD` may be missing. It never fetches, so run
it after fetching. `-C <checkout>` reports on another checkout (sync uses the one holding the default
branch). For prune, pipe `gh pr list --state all --limit 200 --json
number,state,headRefName,headRefOid` into it with `--prs -`. Exit 2 means it didn't run: report its
`error` and decide nothing from it. Fields used below:

- `main_checkout`: the main working tree's absolute path from any checkout; `null` for a bare
  repository with linked worktrees.
- `in_linked_worktree`, `branch`, `detached`, `unborn`, `operation_in_progress`, `identity`.
- `remote`: name, URL and default branch, or `null` when there is no `origin`.
- `default_branch.state`: `up_to_date`, `behind`, `ahead`, `diverged`, `unrelated`, `no_remote`,
  `empty_remote` (fetched and empty) or `unknown` (fetch first), with `ahead`, `behind` and
  `checked_out_at` (the absolute path of the checkout holding it, ready for `-C`).
- `changes[]`: `path`, `status`, `staged`, `incoming` (`identical`, `differs` or `untouched`
  compared with `origin/<default>`; `identical` also requires the index to hold nothing found only
  there), and `sandbox_mask: true` for a sandbox write mask.
- `worktrees[]`: `path` (relative to the main checkout, so `-C` takes `<main_checkout>/<path>`),
  branch, head, lock, `prunable`, `current` (the checkout the session works in, even in a `-C`
  report), `uncommitted` and `untracked` (sandbox write masks excluded and counted in
  `sandbox_masks`), `ignored` (cut at 100, with `ignored_truncated` giving the full count),
  `unpushed`, `merged_by_ancestry`, `commits_since_created` (from the branch's reflog; `null` once
  its creation entry expired), `pr` and `nothing_unpushed` (with `--prs`), `last_activity`,
  `activity`, `modified_within_hour`, `size_bytes`.
- `claude_worktrees_ignored`, `sandbox.git_config_write_masked`, `id_collisions[]`.

## Claude Code's sandbox

A sandboxed session can write only inside the project, which is why worktrees go under it. Inside the
sandbox:
- **Write masks.** `git status` lists empty character devices owned by `nobody` (`.bashrc`,
  `.gitconfig`, `.mcp.json`, `.vscode`, …) that don't exist outside the sandbox. The report marks
  them `sandbox_mask: true`. They are `local`: never stage or delete them, and propose no ignore
  pattern for them.
- **`.git/config` masked** (`sandbox.git_config_write_masked`): every `.git/config` write fails:
  `git remote add`, `git config`, `push -u`, and a branch created from a remote-tracking ref without
  `--no-track`. Plan around it: create branches with `--no-track`, push without `-u`.
- **Exclusions match the start of a command.** A `sandbox.excludedCommands` entry such as
  `git push *` applies only to a command that starts with those words, which is why `git push` and
  `gh pr` always run as commands of their own (publish-and-merge.md).
- **ERR-16.** When the sandbox, or a harness or permission guard (such as the worktree isolation
  guard of an EnterWorktree session), refuses a command a phase needs (a `.git/config` write, a
  push or `gh` call rejected by the network proxy, a worktree path outside the writable area, or any
  command a guard refuses): don't retry around it (no other path, command form, copy of a script or
  tool) and never change sandbox or permission settings. Finish the steps that don't need it, then
  give the exact command for the user to run in a plain shell and, for the sandbox, the settings
  change that would allow it (for example adding `git push *` or `gh pr *` to
  `sandbox.excludedCommands` in `.claude/settings.local.json`), which is the user's decision.
  Report `partial` or `blocked`.

## connect (step 3)

1. **Not a repository** and the request asks to initialize it: `git init -b <branch>`, where the
   branch is the one the repository's instructions name, else the remote's default when the remote
   has commits, else git's `init.defaultBranch`.
2. **A URL given.** No `origin`: `git remote add origin <url>`. For a local path, store an absolute
   path (a relative one resolves against the current directory and breaks in linked worktrees).
   `origin` already names the same repository: report it; `https://github.com/o/r(.git)` and
   `git@github.com:o/r(.git)` are the same repository. **`origin` points elsewhere (ERR-03):** keep it
   unchanged, show both URLs, and ask; change it (`git remote set-url`) or add the URL under another
   name only after a confirmation. Report `awaiting_approval`.
3. **Fetch** (`git fetch origin`), then act on the two histories:

| Local | Remote | Action |
|---|---|---|
| No commits | Has commits | Adopt the remote's history: check out its default branch, reconciling any local files by sync's per-path rule ([sync-and-prune.md](sync-and-prune.md)) |
| Has commits | Empty | Pushing the default branch is the bootstrap: outward-facing, so only when the request names the push; otherwise offer it |
| No commits | Empty | Offer (a) a minimal bootstrap commit on the default branch holding the ignore file, and the README when one exists, so the rest arrives by PR (recommended), or (b) the whole classified change set as the first commit. Creating and pushing either needs the user's choice |
| Has commits | Has commits, merge base | Normal: continue with the requested phases |
| Has commits | Has commits, no merge base | ERR-04 |

**ERR-04, unrelated histories.** Push nothing. Name both histories' latest commits (SHA and
subject), explain that they share no commit, and offer: (a) move the local commits onto a new
branch based on `origin/<default>` and open a PR (recommended), or (b) stop. Never force-push, and
never use `--allow-unrelated-histories` without a confirmation. Report `awaiting_approval`.

## start: branch and worktree (step 4)

**Branch name.** The repository's convention (for example `story/STORY-NNN-<slug>`), else
`<type>/<slug>` with type `feat`, `fix`, `docs`, `chore`, `refactor` or `test`, and the story or
spec ID in the slug when the work has one; lower case, at most 64 characters. A name the user gives
wins. Never reuse a local branch, remote branch or worktree name for different work. An existing
worktree for the same branch is reused.

**Location.** The repository's rule, else `.claude/worktrees/<name>`, where `<name>` is the branch
with `/` replaced by `-`. It sits inside the project, where the sandbox lets a session write, and
it's where `claude --worktree <name>` looks. Either way it is relative to the **main checkout**,
never to the checkout the run started in: build `<main_checkout>/.claude/worktrees/<name>` from the
state report's `main_checkout` and give `git worktree add` that absolute path. A relative path run
from a linked worktree nests the new worktree inside it, and `git worktree remove` of the outer one
then deletes the inner one's files without a warning.
- `main_checkout` is `null` (a bare repository with linked worktrees, no main checkout): ask where
  to put the worktree, and report `awaiting_approval`.
- The sandbox or a guard refuses the path: ERR-16. Never fall back to a path inside the current
  worktree.

**Keep it out of git.** Read the report's `claude_worktrees_ignored` (`git check-ignore` on the main
checkout). Only when it is false: append `.claude/worktrees/` to `.git/info/exclude`, which changes
no tracked file and covers every worktree of the repository, and say that a `.gitignore` entry would
share the rule. When the repository's rules require the `.gitignore` entry instead, add it there and
name it in the next commit's message. Never edit `.gitignore` otherwise.
- Write it with exactly one of these commands, as a Bash call of its own:
  - with the working directory at the main checkout's root (the usual case):
    `echo '.claude/worktrees/' >> .git/info/exclude`;
  - from a linked worktree, where `.git` is a file:
    `echo '.claude/worktrees/' >> <main_checkout>/.git/info/exclude`.

  Claude Code protects `.git/`: file tools can't edit it, a permission check refuses compound
  commands that also `mkdir` or `printf` there, and a headless session may refuse this write in
  either form, which is ERR-16 (below). `git init` already created `.git/info/`.
- If that write is still refused, don't retry it another way. Create the worktree anyway (nothing
  in this skill stages it), and put the command in Action required for the user, in its absolute
  form so it works from any directory.

**Base.** New work branches from `origin/<default>` as just fetched, never from a possibly stale local
default branch:

```bash
git worktree add --no-track -b <branch> <main_checkout>/.claude/worktrees/<name> origin/<default>
```

`--no-track` keeps the command free of `.git/config` writes. Then move the session in with
EnterWorktree (`path`) when that tool is available and the user continues there. Otherwise the
session hasn't moved, even if a Bash command ran `cd` into the worktree: never say it is working
there. Give the user the command that opens it, `claude --worktree <name>` from the main checkout,
in Action required. Run any setup the
repository requires in a new worktree, or report it as the user's step when the session can't.
`start` without carried work commits nothing, and `start` never pushes.

## Carrying pending work onto the branch

When the work to deliver is uncommitted in the current checkout (usually the main checkout on the
default branch), move it onto the new branch by a commit, never by stash, file copies or patches
unless the user chooses one:

0. **Commits outside the default branch, from any checkout.** Read
   `git log --format='%H %s' origin/<default>..HEAD` after fetching. Every listed commit would enter
   a branch made at HEAD, including commits already pushed on another feature branch. Apply this
   check on the default branch, any other branch and a linked worktree when carrying work to a
   separate branch. If the request explicitly includes every listed commit, reuse that choice and
   continue; an empty list also needs no question. Otherwise stop before staging or creating a
   branch, list the SHAs and subjects, and ask whether to (a) stop (recommended), or (b) include
   those commits. With no answer possible, report `awaiting_approval`; with (a), report `blocked`
   and leave everything as it was. A failed log or missing base is not an empty list: report the
   error and stop; a confirmed empty remote follows `connect`'s bootstrap rule. If both histories
   exist but share no commit, use ERR-04 before offering to carry anything.
1. Classify, stage, scan and check it ([classify-and-commit.md](classify-and-commit.md)).
2. Record the pre-move content of each task path: `git hash-object <path>` (absent for a deletion).
3. `git switch -c <branch>` at the current HEAD; the staged task paths and every other edit come
   along.
4. Commit (classify-and-commit.md). Leave unrelated and local paths unstaged.
5. **Verify** before switching back: for each task path, `git rev-parse <branch>:<path>` equals the
   recorded hash, and a deleted path is absent from the branch. On a mismatch, stay on the new
   branch and report it.
6. `git switch <original branch>`: it carries the unrelated edits unchanged.
7. `git worktree add <main_checkout>/.claude/worktrees/<name> <branch>` (location and ignore rule
   above).

A path that mixes task and unrelated edits is `uncertain`: ask whether to carry the whole file or
leave it. When the current checkout is a linked worktree or a non-default branch already dedicated
to this work (the branch the request names, or the task's own), commit there: no new branch or
worktree is needed. Rebasing the new branch onto
`origin/<default>` happens before the push, inside the worktree
([publish-and-merge.md](publish-and-merge.md)).
