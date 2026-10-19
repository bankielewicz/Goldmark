---
name: precompact
description: Prepares a Claude Code session for /compact, so the next session can continue with nothing pasted by hand. It writes a full-fidelity handoff in devforgeai/handoff/, one folder per branch (START-HERE.md with the verified state, decisions and who made them, files to read first and the work outstanding; TASKS.md; a short RESUME-PROMPT.md to paste after compacting), ticks the session's plan file, and saves the session's new learnings to memory. Use before running /compact, ideally at 70 to 80 percent of the context window, or when asked to save learnings and write a handoff, start-here document or resume prompt for the next session after compacting. Not for project documentation, release notes, commit messages or a backlog.
argument-hint: "[note for the next session]"
metadata:
  devforgeai-id: "SKL-012"
  devforgeai-version: "1"
  devforgeai-tracked: "false"
---

# Pre-compaction handoff

Write down what this session knows, so the next one can continue without it. After `/compact` the session keeps
only a summary, and a new session keeps nothing; both can read files. Write a handoff they can act on without asking
about anything this session knew: verified facts, decisions in the decider's words, files to read with why and when,
and the work outstanding with its next action. Then save the durable learnings to memory.

Run before `/compact`, at 70 to 80 percent of the context window: the skill spends context too, and a session cut
off at the limit stops midway. The order below keeps the most important file, START-HERE.md, written first.

This skill runs on its own, never between other skills, and hands off to none: its next step is the user's
`/compact`. The progress tracker ignores it, so a tracked skill run open in the session stays open.

## Inputs

- An argument, when given, is the user's note for the next session; it goes first in START-HERE section 5.
- The conversation itself: work through the checklist here, in the main conversation. A subagent can't see the
  conversation the handoff summarizes.

## Tools

- **Bash**: small read-only state checks and the checker script, each a command of its own.
- **Read**: the handoff folder's earlier files, and this skill's templates and references when a step names them.
- **Write** and **Edit**: the handoff files, `devforgeai/handoff/.gitignore`, the session's plan or task file, and
  memory files. Change no other file, even one that another instruction asks to keep up to date (a log, a notes or
  papercuts file, a changelog): name it in the report as a place the user may want a learning copied.

Ask the user nothing. What can't be established goes into START-HERE section 3, under `### Open`.

## Checklist

Copy it and work through it in order:

```text
- [ ] Check the state
- [ ] Write devforgeai/handoff/.gitignore, then START-HERE.md in one write
- [ ] Write RESUME-PROMPT.md
- [ ] Update TASKS.md; tick the session's plan file
- [ ] Save learnings to memory
- [ ] Run check_handoff.py (at most three runs); review the rest of the self-check
- [ ] Report, with RESUME-PROMPT.md's text
```

The checklist is unnumbered on purpose: a tracked skill run may be open in the session, and its tracker reads
numbered checkbox lines in a reply (`- [x] 2. ...`) as that run's own steps. Write no numbered checkbox line in a
reply during this skill.

The rules for each file, with examples, are in [references/handoff-rules.md](references/handoff-rules.md); read it
before step 2.

### 1. Check the state

- Find the project root: the folder the session started in (not one a later `cd` moved to), or a folder holding it
  that the user names as the project.
- Find the work's branch: the branch of the worktree the session's work is in (the one it edited or committed to,
  for example with `git -C <worktree>`), or the root's own branch when the work is there. Run the git checks below
  in that worktree. The handoff folder is `devforgeai/handoff/<folder>/` under the root, `<folder>` being the
  work's branch name with each run of characters other than letters, digits, `.`, `_` and `-`
  replaced by `-` (`feat/csv-export` becomes `feat-csv-export`); `detached-<short hash>` on a detached HEAD;
  `no-git` when git can't be used for the project, a parent repository's top level included (see below).
- Don't re-read what the session holds (its CLAUDE.md files and memory index are in context). Read only the handoff
  folder's earlier files and, when it exists, the progress tracker's
  `devforgeai/progress/sessions/${CLAUDE_SESSION_ID}/current.json` (the open run's skill and step). This session's
  ID is ${CLAUDE_SESSION_ID}.
- Keep each command's output small: `date -u '+%Y-%m-%d %H:%M'`, `GIT_OPTIONAL_LOCKS=0 git status --porcelain=v2 -b`,
  `git log -n 5 --oneline`, `git diff --stat`, `git diff --stat --cached`, and `gh pr list --limit 10` where `gh`
  works.
- Run no test or build command: cite the last result the session saw, with its date, marked `(unverified)`, unless
  the user's note asks for a run.
- When git or `gh` can't be used, or git's top level is a parent of the project root (a repository that isn't the
  project's, such as a home folder kept in git), skip those checks, say in section 2 what couldn't be checked, and mark what depends on it
  `(unverified)`.

### 2. Write START-HERE.md

- Create the folder and `devforgeai/handoff/.gitignore` holding the line `*` before any other file.
- Write START-HERE.md from [assets/start-here.md](assets/start-here.md) in one write, replacing every
  `[[fill: ...]]`. When the folder holds an earlier START-HERE.md, carry over what still holds: decisions, open
  questions, rules, traps, learnings and unfinished items.
- Full fidelity: exact identifiers (hashes, branch names, pull request numbers, versions, IDs), absolute dates,
  names instead of pronouns, every fact `[checked: <command> -> <result>]` or `(unverified: <source>, <date>)`.
- Progressive disclosure: name files with why and when to read them, and searches to run, instead of copying them.
  Copy in only what exists nowhere else, such as a decision said only in the conversation.
- Quote the user's words for each decision, with who and the date. Put the user's note, if any, first in section 5.
- Never write a secret: no token, key or password, even one said in the conversation.

### 3. Write RESUME-PROMPT.md

Write it from [assets/resume-prompt.md](assets/resume-prompt.md): about 15 lines, at most 40, naming START-HERE.md by
its absolute path, with the branch check, section 7, the first outstanding item, asking before the user's decisions,
and searching the repository and memory before asking.

### 4. Update TASKS.md and the plan file

- Update TASKS.md, or write it from [assets/tasks.md](assets/tasks.md): what this session finished as done with its
  evidence, what is next with its next action. Keep every done entry; past 20, move the oldest to TASKS-archive.md in
  the same folder.
- When the session keeps a plan, task or checkpoint file of its own, tick its checkpoints with their evidence, and
  list it in START-HERE section 4 as the source of truth for its steps; TASKS.md then holds only what that file
  doesn't.

### 5. Save learnings to memory

When the session's instructions describe a memory system, save the new, durable learnings in its format, updating an
existing memory rather than adding a duplicate; see [references/memory.md](references/memory.md). Memory is that
system only (its memory folder and index), never a log or notes file another instruction names. With no memory
system, or when a write is refused, the learnings stay in START-HERE section 6, and the report says why.

### 6. Check

Run the checker as a command of its own:

```bash
python3 ${CLAUDE_SKILL_DIR}/scripts/check_handoff.py --root <project root> devforgeai/handoff/<folder>
```

- Exit 0: clean, or warnings only. Exit 1: fix each problem it lists in the file and line it names, and run it
  again, at most three runs in all; never change a correct reference only to satisfy it. Exit 2: no such folder;
  check the root and the folder name from step 1, write the files if they are missing, and run it again.
- Without `python3`, say so and read the files back against the whole self-check.
- Then review what was written against the self-check's other items (references/handoff-rules.md, "The
  self-check", items 4 to 9). The text is in context: don't read the files again.

### 7. Report

In the reply: the folder and the files written, the plan or task file ticked, the memories saved or updated (or why
none), anything left `(unverified)`, any problem the checker still reports, and the text of RESUME-PROMPT.md in a
fence longer than any fence inside it, so the user can paste it after `/compact`.

## Decisions that belong to the user

Never turn outstanding work into a decision, and never file a fact or a learning the user states as one: a decision
is a choice between options, made by the user. A choice the user hasn't made goes under `### Open` with the options as
the user saw them; the handoff records no choice the user didn't make, and section 5 says what waits for the user.

## Errors

- **The handoff folder can't be written:** say so, quoting the error, and give the files' content in the reply
  instead, so the user can save them.
- **The folder holds a handoff of other work** (another topic on this branch): keep its TASKS.md entries under a `##`
  heading naming that work after the required ones, write START-HERE for the current work, and name the earlier work
  in section 5 as not continued here, in a line before the numbered list.
- **Cut off before the end:** START-HERE.md is written first, so it exists; the next session, given its path,
  finishes by running this skill again.

## References

- [references/handoff-rules.md](references/handoff-rules.md): the folder and its markers, each START-HERE section
  with examples, carrying over an earlier handoff, TASKS.md, the checker's warnings, the self-check.
- [references/memory.md](references/memory.md): what is worth saving to memory, and how.
- [assets/start-here.md](assets/start-here.md), [assets/tasks.md](assets/tasks.md),
  [assets/resume-prompt.md](assets/resume-prompt.md): the templates.
