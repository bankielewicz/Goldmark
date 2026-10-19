# Epic output rules

## Contents

- When to use this
- File and IDs
- Frontmatter
- Link records
- The `done_when` block
- Sections
- Markers
- Change Log
- When validation still fails (ERR-06)
- Self-check list
- Example

## When to use this

Read this before writing (SKILL.md step 7), and check every epic written against the self-check list
(step 8). Stories parse epics mechanically, through their `DW-NN` items and `refines` links, so an epic
that breaks a rule here breaks traceability for every story below it.

## File and IDs

| What | Pattern | Rule |
|---|---|---|
| Epic document | `EPIC-NNN`, at `docs/specs/epic/EPIC-NNN.md` | The highest existing number plus one, or `EPIC-001` if none; the next epic of this run takes the next number. Named by the ID only; the capability goes in `title` |
| Done-when item | `DW-NN` (two digits) | Unique within the epic, numbered in order from `DW-01` |

New epics are numbered in priority order: every Must epic, then Should, then Could; within a priority,
by the lowest requirement ID each refines. IDs are never reused or renumbered. In prose, refer to an
item by its qualified form: `PRD-001#FR-002`, `ARCH-001#DEC-02`, `EPIC-001#DW-01`.

## Frontmatter

Keep exactly these keys, in this order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | `EPIC-NNN`, equal to the file name |
| `type` | `epic` |
| `title` | Quoted capability name, such as `"Online appointment booking"` |
| `status` | `draft`. Only the user approves an epic |
| `version` | `1` |
| `created`, `updated` | Today, unquoted `YYYY-MM-DD` |
| `owner` | Quoted: the name the request gives, otherwise the PRD's `owner` |
| `authors` | Quoted names: the owner and `"claude-code"` |
| `generated_by` | Map with quoted, non-empty `tool` (`"claude-code"`), `model` (your own model ID) and `session` (`"${CLAUDE_SESSION_ID}"`) |
| `reviewed_by` | `[]` |
| `approved_by`, `approved_on` | `""` and `null` |
| `upstream` | Link records, one per line (next section) |
| `supersedes` | `[]` |
| `superseded_by` | `null` |
| `blocked_by` | `[]`. Every requirement an epic refines is ready, so nothing blocks it |
| `priority` | `must`, `should` or `could`: the highest priority among the FRs it refines. A shared NFR never raises it; an epic that refines only NFRs takes their highest priority |
| `target_release` | Quoted: the PRD's `target_release`, copied exactly. If the PRD has none, write `""` and add a section 8 marker. "None" includes `""` and the prd skill's placeholder `"[NEEDS CLARIFICATION: name of the current release]"`; never copy that placeholder |

Keep the `# --- epic-specific ---` comment line before `priority`. Delete the template's trailing
comments on frontmatter lines (`# draft | in-review | …`, `# every PRD requirement in scope…`,
`# must | should | could`).

## Link records

A link is written in flow form, one per line, with its fields in this order:
`- {id: PRD-001, item: FR-002, relation: refines, version: 2, hash: null}`.
- Fields: `id`, optional `item`, `relation`, `version`, `hash`, and an optional quoted `note`. No other
  field.
- `hash` is always `null`. Only the checker writes hashes.
- Links point upstream only. The PRD and ARCH never list their epics.

`upstream` holds, in this order:

| Link | Form | Rule |
|---|---|---|
| Each FR the epic groups | `{id: PRD-NNN, item: FR-NNN, relation: refines, version: <PRD version>, hash: null}` | In ID order. Only eligible FRs |
| Each NFR attached to it | `{id: PRD-NNN, item: NFR-NNN, relation: refines, version: <PRD version>, hash: null}`, adding `note: "partial: <which part>"` when more than one active epic, existing or new, refines that NFR | In ID order, after the FRs. Only eligible NFRs |
| Success metrics it moves (optional) | `{id: PRD-NNN, item: SM-NN, relation: informed_by, version: <PRD version>, hash: null, note: "<how this epic moves the metric>"}` | Only a metric the epic clearly moves |
| The ARCH it relied on | `{id: ARCH-NNN, relation: informed_by, version: <ARCH version>, hash: null}` | Exactly one, last: the current ARCH at its frontmatter `version` |

`<PRD version>` is the version read at SKILL.md step 2, and `<ARCH version>` is the ARCH's own `version`
field, not the version of its PRD link. Never link an ADR, a DEC, a policy setting or another epic:
the ARCH link carries the decisions, and dependencies on other epics are prose in section 5.

## The `done_when` block

Section 4 holds one fenced block whose info string is exactly `yaml items` and whose only top-level key is
`done_when`, with at least one item:

| Field | Value |
|---|---|
| `id` | `DW-NN` |
| `status` | `active` |
| `criterion` | Quoted: an integrated outcome that can only be checked once several stories are done, broader than one story's acceptance criteria |
| `evidence_method` | Quoted: how it will be shown, such as an end-to-end test, a demo or a metric |

Quote every free-text value with double quotes. Inside the fence use `#` comments only, and delete the
template's. Together, the DW items cover every requirement the epic refines.

## Sections

Keep these headings, in this order, exactly as the template has them: `## 1. Goal`,
`## 2. Business value`, `## 3. Scope`, `## 4. Done when`, `## 5. Dependencies and risks`,
`## 6. Technical notes (optional)`, `## 7. Story map`, `## 8. Open questions`, `## Change Log`. The title
heading is `# EPIC-NNN — <title>`.

- **1.** One paragraph: what users can do when this epic is done that they can't do today. No
  implementation.
- **2.** Prose: why it matters, citing the PRD's success metrics by qualified ID where they apply.
- **3.** `**In scope**`: one bullet per requirement the epic refines, naming it by qualified ID (for a
  shared NFR, the part in scope). `**Out of scope**`: related requirements handled elsewhere, each with
  where (`covered by EPIC-002` for an FR, `refined by EPIC-002` for an NFR, `in EPIC-003`,
  `left out: later`, `not planned`), or `- None.`
- **4.** The `done_when` block.
- **5.** Prose: the ARCH decisions this epic relies on, by qualified DEC ID with their resolvers
  (`ARCH-002#DEC-01, resolved by ADR-005`); dependencies on other epics; and risks. When an input is a
  draft, this section opens with the proposal sentence (Markers).
- **6.** High-level constraints only, such as a mandated platform or an NFR that shapes the work. Detailed
  design belongs to specs and ADRs. Write `None.` if there are none; keep the heading.
- **7.** Exactly the template's two-line comment and nothing else:

  ```
  <!-- GENERATED from stories whose upstream cites this epic: story, title, status, DW satisfied.
       Do not edit by hand. -->
  ```

  It is the only `<!--` left in the file.
- **8.** A bullet per `[NEEDS CLARIFICATION: …]` marker, or `- None.`

Delete every other `<!-- -->` comment and every placeholder: no `EPIC-000`, `PRD-000`, `YYYY-MM-DD`, `<item>`,
`<capability name>`, `<integrated outcome>` or template example link is left.

## Markers

| Marker | When | Where |
|---|---|---|
| `**Proposal:** PRD-NNN vN is a draft, so this epic is a proposal until it is approved.` (name the ARCH instead, or both, when the ARCH is the draft) | The PRD or the ARCH has a `status` other than `approved` | First line of section 5, in every epic written |
| `[NEEDS CLARIFICATION: grouping proposed by the skill; not confirmed by the user]` | An FR's placement is unconfirmed: no grouping was given and nobody could confirm one, or a stated grouping left an FR unplaced or placed it twice and nobody could be asked (SKILL.md step 6) | Section 8, in every epic written |
| `[NEEDS CLARIFICATION: target release; the PRD sets none]` | The PRD has no `target_release`, or only the prd skill's placeholder | Section 8 |

## Change Log

The template's four columns: version, date, author, change. One row, authored
`claude-code (session ${CLAUDE_SESSION_ID})`, the same ID as `generated_by.session`:

```markdown
| 1 | 2026-09-28 | claude-code (session 7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f) | Initial draft from PRD-003 v4 and ARCH-002 v3 |
```

## When validation still fails (ERR-06)

After three failed fix attempts, stop. Keep every epic written, as `draft`: never delete one, and never
touch any other file. End with a validation-failure report: each file path and its unresolved errors.
Don't present the epics as ready for the story step, and leave out the next step.

## Self-check list

Read each epic written back and check every item. Fix and re-check, at most three attempts, editing only
epics written in this run.

1. The path is `docs/specs/epic/EPIC-NNN.md`, `id` equals it, the number was free before this run, and
   this run's epics are numbered Must, then Should, then Could.
2. The frontmatter has exactly the keys in the table, in order, with valid values: `type: epic`,
   `status: draft`, `version: 1`, `created` and `updated` today, `blocked_by: []`.
3. `generated_by` has non-empty `tool`, `model` and `session` (this session); `reviewed_by: []`;
   `approved_by: ""`; `approved_on: null`; every `hash` is `null`.
4. `priority` follows the rule in the table, and `target_release` equals the PRD's, or is `""` when the
   PRD has none or only prd's placeholder.
5. `upstream` has a `refines` link, at the PRD version read, for every requirement the epic groups, and
   each of them is eligible (selection.md); partial notes where an NFR is shared; optional SM links;
   exactly one `informed_by` link to the current ARCH at its version, last. No other link.
6. Across the run, the invariants (SKILL.md step 6) hold:
   - every eligible FR is refined by exactly one new epic, and no covered or left-out requirement is
     refined by any;
   - every eligible NFR is attached to at least one new epic, unless an active existing epic already
     refines it;
   - a standalone NFR epic exists only for an eligible NFR that no active epic refines.
   Which epics an NFR goes in is the grouping's choice; a grouping the user stated or changed decides it.
7. Section 4 has one `yaml items` fence holding only `done_when`, with at least one item; IDs are
   `DW-01` onward in order; each item has `id`, `status`, `criterion` and `evidence_method`, and text is
   double-quoted.
8. All nine headings are present in order; sections 3 and 8 hold bullets; section 7 holds exactly the
   GENERATED comment; no other `<!--`, no `<…>` placeholder, no `EPIC-000`, `PRD-000` or `YYYY-MM-DD`,
   and no template example link is left.
9. When an input is a draft, section 5 opens with the proposal sentence. When an FR's placement in the
   run is unconfirmed (SKILL.md step 6), section 8 of every epic written has the unconfirmed-grouping
   marker.
10. The Change Log has one row, whose author is `claude-code (session <ID>)` with this session's ID.
11. No other file changed: the PRD, ARCH, ADRs, policy documents and existing epics are as they were.

## Example

A new epic for PRD-003 version 4, relying on ARCH-002 version 3:

````markdown
---
id: EPIC-006
type: epic
title: "Front-desk day view"
status: draft
version: 1
created: 2026-09-28
updated: 2026-09-28
owner: "Sam Okafor"
authors: ["Sam Okafor", "claude-code"]
generated_by:
  tool: "claude-code"
  model: "claude-opus-5-5"
  session: "7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f"
reviewed_by: []
approved_by: ""
approved_on: null
upstream:
  - {id: PRD-003, item: FR-006, relation: refines, version: 4, hash: null}
  - {id: PRD-003, item: NFR-001, relation: refines, version: 4, hash: null, note: "partial: patient details shown at the front desk"}
  - {id: ARCH-002, relation: informed_by, version: 3, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- epic-specific ---
priority: could
target_release: "Autumn pilot"
---

# EPIC-006 — Front-desk day view

## 4. Done when

```yaml items
done_when:
  - id: DW-01
    status: active
    criterion: "Front-desk staff see the day's appointments, and patient details never leave the EU region"
    evidence_method: "End-to-end test as front-desk staff, plus a check of the storage region"
```
````

(Sections 1–3 and 5–8 and the Change Log are omitted here; a written epic has them all.)
