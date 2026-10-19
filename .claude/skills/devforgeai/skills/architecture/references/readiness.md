# Architectural questions, resolution and readiness

## Contents

- When to use this
- Which questions to record
- Which requirements a question cites
- Resolving a question
- What never resolves a question
- State changes when amending
- A mandated platform that changed
- Reuse, and deciding a question later
- The readiness rule
- Reporting readiness
- Worked example

## When to use this

Read this at SKILL.md step 4 (reuse or amend), step 6 (identify the questions), step 7 (resolve
them) and step 11 (compute readiness). The epic workflow writes epics only for requirements this
rule reports ready, so a question marked resolved by mistake lets two epics build conflicting
foundations.

## Which questions to record

Record a `DEC` for each architectural question that **separate epics must share**, meaning two
epics built independently could answer it differently and not fit together:

| Kind | Example question |
|---|---|
| Component boundaries and responsibilities | "Is booking a separate service from scheduling, or one module?" |
| Data ownership | "Which component owns volunteer contact data?" |
| Major interactions and interfaces | "How do bookings reach the clinic calendar: synchronous API or queued events?" |
| Deployment | "Where does the system run, and as how many deployment units?" |
| How a quality requirement is met | "How are signed-in sessions revoked within the NFR-002 time limit?" |

- **Every `[NEEDS ADR: …]` marker in the PRD becomes a DEC**, with the marker's question and at
  least the requirements it names.
- **One question per DEC.** "Which identity provider?" and "How are sessions revoked?" are two DECs,
  even when both affect sign-in: one can be settled while the other stays open.
- **`blocking: true`** unless the user explicitly says the question doesn't block epic work.
- **Leave feature-level design to specs:** exact API fields, table columns, migrations, class
  structures, error codes and UI layout are never DECs.
- **Never turn a product question into a DEC.** A `null` priority or release, or a
  `[NEEDS CLARIFICATION]` marker in the PRD, belongs to the PRD owner. Mention it in the reply if it
  matters, and leave it alone.
- Where a component boundary is a choice rather than a given (from the PRD, policy, an accepted ADR
  or inspected code), the CMP items show the proposed shape, and a DEC records the choice until it
  is decided.

## Which requirements a question cites

A DEC's `upstream` lists **every requirement whose behavior or quality depends on the answer**, at
the PRD version examined:
- the requirements a `[NEEDS ADR]` marker names;
- for a quality question, the NFR itself **and** each FR whose behavior it governs. A session
  revocation NFR governs the sign-in FR that creates the sessions, so its DEC cites both;
- for a deployment or data-ownership question, each FR that runs in or uses that part.

Link form: `{id: PRD-NNN, item: FR-NNN, relation: informed_by, version: <PRD version>, hash: null}`.
Never cite a requirement a question doesn't affect: an over-cited DEC blocks epics for no reason.

## Resolving a question

A DEC is resolved by exactly one of these three means. Anything else leaves it `state: open` with
`resolved_by: []`.

| Means | When it applies | Written as |
|---|---|---|
| 1. Mandated platform | An approved, active `architecture.mandated_platforms` setting (after R2) whose capability answers **exactly this question** | `state: resolved`, `resolved_by: [POL-NNN#SET-NN]`. It is policy, not a user decision, so it needs no confirmation and writes no ADR, except for a DEC reopened because its platform changed ("A mandated platform that changed") |
| 2. Existing ADR | An ADR with `status: accepted` and no `superseded_by` that **the user confirms** answers exactly this question, in an answer or in the request by naming both ("ADR-004 settles the identity provider") | `resolved_by: [ADR-NNN]` |
| 3. The user's decision | The user explicitly picks one of the options you presented with their trade-offs | A new ADR with `status: accepted` and `approved_by` the user, then `resolved_by: [ADR-NNN]` |

- **Present options (means 3).** For each open DEC, offer 2 or 3 options, each with its trade-offs
  tied to the requirements and quality drivers it affects, with your recommendation first, marked
  "(Recommended)", plus **Decide later**. Batch up to 4 DECs per AskUserQuestion call.
- **Decide later** keeps the DEC open. Only when the user asks to record the deferred decision,
  write it as an ADR with `status: proposed`. A proposed ADR resolves nothing and is never in
  `resolved_by`.
- **A preference stated in the request** ("use Auth0", "reuse our current auth service") is not
  yet a decision. Record it in a new DEC's `notes` ("The request prefers …; not yet decided"); an
  existing DEC stays unchanged, so for one, say it in the reply and in an amendment's Change Log
  row, never in a review record's. Make it the recommended option when you ask, and keep the DEC
  open until the user picks it.
- **With no user** ("proceed without questions"), only means 1 can apply, and never to a DEC
  reopened because its platform changed. Every other DEC stays open, no ADR is written, and existing
  resolutions in an ARCH being amended stay as they are unless the next section changes them. An
  accepted ADR the request names as answering a question doesn't resolve it here: record it as for a
  preference ("The request names ADR-004 as the answer; not confirmed, no user present").

## What never resolves a question

- An accepted ADR that cites the same requirement but answers a different question. An ADR about
  logging that cites the sign-in FR resolves nothing about the identity provider.
- A mandated platform for a different capability, or for part of the question only. An identity
  platform mandate answers "which identity provider"; it doesn't answer "how are sessions revoked".
- A mandated platform that changed since the ARCH recorded it, until the user confirms the setting as
  it now stands ("A mandated platform that changed").
- Evidence of any classification (`observed`, `policy`, `decided`, `context`) on its own. Code that
  uses a library shows observed practice, not a decision.
- A proposed, rejected, deprecated or superseded ADR.
- Confirming the outcome (reuse, amend or create). Confirming the outcome accepts no decision.
- Resolving another DEC that cites the same requirement. A resolver answers only its own question.

## State changes when amending

When amending an ARCH:

1. **Check the resolver of every resolved DEC.**
   - An ADR now superseded, deprecated or rejected, or a policy setting no longer active, no longer
     applied, or whose platform changed (next section): set `state: open` and `resolved_by: []`, and
     record the change in the Change Log ("DEC-01 resolved → open: ADR-002 superseded by ADR-003").
     Record the old ADR as an EVD with `classification: context`.
   - An accepted successor that `supersedes` the old ADR resolves the DEC only if the user confirms
     it answers the same question (means 2). A successor on a different topic resolves nothing.
2. **Give each new or changed requirement that an existing question affects its own DEC.** The
   existing DEC can't cite it, since existing items stay unchanged. Add a new DEC that asks the same
   question for that requirement, cites it, and says in `notes` "Same question as DEC-02, for
   FR-004". It is resolved only by the three means: the original DEC's resolver counts for it only
   if the user confirms it (means 2) or it is a mandated platform. Otherwise the new requirement
   would look ready by omission.

A DEC's `state` and `resolved_by` are the only fields of an existing item an amendment may change
(output-rules.md, "Amending an ARCH").

## A mandated platform that changed

A DEC resolved by a mandated platform (`resolved_by: [POL-NNN#SET-NN]`) counts only while the setting
still mandates the platform, for the capability, that the ARCH recorded for it.
- **Same policy version first:** when the policy's `version` equals the version of the ARCH's link
  to the setting (on the CMP that provides the capability), the setting is unchanged; compare
  nothing.
- **The record** is the setting's entry in the last `Policy resolution:` line of the ARCH's Change
  Log: `architecture.mandated_platforms=<platform> for <capability> (POL-NNN#SET-NN)`.
- **Changed:** the setting's platform or capability now differs from the record. The DEC no longer
  counts: readiness reports it open, reuse isn't offered (next section), and an amendment reopens it
  (`state: open`, `resolved_by: []`), logged in the Change Log as
  `DEC-01 resolved → open: POL-001#SET-01 now mandates <platform now> (was <platform recorded>)`,
  quoting both platforms exactly as the setting and the record name them. Record the setting as it
  now stands as a new `policy` EVD. The CMP that carries the setting's link stays byte-identical, as
  every existing item does, and the amendment adds no CMP or link for the new platform: the new EVD
  and the run's resolution line record it.
- **Awaiting confirmation:** a DEC reopened this way is resolved again by that setting only when the
  user confirms that the setting as it now stands answers it, as in means 2. While that reopening is
  the DEC's latest transition in the Change Log, no run resolves it by that setting without the
  user's confirmation, and a run with no user leaves it open.
- **No platform recorded:** the entry names the setting but no platform
  (`architecture.mandated_platforms=POL-NNN#SET-NN`). Name the gap in the reply. With a user present,
  ask whether the setting mandated the same platform when the DEC was resolved: if yes, it still
  counts; if no or unknown, treat it as changed and write `(was not recorded)` in the reopen row.
  With no user, it still counts.

## Reuse, and deciding a question later

- **Reuse needs a link to this PRD.** Offer reuse only for an ARCH whose frontmatter already links
  this PRD and none of whose resolved DECs relies on a mandated platform that changed. An ARCH that
  covers the system only by its name, or relies on a changed platform, is amended (an amendment adds
  the PRD link, output-rules.md) or replaced by a new ARCH.
- **Name what reuse leaves uncited.** Reuse adds no DEC, so every active requirement that no active
  blocking DEC cites is reported ready. When proposing reuse, and in the report after it is
  confirmed, name each such requirement, marked "(no architectural question cites it)", so the user
  sees what reuse makes ready without an architectural question. Step 4 recommends amend when a new
  requirement is one an existing question affects; a reuse the user confirms anyway stands, with
  the marker.
- **Deciding an open question later is an amendment.** Recording a decision on an existing DEC (means
  2 or 3) changes its `state` and `resolved_by`, which only an amendment may do. With a user present
  and a blocking DEC open, recommend amend at step 4. If reuse was chosen and a DEC then changes, the
  outcome is amend: say why and ask again.
- **A deferred question with a proposed ADR:** the decision is a new accepted ADR that supersedes the
  proposed one (output-rules.md, "Recording a supersession").

## The readiness rule

A requirement R is **ready** for epic work when, for every DEC with `status: active` and
`blocking: true` whose `upstream` cites R:
- `state` is `resolved`, and
- every `resolved_by` entry is **either** an ADR whose file currently has `status: accepted` and
  `superseded_by: null`, **or** an active setting of an approved policy that step 1 applied and that
  still mandates the platform the ARCH recorded ("A mandated platform that changed").

Otherwise R is **blocked** by each DEC that fails. A requirement that no active blocking DEC cites
is ready: no shared question holds it back.

Check ADR files **at the time of writing** (step 11): read each ADR named in `resolved_by` and use
its current `status` and `superseded_by`, not what the ARCH or its evidence says. A DEC whose
resolving ADR has been superseded is reported open, until an accepted successor resolves it. Check
each mandated platform in `resolved_by` the same way, against the setting as it stands now.

## Reporting readiness

In the report block and the handoff:
- List **every active requirement** of the PRD (FR and NFR) exactly once, as ready or blocked.
- For each blocked requirement, name every DEC that blocks it, and only DECs that cite it:
  `FR-001 (DEC-01, DEC-02)`.
- A ready requirement that no active blocking DEC cites is marked "(no architectural question cites
  it)" after a reuse, and may be marked so otherwise.
- Name any resolver that no longer counts, and why: "DEC-01 is open again: ADR-002 was superseded
  by ADR-003".
- Never contradict the lists anywhere else in the reply, and never present readiness as validated
  when validation failed (ERR-05).

## Worked example

A PRD has FR-001 "users sign in", FR-002 "users book a slot", NFR-001 "an administrator can revoke a
user's sessions within 5 minutes" and `[NEEDS ADR: identity provider; affects FR-001]`. An approved
organization policy mandates an identity platform for "identity and authentication" (`POL-001#SET-01`).
An accepted ADR-004 about log retention cites FR-001.

| DEC | Question | Cites | Resolution |
|---|---|---|---|
| DEC-01 | Which identity provider handles sign-in? | FR-001 | `resolved_by: [POL-001#SET-01]` (means 1) |
| DEC-02 | How are sessions revoked within 5 minutes? | FR-001, NFR-001 | Open: the mandate doesn't answer it, and ADR-004 is about logging |
| DEC-03 | Where does booking data live, and which component owns it? | FR-002 | Open, with no user present |

Readiness: FR-001 blocked (DEC-02); FR-002 blocked (DEC-03); NFR-001 blocked (DEC-02). The outcome
stays `null` because nobody confirmed `create`; and using a mandated platform would never make it
`reuse`.
