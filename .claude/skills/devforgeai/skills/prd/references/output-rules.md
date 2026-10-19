# PRD output rules

## Contents

- When to use this
- File and IDs
- Frontmatter
- Link records
- Item blocks
- Collections and fields
- Sections
- Markers
- Change Log
- Self-check list
- Example item blocks

## When to use this

Read this before writing a PRD (SKILL.md step 8), and check the written file against the self-check
list (step 9): one initial check, then at most three repair cycles. The architecture step and the
epic workflow parse PRDs mechanically, and a future checker validates them against
`prd.schema.json`. A PRD that reads well but breaks a rule here breaks everything downstream.

## File and IDs

| What | Pattern | Rule |
|---|---|---|
| Document ID | `PRD-NNN` (three digits) | Highest existing number in `docs/specs/prd/` plus one; `PRD-001` if none |
| File path | `docs/specs/prd/PRD-NNN.md` | Named by the ID only; the product or release name goes in `title` |
| Functional requirement | `FR-NNN` (three digits) | Unique in the document, numbered in order from `FR-001` |
| Non-functional requirement | `NFR-NNN` (three digits) | Unique, numbered in order from `NFR-001` |
| Success metric | `SM-NN` (two digits) | Unique, numbered in order from `SM-01` |
| Assumption | `ASM-NN` (two digits) | Unique, numbered in order from `ASM-01` |

IDs are never reused, renumbered or deleted. An extension continues each collection from its highest
number. To retire an item, set `status: deprecated` (and `superseded_by` if replaced). In prose, refer
to an item by its qualified form, `BRN-001#PRB-01` or `PRD-001#FR-003`.

## Frontmatter

Keep exactly these keys, in this order. Unknown or misspelled keys are errors.

| Key | Value |
|---|---|
| `id` | `PRD-NNN`, equal to the file name |
| `type` | `prd` |
| `title` | Quoted product or release name, never empty |
| `status` | `draft` for a new PRD. An extension keeps `draft` or `in-review`, and turns `approved` into `in-review`. Never write `approved`, and never restore it after a failed validation |
| `version` | Integer: `1` for a new PRD, plus one per extension |
| `created`, `updated` | Unquoted `YYYY-MM-DD` dates |
| `owner` | Quoted name of the accountable human |
| `authors` | List of quoted names. A new PRD: the owner and `"claude-code"`, e.g. `["Clinic operations lead", "claude-code"]`. An extension keeps the existing list, adding `"claude-code"` only if it's missing |
| `generated_by` | Map with quoted, non-empty `tool` (`"claude-code"`), `model` (your model ID) and `session` (`${CLAUDE_SESSION_ID}` as SKILL.md gives it), set by this write. Never guess or copy them. A value the host can't provide is written `"unavailable"` and disclosed in this write's Change Log row and in the reply |
| `reviewed_by` | `[]` for a new PRD; an extension keeps it unchanged, as the record of who reviewed earlier versions (humans only; never filled by the AI) |
| `approved_by` | `""`. An extension of an approved PRD clears it to `""` |
| `approved_on` | `null`. An extension of an approved PRD clears it to `null` |
| `upstream` | Link records, one per line (next section) |
| `supersedes` | `[]` |
| `superseded_by` | `null` |
| `blocked_by` | `[]` |
| `target_release` | Quoted release name, or `"[NEEDS CLARIFICATION: name of the current release]"` |
| `stage` | `prototype`, `mvp`, `evolution` or `null` |
| `operating_context` | `local`, `internal`, `pilot`, `production` or `null` |
| `stakeholders` | List of quoted names, or `[]` |

Keep the `# --- prd-specific ---` comment line before `target_release`.

## Link records

A link is written in flow form, one per line:

```yaml
upstream:
  - {id: BRN-001, item: PRB-01, relation: derives, version: 1, hash: null}
```

- Fields: `id` (a document ID), `item` (optional item ID), `relation`, `version` (the cited document's
  `version` when the link was written: its current one for a link this write adds), `hash`, and an
  optional quoted `note`. No other field.
- `hash` is always `null`. Only the checker writes hashes, so a typed hash is wrong by definition.
- Each link lives in **exactly one place**: on the item that owns it, or in frontmatter when the whole
  document owns it. Never write the same link twice; prose mentions an item only by its qualified
  reference as plain text (`BRN-001#PRB-01`). A BRN problem the PRD addresses is linked in
  frontmatter; if that problem also states a quality need, the NFR carries its own link to it.

| Link | Where | Form |
|---|---|---|
| BRN problem | Frontmatter | `{id: BRN-NNN, item: PRB-NN, relation: derives, …}` |
| BRN idea | The FR (or SM) derived from it | `{id: BRN-NNN, item: IDEA-NN, relation: derives, …}` |
| BRN assumption | The PRD assumption | `{id: BRN-NNN, item: ASM-NN, relation: derives, …}` |
| BRN problem or promoted idea that itself states a quality need | The NFR | `{id: BRN-NNN, item: PRB-NN, relation: derives, …}`, or `item: IDEA-NN` for an idea |
| Accepted ADR that applies | Frontmatter | `{id: ADR-NNN, relation: constrains, …}` |
| Another PRD's shared constraint or NFR | Frontmatter | `{id: PRD-NNN, item: NFR-NNN, relation: constrains, …}` |
| Mandated platform setting | The constraint NFR it produced, never frontmatter | `{id: POL-NNN, item: SET-NN, relation: constrains, version: <policy version>, hash: null}` |
| `interview.max_calls` or `quality.required_categories` from policy | Frontmatter | `{id: POL-NNN, item: SET-NN, relation: informed_by, version: <policy version>, hash: null}` |

Never link a proposed or superseded ADR, an open, parked or rejected idea, a default, a local value,
or a deprecated or non-applicable setting.

**Every FR derives from a promoted idea; every NFR cites its actual source.** An NFR links to a BRN
item only when that item itself states the requirement. Otherwise it cites the policy setting, ADR
or PRD it comes from, and an NFR the user stated has no upstream link. Never add a brainstorm link to
a requirement the user stated or policy added (brn-mapping.md).

## Item blocks

- An item block is a fenced block whose info string is exactly `yaml items`.
- Each fence holds **exactly one top-level key**: `success_metrics`, `functional_requirements`,
  `non_functional_requirements` or `assumptions`. An empty collection is written `success_metrics: []`.
- Inside a fence use `#` comments only. Delete the template's comments inside item blocks.
- **Quote every free-text value** with double quotes. Unquoted text that starts with `>`, `|`, `*`,
  `&`, `!`, `@`, `%` or a backtick, or that contains `: ` or ` #`, silently changes meaning.
- IDs, enum values, `null` and numbers are not quoted. Link records use the flow form above; every
  other list is a block list.

## Collections and fields

Every item has `id` and `status` (`active` or `deprecated`), and may have `superseded_by` (an item
ID) and `upstream` (link records). Besides those, only these fields are allowed:

**`functional_requirements`**: all four fields are required, even when `null`.

| Field | Value |
|---|---|
| `statement` | Quoted; starts "The system shall"; one testable capability |
| `priority` | `must`, `should`, `could`, `wont` or `null` |
| `release` | `current`, `later` or `null` |
| `notes` | `null` or a quoted note |

**`non_functional_requirements`**: `category`, `statement`, `priority` and `release` are all required.
There is no `notes` field.

| Field | Value |
|---|---|
| `category` | `performance`, `security`, `privacy`, `accessibility`, `reliability`, `compliance`, `observability`, `usability`, `maintainability`, `constraint` or `other` |
| `statement` | Quoted and measurable where possible. For `constraint`, the fixed condition and where it applies: "(applies to the whole product)", "(applies to <capability>)" or "(applies to <environment>)" |
| `priority` | `must`, `should`, `could`, `wont` or `null` |
| `release` | `current`, `later` or `null` |

**`success_metrics`**

| Field | Value |
|---|---|
| `metric` | Quoted, measurable |
| `baseline` | Quoted value, or `"[NEEDS CLARIFICATION: …]"` |
| `target` | Quoted value (required), or `"[NEEDS CLARIFICATION: …]"` |
| `measured_by` | Quoted source, or `"[NEEDS CLARIFICATION: …]"` |

**`assumptions`**

| Field | Value |
|---|---|
| `statement` | Quoted |
| `validation` | Quoted: how it will be confirmed or mitigated |
| `state` | `open`, `validated` or `invalidated` |

No other collections and no other fields.

## Sections

Keep these headings, in this order, exactly as the template has them:
`## 1. Summary`, `## 2. Problem and opportunity`, `## 3. Users and personas`,
`## 4. Goals and non-goals`, `## 5. Success metrics`, `## 6. Functional requirements`,
`## 7. Non-functional requirements`, `## 8. User experience`, `## 9. Constraints and dependencies`,
`## 10. Assumptions and risks`, `## 11. Release and rollout`, `## 12. Open questions`,
`## 13. Epic map`, `## Change Log`.

- Section 7 may open with prose before the item block. It records each required quality category
  the user explicitly answered with none, as the user's answer, one sentence each: `Security: the
  user confirmed nothing is needed beyond the platform.`
- Section 9 is prose only; each constraint itself is an NFR with `category: constraint`.
- Section 12 is a bullet list of every open item: `[NEEDS CLARIFICATION]` markers, `[NEEDS ADR]`
  markers, and design preferences for a future ADR. Write `- None.` if there are none.
- Section 13 keeps exactly the template's comment
  `<!-- GENERATED: epics whose upstream cites this PRD. Do not edit by hand. -->` and nothing else. It
  is the only `<!--` left in the file.

## Markers

| Marker | Means | Blocks |
|---|---|---|
| `[NEEDS CLARIFICATION: <question>]` | An unknown the user must answer | Approving the PRD |
| `[NEEDS CLARIFICATION: <category> requirements for <context>]` | A required quality category the user didn't answer, or the rest of a category the user answered only in part (interview.md round 4) | Approving the PRD |
| `[NEEDS CLARIFICATION: <category> requirements for <context>; required by POL-NNN#SET-NN, the user answered none]` | A category applied policy requires (or `constraint`, under a mandated platform), which the user answered with none: the none doesn't waive the policy | Approving the PRD |
| `[NEEDS CLARIFICATION: target for <item>]` | The user kept a requirement or metric but has no target yet; it goes in the item's `target` or statement | Approving the PRD |
| `[NEEDS ADR: <decision>; affects FR-NNN, FR-NNN]` | An open architecture decision: no accepted ADR or approved mandated platform resolves that exact decision yet | Writing epics for the named FRs, not approving the PRD |
| `null` in `stage`, `operating_context` or `release`, or in the `priority` of a `release: current` item | Not decided yet | Approving the PRD |
| `null` `priority` on a `release: later` item | Not prioritized until a release takes the item in | Nothing |

A design preference that is not a hard constraint is written in section 12 as a plain bullet, for
example `- Design preference for a future ADR (not a requirement): microservices, which the user is
leaning towards.` It is never an FR or NFR.

## Change Log

One row per version: version, date, author, change, items affected. The author of a row you write is
`claude-code (session <session ID>)`, with the same ID as `generated_by.session`. The change text
ends with the policy resolution line (policy.md), with no `|` inside it:

```markdown
| 1 | 2026-09-27 | claude-code (session 7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f) | Initial draft from BRN-001. Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default) | all |
```

Never edit earlier rows. An extension adds a row naming the new BRN and the items added, with the
sentence `This revision has not been reviewed.` before the resolution line, for example:

```markdown
| 2 | 2026-10-02 | claude-code (session 7b7b72c0-8b14-4b7d-aba5-f21cdbadc51f) | Extended from BRN-002: FR-004, FR-005 added. Status approved to in-review, approval cleared. This revision has not been reviewed. Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default) | FR-004, FR-005 |
```

## Self-check list

Read the file back and check each item. That is the initial check. Repair and re-check anything that
fails: at most three repair cycles, so at most four checks. An error you can't repair, such as one
inside an existing item an extension must leave unchanged, ends the cycles early (SKILL.md step 9).

1. The path is `docs/specs/prd/PRD-NNN.md` and `id` equals `PRD-NNN`.
2. The frontmatter has exactly the keys in the Frontmatter table, in order, with valid values.
   `status` is not `approved`.
3. `generated_by` has non-empty `tool`, `model` and `session`, set to this session; `reviewed_by`
   is `[]` for a new PRD; for an extension, `authors` (apart from an added `"claude-code"`),
   `reviewed_by` and every earlier Change Log row are unchanged; every `hash` is `null`; `created`
   and `updated` are dates.
4. Every `yaml items` fence holds exactly one of the four collection keys.
5. Every item ID matches its pattern, is unique and is numbered in order. For an extension, every
   existing item is unchanged.
6. Every item has its required fields and only allowed fields, and every enum value is in its list.
7. Every free-text value is double-quoted.
8. Every FR this write adds has an `upstream` link deriving from a **promoted** idea of the BRN that
   no PRD cited before. No NFR this write adds has a BRN link unless that BRN item itself states the
   requirement. Nothing this write adds names an open, parked or rejected idea. An existing item
   whose idea the BRN no longer promotes keeps its link: name it in the reply, as check 9 says.
9. Every link has a valid relation and `hash: null`, and sits in the one place the Link records table
   gives. Every link this write adds cites the cited document's current version. An existing link to
   an older version, in frontmatter or on an item, is a suspect link: leave it
   exactly as written and name it in the reply. It is never an error and never repaired. No policy
   link exists for a default, a local value, or a deprecated or non-applicable setting.
10. Every non-null `stage`, `operating_context`, `priority` and `release` this write set was supplied
    or confirmed by the user. Values already in the PRD before an extension are left as they are.
11. Every required quality category (the floor for the operating context, or production when it is
    unknown, plus applicable policy) is covered by an NFR of that category, by the user's explicit
    none in section 7's prose, or by a `[NEEDS CLARIFICATION: <category> requirements for <context>]`
    marker in section 12. A category that applied policy requires, or the constraint category of a
    mandated platform, keeps its marker naming the setting even after an explicit none. A category
    the user answered only in part keeps its marker for the unanswered rest.
12. No design preference appears as an FR or NFR. Each constraint NFR states a condition and where it
    applies.
13. Every `[NEEDS ADR]` marker names FR IDs that exist in the file.
14. All fourteen headings are present. No leftovers remain: no `<!--` other than the section 13
    GENERATED comment, in a new PRD no frontmatter `#` comment but `# --- prd-specific ---`, no `<…>`
    template placeholders, no `PRD-000`, `BRN-000` or `YYYY-MM-DD`, no
    template example items, and no empty `title`.
15. The last Change Log row's author is `claude-code (session <ID>)` with the ID in
    `generated_by.session`. Its change text ends with a `Policy resolution:` line in the policy.md
    format. For an extension, it says `This revision has not been reviewed.`

## Example item blocks

````markdown
```yaml items
non_functional_requirements:
  - id: NFR-001
    status: active
    category: constraint
    statement: "Payments are processed through Stripe (applies to checkout)."
    priority: null
    release: null
  - id: NFR-002
    status: active
    category: constraint
    statement: "Sign-in uses the Org A Identity Platform (OIDC) (applies to the whole product)."
    priority: null
    release: null
    upstream:
      - {id: POL-001, item: SET-01, relation: constrains, version: 3, hash: null}
```

```yaml items
success_metrics:
  - id: SM-01
    status: active
    metric: "Share of appointments booked online"
    baseline: "0%"
    target: "[NEEDS CLARIFICATION: target share and time frame]"
    measured_by: "[NEEDS CLARIFICATION: where booking source is reported]"
    upstream:
      - {id: BRN-001, item: IDEA-01, relation: derives, version: 1, hash: null}
```
````
