# BRN output rules

## Contents

- When to use this
- File and IDs
- Frontmatter
- Item blocks
- Collections and fields
- Unknowns
- Change Log
- Validation checklist
- Example item blocks

## When to use this

Read this before writing a BRN document, and use the validation checklist after writing it. The
rules exist because the PRD workflow and the `devforgeai check` validator parse these documents
mechanically. A document that reads well but breaks a rule here breaks everything downstream.

## File and IDs

| What | Pattern | Rule |
|---|---|---|
| Document ID | `BRN-NNN` (three digits, e.g. `BRN-007`) | Next free number in `docs/specs/brainstorm/` |
| File path | `docs/specs/brainstorm/BRN-NNN.md` | Named by the ID only; the topic goes in `title` |
| Problem ID | `PRB-NN` (e.g. `PRB-01`) | Unique in the document |
| Idea ID | `IDEA-NN` (e.g. `IDEA-03`) | Unique in the document |
| Assumption ID | `ASM-NN` (e.g. `ASM-02`) | Unique in the document |

Number items from `01` in each collection, in order. IDs are never reused, renumbered or deleted.
When referring to an item in prose, use the qualified form `BRN-001#IDEA-03`.

## Frontmatter

Keep exactly these keys, in this order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | `BRN-NNN`, equal to the file name |
| `type` | `brainstorm` |
| `title` | Quoted descriptive topic, never empty |
| `status` | `draft`, `converged` or `archived` |
| `version` | Integer, `1` for a new BRN |
| `created`, `updated` | Unquoted `YYYY-MM-DD` dates |
| `owner` | Quoted name of the accountable human |
| `authors` | List of quoted names, e.g. `["Bryan", "claude-code"]` |
| `generated_by` | Map with quoted `tool`, `model` and `session`, all non-empty |
| `reviewed_by` | `[]` (only humans who reviewed it; never filled by the AI) |
| `approved_by` | `""` |
| `approved_on` | `null` |
| `upstream` | `[]`, or link records (usually `relation: informed_by`) |
| `supersedes` | `[]` |
| `superseded_by` | `null` |
| `blocked_by` | `[]` |
| `participants` | List of quoted names, or `[]` |
| `sources` | List of quoted raw inputs (notes, tickets, URLs), or `[]` |

Keep the `# --- brainstorm-specific ---` comment line before `participants`.

A link record is written in flow form, one per line:

```yaml
upstream:
  - {id: BRN-001, relation: informed_by, version: 1, hash: null, note: "optional"}
```

`hash` is always `null`. Only the validator writes hashes; a typed hash is wrong by definition.

## Item blocks

- An item block is a fenced block whose info string is exactly `yaml items`.
- Each fence holds **exactly one top-level key**: `problems`, `ideas` or `assumptions`.
- Inside a fence use `#` comments only. `<!-- -->` notes may not appear inside a fence (and none
  remain in a finished BRN).
- **Quote every free-text value** with double quotes. Unquoted text that starts with `>`, `|`,
  `*`, `&`, `!`, `@`, `%` or a backtick, or that contains `: ` or ` #`, silently changes meaning.
  Unquoted `yes`, `no` or `3.10` change type.
- For lists of IDs or prose, use block sequences (`- PRB-01`), never flow lists (`[PRB-01, PRB-02]`).
- IDs, enum values, `null` and numbers are not quoted.

## Collections and fields

Each item has `id` and `status` (`active` or `deprecated`), and may have `superseded_by` (an item
ID) and `upstream` (a list of link records). Besides those, only these fields are allowed:

**`problems`**

| Field | Value |
|---|---|
| `statement` | Quoted, one sentence, from the user's side |
| `who` | Quoted persona |
| `evidence` | Quoted source or data, or `"[NEEDS CLARIFICATION: …]"` |
| `severity` | `high`, `medium` or `low` |

**`ideas`**

| Field | Value |
|---|---|
| `idea` | Quoted idea; the user's own words when the idea came from the user |
| `addresses` | Block list of one or more `PRB-NN` IDs that exist in this document |
| `value`, `effort`, `risk` | `null`, a quoted rating such as `"high"`, or a number, per the framework |
| `score` | `null` or a number, per the framework |
| `disposition` | `open`, `promoted`, `parked` or `rejected`; anything but `open` only when user-confirmed |
| `reason` | `null` while `open`; otherwise a quoted reason the user confirmed |

**`assumptions`**

| Field | Value |
|---|---|
| `statement` | Quoted, phrased "We believe that …" |
| `validation` | Quoted experiment, interview or data that would confirm it |
| `state` | `open`, `validated` or `invalidated` |

No other collections and no other fields. Put method-specific reasoning in the prose sections.

## Unknowns

Never guess. Write `[NEEDS CLARIFICATION: <question>]` where the answer is unknown, in prose or
inside a quoted value. Section 7 also lists open questions as `- [NEEDS CLARIFICATION: …]` bullets;
if there are none, write `- None.`

## Change Log

Add one row per version: version, date, author, what changed. When you write the row, the author
is `claude-code (session <session ID>)`, with the same ID as `generated_by.session`:

```markdown
| 2 | 2026-09-27 | claude-code (session 7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f) | Added IDEA-13 to IDEA-22 |
```

The session ID is the name of that conversation's transcript, so `claude --resume <ID>` can reopen
it. Never edit earlier rows: they record the sessions that wrote earlier versions.

## Validation checklist

Check each item; fix and re-check anything that fails.

1. The file path is `docs/specs/brainstorm/BRN-NNN.md` and `id` equals `BRN-NNN`.
2. The frontmatter has exactly the keys in the Frontmatter table, with valid values.
   `generated_by` has non-empty `tool`, `model` and `session`, `reviewed_by` is `[]`, and every
   `hash` is `null`.
3. Every item ID matches its pattern, is unique, and is numbered in order.
4. Every `yaml items` fence holds exactly one top-level key: `problems`, `ideas` or `assumptions`.
5. Every item has only allowed fields, and every enum value is in its list.
6. Every free-text value is double-quoted, and no flow lists are used for IDs or prose.
7. Every `addresses` entry names a `PRB-NN` that exists in the document.
8. `disposition` is not `open`, and `status` is `converged`, only where the user confirmed it.
9. All eight numbered section headings and `## Change Log` are present.
10. No leftovers remain: no `<!--`, no `<…>` placeholders, no `BRN-000`, no `YYYY-MM-DD`, and no
    empty `title`.
11. The last Change Log row's author is `claude-code (session <ID>)` with the ID in
    `generated_by.session`, unless a person wrote that row.

## Example item blocks

````markdown
```yaml items
problems:
  - id: PRB-01
    status: active
    statement: "Patients forget appointments booked weeks ahead."
    who: "Returning patient"
    evidence: "[NEEDS CLARIFICATION: current no-show rate]"
    severity: high
```

```yaml items
ideas:
  - id: IDEA-01
    status: active
    idea: "Text a reminder two days before, with a one-tap reschedule link."
    addresses:
      - PRB-01
    value: "high"
    effort: "low"
    risk: "low"
    score: 4
    disposition: open
    reason: null
```
````
