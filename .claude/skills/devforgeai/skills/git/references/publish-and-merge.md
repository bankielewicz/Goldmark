# Push, pull request, QA and merge

## Contents

- Check the requested branch's history
- Update onto the base
- push
- Documentation before a PR
- pr
- QA state
- merge: readiness report
- merge: the merge itself

## Check the requested branch's history

For both `push` and `pr`, after fetching and before a rebase, documentation diff or push, resolve
the requested `<branch>` and `origin/<default>` to commits and run
`git merge-base <branch> origin/<default>`. Check that branch, even if the session is on another
branch; `default_branch.state` alone cannot establish its ancestry.

- Exit 0: the histories share a commit; continue.
- Exit 1 with both refs resolved: **ERR-04**. Follow
  [preflight-and-connect.md](preflight-and-connect.md): name both tips (SHA and subject), explain
  that the histories share no commit, offer moving the local work onto a new branch based on
  `origin/<default>` for a PR (recommended), or stop. Await the choice, change no history, push
  nothing and open no PR. Report `awaiting_approval`; do not automatically rebase the unrelated
  root or recommend joining the histories instead of the new-branch option.
- A missing ref or any other command failure: report the error and stop; never treat it as proof
  of unrelated histories or as a successful check. A confirmed empty remote follows `connect`'s
  bootstrap rule instead.

This check applies even when `connect` was skipped, and again after a later fetch before updating
onto the base. It does not authorize any history change.

## Update onto the base

Before the first push and before a merge:
1. `git fetch origin`, then compare the branch with `origin/<default>`
   (`git rev-list --left-right --count <branch>...origin/<default>`) and run the state report in the
   branch's checkout (`repo_state.py -C <main_checkout>/<worktrees[].path>`, an absolute path:
   `-C` resolves a relative one against the current directory), before any rebase. Apply the
   requested-branch history check above to the fetched refs first.
2. **Unpushed?** `git rev-list --count origin/<default>..<branch>` is the branch's own commits;
   `git rev-list --count origin/<default>..<branch> --not --remotes=origin` is those on no remote
   branch. They are unpushed only when the two counts are equal.
3. **Base moved, commits unpushed:** record a backup ref, then rebase them inside the checkout that
   has the branch: `git rebase origin/<default>`. When the rebase brought in new commits, run the
   required checks again before pushing.
4. **Conflict (ERR-08):** run `git rebase --abort` at once so the branch is exactly as before, push
   nothing, and list the conflicting paths (from the rebase output or `git diff --name-only
   --diff-filter=U` before aborting). Never resolve a conflict by guessing. Report `blocked` with the
   paths in Action required.
5. **Some commits already pushed:** don't rewrite them. Bring the base in with a merge commit when the
   repository's rules allow it, or ask.
6. A push that must replace remote commits uses `--force-with-lease=<branch>:<expected sha>` after a
   confirmation naming the branch, never `--force`, and never targets the default branch.

## push

`git push -u origin <branch>`, or `git push origin <branch>` when `.git/config` is write-masked.
Run it as a command of its own that starts with `git push`: never chained after another command,
prefixed with `cd … &&`, or written as `git -C`, so that a sandbox exclusion the user configured for
`git push *` applies. A branch's refs are shared by every checkout, so it works from the main
checkout too; the rebase above is what runs inside the branch's checkout. Work goes out on its own
branch; push the default branch directly only when the request names that
(or for the bootstrap, preflight-and-connect.md) and the repository's rules allow it, and never
forced. `push` opens no PR.

**ERR-09.** The remote rejects the push (non-fast-forward, protected branch, missing permission):
never force. Fetch, report the remote's message; a non-fast-forward goes back to "Update onto the
base". Report `blocked`.

## Documentation before a PR

Before `pr` pushes, look at the branch's diff against `origin/<default>`, naming the branch rather
than HEAD so the check holds from any checkout (`git diff --name-only origin/<default>...<branch>`).
When it changes files other than documentation but changes neither `README.md` nor `CHANGELOG.md`
nor the repository's release-note fragments:
- recommend `/devforgeai:documents-updater` with the merge base (`git merge-base origin/<default>
  <branch>`) as its base revision, and ask
  whether to run it first (recommended) or open the PR now;
- don't ask when the request says to skip documentation, when documents-updater already ran on this
  branch in this session, or when the branch already has an open PR (without `gh`, that can't be
  known: ask);
- **yes:** run it with the Skill tool (`devforgeai:documents-updater`, argument: the merge-base
  SHA). It changes documentation only and never commits. Then classify and commit its edits and
  continue;
- **no, or no answer can arrive:** continue the pr phase as requested and list "documentation not
  reviewed" among the PR's limitations; with no answer, still say in the reply that
  documents-updater is recommended. Never run documents-updater without the user's yes.

Suggest it from no other phase.

## pr

In this order; only steps 4 to 6 need `gh`, and each `gh pr` call runs as a command of its own that
starts with `gh pr`, like the push (a pipeline that starts with `gh pr`, such as `gh pr view … |
python3 …/qa_state.py`, counts as one):
1. Fetch, apply the requested-branch history check, then the documentation check above. These
   need only git; ERR-04 stops here before a diff that requires a merge base.
2. **Document IDs (ERR-15):** after the fetch and before any rebase, when the state report run in the
   branch's checkout lists `id_collisions` (a `docs/specs/<type>/<ID>.md` the branch adds while a
   different file with that path exists on the base), push nothing and open no PR. Name both files;
   renumbering is the user's decision because other documents may cite the ID. Report `blocked`.
   (After a rebase the collision would surface as an add/add conflict instead, ERR-08.)
3. "Update onto the base" and the push above. They need only git.
4. **gh and host (ERR-02):** `gh --version`, `gh auth status`, and a remote on GitHub. Without them,
   stop here: report the pushed branch, `partial`, and the command the user must run.
5. **Existing PR:** `gh pr list --head <branch> --state open --json number,url,isDraft`. When one
   exists, the push updated it: report it, and edit its body only when the user asks.
6. **Create:** `gh pr create --base <default> --head <branch> --title <title> --body-file -` with:
   - a title in the commit convention;
   - the scope: what changed and why, in a few lines;
   - the story, spec or issue IDs it implements;
   - each check with its result;
   - limitations and what wasn't verified ("documentation not reviewed" when the suggestion was
     declined);
   - the attribution line the session's instructions require.
   Add `--draft` when a required check failed or wasn't run, or when the user asks.
7. Report the URL. Action required names the next step: an independent QA session reviews the PR
   (SPEC-008), never this session.

## QA state

QA is an independent Claude or Codex session, never the session that developed the change. It
reviews the PR's head commit, posts a comment whose first line is `QA verdict: passed <sha>` or
`QA verdict: failed <sha>` (the full 40-character SHA it reviewed), then sets `merge-approved` or
`qa-failed` and removes the other. The repository's instructions may rename both labels.

```bash
gh pr view <number> --json labels,comments,headRefOid \
  | python3 "${CLAUDE_SKILL_DIR}/scripts/qa_state.py" [--approved-label <name> --failed-label <name>]
```

| State | Meaning | What the skill says |
|---|---|---|
| `pending` | No QA label and no verdict (or a verdict not yet labeled) | An independent QA session must review the head commit |
| `unverified` | A QA label but no verdict comment | QA must post a verdict naming the head commit |
| `conflicting` | Both labels, or a label contradicting the latest verdict | QA or the human must correct the labels |
| `stale` | The latest verdict names another commit, as after any push | An independent QA session must review again |
| `failed` | `qa-failed` and a failing verdict for the head | Back to development: the verdict's findings are the work to do; fix in the PR's worktree, then commit and push; the state then becomes `stale` |
| `approved` | `merge-approved` and a passing verdict for the head | Only this state allows a merge |

`status`, `pr` and `merge` report the QA state. This skill reads labels and verdicts; it never adds,
removes or creates a label and never posts a verdict, even when asked. Say that the human or an
independent QA session does that.

## merge: readiness report

Merge only the PR the user named or the current branch's PR. Gather, read-only:
- `gh pr view <n> --json number,url,state,isDraft,mergeable,mergeStateStatus,reviewDecision,headRefName,headRefOid,baseRefName,labels,comments`;
- `gh pr checks <n>` (required and other checks);
- `gh repo view --json mergeCommitAllowed,squashMergeAllowed,rebaseMergeAllowed,deleteBranchOnMerge`;
- the QA state (above), and whether the branch is behind the base.

Report: state and draft flag, mergeability and conflicts, each check, the review decision and
requested changes, behind or not, the head SHA, the allowed methods, and the QA state with the
verdict comment's author and URL (`verdict.author` and `verdict.url` from `qa_state.py`), so the
user sees who wrote the verdict they authorize against.

**ERR-10.** When the QA state isn't `approved`, or the PR is a draft, has failing or pending
required checks, requested changes or conflicts, or isn't mergeable: don't offer the merge and
never touch a QA label. Say what must change: an independent QA review for `pending`, `unverified`
or `stale`, development for `failed`, or the human merges it. Report `blocked`. This holds even when
the user authorized the merge in the request.

## merge: the merge itself

Only when the QA state is `approved` and nothing blocks:
1. Ask, after the readiness report: merge now, and with which method, among the allowed ones.
   Recommend the method the repository's instructions name, else the one its recent history shows
   (merge commits, squashes or rebases on the default branch). No answer: `awaiting_approval`.
2. `gh pr merge <n> --merge|--squash|--rebase --match-head-commit <head SHA from the report>`. The
   passing verdict names that same SHA, so a commit pushed after the report is never merged.
3. SKILL.md's never-run list excludes `--admin`, `--auto` and `--delete-branch` (which also deletes
   and switches local branches); never bypass or disable branch protection either.
4. Confirm with `gh pr view <n> --json state,mergeCommit` that the state is `MERGED`, and record the
   merge commit.
5. Delete the remote branch (`git push origin --delete <branch>`) only after a confirmation naming
   it, even when the request asked for the deletion, and skip it when the repository deletes merged
   branches itself (`deleteBranchOnMerge`).
6. Continue with sync and prune only when the request asks for them; otherwise name them as next
   steps.
