---
id: CTX-005
type: context
title: ""              # e.g. "<project>: testing"
status: draft          # draft | approved | superseded | deprecated
version: 1
created: YYYY-MM-DD
updated: YYYY-MM-DD
owner: ""
authors: []
generated_by:
  tool: ""
  model: ""
  session: ""
reviewed_by: []
approved_by: ""
approved_on: null
upstream:              # constrains links, at their versions: each ARCH this document relies on,
                       # and the source of each Decision a prose statement cites
                       # (ADR-NNN; POL-NNN with item SET-NN; ARCH-NNN with item CMP-NN)
  - {id: ARCH-000, relation: constrains, version: 1, hash: null}
supersedes: []
superseded_by: null
blocked_by: []
# --- context-specific ---
document: testing        # fixed: docs/specs/context/testing.md is always CTX-005
freshness_days: DAYS   # days; observed facts older than this are reported as findings, not errors
---

# CTX-005 — Testing

<!-- Project context (DevForgeAI). How testing is done in this project: the pass rule, the testing
     policy in force, test levels per component kind, naming, fixtures, and how to run the tests.

     BELONGS HERE: the application of the testing policy, test levels per component kind, test naming,
     fixtures and test data, and the commands that run the tests.
     DOES NOT: a policy value (the method, the coverage metric and threshold, its scope and exclusions,
     and who may approve an exception come from POL settings or DevForgeAI's defaults, and are only
     cited here); one story's test plan (its spec).

     STATEMENTS. Write every narrative rule or fact as one bullet of one of these kinds:
       - **Decision** (<source>): …  decided by an accepted ADR (ADR-NNN), an approved POL setting
         (POL-NNN#SET-NN) or an ARCH item (ARCH-NNN#CMP-NN). The parentheses name the source, and
         frontmatter `upstream` holds one `constrains` link to that same document and item, at its
         version. Never restate a decision without citing it.
       - **Convention:** …  a rule the user confirmed while this document was written.
       - **Observed** (<path>, YYYY-MM-DD): …  found by read-only inspection and not yet confirmed.
         It becomes a convention only when the user confirms it; approving the document doesn't.
       - **Proposed:** … [NEEDS CLARIFICATION: confirm …]  suggested and not confirmed.
       - **DevForgeAI rule — <topic>:** …  a rule the DevForgeAI framework fixes. It
         carries no document ID and no link.
     TABLES. Each table this template defines says where its rows come from. A table of rules has a
     Basis column holding the same kinds: Convention, Observed (<path>, YYYY-MM-DD), Proposed with its
     marker, or a Decision's source. A row that restates another context document (a tests root from
     source-tree.md) is never firmer than its source there.
     IDS. A bare ADR-, POL-, ARCH-, STORY- or SPEC- ID always means this project's own document. Other
     context documents are named in prose ("tech-stack.md, TEC-02"), and a Markdown hyperlink to them
     is fine, but they never get an `upstream` link record.
     SIGNIFICANT CHOICES. A choice that is hard to reverse, or shared by several epics, and has no ADR
     is never written as a convention: write [NEEDS ADR: <decision>] and hand it back to Architecture
     Definition.
     ONE STORY. A statement that applies to one story belongs in that story's spec, not here.
     RETIRING. A statement is deleted from the text. An item is never deleted or renumbered: set its
     `status` to deprecated and say why in `notes` (for a rejected proposal: who rejected it, and when).
     MARKERS. The document can't be approved while any [NEEDS CLARIFICATION: …] or [NEEDS ADR: …]
     marker, or any active Proposed statement, remains anywhere in it: frontmatter, text, tables,
     items, or its detail files.
     SIZE. At most 500 lines. Past 100 lines, start with a "## Contents" list. Detail that only some
     work needs moves to a detail file, docs/specs/context/testing/<topic>.md (template detail.md),
     linked from this document with one line saying when to read it:
       - [<topic>](testing/<topic>.md): read when <condition>.
     A detail file never links another detail file.
     CHANGES. Once approved, any change raises `version`, updates `updated`, adds a Change Log row, and
     sets `status` back to draft with the approval cleared until the owner approves again. Stories and
     specs that cite this document are proposals meanwhile.
     Delete these comments when you fill in the document. -->

## 1. The pass rule

- **DevForgeAI rule — pass rule:** 100% of the required tests pass. A failing test is accepted
  only as a named exception that records the test, the reason and the approver, and it is always
  reported apart from "tests passed". This is not a configurable threshold.

## 2. Investigating a failing test

- **DevForgeAI rule — investigating a failing test:** a failing test is never investigated by
  changing the working tree.
  1. Before the first change for a work item, run the required tests on the unchanged branch and record
     the results with the commit they ran on. Compare a later failure with that record first.
  2. To run or read the old code, use a separate, disposable worktree at that commit
     (`git worktree add --detach <path> <commit>`, removed afterwards), or `git show <commit>:<path>`.
     Never restore, check out, stash, reset, clean or download files into the working tree to
     investigate a failure.
  3. Commit the work in progress on the work item's branch before each full run of the required tests.
  4. A failure already present in the baseline is reported as present before the change. The pass rule
     still applies to it: it is fixed, or accepted as a named exception.

## 3. Testing policy in force

<!-- One row per testing setting, as resolved from policy. Value: the resolved value. Source: the POL
     setting that set it (POL-NNN#SET-NN, with a `constrains` link in frontmatter) or "(default)" for
     DevForgeAI's default. Never set a value here; change the policy document instead. -->

| Setting | Value | Source |
|---|---|---|
| testing.method | <value> | <POL-NNN#SET-NN or (default)> |
| testing.coverage_metric | <value> | <source> |
| testing.coverage_threshold | <value, or none> | <source> |
| testing.coverage_scope | <value> | <source> |
| testing.coverage_exclusions | <value> | <source> |
| testing.exception_approvers | <value> | <source> |

## 4. Test levels

<!-- For each component kind the ARCH has: the test levels used and the tests root, which is an active
     `holds: tests` root in source-tree.md. Basis: as in the TABLES rule; never firmer than the root's
     basis in source-tree.md. -->

| Kind | Levels | Tests root | Basis |
|---|---|---|---|
| <kind> | <e.g. unit, integration> | <folder>/ | Convention |

## 5. Naming

<!-- How a test cites what it verifies. Keep the framework rule; add the project's exact pattern. -->

- **DevForgeAI rule — test naming:** each test's name or tag cites the acceptance
  criterion or verification item it covers, for example `test_STORY_NNN_AC_NN_<what>` or
  `@STORY-NNN @AC-NN`.
- **Convention:** <the project's exact pattern>

## 6. Fixtures and test data

<!-- Where fixtures live, how test data is created and cleaned up. -->

- **Convention:** <…>

## 7. Running the tests

<!-- The command that runs each component's tests, from the repository root. Basis: as in the TABLES
     rule. -->

| Component | Command | Basis |
|---|---|---|
| ARCH-000#CMP-01 | `<command>` | Convention |

## Change Log

| Version | Date | Author | Change | Items affected |
|---|---|---|---|---|
| 1 | YYYY-MM-DD | | Initial draft | all |
