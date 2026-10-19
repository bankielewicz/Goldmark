---
id: BRN-000
type: brainstorm
title: ""
status: draft          # draft | converged | archived
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
upstream: []           # usually empty; use relation: informed_by for research, interviews, tickets
supersedes: []
superseded_by: null
blocked_by: []
# --- brainstorm-specific ---
participants: []
sources: []            # raw inputs: interview notes, support tickets, analytics, URLs
---

# BRN-000 — <topic>

<!-- A brainstorm is divergent and non-binding. Nothing here is a commitment.
     Its job is to produce clearly identified problems (PRB-), ideas (IDEA-) and
     assumptions (ASM-) that a PRD can cite. Item blocks follow README §1.1:
     quote free text, never delete or renumber items. -->

## 1. Context

<!-- Why are we brainstorming now? What triggered it (incident, request, metric, opportunity)? -->

## 2. Problems

```yaml items
problems:
  - id: PRB-01
    status: active
    statement: "<one sentence, from the user's side>"
    who: "<persona>"
    evidence: "<source link or data>"
    severity: medium          # high | medium | low
```

## 3. Target users

<!-- Prose or a table: persona, job to be done, current workaround, pain. -->

## 4. Ideas

<!-- Capture every idea first with disposition: open. Fill value/effort/risk/score
     during evaluation (section 5), then set disposition at convergence (section 6).
     Do not record which PRD picked an idea up; that view is GENERATED. -->

```yaml items
ideas:
  - id: IDEA-01
    status: active
    idea: "<idea>"
    addresses:
      - PRB-01
    value: null               # e.g. "high", or a number, per the method in section 5
    effort: null
    risk: null
    score: null
    disposition: open         # open | promoted | parked | rejected
    reason: null              # why promoted / parked / rejected
```

## 5. Evaluation method

<!-- Name the lens used to fill value/effort/risk/score: impact/effort, RICE, MoSCoW,
     or dot-voting. Add any discussion that the numbers do not capture. -->

## 6. Convergence

<!-- Summary of what was promoted and why, in prose. Dispositions live on the ideas. -->

## 7. Assumptions and open questions

```yaml items
assumptions:
  - id: ASM-01
    status: active
    statement: "<we believe that…>"
    validation: "<experiment, interview or data that would confirm it>"
    state: open               # open | validated | invalidated
```

- [NEEDS CLARIFICATION: <question>]

## 8. Candidate success signals

<!-- Rough outcome indicators. The PRD turns these into success metrics (SM-).
     They are not acceptance criteria. -->

- <signal>

## Change Log

| Version | Date | Author | Change |
|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft |
