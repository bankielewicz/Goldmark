---
name: context
description: Writes and maintains a DevForgeAI project's context documents in docs/specs/context/ (index.md, architecture.md, tech-stack.md, source-tree.md, testing.md, and one document per component kind in the architecture, such as front-end.md or rdbms.md). Builds them from accepted ADRs, approved policy, the architecture description (ARCH), conventions the user confirms and read-only inspection of paths the user names; cites every decision, labels observed practice, and hands undecided significant choices back to Architecture Definition. Use when the user asks to write, set up, update, refresh or approve the project context, coding conventions, tech stack, source tree or testing conventions, or names one of these files ("update testing.md"); after Architecture Definition and before epics and stories; when the story step reports missing context documents; or when the architecture, ADRs or policy changed. Not for general background questions, the context window, or writing ADRs, PRDs or stories.
argument-hint: "[document]"
metadata:
  devforgeai-id: "SKL-010"
  devforgeai-version: "2"
---

# Context

Write and maintain the project's context documents in `docs/specs/context/` (ADR-004): `index.md`,
the core documents and one document per component kind the architecture uses. Story and spec work
read them to learn the stack, the layout and each layer's conventions, and cite them, so a statement
here binds every story that relies on it.

Four rules shape everything below:
- **Decide nothing.** Every statement says where it comes from: a cited decision, a convention the
  user confirmed, observed practice, or a DevForgeAI rule. A significant choice that nothing decides
  becomes a `[NEEDS ADR: …]` marker and goes back to Architecture Definition.
- **Only what the project needs.** The core documents always; a layer document only when a component
  of a current ARCH has its kind.
- **Bounded.** index.md stays within 100 lines and every other file within 500; readers open only
  what they need.
- **Approval is the user's.** Write drafts. A document becomes `approved` only on the user's explicit
  words in this run, with a named approver.

## Inputs

- `$ARGUMENTS`: empty, or one document name of the set (`tech-stack`, `testing`, `rdbms`, …), with or
  without `.md`. Never accept a file path.
- Read by contract, by these fixed paths, never by listing the repository:
  - `docs/specs/arch/ARCH-*.md`: the current ARCHs (status neither `superseded` nor `deprecated`);
  - `docs/specs/prd/<the PRD each ARCH links>.md`, and the IDs of `docs/specs/prd/PRD-*.md` for ERR-01;
  - `docs/specs/adr/ADR-*.md` (accepted, no `superseded_by`);
  - `docs/specs/policy/POL-*.md`, through the script, and `.claude/devforgeai.local.md`;
  - `docs/specs/story/STORY-*.md` and `docs/specs/spec/SPEC-*.md`: their `upstream` links to CTX
    documents, and the stories' section 3 design records;
  - `docs/specs/ambiguities/AMB-*.md`, and everything under `docs/specs/context/`;
  - `${CLAUDE_SKILL_DIR}/../epic/SKILL.md`: only whether it exists (Glob or `ls`, never Read), for
    step 10.
- Code and configuration: only inside the paths the user names ([inspection.md](references/inspection.md)).
- Templates: `${CLAUDE_SKILL_DIR}/assets/<name>.md`, and `${CLAUDE_SKILL_DIR}/assets/detail.md` for
  detail files.

## Tools

- **Reading:** Read, Glob and Grep; without them, `ls`, `find`, `grep`, `cat` and `head` with explicit
  paths, on the inputs above and inside the inspection scope.
- **Writing:** Write only for a new document or detail file. Every change to an existing context
  document, and to an ambiguity entry's `resolution`, is an Edit. A restore copies files back with the
  script, never by re-emitting text.
- **Bash:** only these commands, and the read-only commands above:

  ```
  python3 ${CLAUDE_SKILL_DIR}/scripts/validate_policy.py docs/specs/policy
  python3 ${CLAUDE_SKILL_DIR}/scripts/context_check.py snapshot new
  python3 ${CLAUDE_SKILL_DIR}/scripts/context_check.py snapshot <run folder>/pre-approval
  python3 ${CLAUDE_SKILL_DIR}/scripts/context_check.py check --snapshot <start>
  python3 ${CLAUDE_SKILL_DIR}/scripts/context_check.py restore <folder> <file>...
  ```

  Run them from the project root. Never run git, project code or a `devforgeai` command.

## Decisions that belong to the user

The documents record decisions; they never make them. These are the user's:
- **Each convention**, and whether an observed fact becomes one.
- **The inspection scope**, and any read outside it.
- **Whether a choice needs an architecture decision.** A choice the user confirms as a convention has
  been judged not significant.
- **Approving a document, and who the approver is.**
- **Deprecating a layer document**, rewriting a document that fails its check, the kinds of a component
  the ARCH leaves without them, where an ambiguity entry belongs, the freshness window, and writing
  drafts after stopping early.

Record one of these only when the user made it, in an answer or in the request. Ask with
AskUserQuestion when it is available: at most 4 questions per call, 2–4 options each, the recommended
option first and marked "(Recommended)"; otherwise ask in plain text and end your turn. Every
question, the scope, freshness and approver questions included, is asked within the
`interview.max_calls` calls ([interview.md](references/interview.md)).

**"Proceed without questions."** When the request says to proceed without questions, or not to ask
anything, ask nothing in this run: inspect only paths the request names, use 90 days for freshness,
write every undecided section as a Proposed statement, and approve only a document the request
approves with the approver's name. Every other step still runs.

## Workflow

Keep this checklist in your working notes, not in the final reply, and tick items off as you go:

```
- [ ] 1. Read the current ARCHs
- [ ] 2. Resolve policy
- [ ] 3. Compute the document set and the run's scope
- [ ] 4. Snapshot and review the existing documents
- [ ] 5. Inspect the named paths
- [ ] 6. Interview
- [ ] 7. Write the documents and fold accepted entries, index.md last
- [ ] 8. Check, repair and record the folds
- [ ] 9. Approve on explicit words
- [ ] 10. Report and hand off
```

### 1. Read the current ARCHs (BEH-02, ERR-01)

Read every current ARCH: its `system`, `owner`, `status`, `version`, the PRD its `upstream` names,
its active CMP items (`name`, `kinds`, `responsibility`, `deployment`, `interacts_with`) and its DEC
items. Take the active components of every current ARCH together; one that appears in two ARCHs is
listed once per ARCH.
- **No current ARCH (ERR-01):** write nothing. Say that the context documents need an architecture
  description, and tell the user to run `/devforgeai:architecture PRD-NNN`, naming the PRD IDs in
  `docs/specs/prd/` when there are any. Stop.
- **A draft or in-review ARCH:** continue, and say in the report and in each written document's Change
  Log row that the documents are proposals because the architecture isn't approved.
- **The linked PRD,** as it is on disk: only its ID, its `operating_context`, and the NFR items a
  convention cites. When its `version` isn't the one the ARCH links, report the stale link, take only
  its ID, treat the operating context as unknown unless the request gives it, and cite no NFR. Never
  read an earlier version.

### 2. Resolve policy (BEH-03, ERR-02, ERR-03)

Before any question, follow [policy.md](references/policy.md) R1 to R5 with
[defaults.md](references/defaults.md).
1. When `docs/specs/policy/` holds a `POL-*.md`, run `validate_policy.py` (above) and act on its exit
   code. Never validate policy by reading the documents instead; reading a document's `status` for
   the exit-2 case below is fine.
   - **0:** continue; its `ignored` lines go into the resolution line.
   - **1 (ERR-02):** stop before asking or writing anything. Name each error it printed (the policy
     file, the setting or frontmatter field, the field and the rule) and say nothing was written.
   - **2, or it can't run (ERR-03):** when any policy document is `approved`, stop: say policy
     validation couldn't run, quote its message, and write nothing. Otherwise use the defaults.
2. Resolve `interview.max_calls` (with the local preference file), `architecture.mandated_platforms`,
   `quality.required_categories` and the six `testing.*` keys (policy.md, "The testing settings").
   The operating context comes from the request, otherwise from the `operating_context` of the PRDs
   the current ARCHs link when they all agree, otherwise it is unknown. Never ask for it.
3. Compose the resolution line ([output-rules.md](references/output-rules.md), "The resolution line").

### 3. Compute the document set and the run's scope (BEH-04, BEH-01, ERR-04, ERR-05)

Compute the set from the kinds of the active components ([documents.md](references/documents.md),
"The document set"), with ERR-05 for a component without kinds.
- **Scope:** `$ARGUMENTS`; when it is empty, a request to update only one named document. A name of
  the set limits the writes to that document and index.md; the others are read, never written. No
  argument and no such request: the whole set.
- **A name outside the set (ERR-04):** write nothing. List the document names of the current set and
  ask which one, or whether to run for all. Stop. A path outside `docs/specs/context/`
  (`docs/guides/testing.md`) names no document of the set, whatever its file name: ERR-04 too.

### 4. Snapshot and review the existing documents (BEH-18, BEH-13 to BEH-16, ERR-10, ERR-11)

1. Before anything is written, run `context_check.py snapshot new`. Keep the folder it prints as
   `<start>` for the whole run; its parent is the run folder. If it exits 2, stop (ERR-13): quote its
   `Cannot run:` line and write nothing.
2. List `docs/specs/context/`. Read each document of the set and each detail file its parent links, in
   full. A file with one of the 12 names that isn't in the current set is a layer document of an
   earlier set: read its frontmatter and Change Log; when it is deprecated, leave it; otherwise it is
   BEH-04's (item 6). The detail files such a document links are its own, not stray paths. Report any
   other path by its path (ERR-11), and never read, edit or delete it.
3. Run `check --snapshot <start>`. Each `unchanged, invalid` line is a document that fails before the
   run (ERR-10): report its errors, and rewrite it only when the user confirms in this run (a
   revision). Otherwise leave it unchanged and continue with the others.
4. **Suspect links (BEH-14):** in each existing document the run may write, a link whose target now
   has a higher version is suspect. Re-read the target and every statement that cites it: no statement
   changes, a relink; a statement changes, a revision ([output-rules.md](references/output-rules.md),
   "Changing an existing document").
5. **Stale observations (BEH-15):** report each observed statement or item whose date is older than
   its document's `freshness_days` (the document, the statement or item, the date). It is never an
   error and never blocks approval; re-inspecting needs the user to name the path again.
6. Note the stories and specs whose `upstream` cites a CTX document (BEH-13), the stories' section 3
   design records (BEH-11), the accepted ambiguity entries to fold (BEH-16) and any layer document
   whose kind is gone (BEH-04).

### 5. Inspect the named paths (BEH-05, ERR-06)

Follow [inspection.md](references/inspection.md). Only the paths the user named, read-only; with none,
inspect nothing.

### 6. Interview (BEH-07, BEH-08, ERR-09)

Follow [interview.md](references/interview.md): the scope question when no paths were named, observed
facts to confirm, one question per undecided section, and the freshness window, within the budget.
Answers stated in the request count. Under "Proceed without questions", ask nothing.

### 7. Write the documents and fold accepted entries, index.md last (BEH-06, BEH-09 to BEH-12, BEH-16, ERR-07, ERR-12)

Read [output-rules.md](references/output-rules.md) and [documents.md](references/documents.md) first.
Write only the documents in the run's scope, and in each, only what its sources give.
- **A new document:** Write it from `${CLAUDE_SKILL_DIR}/assets/<name>.md`, deleting every author
  comment and leaving no placeholder. Frontmatter as output-rules.md says, with
  `generated_by.session: "${CLAUDE_SESSION_ID}"`, and one Change Log row authored
  `claude-code (session ${CLAUDE_SESSION_ID})` whose text ends with the resolution line, for example
  `Initial draft from ARCH-001 v1. Policy resolution: interview.max_calls=8 (default); …`.
- **An existing document:** Edit only the statements and items whose source changed in this run or
  that the user changed. A revision raises the version, returns an approved document to draft, and
  adds a row authored `claude-code (session ${CLAUDE_SESSION_ID})`; a relink adds the row
  `Re-reviewed against <ID> v<N>: no change`. Never delete or renumber an item.
- **Every statement** carries its label; every decision is cited with a `constrains` link; each
  undecided section gets a Proposed statement with `[NEEDS CLARIFICATION: confirm …]`.
- **A significant choice that nothing decides (ERR-07):** `[NEEDS ADR: <decision>; affects
  <documents>]`, once, in its section (documents.md), never a convention.
- **testing.md** cites each resolved testing value and never sets one; **ui-mockups.md**'s table comes
  only from stories' design records; **tech-stack.md** and **source-tree.md** follow documents.md's
  item rules.
- **Accepted ambiguity entries** noted at step 4: fold each into its document now, as output-rules.md
  "Folding accepted ambiguity entries" items 1 and 2 say. A fold that changes a document is part of
  that document's revision. Leave each `resolution` for step 8.
- **One revision per document per run:** the version rises once, at the document's first change, and
  every later Edit in the run (another change, a fold, a moved link) stays in that version and its one
  Change Log row.
- **Size:** within the limits, with detail files from `${CLAUDE_SKILL_DIR}/assets/detail.md` when a
  document would pass 500 lines; ERR-12 when the items alone would.
- **index.md last**, with one row per document of the set that exists after the run, at its version.
  When no row changes and none of its links is suspect, leave it unchanged.

### 8. Check, repair and record the folds (BEH-18, BEH-16, ERR-08)

1. Run `check --snapshot <start>`: that is check 1. Repair each error it reports with Edit and check
   again, at most three repair cycles. A repair never raises a version or adds a Change Log row. An
   `unchanged, invalid` line is ERR-10's, not a repair target.
2. Read each written file back against the self-check list in output-rules.md, for what needs
   judgement: the resolution line's entries and order, and whether each statement says what its source
   says.
3. Record each check and repair in the reply, quoting the script's lines.
4. Errors left after three repair cycles, or an error you can't repair: follow output-rules.md,
   "When validation still fails (ERR-08)". `check` or `restore` exiting 2: ERR-13.
5. After the check passes, set the `resolution` of each entry folded at step 7 (output-rules.md,
   "Folding accepted ambiguity entries", item 3) and run `check --snapshot <start>` again, repairing any
   error within the same three cycles.

### 9. Approve on explicit words (BEH-17)

After ERR-08 or ERR-13, skip this step: approve nothing. Otherwise approve only on the user's explicit
words in this run that approve a named document, or all context documents ("I approve
tech-stack.md"). Never infer approval from silence, from an earlier run, or from a request to write the
documents.
- Approve a document only when it passed the check and holds no `[NEEDS CLARIFICATION: …]` or
  `[NEEDS ADR: …]` marker and no active Proposed statement or item, its detail files included.
  Otherwise say which markers remain and leave it draft.
- `approved_by` is the name the user gives for the approver ("I'm Example Owner, and I approve…").
  When none is given, ask who is approving, offering the document's owner first. When no answer can
  arrive, approve nothing, and say the approver wasn't named.
- Procedure:
  1. Run `snapshot <run folder>/pre-approval`. If it exits 2, apply ERR-13 and approve nothing.
  2. With Edit: `status: approved`, `approved_by`, `approved_on` today, and the Change Log row
     `| <version> | <today> | <approver> | Approved | — |`.
  3. Run `check --snapshot <start>`.
  4. If it fails, run `restore <run folder>/pre-approval <file>` for each approved document. For each
     `restored` line, report that document as not approved, with the errors. For each `NOT RESTORED`
     line, report "approval rollback failed" with the `status`, `approved_by` and `approved_on` the file
     now holds, and replace the handoff with a report in ERR-08's format, restoring nothing else.

### 10. Report and hand off (BEH-19)

When the run wrote or checked documents, the final reply opens with this block, filled in. Nothing
comes before it, not even the checklist:

```
Context documents: <name> (CTX-NNN v<N>, <status>; new | revised | relinked | unchanged), …
Inspection: <each path read> | none (no paths named)
Markers left: <name>: <count>, … | none
Handed back to architecture: <each NEEDS ADR decision> | none
Ambiguity entries folded: AMB-NNN#ENT-NN → CTX-NNN, … | none
Policy resolution: <the resolution line's entries>
```

"Markers left" counts the `[NEEDS CLARIFICATION` and `[NEEDS ADR` markers in each document and its
detail files, item notes included. Then, briefly:
- each check and repair, quoting the script's lines;
- the findings: stale PRD links; documents to deprecate; design records read in neither form; stories
  and specs now proposals; stale observations; entries not folded; components without kinds; documents
  failing before the run; other paths in `docs/specs/context/`; ERR-12; observed practice that differs
  from a decision; reads refused outside the scope; the budget running out;
- for an approval request: which markers keep a document from approval, or that the approver wasn't
  named;
- the draft-ARCH warning, and any testing value the user wants changed, which belongs in a policy
  document.

The next step comes last, as its own paragraph outside any code block. It starts with the words
**Next step**, names documents by name and PRDs by ID, and nothing follows it:
- **A document of the set holds a `[NEEDS ADR]` marker:** tell the user to run
  `/devforgeai:architecture PRD-NNN` with the PRD ID of each current ARCH.
- **Otherwise, if `${CLAUDE_SKILL_DIR}/../epic/SKILL.md` exists:** tell the user to run
  `/devforgeai:epic PRD-NNN` with each PRD ID.
- **Otherwise:** say the epic workflow (planned as `/devforgeai:epic`) isn't built yet, and that once
  it is, `/devforgeai:epic PRD-NNN` runs for each PRD ID.

Never start another workflow. A run that stops before writing (ERR-01 to ERR-04) says why and what the
user can do, with no block. After ERR-08, or ERR-13 once files are written, a validation-failure report
replaces both the block and the next step.

## Output contract

- **Paths:** `docs/specs/context/<name>.md` with the fixed names and IDs, and detail files
  `docs/specs/context/<name>/<topic>.md`. Nothing else is written, except the `resolution` field of a
  folded ambiguity entry and the run's snapshots in the system temp folder.
- **Shape:** each document follows its template, `context.schema.json` and output-rules.md; index.md
  has exactly Documents, Loading and Change Log.
- **Statements:** every one labelled, every decision cited with a `constrains` link, every undecided
  section Proposed with its marker, every undecided significant choice a `[NEEDS ADR]` marker.
- **Never:** modify a PRD, ARCH, ADR, policy document, epic, story or spec; write an ADR, a policy
  setting or a new ambiguity entry; run, build or test project code or run git; call `/design`; delete
  a file; approve without the user's explicit words and a named approver.

## Examples

**First run, no user.** "Write the project context documents. I confirm Typer 0.12.x for the CLI, …
Proceed without questions." ARCH-001 (approved) has a `user-interface`, a `service` and a
`relational-store` component; ADR-001 chose Python 3.12, ADR-002 SQLite 3. The skill writes index.md,
architecture.md, tech-stack.md, source-tree.md, testing.md, front-end.md, ui-mockups.md,
middle-tier.md and rdbms.md as drafts: Python `"3.12.x"` and SQLite `"3.x"` as decisions citing their
ADRs, pipx as a decision citing the CMP items whose deployments name it, Typer as a convention, the
proposed roots `src/<slug>/` and `tests/<slug>/`, and a Proposed statement in every undecided section.
It checks them, then reports and ends with "Next step: run `/devforgeai:epic PRD-001` …".

**An undecided broker.** A `platform` component's deployment says "the message broker is undecided".
back-end.md section 4 gets `[NEEDS ADR: the message broker …; affects back-end.md]`, the report lists it
under "Handed back to architecture", and the next step is `/devforgeai:architecture PRD-001`.

**A change to an approved document.** "In tech-stack.md, add the convention that the lock file is
committed with every dependency change." tech-stack.md (approved, version 2) gets the Convention in
section 2 by Edit, becomes version 3 and draft, and index.md's row follows. STORY-001 cites CTX-003, so
the report says its work is a proposal until tech-stack.md is approved again.

## References

- [references/documents.md](references/documents.md): read at steps 3 and 7. The document set, the four
  sources, significant choices, and what each document holds, with the item rules.
- [references/interview.md](references/interview.md): read at step 6 and before any question. Budget,
  order, options, confirming observed facts, and every other question.
- [references/inspection.md](references/inspection.md): read at step 5. Scope, allowed commands, what to
  read and how observations are recorded.
- [references/output-rules.md](references/output-rules.md): read before step 7. Frontmatter, links,
  statements, items, Change Log, the resolution line, size, folding, approval, checking, ERR-08,
  ERR-13 and the self-check list.
- [references/policy.md](references/policy.md) and [references/defaults.md](references/defaults.md):
  read at step 2. Byte-identical copies of the prd skill's files, as are `scripts/validate_policy.py`
  and the policy and common schema copies in `references/schemas/`.
