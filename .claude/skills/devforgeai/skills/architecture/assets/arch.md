---
id: ARCH-000
type: arch
title: ""
status: draft          # draft | in-review | approved | superseded | deprecated
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
upstream:              # the PRD this description was defined against, and applied policy settings
  - {id: PRD-000, relation: informed_by, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- arch-specific ---
system: ""             # the system or product this description covers
outcome: null          # reuse | amend | create for the PRD version examined; null until the user confirms
inspection_scope: []   # components or directories the user named for read-only inspection
---

# ARCH-000 — <system> architecture

<!-- An architecture description records the system-level decisions that separate epics must share:
     boundaries, responsibilities, data ownership, major interactions, deployment, and the quality
     requirements driving them. Exact API fields, migrations and class structures belong in specs.
     Significant choices are ADRs; this document shows how they fit together.
     Confirming the outcome (reuse / amend / create) never accepts the decisions inside it. -->

## 1. Context and scope

<!-- What the system is for, what's inside and outside it, and which PRD version this was defined against. -->

## 2. Quality drivers

<!-- Prose: the NFRs and constraints that shape the design. Components cite them in their upstream links. -->

## 3. Components

```mermaid
flowchart LR
    A[Component] --> B[Component]
```

```yaml items
components:
  - id: CMP-01
    status: active
    name: "<component>"
    kinds:                 # one or more: user-interface | service | platform | api | relational-store | data-store | external
      - "service"
    responsibility: "<what it is responsible for>"
    owns_data:
      - "<data it is the owner of>"
    interacts_with:
      - "<CMP-NN or external system>"
    deployment: "<deployment unit>"
    upstream:
      - {id: PRD-000, item: NFR-001, relation: informed_by, version: 1, hash: null}
```

## 4. Architectural questions

<!-- One item per question that separate epics must agree on. upstream lists the requirements the
     question affects, at the PRD version examined. A requirement is ready for epics only when every
     blocking question that cites it is resolved by an accepted, non-superseded ADR or an approved,
     active policy setting. Never mark a question resolved without an explicit decision. -->

```yaml items
decisions:
  - id: DEC-01
    status: active
    question: "<the architectural question>"
    blocking: true
    state: open               # open | resolved
    resolved_by: []           # ADR-NNN or POL-NNN#SET-NN when resolved
    notes: null
    upstream:
      - {id: PRD-000, item: FR-001, relation: informed_by, version: 1, hash: null}
```

## 5. Evidence inspected

<!-- Every project source consulted for architecture analysis, with the version and status examined.
     classification is independent of kind and keeps observed practice (code or configuration inspected
     in scope), approved policy, accepted decisions and context (the PRD, existing ARCHs, non-accepted
     ADRs, uncorroborated documentation) apart. Context establishes neither behavior nor a decision.
     Insufficient evidence is an explicit unknown, never a confident "reuse". -->

```yaml items
evidence:
  - id: EVD-01
    status: active
    source: "<path, ADR-NNN or POL-NNN#SET-NN>"
    kind: code                # code | document | adr | policy | prd
    finding: "<what it shows>"
    classification: observed  # observed | policy | decided | context
```

## 6. Deployment

<!-- Prose plus optional Mermaid: deployment units, environments, and what runs where. -->

## 7. Requirement changes proposed to the PRD owner

<!-- Findings that need a PRD amendment (infeasible, too costly, conflicting). This document never
     edits the PRD; the owner decides. -->

## 8. Open questions

- [NEEDS CLARIFICATION: <question>]

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
