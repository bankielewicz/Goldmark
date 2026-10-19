# The interview

## Contents

- When to use this
- The budget
- "Proceed without questions"
- What to ask, in order
- Options for a section's question
- Confirming observed facts
- Answers stated in the request
- When no answer comes
- Other questions the run may ask

## When to use this

Read this at SKILL.md step 6, and before any other question the run asks. The interview fills only
what no decision covers: a convention exists only when the user confirmed it.

## The budget

- `interview.max_calls` is resolved at step 2 (default 8; a project setting or the local preference
  file may change it). It caps the calls. Every question is asked within it: the scope and freshness
  questions, and those of ERR-04 to ERR-06, ERR-10, BEH-04, BEH-16 and BEH-17, included.
- One call asks at most 4 questions, each with 2 to 4 options, the recommended option first and
  marked "(Recommended)". The user may answer in their own words. Use AskUserQuestion when it is
  available; otherwise ask in plain text and end the turn.
- Write nothing that an unanswered question affects until the answer arrives.
- **When the budget runs out**, ask nothing more: each unasked section gets a Proposed statement with
  its marker (documents.md), and the report says the budget ran out.

## "Proceed without questions"

A request that says to proceed without questions, or not to ask anything, means no question is asked
in this run: no scope question, so nothing is inspected unless the request names paths; no freshness
question, so 90; no section questions, so each undecided section is Proposed; no approver question, so
nothing is approved unless the request names the approver. Every other step still runs.

## What to ask, in order

1. **The inspection scope**, once, when the request names no paths: "Which paths may I read to see how
   the project does things now?" Options: the folders the ARCH's deployments suggest, and "None: don't
   read any code". The answer is paths, or none (inspection.md).
2. **Observed facts to confirm** (below), batched with the next questions.
3. **One question per section that has no decision and no convention**, for the documents in the
   run's scope only, in the index's order (documents.md, "index.md"), each document's sections in the
   template's order. A convention already in an existing document counts as confirmed: don't ask about
   it again.
4. **The freshness window**, once, only when a new document is written: "How many days may an observed
   fact stay current before it is reported as stale?" Recommended: 90.

Fill each batch of up to 4 questions in this order, and stop at the budget. The questions SKILL.md
steps 3 and 4 raise (a component's kinds, deprecating a document, rewriting a failing document, where
an ambiguity entry belongs) go into the first call, before the scope question; ERR-04's question is
asked alone, since the run stops after it.

## Options for a section's question

Offer, in this order:
1. at most two options derived from the tech stack and the observed facts, the recommended one first;
2. "Leave it as a proposal" (the section gets a Proposed statement with its marker);
3. "This needs an architecture decision (ADR)" (the section gets a `[NEEDS ADR: …]` marker and the run
   hands the choice back to architecture: documents.md, "Significant choices").

That is 2 to 4 options. An answer the user writes in their own words is a convention, unless it says
the choice needs an ADR. A choice the user confirms as a convention has been judged not significant.

## Confirming observed facts

An observed fact becomes a convention only when the user confirms it. Ask, in the same batches:
"Observed <fact> (<path>): keep it as the project's convention?" with the options:
- **Keep (Recommended)**: write it as a Convention. An item keeps `observed_in` and `observed_on` as
  history and gets `basis: convention`;
- **Change**: write the user's version as a Convention;
- **Leave as observed**: it stays an Observed statement or `basis: observed` item;
- **This needs an architecture decision (ADR)**: a `[NEEDS ADR: …]` marker, as above.

## Answers stated in the request

An answer stated in the request counts as an answer, and its question isn't asked:
- conventions the request confirms ("I confirm Typer 0.12.x for the shiftlog CLI", "Keep pytest as our
  convention");
- the inspection scope ("You may read pyproject.toml and src/");
- the freshness window, the approver's name and the approval itself (SKILL.md step 9);
- that a choice is not hard to reverse or not shared by several epics, which makes a confirmed choice a
  convention.

## When no answer comes

- **No answer to a section's question:** the section stays Proposed.
- **The user stops before the interview ends** (ERR-09): offer to write the documents with every
  unanswered section as a Proposed statement or marker. With no answer to the offer, write nothing, and
  say how to resume: run the skill again.

## Other questions the run may ask

Each counts toward the budget, and each has a defined result when no answer can arrive:

| Question | When | With no answer |
|---|---|---|
| Which document, or all? (ERR-04) | The argument or request names a document outside the set; list the set's names | Write nothing |
| Which kinds does ARCH-NNN#CMP-NN have? (ERR-05) | An active component has no `kinds`; options from `user-interface`, `service`, `platform`, `api`, `relational-store`, `data-store`, `external` | The marker in architecture.md's Kinds column; no layer document for it |
| May I read `<path>`, outside the named scope? (ERR-06) | Inspection needs it | Don't read it; the statement becomes `[NEEDS CLARIFICATION: <path> is outside the inspection scope]` |
| Deprecate `<document>`? (BEH-04) | Its kind is in no current ARCH | Change nothing |
| Rewrite `<document>`, which fails its check? (ERR-10) | An existing document fails before the run changes it | Leave it unchanged |
| Where does AMB-NNN#ENT-NN belong? (BEH-16) | A narrative entry's `checked` names no section | Leave the entry unfolded and report it |
| Who is approving? (BEH-17) | The user approves without naming the approver; offer the document's owner first | Approve nothing, and say the approver wasn't named |
| How should tech-stack.md or source-tree.md continue? (ERR-12) | Its items alone would pass 500 lines; say how many items | Write nothing to that document |
