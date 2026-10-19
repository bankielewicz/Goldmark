# The context documents: set, sources and content

## Contents

- When to use this
- The document set
- Sources and statement kinds
- Significant choices and [NEEDS ADR]
- What each document is built from
- Technology items (tech-stack.md)
- Root items (source-tree.md)
- testing.md
- ui-mockups.md and approved-design records
- index.md

## When to use this

Read this at SKILL.md steps 3 and 7: which documents the project needs, where every statement may
come from, and what each document holds. The templates in `assets/` give each document's sections;
their author comments are rules too, and where this file is more specific, it governs.
`output-rules.md` gives the format.

## The document set

The **core documents** are always in the set:

| File | ID | Purpose (index row) |
|---|---|---|
| `index.md` | CTX-001 | The entry point (no row of its own) |
| `architecture.md` | CTX-002 | Components and the conventions that cut across layers |
| `tech-stack.md` | CTX-003 | Technologies, their allowed versions, and where each was chosen |
| `source-tree.md` | CTX-004 | Where code, tests, configuration and documentation live |
| `testing.md` | CTX-005 | How testing is done, and the testing policy in force |

A **layer document** is in the set for each kind that at least one active component of a current ARCH
has. A component can have several kinds.

| Kind | Documents |
|---|---|
| `user-interface` | `front-end.md` (CTX-011) and `ui-mockups.md` (CTX-017) |
| `service` | `middle-tier.md` (CTX-012) |
| `platform` | `back-end.md` (CTX-013) |
| `api` | `api.md` (CTX-014) |
| `relational-store` | `rdbms.md` (CTX-015) |
| `data-store` | `datastore.md` (CTX-016) |
| `external` | none of its own: `architecture.md` and `tech-stack.md` name the component |

- **A component with no `kinds`** (ERR-05): ask which kinds it has (interview.md). Use the answer for
  this run only, and say in the report and in the Change Log row of each layer document it adds that
  the kinds came from the user's answer and that the ARCH should record them when next amended. With no
  answer, its `architecture.md` row holds `[NEEDS CLARIFICATION: kinds of ARCH-NNN#CMP-NN]` in the
  Kinds column and `none` in the Documents column, no layer document is written for it, and the report
  names it.
- **A layer document whose kind is in no current ARCH any more** (BEH-04): report it and ask whether to
  deprecate it. On yes, it is a revision: `status: deprecated`, a Change Log row starting
  `Deprecated: <kind> is in no current ARCH` (output-rules.md), and index.md drops its row. With no
  answer, change
  nothing. A layer document whose Change Log says its kind came from an ERR-05 answer isn't reported
  while that component still has no kinds. Never delete a file.
- **A `[document]` argument**, or a request to update only one named document (BEH-01), limits the run
  to that document and index.md. The others are read, never written. A name outside the set is ERR-04.

## Sources and statement kinds

A statement is written only from these four sources, and says which one it comes from:

| Source | Label in the document | Where it comes from |
|---|---|---|
| A decision | `**Decision** (<source>):`, or an item with `basis: decision` | An accepted ADR (`ADR-NNN`, status `accepted`, no `superseded_by`), an applied POL setting (`POL-NNN#SET-NN`), or an active item of a current ARCH (`ARCH-NNN#CMP-NN`, `ARCH-NNN#DEC-NN`). Each cited with one `constrains` link at that document's version |
| A convention | `**Convention:**`, or `basis: convention` | Only what the user confirmed in this run (an answer, or the request), or a convention already in an existing document |
| Observed practice | `**Observed** (<path>, YYYY-MM-DD):`, or `basis: observed` | Read-only inspection of the named paths (inspection.md) |
| A DevForgeAI rule | `**DevForgeAI rule — <topic>:**` | The rules the templates already state, kept word for word. It names no document ID |

- **When a section has none of these**, write a **Proposed** statement: your suggestion, marked
  `**Proposed:** <suggestion> [NEEDS CLARIFICATION: confirm <what>]`. With no suggestion, write only
  the marker. A table row's Basis is then `Proposed [NEEDS CLARIFICATION: confirm <what>]`.
- **A section's Decision covers only what its source decides.** Anything else the section's template
  asks for, undecided, still gets a Proposed statement in that section (for example, front-end.md's
  framework when only the language is decided).
- **Never restate a decision without citing it, never write a decision's outcome as a convention, and
  never cite** a proposed, rejected, deprecated or superseded ADR, a draft or unapplied policy setting,
  or a deprecated ARCH item.
- **Observed practice is not a convention.** It becomes one only when the user confirms it
  (interview.md, "Confirming observed facts"). Approving a document never turns an Observed statement
  into a convention. When observed practice differs from a decision, report it as a finding
  labelled "observed practice".
- **One statement, one source.** A statement that applies to one story belongs in that story's spec.

## Significant choices and [NEEDS ADR]

ADR-004 D1's test decides whether a choice is significant: it is hard to reverse, or several epics
share it. These choices always meet it:
- a programming language or runtime;
- a datastore engine;
- a hosting or deployment platform;
- the transport between components (a message broker or queue included), or their interface style;
- a component boundary.

Any other choice meets it when the user says so. Every question about a choice offers
"This needs an architecture decision (ADR)" (interview.md), and a choice the user confirms as a
convention, in an answer or in the request, has been judged not significant by the owner.

**What decides a significant choice:** only an accepted ADR, an applied mandated-platform setting, or an
active item of a current ARCH that states it: a DEC item, or a component's `kinds`, `deployment`,
`responsibility` or `interacts_with`.
- A deployment that puts two interacting components in the same package or process decides their
  transport: an in-process call.
- Reaching a component whose engine an accepted ADR or the ARCH decides, when that engine runs inside
  the caller's process (a SQLite file the package opens), is also an in-process call, decided by that
  source.
- A choice is decided at its own level. Once the language, runtime, engine, platform or transport is
  decided, how it is used (a library, an ORM, a naming scheme, a retry policy) is a convention question
  for the interview, never a `[NEEDS ADR]`, unless the user says it needs an architecture decision.
- A field that calls the choice undecided, TBD or open decides nothing.

**When a section would state a significant choice that nothing decides** (ERR-07): write
`[NEEDS ADR: <the decision>; affects <documents>]` once, in that section, as its own bullet, and never
as a convention. A message broker or queue goes in back-end.md, section 4 (Jobs and queues). Another
document that mentions the choice names the document holding the marker (`the message broker is open:
back-end.md, section 4`) instead of repeating the marker. List the decision under "Handed back to
architecture" in the report; the next step becomes the architecture step. Then continue with the run.

## What each document is built from

| Document | Decision (cited) | Observed (inspection) | Convention (interview) | DevForgeAI rule |
|---|---|---|---|---|
| `index.md` | — | — | — | the loading rule (template) |
| `architecture.md` | the component table; the ADRs that decide a cross-cutting rule | logging, configuration and error-handling practice in the named paths | error handling, logging, configuration, security basics | — |
| `tech-stack.md` | a technology an accepted ADR chose, an applied mandated-platform setting, or an active component's `deployment` that names it | manifests and lock files: name and declared range | a technology or range the user states | the upgrade rule (template §2) |
| `source-tree.md` | an ADR that fixes a folder | folders directly inside the named paths | a layout the user confirms | — |
| `testing.md` | the resolved `testing.*` settings, each a POL setting or DevForgeAI's default | test configuration and the command a CI workflow runs | test levels per kind, naming pattern, fixtures, commands | the pass rule, investigating a failing test, test naming (template §1, §2, §5) |
| layer documents | ADRs that decide a rule of that layer | practice in the named paths | each template section without a decision | — |
| `ui-mockups.md` | — | — | the design system's location | the approved-design table is generated from stories (below) |

**architecture.md.** Section 1's table has one row per active component of each current ARCH, in ARCH
ID order, then CMP order. Its columns are Component, Item (`ARCH-NNN#CMP-NN`), Kinds, Responsibility,
Deployment and Documents. Responsibility and Deployment are quoted as the ARCH writes them. Documents
names the layer documents its kinds map to (`none` for `external`). Frontmatter holds one `constrains`
link per row's CMP item, at the ARCH's version, instead of a link to the whole ARCH. Below the table,
point to the ARCH's open DEC items by ID; never restate a DEC outcome. Sections 2 to 5 hold the
cross-cutting rules. In an existing architecture.md, add or change rows in the table's current columns:
the six-column form is for a new document.

**Layer documents.** Section 1, "Components covered", lists each active component whose kinds map to
the document, as `- ARCH-NNN#CMP-NN <name>`. Every other section follows its template comment.

## Technology items (tech-stack.md)

One item per technology: third-party software the components are built with or run on (languages,
runtimes, frameworks, libraries, databases, build, install and test tools, hosted services), and each
`external` component. The project's own packages are not technologies.

- **`name`:** the technology as its source names it, without its version (`"Python"`, `"SQLite"`).
- **`version_range`**, always quoted:
  - a range the source writes, or an exact version (three parts, or a pin such as `==0.12.3`), is
    copied as written;
  - a bare version of one or two parts, `V`, names a release line and is written `"V.x"`: "Python
    3.12" gives `"3.12.x"`, "SQLite 3" gives `"3.x"`, "pytest 8.x" stays `"8.x"`;
  - with no version, `"unpinned"`.
- **`used_by`:** the components the source or the user names ("Use Python 3.12 for every component"
  names every active non-`external` component; "Typer for the shiftlog CLI" names that component).
  Otherwise, the active components whose `deployment` or `responsibility` names the technology.
  Otherwise, every active component that isn't `external`. Always `ARCH-NNN#CMP-NN` strings.
- **`basis`:**
  - `decision`, with an item `upstream` holding a `constrains` link to the ADR
    (`{id: ADR-NNN, relation: constrains, version: N, hash: null}`), to the setting
    (`{id: POL-NNN, item: SET-NN, …}`), or to each CMP item whose deployment names it
    (`{id: ARCH-NNN, item: CMP-NN, …}`, at the ARCH's version);
  - `convention`;
  - `observed`, with `observed_in` (the file read) and `observed_on` (today);
  - `proposed`, with the marker in `notes`.
- **Deployment-named technologies.** An active component's `deployment` that names a technology makes
  an item with basis `decision` citing that CMP item: "The shiftlog Python package, installed with
  pipx" gives pipx (`"unpinned"`, used by the components whose deployments name it, one link per CMP).
- **One item per technology.** When several sources name it, write one item: cite every deciding source
  on it, take `version_range` from the source that gives a version (the narrowest, when several do),
  and use the strongest basis (decision, then convention, then observed, then proposed).
- **A confirmed observed item** keeps `observed_in` and `observed_on` as history and gets
  `basis: convention` (interview.md).

## Root items (source-tree.md)

One item per folder and component. `path` is repository-relative, uses forward slashes and ends in
`/`; never absolute, never `..`.

- **Observed roots:** each folder listed directly inside a named path, not the named path itself
  (naming `src/shiftlog/` with `cli/` inside gives `src/shiftlog/cli/`). `observed_in` is the named
  path listed, `observed_on` today.
- **`holds`:** `tests` when the path's first segment is `tests` or `test`; `docs` for `docs/`, `config`
  for `config/`; `generated` for `build/`, `dist/`, `out/` and `target/`; `fixtures` when the last
  segment is `fixtures`; otherwise `code`.
- **`component`:** the component the user names for the folder. Otherwise, the active component whose
  slug, or the last word of its slug, equals the folder's last segment (`shiftlog CLI` has the slug
  `shiftlog-cli`, which matches `cli/`; `Shift service`, `shift-service`, matches `service/`).
  Otherwise `null`.
- **The slug** of a component is its `name` in lowercase, with each run of characters other than
  `a`–`z` and `0`–`9` replaced by `-`, and leading and trailing `-` removed.
- **Proposed roots:** for each active component that isn't `external` and has no code root (observed,
  confirmed or decided), propose `src/<slug>/` (`holds: code`); for each such component with no tests
  root, propose `tests/<slug>/` (`holds: tests`). Both are `basis: proposed` with notes
  `"[NEEDS CLARIFICATION: confirm this folder]"`. They stay proposals until the user confirms them.
- At least one active `holds: code` root always exists (the schema requires it); the proposals
  guarantee it.

## testing.md

- Sections 1 and 2 (the pass rule; investigating a failing test) and section 5's naming rule are the
  template's DevForgeAI rules, kept unchanged.
- **Section 3, Testing policy in force:** one row per testing key, in this order: `testing.method`,
  `testing.coverage_metric`, `testing.coverage_threshold`, `testing.coverage_scope`,
  `testing.coverage_exclusions`, `testing.exception_approvers`. Value is the resolved value; Source is
  `POL-NNN#SET-NN` (with a `constrains` link to that setting in frontmatter) or `(default)`. Defaults:
  `tdd`; `line`; none (not enforced, reported as "no coverage threshold set"); every `holds: code` root
  in source-tree.md; the `generated`, `tests` and `fixtures` roots; the story's owner.
- **Never set a testing value here.** A value the user wants changed goes into a policy document, which
  this skill doesn't write: say so in the report.
- **Section 4, Test levels:** one row per component kind in the set, with Levels, Tests root (an active
  `holds: tests` root in source-tree.md) and a Basis never firmer than that root's basis.
- **Section 7, Running the tests:** one row per active component that isn't `external`, with Command and
  Basis.
- Name methods only as ADR-005 does (`tdd`, `test-after`, `atdd`, `bdd`, `spike-and-stabilize`);
  never describe them further.

## ui-mockups.md and approved-design records

Section 3's table is **generated** from stories, under the template's GENERATED line. Read section 3 of
each `docs/specs/story/STORY-*.md`, in story ID order. An export path lies under
`docs/specs/story/design/STORY-NNN/` and ends in `.png` or `.pdf`.

- **The fixed form:**
  `- **Approved design:** <screen or flow>; <export path>[, <export path>…]; approved <YYYY-MM-DD>; bundle <link or none>`
- **Today's form:** any other line that names one or more export paths, the word `approved` and
  exactly one date `YYYY-MM-DD`. Its row takes those paths and that date, the first `https://` link on
  the line as the bundle (or `none`), and `(not recorded)` as the screen or flow.

Try each line against the fixed form first. A line that starts `- **Approved design:**` but doesn't
parse in the fixed form is reported, never read as today's form. A section-3 line that names a path
under `docs/specs/story/design/` and fits neither form is reported by story ID and gets no row.

One row per line: Story, Screen or flow, Exports (the paths, comma-separated), Approved on, Handoff
bundle (`none` when there is none), copied as recorded. With no record, the table keeps its header and
no row. Never add a design no story records, and never link a story from ui-mockups.md (no STORY link
in its frontmatter). A rebuild that changes no row changes neither the document nor its version.

## index.md

Written last in the run (BEH-12), with exactly the sections Documents, Loading and Change Log:
- **Documents:** one row per document of the set that exists after the run, in the order of the first
  table above, then the layer documents in this order: front-end.md, ui-mockups.md, middle-tier.md,
  back-end.md, api.md, rdbms.md, datastore.md. Columns: File (a link), ID, Version (its current
  version), Kinds (`all` for core documents, the mapped kind for a layer document) and Purpose. A
  deprecated document gets no row; index.md gets none.
- **Loading:** the template's loading rule, unchanged.
- It stays within 100 lines and has no `freshness_days`. Readers cite the documents they use, never
  index.md, so an index revision affects no story or spec.
