# The work file

The brainstorm as it stands, saved as the session goes, so that a run the progress tracker
continues later has the problems, ideas, scores and proposals, not only the step it stopped at. It is
a work copy, never the BRN: nothing downstream reads it, the validator doesn't check it, and the
skill never deletes it (the progress tracker does, once the BRN validates, and removes old ones).

## Contents

- [Where it lives](#where-it-lives)
- [When to write it](#when-to-write-it)
- [Its shape](#its-shape)
- [Continuing a run](#continuing-a-run)

## Where it lives

The path SKILL.md's *Inputs* gives: the draft the progress tracker's line on continuing names, else
`devforgeai/drafts/brainstorm/<session ID>.md`, with the same session ID as the BRN's
`generated_by.session`. Never under `docs/specs/`: the progress tracker treats a write there as the
BRN's own (step 6) and would refuse it in enforce mode.

Before the first write, when `devforgeai/drafts/.gitignore` is missing, write it with the single line
`*`, so no work file reaches git.

## When to write it

Write the whole file with the Write tool, three times:

1. After marking step 3 completed and before marking step 4 in progress: problems, ideas and
   assumptions.
2. After marking step 4 completed and before marking step 5 in progress: the evaluation and scores
   added.
3. With step 5 in progress, once the proposals are made: before asking step 5's question, or, when no
   question is asked (the waiver's *Proceed without questions*, or no AskUserQuestion), right away.

When the user stops and agrees to save a draft BRN (ERR-02), first write the work file (again, or for
the first time) with its `id` set to the ID the draft BRN gets, so a continued run extends that BRN
instead of allocating another; then write the draft BRN, and don't run the validator on it: a
validated BRN tells the progress tracker the work file is done with, and it deletes the file.

Once the BRN is written (step 6), don't save the work file again, even when step 5 is asked again
for a confirmation that comes later.

## Its shape

Build it from `${CLAUDE_SKILL_DIR}/assets/brainstorm.md`, as step 6 builds the BRN, except:

- `id`: `BRN-000` (the template's placeholder) for a new BRN. When extending an existing BRN, or after
  ERR-02 saved a draft BRN, that BRN's ID: this is what tells a continued run to extend it.
- `title`: the topic. `status: draft`. `version`: 1 for a new BRN, or the extended BRN's current
  version plus one.
- `generated_by.session` and the Change Log: this session's ID and one row for this session, as step 6
  writes them; when extending, the BRN's existing rows are kept above it. Every save sets both to this session.
- Every idea `disposition: open`, `reason: null`. A proposal is never a disposition value.
- Sections not reached yet hold `[NEEDS CLARIFICATION: not reached]`.
- Step 5's proposals, once made, as a table in section 6 (Convergence): idea ID, title, proposed
  disposition, reason. Write it in table cells only, never as a `disposition:` line.
- No HTML comments.

When extending, start from the existing BRN's content with every item ID kept.

## Continuing a run

When the progress tracker's line that continues an earlier brainstorm run says "Its draft is <path>":

1. Load that file and save to it from then on, at the same points as above (the saves before step 4
   and step 5 are made only when the run reaches those points). Keep its problems, ideas, assumptions,
   scores and IDs. A draft that can't be read counts as none: continue as the line says.
2. When its `id` is a BRN's ID, step 6 extends that BRN (version + 1, a Change Log row for this
   session) without asking extend-or-new again; otherwise step 6 allocates the ID as usual.
3. Follow every other sentence of the line as written: the tracker decides which step is asked and
   with which proposals.

A run with no such line reads no work file, and never reads another session's.
