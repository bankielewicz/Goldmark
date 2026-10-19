---
name: documents-updater
description: Updates or creates a repository's README, CHANGELOG and other affected documentation after work is done, using git diffs and the current files as evidence. Use when the user asks to update, refresh or sync the docs, README or changelog, document what changed, add changelog or release-note entries for unreleased work, create a missing README or guide, or bring documentation up to date at the end of a work session, even when they don't name a file. Proposes exact edits instead of applying them when asked to draft, preview or review documentation changes. Not for commit messages, pull request descriptions, code comments or docstrings, or a single edit the user spells out, such as fixing one typo.
argument-hint: "[base-revision-or-range] [propose]"
metadata:
  devforgeai-id: "SKL-005"
  devforgeai-version: "1"
---

# Documents updater

Bring a repository's documentation in line with its current state after work is done: update or
create `README.md`, `CHANGELOG.md` and the affected guides and references, with git diffs and the
current files as evidence. Readers act on what these documents say, so every claim must be
confirmed against the repository. An unchanged document set is a valid result.

## Inputs

- `$ARGUMENTS`, all optional: a base revision (`abc1234`, `v1.2.0`) or a range (`v1.2.0..HEAD`),
  the word `propose`, or free text describing the scope. Otherwise use the scope the conversation
  already established.
- The repository: its instructions (`CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING.md`, documentation
  conventions, release configuration), its git state, and its documents and templates.
- The conversation: the task that was done, and any git status captured at the start of the session.
- The audience: the existing documents' readers; otherwise the users, operators and contributors
  the project's purpose implies.
- Fallback templates: `${CLAUDE_SKILL_DIR}/assets/<type>.md`, used only when the repository has no
  template or established structure for that document.

Use Read, Glob and Grep to inspect files. Use Bash for read-only git commands, the validator
script, the repository's own documentation checks, and safe checks of examples: `--help`, an
existing test, or an example run on a scratch file outside the repository. A safe check modifies no
repository file and needs no network or credentials. Turn off caches a check would write into the
repository (`PYTHONDONTWRITEBYTECODE=1`, pytest's `-p no:cacheprovider`), or remove what it
created. Never install packages or run deploy, migration or release commands. Never run a
`devforgeai` command: that CLI doesn't exist, and a program with that name on PATH can't be
trusted.

## Decisions that belong to the user

- **Edit or propose.** Apply edits only when the request, or authorization already given in this
  session, asks to update, create or fix documentation. Otherwise propose: when the request says to
  propose, draft, suggest, review or preview, says not to touch files or passes `propose`, and
  whenever nobody asked for documentation changes (for example when the skill started on its own
  at the end of a task). In proposal mode, modify no repository file.
- **The comparison point**, when neither the repository nor the conversation resolves it (step 1).
  Never substitute the last commit, the latest tag, the root commit or an assumed `main`.
- **Facts only the owner knows:** commands, URLs, credentials, owners, licenses, support
  guarantees, version numbers, release dates and release status. Never invent them. Leave the
  instruction out and name the gap.
- **Releases and repository operations.** Never assign a version, date an entry or mark work as
  released. Never change code, stage, commit, push, tag, release or deploy unless the user's task
  already includes it.
- **Accepted requirements.** When the implementation conflicts with one, flag the discrepancy.
  Never edit the requirement to match.

**Asking.** Ask only when a remaining ambiguity prevents an accurate, properly scoped update, and
never for anything the conversation or the repository already establishes. An ambiguity that
doesn't change the result needs no question: name the reading you used in the `Scope` line. Use
AskUserQuestion when it is available, and continue once it is answered. Otherwise, or when no answer
can arrive, finish the unambiguous parts, put the question under `Action required`, and end the
reply with the completion response: `partial` if you changed or proposed anything, `blocked` if
not.

## Workflow

Copy this checklist into your response and tick items off as you go:

```
- [ ] 1. Establish the scope
- [ ] 2. Build the evidence inventory
- [ ] 3. Select what deserves documentation
- [ ] 4. Choose documents and templates
- [ ] 5. Write or propose the edits
- [ ] 6. Validate the documents
- [ ] 7. Report
```

Tick a step you skip, and add `(skipped: <reason>)`.

### 1. Establish the scope

Follow [references/scope-and-evidence.md](references/scope-and-evidence.md).
1. Confirm the repository root. Read its instructions, contribution guidance, documentation
   conventions and release configuration.
2. Inventory the existing documentation: canonical files, READMEs outside the root, generated
   outputs, release-note fragments, templates, the project type and its readers.
3. Record `git status --short --untracked-files=all`. Read every document before changing it: that
   read is its pre-edit content. (`git show HEAD:<path>` is enough only for a document with no
   local edits.) A dirty working tree is not a reason to stop.
4. **No git, or no commits:** work from the current files. Corrections of current facts, and
   missing documents built from the current files, proceed; only claims about what changed need a
   baseline. In a repository without commits, document its initial state and invent no release
   history. Skip the rest of this step.
5. Resolve the before state, taking the first that applies:
   1. a revision, range or scope the user gave ("my uncommitted changes" means `HEAD`);
   2. a captured task-start state, only when this session did the work being documented: the git
      status recorded when the session began, or a commit or stash made then. When the session
      began with the documentation request, that snapshot is the target, not the base: skip this
      rule;
   3. the merge base with the branch's known integration branch, when the scope is that branch's
      work;
   4. `HEAD`, when the scope is the uncommitted changes, for example when the work the request
      describes exists only in them.

   If none applies, ask for the comparison point. Until it is answered, write no changelog entries
   and no other claim about what changed, not even in the reply: you may list the candidate commits
   (hash, date, subject) for the user to pick from, but never draft entries or say what each
   choice would add.
6. Exclude local edits that predate the task. Leave an edit whose attribution is uncertain
   undocumented, and name it in the report. What the user says is part of the task wins over a
   captured start state.

### 2. Build the evidence inventory

Inspect every committed, staged, unstaged and untracked change in scope (the git commands are in
scope-and-evidence.md). Read the affected source, configuration, tests, specifications and
documents until each outcome is understood. Keep a working note for each meaningful outcome
(Outcome, Audience, Evidence, Action, Destination), and never publish it unless the project
requires an evidence record.
- Use summaries, commit messages and the task description to find evidence, never as evidence.
- Group edits that deliver one result. Leave out reverted and superseded attempts.
- Keep *specified*, *implemented*, *tested* and *released* distinct. A diff shows that content
  changed. It never shows that tests passed, that anything was released, or that anything got
  faster.

### 3. Select what deserves documentation

Use the treatment table in [references/document-selection.md](references/document-selection.md).
Document a change when it affects what a reader can do, how they must do it, or what they must
maintain or operate. Leave out formatting, internal renames, routine cleanup, extra tests and
routine dependency churn, unless one has a material effect on readers. Never claim better speed,
reliability, security, completeness or readiness without a measured result; describe the concrete
behavior instead.

If nothing deserves documentation, still check `README.md`, `CHANGELOG.md` and every document that
mentions an affected name against the current state (step 4's search). If all are accurate, go to
step 7 and report `Result: no_change`, with `Validation` naming what you reviewed.

### 4. Choose documents and templates

Follow document-selection.md.
- Review `README.md` and `CHANGELOG.md` on every run.
- Search the repository for each affected command, option, configuration key, path and concept.
  Include the documents that mention them, even when those documents aren't in the diff.
- Update a canonical document that has another name or location; never create a competing copy.
  Create a missing document only under the missing-document rules.
- For generated documentation or release notes, edit the source or change fragment, never the
  generated output.
- **Template:** the repository's own template or established structure first. Otherwise pick the
  project profile and document template from document-selection.md, and start from
  `${CLAUDE_SKILL_DIR}/assets/<type>.md`.
- **Governed documents** (specifications, ADRs and task records with versions and approvals, such
  as DevForgeAI's `docs/specs/`): change them only when the task authorizes it, and only through
  the repository's process.

### 5. Write or propose the edits

Read [references/writing-standards.md](references/writing-standards.md) and
[references/output-rules.md](references/output-rules.md) first.
- Edit the smallest useful sections. Keep the repository's voice, structure and vocabulary, and use
  relative links for files in the repository.
- **README:** the current supported state. Keep release and branch qualifiers. Never add a session
  report, commit list or diary.
- **Changelog:** the existing format; otherwise an `Unreleased` section with only the Keep a
  Changelog categories that have entries. Write one entry per outcome, merged with any existing
  entry for the same change. Published releases stay byte-identical, except for a factual
  correction the user asked for. Breaking changes are prominent and name the required action.
- **New documents:** built from the template, with every unused section, `{{placeholder}}` and
  `<!-- guide: -->` comment removed.
- **Proposal mode:** put in the reply the exact replacement text or a unified diff for each change,
  and the full content of each new file. Write nothing.

If a target section changed during the run, reread it and keep the newer content.

### 6. Validate the documents

In proposal mode, check the proposal against the self-check list. You may also run
`git apply --check` on a proposed diff, or `check_docs.py` on scratch copies outside the
repository. Otherwise:
1. Run the validator on every Markdown file you created or edited:

   ```bash
   python3 "${CLAUDE_SKILL_DIR}/scripts/check_docs.py" README.md CHANGELOG.md docs/<changed>.md
   ```

   It exits 0 when there are no errors. Fix every error in lines you added or changed. An error in
   a line you didn't touch is pre-existing: leave it, and report it under `Validation`; it doesn't
   make the result `partial`. Warnings are advisory.
2. Check the **Self-check list** in output-rules.md item by item, including a comparison of each
   document against its pre-edit content.
3. Run the repository's own documentation checks if it defines any (a docs build, a Markdown
   linter). Check whitespace with `git diff --check -- <docs>` for unstaged edits,
   `git diff --cached --check -- <docs>` for staged ones, and
   `git diff --no-index --check /dev/null <file>` for each new, untracked document. That last
   command exits non-zero even when clean, so judge it by its output: none means clean.
4. Fix each problem in what you changed and check again, at most three attempts. If such errors
   remain, stop and report `partial` with each remaining error and its file.

Verify examples with existing evidence or a safe check (Inputs). Never report an example or a test
as passing unless you ran it. Preview the rendered documents when the repository offers a safe
preview (such as a docs build); otherwise say you reviewed the source only.

### 7. Report

End the reply with the completion response, and put nothing after it:

```text
Result: updated | proposed | no_change | partial | blocked
Scope: <comparison point and target; uncertainty only if material>
Documents: <paths created, updated or proposed>
Highlights:
- <meaningful outcome>
Validation: <checks performed and relevant limitations>
Action required: <migration, unresolved discrepancy or essential clarification>
```

Omit empty fields. Keep one to three highlights unless a required user action needs more. Each
highlight is a reader-facing outcome the documents now describe ("README and changelog document
`--exclude PATTERN`"), not the evidence inventory or every implementation change. Show the ticked
checklist once, before the report block. In proposal
mode the proposed edits come before this block. The result values are defined in output-rules.md.

## Output contract

- **Files:** only documentation in scope, in the user's repository. New documents go where the
  repository's layout puts them, otherwise in `docs/<kebab-case-topic>.md` (the root `README.md`,
  `CHANGELOG.md` and `CONTRIBUTING.md` excepted). Nothing else changes, and in proposal mode
  nothing changes at all.
- **Claims:** each is supported by the repository at the target. No invented commands, URLs,
  credentials, owners, licenses, guarantees, versions, dates or release status.
- **Idempotent:** a second run with the same evidence adds no entries and no cosmetic churn.
- **Reply:** ends with the completion response.

## Examples

**Uncommitted feature.** "I've added an `--exclude` option to the sync command; update the docs."
The scope is the uncommitted changes, so the base is `HEAD`. The skill finds the option in the diff
and a new untracked test, and searches for every document that lists the sync options. It updates
the command reference and the README's usage section, and adds an `Unreleased` → `Added` entry to
the existing changelog, with no version and no date. It validates and reports `Result: updated`.

**Review first.** "Show me what you'd change in the docs for the API client rewrite before editing
anything." The skill takes the merge base with the remote's default branch, puts a unified diff for
each changed section and the full text of any new file in the reply, and reports
`Result: proposed`.

**Unknown baseline.** "Write release notes for everything I did on this feature." The repository has
no remote, no tags and no integration branch, so nothing shows where the work began. The skill asks
for the comparison point and writes no entries.

## References

- [references/scope-and-evidence.md](references/scope-and-evidence.md): read at step 1. The
  before-state rules, attribution, the evidence note and the git commands.
- [references/document-selection.md](references/document-selection.md): read at steps 3 and 4.
  Treatment by change type, which documents to update, missing-document rules, project profiles and
  the template for each document type.
- [references/writing-standards.md](references/writing-standards.md): read before step 5. Layout,
  wording and concise-reporting rules.
- [references/output-rules.md](references/output-rules.md): read before step 5. README and
  changelog rules, the proposal format, the completion response, and the self-check list used at
  step 6.
- `${CLAUDE_SKILL_DIR}/assets/`: the fallback templates, one per document type, listed in
  document-selection.md.
- `${CLAUDE_SKILL_DIR}/scripts/check_docs.py`: run at step 6.
