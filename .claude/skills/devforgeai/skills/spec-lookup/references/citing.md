# Citing what the specs say

How to read `find_spec.py`'s output, how to cite it, and how to hand a lookup to the plugin's lookup agent.

## Contents

- Reading the output
- Citation forms
- Queries
- The lookup agent's task message
- Examples

## Reading the output

One line per hit, then a coverage line:

```text
<path>:<line>  <DOC> v<version> <status>  [<ITEM> <item status>]  "<excerpt>"
```

- `<path>` starts `docs/specs/`, relative to the project root, so a reader can open it at `<line>`.
- `<DOC> v<version> <status>` comes from the document's frontmatter (`id`, `version`, `status`). A document whose
  `superseded_by` is set shows `<status>, superseded by <ID>`.
- A document with no frontmatter shows its path as its ID, `v?`, and the value of a `**Status:**` line in its first
  10 lines (up to the first full stop), else `unknown`.
- `<ITEM> <item status>` appears when the line is inside an item block (```` ```yaml items ````): on the item's own
  definition, and on any other line within that item.
- `<excerpt>`: for an item's definition, its `rule`, `obligation`, `statement` or `handling` text; otherwise the
  line's text. Cut at 100 characters, ending `…`.

For an item ID, its definitions come first, then every other line naming it. Otherwise hits are in path then line
order. At most 50 are printed, then `<n> more hits: narrow the query`.

The last line is the coverage line:

```text
<n> hits; searched <k> files under docs/specs/ (<folders>)[; skipped <m> unreadable: <paths>]
no match: "<query>" is in none of <k> files under docs/specs/ (<folders>)[; skipped …]
no docs/specs/ folder under <DIR>: nothing is specified yet
```

Exit status: 0 with a hit; 1 with no match or no `docs/specs/` folder; 2 when it can't run (the root isn't a folder,
or the query is empty).

## Citation forms

- An item: `docs/specs/spec/SPEC-012.md:484 (SPEC-012 v10, approved; BEH-18 active)`.
- A line that isn't an item: `docs/specs/adr/ADR-006.md:37 (ADR-006 v1, accepted)`.
- Not in force, cited with its status, never as decided:
  - a draft or in-review document: `(SPEC-002 v1, draft)`;
  - a deprecated item: `(SPEC-013 v9, approved; VER-27 deprecated)`;
  - a superseded document: `(SPEC-003 v3, superseded, superseded by SPEC-001)`.

A recorded decision is cited the same way: a Change Log row authored by the owner, an ADR's "Chosen option" line or
an accepted Status-history row is a line like any other.

## Queries

- **Document ID**, equal to a file's frontmatter `id`: `SPEC-012`, `ADR-006`. Every line naming it.
- **Qualified item ID**: `SPEC-012 BEH-18` or `SPEC-012#BEH-18`. The item's definition in that document, then every
  line naming it with its document. Prefer this form: item IDs repeat across documents (every spec has a BEH-01).
- **Bare item ID**: `BEH-18`. Every document's definition, then every line naming it.
- **Term**: any other text. Every line holding all its words, case-insensitively, in any order, as literal text (no
  regular expressions). Use two or three distinctive words.

For the second search after a miss (BEH-04), change the form: a synonym (`sign-in` for `login`), fewer words, the
bare item ID, or the document ID.

## The lookup agent's task message

During another DevForgeAI skill's workflow, the main conversation hands the lookup to `devforgeai:spec-lookup` with a
message like this, filling in the absolute paths (the running skill's base directory, then
`../spec-lookup/scripts/find_spec.py`):

```text
Script: /abs/path/to/devforgeai/skills/spec-lookup/scripts/find_spec.py
Project root: /abs/path/to/project
Queries:
SPEC-004 BEH-05
password reset
Read (optional): docs/specs/spec/SPEC-004.md:27
```

`Read` lines are optional: the agent returns the text of each named line after the query blocks.

The agent runs `python3 <script> --root <project root> "<query>"` once per query and replies with the output lines
exactly as printed, one fenced block per query. Wait for its report, then cite only its `path:line` lines.

## Examples

Covered, in the main conversation:

```text
$ python3 ${CLAUDE_SKILL_DIR}/scripts/find_spec.py "audit log"
docs/specs/spec/SPEC-007.md:58  SPEC-007 v2 approved  BEH-11 active  "Audit log entries are kept for 400 days, then deleted."
docs/specs/adr/ADR-003.md:22  ADR-003 v1 accepted  "Chosen option: an append-only audit log table in the main database."
2 hits; searched 9 files under docs/specs/ (adr, prd, spec)
```

Reply: "Two records cover it: docs/specs/spec/SPEC-007.md:58 (SPEC-007 v2, approved; BEH-11 active) keeps entries
for 400 days, and docs/specs/adr/ADR-003.md:22 (ADR-003 v1, accepted) puts them in an append-only table …"

Uncovered, with no user to ask:

```text
$ python3 ${CLAUDE_SKILL_DIR}/scripts/find_spec.py "push notifications"
no match: "push notifications" is in none of 9 files under docs/specs/ (adr, prd, spec)
$ python3 ${CLAUDE_SKILL_DIR}/scripts/find_spec.py "notification"
no match: "notification" is in none of 9 files under docs/specs/ (adr, prd, spec)
```

Reply: both coverage lines, then `[NEEDS CLARIFICATION: not in any spec: push notifications]`. Nothing is built, and
the reply doesn't say push notifications were never discussed: a search can miss.
