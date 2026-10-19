# DSN output rules

## Contents

- When to use this
- Files
- Frontmatter
- Link records
- Board items
- The idea coverage table
- Sections 1, 4 and 5
- Writing a new DSN
- Amending a DSN
- Change Log rows
- Approval
- The report
- Checking with dsn_check.py
- When validation still fails (ERR-11)
- When the script cannot run (ERR-14)
- Example board items
- Self-check list

## When to use this

Read this before writing (SKILL.md step 7), and check every written file at step 8: `dsn_check.py check`
decides every rule a script can, and the self-check list at the end keeps only what needs judgement. Later
skills read the DSN mechanically (its ID, version and status, the active board items, the canvas facts), so a
document that reads well but breaks a rule here misleads every document that cites it.

## Files

- `docs/specs/design/<ID>.md`, where `<ID>` is the ID the `next` command printed (a create run) or the DSN's
  own ID (an amend run). Never take a file name from the user.
- The boards copy, `docs/specs/design/<ID>/boards/`, is written by the import (`place` moves the files in); never
  with Write or Edit, and never edited after it is placed. In the session's scratchpad the skill also writes the
  files it publishes to the canvas and the digest file of an import.
- Write nothing else in the repository: no other document. Never delete a file or an artifact.

## Frontmatter

Keep exactly the template's keys, in its order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | The ID; equals the file name |
| `type` | `design` |
| `title` | Quoted: the BRN's title followed by `: release design`, unless the user gives another |
| `status` | `draft` for a new DSN; the rules of "Amending" and "Approval" otherwise. Never `superseded` or `deprecated`: those are the user's |
| `version` | `1` for a new DSN; plus one per amend run; unchanged by an approval |
| `created`, `updated` | Unquoted `YYYY-MM-DD`: today for a new DSN; an amend or an approval sets `updated` to today |
| `owner` | Quoted: the BRN's `owner`, unless the user names another |
| `authors` | `["<owner>", "claude-code"]` |
| `generated_by` | `tool: "claude-code"`, `model:` your own model ID, `session:` the session ID SKILL.md gives, all quoted. When the host cannot provide one, write `"unavailable"` and say so in this write's Change Log row and in the report. An amend keeps the existing values |
| `reviewed_by` | `[]` for a new DSN; kept otherwise. Humans only |
| `approved_by`, `approved_on` | `""` and `null` until approval |
| `upstream` | Link records (next section) |
| `supersedes`, `superseded_by`, `blocked_by` | `[]`, `null`, `[]` |
| `canvas` | The `https://` URL the boards were imported from, quoted: the URL of the publish result, or the one the request gave; `null` when the boards were already in the folder and the request gave none |
| `canvas_version` | Quoted. After an import, the identifier the Artifact tool's read reported for the canvas as imported, never asked: the copy now in the boards folder. For a copy already in the folder, the version the request states, else `null` with a marker, and never asked. Never carried over from an earlier copy |
| `canvas_format` | The integer `v` of `canvas.json`, from the `boards` command |
| `boards_root` | `docs/specs/design/<ID>/boards/` for this document's own ID |
| `considered` | `[]` for a new DSN. In an amend run, entries of the form `PRD-NNN@N`, `ADR-NNN@N`, `declined:PRD-NNN#FR-NNN`, `declined:PRD-NNN#NFR-NNN` or `declined:ADR-NNN`, without repeats; no `declined:` entry names an item an active board's `answers` holds |

Keep the `# --- design-specific ---` line before `canvas`, and delete the template's other comments.

## Link records

One record per line, in flow form: `- {id: BRN-001, relation: derives, version: 1, hash: null}`. `hash` is always
`null`. `upstream` holds:
- one `derives` link to the BRN with no `item`, at the BRN's version;
- one `derives` link with `item: IDEA-NN` to the same BRN, at the same version, for each distinct idea an
  active board names, a `withdrawn` idea that a board still names included;
- nothing else: no link to a PRD or an ADR. A board's references to requirements and ADRs are plain `answers`
  text, because the PRD's section 8 links the DSN, and a link back would make each bump the other's suspect link
  for ever.

After every change to the items, keep this list exact: add a link for a newly named idea, and drop the link of
an idea no active board names any more.

## Board items

One item for each board `canvas.json` names, numbered `BRD-01` upward in canvas (key) order. Quote every
free-text value.

**Step order within a flow.** A flow's active items, in the order they stand in the block, are its steps. A
create run writes the items in canvas order and, where the user confirmed another step order for a flow,
rearranges that flow's items among the positions they occupy. An amend inserts a new board's item right after the
item at the step before it (or right before the item at the step after it), and a new flow's items after the last
item. BRD numbers are allocated once, in order of appearance, and never renumbered, so they need not ascend down
the block, and no check rule sorts it. A step order the user has not confirmed is recorded as proposed, with a
`[NEEDS CLARIFICATION: step order of the flow <slug>]` marker in section 5.

| Field | Holds |
|---|---|
| `id` | `BRD-NN`, two digits, never renumbered or reused |
| `status` | `active`, or `deprecated` for a board that left the canvas; an item is never deleted |
| `superseded_by` | Optional: the `BRD-NN` that replaces a deprecated board, only when the user says so |
| `file` | The file name as `canvas.json` names it: flat and plain, no `/`, `\`, `..` or control character |
| `title` | The name the user confirmed, else the file name up to its first dot (the whole name when that is empty, as for `.hidden`) |
| `flow` | The user's flow as a lowercase slug, or `null` until confirmed |
| `surface` | `web`, `desktop`, `mobile` or `terminal`, or `null` until confirmed |
| `ideas` | The promoted ideas the board shows, a list of `IDEA-NN`; `[]` when the user confirmed none; `null` until confirmed |
| `answers` | `PRD-NNN#FR-NNN`, `PRD-NNN#NFR-NNN` or `ADR-NNN`: a list, `[]` when none. Written only for a candidate the user confirmed or a reference the user states |
| `sha256` | The digest the `boards` command printed for the file at this run |
| `notes` | Optional: free text, or `null`; it holds the marker for each `null` field of the item |

An item has no other field, `upstream` included. An item with a `null` `flow`, `surface` or `ideas` has
`[NEEDS CLARIFICATION` in its `notes`. A file that left the canvas and came back gets a new item with the next
free number; the deprecated item stays.

## The idea coverage table

Section 3 holds `| Idea | Boards | Status |` and one row for each promoted idea of the BRN, in idea ID order, at
the version read. The `Boards` cell lists the active `BRD-NN` items that name the idea, as plain values
separated by commas (`BRD-01, BRD-03`: no backticks, no "and"), or `none`; `check` rejects other forms.
- `designed`: at least one active board names the idea.
- `not a screen`: the user said no board is needed.
- `no board yet`: no active board names it and the user has not said `not a screen`. Section 5 holds a marker
  that names the idea.
- `withdrawn`: an earlier version listed the idea as promoted and the BRN no longer does. `Boards` lists the
  active boards that still name it, or `none`.

Keep section 3 equal to this after every write. Never add a row for an idea that is not promoted, other than a
`withdrawn` row, and never record an idea as `not a screen` unless the user said so.

## Sections 1, 4 and 5

- **1. Scope:** two to four sentences: the product or release (from the BRN's title and context), the flows and
  the surfaces that appear. Claim nothing the user did not confirm.
- **4. Canvas:** where the boards were imported from: the `canvas` and `canvas_version` values (or `null` with
  the marker `[NEEDS CLARIFICATION: canvas URL and version copied]`), the date of the import when known, and the
  re-import rule the template states: a new import means a new run, which amends the document. The brief is not
  recorded here.
- **5. Open questions:** one `[NEEDS CLARIFICATION: …]` bullet for each marker the document holds outside a
  board's `notes`, or `None.`

## Writing a new DSN

Use Write once, from the template (SKILL.md gives the path):
- keep every heading of the template, in its order;
- delete every author comment, HTML and `#` alike, and replace every `[[fill: …]]` placeholder and every
  stand-in value (`DSN-000`, `BRN-000`, `YYYY-MM-DD`, `canvas_format: 0`, the example board and its zero digest);
- ID, version 1, status `draft`, `created` and `updated` today, `considered: []`;
- one board item for each board in canvas order (a flow's items rearranged among the positions they occupy
  where the user confirmed another step order), with the file, the confirmed or `null` mapping, `answers: []`
  unless the user stated a reference (taken as stated: the check, not the interview, tests that the item
  exists), and the digest from the script;
- sections 3 to 5 as above, and one Change Log row.

The write is a revision of no earlier document: it overwrites nothing.

## Amending a DSN

Use Edit, never Write, starting from the document's current text.
- **Version:** raise `version` by one whatever the status, a draft's included, at most once per run, by the
  first write that changes content. Later edits in the same run (repairs, the user's corrections) keep the
  version and update that run's Change Log row instead of adding another. Set `updated` to today.
- **Change only** what the user confirmed or a script fact requires (a digest, `canvas_format`, a link
  version, `considered`). Keep every other line.
- **Items:** never delete or renumber one. Deprecate a removed board's item. Insert a new board's item (a file
  that left and came back included) with the next free BRD number at the step the user confirms within its flow,
  and a new flow's items after the last item; the insertion is for ordering only, and no number changes. Add the
  confirmed `answers` entries (never to `upstream`). Update `sha256` for every changed board.
- **`canvas_version`:** replaced by the new identifier only in a run that writes for another reason. A run that
  imports and then finds nothing to change ends at ERR-17, leaves the recorded version, and names the canvas's
  newer identifier in its report as recorded nowhere yet. Without an import, in a run in which a board changed,
  was added or was removed, it is `null` with the marker unless the request states one, never the old value.
  When no board changed, keep it. A DSN written on the copy path with a null canvas or `canvas_version` keeps its
  marker in section 4 and cannot be approved until an import, or a run whose request states the fact, fills it.
- **Status:** an approved DSN becomes `in-review` with `approved_by` cleared and `approved_on` null, so the
  change is reviewed explicitly; a draft or in-review DSN keeps its status.
- **Keep** the existing `authors` (adding the tool if it is missing), `reviewed_by` and every Change Log row.
  The new version has not been reviewed: say so in the Change Log row and in the report.
- **Links:** move them to the BRN's current version in the same run when the BRN has moved. After every item
  change, `upstream` holds one item link for each distinct idea an active board names (a kept `withdrawn` idea
  included) and no other.

## Change Log rows

Never change a row an earlier run wrote; this run's own row may grow to name every change. Never write the
literal marker text `[NEEDS CLARIFICATION` in a row: rows are never rewritten, so it would block approval for
ever. Describe markers in words ("1 marker left"). Columns: `| Version | Date | Author | Change | Items affected |`.

| Event | Version | Author | Change |
|---|---|---|---|
| New DSN | `1` | `claude-code (session <session ID>)` | `Created from BRN-NNN vN and the boards; N markers left`, N being the count |
| Amend (one row per run) | the new version | the same | The boards changed, added or removed (by file); whether the copy was imported again from the canvas; the `canvas_version` now recorded; the ideas, requirements and ADRs considered, with the document versions read; any link moved; `The new version has not been reviewed` |
| Approval | the current version | the approver's name | `Approved` |

Items affected: `all` for a new DSN; the BRD IDs and section names for an amend; `status` for an approval. The
session ID is the one SKILL.md gives.

## Approval

Only on the user's explicit words (SKILL.md step 9).

**The offer.** When the request does not already approve the DSN, offer approval once: with AskUserQuestion when
it is available and the request does not say to proceed without questions, "Approve <ID> now?", with "Not now"
first and marked (Recommended), then "Approve". No answer, or "Not now", leaves the status as it is. Without
AskUserQuestion, the offer is the last finding of step 10, in plain text, before the Next step paragraph:
"Approve <ID> now? Reply 'approve <ID>' with your name, or 'not now'." If the user never answers, the status
stays and the Next step still holds; a later `approve <ID>` is an approval-only run.

**The approver.** `approved_by` is the name the user gives ("I'm Example Owner, and I approve DSN-001"),
including words after `approve DSN-NNN`. Words that name no one mean ask who is approving, offering the
document's owner first; that question, without AskUserQuestion, takes the same last-finding slot as the offer.
When no answer can arrive (under "proceed without questions" the gate stays closed), do not approve and say the
approver was not named.

**The Edit.** One Edit touches only `status` (`approved`), `approved_by`
(the name the user gave), `approved_on` (today), `updated` (today) and one new Change Log row
`| <version> | <today> | <approver> | Approved | status |`. Raise no version. Then run the check again. If it
fails, undo with a second Edit that restores those four fields and removes the row, and report the DSN as not
approved, with the errors. If the undo fails, report "approval rollback failed" with the `status`,
`approved_by` and `approved_on` the file now holds.

## The report

Step 10 of SKILL.md gives the block and the next step. These are the rules of each line and of what follows.
- **`Boards`** is printed in a create and in an amend run and names the copy in the boards folder that the run
  read: `boards_root`, the number of active boards, and `canvas_version` or `unknown`.
- **`Flows`** lists the flows in the order they first appear in the boards block, each hyphen shown as a space,
  with `unconfirmed (<count>)` last for the boards with a `null` flow. `none` only when there are no active
  boards; with no confirmed flow the line is `Flows: unconfirmed (<count>)`.
- **`Boards with no idea`** lists the active boards whose `ideas` and `answers` are both `[]` (not `null`).
  **`Ideas with no board`** lists the `no board yet` rows. **`Markers left`** counts every
  `[NEEDS CLARIFICATION` marker in the DSN, a board's `notes` included.
- **The last line** is the script's last line: `OK docs/specs/design/<ID>.md`. After a write whose check could
  not run (ERR-14), it is the script's `Cannot run: …` line instead, and the report names the DSN as written and
  not checked.
- **An approval-only run's block** is the one line `Design document: <ID> (v<N>, approved)`. When that run
  approved nothing, the line shows the status as it is: `(v<N>, <status>; not approved)`.
- **Findings follow the block,** briefly: where the boards came from (imported from the canvas at version
  `<identifier>`; or a copy recorded as it is, with `no Artifact tool in this session`, `no canvas is recorded` or
  `recorded as it is by choice`, and the canvas not checked); a pending boards folder under another number that
  the run did not use; each check and repair, quoting the script's lines; in an amend run both
  pre-check commands as run, in order (`dsn_check.py boards DSN-NNN`, then `dsn_check.py check --before-amend
  DSN-NNN`), each with its last line and its `fact:` lines; the suspect-link warnings; the `answers` entries gone
  stale (a deprecated PRD item, an ADR no longer accepted); the boards `head` reported as read in part; in an
  amend run the documents that cite the DSN at an older version; the candidates the user declined or that were
  left for a later run, with their number; the unconfirmed mappings; and, after an amend, that the new version
  has not been reviewed, and any provenance written as `unavailable`. The last finding is the plain-text
  approval offer or approver question of step 9 (see "Approval"), only when step 9 makes one: no marker left, no
  "proceed without questions", no approval already given in the request, and no AskUserQuestion. The Next step
  paragraph follows it and ends the reply.
- **No block** for a run that stops before writing: ERR-01 to ERR-10, ERR-12, ERR-14 before the write, ERR-15
  without confirmation, ERR-16 to ERR-24, ERR-13 without a save, and an approval-only request for an approved
  DSN. A run that ends after making a canvas has the canvas report in its place (the canvas reference).
  ERR-11's report replaces the block and the next step.

## Checking with dsn_check.py

SKILL.md gives the commands. `check` prints one line for each error,
`<file>:<line>: <part>: <message> (<rule>)`, a `warning: …` line for each suspect link or stale reference, then
`OK <file>` or `INVALID: <n> error(s) in <file>`; exit 0 valid, 1 invalid, 2 cannot run.
- Run one initial check, then at most three repair-and-readback cycles, so at most four checks. A repair
  changes the file to address a reported error.
- Record each check and repair in the reply, quoting the script's lines (`Check 1: INVALID: 1 error(s) in
  docs/specs/design/DSN-001.md: …; repair 1: added the marker; check 2: OK docs/specs/design/DSN-001.md`).
- A suspect-link warning is named in the reply and never repaired by editing another document.
- An error that cannot be repaired (one in a file this skill may not edit) ends the cycles early and is
  reported.

## When validation still fails (ERR-11)

After the initial check and three repair cycles with errors left, or an error that cannot be repaired: stop. A
new DSN stays `draft`. An amended DSN keeps the status "Amending" gave it and is never set to `approved`.
Replace the report block and the next step with a validation-failure report that lists the checks, the repairs
and the unresolved errors. Do not hand off to the PRD and do not name the DSN as ready.

## When the script cannot run (ERR-14)

- Before writing (a script command exits other than 0 or 1, `python3` or the script is missing, or a BRN it
  needs cannot be read): stop, say the check could not run, quote its `Cannot run:` line, and write nothing.
- After the run has written (`check` exits other than 0 or 1): name the file as written and not checked, leave
  its status as it is, approve nothing, and say so in the report. The block's last line is the script's
  `Cannot run: …` line in place of `OK …` (see "The report").

## Example board items

```yaml
boards:
  - id: BRD-01
    status: active
    file: "Home.dc.html"
    title: "Home"
    flow: report-and-home
    surface: web
    ideas: ["IDEA-02"]
    answers: []
    sha256: "9f2c5a1e0b7d4c3a8e6f1d2b3c4a5968778695a4b3c2d1e0f9a8b7c6d5e4f3a2"
    notes: null
  - id: BRD-02
    status: active
    file: "List.dc.html"
    title: "List"
    flow: shifts
    surface: terminal
    ideas: null
    answers: ["ADR-009"]
    sha256: "1b3d5f7a9c0e2b4d6f8a0c2e4b6d8f0a1c3e5b7d9f1a3c5e7b9d1f3a5c7e9b0d"
    notes: "[NEEDS CLARIFICATION: ideas this board shows]"
```

## Self-check list

Read each written file back for what the script cannot decide:
1. Each mapping says what the user confirmed: every title, flow, surface, idea list, `answers` entry and
   coverage status is the user's words or a stated answer, never something inferred from a board's text.
2. Every `null` has a marker that names what is open, and no marker is left for a value now known. Section 5
   holds a marker for each `no board yet` idea, and the count in the Change Log row matches the document.
3. Section 1 claims nothing the user did not confirm: no flow, surface or idea beyond the board items.
4. The Change Log row describes the run: the boards changed, added or removed, whether the copy was imported
   again, the `canvas_version` now recorded, the documents and versions considered, the links moved, the
   unavailable provenance, and the statement that an amended version has not been reviewed.
5. `canvas` and `canvas_version` are the tool's (after an import) or the request's words, never asked and never
   carried over from an earlier copy; section 4 says where the boards came from.
6. A flow's step order is the user's, or recorded as proposed with its marker in section 5.
7. In an amend run, only what the user confirmed or a script fact required was edited, no item was deleted or
   renumbered, the version rose once, `considered` follows the lazy rule, and the status follows "Amending".
8. No author comment, `[[fill:` placeholder or stand-in value is left (the check finds comments and
   placeholders, not `DSN-000` or a zero digest).
9. Nothing was written in the repository but the DSN and the boards copy.
