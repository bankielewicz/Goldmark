---
id: CTX-004
type: context
title: ""              # e.g. "<project>: source tree"
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
                       # a root's own decision link goes on its item, never here
  - {id: ARCH-000, relation: constrains, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- context-specific ---
document: source-tree    # fixed: docs/specs/context/source-tree.md is always CTX-004
freshness_days: DAYS   # days; observed facts older than this are reported as findings, not errors
---

# CTX-004 — Source tree

<!-- Project context (DevForgeAI). The repository layout: where each component's code, tests,
     configuration and documentation live, and which folders are generated.

     BELONGS HERE: one root per folder that holds code, tests, configuration, documentation, fixtures or
     generated files, with the component it belongs to; and the layout conventions.
     DOES NOT: technology choices (tech-stack.md); how a layer is organized inside its code root (the
     layer document).

     ROOTS are items, one per folder and component: `path` is repository-relative, uses forward
     slashes and ends in "/" (never absolute, never a .. segment); `holds` is code, tests, config,
     docs, generated or fixtures; `component` is the ARCH component the folder belongs to. A folder
     several components share for code or tests appears once per component, with the same path;
     `component: null` means the folder belongs to no component (documentation, generated output,
     shared configuration). `basis` works as in tech-stack.md.
     READERS use active roots only. The story step reads the active `holds: code` roots to tell whether
     the project is new (none of them exists yet), and each component's active `holds: tests` roots to
     find its test folder: a component with none has no test folder yet. At least one active code root
     is required. In a new project the layout is proposed until the user confirms it, and the first
     story creates it. IDs SRC-01, SRC-02, … are never renumbered or reused. The items stay in this
     file, never in a detail file (a rule of these templates); if they alone would pass 500 lines, stop
     and ask the owner.

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
     work needs moves to a detail file, docs/specs/context/source-tree/<topic>.md (template detail.md),
     linked from this document with one line saying when to read it:
       - [<topic>](source-tree/<topic>.md): read when <condition>.
     A detail file never links another detail file.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## 1. Roots

```yaml items
roots:
  - id: SRC-01
    status: active
    path: "<folder>/"            # e.g. "src/cli/"
    holds: code                  # code | tests | config | docs | generated | fixtures
    component: "ARCH-000#CMP-01" # null: the folder belongs to no component
    basis: proposed              # decision | convention | observed | proposed
    notes: "[NEEDS CLARIFICATION: confirm this folder]"
  - id: SRC-02
    status: active
    path: "<folder>/"
    holds: tests
    component: "ARCH-000#CMP-01"
    basis: observed
    observed_in: "<path>"
    observed_on: YYYY-MM-DD
    notes: ""
```

## 2. Layout conventions

<!-- Naming of folders and files, and where new modules go. -->

- **Convention:** <…>

## 3. Generated files

<!-- Which roots are generated, how they are regenerated, and that they are never edited by hand. -->

- **Convention:** <…>

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
