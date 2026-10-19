---
id: PRD-000
type: prd
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
upstream:              # document-level: the brainstorm problems this PRD addresses
  - {id: BRN-000, item: PRB-01, relation: derives, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- prd-specific ---
target_release: ""     # name of the release "current" items belong to, e.g. "MVP"
stage: null            # prototype | mvp | evolution (scope maturity); null until the user decides
operating_context: null  # local | internal | pilot | production (who uses it, with what data); null until decided
stakeholders: []
---

# PRD-000 — <product or release name>

<!-- The PRD states WHAT and WHY, not HOW. Every requirement is an item with its own ID.
     Every functional requirement derives from a promoted brainstorm idea. A non-functional
     requirement cites its actual source: a brainstorm item only when that item states the
     requirement, otherwise the policy setting, ADR or PRD it comes from, and no link when the
     user stated it. Design choices belong in a Spec or ADR. -->

## 1. Summary

<!-- Two or three sentences a stakeholder can read in 20 seconds. -->

## 2. Problem and opportunity

<!-- Prose. The problems themselves are linked in frontmatter `upstream`. -->

## 3. Users and personas

<!-- Prose or a table: persona, goal, context and frequency. -->

## 4. Goals and non-goals

**Goals**
- <outcome>

**Non-goals** (explicitly out of scope)
- <thing we will not do, and why>

## 5. Success metrics

<!-- Outcome measures, often observed after release. These are NOT story acceptance criteria. -->

```yaml items
success_metrics:
  - id: SM-01
    status: active
    metric: "<metric>"
    baseline: "<today's value>"
    target: "<target value>"
    measured_by: "<dashboard or query>"
    upstream:
      - {id: BRN-000, item: IDEA-01, relation: derives, version: 1, hash: null}
```

## 6. Functional requirements

<!-- One testable capability per item. Use "shall". Every item derives from a promoted idea
     (an upstream derives link to it).
     priority: must | should | could | wont (MoSCoW importance within its release).
     release: current (this PRD's target_release) | later (backlog).
     Both stay null until the user decides. A PRD can't be approved while any release, or the
     priority of any current-release item, is null. -->

```yaml items
functional_requirements:
  - id: FR-001
    status: active
    statement: "The system shall <capability>."
    priority: null            # must | should | could | wont
    release: null             # current | later
    notes: null
    upstream:
      - {id: BRN-000, item: IDEA-01, relation: derives, version: 1, hash: null}
```

## 7. Non-functional requirements

<!-- Make each one measurable. category: performance | security | privacy | accessibility |
     reliability | compliance | observability | usability | maintainability | constraint | other
     "constraint" records a fixed external condition (mandated platform, integration, data
     residency, existing system). It never records a design choice: those go in an ADR or spec.
     upstream: only the actual source (see the note at the top); none when the user stated it. -->

```yaml items
non_functional_requirements:
  - id: NFR-001
    status: active
    category: performance
    statement: "<p95 latency < N ms at M requests/s>"
    priority: null
    release: null
```

## 8. User experience

<!-- Key flows, wireframe links, content requirements. Link to designs; do not embed large ones. -->

## 9. Constraints and dependencies

<!-- Prose context only. Each constraint that a spec must obey is an NFR item with
     category: constraint (section 7), so specs can cite it. -->

## 10. Assumptions and risks

```yaml items
assumptions:
  - id: ASM-01
    status: active
    statement: "<assumption or risk>"
    validation: "<how we will confirm it or mitigate it>"
    state: open               # open | validated | invalidated
    upstream:                 # delete if the assumption is new in this PRD
      - {id: BRN-000, item: ASM-01, relation: derives, version: 1, hash: null}
```

## 11. Release and rollout

<!-- Phasing, feature flags, migration, launch criteria. -->

## 12. Open questions

- [NEEDS CLARIFICATION: <question>]

## 13. Epic map

<!-- GENERATED: epics whose upstream cites this PRD. Do not edit by hand. -->

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
