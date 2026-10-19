# Bounded inspection and evidence

## Contents

- When to use this
- The inspection scope
- Documents read by contract
- Allowed commands
- Leaving the scope
- Recording evidence
- Classification
- Insufficient evidence

## When to use this

Read this at SKILL.md step 5, before reading any code or configuration, and again when you write the
`evidence` block. Inspection is bounded so that its cost is predictable and so that observed
practice is never mistaken for policy or for a decision.

## The inspection scope

`inspection_scope` lists the components or directories **the user named**, in the request or in an
answer, as repository-relative paths (`services/auth/`, `infra/terraform/`).
- Nothing named: `inspection_scope: []`, and no code or configuration is read. Say so in section 1.
- The request relies on existing code without naming where it is ("reuse our current auth
  service"): when a user is present, ask which directories hold it. With no user, keep the scope
  empty and follow "Insufficient evidence" below.
- Never widen the scope yourself, and never infer it from the repository layout.

## Documents read by contract

These are always read, and they are not inspection: `docs/specs/prd/`, `docs/specs/arch/`,
`docs/specs/adr/`, `docs/specs/policy/` and `.claude/devforgeai.local.md`. Reach them by these fixed
paths (`Glob docs/specs/adr/ADR-*.md`, or `ls docs/specs/adr`); a missing folder means there are no
documents of that type. Never list the working directory or the repository root to find them.
Reading back the ARCH and ADRs you wrote (step 10) is validation, not inspection. The skill's own
templates and references are framework instructions, not project evidence: never record them as
evidence.

## Allowed commands

Inspection is **read-only**, and every path it reads, lists or searches is inside
`inspection_scope`.
- Prefer Read, Glob and Grep where they are available, always with a path inside the scope
  (`Grep pattern="session" path="services/auth"`).
- Bash may also run `ls`, `find`, `grep`, `cat`, `head` and `test -f`, and nothing else, with
  explicit paths inside the scope, in the documents read by contract, or in the skill's own files
  (some Claude Code builds have no Glob or Grep tool): no redirection (`>`, `>>`, `tee`), no writes,
  and no running, installing or building project code. A pipe may only feed another of these
  commands (`grep -rn session services/auth | head`).
- Never list or search the whole repository: no `find .`, no `grep -r` on `.`, and no Glob
  pattern such as `**/*` without a scoped path.

## Leaving the scope

Follow references (imports, configuration includes, file paths in configuration) only while they
stay inside the scope. When understanding the scope needs a file outside it (ERR-03):
- **A user is present:** ask before reading it, naming the path and why you need it.
  - *Yes:* read it, add its directory or file to `inspection_scope`, and record an `observed` EVD.
  - *No:* don't read it. Record an explicit unknown naming the path (below).
- **No user:** don't read it; record the unknown.

The unknown is a section 8 bullet: `[NEEDS CLARIFICATION: shared/config.js is outside the
inspection scope and was not read; it may set the session lifetime]`.

## Recording evidence

Record **every project source consulted for architecture analysis** as one EVD item:
the PRD, each other ARCH that bears on this system, each ADR that bears on this PRD's questions (it
cites one of the PRD's requirements, or resolves or may answer a DEC), each applied policy setting,
and each code or configuration file (or directory, when you read several files in it for one
finding). An ARCH read only to check coverage (SKILL.md step 4) is not evidence, and neither is the
ARCH you are amending or reviewing; a review record adds no EVD at all. When amending against a newer PRD version, add
a new PRD EVD for that version; the existing one stays unchanged.
- `source`: the document ID (`PRD-001`, `ARCH-001`, `ADR-002`), the setting (`POL-001#SET-01`), or
  the repository-relative path (`services/auth/session.ts`).
- `kind`: `prd`, `document` (an ARCH or other project documentation), `adr`, `policy` or `code`
  (source code or configuration).
- `finding`: what it shows, quoted. For a document or policy, start with the version and status
  examined: `"Version 2, status superseded (superseded_by ADR-003): chose the county Keycloak for
  sign-in."`
- `classification`: below, chosen independently of `kind`.

Policy documents that R1 skipped (not approved) and settings that didn't apply are recorded in the
resolution line only: no EVD and no link.

## Classification

| Classification | Use it for | Examples |
|---|---|---|
| `observed` | Code or configuration you directly inspected inside `inspection_scope` | `kind: code`, a session store found in `services/auth/` |
| `policy` | An approved, active, applied policy setting | `kind: policy`, `POL-001#SET-01` |
| `decided` | An ADR with `status: accepted` and no `superseded_by` | `kind: adr`, `ADR-001` |
| `context` | Any other consulted input or historical material | The input PRD (`kind: prd`); an existing ARCH (`kind: document`); a proposed, rejected, deprecated or superseded ADR (`kind: adr`); documentation whose claim the inspected code doesn't corroborate (`kind: document`) |

- `context` establishes neither implemented behavior nor an accepted decision. The PRD is still the
  requirements authority, through the links.
- **No classification resolves a DEC by itself** (readiness.md). A `decided` EVD supports means 2
  only when the user confirms the ADR answers the question; a `policy` EVD accompanies means 1.
- **Observed practice is not policy.** When inspected code uses a platform that policy doesn't
  mandate, or a different one, report it as a finding labelled "observed practice". It is not an
  error.
- An ADR that changed status since an existing ARCH recorded it gets a **new** EVD with its current
  version and status; the existing EVD stays unchanged.

## Insufficient evidence

When the evidence can't show what the request or a decision needs, say so explicitly:
- add a `[NEEDS CLARIFICATION: …]` bullet in section 8 naming what is missing and why ("No inspection
  scope was named and no code was inspected, so whether the current auth service can be reused is
  unknown");
- never recommend or record `outcome: reuse` on assumption, and never resolve a DEC from it.
