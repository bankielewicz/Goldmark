---
name: spec-lookup
description: Looks up what a DevForgeAI project has already decided before Claude proposes, designs or changes behaviour. It searches the project's docs/specs/ (specifications, ADRs, PRDs and their recorded decisions) with a bundled script, cites each match by file and line with the document's version and status, and treats anything with no match as the user's decision to ask about, never something to build. During another DevForgeAI skill's workflow, a subagent runs the lookup instead of loading this skill. Use when about to propose, design, plan or change a feature or behaviour in a project with a docs/specs/ folder, when asked whether something was decided or specified, or to find a spec, ADR, requirement or decision by its ID (SPEC-012, ADR-006, FR-021, SPEC-012 BEH-18). Not for searching source code or general questions.
argument-hint: "[ID or term]"
metadata:
  devforgeai-id: "SKL-011"
  devforgeai-version: "1"
---

# Spec lookup

Ground every proposal in what the project decided. The project's specifications, ADRs, PRDs and their recorded
decisions live under `docs/specs/`. Before proposing, designing, planning or changing a feature or behaviour, search
them, cite what covers it, and hand everything else to the user as their decision. Never build what nothing covers
until the user decides it.

This skill reads and never changes a file: it writes and edits nothing.

## Inputs

- `$ARGUMENTS`, optional: a document ID (`SPEC-012`), a qualified item ID (`SPEC-012 BEH-18` or `SPEC-012#BEH-18`),
  a bare item ID (`BEH-18`), or a term. With an argument, search it at once. With none, search the terms of the
  request the skill was loaded for.
- The project's `docs/specs/`, read only through the bundled script.

## Tools

- **Bash**, only to run `python3 ${CLAUDE_SKILL_DIR}/scripts/find_spec.py "<query>"` from the project root (adding
  `--root <project root>` when the working directory isn't it), one query per call, each call a command of its own
  with nothing chained to it.
- **Read**, to open a cited file at the cited line when its excerpt isn't enough.
- **AskUserQuestion**, for a behaviour no spec covers.

Never decide coverage by listing, grepping or reading files instead of running the script.

## When to look up

Look up before proposing, designing, planning or changing a feature or behaviour in a project that has a
`docs/specs/` folder, and whenever asked whether something was decided or specified. Search each behaviour's key
terms and every document or item ID the request names.

**During another DevForgeAI skill's workflow** (a brainstorm, prd, architecture or other `devforgeai` skill run in
progress in this session), don't load this skill: loading a plugin skill ends that skill's tracked run. Hand the
lookup to the plugin's lookup agent instead, `devforgeai:spec-lookup`, which runs the same script with only Bash and
Read:

1. Give it a self-contained task message, since a subagent sees none of this conversation, its skills or the files
   already read, in three parts: `Script:` the script's absolute path (the running skill's base directory, then
   `../spec-lookup/scripts/find_spec.py`), `Project root:` the absolute project folder, and `Queries:` one per line. The template is in
   [references/citing.md](references/citing.md).
2. Wait for its report before proposing or asking about the behaviour. A subagent may run in the background; don't
   go on without its report.
3. Cite only lines of the form `path:line` from the report. The harness may prepend a line starting `[harness:` or
   escape text in a report; such lines are no citations.

Outside another skill's workflow, run the lookup here, in the main conversation: a subagent adds a fresh context, a
model call and a delay.

## Workflow

1. **Choose the queries.** List each behaviour the request asks to propose, design or change. For each, take every
   document or item ID the request names, and two or three key terms (`password reset`, `offline mode`).
2. **Search** (BEH-02). Run the script once per query, as a command of its own:

   ```bash
   python3 ${CLAUDE_SKILL_DIR}/scripts/find_spec.py "SPEC-012 BEH-18"
   ```

   Read its exit status and last line, the coverage line, which says what was searched in every case:
   - exit 0: hits, then the coverage line `<n> hits; searched <k> files under docs/specs/ (<folders>)`;
   - exit 1 with `no match: "<query>" is in none of …`: nothing matched this query; go to step 4 before treating the
     behaviour as uncovered;
   - exit 1 with `no docs/specs/ folder under <DIR>: nothing is specified yet`: the project specifies nothing. Say in
     the reply that nothing is specified yet, and treat every behaviour as the user's decision (step 5 for each);
   - exit 2, or the script can't run (no `python3`): say so, quoting its message, and treat every behaviour as
     uncovered (step 5), with no search and no citation;
   - `<n> more hits: narrow the query`: search again with a qualified ID or more words before citing.
3. **Cite each covered behaviour** (BEH-03) as `path:line (DOC vN, status)`, adding the item's ID and status for an
   item, in the reply before or with the proposal. A hit covers a behaviour only when its line states or decides it:
   a shared word is no coverage (a search for `mode` that hits "enforce mode" says nothing about a dark mode). When
   the excerpt doesn't settle it, Read the line before citing it. A hit in a document that isn't approved or accepted, on a
   deprecated item, or in a superseded document is cited with that status, so the reader sees that it isn't in
   force.
4. **Search a second time before treating anything as uncovered** (BEH-04). A search with no match permits asking,
   never a claim that something was never discussed. Search again with at least one other form: a synonym, the bare
   item ID, or the document ID. Report the coverage line of each search.
5. **Hand uncovered behaviour to the user** (BEH-05). A behaviour with no covering hit after step 4 is "not in any spec: the
   user's decision". Ask the user about it, with AskUserQuestion when available and in plain text otherwise, and
   never build, write or plan it as decided until the user answers. When no user can answer, or the request says
   to proceed without questions, put this in the reply for each such behaviour, and build and write nothing for it:

   ```text
   [NEEDS CLARIFICATION: not in any spec: <behaviour>]
   ```

## Decisions that belong to the user

- Every behaviour no spec, ADR or recorded decision covers. The skill finds what was decided; it decides nothing.
- Whether a draft, deprecated or superseded hit should guide the work: cite it with its status and let the user
  say.

## Output contract

- Each covered behaviour: one citation `path:line (DOC vN, status)` (with `ITEM item-status` for an item), then what
  it says, then the proposal it grounds.
- Each search: its coverage line, so the reader sees what was searched.
- Each uncovered behaviour: "not in any spec: the user's decision" and a question, or the
  `[NEEDS CLARIFICATION: not in any spec: <behaviour>]` marker when nobody can answer or the request says to proceed
  without questions. Never "this was never discussed".
- No file is created or changed.

## Examples

Covered: asked to propose how password resets work, the script finds `docs/specs/spec/SPEC-004.md:27  SPEC-004 v3
approved  BEH-05 active  "Reset links expire after 30 minutes and work once."`. The reply cites
`docs/specs/spec/SPEC-004.md:27 (SPEC-004 v3, approved; BEH-05 active)` and proposes single-use links that expire
after 30 minutes.

Uncovered: asked to add an offline mode, `"offline mode"` and then `offline` both print `no match`. The reply reports
both coverage lines, says an offline mode is not in any spec, so it is the user's decision, and asks; with no user, it
marks `[NEEDS CLARIFICATION: not in any spec: offline mode]` and builds nothing.

## References

- [references/citing.md](references/citing.md): the output line format, citation forms, the lookup agent's task
  message, and more examples.
