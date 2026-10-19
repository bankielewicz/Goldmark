# Output rules

## Contents

- The completion response
- Reporting rules
- Commands that need a confirmation
- Self-check list

## The completion response

The last thing in the reply, after the ticked checklist, with empty fields omitted and nothing after
it:

```text
Result: done | partial | blocked | awaiting_approval | no_change
Phases: <phases run, in order>
Branch: <branch> → <base>
PR: <URL, state>
Checks: <each check: passed, failed or not run>
Changes: <committed, left unstaged, proposed for ignoring, blocked>
Action required: <approval needed, conflict, recovery command, the user's own step>
```

| Result | When |
|---|---|
| `done` | Every requested phase finished |
| `partial` | Some requested phases finished and an issue remains (a check failed, `gh` missing for the PR, a sandbox block) |
| `blocked` | Nothing could proceed (not a repository, an operation in progress, a blocked scan finding, a rebase conflict, QA not approved) |
| `awaiting_approval` | A gate needs the user's answer: the question goes in Action required |
| `no_change` | Everything requested was already done, or start, commit, push or pr had nothing to deliver |

- **Checks:** each check with its command and result, for example
  `python3 -m unittest: passed (12 tests)`, `git diff --cached --check: passed`. `not run` says why.
- **Changes:** name paths or short groups: committed (with the commit's subject), left unstaged
  (unrelated, uncertain), proposed for ignoring (with the pattern and the file it belongs in),
  blocked (file and line, never a value).
- **Action required:** anything the user must do: approve (the exact question), resolve a conflict
  (the paths), run a command the session can't (the command and why), an independent QA review,
  the repository's post-merge step, a recovery command after a failure.

## Reporting rules

- Report the state after the run, from git itself (`git log -1`, `git status`), not from memory.
- Never paste the state report, the scan JSON or full command output; quote only the lines that
  matter (a conflict path, a hook's error, a remote's rejection).
- Never print a secret's value, even partly, including for a scan warning.
- A script that exited 2 didn't run: report its `error` as the reason, never as a finding or state.
- Say what was verified and what wasn't: a check not run, documentation not reviewed, PR state not
  read without `gh`.
- When the remote is public or its visibility is unknown and something was pushed, say it is
  published.
- In `status`, report: the branch and checkout, the default branch's state with ahead/behind counts,
  the changes grouped by class, the worktrees (branch, activity, lock, merged), and the PR's QA state
  when `gh` can read it.

## Commands that need a confirmation

SKILL.md's gates hold the one list of commands never to run; it isn't repeated here. These run only
after a confirmation naming the exact target, given in this run:

| Command | Why |
|---|---|
| `git push --force-with-lease=<branch>:<sha>` | Replaces remote commits |
| `git reset --hard` | Discards working-tree and index content |
| `git clean` | Deletes untracked files |
| `git checkout -- <path>` / `git restore <path>` over content not proven identical to what replaces it | Discards edits |
| `git branch -D` | Deletes a branch git considers unmerged |
| `git stash drop` / `git stash clear` | Deletes stashed work |
| Rebase or amend of pushed commits | Rewrites published history |
| `git rm --cached` | Deletes the file for everyone once merged |
| `git push origin --delete <branch>` | Deletes a remote branch, even when the request named it |
| `git merge --allow-unrelated-histories` | Joins unrelated histories |

## Self-check list

Before the completion response, confirm each item:

1. Every decision came from the state report run after the fetch, and the default branch came from
   the remote, not an assumption.
2. No command on SKILL.md's never-run list ran; every confirmation-only command ran after a
   confirmation naming its target, given in this run. No refused command was retried another way.
3. Only task paths were staged, by explicit path. Unrelated, local, uncertain and blocked paths are
   unstaged and named; sandbox write masks were never staged.
4. The scan ran on the staged content before every commit and exited 0 or 1; no blocked finding was
   committed, and a warning was committed only on the user's yes.
5. Each required check is reported with its command as passed, failed or not run.
6. Carried work was verified by hash before switching back (when it applied), and work was carried
   from a default branch ahead of origin only after the user chose to include those commits.
   Every worktree created is under the main checkout, by an absolute path.
7. Every branch move or rewrite has a backup ref or a recorded SHA, and a failed step's recovery
   commands are in Action required.
8. Nothing was pushed, and no PR was created or edited, unless the request named it or the user
   confirmed it in this run; each `git push` and `gh pr` call was a command of its own.
9. No merge happened without the `approved` QA state and a confirmation given after this run's
   readiness report, with `--match-head-commit`. No QA label was touched and no verdict posted.
10. No worktree or branch was removed without proof that its content exists elsewhere and a
    confirmation naming it, from prune or from sync; none was the session's own worktree; and no
    worktree was removed before its non-regenerable ignored files were named in a question. A file
    was restored or removed only when proven identical to the incoming version. A locked worktree
    is reported as locked.
11. A differing local edit, and any staged version found nowhere else, is byte-identical to how it
    started.
12. The reply never says the session is working in a worktree unless EnterWorktree moved it there (a
    Bash `cd` doesn't); otherwise it gives `claude --worktree <name>` to open it.
13. The reply ends with the completion response, and its Result matches the definitions above.
