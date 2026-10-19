# Interview: question bank and batching

## Contents

- Rules for every call
- Before the rounds: the gates
- Round 1: framing
- Round 2: architecture context
- Round 3: requirements
- Round 4: quality and constraints
- Recording quality answers
- Round 5: success metrics
- Depth by stage
- Recording answers

## Rules for every call

- **Draft first.** Ask only about gaps in the draft (SKILL.md step 6). Skip every question that the
  BRN, the request or an earlier answer already settles, and say in one line what you skipped and why.
- **Batch.** At most **4 questions per call**, each with **2–4 options** (the host's limit:
  AskUserQuestion in Claude Code). The user
  can always answer in their own words. Group questions from the same round; fill a call with
  questions from the next round rather than send a half-empty call.
- **Budget.** At most `interview.max_calls` calls in total (policy step; default 8), counting gate
  questions. Stop asking when the budget is spent unless the user asks for more, and record every
  remaining gap as `[NEEDS CLARIFICATION: …]`. A gate is still asked after the budget is spent:
  nothing is written without its answer.
- **Too many choices.** A question with more choices than the host allows (4 options in
  AskUserQuestion) is split into several questions, for example the accepted ADRs in groups of
  four.
- **Suggest, never assume.** An option may carry your suggestion, marked "(suggested)". A
  suggestion is written only after the user picks it.
- **No AskUserQuestion?** Ask the same questions as a numbered plain-text list, then end your turn.
- **"Proceed without questions"** skips all rounds. It does not answer the gates.

## Before the rounds: the gates

These are asked when reached, even under "proceed without questions", and nothing is written
without an answer. A gate the request or an earlier answer settles explicitly ("write a new PRD for
BRN-002", "extend PRD-001", "it's a draft, continue anyway") is not asked again; say in the reply
where the answer came from. "Proceed without questions" settles no gate.
- which BRN, when no ID was given (list unprocessed BRNs with titles and uncited promoted ideas);
- continue from an unconverged BRN? (options: continue anyway; stop and converge the brainstorm first);
- new PRD or extend PRD-NNN? State your recommendation first, with reasons about scope, owner and
  release lifecycle. Options: new PRD (recommended, if so); extend PRD-NNN.

## Round 1: framing

| Question | Options | Skip when |
|---|---|---|
| Stage? | prototype (exploratory, may be discarded); mvp (smallest scope real users get value from, built upon); evolution (changes an established product) | The request or BRN states it |
| Operating context? Ask first when unknown; it decides the quality questions | local (developers only, synthetic data); internal (own organization, may touch real internal data); pilot (limited real external users or real customer data); production (generally available, real users and data) | The request or BRN states it |
| Name of the current release (`target_release`)? | "MVP"; "v1"; "Pilot" (suggestions) | The request names it |
| Primary users? | The personas the BRN names, plus "others" | The BRN's target users section names them |
| Non-goals beyond the parked and rejected ideas? | none; list them | The request states them |

## Round 2: architecture context

- Which accepted ADRs apply? List each accepted, non-superseded ADR with a one-line proposal
  (applies or not). This is multi-select.
- For each design preference the user stated ("microservices", "React"): is it a hard constraint?
  Options: hard constraint; preference only (goes to open questions as a future ADR).
- Any fixed external conditions? Options: mandated platform; required integration; data residency
  or regulation; existing system to keep.
- Any architecture question still open that affects requirements? Each answer becomes a
  `[NEEDS ADR: <decision>; affects FR-NNN, …]` marker.

## Round 3: requirements

One question per drafted requirement, FR or NFR, showing its statement:

| Option | Writes |
|---|---|
| must now | `priority: must`, `release: current` |
| should now | `priority: should`, `release: current` |
| later | `release: later`, `priority: null` |
| won't (this release) | `priority: wont`, `release: current` |

The user may also edit the statement, answer "could now" (`priority: could`, `release: current`), or
answer "decide later", which leaves both `null`. A requirement that should never be built is edited
or dropped, not marked `wont`.

## Round 4: quality and constraints

**Which categories must be asked** comes from the operating context: the quality floor in
`defaults.md`, plus the categories that applicable policy adds (policy.md R4). When the context is
unknown, use the production set. Ask one question per required category that the draft doesn't
already cover, then **always** one open question: "Any other quality need (usability,
maintainability, other)?"

| Category | Example options |
|---|---|
| constraint | mandated platform; required integration; data residency; none |
| security | sign-in required; role-based access; audit trail; none beyond the platform |
| privacy | personal data limited to its owner and staff; data retention limit; none |
| reliability | availability target (e.g. 99.5%); recovery time; best effort |
| observability | error alerting; usage dashboard; logs only |
| compliance | named regulation (e.g. GDPR, HIPAA, SOC 2); internal policy only; none |
| performance | response time target; throughput target; no target yet |
| accessibility | WCAG 2.2 AA; WCAG 2.2 A; no target yet |

Record each answer as the next section says. Each NFR this round writes then gets round 3's
question in a following call.

## Recording quality answers

Answers come from the interview or from the request itself ("security needs nothing beyond the
platform"). Besides stated requirements, keep four kinds of answer apart (explicit none, no target
yet, partial, no answer), for every required category:

| Answer | Example | Write |
|---|---|---|
| **Requirements** | "Staff sign in with two factors" | One NFR per requirement stated, with that `category`, measurable where possible |
| **Explicit none**: the user confirms the category needs nothing | "none"; "nothing beyond the platform" | No NFR. One sentence in section 7's prose, recording it as the user's answer: `Security: the user confirmed nothing is needed beyond the platform.` |
| **No target yet**: the user keeps a requirement or metric but has no number | "Pages should load fast, no target yet" | The NFR or metric, with the target written `[NEEDS CLARIFICATION: target for <item>]` (in an NFR's statement, or a metric's `target`) |
| **Partial**: some of the category is answered | Security: "sign-in required" | The NFRs it states, plus the category marker below for the rest, unless the user said the rest needs nothing (then the rest is an explicit none) |
| **No answer** | The category isn't mentioned, or the budget ran out | In section 12: `[NEEDS CLARIFICATION: <category> requirements for <context>]` |

`<context>` is the operating context, or `production` when it is unknown. For example:
`[NEEDS CLARIFICATION: accessibility requirements for production]`. Never write a placeholder NFR
for an unanswered category, and never turn "none" into a placeholder or into silence: the prose
sentence is the record.

**An explicit none never waives applicable policy.** A category that an applied `quality.required_categories`
setting requires (policy.md R4) still applies after an explicit none. Record the none in section 7's
prose, and keep a marker that names the setting:

`[NEEDS CLARIFICATION: compliance requirements for internal; required by POL-001#SET-01, the user answered none]`

A mandated platform (`architecture.mandated_platforms`) is likewise written as its constraint NFR
whatever the user answered. If the user answered the constraint category with none, record the none
and keep the marker for `constraint`, naming the platform's setting. The resolution line and links
follow policy.md as usual.

## Round 5: success metrics

For each drafted metric: baseline today, and target (with a time frame). Options may suggest values
from the BRN's evidence. Unanswered baselines or targets stay `[NEEDS CLARIFICATION: …]`. A metric
the user states or keeps with "no target yet" stays in `success_metrics`, with
`target: "[NEEDS CLARIFICATION: target for <metric>]"`.

## Depth by stage

| Stage | Requirements | Also ask |
|---|---|---|
| prototype | Capability level only: one requirement per promoted idea | Minimal rollout: who tries it, and how it is thrown away or kept |
| mvp | Confirm each current-release requirement (round 3 for every one) | Launch criteria and fallback |
| evolution | As mvp | Effects on existing behaviour and systems: what changes for current users, data migration, compatibility |
| unknown | As mvp | — |

## Recording answers

- Write only what the user answered or confirmed. An unanswered question leaves its field `null` or
  its gap marked. A partial answer settles only the part it answers.
- An answer that settles something drafted earlier replaces the draft; an answer that contradicts
  the BRN is recorded as the user said it, with a note in the item's `notes` (FRs) or in prose.
- If the user stops partway, ask whether to save a draft PRD with every undecided field `null`. A
  saved draft is validated and reported like any other write (SKILL.md steps 8 to 10).
