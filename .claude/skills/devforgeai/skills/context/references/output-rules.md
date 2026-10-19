# Context document output rules

## Contents

- When to use this
- Files
- Frontmatter
- Link records
- Statements and tables
- Item blocks
- Writing a new document
- Changing an existing document
- Change Log rows
- The resolution line
- Size and detail files
- Folding accepted ambiguity entries
- Approval
- Checking with context_check.py
- When validation still fails (ERR-08)
- When the script can't run (ERR-13)
- Self-check list
- Example item blocks

## When to use this

Read this before writing (SKILL.md step 7), and check every written file at step 8:
`context_check.py check` decides every rule a script can, and the self-check list at the end keeps
only what needs judgement. Story and spec read these documents mechanically, so a document that reads
well but breaks a rule here misleads every story that cites it.

## Files

- `docs/specs/context/<name>.md`, with the fixed names and IDs of documents.md. Never take a file name
  from the user, and never write any other path under `docs/specs/context/`.
- Detail files: `docs/specs/context/<name>/<topic>.md`, one level deep, `<topic>` in lowercase words
  joined by `-`.
- Never delete a file, and never write outside `docs/specs/context/`, except the `resolution` field of
  a folded ambiguity entry and the run's snapshots in the system temp folder.

## Frontmatter

Keep exactly the template's keys, in its order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | The document's fixed ID (`CTX-003` for tech-stack.md) |
| `type` | `context` |
| `title` | Quoted: the template's example title with `<project>` replaced by the `system` of the first current ARCH in ARCH ID order: `"shiftlog: tech stack"` |
| `status` | `draft` for a new document. `approved` only by step 9, `deprecated` only by BEH-04 |
| `version` | `1` for a new document; plus one per revision; unchanged by a relink or an approval |
| `created`, `updated` | Unquoted `YYYY-MM-DD`: today for a new document; a revision sets `updated` to today |
| `owner` | Quoted: the `owner` of the first current ARCH in ARCH ID order, unless the user names another |
| `authors` | `["<owner>", "claude-code"]` |
| `generated_by` | `tool: "claude-code"`, `model:` your own model ID, `session:` the session ID SKILL.md gives, all quoted. An existing document keeps its values |
| `reviewed_by` | `[]` for a new document; kept otherwise. Humans only |
| `approved_by`, `approved_on` | `""` and `null` until approval |
| `upstream` | Link records, one per line (next section) |
| `supersedes`, `superseded_by`, `blocked_by` | `[]`, `null`, `[]` |
| `document` | The document's name, equal to the file name without `.md` |
| `freshness_days` | Not in index.md. `90` for a new document unless the user gives another number; an existing document keeps its value |

Keep the `# --- context-specific ---` line before `document`, and delete the template's trailing
comments on the other lines.

## Link records

One record per line, in flow form: `- {id: ADR-001, relation: constrains, version: 1, hash: null}`;
add `item: SET-01` or `item: CMP-02` to cite an item. `version` is the cited document's current
version; `hash` is always `null`.

A document's frontmatter `upstream` holds:
- a `constrains` link to each current ARCH, at its version. architecture.md instead holds one
  `constrains` link per component its table lists (`{id: ARCH-001, item: CMP-01, …}`);
- a `constrains` link to each source a prose Decision or a table's Basis cites;
- in testing.md, a `constrains` link to each applied testing setting (`{id: POL-001, item: SET-01, …}`);
- an `informed_by` link to each applied `interview.max_calls` and `quality.required_categories`
  setting.

A technology's or root's own decision link goes on its item, never in frontmatter; so does an applied
mandated platform's. Defaults, local values and ignored documents get no link. Never link another
context document, a story or a spec: other context documents are named in prose ("tech-stack.md,
TEC-02"), and a Markdown hyperlink to them is fine.

## Statements and tables

Every narrative rule or fact is one bullet with its label (documents.md, "Sources and statement
kinds"):
- `- **Decision** (ADR-002): SQLite 3, in a local file (tech-stack.md, TEC-02).` The parentheses name
  each source as `ADR-NNN`, `POL-NNN#SET-NN` or `ARCH-NNN#CMP-NN`, and each has its `constrains` link.
- `- **Convention:** dependency updates go in their own pull request.`
- `- **Observed** (pyproject.toml, 2026-10-01): pytest is declared as >=8,<9.`
- `- **Proposed:** one Typer command per module. [NEEDS CLARIFICATION: confirm how commands are organized]`
- `- **DevForgeAI rule — <topic>:** …` only as the template states it; it names no document ID.

A table of rules has a Basis column holding `Convention`, `Observed (<path>, <date>)`,
`Proposed [NEEDS CLARIFICATION: confirm …]` or a Decision's source (`ADR-001`). A row that restates
another context document is never firmer than its source there. testing.md's Source column holds
`POL-NNN#SET-NN` or `(default)`. Markers are `[NEEDS CLARIFICATION: <question>]` and
`[NEEDS ADR: <decision>; affects <documents>]`; a bare ADR-, POL-, ARCH-, STORY- or SPEC- ID always
means the project's own document.

## Item blocks

The info string is exactly `yaml items`, with one collection key: `technologies` in tech-stack.md and
`roots` in source-tree.md only. Quote every free-text value; IDs are `TEC-NN` and `SRC-NN`, numbered
from `-01` and continuing from the highest; never reused, renumbered or deleted.

| Field | Technologies | Roots |
|---|---|---|
| `id`, `status` | `TEC-NN`; `active` or `deprecated` | `SRC-NN`; `active` or `deprecated` |
| content | `name`, `version_range` (quoted), `used_by` (block list of `"ARCH-NNN#CMP-NN"`) | `path` (quoted, ends in `/`), `holds`, `component` (`"ARCH-NNN#CMP-NN"` or `null`) |
| `basis` | `decision`, `convention`, `observed` or `proposed` | the same |
| by basis | `decision`: `upstream` with its `constrains` links. `observed`: `observed_in`, `observed_on`. `proposed`: the marker in `notes` | the same |
| `notes` | Quoted; a deprecated item's reason (who retired it, and when, for a rejected proposal) | the same |

A retired item stays with `status: deprecated` and its reason in `notes`. Items never move into a
detail file.

## Writing a new document

Use Write once per new document, from `assets/<name>.md` (SKILL.md gives the path):
- keep every heading of the template, in its order, and its DevForgeAI rules word for word;
- delete every `<!-- … -->` comment;
- replace every placeholder (`<…>`, `YYYY-MM-DD`, `DAYS`, `ARCH-000`, the example rows and items) with
  real content; leave no example value;
- start with a `## Contents` list when the document is longer than 100 lines.

## Changing an existing document

Use Edit, never Write, starting from the document's current text. Change only the statements and
items whose source changed in this run, or that the user changed; keep every other line as it is.
- **A revision** changes text or items (BEH-13), whatever the document's status: raise `version` by
  one, set `updated` to today, add a Change Log row, and, if the document was approved, set `status`
  to `draft` with `approved_by: ""` and `approved_on: null` (or `deprecated`, under BEH-04). Then name
  in the report every story and spec whose `upstream` cites that document (an `id: CTX-NNN` link in
  `docs/specs/story/` or `docs/specs/spec/`): their work is a proposal until the document is approved
  again.
- **A relink** (BEH-14) changes only a suspect link's `version`: add the row `Re-reviewed against
  <ID> v<N>: no change`, and change neither `version` nor `status` nor `updated`.
- **A deprecation** (BEH-04) is a revision with `status: deprecated`, whose row's text starts
  `Deprecated: <kind> is in no current ARCH` and, as every revision row, ends with the resolution line.
- **At most one revision per document per run.** `version` rises by one at the document's first
  change; every later Edit in the run (another change, a fold, a repair) stays in that version, and its
  one Change Log row names every change. A relink row is written only for a document the run doesn't
  otherwise revise; a revised document's suspect links move to their new versions inside the revision.
- Revising a detail file revises its parent. A detail file the run changes gets the parent's new
  version in its first line; a detail file the run doesn't change keeps its line.

## Change Log rows

Never change a row an earlier run wrote; this run's own row may grow to name every change. One row per
document written in the run:

| Event | Version | Author | Change |
|---|---|---|---|
| New document | `1` | `claude-code (session <session ID>)` | `Initial draft from ARCH-NNN vN. <resolution line>` |
| Revision (one per document per run) | the new version | the same | `<what changed>. <resolution line>` |
| Relink | the current version | the same | `Re-reviewed against <ID> v<N>: no change` (no resolution line) |
| Approval | the current version | the approver's name | `Approved` (no resolution line) |

Items affected: `all` for a new document, the changed item IDs or section names for a revision, `—`
otherwise. When a current ARCH is draft or in-review, the change text of every document written says
`The documents are proposals: ARCH-NNN is not approved.` before the resolution line. A layer document
added from an ERR-05 answer says `Kinds of ARCH-NNN#CMP-NN from the user's answer; the ARCH should
record them.` The resolution line always ends the change text.

## The resolution line

The same line ends every row of the run that carries one, and the report repeats it:

`Policy resolution: <entry>; <entry>; …`

Entries in this order, with no `|` character:
1. to 6. as `policy.md` gives them: `interview.max_calls=<N> (<source>)`, the mandated platforms, the
   required categories, then any `POL-NNN#SET-NN not applicable (<context>)`,
   `operating context unknown, resolved as production`, and `ignored <file or local entry> (<reason>)`
   entries;
7. the six testing entries, in the order of `defaults.md`: `<key>=<value> (POL-NNN#SET-NN)` or
   `<key>=<value> (default)`, with lists joined by `,` and the threshold as a number or `none`.

With no policy and a known operating context:

`Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default); testing.method=tdd (default); testing.coverage_metric=line (default); testing.coverage_threshold=none (default); testing.coverage_scope=code roots (default); testing.coverage_exclusions=generated,tests,fixtures roots (default); testing.exception_approvers=story owner (default)`

## Size and detail files

- index.md has at most 100 lines; every other file at most 500. Past 100 lines, a file starts with a
  `## Contents` list of its `##` sections.
- When a document would pass its limit, move sections into detail files from `assets/detail.md`: the
  last section before the Change Log first, repeating until the document is within its limit. Never
  move the Contents, a section holding items, or the Change Log. Each moved section leaves one line in
  the parent: `- [<topic>](<name>/<topic>.md): read when <condition>.`
- A detail file starts with `> Part of CTX-NNN (<name>.md), version N. A change here raises that
  document's version.`, has no frontmatter and no items, stays within 500 lines (a Contents list past
  100), and never links another detail file. Its Decision sources are linked in the parent's
  frontmatter.
- When the items alone would put tech-stack.md or source-tree.md over 500 lines (ERR-12), write
  nothing to that document; say how many items there are and ask the owner how to proceed.

## Folding accepted ambiguity entries

An entry of `docs/specs/ambiguities/AMB-*.md` counts when its `state` is `accepted`, its `relates_to`
names the ID of an existing context document this run may write, and its `resolution` is empty.
1. **Already stated:** its action stays within an active decision or convention item's
   `version_range` (in the ecosystem's own range syntax; `x` stands for any number, and `unpinned`
   allows every version), or repeats a statement the document holds. Change nothing in the document
   for it.
2. **Otherwise, by what it changes:**
   - a path ending in `/` that no active root contains: a new root item with the next free SRC ID,
     `basis: convention`, and `notes` naming the entry (`"Accepted in AMB-001#ENT-04"`);
   - a technology no item lists, or a version outside an item's range: it can't be folded; report the
     entry and leave it unfolded;
   - anything else: one Convention bullet, worded from the entry's question and action. In
     tech-stack.md it goes in section 2 (Upgrades); in another document, in the section the entry's
     `checked` list names; when none is named, ask where it belongs (interview.md).
3. **After the document passes step 8's check,** set each folded entry's `resolution`, with Edit, to
   `"folded into CTX-NNN vN"` or `"folded into CTX-NNN vN: already stated"`, with the document's
   version after this run. Change no other field or entry, never write a new entry, and check again.

## Approval

Only on the user's explicit words in this run that approve a named document, or all context
documents (SKILL.md step 9 has the procedure). The approval Edits set `status: approved`,
`approved_by: "<approver>"`, `approved_on: <today>`, and add the row
`| <version> | <today> | <approver> | Approved | — |`. Nothing else changes. A document is approved
only when it passed the check and holds no marker and no active Proposed statement or item, its
detail files included.

## Checking with context_check.py

SKILL.md gives the commands. The script prints one line per error,
`<file>: <part>: <field>: <message> (<rule>)`, then `OK: <N> files checked` or
`INVALID: <N> error(s) in <M> file(s)`; exit 0 valid, 1 invalid, 2 can't run.
- The first `check --snapshot <start>` after writing is check 1. Repair each reported error with Edit
  and check again: at most three repair cycles, so at most four checks in the repair loop (the
  checks after recording folds and after an approval come on top). Record each check and repair
  in the reply, quoting the script's lines (`Check 1: INVALID: 1 error(s) in 1 file(s):
  docs/specs/context/testing.md: line 52: Basis: … (statement); repair 1: added the marker; check 2:
  OK: 10 files checked`).
- A line `<file>: unchanged, invalid: … (ERR-10)` names a document that already failed before the run
  and that the run left alone: report it (ERR-10), never repair it unasked.
- An error the run can't repair (it would mean changing an item that must stay, or a document outside
  the run's scope) ends the cycles early.

## When validation still fails (ERR-08)

After three repair cycles with errors left, or an error that can't be repaired:
1. For each document that existed before the run and still fails, run
   `context_check.py restore <start> <file> [<detail file>…]`, with each of its detail files that the
   snapshot holds. It prints `restored <file>` or `NOT RESTORED <file>: <reason>` for each. Report by
   path any detail file this run created for a restored document: it is no longer linked.
2. An ambiguity log goes back from the snapshot too when every entry the run folded into it targets a
   restored document; otherwise reset, with Edit, the `resolution` of each entry folded into a restored
   document to `""`, and confirm the log with `check --snapshot <start>`.
3. Documents that pass keep their changes; new documents stay `draft` with their errors listed; index.md
   lists the documents as they are after the restore.
4. Replace the report block and the next step with a validation-failure report: each file path and
   whether it was restored, kept or not restored; every check and repair; each unresolved error. Never
   present a document as approved.

## When the script can't run (ERR-13)

- `snapshot new` exits 2, or `python3`, PyYAML or jsonschema is missing: stop before writing. Say the
  context check couldn't run, quote its `Cannot run:` line, and write nothing.
- `check` or `restore` exits 2 after the run has written: name the files written and not checked, leave
  them `draft`, approve nothing, and replace the handoff with a report in ERR-08's format.

## Self-check list

Read each written file back for what the script can't decide:
1. Each statement and item says what its source says: a Decision states only what the cited ADR,
   setting or ARCH item decides; a Convention only what the user confirmed; an Observed statement only
   what the path shows. Nothing is invented.
2. No decision outcome is written as a Convention, no significant choice is written as a convention,
   and each `[NEEDS ADR]` marker appears once, in the right section (documents.md).
3. Every template section with neither a decision nor a convention holds a Proposed statement with
   `[NEEDS CLARIFICATION: confirm …]`, or a Basis of that form.
4. Every technology in use has one item, with the range, `used_by` and basis documents.md gives; every
   active non-external component has a code root and a tests root, observed, confirmed, decided or
   proposed.
5. The resolution line holds the entries of "The resolution line", in that order, and is the same in
   every row of the run and in the report.
6. Titles, owner, `freshness_days` and the Change Log row follow "Frontmatter" and "Change Log rows";
   no placeholder, example value or author comment is left.
7. In an existing document, only the statements and items whose source changed were edited, no item
   was deleted or renumbered, and the version and status follow "Changing an existing document".
8. index.md lists every document of the set with its version after the run; ui-mockups.md's table
   matches the stories' records.

## Example item blocks

```yaml
technologies:
  - id: TEC-01
    status: active
    name: "Python"
    version_range: "3.12.x"
    used_by:
      - "ARCH-001#CMP-01"
      - "ARCH-001#CMP-02"
    basis: decision
    upstream:
      - {id: ADR-001, relation: constrains, version: 1, hash: null}
    notes: ""
  - id: TEC-02
    status: active
    name: "pytest"
    version_range: ">=8,<9"
    used_by:
      - "ARCH-001#CMP-01"
    basis: convention
    observed_in: "pyproject.toml"
    observed_on: 2026-10-01
    notes: "Confirmed by the user on 2026-10-01"
roots:
  - id: SRC-01
    status: active
    path: "src/shift-service/"
    holds: code
    component: "ARCH-001#CMP-02"
    basis: proposed
    notes: "[NEEDS CLARIFICATION: confirm this folder]"
```
