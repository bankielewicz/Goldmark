---
id: CTX-003
type: context
title: ""              # e.g. "<project>: tech stack"
status: draft          # draft | approved | superseded | deprecated
version: 1
created: YYYY-MM-DD
updated: YYYY-MM-DD
owner: ""
authors: []
generated_by:
  tool: ""
  model: ""
  session: ""
reviewed_by: []
approved_by: ""
approved_on: null
upstream:              # constrains links, at their versions: each ARCH this document relies on,
                       # and the source of each Decision a prose statement cites
                       # (ADR-NNN; POL-NNN with item SET-NN; ARCH-NNN with item CMP-NN)
                       # a technology's own decision link goes on its item, never here
  - {id: ARCH-000, relation: constrains, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- context-specific ---
document: tech-stack     # fixed: docs/specs/context/tech-stack.md is always CTX-003
freshness_days: DAYS   # days; observed facts older than this are reported as findings, not errors
---

# CTX-003 — Tech stack

<!-- Project context (DevForgeAI). Each technology the project uses: its allowed version range, the
     components that use it, and where it was chosen.

     BELONGS HERE: languages, frameworks, libraries, databases, build and test tools, and hosted
     services the code depends on; an external system the project integrates with (a component of kind
     `external`), with the version of the interface used.
     DOES NOT: why a technology was chosen (its ADR or POL setting); how a layer uses it (the layer
     document); a library only one story needs (that story's spec).

     TECHNOLOGIES are items, one per technology, with a `basis` (decision, convention, observed,
     proposed) as the statement kinds below: a decision's `upstream` holds its `constrains` link (an
     ADR; a POL setting; an ARCH item); an observed item names `observed_in` (the file read) and
     `observed_on` (the date); a proposed item's `notes` carry [NEEDS CLARIFICATION: confirm …].
     VERSION RANGE: quoted, in the ecosystem's own syntax ("^1.0", ">=3.12,<3.13", "17.x"), or
     "unpinned" for any version. Only an active decision or convention item's range is an allowed
     range; on an observed or proposed item the range is information, not permission.
     IDs TEC-01, TEC-02, … are never renumbered or reused. The items stay in this file, never in a
     detail file, so the list reads from one place (a rule of these templates, not of ADR-004); if the
     items alone would pass 500 lines, stop and ask the owner.

     STATEMENTS. Write every narrative rule or fact as one bullet of one of these kinds:
       - **Decision** (<source>): …  decided by an accepted ADR (ADR-NNN), an approved POL setting
         (POL-NNN#SET-NN) or an ARCH item (ARCH-NNN#CMP-NN). The parentheses name the source, and
         frontmatter `upstream` holds one `constrains` link to that same document and item, at its
         version. Never restate a decision without citing it.
       - **Convention:** …  a rule the user confirmed while this document was written.
       - **Observed** (<path>, YYYY-MM-DD): …  found by read-only inspection and not yet confirmed.
         It becomes a convention only when the user confirms it; approving the document doesn't.
       - **Proposed:** … [NEEDS CLARIFICATION: confirm …]  suggested and not confirmed.
       - **DevForgeAI rule — <topic>:** …  a rule the DevForgeAI framework fixes. It
         carries no document ID and no link.
     TABLES. Each table this template defines says where its rows come from. A table of rules has a
     Basis column holding the same kinds: Convention, Observed (<path>, YYYY-MM-DD), Proposed with its
     marker, or a Decision's source. A row that restates another context document (a tests root from
     source-tree.md) is never firmer than its source there.
     IDS. A bare ADR-, POL-, ARCH-, STORY- or SPEC- ID always means this project's own document. Other
     context documents are named in prose ("tech-stack.md, TEC-02"), and a Markdown hyperlink to them
     is fine, but they never get an `upstream` link record.
     SIGNIFICANT CHOICES. A choice that is hard to reverse, or shared by several epics, and has no ADR
     is never written as a convention: write [NEEDS ADR: <decision>] and hand it back to Architecture
     Definition.
     ONE STORY. A statement that applies to one story belongs in that story's spec, not here.
     RETIRING. A statement is deleted from the text. An item is never deleted or renumbered: set its
     `status` to deprecated and say why in `notes` (for a rejected proposal: who rejected it, and when).
     MARKERS. The document can't be approved while any [NEEDS CLARIFICATION: …] or [NEEDS ADR: …]
     marker, or any active Proposed statement, remains anywhere in it: frontmatter, text, tables,
     items, or its detail files.
     SIZE. At most 500 lines. Past 100 lines, start with a "## Contents" list. Detail that only some
     work needs moves to a detail file, docs/specs/context/tech-stack/<topic>.md (template detail.md),
     linked from this document with one line saying when to read it:
       - [<topic>](tech-stack/<topic>.md): read when <condition>.
     A detail file never links another detail file.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## 1. Technologies

```yaml items
technologies:
  - id: TEC-01
    status: active
    name: "<technology>"     # e.g. "PostgreSQL"
    version_range: "<range>" # e.g. "16.x"
    used_by:
      - "ARCH-000#CMP-01"
    basis: decision          # decision | convention | observed | proposed
    upstream:
      - {id: ADR-000, relation: constrains, version: 1, hash: null}
    notes: ""
  - id: TEC-02
    status: active
    name: "<technology>"
    version_range: "<range>"
    used_by:
      - "ARCH-000#CMP-01"
    basis: observed
    observed_in: "<path>"    # e.g. "services/api/pyproject.toml"
    observed_on: YYYY-MM-DD
    notes: ""
```

## 2. Upgrades

<!-- How version changes are handled. Keep the framework rule; add only what the user confirmed. -->

- **DevForgeAI rule — ambiguities log:** a version change to a technology listed
  here is recorded in the work item's ambiguities log, and the work continues, only when all of
  these hold: the item is an active decision or convention; the new version is within its
  `version_range` and isn't a new major version; the change is small and reversible; and it
  changes no required behaviour, scope, interface, permission or security, and no spec or
  acceptance-criterion obligation, policy value or approval. Otherwise it is asked about, and
  so is every new technology. Logging never authorizes contradicting a spec.
- **Convention:** <…>

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
