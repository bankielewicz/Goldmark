# Read-only inspection

## Contents

- When to use this
- The scope
- Inputs read by contract
- Allowed commands
- What to read
- Recording observed facts
- Leaving the scope

## When to use this

Read this at SKILL.md step 5, before reading any code or configuration. Inspection is bounded so that
its cost is predictable and so that observed practice is never mistaken for a decision or a
convention.

## The scope

The scope is the list of paths **the user named**, in the request ("You may read pyproject.toml,
src/shiftlog/ and tests/") or in answer to the scope question (interview.md). Nothing named: inspect
nothing, and the report says `Inspection: none (no paths named)`. Never widen the scope yourself, and
never infer it from the repository layout or the ARCH.

## Inputs read by contract

These are not inspection, and they are read whatever the scope: the current ARCHs, the PRDs they link,
accepted ADRs, policy documents and `.claude/devforgeai.local.md`, stories and specs (for their
section 3 design records and their `upstream` links to CTX documents), ambiguity logs, and the context
documents with their detail files. Reach them by their fixed paths (`Glob docs/specs/adr/ADR-*.md`, or
`ls docs/specs/adr`); a missing folder means there are none. Listing `docs/specs/context/` is part of
step 4. Never list the working directory or the repository root to find anything. The skill's own
templates and references are framework instructions, not project facts.

## Allowed commands

Inspection is read-only, and every path it reads, lists or searches is inside the scope.
- Use Read, Glob and Grep when they are available, always with a path inside the scope.
- Otherwise use only `ls`, `find`, `grep`, `cat` and `head` with explicit paths inside the scope: no
  redirection (`>`, `>>`, `tee`), no writes, and no running, installing or building project code. A
  pipe may only feed another of these commands.
- Never list or search the whole repository: no `find .`, no `grep -r` on `.`, and no Glob pattern such
  as `**/*` without a scoped path.

## What to read

Within the scope:
- dependency manifests and lock files, for example `package.json`, `pyproject.toml`,
  `requirements*.txt`, `go.mod`, `Cargo.toml`, `*.csproj`, `pom.xml`, `build.gradle*`, `Gemfile`,
  `composer.json`: each third-party package's name and declared range becomes a technology item
  (documents.md);
- test configuration (`pytest.ini`, `jest.config.*`, `[tool.pytest]`) and the command a CI workflow
  runs: testing.md;
- logging, configuration and error-handling practice: architecture.md and the layer documents;
- the folders directly inside each named folder: each becomes a root item (documents.md, "Root
  items"). A named file is read, not listed.

## Recording observed facts

Record each fact as observed, with the path read and today's date:
- on an item: `basis: observed`, `observed_in: "<path>"`, `observed_on: <today>`;
- in prose: `**Observed** (<path>, <today>): <fact>`;
- in a table's Basis column: `Observed (<path>, <today>)`.

Each observed fact is then offered for confirmation (interview.md); under "Proceed without
questions", a fact the request already confirms becomes a convention, and every other one stays
observed. The report's Inspection line lists every path read.

## Leaving the scope

Follow an import or reference only while it stays inside the scope. When a statement needs a path
outside it (ERR-06), ask before reading it. When the user declines, or no answer can arrive, don't
read it, and write the affected statement as `[NEEDS CLARIFICATION: <path> is outside the inspection
scope]`.
