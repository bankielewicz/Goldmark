---
name: prd
description: Turns a DevForgeAI brainstorm (BRN) document into a product requirements document (PRD), interviewing only for what the brainstorm leaves open, such as delivery stage, priorities, current-release versus later scope, quality requirements and constraints. Use when the user wants to write a PRD, define requirements or scope from a brainstorm, or continue the DevForgeAI planning chain after brainstorming.
argument-hint: "[BRN-NNN]"
metadata:
  devforgeai-id: "SKL-002"
  devforgeai-version: "5"
---

# PRD

Turn one brainstorm (BRN) into a PRD at `docs/specs/prd/PRD-NNN.md`, the second document of the
planning chain. The architecture step and the epic workflow read the PRD mechanically: they build
what it marks as decided and treat every `null` as open. So the PRD must be structurally exact and
must record only decisions the user actually made.

## Inputs

- `$ARGUMENTS`: a BRN ID such as `BRN-002`, or empty. When it is empty, use a BRN ID the user
  stated in the conversation. Never accept a file path; if given one, ask for the ID.
- BRNs: `docs/specs/brainstorm/BRN-*.md` (read-only). PRDs: `docs/specs/prd/PRD-*.md`.
- Architecture context: `docs/specs/adr/ADR-*.md`, plus documents the BRN or the request names.
- Policy: `docs/specs/policy/POL-*.md` and `.claude/devforgeai.local.md`, if present.
- The template: `${CLAUDE_SKILL_DIR}/assets/prd.md`.

Use Read, plus Glob and Grep when available (otherwise `ls` on the named folder), and Write and Edit
for the PRD. Use Bash only for that `ls` and the policy validation script (step 1). Never crawl
the codebase, and never run a `devforgeai` command: that CLI doesn't exist, and a program with that
name on PATH can't be trusted.

## Decisions that belong to the user

The PRD records decisions; it never makes them. These are the user's:

- **Which BRN** to use, when no ID was given.
- **Whether to continue from an unconverged BRN.**
- **New PRD or extend an existing one.**
- **`stage`, `operating_context`, and each requirement's `priority` and `release`.**
- **Whether a design preference is a hard constraint**, and which accepted ADRs apply.

Write a value only when the user supplied it (in the request, in the BRN, or in an answer) or
confirmed your suggestion. A statement that clearly maps to one value counts as supplied: "this is
our MVP" is `stage: mvp`; "real patients book through it from day one" is
`operating_context: production`. For `priority` and `release`, only a statement about importance or
release counts ("sign-up is a must for this release"). A modal verb inside a requirement's wording
("patients must sign in", "it must run on AWS") states the requirement, not its priority, and
`release` is never inferred from the stage. Otherwise write `null` for those four fields and a
`[NEEDS CLARIFICATION: …]` marker for any other gap. A `null` means "not decided yet". An
invented value looks decided to every later step, which then builds on a choice nobody made.

**Asking.** Use AskUserQuestion when it is available, within its limit: at most 4 questions per
call, 2–4 options each. Otherwise ask in plain text and end your turn. Either way, write nothing
until the answer arrives.

**"Proceed without questions."** When the request says to proceed without questions (or "decide
nothing else", "don't ask me anything"), ask no interview questions and leave every unanswered
gap `null` or marked. Every step still runs, including the quality categories in step 7. This
never answers the three gates: which BRN, an unconverged BRN, and new versus extend. A gate is
answered only by an explicit answer to that gate, in the request ("write a new PRD for BRN-002",
"extend PRD-001", "BRN-002 is a draft, continue anyway") or in reply to an earlier question. Such an
answer is final: don't ask that gate again, and name in the reply where the answer came from. If a
gate is open, ask it and write nothing. With no answer, no file is written.

## Workflow

Copy this checklist into your response and tick items off as you go:

```
- [ ] 1. Resolve policy (R1, R2)
- [ ] 2. Select the BRN
- [ ] 3. Read the BRN
- [ ] 4. Choose: new PRD or extend
- [ ] 5. Read architecture context
- [ ] 6. Draft from the BRN
- [ ] 7. Interview only the gaps; settle quality categories
- [ ] 8. Write the PRD
- [ ] 9. Validate the PRD
- [ ] 10. Report and hand off
```

### 1. Resolve policy (R1, R2)

Follow [references/policy.md](references/policy.md) with the framework defaults in
[references/defaults.md](references/defaults.md). This step comes first, before any question.
1. If `docs/specs/policy/` holds no `POL-*.md`, use the framework defaults and go to item 4.
2. Otherwise validate every document with the skill's script, using Bash. It needs Python 3 with
   PyYAML and jsonschema; without them it exits 2 (item 3).

   ```
   python3 "${CLAUDE_SKILL_DIR}/scripts/validate_policy.py" docs/specs/policy
   ```

   It skips and reports draft and in-review documents (SV-06), and checks each approved one in full
   against the policy schemas and SV-01 to SV-06 and SV-08. Never validate the documents by reading
   them instead. Reading them is still needed: their `status` for item 3's last case and, once the
   script passes, each setting's values to resolve item 4 (R2) and step 7 (R4).
3. **Act on its exit code** (policy.md, R1):
   - **0:** continue. Its `ignored` lines go into the resolution line.
   - **1: stop (ERR-08).** Before asking or writing anything, name each error it printed: the
     policy file, the setting (`SET-NN` and its key) or frontmatter field, the field, and the rule
     (`schema` or `SV-NN`). Write nothing. Never fall back silently.
   - **2, or the script can't be run:** if any policy document has `status: approved`, stop
     (ERR-08): say that policy validation couldn't run, quote its message, and write nothing. If
     none is approved, continue with the framework defaults.
4. Resolve `interview.max_calls` and `architecture.mandated_platforms` (R2). An override that
   `overridable_by` doesn't allow also stops the skill (ERR-08), named the same way. Then read local
   preferences (policy.md, "Local preferences"). A bad local entry is ignored and reported, never
   fatal.

### 2. Select the BRN

- **An ID was given** (`$ARGUMENTS` or the conversation). Read `docs/specs/brainstorm/<ID>.md`. If
  it doesn't exist (ERR-01), say so, list the BRN IDs that do exist with their titles, and stop.
- **No ID.** List the *unprocessed* BRNs. A BRN is unprocessed when it has at least one
  `disposition: promoted` idea that no PRD cites. To check, search `docs/specs/prd/PRD-*.md` for
  upstream links with `id: BRN-NNN` and `item: IDEA-NN`, at any version. Show each unprocessed
  BRN's ID, title and number of uncited promoted ideas, and ask which one to use. Don't list fully
  cited BRNs.
  Never guess, even when only one is listed. If none is unprocessed (ERR-04), say why, write
  nothing and stop. If no BRN exists, say that no brainstorm exists yet and point to
  `/devforgeai:brainstorm`. Otherwise list each BRN that has no promoted idea, with its `status`,
  pointing to `/devforgeai:brainstorm` to converge it, and for each other BRN say that its
  promoted ideas are all already cited, naming the PRDs that cite them.

Nothing is ever written into a BRN to mark it processed.

### 3. Read the BRN

Read its frontmatter `status`, `owner` and `version`, and its `problems`, `ideas` and
`assumptions` item blocks, plus the candidate success signals (section 8).
- **Malformed** (ERR-05): if an item block can't be read, report which block failed and the BRN
  path, and stop. Never repair a BRN. Never modify a BRN at all.
- **No promoted idea** (ERR-03): say the BRN has no promoted idea, so there is nothing to turn into
  requirements. Write nothing, and point the user back to `/devforgeai:brainstorm` to converge it.
- **Every promoted idea already cited** (ERR-04): say so, naming the PRD item that cites each one.
  Write nothing, and stop. This holds even when the request asks for a new PRD.
- **Not converged** (ERR-02): when `status` is not `converged`, warn that some ideas may not be
  decided yet, even when every idea has a disposition: only the user's convergence settles them.
  Ask whether to continue anyway. Continue only on an explicit yes; with no answer, write nothing.

Use **only** ideas with `disposition: promoted`. Never cite an open, parked or rejected idea
anywhere in the PRD, by ID or by link. Leave out a promoted idea that any PRD already cites, at any
version, and name it in the reply with the item that cites it ("IDEA-01 left out: PRD-001#FR-001").

### 4. Choose: new PRD or extend

If `docs/specs/prd/` holds no PRD, the PRD is new: continue.

Otherwise, read each existing PRD's `title`, goals, non-goals, `owner`, `status` and
`target_release`, and decide on scope, ownership and lifecycle. Never decide on product identity or
on how many PRDs exist. A superseded or deprecated PRD is never extended, even when the request
names it: recommend a new PRD and ask.
- Recommend **extending** a PRD only when the promoted ideas belong to its existing initiative and
  scope, share its owner, and fit its release lifecycle.
- Recommend a **new PRD** when they form a distinct initiative, have a different owner or approval
  path, or follow a different schedule, even within the same product.

**A choice the user already stated answers this gate.** When the request, or the user's reply to
an earlier question, explicitly chooses a new PRD ("write a new PRD for BRN-002") or names the PRD
to extend ("extend PRD-001"), follow that choice without asking again, and say in the reply where
it came from ("New PRD, as your request asked"). If your recommendation differs, give its reasons
in one sentence; the choice still stands. "Proceed without questions" is not a choice.

Otherwise, state your recommendation with its reasons, and ask. A single existing PRD is not
evidence that it is the right destination. Write nothing and change no PRD until the user answers.
For how to extend, see step 8.

### 5. Read architecture context

Read `docs/specs/adr/ADR-*.md` (if present) and any document the BRN or the request names. Nothing
else. Ignore superseded ADRs. Classify what you learn:

| Finding | Recorded as |
|---|---|
| Existing commitment: an accepted ADR that applies | Frontmatter link `{id: ADR-NNN, relation: constrains, version: N, hash: null}` |
| Hard constraint (mandated platform, required integration, data residency, existing system, regulation) | NFR with `category: constraint` |
| Design preference (architecture style, framework) | An open question, never a requirement |
| Unresolved decision, including a *proposed* ADR | `[NEEDS ADR: <decision>; affects FR-NNN, …]` in section 12, and no link |

Propose which accepted ADRs apply and let the user confirm them in the interview. When there is
no interview, link only accepted ADRs that the request names, and mention the others in the
reply as proposals. Every `architecture.mandated_platforms` setting left after R2 (step 1) becomes a
constraint NFR, whether or not the BRN mentions its capability. The NFR's item `upstream` cites its setting (policy.md). Never decide a design question in
the PRD.

### 6. Draft from the BRN

Draft the whole PRD before asking anything, following
[references/brn-mapping.md](references/brn-mapping.md):
- problems that an idea this PRD drafts addresses → section 2 prose, plus frontmatter `derives`
  links (a problem the PRD already links keeps its link);
- each promoted idea that no PRD cites yet (step 3) → one or more functional requirements starting
  "The system shall", each with an item `derives` link to its idea, so every FR derives from a
  promoted idea;
- assumptions → `assumptions`, with `derives` links;
- candidate success signals → `success_metrics`.

**NFRs cite their actual source.** Link an NFR to a BRN item only when that item itself states the
requirement. Otherwise it cites the policy setting, ADR or PRD it comes from, and it has no link
when the user stated it. Never add a brainstorm link to a requirement the user stated or policy
added.

**Constraints, not design.** Record each fixed external condition as an NFR with
`category: constraint`. State it as the condition, not as a design, and say where it applies:
"(applies to the whole product)", "(applies to <capability>)" or "(applies to <environment>)".
Draft a design preference the user offers ("I'm leaning towards microservices") as a section 12
bullet: a design decision for a future ADR, never a requirement or constraint. The interview (round
2) asks whether it is a hard constraint; only a "yes" moves it into a constraint NFR.
If another PRD already defines the constraint or a cross-cutting NFR, cite it with a frontmatter
link `{id: PRD-NNN, item: NFR-NNN, relation: constrains, version: N, hash: null}` instead of
copying it, and say in the PRD what it applies to.

### 7. Interview only the gaps; settle quality categories

When the request says to proceed without questions, ask nothing and go straight to **Quality
categories** below, which always runs. Otherwise, ask in batched rounds following
[references/interview.md](references/interview.md):
1. framing: stage, operating context, `target_release` name, primary users, non-goals;
2. architecture context;
3. requirements;
4. quality and constraints;
5. success metrics.

Skip every question the BRN or the request already answers. Follow interview.md's rules for every
call: at most 4 questions per call, split any question with more options than the host allows,
and use at most `interview.max_calls` calls (step 1; default 8), gate questions included, unless the
user asks for more. A gate is still asked after the budget is spent. Anything else left when the
budget runs out becomes `[NEEDS CLARIFICATION]`.

- **Requirements.** Ask one question per requirement, FR or NFR, showing its drafted statement, with
  these options (an NFR that round 4 writes gets it in a following call):
  - *must now* or *should now* (or *could now*, if the user says so): write that priority with
    `release: current`;
  - *later*: `release: later`, `priority: null`;
  - *won't*: `priority: wont`, `release: current`. This is an explicit exclusion from this
    release; a requirement that should never be built is edited or dropped instead.

  The user may also edit the statement, or answer *decide later*, which leaves both `null`.
- **Quality.** Ask one question per required category (below) that the draft doesn't cover, and
  always one open question for any other quality need. Stage decides the depth (interview.md).

If the user stops partway (ERR-07), ask whether to save a draft PRD. If yes, write it with every
undecided field `null`, then validate and report it as any other write (steps 8 to 10). If no,
write nothing.

**Quality categories (always runs, with or without an interview).** Read interview.md's "Recording
quality answers" even when there is no interview: it holds the exact entries and markers.
1. Establish the operating context (R3): from the request, the BRN, or the framing answer.
2. Take the floor for that context from [defaults.md](references/defaults.md). Add the
   categories of each policy `quality.required_categories` setting whose `applies_when` includes
   the context (R4); additions never remove a floor category.
3. If the context is still unknown, use the production set, keep `operating_context: null`, and
   record `operating context unknown, resolved as production` in the resolution line.
4. Settle each required category from the user's answer, in the request or the interview, keeping
   four kinds apart: an explicit none (one sentence in section 7's prose, no NFR); no target yet
   (the requirement or metric kept, its target marked); a partial answer (the NFRs it states, and
   the category's marker kept for the rest); and no answer (the category's marker in section 12,
   never a placeholder requirement).
5. **An explicit none never waives policy.** A category that applied policy requires, or a mandated
   platform's constraint category, keeps a section 12 marker naming the setting after a none, and a
   mandated platform's constraint NFR is written whatever the user answered.

### 8. Write the PRD

Read [references/output-rules.md](references/output-rules.md) before writing.

**New PRD.**
1. **ID and path.** Take the highest `NNN` among `docs/specs/prd/PRD-NNN.md` and add one, or use
   `PRD-001` when there is none. Write `docs/specs/prd/PRD-NNN.md`, creating the folder if it is
   missing. Never ask for or accept a file name. The product or release name goes in `title`.
2. **Template.** Build from `${CLAUDE_SKILL_DIR}/assets/prd.md`. Keep every section heading,
   including section 13, "Epic map", with its GENERATED comment. Replace every placeholder with
   content or a `[NEEDS CLARIFICATION: …]` marker, and every example item with real items; write a
   collection with none as `success_metrics: []`, never as a placeholder item. Delete every other
   `<!-- -->` comment and the frontmatter's `#` comments, keeping the `# --- prd-specific ---` line.
3. **Frontmatter.**
   - `status: draft`, `version: 1`, and `created` and `updated` set to today's date.
   - `owner`: the name the request gives, otherwise the BRN's `owner`.
   - `authors`: the owner and `"claude-code"`.
   - `generated_by`: `tool: "claude-code"`, `model:` your own model ID, and
     `session: "${CLAUDE_SESSION_ID}"`.
   - `reviewed_by: []`, `approved_by: ""`, `approved_on: null`. Every `hash` is `null`.
   - `stage` and `operating_context` per "Decisions that belong to the user". Never set
     `approved`.
4. **Links.** Record each applied policy setting in exactly one place, as policy.md R5 says: a
   mandated platform's `constrains` link on its constraint NFR only, every other applied setting as
   a frontmatter `informed_by` link.
5. **Change Log.** Write one row whose author is `claude-code (session ${CLAUDE_SESSION_ID})`. Its
   change text ends with the policy resolution line (policy.md), for example:
   `Initial draft from BRN-001. Policy resolution: interview.max_calls=8 (default); …`.

**Extending a PRD** (only after the user chose it):
- Raise `version` by one and set `updated`. Keep `status` (draft or in-review).
- Set `generated_by` to this session and your model. Keep the existing `authors`, adding
  `"claude-code"` only if it's missing. Keep `reviewed_by` and every earlier Change Log row exactly
  as they are: they record who reviewed earlier versions.
- Give new items the next free number in each collection. Leave every existing item
  byte-identical, even one that looks invalid: write the extension, and step 9 reports the error.
- Add the new BRN links and one Change Log row that names the new BRN and the items added, says
  `This revision has not been reviewed.`, and ends with the resolution line. Never edit earlier rows.
- If the PRD was `approved`, set `status: in-review` and clear `approved_by` (`""`) and
  `approved_on` (`null`), so the widened scope is reviewed explicitly. Tell the user that epics
  citing this PRD are now suspect links to re-review.
- Say in the handoff, too, that the new revision hasn't been reviewed.

### 9. Validate the PRD

Read the file back and check it against the **Self-check list** in
[output-rules.md](references/output-rules.md), item by item. Also check that every non-null
`stage`, `operating_context`, `priority` and `release` this write set is one the user supplied or
confirmed, and reset any other value this write set to `null`. Values already in the PRD before an
extension are not this write's decisions: leave them as they are.

- **Count the checks.** The first readback is the initial check. For each error it reports, repair
  the file, read it back and check again: at most three repair cycles, so at most four checks.
- **A repair changes the file** to address a reported error. An error you can't repair ends the
  cycles early; don't repeat an unchanged check. For example, an error inside an existing item that
  an extension must leave byte-identical can't be repaired, because changing that item breaks
  step 8.
- Record each check and repair in the reply, for example `Check 1: 1 error (FR-004 lacks a
  derives link); repair 1: added it; check 2: passed`.

**If errors remain (ERR-06), stop.** Never set or restore `approved` on content that failed
validation:
- a new PRD stays `draft`;
- an extension keeps the status step 8 gave it: a draft or in-review PRD keeps its status, and an
  approved PRD stays `in-review` with `approved_by: ""` and `approved_on: null`.

Then end with the validation-failure report (step 10), not the handoff.

### 10. Report and hand off

Write the final reply in this order:

1. This block, filled in. It opens the reply: nothing comes before it, not even the checklist.

   ```
   PRD-NNN written to docs/specs/prd/PRD-NNN.md (from BRN-NNN; new | extended to version N)
   Requirements: N functional, N non-functional (N constraints) · Success metrics: N
   Open decisions: N (null stage, operating_context or release; null priority on current-release items)
   Open questions: N [the NEEDS CLARIFICATION markers and design questions, briefly]
   Validation: passed at check N of at most 4 [each repair, briefly, or "no repairs"]
   Policy resolution: [the resolution line's entries]
   ```

2. Every `[NEEDS ADR]` marker, each as this pair: the marker as written in section 12, then the
   sentence with that marker's FR IDs in place of `FR-NNN, …` and nothing else changed.

   ```
   [NEEDS ADR: <decision>; affects FR-NNN, …]
   Epics for FR-NNN, … must wait until the architecture step resolves that exact decision, by an accepted ADR or an approved mandated platform answering it.
   ```

   Name both ways even when the resolution line reads
   `architecture.mandated_platforms=none (default)`: a policy approved later can answer it, and
   that is the architecture step's call.
3. Any other discussion, such as where a gate's answer came from, ADRs you proposed but didn't
   link, the promoted ideas left out because a PRD already cites them (step 3), and, after an
   extension, suspect epics, every existing link that cites an older version of its document (a
   suspect link to review, never an error; output-rules.md check 9), and that the new revision
   hasn't been reviewed.
4. The next step, as its own paragraph outside any code block. It starts with the words
   **Next step**, names the PRD by its ID and never by its path, and nothing follows it.

The next step tells the user to run `/devforgeai:architecture PRD-NNN`, for example: "Next step:
run `/devforgeai:architecture PRD-001` to settle the architecture questions this PRD leaves open."

Never start architecture work, and never write an ADR or an epic.

When the skill stops without writing (a gate is open, ERR-01 to ERR-05, ERR-08), the reply says why
and what the user can do. Leave out the report block.

**After ERR-06**, a validation-failure report replaces both the block and the handoff. It gives the
file path and the status left (with the approval cleared, if it was approved), every check and
repair made, and each unresolved error with where it is. It says the PRD is not ready for the
architecture step until its owner fixes those errors. It never tells the user to run
`/devforgeai:architecture` and never presents the PRD as valid.

## Output contract

- **Path:** `docs/specs/prd/PRD-NNN.md` in the current project; the name is the ID only.
- **Shape:** `${CLAUDE_SKILL_DIR}/assets/prd.md`, with every heading kept, including the
  GENERATED epic map, and no author comments.
- **Data:** only the `success_metrics`, `functional_requirements`, `non_functional_requirements` and
  `assumptions` collections, with their defined fields
  ([output-rules.md](references/output-rules.md)).
- **Decisions:** `stage`, `operating_context`, `priority` and `release` are non-null only where the
  user supplied or confirmed them. `status` is never `approved`, and a failed validation never
  restores it.
- **Traceability:**
  - every FR derives from a promoted idea; every NFR cites its actual source (a BRN item only when
    that item states it), and none when the user stated it;
  - every applied policy setting is linked once, with its version;
  - every open architecture decision is a `[NEEDS ADR]` marker naming the requirements it blocks.
- **History:** an extension keeps the authors, reviewers and every earlier Change Log row, and says
  that the new revision hasn't been reviewed.

## Examples

**Answers in the request.** "Write the PRD for BRN-001. It's a prototype that only our developers
will use, with test data; priorities can wait. Proceed without questions." The skill resolves
policy (none, so framework defaults) and reads BRN-001 (converged, two promoted ideas). No PRD
exists yet. It drafts two capability-level requirements, one per promoted idea, both with `null`
priority and release, and writes
`stage: prototype`, `operating_context: local`. It marks the constraint category as
`[NEEDS CLARIFICATION]`, validates, reports, and hands off.

**No ID given.** "Let's write the next PRD." The skill lists the unprocessed BRNs with their counts
of uncited promoted ideas, asks which one to use, and writes nothing yet.

**An unrelated PRD exists.** The skill recommends a new PRD, with reasons about scope, owner and
schedule, asks, and writes nothing until the user answers.

**The choice is stated.** "Write a new PRD for BRN-002. Proceed without questions." PRD-001 covers
another initiative. The request answers the new-versus-extend gate, so the skill writes PRD-002
without asking, leaves PRD-001 unchanged, and says the choice came from the request.

## References

- [references/policy.md](references/policy.md): read at step 1. It covers the validation script's
  exit codes, precedence, R3 to R5, and the resolution line.
- [references/defaults.md](references/defaults.md): read at step 1. It holds the framework defaults
  and the quality floor per operating context.
- [references/brn-mapping.md](references/brn-mapping.md): read at step 6. It says how each BRN
  part maps into the PRD.
- [references/interview.md](references/interview.md): read at step 7, even when there is no
  interview, for the quality-category rule and how to record quality answers. It holds the question
  bank, depth by stage, and batching.
- [references/output-rules.md](references/output-rules.md): read before step 8. It holds the
  keys, fields, links and markers, and the self-check list used at step 9.
