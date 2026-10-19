---
id: ADR-000
type: adr
title: ""              # the decision, stated as a result, e.g. "Use signed tokens for magic links"
status: proposed       # proposed | accepted | rejected | deprecated | superseded
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
approved_by: ""        # decider
approved_on: null      # decision date
upstream:
  - {id: PRD-000, item: NFR-001, relation: informed_by, version: 1, hash: null}
supersedes: []         # e.g. [ADR-002]; the superseded ADR sets superseded_by
superseded_by: null
blocked_by: []
# --- adr-specific ---
consulted: []
informed: []
---

# ADR-000 — <decision>

<!-- Based on MADR (adr.github.io/madr). Once accepted, do not rewrite the
     substance. Record material changes in a new ADR that supersedes this one.
     Specs that must obey this decision cite it with relation: constrains. -->

## Context and problem statement

<!-- What forces are at play? What question needs answering? Cite PRD/NFR items. -->

## Decision drivers

- <driver, e.g. PRD-000#NFR-001 latency budget>

## Considered options

1. <option A>
2. <option B>

## Decision outcome

**Chosen option:** <option>, because <justification tied to the drivers>.

### Consequences

- Good: <consequence>
- Bad: <consequence>

### Confirmation

<!-- How we will check the decision was implemented as intended (review, fitness test, metric). -->

## Pros and cons of the options

### <option A>
- Good, because …
- Bad, because …

### <option B>
- Good, because …
- Bad, because …

## Status history

| Date | Status | Note |
|---|---|---|
| YYYY-MM-DD | proposed | |
