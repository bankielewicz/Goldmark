---
name: architecture
description: Performs DevForgeAI Architecture Definition for a PRD. It identifies the architectural questions separate epics must share, settles each only by an explicit decision, an accepted ADR or approved policy, and writes an architecture description (ARCH) with ADRs and a report of which requirements are ready for epic work. Use after a PRD is written, when deciding system architecture, components, data ownership or deployment, or when resolving NEEDS ADR markers.
argument-hint: "PRD-NNN"
metadata:
  devforgeai-id: "SKL-003"
  devforgeai-version: "9"
---

# Architecture

Perform the Architecture Definition step for one PRD. Identify the architectural questions that
separate epics must share, settle each one only by policy, an accepted ADR or the user's explicit
decision, and write `docs/specs/arch/ARCH-NNN.md` plus any ADRs. The epic workflow reads the ARCH
mechanically and writes epics only for requirements the readiness rule reports ready. A question
marked resolved without a real decision lets separate epics build conflicting foundations.

Three rules shape everything below:
- **Readiness is decision-specific.** A requirement is ready only when every blocking question that
  cites it is resolved. An ADR or policy setting resolves the question it answers, never "the
  requirement".
- **Decisions are accepted one by one.** Confirming the outcome (reuse, amend or create) accepts
  none of the decisions inside it.
- **Evidence is honest.** Inspection is bounded and recorded; observed practice isn't policy; missing
  evidence is stated as unknown.

## Inputs

- `$ARGUMENTS`: a PRD ID such as `PRD-002`, or empty. When it is empty, use a PRD ID the user
  stated in the conversation. Never accept a file path; if given one, ask for the ID.
- Read by contract: PRDs in `docs/specs/prd/` (read-only), ARCHs in `docs/specs/arch/`, ADRs in
  `docs/specs/adr/`, policy in `docs/specs/policy/` and `.claude/devforgeai.local.md`. Reach them by
  these paths; never list the working directory or the repository root to find them.
- Code and configuration: only inside the inspection scope the user names
  ([references/inspection.md](references/inspection.md)).
- Templates: `${CLAUDE_SKILL_DIR}/assets/arch.md` and `${CLAUDE_SKILL_DIR}/assets/adr.md`.

Use Bash only to run the policy validation script with `python3` (step 1) and the read-only commands
[inspection.md](references/inspection.md) allows. Never run a `devforgeai` command: that CLI doesn't
exist, and a program with that name on PATH can't be trusted.

## Decisions that belong to the user

The ARCH records decisions; it never makes them. These are the user's:

- **Which PRD**, when no ID was given.
- **Which ARCH**: reuse or amend an existing one that covers the system, or create another.
- **The inspection scope**, and any read outside it.
- **The answer to each architectural question**: an option you presented, or confirming that an
  existing accepted ADR answers it. Also whether a question is non-blocking.
- **The outcome**: reuse, amend or create.
- **Superseding an accepted ADR.**
- **Saving a draft** when the user stops mid-session.
- **Changes to the PRD**: you only propose them; the PRD owner decides.

Write a decision only when the user made it explicitly, in an answer or in the request. The request
can answer only these:
- which PRD;
- which ARCH, and the direction of the outcome, only by naming it for that ARCH ("amend ARCH-001",
  "reuse ARCH-001; I confirm reuse", "create a new ARCH"). Asking to define or update the
  architecture, or to reuse a component or service, names no outcome. The outcome the request named
  only picks the direction: step 8 still asks once the change is known (a reuse that writes nothing
  excepted), unless the request says to proceed without questions;
- the inspection scope, and that a question is non-blocking;
- that a named accepted ADR answers a named question ("ADR-004 settles the identity provider").

An answer to an architectural question stated in the request ("use Keycloak", "reuse our current
auth service") is a preference: record it in a new DEC's `notes` (for an existing DEC, which stays
unchanged, say it in the reply and in an amendment's Change Log row, never in a review record's)
and recommend it when you ask. It never becomes an ADR until the user picks it. The one exception
to all of this is policy: an approved mandated platform that answers exactly a question resolves it
without asking.

**Asking.** Use AskUserQuestion when it is available: at most 4 questions per call, 2–4 options
each, with the recommended option first and marked "(Recommended)", each form tagged with its step
(Workflow, item 4). Otherwise ask in plain text and end your turn. Use at most `interview.max_calls`
calls (step 1; default 8) unless the user asks for more; questions left when the budget runs out
stay open. The waiver question doesn't count against `interview.max_calls`. When step 8 will ask,
keep one call for its question; a gate still comes first. Write nothing that a pending answer
affects until the answer arrives.

**"Proceed without questions."** When the request says to proceed without questions (or "don't ask
me anything", "proceed without asking me anything else", "decide nothing"), ask nothing but an open
gate (below):
- no question is newly resolved except by a mandated platform, and never a DEC reopened because its
  mandated platform changed. Resolutions already in an ARCH being amended follow
  [readiness.md](references/readiness.md), "State changes when amending";
- an accepted ADR the request names as answering a question doesn't resolve it: record it as for a
  preference (above), and the DEC stays open;
- `outcome` stays `null` unless the request names it for this ARCH (above), and step 8 asks nothing;
- write no ADR at all;
- read nothing outside the inspection scope, and record the gap as an unknown.

Everywhere in this skill, "the request says to proceed without questions" means the request says so
**and** the waiver question (step 1, item 5) was answered *Proceed without questions* or couldn't be
asked. After *Ask me as usual*, anything typed instead or a dismissal, ask as if the request hadn't
said so, step 8's confirmation included.

A request that only defers decisions ("leave the questions open", "I'll decide them later") is not
this rule: those questions take *Decide later* at step 7, with no question asked, and step 8 still
asks.

Every other step still runs, and a new ARCH is still written when none covers the system. This
never answers the two gates: **which PRD**, and **reuse, amend or create when an existing ARCH covers
the system**. A gate is answered only by an explicit answer to that gate, in the request ("amend
ARCH-001") or in reply to your question; name where the answer came from. If a gate is open, ask it
and write nothing.

## Workflow

Work through this checklist:

```
- [ ] 1. Resolve policy (R1, R2)
- [ ] 2. Select the PRD
- [ ] 3. Read the PRD; apply R3 and R4
- [ ] 4. Select or create the ARCH
- [ ] 5. Inspect within the scope
- [ ] 6. Identify the architectural questions and components
- [ ] 7. Resolve each question explicitly
- [ ] 8. Propose and confirm the outcome
- [ ] 9. Write the ARCH and ADRs
- [ ] 10. Validate every file written
- [ ] 11. Compute readiness, report and hand off
```

**Keep the checklist in the task list** when the session has task-list tools (TaskCreate and
TaskUpdate, or TodoWrite; load them through ToolSearch if they are deferred). DevForgeAI's progress
tracker credits an answer to a step only when the list marks that step in progress and, for a
question form, its tag names that step:

1. Before anything else, create one task per step: subject `<N>. <title>` (the step's line without
   the box), metadata `devforgeai_step: N`; with TodoWrite, content `<N>. <title>`. Create them even
   when an earlier run's tasks are still in the list: those don't count for this run.
2. Mark a step in_progress when its work starts, and completed as soon as it is done, one step at a
   time; a step with nothing to do is completed too (step 5 with no scope, step 7 with no open
   question). Mark step 11 completed just before writing the final reply. After step 8's *Write
   nothing*, leave steps 9 to 11 pending instead: the run stops there.
3. Before asking any question, mark the step it belongs to in_progress: the waiver → step 1; which
   PRD → step 2; reuse, amend or create → step 4; reading outside the inspection scope → step 5;
   every decision question → step 7; the outcome → step 8; any other question → the step whose work
   asks it (a component's kind → step 6). Complete step 7 and mark step 8 in_progress before the
   outcome question, and ask it alone, in its own form. Never put two steps' questions in one question form.
4. Tag each question form with its step: AskUserQuestion's
   `metadata: {"source": "devforgeai_step:N"}`, which the user doesn't see, and `header: "Step N"`
   on each of its questions, which the user does. The one exception is the waiver question (step 1,
   item 5), tagged `devforgeai_waiver`.

Without task-list tools, copy the checklist into your response and tick items off (`- [x] N.`) in
your reply text as you go.

### 1. Resolve policy (R1, R2)

Follow [references/policy.md](references/policy.md) with the framework defaults in
[references/defaults.md](references/defaults.md). This step comes first, before any question.
1. Run the skill's script with Bash, whatever `docs/specs/policy/` holds, a missing folder included:

   ```
   python3 ${CLAUDE_SKILL_DIR}/scripts/validate_policy.py docs/specs/policy
   ```

   Run it alone, on one line: never joined to another command with `;`, `|` (or `||`) or a
   background `&`, and with no second command line (`&&` and `2>&1` are fine). Then its exit status
   is the script's: the progress tracker credits no run whose status another command hides. Don't
   guard it with a folder check (`test -d docs/specs/policy &&`): the script reports a missing folder
   itself, and the check's exit 1 would read as a policy error.
2. With no folder, or no approved `POL-*.md` in it, it prints `No policy folder at …` or
   `OK: no approved policy document …` and exits 0: there is no approved policy, so the framework
   defaults apply. It skips and reports every document that isn't approved (SV-06), and checks each
   approved one in full against the policy schemas and SV-01 to SV-06 and SV-08. Never validate the
   documents by reading them instead. Reading them is still needed: their `status` for item 3's last
   case and, once the script passes, each setting's values to resolve item 4 (R2).
3. **Act on its exit code** (policy.md, R1):
   - **0:** continue. Its `ignored` lines go into the resolution line.
   - **1: stop (ERR-02).** Before asking or writing anything, name each error it printed: the
     policy file, the setting (`SET-NN` and its key) or frontmatter field, the field, and the rule
     (`schema` or `SV-NN`), and say nothing was written. Never fall back silently.
   - **2, or the script can't be run:** if any policy document has `status: approved`, stop
     (ERR-02): say that policy validation couldn't run, quote its message, and write nothing. If
     none is approved, continue with the framework defaults.
4. Resolve `interview.max_calls` and `architecture.mandated_platforms` (R2); an override that
   `overridable_by` doesn't allow also stops the skill (ERR-02). Then read local preferences.
5. **The waiver**, only when the request says to proceed without questions (or "don't ask me
   anything", "proceed without asking me anything else", "decide nothing") and AskUserQuestion is
   available: after the policy script and before any other question, with step 1 still in_progress,
   ask it at most once in a run, alone in its own form, with `metadata: {"source": "devforgeai_waiver"}` (not
   `devforgeai_step:1`: the progress tracker records it as the waiver, never as a step's answer),
   `header: "Step 1"`, the question "Your request says to proceed without questions. Should I?" and
   exactly these two options, in this order:
   - `Proceed without questions`, description "I ask nothing more; decisions that need you stay
     open, except the outcome your request named.";
   - `Ask me as usual`, description "I ask about each decision as it comes up."

   The answer decides how "Proceed without questions" applies (above). Without AskUserQuestion, ask
   nothing, in plain text or otherwise, and follow the request; when the request doesn't say to
   proceed without questions, never ask it. The two gates are still asked when open.

### 2. Select the PRD

- **No PRD exists at all** (with or without an ID): say that no PRD exists yet and that nothing was
  written, and point to `/devforgeai:prd` as the next step. Write nothing.
- **An ID was given.** Read `docs/specs/prd/<ID>.md`. If it doesn't exist (ERR-01), list the PRD IDs
  that do exist with their titles and status, write nothing, and stop.
- **No ID.** List every PRD with its ID, title and status, and ask which one to use. Never guess,
  even when only one exists. With no answer, write nothing.

### 3. Read the PRD; apply R3 and R4

Read its frontmatter (`status`, `version`, `owner`, `stage`, `operating_context`), its functional and
non-functional requirements (constraints included), and every `[NEEDS ADR]` and
`[NEEDS CLARIFICATION]` marker. Every PRD link you add uses the version you read here; links on
existing items keep theirs. Never edit the PRD.
- **Draft PRD** (`status` other than `approved`): warn now, before any question or write, that the
  result is a proposal because the PRD may still change. Repeat the warning in the handoff.
- **R3 and R4:** the operating context is the one the request states, otherwise the PRD's
  `operating_context` (policy.md R3). If it is still unknown, evaluate policy as `production`. Note
  the required quality categories for section 2.
- **Product questions stay the PRD owner's.** A `null` priority or release, or a
  `[NEEDS CLARIFICATION]` marker, never becomes an architectural question
  ([references/readiness.md](references/readiness.md)).

### 4. Select or create the ARCH

Read the frontmatter of each `docs/specs/arch/ARCH-*.md`: `system`, `status`, `version`, `outcome` and
its PRD links. An ARCH **covers this system** when its frontmatter links this PRD, or its `system`
names the product this PRD's title names.
- **None covers this system:** a new ARCH is created at step 9, with the next free ID. No question
  here; step 8 confirms create.
- **One covers it:** compare what it was defined against with the PRD now: the PRD version it cites,
  new or changed requirements and `[NEEDS ADR]` markers, resolvers that were superseded, and
  mandated platforms that changed (readiness.md). Offer **reuse** only when its frontmatter already
  links this PRD and no mandated platform it relies on changed (readiness.md, "Reuse, and deciding
  a question later"). Recommend **amend** when it needs new or changed questions or components, or
  when a user is present and a blocking DEC is open: deciding it is an amendment. Give the reasons
  and ask. Offer a separate new ARCH only as a non-recommended option. Never create a second
  baseline automatically, and write nothing until the user answers.
- **Several could apply** (ERR-04): list them with their systems and ask. Never pick one silently.

A choice of reuse or amend, in the request or an answer, picks the direction. It confirms the
outcome only when the run will write nothing to the existing ARCH (a reuse whose PRD link already
equals the PRD's version); otherwise step 8 asks for confirmation once the change is known, unless
the request says to proceed without questions (step 8). A reuse that step 4 can't offer answers
nothing: say why (naming the DEC, the setting and both platforms when a mandated platform changed),
offer amend or create, and write nothing until the user answers. Otherwise the choice is what step 8
proposes, unless step 8 finds another outcome is needed, such as a DEC changing in a reuse run: then
say why and ask again.

### 5. Inspect within the scope

Follow [references/inspection.md](references/inspection.md). The scope is what the user named; if
nothing was named, it is `[]` and no code is read. Every path you read, list or search is inside the
scope, apart from the documents read by contract. Ask before going outside it (ERR-03); with no user
or a "no", record the unknown.

Record an EVD for every project source you consult (inspection.md, "Recording evidence"): the PRD
(`kind: prd`, `classification: context`), each other ARCH that bears on this system, each ADR that
bears on this PRD's questions, each applied policy setting, and each code or configuration source.
When the evidence is insufficient, write a `[NEEDS CLARIFICATION]` marker and never recommend the
`reuse` outcome on assumption.

### 6. Identify the architectural questions and components

Follow [readiness.md](references/readiness.md):
- Record a DEC for each question separate epics must share: component boundaries and
  responsibilities, data ownership, major interactions and interfaces, deployment, and how quality
  requirements are met. Every `[NEEDS ADR]` marker becomes a DEC.
- Each DEC cites **every** affected requirement, at the PRD version examined, and is blocking unless
  the user says otherwise. Leave feature-level detail (API fields, migrations, class structures) to
  specs.
- Describe the components separate epics must share as CMP items, with a Mermaid overview: at least
  one, even with no user. They show the proposed shape; where a boundary is a choice rather than a
  given, a DEC records that choice (readiness.md). Link each CMP to the NFRs and constraints it
  serves. Each mandated platform (step 1) appears as the CMP that provides that capability, carrying
  the setting's `constrains` link: the one place R5 allows. When amending after its platform changed,
  leave that CMP as it is and add none (readiness.md, "A mandated platform that changed").
- Give each CMP its `kinds` from output-rules.md: one or more of `user-interface`, `service`,
  `platform`, `api`, `relational-store`, `data-store` and `external`. A component can have several (a
  service that also exposes an API has `service` and `api`). Ask when a kind is uncertain. With no
  user, record only kinds the PRD or the evidence states; if none is certain, leave `kinds` out and
  add `[NEEDS CLARIFICATION: kinds of CMP-NN]` to section 8.
- **When amending**, add new items after the existing ones, leave existing items unchanged, reopen
  any DEC whose resolver no longer counts, and give a new requirement that an existing question
  affects a new DEC of its own (readiness.md, "State changes when amending").

### 7. Resolve each question explicitly

Use only the three means in [readiness.md](references/readiness.md), in this order:
1. **Mandated platforms:** a setting that answers exactly a question resolves it as policy, with no
   question and no ADR. A DEC reopened because its platform changed needs the user's confirmation
   instead, as in means 2 (readiness.md, "A mandated platform that changed").
2. **Existing accepted ADRs:** when one may answer a question, ask the user to confirm it answers
   exactly that question.
3. **The user's decision:** for each remaining question, present 2 or 3 options with trade-offs plus
   *Decide later*. Each explicit pick becomes a new ADR with `status: accepted`. *Decide later*
   keeps the question open; write a proposed ADR only if the user asks to record the deferral. A
   later decision on a question with a proposed ADR supersedes that ADR (output-rules.md).

`approved_by` on an accepted ADR is the deciding user's name. If the conversation hasn't named them,
ask who is deciding, offering the PRD owner as the first option. A preference stated in the request
is recorded as "Decisions that belong to the user" says and made the recommended option; it is not a
decision until picked.
With no user, only means 1 applies.

- **PRD conflicts (BEH-12):** when architecture shows a requirement is infeasible, too costly or in
  conflict (for example with a mandated platform), write the proposed change in section 7 and in the
  handoff, addressed to the PRD owner. Never edit the PRD: the owner amends it.
- **Superseding an accepted ADR** only on the user's explicit approval (output-rules.md).
- **Stopping mid-session** (ERR-06): offer to save a draft ARCH with every unanswered question open
  and `outcome: null`. Write it, or nothing, as the user chooses.

### 8. Propose and confirm the outcome

Propose one outcome, with reasons:
- **reuse:** step 4 can offer it (the ARCH links this PRD, and no mandated platform it relies on
  changed), it covers the PRD with no new or changed question, and no DEC changes in this run. Once
  the user confirms reuse, new requirements don't override it: name each one no active blocking DEC
  cites (below);
- **amend:** the existing ARCH needs new or changed questions or components, or a DEC's `state` or
  `resolved_by` changes in this run (a decision recorded, or a resolver that no longer counts);
- **create:** no ARCH covers the system.

Using a mandated or existing platform or component is not a reuse outcome. When the request says to
proceed without questions, ask nothing: write the outcome the request named for this ARCH, otherwise
`null`. Otherwise, unless the outcome is a reuse that writes nothing (its PRD link already equals
the PRD's version: step 4's choice, in the request or an answer, confirms that), ask the user to
confirm the outcome now that the change is known, alone in its own question form, even when step 4
or the request already chose or confirmed it: a confirmation given before the change is known only
picks the direction.
- **The run will write to the existing ARCH** (an amendment, or reuse's review record): name what
  will change: the DEC, CMP and EVD items added, the DECs whose state or resolver changes, the ADRs
  accepted or superseded, the new version, and an approved ARCH's return to in-review.
- **Create:** name the new ARCH's ID and what it records.

Offer, in order: `Confirm <outcome> (Recommended)`, which writes it; `Write it with the outcome open`
(not for reuse, which has nothing to write so), the change with `outcome: null`; `Write nothing`:
write and edit no file, mark step 8 completed, leave steps 9 to 11 pending (step 9 completed without
a write claims work not done), reply with each question and its answer, say nothing was written so a
later run can use them again, and stop. Typed text is the user's answer: act on what it says; a
dismissal leaves the question unanswered: write nothing.

Write `outcome` only when it is confirmed at step 8, or the request named it and said to proceed
without questions; otherwise it stays `null`. Confirming the outcome accepts no decision. When
proposing reuse, and in the report when it is confirmed, name each active requirement no active
blocking DEC cites: it is reported ready with no architectural question holding it back
(readiness.md, "Reuse, and deciding a question later").
- **Reuse confirmed and the ARCH's PRD link is older than the PRD's version:** write the review record
  (output-rules.md), and nothing else.
- **Reuse confirmed and the link already equals the PRD's version:** nothing to write; go to step 11.

### 9. Write the ARCH and ADRs

Read [references/output-rules.md](references/output-rules.md) before writing.

**New ARCH.**
1. **ID and path:** the highest `NNN` among `docs/specs/arch/ARCH-NNN.md` plus one, or `ARCH-001`.
   Create the folder if it is missing. Never ask for or accept a file name.
2. **Template:** build from `${CLAUDE_SKILL_DIR}/assets/arch.md`. Keep every heading, replace every
   placeholder and example item, and delete every `<!-- -->` comment.
3. **Frontmatter:** `status: draft`, `version: 1`, `created` and `updated` today; `owner` from the
   request, otherwise the PRD's owner; `authors` the owner and `"claude-code"`; `generated_by` with
   `tool: "claude-code"`, `model:` your own model ID and `session: "${CLAUDE_SESSION_ID}"`;
   `reviewed_by: []`, `approved_by: ""`, `approved_on: null`; every `hash: null`. `upstream` holds
   the PRD link at the version read in step 3, plus the `informed_by` policy links R5 puts in
   frontmatter. Set `system` (the product this PRD's title names), `outcome` and
   `inspection_scope`.
4. **Change Log:** one row, author `claude-code (session ${CLAUDE_SESSION_ID})`, whose change text
   ends with the policy resolution line, for example `Initial draft for PRD-001 v1. Policy
   resolution: interview.max_calls=8 (default); …`.

**Amending** and **the review record** follow output-rules.md exactly.

**ADRs.** One per explicit decision (`accepted`), and one per deferral the user asked to record
(`proposed`), from `${CLAUDE_SKILL_DIR}/assets/adr.md` (output-rules.md, "ADRs"). Write the ADR before
the DEC that it resolves.

Never modify a PRD, BRN or policy document, and never modify an existing ADR except to record a
supersession the user explicitly approved.

### 10. Validate every file written

Read each written file back and check it against the **Self-check list** in
[output-rules.md](references/output-rules.md), item by item.

- **Count the checks.** The first readback is the initial check. For each error it reports, repair
  the file, read it back and check again: at most three repair cycles, so at most four checks.
- **A repair changes a file** to address a reported error. An error you can't repair ends the
  cycles early; don't repeat an unchanged check. For example, an error inside an existing item that
  an amendment must leave byte-identical can't be repaired, because changing that item breaks the
  amendment rules.
- Record each check and repair in the reply, for example `Check 1: 1 error (DEC-03 has no
  upstream link); repair 1: added it; check 2: passed`.

**If errors remain (ERR-05), stop.** Never leave `approved` or `accepted` on content that failed
validation. Follow output-rules.md, "When validation still fails (ERR-05)", exactly: the status each
file keeps, the rollback of a supersession recorded in this run, and the audit records. Then end
with the validation-failure report (step 11), skip the readiness handoff, and never present
readiness as validated.

### 11. Compute readiness, report and hand off

Compute readiness with the rule in [readiness.md](references/readiness.md) from the ARCH as written
(or as it stands, when nothing was written), reading each resolving ADR's current `status` and
`superseded_by`, and each mandated platform's current setting, now.

Write the final reply in this order:

1. This block, filled in. It opens the reply: nothing comes before it, not even the checklist.

   ```
   ARCH-NNN written to docs/specs/arch/ARCH-NNN.md (for PRD-NNN vN; new | amended to version N | review recorded | unchanged)
   Outcome: create | amend | reuse | unconfirmed (proposed: <outcome>)
   ADRs written: ADR-NNN (accepted | proposed), … | none
   Ready for epic work: FR-NNN, NFR-NNN, … | none
   Blocked: FR-NNN (DEC-NN, DEC-NN); NFR-NNN (DEC-NN) | none
   Proposed PRD changes: <requirement: change, for the PRD owner> | none
   Policy resolution: <the resolution line's entries>
   ```

   Ready and Blocked together list every active FR and NFR of the PRD exactly once. After a reuse,
   mark each ready requirement no active blocking DEC cites `(no architectural question cites it)`.

2. Then, briefly: the checks and repairs from step 10; the draft-PRD warning if it applies; each open
   question with its DEC ID; any resolver that no longer counts and why; each proposed PRD change,
   addressed to the PRD owner; and any finding labelled "observed practice".
3. The next step, as its own paragraph outside any code block. It starts with the words
   **Next step**, names the PRD by its ID and never by its path, and nothing follows it.

For the next step, check whether `${CLAUDE_SKILL_DIR}/../epic/SKILL.md` exists:
- **It exists:** tell the user to run `/devforgeai:epic PRD-NNN` for the ready requirements.
- **It does not exist:** say the epic workflow (planned as `/devforgeai:epic`) is not built yet, and
  that once it is, `/devforgeai:epic PRD-NNN` runs for the ready requirements.

For example: "Next step: the epic workflow (planned as `/devforgeai:epic`) isn't built yet. Once it
is, run `/devforgeai:epic PRD-001` for the ready requirements; FR-001 waits for DEC-01 and DEC-02."

Never start epic work, and never write an epic.

When the skill stops without writing (a gate is open, the outcome question is pending at step 8, the
user picked *Write nothing* at step 8, no PRD exists, ERR-01, ERR-02, ERR-04), the reply says why and
what the user can do, and leaves out the report block and the next step.

**After ERR-05**, a validation-failure report replaces both the block and the next step. It gives
each file path with the status left (and cleared approvals), any supersession rolled back, every
check and repair made, and each unresolved error with where it is. It lists no requirement as ready,
says readiness wasn't validated, and never tells the user to run `/devforgeai:epic`.

## Output contract

- **Paths:** `docs/specs/arch/ARCH-NNN.md` and `docs/specs/adr/ADR-NNN.md` in the current project;
  each name is the ID only.
- **Shape:** the two templates, with every heading kept and no author comments.
- **Data:** the ARCH holds only the `components`, `decisions` and `evidence` collections, with their
  defined fields ([output-rules.md](references/output-rules.md)).
- **Decisions:** `outcome` is non-null only when the user confirmed it at step 8, or the request named
  it and said to proceed without questions. A DEC is resolved only by an
  applied mandated platform, an accepted ADR the user confirmed, or a new ADR the user accepted. The
  skill never sets an ARCH's status to `approved` (a review record leaves an approved ARCH approved),
  and no ADR is accepted without the user.
- **Traceability:** every DEC cites the requirements it affects at the PRD version examined; every
  applied policy setting is linked once; every project source consulted is an EVD (a review record
  adds none).
- **Readiness:** reported per requirement by the rule, never stored and never guessed.

## Examples

**No user, no ARCH yet.** "Do the architecture for PRD-002. Proceed without questions." The skill
runs the policy script (no folder, so defaults); with no question tool it asks no waiver (with one,
it asks the waiver first, and on *Ask me as usual* asks as usual). It reads PRD-002 (approved, version 3, with
`[NEEDS ADR: calendar integration; affects FR-004]`). No ARCH exists, so it will create one. No scope
was named, so no code is read. It records a DEC per shared question, each citing the requirements it
affects, all open; writes no ADR; leaves `outcome: null`; validates; and reports FR-004 as blocked by
its DEC, then the next step.

**An ARCH already covers the system.** "Run architecture for PRD-002." ARCH-001 covers the same
clinic system and cites PRD-002 version 2; version 3 adds a `[NEEDS ADR]` marker. The skill
recommends amending ARCH-001, with that reason, asks, and writes nothing until the user answers.
Once the user picks amend and step 7 is done, it asks again at step 8, naming the new DEC, the new
version and ARCH-001's return to in-review.

**A decision made interactively.** For "Which identity provider handles sign-in?" the skill offers
three providers with trade-offs against the security NFRs, recommending one. The user picks it, so
the skill writes ADR-004 (`accepted`, `approved_by` the user) and sets the DEC's
`resolved_by: [ADR-004]`. The user then confirms `create`; that resolves nothing else, so the
session-revocation question stays open.

## References

- [references/policy.md](references/policy.md): read at step 1. The validation script's exit codes,
  precedence, R3 to R5, and the resolution line. A byte-identical copy of the prd skill's file, as
  are `scripts/validate_policy.py` and `references/schemas/`.
- [references/defaults.md](references/defaults.md): read at step 1. Framework defaults and the quality
  floor per operating context. A byte-identical copy of the prd skill's file.
- [references/inspection.md](references/inspection.md): read at step 5. The inspection scope, allowed
  commands, leaving the scope, and evidence classification.
- [references/readiness.md](references/readiness.md): read at steps 4, 6, 7 and 11. Which questions to
  record, the three means of resolution, mandated platforms that changed, reuse, and the readiness
  rule.
- [references/output-rules.md](references/output-rules.md): read before step 9. Keys, fields, links,
  amendment, the review record, ADRs, ERR-05, and the self-check list used at step 10.
