---
name: epic
description: Turns a DevForgeAI PRD into epic documents for the requirements that are ready for epic work and in the current release. It reads readiness from the architecture description (ARCH), proposes how to group the requirements into epics, writes them once the user confirms, and reports every requirement left out and why. Use after Architecture Definition, when splitting a PRD into epics or planning delivery.
argument-hint: "PRD-NNN"
metadata:
  devforgeai-id: "SKL-004"
  devforgeai-version: "4"
---

# Epic

Perform the epic step for one PRD, after Architecture Definition. Select the PRD's requirements that are
ready for epic work and in the current release, propose how to group them into epics, write the epics
the user confirms as `docs/specs/epic/EPIC-NNN.md`, report every requirement left out with its reason,
and hand off to the story step. An epic written for a requirement whose architectural question is still
open lets separate epics build conflicting foundations.

Three rules shape everything below:
- **The ARCH file is the readiness contract.** Readiness is computed from the architecture description
  and the ADR and policy files as they are now, never from any skill's reply or summary.
- **Release selects, priority orders.** `release: current` decides what gets an epic. MoSCoW priority
  orders the epics (Must, then Should, then Could) and never selects on its own, except `wont`. A `null`
  release, or a `null` priority in the current release, is undecided and goes back to the PRD owner. A
  `later` requirement's `null` priority is normal, not undecided.
- **The skill adds, it never rewrites.** It writes new epics only. The PRD, BRNs, ARCHs, ADRs, policy
  documents and existing epics are read-only.

## Inputs

- `$ARGUMENTS`: a PRD ID such as `PRD-002`, or empty. When it is empty, use a PRD ID the user stated in
  the conversation. Never accept a file path; if given one, ask for the ID.
- Read by contract, all read-only: the PRD at `docs/specs/prd/<ID>.md`, ARCHs in `docs/specs/arch/`, ADRs
  in `docs/specs/adr/`, existing epics in `docs/specs/epic/`, and approved policy documents in
  `docs/specs/policy/` (only for the bounded check in
  [references/selection.md](references/selection.md)). Reach them by these paths; never list the working
  directory or the repository root to find them.
- Template: `${CLAUDE_SKILL_DIR}/assets/epic.md`.

**Tools.** Read and Glob (or read-only `ls` and `cat` on `docs/specs/` when Glob isn't available); Write for
new epics; Edit only on epics written in this run, at step 8; AskUserQuestion. Inspect no code, and run no
other shell command. Never run a `devforgeai` command: that CLI doesn't exist, and a program with that
name on PATH can't be trusted.

## Decisions that belong to the user

The skill proposes; the user decides:

- **Which PRD**, when no ID was given.
- **Which ARCH**, when several active ARCHs cite the PRD (ERR-04).
- **The platform behind an older-form policy record**: when the ARCH recorded a policy resolver only as
  `architecture.mandated_platforms=POL-NNN#SET-NN`, with no platform (selection.md, check 3), ask at step
  4, before step 6, one question per resolver, naming the setting's current value:
  `Did POL-NNN#SET-NN already mandate "<platform> for <capability>" when DEC-NN was resolved?`
  Offer Yes, No and Don't know, with none recommended: only the user knows. Yes: it counts. No or don't
  know: treat it as a changed mandate (blocked).
- **The grouping**: which epics, and which requirements each holds. You only propose it.
- **Priority, release and requirement changes**: an undecided priority or release, and any change to a
  requirement, belong to the PRD owner. Report them; never decide or ask about them.
- **Approving an epic**: epics are always written as `draft`.

A grouping stated in the request counts as confirmed: "one epic for everything eligible", "one epic per
priority level", "put FR-003 and FR-004 together". Apply it, or a change the user makes when asked, as
given. Only the invariants at step 6 limit it, never your own grouping preferences. It never makes a
left-out requirement eligible. If it leaves an eligible FR unplaced or places one twice, ask about that
FR; when nobody can be asked, follow step 6.

**Asking.** Decide from the request alone whether a user is present: one is, unless the request says to
proceed without questions. Use AskUserQuestion when it is available: at most 4 questions per call, 2–4
options each, with the recommended option first and marked "(Recommended)" (except the older-form
question above, which recommends nothing). When it isn't available or
fails, put the question at the end of the final reply and end your turn. Write nothing that a pending
answer affects until the answer arrives.

**"Proceed without questions."** When the request says to proceed without questions (or "don't ask me
anything") and gives no grouping, nobody can confirm one: write the proposed grouping with the
unconfirmed-grouping marker (step 6). Whenever the request says to proceed without questions, an
older-form policy record counts, and the reply's notes name the gap. This never answers **which PRD** or
**which ARCH**: if either is open, ask it and write nothing.

## Workflow

Copy this checklist into your response and tick items off as you go:

```
- [ ] 1. Select the PRD
- [ ] 2. Read the PRD
- [ ] 3. Find the current ARCH
- [ ] 4. Compute readiness
- [ ] 5. Read existing epics and select
- [ ] 6. Propose and confirm the grouping
- [ ] 7. Write the epics
- [ ] 8. Validate every epic written
- [ ] 9. Report and hand off
```

### 1. Select the PRD

- **An ID was given.** Read `docs/specs/prd/<ID>.md`. If it doesn't exist (ERR-01), list the PRD IDs that
  do exist with their titles and status, write nothing, and stop.
- **No ID.** List every PRD in `docs/specs/prd/` with its ID, title and status, and ask which one to use.
  Never guess, even when only one exists. With no answer, write nothing.

### 2. Read the PRD

Read its frontmatter (`status`, `version`, `owner`, `target_release`), every functional and
non-functional requirement with its `status`, `priority` and `release`, and every `[NEEDS ADR]` marker.
Every PRD link you write uses the `version` read here. Never edit the PRD.

**Draft inputs.** If the PRD's `status` is anything but `approved` (or, at step 3, the ARCH's), the epics
are proposals because an input may still change. Say so in every epic written (output-rules.md,
"Markers") and in the handoff.

### 3. Find the current ARCH

Follow [references/selection.md](references/selection.md), "Finding the current ARCH". Ignore any ARCH
whose `status` is `superseded` or `deprecated`. Use an ARCH only when its frontmatter link to this PRD has
the PRD's current version. Otherwise stop and write nothing:
- no active ARCH cites the PRD (ERR-02): say that readiness comes from the architecture description, so
  nothing can be selected without one, and tell the user to run `/devforgeai:architecture PRD-NNN`
  first;
- the ARCH's PRD link has another version, usually older (ERR-03): name both versions, and tell the
  user to review the architecture with `/devforgeai:architecture PRD-NNN`; confirming reuse there
  records the review;
- several active ARCHs cite it (ERR-04): list them and ask.

### 4. Compute readiness

Follow selection.md, "The readiness rule", for every active FR and NFR:
- a DEC cites a requirement only through a link with **this PRD's ID and that item**; an ARCH can cover
  several PRDs whose requirement numbers collide;
- read each DEC that cites the requirement, its `state` before its resolvers (an open DEC blocks), and
  each resolving ADR's `status` and `superseded_by` from its file now;
- apply the bounded policy check (selection.md) to each `POL-NNN#SET-NN` resolver, and resolve no other
  policy. Two policies that mandate platforms for different capabilities don't conflict, and a newer
  policy version passes when the ARCH's latest resolution line still holds the setting unchanged. A
  changed platform or capability **blocks** the requirement by its DEC. A line that names the setting
  only in the older form is an evidence gap: with a user present, ask the question in "Decisions that
  belong to the user" now, then stop, writing nothing and giving no report until the answer arrives; with
  no user, it counts. A missing record is unknown;
- match every `[NEEDS ADR]` marker to its own DEC; a marker with no matching question blocks every
  requirement it names, even when other DECs citing them are resolved;
- when readiness can't be established, the requirement is **unknown**, naming the missing input or
  failed check: never guess ready or blocked. A resolved DEC with an empty `resolved_by` is unknown.

### 5. Read existing epics and select

Read every existing epic's frontmatter (selection.md, "Covered requirements"): an FR of this PRD that an
active existing epic refines is **covered**. NFRs are never covered. Note the highest `EPIC-NNN` in use.

Apply the selection rule. A requirement is **eligible** when it is active, ready, `release: current`,
`priority` must, should or could, and, for an FR, not covered. Every requirement that ends up in no new
epic gets one row with every reason that applies and one next action (selection.md, "Left-out rows"). A
blocked or unknown NFR that an active existing epic refines lists `refined by EPIC-NNN` in its row.

**When there is nothing to write**, decide which case applies before you write the reply:
1. **Everything eligible already has an epic (ERR-05).** No FR is eligible, and every eligible NFR is
   already refined by an active epic. At least one requirement is active, ready, `release: current` and
   must, should or could; for example, on a rerun with unchanged inputs. Write nothing, then go to
   step 9. Say that no requirement needs a new epic. Give each eligible NFR its
   `already refined by EPIC-NNN` row.
2. **Nothing is eligible yet (ERR-08).** No requirement is active, ready, `release: current` and must,
   should or could; for example, an open DEC blocks every current-release requirement. Write nothing,
   then go to step 9. Say that **no requirement is eligible for an epic yet**. Never say that no
   requirement needs a new epic, and never say that everything already has an epic.

### 6. Propose and confirm the grouping

Group the eligible requirements into epics by the rules below. Show each proposed epic's working title,
priority and requirements (by ID, with a few words each), then:
- **The request stated a grouping:** it is confirmed; apply it as given, within the invariants.
- **Someone can answer:** ask the user to confirm or change the grouping, recommending your proposal.
  Write nothing until they do. Apply a changed grouping as given, within the invariants; ask again only
  if it breaks one. If the user stops before confirming (ERR-07), write nothing and say how to resume:
  run `/devforgeai:epic PRD-NNN` again.
- **Nobody can confirm** (the request says to proceed without questions and gives no grouping): write
  your proposal.
- **A stated grouping leaves an eligible FR unplaced or places one twice, and nobody can be asked:**
  keep the rest of the stated grouping, and place only that FR by your proposal.

In the last two cases an FR's placement is unconfirmed: add this marker to section 8 of **every** epic
written in this run: `[NEEDS CLARIFICATION: grouping proposed by the skill; not confirmed by the user]`.

**Invariants.** No grouping, stated, changed or proposed, may break these:
- An epic refines only eligible requirements.
- Each eligible FR is refined by **exactly one** new epic.
- Each eligible NFR is attached to at least one new epic, unless an active existing epic already
  refines it. Which epics it goes in is the grouping's choice. Add `note: "partial: <which part>"`
  when more than one active epic, existing or new, refines it.
- A **standalone NFR epic** is written only for an eligible NFR that no active epic, existing or new in
  this run, refines.
- An epic's **priority** is the highest among the FRs it refines (Must, then Should, then Could); a
  shared NFR never raises it, and a standalone NFR epic takes its NFRs' highest priority.
- New epics are **numbered and listed** Must first, then Should, then Could.

**How to propose** (heuristics for your own proposal; a user's grouping overrides them):
- Each epic is a **deliverable capability**: something users can do when it is done. Never propose
  grouping by layer, component or team.
- Attach an eligible NFR to every new epic whose capability it constrains. Attaching it never creates
  another deliverable.

### 7. Write the epics

Read [references/output-rules.md](references/output-rules.md) before writing. For each confirmed epic,
in priority order:
1. **ID and path:** the next free `docs/specs/epic/EPIC-NNN.md` (the highest existing number plus one,
   or `EPIC-001`). Create the folder if it is missing. Never ask for or accept a file name.
2. **Template:** build from `${CLAUDE_SKILL_DIR}/assets/epic.md`. Keep every heading; replace every
   placeholder and example link; delete every `<!-- -->` comment except section 7's GENERATED
   comment, which stays as the story map placeholder.
3. **Frontmatter:** `status: draft`, `version: 1`, today's dates; `owner` from the request, otherwise the
   PRD's owner; `authors` the owner and `"claude-code"`; `generated_by` with `tool: "claude-code"`,
   `model:` your own model ID and `session: "${CLAUDE_SESSION_ID}"`; `reviewed_by: []`,
   `approved_by: ""`, `approved_on: null`; every `hash: null`; `priority` by the invariants (step 6);
   `target_release` the PRD's (output-rules.md says what to write when the PRD names none).
   `upstream`: a `refines` link to every requirement it groups at the PRD
   version, then an `informed_by` link to the current ARCH at its version.
4. **Body:** the goal, business value and scope; at least one `DW-NN` item with a criterion and an
   evidence method; the dependencies (the ARCH decisions it relies on, and other epics) and risks; the
   proposal sentence when an input is a draft.
5. **Change Log:** one row, author `claude-code (session ${CLAUDE_SESSION_ID})`.

Never write a story, and never modify an existing file.

### 8. Validate every epic written

Read each epic back and check it against the **Self-check list** in
[output-rules.md](references/output-rules.md), item by item. Fix each problem with Edit on that epic and
check again, **at most three attempts**. If errors remain (ERR-06), stop: keep the epics as `draft`, and
end with a validation-failure report naming each file path and its unresolved errors. Don't present
the epics as ready for the story step.

### 9. Report and hand off

Write the final reply in this order, as plain Markdown:

1. The report, which opens the reply: nothing comes before it, not even the checklist.

   ```
   Epics for PRD-NNN vN, from ARCH-NNN vN:
   - EPIC-NNN — <title> (must): FR-NNN, FR-NNN, NFR-NNN. docs/specs/epic/EPIC-NNN.md
   Left out:
   - FR-NNN: <reason>; <reason>. <Next action>.
   ```

   List every epic written with its path, title, priority and the requirements it refines. When nothing
   was written, use instead the line for the case at step 5: "No requirement needs a new epic." for
   ERR-05, or "No requirement is eligible for an epic yet." for ERR-08. Then give one row for every
   requirement that is in no new epic, in PRD order (selection.md, "Left-out rows"), or
   `Left out: none`. Each eligible NFR that no new epic attaches gets its `already refined by EPIC-NNN`
   row. Together, the epic lines and rows name **every FR exactly once** and **every NFR at least once**.
   An NFR attached to several new epics appears in each of their lines.
2. Then, briefly:
   - the proposal warning when an input is a draft;
   - the unconfirmed-grouping warning when it applies;
   - any resolver that no longer counts, and why;
   - any policy resolver that still counts at a newer policy version, naming both versions;
   - any policy resolver whose ARCH record has no platform (the older form), naming the gap and whether
     the user confirmed it or nobody could be asked (selection.md, "The bounded policy check").
   Ask no questions about left-out requirements.
3. The next step, as its own paragraph outside any code block. It starts with the words **Next step**,
   names the epics by ID and never by path, and nothing follows it.

For the next step, check with Read or Glob whether `${CLAUDE_SKILL_DIR}/../story/SKILL.md` exists:
- **It exists:** tell the user to run `/devforgeai:story EPIC-NNN` for each new epic, Must first.
- **It does not exist:** say stories are written by hand from the DevForgeAI story template for now, and
  that once the story skill (planned as `/devforgeai:story`) is built, it runs with an epic ID, naming
  the new epics.

For example: "Next step: the story skill (planned as `/devforgeai:story`) isn't built yet, so write
stories for EPIC-001 by hand from the story template for now. Once it is, run
`/devforgeai:story EPIC-001`."

When nothing was written:
- **Everything eligible already has an epic (ERR-05):** name the story step for the epics that already
  refine the requirements.
- **Nothing is eligible yet (ERR-08):** name **no** story step, and don't mention `/devforgeai:story`.
  The next step is `/devforgeai:architecture PRD-NNN`, to resolve the blocking questions, when any row
  is blocked or unknown. Otherwise it is the PRD owner's decision on the undecided rows. Otherwise say
  that nothing remains for the current release.

When the skill stops without writing (ERR-01 to ERR-03, ERR-07), the reply says why and what the user can
do, leaves out the report, and its next step names the command to run (`/devforgeai:architecture PRD-NNN`
for ERR-02 and ERR-03). When it stops to ask (which PRD, which ARCH for ERR-04, or the older-form question
at step 4) without AskUserQuestion, the reply says why, leaves out the report and the next step, and ends
with the question. After ERR-06, the validation-failure report replaces both the report and the next step.

## Output contract

- **Paths:** new epics only, at `docs/specs/epic/EPIC-NNN.md` in the current project; each name is the ID
  only.
- **Shape:** the template, with every heading kept, section 7's GENERATED comment kept, and no other
  author comments.
- **Data:** only the `done_when` collection, with its defined fields
  ([output-rules.md](references/output-rules.md)).
- **Selection:** an epic refines only eligible requirements; every eligible FR is in exactly one new
  epic; readiness comes from the ARCH, ADR and policy files, never from a reply.
- **Report:** every FR named exactly once and every NFR at least once; no story step when nothing is
  eligible yet (ERR-08).
- **Decisions:** the grouping is the user's; an unconfirmed grouping carries its marker. Every epic is
  `draft`.
- **Traceability:** `refines` links at the PRD version read, and one `informed_by` link to the ARCH at its
  version; stories will cite the `DW-NN` items.

## Examples

**Grouping stated.** "Write the epics for PRD-002: one epic for everything eligible. Proceed without
questions." ARCH-001 cites PRD-002 at its current version 3. FR-001 and FR-003 are ready; FR-002 is blocked
by DEC-02 (open); FR-004 is `release: later`. The skill writes EPIC-001 refining FR-001, FR-003 and the
eligible NFR-001, then reports FR-002 (blocked by DEC-02) and FR-004 (later) with their next actions,
and ends with the next step naming EPIC-001.

**Stale architecture.** ARCH-001 cites PRD-002 at version 2, and PRD-002 is at version 3 after a priority
change. The skill writes nothing, says ARCH-001 was reviewed against v2 while the PRD is v3, and tells
the user to review the architecture with `/devforgeai:architecture PRD-002`.

**Interactive.** With no grouping in the request, the skill proposes "Online booking (must): FR-001,
FR-003, NFR-001" and "Front-desk day view (could): FR-006, NFR-001 (partial)", asks the user to confirm
or change it, and writes nothing until they answer.

## References

- [references/selection.md](references/selection.md): read at steps 3, 4 and 5. Finding the current ARCH,
  the readiness rule, marker matching, the bounded policy check, covered requirements, the selection
  rule and the left-out rows.
- [references/output-rules.md](references/output-rules.md): read before step 7. Keys, links, the
  `done_when` block, sections, markers, ERR-06, and the self-check list used at step 8.
