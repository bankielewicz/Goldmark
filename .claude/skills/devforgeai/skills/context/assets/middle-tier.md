---
id: CTX-012
type: context
title: ""              # e.g. "<project>: middle tier"
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
document: middle-tier    # fixed: docs/specs/context/middle-tier.md is always CTX-012
freshness_days: DAYS   # days; observed facts older than this are reported as findings, not errors
---

# CTX-012 — Middle tier

<!-- Project context (DevForgeAI). Conventions for every application-logic component: domain structure,
     boundaries within a component, orchestration, validation and transactions.

     BELONGS HERE: how business logic is organized and behaves.
     DOES NOT: a data store's own conventions (rdbms.md, datastore.md); an interface's contract (api.md,
     and each operation's spec).

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
     work needs moves to a detail file, docs/specs/context/middle-tier/<topic>.md (template detail.md),
     linked from this document with one line saying when to read it:
       - [<topic>](middle-tier/<topic>.md): read when <condition>.
     A detail file never links another detail file.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## 1. Components covered

<!-- Each active component of each ARCH whose kinds include `service`: its qualified ID
     and name. -->

- ARCH-000#CMP-01 <name>

## 2. Domain structure

<!-- How the domain is modelled and organized in the code. -->

- **Convention:** <…>

## 3. Boundaries within a component

<!-- How a component is divided internally, and what may call what. -->

- **Convention:** <…>

## 4. Orchestration

<!-- How multi-step work is coordinated. -->

- **Convention:** <…>

## 5. Validation

<!-- Where input is validated, and how failures are reported. -->

- **Convention:** <…>

## 6. Transactions

<!-- Transaction boundaries and how partial failure is handled. -->

- **Convention:** <…>

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
