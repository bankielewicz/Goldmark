# Mapping a BRN into a PRD

Read this at SKILL.md step 6. The BRN is read-only; everything here writes only into the PRD.

## Contents

- Summary table
- What the BRN offers
- Problems
- Promoted ideas into requirements
- Non-functional requirements
- Assumptions
- Candidate success signals into metrics
- Everything else
- Worked example

## Summary table

| BRN | PRD | Link |
|---|---|---|
| `problems` (PRB) addressed by a promoted idea this PRD drafts | Section 2 prose | Frontmatter `upstream`: `{id: BRN-NNN, item: PRB-NN, relation: derives, version: <BRN version>, hash: null}` |
| promoted `ideas` (IDEA) that no PRD cites yet | One or more `functional_requirements`; every FR derives from one | Item `upstream`: `{id: BRN-NNN, item: IDEA-NN, relation: derives, version: <BRN version>, hash: null}` |
| `assumptions` (ASM) | `assumptions` | Item `upstream`: `{id: BRN-NNN, item: ASM-NN, relation: derives, version: <BRN version>, hash: null}` |
| Candidate success signals (section 8 prose) | `success_metrics` | `derives` the promoted IDEA it measures; otherwise no link |
| Open, parked and rejected ideas | Nothing | Never cited by ID or link |

`version` in every BRN link is the BRN's frontmatter `version`.

## What the BRN offers

- Frontmatter: `status` (must be `converged`, or the user confirmed continuing), `owner`, `version`.
- Section 1 (context) and section 3 (target users): prose for PRD sections 2 and 3.
- `problems`, `ideas`, `assumptions`: item blocks.
- Section 6 (convergence): why ideas were promoted, parked or rejected.
- Section 8: candidate success signals, as prose bullets.

## Problems

- Describe each problem that a promoted idea this PRD drafts `addresses` in section 2 prose, from the affected
  user's side. Mention it by its qualified reference in plain text, for example `(BRN-001#PRB-01)`.
- Link each of those problems once in frontmatter `upstream` with `relation: derives`.
- A problem that no promoted idea addresses isn't linked. The PRD doesn't set out to solve it.

## Promoted ideas into requirements

- Draft only promoted ideas that no PRD cites yet. Leave out one that a PRD already cites, and name it
  in the reply with the item that cites it (SKILL.md step 3).
- Each promoted idea becomes **one or more** FRs. Split an idea when it holds separate capabilities
  that can be prioritized separately ("book, move and cancel" gives "book" plus "move or cancel").
  For a `prototype`, keep one FR per idea (capability level); when round 1 sets `prototype` after
  drafting, merge the draft back to one FR per idea before round 3.
- Every statement starts "The system shall" and states one testable capability, *what*, not *how*.
- Each FR carries an item `upstream` link to its idea, so every FR derives from a promoted idea. An
  FR serving two promoted ideas carries two links.
- `priority` and `release` stay `null` until the user decides (interview round 3). `notes: null`
  unless there is something to note, such as "The user chose to decide priority and release later."
- Number FRs `FR-001`, `FR-002`, … in the order of the ideas.

## Non-functional requirements

NFRs come mostly from the user (the request or the interview), from policy and from architecture
context, not from the BRN. **An NFR cites its actual source, and only that:**
- a BRN problem or promoted idea that itself states the requirement (`derives`);
- a mandated-platform policy setting (item `constrains` link, policy.md);
- an accepted ADR or another PRD's NFR it comes from (frontmatter `constrains` link, SKILL.md steps 5
  and 6).

An NFR the user stated, including a constraint such as "it must run on AWS", has **no** upstream
link. Never add a brainstorm link to a requirement the user stated or policy added, and never link
an NFR to an idea just because the idea is the capability the NFR constrains: that claims the
brainstorm decided something it didn't.

## Assumptions

- Carry each BRN assumption over as a PRD assumption with the same statement and validation, and
  `state` unchanged. Link it with `relation: derives`.
- Add new assumptions the PRD depends on without an upstream link.
- Number PRD assumptions from `ASM-01` in the PRD; the BRN's numbers don't carry over.

## Candidate success signals into metrics

- Each signal becomes one `SM-NN` with `metric` stated as something measurable ("Share of
  appointments booked online").
- If the signal measures a drafted idea's effect, link it: `derives` that IDEA. Otherwise leave out
  `upstream`.
- Linked or not, `baseline`, `target` and `measured_by` come from the BRN's evidence or the user.
  Otherwise they are `[NEEDS CLARIFICATION: …]` markers.
- A signal that only measures a parked or rejected idea, or a promoted idea left out because a PRD
  already cites it, is dropped.
- A metric the user states with "no target yet" is kept, with
  `target: "[NEEDS CLARIFICATION: target for <metric>]"` (interview.md, "Recording quality answers").

## Everything else

- **Goals** (section 4): the outcomes the promoted ideas aim at, stated as outcomes.
- **Non-goals** (section 4): the user's stated non-goals. You may list what the BRN parked or rejected
  as non-goals, described in words, **without** their IDs ("A native mobile app (rejected in the
  brainstorm)").
- **Users** (section 3): from the BRN's target users and problems' `who`.
- **Summary** (section 1): two or three sentences, written last.
- **User experience, rollout** (sections 8 and 11): from the request or answers; otherwise a
  `[NEEDS CLARIFICATION: …]` marker.

## Worked example

BRN-001 (version 1) promotes IDEA-01 "Patients book, move and cancel their own appointments online"
(addresses PRB-01) and IDEA-02 "Text-message reminders the day before" (addresses PRB-02). It parks
IDEA-03 and rejects IDEA-04.

```yaml items
functional_requirements:
  - id: FR-001
    status: active
    statement: "The system shall let a patient book an available appointment online."
    priority: null
    release: null
    notes: null
    upstream:
      - {id: BRN-001, item: IDEA-01, relation: derives, version: 1, hash: null}
  - id: FR-002
    status: active
    statement: "The system shall let a patient move or cancel their own appointment online."
    priority: null
    release: null
    notes: null
    upstream:
      - {id: BRN-001, item: IDEA-01, relation: derives, version: 1, hash: null}
  - id: FR-003
    status: active
    statement: "The system shall send a text reminder the day before each appointment."
    priority: null
    release: null
    notes: null
    upstream:
      - {id: BRN-001, item: IDEA-02, relation: derives, version: 1, hash: null}
```

The frontmatter links PRB-01 and PRB-02. IDEA-03 and IDEA-04 appear nowhere by ID.
