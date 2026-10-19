---
id: EPIC-000
type: epic
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
upstream:              # every PRD requirement in scope; note partial coverage
  - {id: PRD-000, item: FR-001,  relation: refines, version: 1, hash: null}
  - {id: PRD-000, item: NFR-001, relation: refines, version: 1, hash: null, note: "partial: <which part>"}
  - {id: PRD-000, item: SM-01,   relation: informed_by, version: 1, hash: null, note: "<how this epic moves the metric>"}
supersedes: []
superseded_by: null
blocked_by: []
# --- epic-specific ---
priority: null         # must | should | could
target_release: ""
---

# EPIC-000 — <capability name>

<!-- An epic is a slice of value too large for one story. It groups PRD requirements
     into a deliverable capability and defines when that capability is complete.
     It does not describe implementation. -->

## 1. Goal

<!-- One paragraph: what users can do when this epic is done that they cannot do today. -->

## 2. Business value

<!-- Prose. The success metrics this epic moves are linked in frontmatter `upstream`. -->

## 3. Scope

**In scope**
- <item>

**Out of scope**
- <item> (covered by <other epic> for an FR, refined by <other epic> for an NFR, or "not planned")

## 4. Done when

<!-- Cross-story criteria that can only be checked once several stories are complete.
     Broader than story AC. Every DW- item must be satisfied by at least one story AC
     (the checker reports DW items with no satisfying AC). -->

```yaml items
done_when:
  - id: DW-01
    status: active
    criterion: "<integrated outcome>"
    evidence_method: "<end-to-end test, demo or metric>"
```

## 5. Dependencies and risks

<!-- Prose: the ARCH decisions this epic relies on, the other epics it depends on, and the risks.
     Dependencies on other epics stay prose; frontmatter `blocked_by` stays []. -->

## 6. Technical notes (optional)

<!-- High-level constraints only. Detailed design goes in SPEC and ADR documents. -->

## 7. Story map

<!-- GENERATED from stories whose upstream cites this epic: story, title, status, DW satisfied.
     Do not edit by hand. -->

## 8. Open questions

- [NEEDS CLARIFICATION: <question>]

## Change Log

| Version | Date | Author | Change |
|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft |
