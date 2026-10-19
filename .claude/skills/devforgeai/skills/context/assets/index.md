---
id: CTX-001
type: context
title: ""              # e.g. "<project>: project context index"
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
  - {id: ARCH-000, relation: constrains, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- context-specific ---
document: index          # fixed: docs/specs/context/index.md is always CTX-001
---

# CTX-001 — Project context index

<!-- Project context (DevForgeAI). The entry point to the project context documents. A skill reads this
     file first, then opens only the documents the work needs.

     BELONGS HERE: one row per context document that exists, and the loading rule. Nothing else.
     DOES NOT: any decision, convention or fact (those live in the documents listed); detail files (each
     is listed in its parent document, with its "read when" line).

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
     SIZE. At most 100 lines.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## Documents

<!-- One row per context document that exists in docs/specs/context/. Version: its current version.
     Kinds: "all" for the core documents (architecture, tech-stack, source-tree, testing); for a layer
     document, the component kinds DevForgeAI maps to it: front-end.md and ui-mockups.md cover
     user-interface, middle-tier.md service, back-end.md platform, api.md api, rdbms.md relational-store,
     datastore.md data-store. -->

| File | ID | Version | Kinds | Purpose |
|---|---|---|---|---|
| [architecture.md](architecture.md) | CTX-002 | 1 | all | Components and the conventions that cut across layers |
| [tech-stack.md](tech-stack.md) | CTX-003 | 1 | all | Technologies, their allowed versions, and where each was chosen |
| [source-tree.md](source-tree.md) | CTX-004 | 1 | all | Where code, tests, configuration and documentation live |
| [testing.md](testing.md) | CTX-005 | 1 | all | How testing is done, and the testing policy in force |
| [<layer>.md](<layer>.md) | CTX-0NN | 1 | <kind> | <one-line purpose> |

## Loading

- **DevForgeAI rule — context loading:** read this index first. Then open only the documents
  for the kinds of the components the work touches, plus the core documents it needs, and a detail file
  only when its "read when" line in its parent applies. Never open every document by default.

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
