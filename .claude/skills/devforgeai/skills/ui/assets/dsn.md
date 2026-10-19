---
id: DSN-000
type: design
title: ""              # the BRN's title followed by ": release design", unless the user gives another
status: draft          # draft | in-review | approved | superseded | deprecated
version: 1
created: YYYY-MM-DD
updated: YYYY-MM-DD
owner: ""              # the BRN's owner, unless the user names another
authors: []            # the owner and "claude-code"
generated_by:
  tool: ""
  model: ""
  session: ""
reviewed_by: []
approved_by: ""
approved_on: null
upstream:              # derives links at the BRN's version: one to the BRN without an item, then one with item IDEA-NN for each distinct idea an active board names
  - {id: BRN-000, relation: derives, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- design-specific ---
canvas: null           # the canvas URL the boards were imported from ("https://…"): the publish result's or the request's, else null
canvas_version: null   # the identifier the import reported for the copy now in boards/ (quoted), else the request's, else null
canvas_format: 0       # the v of canvas.json, read from the boards command, never asked
boards_root: docs/specs/design/DSN-000/boards/
considered: []         # [] for a new DSN; an amend run adds PRD-NNN@N, ADR-NNN@N and declined:… entries
---

# DSN-000 — [[fill: the title, as in the frontmatter]]

<!-- Design document (DevForgeAI). The screen designs of one release, imported from a Claude Design canvas into
     boards_root (or placed there by the user and recorded as they were), and the mapping of each board to a
     flow, a surface and the brainstorm's promoted ideas.

     BELONGS HERE: the board items, the idea coverage, the canvas facts and the open markers.
     DOES NOT: a board's content, design tokens or colours (the context documents own the look), a story's
     design, or a decision the user did not confirm.

     MARKERS. A value the user has not confirmed is null with [NEEDS CLARIFICATION: …], in the board's
     notes or in section 5. The document cannot be approved while any marker remains.
     AUTHOR COMMENTS. Delete every comment in this file, HTML and # alike, except the line
     # --- design-specific ---, and replace every [[fill: …]] placeholder and every stand-in value
     (DSN-000, BRN-000, YYYY-MM-DD, canvas_format: 0, the example board) before writing.
     CHANGES. An amend raises version once a run, adds one Change Log row and never deletes or renumbers a
     board item.
     Delete these comments when you fill in the document. -->

## 1. Scope

[[fill: two to four sentences: the product or release the design covers, from the BRN's title and context, the flows and the surfaces that appear; nothing the user did not confirm]]

## 2. Boards

<!-- One item for each board canvas.json names, numbered BRD-01 upward in its order. A flow's items, in the
     order they stand here, are its steps; after an amend, a board added later gets the next free number and is
     inserted at its step. Quote every free-text value. A deprecated item stays. notes holds the marker for each
     null field of the item, else null. Delete the # comments below. -->

```yaml items
boards:
  - id: BRD-01                  # two digits, allocated in order, never renumbered or reused
    status: active              # active, or deprecated for a board that left the canvas
    file: "Example.dc.html"     # the file name as canvas.json names it
    title: "Example"            # the name the user confirmed, else the file name up to its first dot
    flow: null                  # a lowercase slug such as day-to-day, or null until the user confirms it
    surface: null               # web, desktop, mobile or terminal, or null until the user confirms it
    ideas: null                 # promoted ideas it shows, as a list of IDEA-NN; [] for none, null until confirmed
    answers: []                 # PRD-NNN#FR-NNN, PRD-NNN#NFR-NNN or ADR-NNN the user confirmed, as plain text
    sha256: "0000000000000000000000000000000000000000000000000000000000000000"   # from the boards command
    notes: "[NEEDS CLARIFICATION: flow, surface and ideas]"
```

## 3. Idea coverage

<!-- One row for each promoted idea of the BRN, in idea ID order. Boards: the active BRD-NN items that name
     the idea, as comma-separated BRD-NN values (BRD-01, BRD-03: no backticks, no "and"), or none. Status is one of: designed (an active board names it), not a screen (the user said
     the idea needs no board), no board yet (no board names it and the user has not said not a screen; a
     marker in section 5 names the idea), withdrawn (an earlier version listed it as promoted and the BRN no
     longer does). -->

| Idea | Boards | Status |
|---|---|---|
| [[fill: IDEA-NN]] | [[fill: the active BRD-NN items that name it, comma-separated, or none]] | [[fill: designed, not a screen, no board yet or withdrawn]] |

## 4. Canvas

- Canvas: [[fill: the canvas URL the boards were imported from, or null]]
- Canvas version imported: [[fill: the version identifier the import reported, or the version the request states for a copy already in the folder, or null]]
- Date of the import: [[fill: the date of the import, or the date the request gives for the copy, or not known]]
[[fill: when the URL or the version is null, one line holding the marker NEEDS CLARIFICATION: canvas URL and version copied, written in square brackets as the other markers are; otherwise delete this line]]

The boards in boards_root were imported from this canvas by the ui skill, or placed there and recorded as they
were. A new import means a new run, which amends this document. The brief is not recorded here.

## 5. Open questions

- [[fill: the text of each marker this document holds outside a board's notes, one bullet each, or replace the list with the line None.]]

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | claude-code (session [[fill: the session ID]]) | [[fill: Created from BRN-NNN vN and the boards; N markers left, N being the count]] | all |
