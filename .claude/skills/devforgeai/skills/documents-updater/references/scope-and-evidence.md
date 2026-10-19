# Scope and evidence

Read at step 1. Covers steps 1 and 2: the before state, attribution, and the evidence inventory.

## Contents

1. Resolving the before state
2. Attributing local edits
3. When git can't answer
4. The evidence inventory
5. Git commands

## 1. Resolving the before state

The **target** is the current working tree, including relevant local additions, unless the user
asks for a committed snapshot. The **before state** is what the target is compared with. Take the
first rule that applies:

| # | Source | Use it when | Base |
|---|---|---|---|
| 1 | The user | They gave a revision, tag or range, or named the scope ("my uncommitted changes"), now or earlier in the conversation | That revision; for `A..B`, the target is `B`; for the uncommitted changes, `HEAD` |
| 2 | Captured task-start state | This session did the work being documented, and the conversation recorded the state before it: the git status snapshot at session start, a commit or stash made then, or the files the session itself edited | That state; see §2 for edits that already existed then |
| 3 | The branch's integration branch | The scope is the branch's work, and the integration branch is known from the user, the repository's instructions (for example "PRs target `develop`") or the remote's default branch (`refs/remotes/<remote>/HEAD`) | `git merge-base HEAD <integration-branch>` |
| 4 | `HEAD` | The scope is only the current uncommitted changes, for example when the work the request describes exists only in them | `HEAD` |

- Claude Code records a git status when every session begins. When the session began with the
  documentation request, the work was done before it: that snapshot shows the target, not the
  base, so rule 2 doesn't apply. Use rules 3 and 4.
- A branch's own tracking branch (`origin/feature-x` for `feature-x`) is not its integration branch.
- A local branch that happens to be called `main` or `master` is not evidence that the work started
  there. With no other evidence, ask.
- Never silently fall back to the last commit, the latest tag, the root commit or an assumed `main`.
  When no rule applies, ask for the comparison point (SKILL.md, "Asking"). Until it is answered, make
  no historical change claims: no changelog entries, no "now supports" statements, and no draft of
  what each possible base would add, even in the reply. Listing the candidate commits (hash, date,
  subject) so the user can pick one is fine. Corrections of plainly wrong current facts may
  proceed.
- A given revision that doesn't resolve (`git rev-parse --verify --quiet "<rev>^{commit}"` fails)
  stops the run with `Result: blocked`. Name the revision, and list the nearest branches and tags.

An ambiguity that doesn't change the result needs no question. For example, when "my latest
changes" could mean the uncommitted work or also the last commit, and the documents come out the
same either way, name the reading you used.

State the selected scope in the `Scope` line. Mention uncertainty only when it is material:

| Situation | Example `Scope` line |
|---|---|
| Uncommitted work | `Scope: uncommitted changes against HEAD (a1b2c3d)` |
| Branch work | `Scope: merge-base with origin/main (9f8e7d6)..working tree` |
| No commits yet | `Scope: repository with no commits; initial state of the working tree` |
| No git | `Scope: no git history (current files only)` |
| Unresolved | `Scope: comparison point unresolved (no revision given, no remote, tag or integration branch)` |

## 2. Attributing local edits

This applies only when this session did the work (rule 2). When the task began with local changes,
a starting commit alone can't tell which edits belong to the task.
- Files that were clean at the start and changed since belong to the task.
- Files that were already modified at the start belong to the task only where the conversation
  shows what the session changed (it read the file first, or made a specific edit).
- Exclude edits that clearly predate the task. Leave any edit whose attribution is uncertain
  undocumented, and name it in the reply. Never attribute it to the task.
- What the user says is part of the task wins over the captured start state: "the `--exclude` work in
  my uncommitted changes" includes a file that was already modified when the session began.

## 3. When git can't answer

| Situation | Handling |
|---|---|
| Not a git repository, or git unavailable | Work from the current files: correct current facts and create missing documents from them. Make no claim about what changed. `Scope: no git history (current files only)` |
| A git repository with no commits | Document the project's initial state from the current files. Invent no release history and no changelog versions |
| No rule in §1 applies | Ask for the comparison point, then proceed. With no answer: report `partial` (if you changed or proposed anything) or `blocked` |

## 4. The evidence inventory

Inspect every committed, staged, unstaged and untracked change in scope (§5). Read the affected
source, configuration, tests, specifications and documents far enough to understand each outcome,
not just the diff lines.

Keep one working note per meaningful outcome, in your own context and never in a document (unless
the project requires an evidence record):

```text
Outcome: What changed in the resulting project state?
Audience: Who needs to know?
Evidence: Which diff and current file support the claim?
Action: Does anyone need to change usage, configuration or deployment?
Destination: Which existing section, or necessary new document, should explain it?
```

**Confirming claims.** An earlier summary, a commit message or the task description tells you
where to look, not what is true. Confirm each substantive claim in the repository.

**Status words.** Use only the status the evidence supports:

| Status | Evidence that supports it |
|---|---|
| Specified | A specification, design or requirement describes it |
| Implemented | The code or configuration at the target does it |
| Tested | A test exists **and** a run of it passed in this session or in a recorded result. A test file alone is not "tested" |
| Released | A tag, published version or release record includes it. Unreleased by default |

A diff never shows that tests passed, that a feature was deployed or released, or that anything is
faster, more reliable or more secure.

**Grouping.** Several edits that deliver one result are one outcome. Leave out reverted and
superseded intermediate attempts: the documents describe the resulting state, not the path to it.

**Requirement conflicts.** When the implementation contradicts an accepted requirement or
specification, document only what the evidence supports and put the discrepancy under
`Action required`. Never rewrite the requirement to match the implementation.

## 5. Git commands

Command templates, not a script. Run them from the repository root, and replace `<BASE>` with the
resolved commit. All are read-only.

| Purpose | Command |
|---|---|
| Local state | `git status --short --untracked-files=all` |
| Unstaged changes | `git diff --` |
| Staged changes | `git diff --cached --` |
| Untracked, non-ignored files | `git ls-files --others --exclude-standard` |
| Committed changes since the base | `git diff <BASE> HEAD --` |
| Net tracked working-tree changes since the base | `git diff <BASE> --` |
| Changed paths and renames | `git diff --name-status --find-renames <BASE> --` |
| Commits since the base (to locate evidence) | `git log --oneline <BASE>..HEAD` |
| Does a revision resolve? | `git rev-parse --verify --quiet "<rev>^{commit}"` |
| The remote's default branch | `git symbolic-ref --quiet --short refs/remotes/origin/HEAD` |
| Merge base with the integration branch | `git merge-base HEAD <integration-branch>` |
| Nearest branches and tags | `git branch -a` and `git tag --list --sort=-creatordate` |
| Whitespace in tracked documents | `git diff --check -- <DOC_PATHS>` and `git diff --cached --check -- <DOC_PATHS>` |
| Whitespace in a new, untracked document | `git diff --no-index --check /dev/null <FILE>`: it exits non-zero even when clean, so judge by its output (none means clean) |

- The staged and unstaged views can overlap or cancel each other. Report the target's net state
  (`git diff <BASE> --`), and inspect untracked files separately.
- A clean working tree doesn't rule out relevant committed work.
- Normal diffs omit untracked files, so read new files directly.
- When scripting file inventories, use NUL-delimited output (`-z`) to handle unusual file names.
