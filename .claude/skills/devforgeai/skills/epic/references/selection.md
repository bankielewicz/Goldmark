# The current ARCH, readiness and selection

## Contents

- When to use this
- Finding the current ARCH
- The readiness rule
- Matching `[NEEDS ADR]` markers
- The bounded policy check
- Covered requirements
- The selection rule
- Left-out rows
- Worked example

## When to use this

Read this at SKILL.md step 3 (find the ARCH), step 4 (compute readiness) and step 5 (select).

## Finding the current ARCH

Read the frontmatter of every `docs/specs/arch/ARCH-*.md`. Ignore an ARCH whose `status` is `superseded` or
`deprecated`. An active ARCH **cites this PRD** when its frontmatter `upstream` has a link whose `id` is
this PRD's ID (with no `item`). Links on items inside the ARCH don't count. An ARCH can cite several
PRDs.

| What you find | Result |
|---|---|
| No active ARCH cites the PRD, or the folder doesn't exist | **ERR-02.** Write nothing. Readiness comes from the architecture description, so tell the user to run `/devforgeai:architecture PRD-NNN` first |
| One active ARCH cites it, and that link's `version` equals the PRD's `version` | It is the **current ARCH**. Use it |
| One active ARCH cites it, and that link's `version` differs from the PRD's `version` (usually older; newer only after a hand edit) | **ERR-03.** Write nothing. Name both versions ("ARCH-001 was last reviewed against PRD-001 v1; the PRD is now v2") and tell the user to review the architecture against this PRD version with `/devforgeai:architecture PRD-NNN`. Confirming reuse there records the review; the architecture changes only if the review finds it must |
| Several active ARCHs cite it | **ERR-04.** List each with its `system` and the version of its PRD link, and ask which one to use. Never pick one silently. With no one to answer, write nothing |

A version mismatch means a review is due, not that the architecture is wrong. Never treat an ARCH
reviewed against an older PRD version as current, and never treat "no ARCH" as "nothing blocks".

## The readiness rule

Apply it to every active FR and NFR of the PRD, from the current ARCH's `decisions` block and the ADR and
policy files as they are **now**. A DEC **cites** R when its `upstream` has a link whose `id` is this
PRD's ID and whose `item` is R. A link to another PRD's item with the same number is not R: an ARCH can
cover several PRDs, so always match the PRD ID and the item together.

Read each DEC's `state` before its resolvers: an `open` DEC blocks R whatever its `resolved_by` holds.
A requirement R is **ready** when, for every DEC with `status: active` and `blocking: true` that cites R:
- `state` is `resolved`, and
- every `resolved_by` entry counts:
  - `ADR-NNN`: `docs/specs/adr/ADR-NNN.md` exists, and its frontmatter has `status: accepted` and
    `superseded_by: null`. Read the file; never trust what the ARCH, its evidence or its notes say
    about the ADR.
  - `POL-NNN#SET-NN`: it passes the bounded policy check below.

In addition, every `[NEEDS ADR]` marker that names R must have its own matching DEC (next section).

The outcome for R is one of:
- **ready**: every DEC that cites R passes, and every marker naming R is matched.
- **blocked**, naming each cause:
  - a DEC whose `state` is `open`: `DEC-01 (open)`;
  - a DEC resolved by an ADR that no longer counts: `DEC-03 (ADR-002 superseded by ADR-003)`, or
    `(ADR-007 is proposed)`, `(… rejected)`, `(… deprecated)`;
  - a DEC resolved by a mandated platform that changed since the ARCH applied it (bounded policy check,
    check 3): `DEC-07 (POL-001#SET-01 now mandates "<platform> for <capability>"; ARCH-001 recorded
    "<platform> for <capability>")`;
  - a marker with no matching DEC: `[NEEDS ADR] marker without a matching question ("<the marker's
    decision>")`;
  - a marker whose question a DEC asks without citing every requirement the marker names, so the marker
    is still unmatched: `[NEEDS ADR] marker: DEC-04 asks "<the marker's decision>" but doesn't cite
    FR-002`. Every requirement the marker names gets this reason.
- **unknown**, naming the input: readiness can't be established, so R is never asserted ready or blocked
  on that point. Causes:
  - a resolving ADR's file is missing (`ADR-004 not found`) or unreadable;
  - a `resolved_by` entry in no recognizable form;
  - a DEC with `state: resolved` and an empty `resolved_by` (`DEC-05 is resolved by nothing`);
  - a policy resolver that fails the bounded check for any other reason (next sections).

A DEC blocks **only** the requirements it cites. A DEC with `blocking: false` or `status: deprecated`
blocks nothing. A requirement no DEC cites and no marker names is ready. If one DEC blocks R and another
is unknown, R has both reasons.

## Matching `[NEEDS ADR]` markers

Find every `[NEEDS ADR: <decision>; affects FR-NNN, …]` marker in the PRD. For each one, look for its
**matching DEC**: an active DEC whose `question` asks the marker's decision, and whose `upstream` cites
every requirement the marker names. "Which identity provider handles sign-in?" matches
`[NEEDS ADR: identity provider for sign-in; affects FR-001]`.

- A matching DEC is then judged like any other DEC: open, resolved by a resolver that counts, or not. A
  resolver that counts answers the marker, whether it is an accepted ADR or a policy setting that passes
  the bounded check: a mandated platform settles the decision as an ADR does (SPEC-003 BEH-07).
- **No DEC clearly asks the marker's decision:** every requirement the marker names is **blocked**
  ("marker without a matching question"), even when other DECs citing that requirement are resolved.
- **A DEC asks the marker's decision but doesn't cite every requirement the marker names:** the marker
  is still unmatched, and every requirement it names is blocked with the reason that says which
  requirement the DEC doesn't cite (readiness rule, above).
- A resolved DEC about a **different** decision never answers the marker, even when it cites the same
  requirement, whether an ADR or a policy setting resolves it: an audit-storage DEC doesn't answer a
  payment-provider marker, and neither does a DEC about payment receipts resolved by a mandated mail
  platform. When in doubt, treat the marker as unmatched; a wrong "ready" is worse than a wrong "blocked".

## The bounded policy check

A `POL-NNN#SET-NN` resolver counts only when all four hold. This is a check, not policy resolution: never
apply defaults or local preferences, and never resolve anything else from policy.

1. **Approved.** `docs/specs/policy/POL-NNN.md` exists and has `status: approved`.
2. **Active mandate.** Setting `SET-NN` in its `settings` block has `status: active` and
   `key: architecture.mandated_platforms`, the only kind of setting that resolves a DEC.
3. **Still the mandate the ARCH recorded.** The ARCH has a link to the setting: `id: POL-NNN` and
   `item: SET-NN`, usually on the component that provides the capability. With no link, check 3 fails.
   - **Same version.** The link's `version` equals the policy document's `version`: check 3 passes.
     Read nothing more. A policy version bump alone never blocks R.
   - **Different version.** Find the ARCH's **current resolution line**: the `Policy resolution:` line
     in the *last* Change Log row that has one. Create, amend and reuse-review rows have one; approval
     rows don't. Never fall back to an older row. Build the entry from the setting's current `value`:
     `architecture.mandated_platforms=<platform> for <capability> (POL-NNN#SET-NN)`. Compare it with the
     line as exact text, after trimming spaces around the platform and the capability. The architecture
     step copies both verbatim, so ignore nothing else. Exactly one outcome applies:
     - **Unchanged:** the line contains the entry. Check 3 passes. Note the newer version in the reply's
       notes (SKILL.md step 9, item 2).
     - **Changed:** the line names `POL-NNN#SET-NN` in that form with a different platform or capability.
       The mandate changed since the ARCH applied it, so the DEC's question is open again: R is
       **blocked** by that DEC, naming the change, and the next action hands back to the architecture step.
     - **No platform recorded:** the line names the setting only in the older form
       `architecture.mandated_platforms=POL-NNN#SET-NN`, with no platform or capability. Only that exact
       form qualifies. It is an evidence gap: name it. With a user present, ask whether the setting
       mandated the platform it now names, for its capability, when the question was resolved (SKILL.md,
       "Decisions that belong to the user"): yes, check 3 passes; no or don't know, treat it as changed
       (blocked). With no user, check 3 passes, and the reply's notes name the gap.
     - **No record:** the line doesn't name `POL-NNN#SET-NN` at all, or no Change Log row has a
       `Policy resolution:` line. A missing record is never the older form: check 3 fails.
4. **No contested mandate.** No other approved document in `docs/specs/policy/` has an active
   `architecture.mandated_platforms` setting for the **same capability**. Compare capabilities as the
   policy script does: trim outer spaces and ignore case, but inner spaces count; no other
   normalization. Settings for different capabilities never conflict, so an organization policy and a
   project policy can each mandate platforms. The one exception for the same capability is a permitted
   override: the resolver is the project policy's setting (frontmatter `scope: project`), and the
   organization policy's setting (`scope: organization`) has `overridable_by` including `project`. An
   organization setting that such an override replaces fails this check.

A changed mandate (check 3) makes R **blocked** by its DEC, for example
`blocked by DEC-07 (POL-001#SET-01 now mandates "<platform> for <capability>"; ARCH-001 recorded "<platform> for <capability>")`,
or, for an older-form record the user didn't confirm (no or don't know),
`blocked by DEC-07 (POL-001#SET-01 now mandates "<platform> for <capability>"; ARCH-001 recorded no platform, and the user didn't confirm it)`.
Any other failed check makes R **unknown**, naming the resolver and the condition that failed:
- `POL-001#SET-01 fails the policy check: setting deprecated (check 2)`
- `POL-001#SET-01 fails the policy check: ARCH-001 has no link to it (check 3)`
- `POL-001#SET-01 fails the policy check: ARCH-001's latest resolution doesn't record it (check 3)`
- `POL-001#SET-01 fails the policy check: POL-002#SET-01 also mandates "<capability>" (check 4)`

Both hand back to the architecture step, `/devforgeai:architecture PRD-NNN`, which re-resolves policy and,
for a changed mandate, reopens the question. A resolver that passes at a newer policy version counts; say
so in the reply's notes (SKILL.md step 9, item 2), for example
`POL-001#SET-01 still counts: POL-001 is now v2, ARCH-001 linked v1, and the setting is unchanged`, or,
for the older form when nobody could be asked,
`POL-001#SET-01 counted: ARCH-001's resolution line names it only as architecture.mandated_platforms=POL-001#SET-01, with no platform recorded, and nobody could be asked`.

## Covered requirements

Read the frontmatter of every `docs/specs/epic/EPIC-*.md`. An existing epic is **active** unless its
`status` is `superseded` or `deprecated`. An FR is **covered** when an active existing epic's frontmatter
`upstream` has a link with `relation: refines`, `id` equal to this PRD's ID and `item` equal to the FR's
ID, at any `version`.

- An FR of another PRD with the same number is not this FR: both the PRD ID and the item must match.
- **NFRs are never covered.** An existing epic that refines an eligible NFR doesn't stop it being
  attached to the new epics it constrains (SKILL.md step 6). If no new epic attaches it, its row reads
  `already refined by EPIC-NNN` (Left-out rows).
- **The NFR review signal.** An NFR that is blocked or unknown, and that an active existing epic refines
  (this PRD's ID and the item), lists `refined by EPIC-NNN` for every such epic. When it is the first
  listed reason (no `wont`, `later` or `undecided` applies), the next action is
  `Review EPIC-NNN's work before continuing.`: the epic's work rests on a question that is no longer
  settled. It is still not covered.
- A covered FR that is also blocked or unknown now gets both reasons: the existing epic's work rests on
  a question that is no longer settled.
- Existing epics are read-only: never modify, renumber, supersede or duplicate one.

## The selection rule

A requirement R of the PRD (an FR or an NFR) is **eligible** when all hold:
1. `status: active`;
2. R is ready;
3. `release: current`;
4. `priority` is `must`, `should` or `could`;
5. for an FR only: R is not covered.

Priority orders the epics and never selects on its own, apart from `wont`. A `null` release, or a `null`
priority with `release: current`, is **undecided**: it belongs to the PRD owner, never to you. A `later`
requirement always has `priority: null`, because the prd skill writes later requirements that way. That
null is not undecided.

## Left-out rows

Every requirement that is in no new epic gets **one row** in the reply. That is every requirement that
isn't eligible, plus any eligible NFR that no new epic attaches. A row gives **every reason that
applies**, in the order below, and **one next action: the first listed reason's**. Several reasons never
mean several questions, and a row never asks the user anything.

| Order | Reason, as written in the row | When | Next action |
|---|---|---|---|
| 1 | `deprecated` | `status` is not `active` | `No action.` |
| 2 | `won't have (priority wont)` | `priority: wont` | `No action for this release.` |
| 3 | `later (release later)` | `release: later`. Never add `undecided` for its null priority | `No action for the current release.` |
| 4 | `undecided (priority null)`, `(release null)` or `(priority and release null)` | `release` is `null`, or `priority` is `null` with `release: current` | `The PRD owner decides.` |
| 5 | `covered by EPIC-NNN` (an FR) or `refined by EPIC-NNN` (an NFR) | An FR: an active existing epic refines it. An NFR: it is blocked or unknown, and an active existing epic refines it. Name every such epic | `No action.` for an FR that is neither blocked nor unknown; otherwise `Review EPIC-NNN's work before continuing.` |
| 6 | `blocked by DEC-NN (<cause>)`, or `blocked: [NEEDS ADR] marker …` (readiness rule), including a changed mandate | Not ready | `Resolve it with /devforgeai:architecture PRD-NNN.` |
| 7 | `unknown: <input and failed condition>` | Readiness can't be established | `Fix <the input>, or review the architecture with /devforgeai:architecture PRD-NNN, then run again.` |
| — | `already refined by EPIC-NNN` | An eligible NFR that no new epic attaches. An active existing epic always refines it, because otherwise a standalone NFR epic is written | `No action.` |

Compute readiness for a requirement left out for another reason too (`later`, `wont`, `undecided`), and
list its blocking DECs: they tell the PRD owner what else waits. A deprecated requirement gets only
`deprecated`. An NFR is never `covered`. A blocked or unknown NFR that an active existing epic refines
gets `refined by EPIC-NNN` among its reasons, even when an earlier reason (`later`, `wont`, `undecided`)
gives the action. Only an eligible NFR that no new epic attaches gets `already refined by EPIC-NNN`; any
other ineligible NFR gets only its own reasons, even when an existing epic refines it.

Row format, one line each, the requirement ID first:

```
- FR-005: later (release later); blocked by DEC-06 (open). No action for the current release.
- FR-008: covered by EPIC-002; blocked by DEC-03 (ADR-002 superseded by ADR-003). Review EPIC-002's work before continuing.
- FR-009: blocked by DEC-06 (POL-002#SET-01 now mandates "Hosted SMS gateway for appointment reminders"; ARCH-002 recorded "On-premises SMS modem for appointment reminders"). Resolve it with /devforgeai:architecture PRD-003.
- NFR-001: already refined by EPIC-001. No action.
- NFR-002: refined by EPIC-003; blocked by DEC-05 (open). Review EPIC-003's work before continuing.
- NFR-004: later (release later); refined by EPIC-003; blocked by DEC-05 (open). No action for the current release.
```

## Worked example

PRD-003 (version 4) has FR-001 to FR-006 and NFR-001, and
`[NEEDS ADR: calendar sync with the practice system; affects FR-002]`. ARCH-002 cites PRD-003 at version 4.
EPIC-004 (approved) refines FR-001.

| DEC | Question | Cites | State | Resolver now |
|---|---|---|---|---|
| DEC-01 | Which component owns appointment data? | FR-001, FR-002, NFR-001 | resolved | ADR-005, accepted |
| DEC-02 | How are SMS reminders sent? | FR-003 | resolved | ADR-006, superseded by ADR-009 |
| DEC-03 | Which video service hosts remote visits? | FR-004 | open | none |

FR-004 has `release: later` (with `priority: null`, as the prd skill writes it), FR-005 has
`priority: null` with `release: current`, and FR-006 (`could`, `current`) has no DEC.
Every other requirement is `must`, `current`.

```
- FR-001: covered by EPIC-004. No action.
- FR-002: blocked: [NEEDS ADR] marker without a matching question ("calendar sync with the practice system"). Resolve it with /devforgeai:architecture PRD-003.
- FR-003: blocked by DEC-02 (ADR-006 superseded by ADR-009). Resolve it with /devforgeai:architecture PRD-003.
- FR-004: later (release later); blocked by DEC-03 (open). No action for the current release.
- FR-005: undecided (priority null). The PRD owner decides.
```

FR-002 is blocked although DEC-01, which cites it, is resolved: DEC-01 asks about data ownership, not
calendar sync. The eligible requirements are FR-006 and NFR-001. NFRs are never covered, so even if
EPIC-004 also refined NFR-001, your proposal would still attach it to the new epic it constrains.
