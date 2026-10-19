---
id: CTX-016
type: context
title: ""              # e.g. "<project>: data store"
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
document: datastore      # fixed: docs/specs/context/datastore.md is always CTX-016
freshness_days: DAYS   # days; observed facts older than this are reported as findings, not errors
---

# CTX-016 — Data store

<!-- Project context (DevForgeAI). Conventions for every non-relational store component (document, key-
     value, object, search or cache): engine and version, naming, schema changes and migrations,
     indexing, and consistency and transactions.

     BELONGS HERE: how each non-relational store is named, changed and used.
     DOES NOT: a stored structure's definition (that lives in its spec, as a `DM-NN` item written as
     JSON Schema); the store choice itself (its ADR, cited in tech-stack.md).

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
     work needs moves to a detail file, docs/specs/context/datastore/<topic>.md (template detail.md),
     linked from this document with one line saying when to read it:
       - [<topic>](datastore/<topic>.md): read when <condition>.
     A detail file never links another detail file.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## 1. Components covered

<!-- Each active component of each ARCH whose kinds include `data-store`: its qualified ID
     and name. -->

- ARCH-000#CMP-01 <name>

## 2. Engine and version

<!-- The store type and engine, citing its tech-stack.md item. -->

- **Convention:** <…>

## 3. Naming

<!-- Collections, keys, buckets and indexes. -->

- **Convention:** <…>

## 4. Schema changes and migrations

<!-- How stored structures change: the migration tool, if the store has one, the rules every migration
     follows, and how existing data is migrated. Where the store has no such mechanism, say so and why.
     -->

- **Convention:** <…>

## 5. Indexing

<!-- When and how indexes are added. -->

- **Convention:** <…>

## 6. Consistency and transactions

<!-- The consistency the code may rely on, the atomicity or transaction boundaries the store supports,
     and how conflicts are handled. Where the store supports no transactions, say so and how the code
     copes. -->

- **Convention:** <…>

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
