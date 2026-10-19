# ARCH and ADR output rules

## Contents

- When to use this
- File and IDs
- ARCH frontmatter
- Link records
- Item blocks
- Collections and fields
- Sections
- Markers
- Change Log
- Amending an ARCH
- The review record
- ADRs
- Recording a supersession
- When validation still fails (ERR-05)
- Self-check list
- Example item blocks

## When to use this

Read this before writing (SKILL.md step 9), and check every written file against the self-check list
(step 10): one initial check, then at most three repair cycles. The epic workflow parses the ARCH
mechanically, and a future checker validates it against `arch.schema.json` and ADRs against
`adr.schema.json`. An ARCH that reads well but breaks a rule here
breaks readiness for every epic.

## File and IDs

| What | Pattern | Rule |
|---|---|---|
| ARCH document | `ARCH-NNN`, at `docs/specs/arch/ARCH-NNN.md` | Highest existing number plus one; `ARCH-001` if none. Named by the ID only; the system name goes in `title` and `system` |
| ADR document | `ADR-NNN`, at `docs/specs/adr/ADR-NNN.md` | Highest existing number plus one; `ADR-001` if none |
| Component | `CMP-NN` (two digits) | Unique, numbered in order from `CMP-01` |
| Architectural question | `DEC-NN` (two digits) | Unique, numbered in order from `DEC-01` |
| Evidence | `EVD-NN` (two digits) | Unique, numbered in order from `EVD-01` |

IDs are never reused, renumbered or deleted. An amendment continues each collection from its highest
number. In prose, refer to an item by its qualified form: `ARCH-001#DEC-02`, `PRD-001#FR-001`.

## ARCH frontmatter

Keep exactly these keys, in this order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | `ARCH-NNN`, equal to the file name |
| `type` | `arch` |
| `title` | Quoted: `"<system> architecture"` |
| `status` | `draft` for a new ARCH; see "Amending an ARCH". Never set `approved` (a review record leaves an approved ARCH approved) |
| `version` | Integer: `1` for a new ARCH, plus one per amendment |
| `created`, `updated` | Unquoted `YYYY-MM-DD` dates |
| `owner` | Quoted: the name the request gives, otherwise the PRD's `owner` |
| `authors` | Quoted names: the owner and `"claude-code"` |
| `generated_by` | Map with quoted, non-empty `tool` (`"claude-code"`), `model` and `session` |
| `reviewed_by` | `[]` for a new ARCH; kept otherwise (humans only, never filled by the AI) |
| `approved_by`, `approved_on` | `""` and `null` for a new ARCH |
| `upstream` | Link records, one per line (next section) |
| `supersedes` | `[]` |
| `superseded_by` | `null` |
| `blocked_by` | `[]` |
| `system` | Quoted name of the system or product the description covers |
| `outcome` | `reuse`, `amend`, `create` or `null`. Non-null only when the user confirmed it at step 8, or the request named it and said to proceed without questions |
| `inspection_scope` | Block list of quoted repository-relative paths the user named, or `[]` |

Keep the `# --- arch-specific ---` comment line before `system`. Delete the template's trailing
comments on frontmatter lines.

## Link records

A link is written in flow form, one per line:
`- {id: PRD-001, item: FR-001, relation: informed_by, version: 1, hash: null}`.
- Fields: `id`, optional `item`, `relation`, `version` (the cited document's version), `hash`, and an
  optional quoted `note`. No other field.
- `hash` is always `null`. Only the checker writes hashes.
- A link lives in **exactly one place**:

| Link | Where | Form |
|---|---|---|
| The PRD examined | ARCH frontmatter, one link per PRD the ARCH covers | `{id: PRD-NNN, relation: informed_by, version: <PRD version>, hash: null}`, this run's PRD at the version examined |
| A requirement a question affects | The DEC | `{id: PRD-NNN, item: FR-NNN, relation: informed_by, …}` (or `NFR-NNN`) |
| A quality driver or constraint a component serves | The CMP | `{id: PRD-NNN, item: NFR-NNN, relation: informed_by, …}` |
| A mandated platform setting | The CMP that provides that capability, never frontmatter | `{id: POL-NNN, item: SET-NN, relation: constrains, version: <policy version>, hash: null}` |
| `interview.max_calls` or `quality.required_categories` from policy | ARCH frontmatter | `{id: POL-NNN, item: SET-NN, relation: informed_by, version: <policy version>, hash: null}` |
| A requirement an ADR decides | The ADR's frontmatter | `{id: PRD-NNN, item: FR-NNN, relation: informed_by, …}` |

ADRs are never linked from the ARCH: they appear in `resolved_by` and as EVD items. Never link a
default, a local value, a deprecated or non-applicable setting, or an ignored policy document.

## Item blocks

- An item block is a fenced block whose info string is exactly `yaml items`.
- Each fence holds **exactly one top-level key**: `components`, `decisions` or `evidence`.
- Inside a fence use `#` comments only, and delete the template's comments.
- **Quote every free-text value** with double quotes. IDs, enum values, booleans, `null` and
  numbers are not quoted, except in `source` and in the `kinds` and `interacts_with` lists, which are
  always quoted.
- Link records and `resolved_by` use flow form; every other list is a block list, or `[]` when empty.

## Collections and fields

Every item has `id` and `status` (`active` or `deprecated`), may have `superseded_by` (an item ID),
and has only the fields below, in this order.

**`components`** (at least one):

| Field | Value |
|---|---|
| `name` | Quoted |
| `kinds` | Block list of one or more quoted kinds from the table below. Leave it out only when no kind is certain, with a `[NEEDS CLARIFICATION: kinds of CMP-NN]` marker in section 8 |
| `responsibility` | Quoted: what it is responsible for, and what it is not |
| `owns_data` | Block list of quoted data it is the owner of, or `[]` |
| `interacts_with` | Block list of quoted `CMP-NN` IDs or external systems, or `[]` |
| `deployment` | Quoted deployment unit, or `"Open: see DEC-NN"` when a DEC about deployment is open |
| `upstream` | Optional links to the NFRs and POL settings it serves |

Component kinds (SPEC-003 §4; they select the project context documents, ADR-004 D2):

| Kind | A component that is… |
|---|---|
| `user-interface` | a surface people use: web, desktop, mobile or CLI |
| `service` | application or business logic |
| `platform` | background jobs, workers, integrations with external systems, hosting |
| `api` | an interface exposed to another component or to external consumers |
| `relational-store` | a relational database |
| `data-store` | a non-relational store: document, key-value, object, search or cache |
| `external` | a system outside the project, such as a mandated identity platform |

**`decisions`**:

| Field | Value |
|---|---|
| `question` | Quoted, one architectural question |
| `blocking` | `true` unless the user said otherwise |
| `state` | `open` or `resolved` |
| `resolved_by` | Flow list: `[]` when open; `[ADR-NNN]` or `[POL-NNN#SET-NN]` (non-empty) when resolved |
| `notes` | `null` or quoted (stated preferences, trade-offs discussed, why it is open) |
| `upstream` | At least one link to an affected requirement |

**`evidence`** (at least the PRD):

| Field | Value |
|---|---|
| `source` | Quoted document ID, setting ID or repository-relative path |
| `kind` | `code`, `document`, `adr`, `policy` or `prd` |
| `finding` | Quoted; for documents and policy, starting with the version and status examined |
| `classification` | `observed`, `policy`, `decided` or `context` (inspection.md) |

## Sections

Keep these headings, in this order, exactly as the template has them:
`## 1. Context and scope`, `## 2. Quality drivers`, `## 3. Components`,
`## 4. Architectural questions`, `## 5. Evidence inspected`, `## 6. Deployment`,
`## 7. Requirement changes proposed to the PRD owner`, `## 8. Open questions`, `## Change Log`.

- **1.** What the system is for, what is inside and outside it, the PRD ID, version and status
  examined, and the inspection scope (or that none was named and no code was inspected).
- **2.** Prose: the NFRs and constraints that shape the design, by ID, and the required quality
  categories (the floor for the PRD's operating context plus applicable policy) that no NFR covers.
- **3.** A Mermaid `flowchart` of the components, then the `components` block.
- **4.** The `decisions` block. Readiness is not stored: it is computed and reported in the reply
  (readiness.md).
- **5.** The `evidence` block.
- **6.** Prose, optionally Mermaid: deployment units and environments, or the DEC that leaves
  deployment open.
- **7.** A bullet per proposed PRD change, addressed to the PRD owner: the requirement ID, the
  problem (infeasible, too costly or conflicting, and with what), and the change proposed. Write
  `- None.` if there are none.
- **8.** A bullet per `[NEEDS CLARIFICATION: …]` marker. Write `- None.` if there are none.

No `<!--` comment is left anywhere in the file.

## Markers

| Marker | Means |
|---|---|
| `[NEEDS CLARIFICATION: <question>]` | Missing evidence or an unknown the user must answer (inspection.md) |
| `state: open` on a DEC | An architectural question nobody has settled yet |
| `outcome: null` | The user hasn't confirmed reuse, amend or create |

## Change Log

One row per write: version, date, author, change, items affected. The author of a row you write is
`claude-code (session ${CLAUDE_SESSION_ID})`, this session's ID (for a new or amended ARCH, the same
ID as `generated_by.session`). The change text ends with the
policy resolution line (policy.md), with no `|` inside it:

```markdown
| 1 | 2026-09-28 | claude-code (session 7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f) | Initial draft for PRD-001 v1. Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default) | all |
```

Log each DEC transition in the row, in one of these forms:
- `DEC-01 resolved → open: ADR-002 superseded by ADR-003`;
- for a mandated platform that changed (readiness.md), quoting both platforms exactly:
  `DEC-01 resolved → open: POL-001#SET-01 now mandates <platform now> (was <platform recorded>)`;
- `DEC-02 open → resolved: ADR-007 accepted (decided by <name>)`, or
  `DEC-01 open → resolved: POL-001#SET-01 as it now stands (confirmed by <name>)`;
- `DEC-01 resolved_by ADR-001 → ADR-008 (supersession approved by <name>)`.

Never edit earlier rows.

## Amending an ARCH

Only after the user chose to amend it:
- Raise `version` by one, set `updated` to today, set `generated_by` to this session, and add
  `"claude-code"` to `authors` if missing. Keep `reviewed_by`.
- `status`: keep `draft` or `in-review`. An `approved` ARCH becomes `in-review`, with
  `approved_by: ""` and `approved_on: null`.
- Move this PRD's frontmatter link to its current version, or add one if the ARCH covered the system
  through another PRD. Links on existing items keep their versions; links you add use the current
  version.
- `outcome`: the confirmed outcome (`amend`), or `null` if not confirmed.
- New items take the next free number in each collection. Existing items stay **byte-identical**,
  except a DEC's `state` and `resolved_by` (readiness.md, "State changes when amending"); log each
  transition in the Change Log row.
- Add one Change Log row naming what changed, ending with the resolution line.

## The review record

When the user confirms **reuse** and the ARCH's frontmatter PRD link cites an older PRD version, make
exactly three changes:
1. The frontmatter PRD link's `version` becomes the PRD's current version.
2. `outcome: reuse`.
3. One Change Log row whose version column repeats the ARCH's current version and whose change text is
   `Reviewed against PRD-NNN vN: reuse confirmed, no architectural change. Policy resolution: …`,
   with items affected `none`.

Everything else stays byte-identical: `version`, `updated`, `status`, `generated_by`, `authors`, the
approval fields and every item with its links (which still cite the older version, so they show as
suspect). It is a relink, not an amendment: an approved ARCH stays approved.

When the link already equals the PRD's version, confirming reuse writes nothing.

## ADRs

Write each ADR from `${CLAUDE_SKILL_DIR}/assets/adr.md`, keeping its headings and deleting every
`<!-- -->` comment and placeholder.

| Key | Accepted (the user decided) | Proposed (the user deferred and asked to record it) |
|---|---|---|
| `title` | Quoted decision stated as a result: `"Use the Org B Keycloak realm for sign-in"` | Quoted open question: `"Choose the identity provider for sign-in"` |
| `status` | `accepted` | `proposed` |
| `version` | `1` | `1` |
| `created`, `updated` | Today | Today |
| `owner` | The ARCH `owner` | The ARCH `owner` |
| `authors` | The deciding user and `"claude-code"` | The owner and `"claude-code"` |
| `generated_by` | This session, as for the ARCH | Same |
| `reviewed_by` | `[]` | `[]` |
| `approved_by` | Quoted name of the user who decided | `""` |
| `approved_on` | Today | `null` |
| `upstream` | The requirements the DEC cites, same versions | Same |
| `supersedes` | `[]`, or `[ADR-old]` for an approved supersession | `[]` |
| `superseded_by`, `blocked_by`, `consulted`, `informed` | `null`, `[]`, `[]`, `[]` | Same |

The body records the DEC's question (cite it as `ARCH-NNN#DEC-NN`), the drivers, every option
presented with its pros and cons, and the chosen option with the user's reason. "Confirmation" says
how the choice will be checked (for example, in the spec of the first epic that uses it). In a
proposed ADR, "Decision outcome", "Consequences" and "Confirmation" each say `Not decided yet.`,
and the DEC's `notes` name it ("Deferred: ADR-005 (proposed)"). The Status history has one row:
today, the status, and `Decided by <name> in session <ID>` or `Deferred by <name>`.

When a question with a proposed ADR is later decided, write the decision as a new accepted ADR that
supersedes the proposed one (next section); the user's decision is the approval.

## Recording a supersession

Only when the user explicitly approves replacing an accepted ADR (BEH-16), or decides a question
whose deferral a proposed ADR records (the user's decision is the approval):
- the **old** ADR changes only `status: superseded`, `superseded_by: ADR-new`, and one Status history
  row (`superseded by ADR-new`). Nothing else changes, including its version;
- the **new** ADR is accepted with `supersedes: [ADR-old]`;
- the DEC's `resolved_by` becomes `[ADR-new]` (from `[ADR-old]`, or from `[]` when the old ADR was
  proposed), logged in the ARCH Change Log;
- the old ADR is recorded as a new EVD with `classification: context`.

Never modify an existing ADR in any other way, and never modify a PRD, BRN or policy document.

## When validation still fails (ERR-05)

When errors remain after the initial check and three repair cycles, or an error can't be repaired,
stop. Never leave `approved` or `accepted` on content that failed validation. Keep the user's
architectural choices (the ADR text) and unrelated content:
- **The ARCH.** A new ARCH stays `draft`. An amended ARCH keeps the status the amendment gave it
  ("Amending an ARCH"): `draft` and `in-review` stay, and an approved ARCH stays `in-review` with
  `approved_by: ""` and `approved_on: null`. Never restore `approved`. A review record that fails
  validation is treated the same way: an approved ARCH becomes `in-review` with `approved_by: ""`
  and `approved_on: null`, and a draft or in-review ARCH keeps its status.
- **ADRs accepted in this run.** Each becomes `status: proposed` with `approved_by: ""` and
  `approved_on: null`, and is kept. Each DEC it resolved returns to `state: open`,
  `resolved_by: []`.
- **A supersession recorded in this run is rolled back.** When such an ADR superseded an existing
  ADR:
  - restore the older ADR byte-for-byte to its state before the run: its `status`,
    `superseded_by`, Status history and every other byte, as if this run never touched it;
  - clear the replacement's `supersedes` to `[]`, as well as its approval fields. Keep the intended
    replacement and the user's decision in its prose: add to "Decision outcome" the sentence
    `Intended to supersede ADR-NNN, as <name> decided in session <ID>; not in force, because
    validation failed (ERR-05).`;
  - leave each DEC that depended on the replacement open with `resolved_by: []`. Never reconnect it
    to the older ADR, even though that ADR is restored;
  - set the EVD item this run added for the older ADR ("Recording a supersession") to
    `status: deprecated`, and leave its other fields as written. Never delete it: IDs are never
    deleted ("File and IDs").
- **Audit records.** An ARCH Change Log row
  (`Validation failed (ERR-05): <the unresolved errors, briefly>. Left <status>.`, adding
  `Approval cleared.` when the ARCH was approved, and `Supersession of ADR-NNN rolled back. EVD-NN
  deprecated: it recorded a supersession no longer in force.` when one was), and for each ADR made
  proposed a Status history row: `Restored to proposed: validation failed`, or, for a replacement,
  `Restored to proposed: validation failed; the supersession of ADR-NNN that <name> approved (or
  decided) is not in force`. The restored older ADR gets no row: it is back exactly as it was.

End with the validation-failure report (SKILL.md step 11): each file path with the status left, the
checks and repairs made, the unresolved errors, and any supersession rolled back. Skip the readiness
handoff, and never present readiness as validated.

## Self-check list

Read each written file back and check every item. That is the initial check. Repair and re-check
anything that fails: at most three repair cycles, so at most four checks. An error you can't repair,
such as one inside an existing item an amendment must leave byte-identical, ends the cycles early
(SKILL.md step 10).

**ARCH**
1. The path is `docs/specs/arch/ARCH-NNN.md`, `id` equals it, and no second ARCH was created for a
   system an existing ARCH covers.
2. The frontmatter has exactly the keys in the table, in order, with valid values. `status` is not
   `approved` unless it was approved before and this write was a review record that passes every
   check (one that fails leaves it `in-review`, ERR-05).
3. `generated_by` has non-empty `tool`, `model` and `session` (this session, unless this write was a
   review record); `reviewed_by` is `[]` for a new ARCH; every `hash` is `null`.
4. `outcome` is non-null only if the user confirmed it at step 8, or the request named it and said to
   proceed without questions.
5. Exactly one frontmatter link cites this run's PRD, at the version examined.
6. Every `yaml items` fence holds exactly one of `components`, `decisions`, `evidence`; there is at
   least one component and at least one evidence item.
7. Every item ID matches its pattern, is unique and numbered in order; for an amendment, every
   existing item is byte-identical except DEC `state` and `resolved_by` transitions, each logged in
   the Change Log (a DEC reopened for a changed mandated platform in the form "Change Log" gives).
8. Every item has its required fields in order and only allowed fields; every enum value is valid;
   every free-text value is double-quoted. Every new CMP has `kinds` from the kinds table, or a
   `[NEEDS CLARIFICATION: kinds of CMP-NN]` marker in section 8.
9. Every DEC has at least one upstream link to a requirement that exists in the PRD. `state: open`
   has `resolved_by: []`; `state: resolved` has a non-empty `resolved_by`, and every entry is an
   accepted, non-superseded ADR that exists or a policy setting that step 1 applied and that still
   mandates the platform the ARCH recorded (readiness.md, "A mandated platform that changed").
10. Every `[NEEDS ADR]` marker in the PRD has a DEC citing at least the requirements it names.
11. No DEC is resolved by an ADR written in this run unless the user explicitly chose that option.
    With no user, no ADR was written and no DEC was newly resolved except by a mandated platform;
    resolutions already in an amended ARCH changed only as readiness.md allows. No DEC reopened
    because its mandated platform changed was resolved again by that setting without the user's
    confirmation.
12. The PRD has an EVD with `kind: prd` and `classification: context`. Every EVD's classification
    follows inspection.md, and every code EVD's path is inside `inspection_scope`.
13. Every link sits in the one place the Link records table gives, and no policy link exists for a
    default, a local value, or a deprecated or non-applicable setting.
14. All nine headings are present; sections 7 and 8 hold bullets or `- None.`; no `<!--`, no `<…>`
    placeholder, no `ARCH-000`, `PRD-000` or `YYYY-MM-DD`, and no template example item is left.
15. The last Change Log row's author is `claude-code (session <ID>)` with this session's ID, and its
    change text ends with a `Policy resolution:` line in the policy.md format.

**Each ADR written or changed**
16. The path is `docs/specs/adr/ADR-NNN.md` and `id` equals it; the frontmatter has the template's
    keys in order, with `type: adr`, and the status and approval fields in the ADRs table.
17. `status: accepted` only for a decision the user explicitly made; every `hash` is `null`; no
    `<!--` or `<…>` placeholder is left; the Status history has the row described above.
18. An existing ADR, accepted or proposed, changed only as "Recording a supersession" allows, or,
    after ERR-05, is byte-identical to its state before the run.

## Example item blocks

````markdown
```yaml items
components:
  - id: CMP-01
    status: active
    name: "Volunteer web app"
    kinds:
      - "user-interface"
    responsibility: "Sign-in flow, shift browsing and booking screens; holds no data of its own"
    owns_data: []
    interacts_with:
      - "CMP-02"
      - "CMP-03"
    deployment: "Open: see DEC-03"
    upstream:
      - {id: PRD-001, item: NFR-002, relation: informed_by, version: 1, hash: null}
  - id: CMP-03
    status: active
    name: "Identity platform"
    kinds:
      - "external"
    responsibility: "Authenticates volunteers and issues sessions; external, provided by the organization"
    owns_data:
      - "Volunteer credentials"
    interacts_with:
      - "CMP-01"
    deployment: "External: Org A Identity Platform (OIDC)"
    upstream:
      - {id: POL-001, item: SET-01, relation: constrains, version: 3, hash: null}
```

```yaml items
decisions:
  - id: DEC-01
    status: active
    question: "Which identity provider handles volunteer sign-in?"
    blocking: true
    state: resolved
    resolved_by: [POL-001#SET-01]
    notes: "Mandated by organization policy."
    upstream:
      - {id: PRD-001, item: FR-001, relation: informed_by, version: 1, hash: null}
  - id: DEC-02
    status: active
    question: "How are a volunteer's sessions revoked within the NFR-001 time limit?"
    blocking: true
    state: open
    resolved_by: []
    notes: null
    upstream:
      - {id: PRD-001, item: FR-001, relation: informed_by, version: 1, hash: null}
      - {id: PRD-001, item: NFR-001, relation: informed_by, version: 1, hash: null}
```

```yaml items
evidence:
  - id: EVD-01
    status: active
    source: "PRD-001"
    kind: prd
    finding: "Version 1, status approved: sign-in (FR-001) with session revocation (NFR-001); the identity provider is a NEEDS ADR marker."
    classification: context
```
````
