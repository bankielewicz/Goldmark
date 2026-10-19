# The interview

## Contents

- When to use this
- Draft first
- The batches, in order
- Writing what the answers give
- The canvas facts are never asked
- Surfaces
- Candidates in an amend run
- Answers stated in the request
- Proceed without questions
- When the user stops (ERR-13)

## When to use this

Read this at step 6 of SKILL.md, and before any other question the run asks. The interview confirms the
mappings; it fills nothing a board or a promoted idea already answers, and it never decides for the user. The
grouping into flows and the briefs are confirmed earlier, at step 3; the questions about the canvas (create it,
import it, iterate later) are in the canvas reference.

## Draft first

Before asking anything, draft from the boards and the promoted ideas, and write none of it: each board's
title, flow, surface and ideas, and each promoted idea's coverage. Then ask only what the request does not
answer. Group the boards into proposed flows by what the lines of each board show, and name a flow as a
lowercase slug (`day-to-day`).

## The batches, in order

A call holds at most 4 questions, each with 2 to 4 options, the recommended option first and marked
"(Recommended)". The user may answer in their own words. Fill each call in this order and carry the rest to
the next call. The interview is bounded by this structure, not by a call budget.
1. **One question for each flow,** showing its boards, each with the proposed title, surface and ideas, in the
   proposed step order. For a flow the user confirmed at the brief step the boards are those of its row on the
   canvas; on the copy path the skill proposes the flows from the boards. Options: confirm the group and the
   step order as shown (Recommended); change a board's flow, surface, ideas or place in the order; leave the flow
   unconfirmed. The step order is proposed from the positions `boards` printed: each flow's boards by `y`, then
   `x` ascending, and by the keys' order when a position is not a number; never the `order` list, which is
   stacking. A flow of one board has no step order to ask. A step order the request states ("Home and Report,
   in that order") counts as confirmed; one the user has not confirmed is recorded as proposed, with a
   `[NEEDS CLARIFICATION: step order of the flow <slug>]` marker in section 5.
2. **One question for the promoted ideas that no board shows.** For each: `not a screen` or `no board yet`.
   Propose `not a screen` (first, Recommended) only for an idea that names no screen, flow or user
   interface; otherwise propose `no board yet` first. An idea is recorded as `not a screen` only when the user
   said so.
3. **Amend runs only: one question for each group of changed, new or removed boards.** Show the candidates (see
   "Candidates in an amend run") that could bear on each, so the user can name the board that answers a
   candidate, or decline it. For a changed board, ask whether its mapping stands; for a removed one, whether to
   deprecate its item; for a withdrawn idea, whether to keep or drop the mapping. In an amend run with the
   Artifact tool, the import question and the offer to add a flow come before these questions (canvas
   reference).
4. **The approver and approval,** only at step 9 of SKILL.md.

Write nothing that an unanswered question affects until the answer arrives. When AskUserQuestion is not
available, ask in plain text, at the end of the reply, and end the turn. The exception is the approval offer
and the approver question of step 9 in SKILL.md: they are the last finding, before the Next step paragraph.

## Writing what the answers give

- Write a flow, surface, idea list, title or coverage status only when the user supplied or confirmed it.
- Otherwise write `null` (for a title, the file name up to its first dot; for an idea no board shows,
  `no board yet`) with a `[NEEDS CLARIFICATION: …]` marker, in the board's `notes` or in section 5.
- A mapping the user changes is written as changed. A mapping the user leaves open is written `null` with its
  marker.

## The canvas facts are never asked

The canvas URL and `canvas_version` are not an interview question: the user never saw the version, which only an
import reports. After an import they come from the Artifact tool's results (the URL of the publish result, or the
one the request gave; the identifier of the import's first read). Without an import in the run (a copy that was
already in the folder, or an amend run without the Artifact tool), record them only as the request states them,
and the date of the copy when the user gives it. A fact the request does not state is `null`, with
`[NEEDS CLARIFICATION: canvas URL and version copied]` in section 4, and no question is asked. Never take the
version from a file in the boards folder. In an amend run in which a board changed, was added or was removed
without an import (the user replaced files by hand), `canvas_version` is `null` with the marker unless the request
states one, never the old value, which would name a copy the skill no longer holds. When no board changed, keep
it.

## Surfaces

Treat graphical and terminal screens alike. Every board gets a surface of `web`, `desktop`, `mobile` or
`terminal`, confirmed by the user. A terminal screen is a command-line or other text-mode screen, designed in
Claude Design like any other board. A board whose surface the user has not confirmed has surface `null` and a
marker. A terminal board is mapped to flows and ideas, counted and reported exactly as the others are.

## Candidates in an amend run

The candidates come from the PRDs and the accepted ADRs (SKILL.md step 2), in this order: the PRDs before the
ADRs, each in document-ID order, the items of a document in document order. Put at most 4 in a call and at most
12 in a run, taking them in that order; report the rest as left for a later run. Each carries its citation: the
file, the item and the version. The user either names the board that answers it (recorded in that board's
`answers`), or declines it (recorded in `considered`), or leaves it for a later run.

## Answers stated in the request

An answer the request states counts as confirmed: a board's flow, surface and ideas, a flow's step order, which
idea names no screen, which idea has no board yet, who approves. Ask only about the rest. A canvas URL or version
the request states is recorded as stated, and never asked.

## Proceed without questions

A request that says to proceed without questions, or not to ask anything, asks nothing in this run:
- **Mappings.** In a create run, every mapping the request does not state is `null` with its marker, and so is
  every unstated canvas fact. In an amend run, an existing item's mapping the request doesn't state stays as it
  is (BEH-13; ERR-13's "unchanged"); a new board's unstated flow, surface and ideas, and `canvas_version` after a
  board changed, was added or was removed, are `null` with their markers, and its title is the file name up to
  its first dot, as in a create run (BEH-09, BEH-10). A board that was removed has its item deprecated without
  asking, because the script's fact requires it (BEH-13), and the report says so.
- **Ideas no board shows.** Every promoted idea no board shows, and that the request does not say names no
  screen, is `no board yet`; never `not a screen` on the skill's own judgement.
- **Candidates.** No candidate is put to the user. A candidate the request itself declines (by name or as a
  group), or assigns to a named board (by file name, BRD ID or title), counts as put to the user and is
  recorded: `declined:` in `considered`, or the board's `answers`. A document whose every candidate the request answered gets its `PRD-NNN@N` or `ADR-NNN@N` entry.
  Only the candidates the request leaves unanswered are reported as left for a later run, with their number, and
  no entry is written for their documents. Recorded candidates count toward the caps.
- **Nothing to change.** When the pre-check prints no fact, the links are current, the request names no change
  and only unasked candidates remain, candidates left unasked don't prevent ERR-17: write nothing, stop with
  no check and no approval offer, say the DSN is current (ERR-17's report), and say how many candidates wait
  for an interactive run.
- **Approval.** No approval is offered (an approval the request itself gives, with a name, still applies at
  step 9 of SKILL.md).

Every other step still runs. It never answers the gates: which BRN, an unconverged BRN, which of several DSNs
to amend, the confirmation ERR-15 asks for (without it, leave the DSN unchanged and stop), the confirmation of
the grouping and the briefs (no canvas is made and nothing is sent: ERR-22), and the question that stays open
after a canvas (it ends the run as "Iterate later").

## When the user stops (ERR-13)

When the user stops before the interview ends, offer to save the DSN with every unanswered mapping `null` and
marked (a create run) or unchanged (an amend run; a board that was removed is still deprecated, because the
script's fact requires it, and the report says so). On a yes, write it, then validate and report it as any
other write. With no answer to the offer, write nothing and say how to resume: run `/devforgeai:ui BRN-NNN <canvas URL>`,
printing the canvas URL in the reply and, when the run imported, the identifier of the import. The copy in the
boards folder is then recorded as it is, or, with the Artifact tool, imported again on the user's yes, and no
canvas fact is asked.
