---
name: brainstorm
description: Runs a structured brainstorming session and writes a DevForgeAI brainstorm (BRN) document with identified problems, ideas, assumptions and user-confirmed dispositions. Use whenever the user asks to brainstorm, explore ideas or options, come up with ways to solve a problem, or generate ideas for a product, feature or process, even when they don't mention a document, and to start the DevForgeAI planning chain before a PRD.
argument-hint: "[topic]"
metadata:
  devforgeai-id: "SKL-001"
  devforgeai-version: "10"
---

# Brainstorm

Run a brainstorming session on one topic and record it as a BRN document at
`docs/specs/brainstorm/BRN-NNN.md`, the first document of the planning chain. A PRD cites the BRN's
problems, ideas and assumptions by ID, so the document must be structurally exact and must only
claim decisions the user actually made.

## Inputs

- `$ARGUMENTS`: the topic. It may be empty; then take the topic from the conversation, or ask for it.
- Existing BRNs: `docs/specs/brainstorm/BRN-*.md` in the current project.
- The template: `${CLAUDE_SKILL_DIR}/assets/brainstorm.md`.
- The work file: `devforgeai/drafts/brainstorm/${CLAUDE_SESSION_ID}.md` (or the draft path the progress
  tracker's line on continuing names, below), the work so far, saved after steps 3 and 4 and at
  step 5 ([work-file.md](references/work-file.md)).

## Decisions that belong to the user

Two decisions are the user's, never yours:

- **Each idea's disposition** (`promoted`, `parked` or `rejected`) and its `reason`.
- **Convergence**: whether the brainstorm is finished (`status: converged`).

Propose both, then ask. Anything the user has not explicitly confirmed stays `disposition: open`,
`reason: null` and `status: draft`. When no confirmation can be obtained, treat it as *not
confirmed* and continue with those values. This happens when the user said to proceed without
questions (and picked *Proceed without questions* in the waiver question, Intake, or it couldn't be
asked) or no user is there to answer. Record your proposals in section 6 prose so nothing is
lost. When the user confirms them later, after the BRN is written, don't edit from that message
alone. Continue this run's tasks (create none): mark step 5 in_progress, ask the step-5 form,
tagged; then mark step 6 and edit the BRN in place, step 7 and validate it again, and step 8 to
report. An unconfirmed disposition looks valid to every structural check, and a PRD would build on a
choice nobody made. That is why this rule matters more than any other in this skill.

## Workflow

Work through this checklist:

```
- [ ] 1. Intake: topic, existing BRNs, clarifying questions
- [ ] 2. Select a framework
- [ ] 3. Diverge: problems, ideas, assumptions
- [ ] 4. Evaluate with the framework
- [ ] 5. Propose dispositions and ask the user to confirm
- [ ] 6. Write the BRN
- [ ] 7. Validate the BRN
- [ ] 8. Report and hand off
```

**Keep the checklist in the task list** when the session has task-list tools (TaskCreate and
TaskUpdate, or TodoWrite; load them through ToolSearch if they are deferred). DevForgeAI's progress
tracker credits an answer to a step only when the list marks that step in progress and, for a
question form, its tag names that step:

1. Before anything else, even before asking for a topic, create one task per step: subject
   `<N>. <title>` (the step's line without the box), metadata `devforgeai_step: N`; with TodoWrite,
   content `<N>. <title>`. Create them even when an earlier run's tasks are still in the list: those
   don't count for this run.
2. Mark a step in_progress when its work starts, and completed as soon as it is done, one step at a
   time; a step with nothing to do is completed too. Mark step 8 completed just before writing the
   final reply, so nothing follows the hand-off.
3. Before asking any question, mark the step it belongs to in_progress: the waiver, topic, clarifying
   and extend-or-new questions belong to step 1; confirming dispositions and convergence belongs to step
   5; any other question (the framework's own, saving a draft) belongs to the step whose work asks
   it. Never put two steps' questions in one question form.
4. Tag each question form with its step: AskUserQuestion's
   `metadata: {"source": "devforgeai_step:N"}`, which the user doesn't see, and `header: "Step N"`
   on each of its questions, which the user does. The one exception is the waiver question (Intake),
   tagged `devforgeai_waiver`.

Without task-list tools, copy the checklist into your response once you have a topic, and tick items
off (`- [x] N.`) in your reply text as you go.

**Save the work as it goes.** After step 3 and after step 4, and at step 5 before its question, write
the whole work file (*Inputs*) with the Write tool, in the shape
[work-file.md](references/work-file.md) gives: `id: BRN-000`, every disposition `open`, step 5's
proposals as a table. Before the first save, when `devforgeai/drafts/.gitignore` is missing, write it
holding the single line `*`. Never write the work file under `docs/specs/`, never run the validator on
it, never delete it (the progress tracker removes it once the BRN validates), and don't save it again
once the BRN is written.

**Continuing an earlier run.** When this text ends with the progress tracker's line "This run
continues the earlier brainstorm run …" and it says "Its draft is <path>", that path is the work file:
load it, save to it from then on, and keep its problems, ideas, scores and IDs. Follow the rest of the
line as written ([work-file.md](references/work-file.md#continuing-a-run)).

### 1. Intake

**The waiver comes first**, only when the request says to proceed without questions (or not to ask,
or to skip questions) and AskUserQuestion is available. With step 1 marked in_progress and before any
other question, ask it at most once in a run, alone in its own form, with
`metadata: {"source": "devforgeai_waiver"}` (not `devforgeai_step:1`: the progress tracker records it
as the waiver, never as a step's answer),
`header: "Step 1"`, the question "Your request says to proceed without questions. Should I?" and
exactly these two options, in this order:
- `Proceed without questions`, description "I ask nothing more, except which topic and whether to
  extend an existing BRN, when open; decisions that need you stay open.";
- `Ask me as usual`, description "I ask about each decision as it comes up."

On *Proceed without questions*, follow the request: ask nothing else, except the topic question
(Intake 1) when no topic is given and extend-or-new (Intake 2) when a BRN on the topic exists. On
*Ask me as usual*, on anything typed instead, or on a dismissal, ask as if the request hadn't said
so. Without
AskUserQuestion, ask nothing, in plain text or otherwise, and follow the request; when the request
doesn't say to proceed without questions, never ask it.

1. **Topic.** Use `$ARGUMENTS`, or the topic stated in the conversation. If there is none, ask
   "What topic should we brainstorm?" and stop. Create no file and no directory until the user
   gives a topic (the task list is not a file).
2. **Existing BRNs.** Glob `docs/specs/brainstorm/BRN-*.md` and read each file's `title`. If one
   covers the same or a closely similar topic, show its ID, title and path and ask:
   *extend it* (version + 1, with a Change Log entry) or *create a new BRN*. Wait for the answer
   and write nothing before it. Never overwrite an existing BRN silently.
3. **Clarifying questions.** Ask at most three, in one message: what triggered this, who is
   affected, and what constraints apply. Skip any the request already answers. Ask none when
   the request says to proceed without questions and the waiver was answered *Proceed without
   questions* or couldn't be asked. Record anything still unknown as
   `[NEEDS CLARIFICATION: question]` in the document; never fill a gap with a guess.

Use AskUserQuestion for questions and confirmations when it is available, tagged with its step
(Workflow); otherwise ask in plain text and end your turn.

### 2. Select a framework

Read `${CLAUDE_SKILL_DIR}/references/frameworks/INDEX.md`. Pick the framework the user named, or
the one whose *use when* criteria best fit the topic; use `diverge-converge` when nothing fits
better. Read the chosen framework file and follow it from here on. Tell the user in one
sentence which framework you chose and why, and switch if they ask.

If the index is missing, or the chosen file is missing or lacks any section the index requires,
fall back to `diverge-converge` and tell the user which file was the problem.

### 3. Diverge

Follow the framework's steps and questions.

- Frame **problems** first (`PRB-NN`), stated from the affected user's side.
- Capture **ideas** (`IDEA-NN`). Record the user's own ideas in their own words, alongside the
  ideas you generate. Generate between 5 and 15 ideas unless the user asks for another number.
  Every idea addresses at least one problem and starts with `disposition: open`.
- Capture **assumptions** (`ASM-NN`): beliefs the ideas depend on, each with how to validate it.

Mark step 3 completed, then save the work file (Workflow) before marking step 4 in progress.

### 4. Evaluate

Fill `value`, `effort`, `risk` and `score` on each idea exactly as the framework's *Mapping to the
BRN* section says. Write only into the `problems`, `ideas` and `assumptions` collections and
their defined fields, whatever the framework. Framework-specific reasoning goes into the section
5 (evaluation method) and section 6 (convergence) prose, never into new YAML keys.

Mark step 4 completed, then save the work file again before marking step 5 in progress.

### 5. Propose dispositions and confirm

Propose a disposition and a one-line reason for each idea, and ask the user to confirm them and
whether the brainstorm has converged. Save the work file with the proposals table first, before
asking, or right away when no question is asked. With AskUserQuestion, put the proposals inside the form, never
only in text before it (the dialog hides that text). One form tagged step 5 (Workflow, item 4),
both questions with `header: "Step 5"`:
1. "Confirm these dispositions?", options `Accept all as proposed` (Recommended) and `Leave them
   open`, each option's `preview` holding the table: idea ID, title, proposed disposition, reason.
   When the table is too long for a preview, list each idea's ID and proposed disposition there and
   say in the question that the reasons are in the reply. Changes come typed under Other.
2. "Has the brainstorm converged?", options `Converged` and `Not yet`.

Without AskUserQuestion, present the table in plain text and ask. Then:

- Accept all writes every proposal; Leave them open writes none; typed changes write the dispositions
  the text states; write no other disposition, and leave every other idea `open` with `reason: null`.
- Set `status: converged` only on *Converged*; otherwise `draft`.

If the user stops partway through the session, ask whether to save what has been captured as a
draft BRN. If yes, first save the work file once more with its `id` set to the ID the draft BRN gets
(allocated as step 6 does), so a continued run extends it; then write the draft BRN with every
disposition `open`, and don't run the validator on it. If no, write nothing more.

### 6. Write the BRN

1. **ID and path.** Take the highest `NNN` among `docs/specs/brainstorm/BRN-NNN.md` and add one;
   use `BRN-001` when there is none. Zero-pad to three digits. Write the file to
   `docs/specs/brainstorm/BRN-NNN.md`. The file name is the ID only: never ask for a file name, and
   if the user offers one, say the file is named by its ID and put their wording in the title.
   Create `docs/specs/brainstorm/` if it is missing, and say that you created it.
2. **Build from the template.** Read `${CLAUDE_SKILL_DIR}/assets/brainstorm.md`. Keep every section
   heading. Replace every placeholder with content or with a `[NEEDS CLARIFICATION: …]` marker.
   Replace the example items with the real ones. Delete every HTML comment.
3. **Frontmatter.**
   - `id`: the allocated ID. `title`: the descriptive topic. `version: 1`.
   - `created` and `updated`: today's date as `YYYY-MM-DD`.
   - `status`: see step 5.
   - `owner`: the user's name if the conversation or existing `docs/specs/` documents show it.
     Otherwise ask, or use `"[NEEDS CLARIFICATION: owner]"` when you cannot ask.
   - `authors`: the user (the owner's name) and `"claude-code"`.
   - `generated_by`: `tool: "claude-code"`, `model:` your own model ID (for example
     `"claude-opus-5-5"`), `session: "${CLAUDE_SESSION_ID}"`: the latest session to write the BRN.
   - `reviewed_by: []`, `approved_by: ""`, `approved_on: null`. Every `hash` is `null`.
   - `upstream`, `participants` and `sources`: fill from the conversation, or leave them empty.
4. **Change Log.** One row: version, today's date, `claude-code (session ${CLAUDE_SESSION_ID})`,
   what changed. The session ID is the name of the conversation's transcript, so
   `claude --resume` with that ID reopens the conversation behind that version.

**Extending an existing BRN.** Keep every existing item ID with its meaning unchanged, because PRD
requirements cite these IDs. Give new items the next free number in their collection. Retire an
item by setting `status: deprecated` (plus `superseded_by` if replaced), never by deleting or
renumbering it. Bump `version`, set `updated`, set `generated_by.session` to this session, and
add a Change Log row naming this session. Never edit earlier Change Log rows: they keep the
sessions that wrote earlier versions.

Read [output-rules.md](references/output-rules.md) before writing: it defines the frontmatter
keys, ID patterns, item-block rules and allowed fields.

### 7. Validate the BRN

1. Run the validator:

   ```bash
   python3 ${CLAUDE_SKILL_DIR}/scripts/validate_brn.py docs/specs/brainstorm/BRN-NNN.md
   ```

   Run it alone, on one line: never joined to another command with `;`, `|` (or `||`) or a
   background `&`, and with no second command line (`&&` and `2>&1` are fine). Then its exit status
   is the validator's: the progress tracker credits no run whose status another command hides. It
   prints one line per problem (`line N: message`) and exits 1, or prints `OK: path` and exits 0.
   Don't run any `devforgeai` command, even if one is on PATH. Only without a shell or Python, check
   the file by hand against the *Validation checklist* in
   [output-rules.md](references/output-rules.md).
2. The validator can't know what the user confirmed. After it passes, read the file back and
   check that every disposition other than `open`, and `status: converged`, is one the user
   confirmed in step 5. Reset anything else to `disposition: open` with `reason: null`, or to
   `status: draft`.
3. Fix every problem found, then run the checks again. Stop after three attempts.
4. If errors remain after three attempts, set `status: draft`, stop, and list the remaining
   errors with the file path.

### 8. Report and hand off

Write the final reply in this order, so the facts come first and the next step is the last thing
the user reads:

1. This block, filled in. It opens the reply: nothing comes before it, not even the workflow
   checklist.

   ```
   BRN-NNN written to docs/specs/brainstorm/BRN-NNN.md (framework: [name])
   Problems: N · Ideas: N · Assumptions: N
   Promoted: IDEA-NN, … | none confirmed yet (proposals are in section 6)
   Open questions: [each NEEDS CLARIFICATION marker and open assumption, briefly]
   ```

2. Any discussion.
3. The next step, as its own paragraph outside any code block, starting with the words
   **Next step**. Nothing follows it.

For the next step, check whether `${CLAUDE_SKILL_DIR}/../prd/SKILL.md` exists:

- **It exists:** tell the user to run `/devforgeai:prd BRN-NNN` with the BRN **ID**, never a
  path, and say the BRN at its path is the input.
- **It does not exist:** say the PRD workflow (planned as `/devforgeai:prd`) is not built yet,
  and that once it is, `/devforgeai:prd BRN-NNN` (the ID, never a path) runs on this BRN.

For example: "Next step: the PRD workflow isn't built yet. Once it is, run
`/devforgeai:prd BRN-001` to turn this brainstorm into a PRD."

Do not start writing a PRD.

## Output contract

- **Path:** `docs/specs/brainstorm/BRN-NNN.md` in the current project; the name is the ID only.
- **Shape:** the template `${CLAUDE_SKILL_DIR}/assets/brainstorm.md`, with every heading kept and
  no author comments.
- **Data:** only the `problems`, `ideas` and `assumptions` collections with their defined fields
  ([output-rules.md](references/output-rules.md)).
- **Decisions:** dispositions and `status: converged` appear only when the user confirmed them.

## Examples

**Named topic, user answers.** User: "Let's brainstorm how our dental clinic could cut no-shows."
1. The skill asks up to three questions (trigger, who is affected, constraints) and picks
   `diverge-converge`, saying why.
2. It captures 3 problems, 10 ideas (two in the user's own words) and 4 assumptions, and
   rates them.
3. It proposes promote IDEA-01 and IDEA-04, park 3 ideas and reject 1. The user confirms all
   but one, which they change to parked, and says the brainstorm is done.
4. It writes `docs/specs/brainstorm/BRN-001.md` with those dispositions and `status: converged`.
5. The validator prints OK, and the skill reports and hands off.

**User says to proceed without questions.** The skill first asks the waiver question (with
AskUserQuestion); on *Ask me as usual* it asks as in the first example. On *Proceed without
questions*, or with no question tool, it asks nothing more, except the topic or extend-or-new when
open, and records unknowns as
`[NEEDS CLARIFICATION]`. It writes every idea with `disposition: open` and `status: draft`, and puts
its proposals in section 6. Its final reply says the user can confirm them later; when the user
replies, it marks step 5 again, asks the step-5 form, and only then edits the BRN, validates it
again and reports.

**A BRN on the topic already exists.** The skill shows it and asks: extend it or create a new
one. It writes nothing until the user answers.

## References

- [references/frameworks/INDEX.md](references/frameworks/INDEX.md): read at step 2 to choose a
  framework. It links to each framework file.
- [references/output-rules.md](references/output-rules.md): read before step 6.
- [references/work-file.md](references/work-file.md): read before the first save at step 3, and when
  continuing an earlier run.
- `scripts/validate_brn.py`: run at step 7; it applies the output rules mechanically.
