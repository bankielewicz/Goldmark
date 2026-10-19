# Policy resolution (configuration contract v1)

## Contents

- When to use this
- Layers
- R1. Load and validate
- R2. Resolve the unconditional settings
- Local preferences
- R3. Establish the operating context
- R4. Apply the conditional settings
- R5. Record
- The resolution line
- Stopping on a policy error

## When to use this

Every DevForgeAI workflow that writes a planning document resolves policy this way, in this order,
before it asks a question or writes a file (ADR-003 A3 to A5). Each skill that resolves policy keeps
a byte-identical copy of this file, `defaults.md`, `scripts/validate_policy.py` and
`references/schemas/`, so two workflows reach the same result.
"The document" below means the document the workflow writes.

Policy is **approved rules**. Observed practice (what the code happens to use) is not policy. When
the two disagree, report it as a finding labelled "observed practice". That is not an error.

## Layers

From general to specific:

1. **Framework defaults**, in `defaults.md`. The most general layer.
2. **Organization policy**: an approved `docs/specs/policy/POL-NNN.md` with `scope: organization`,
   vendored from the organization, with its `source` recorded.
3. **Project policy**: an approved `docs/specs/policy/POL-NNN.md` with `scope: project`.
4. **Local preference**: `.claude/devforgeai.local.md`, for interaction defaults only.

**Which value wins:** the most specific layer that is *allowed* to set it. **Who may override:** a
setting's `overridable_by` names the more specific layers that may override it (`project`, `local`).
Framework requirements, document contracts and platform limits (`defaults.md`) are not settings, and
nothing overrides them.

## R1. Load and validate

Policy documents are `docs/specs/policy/POL-*.md`: YAML frontmatter plus a `settings` item block. If
the folder or the files are missing, there is no policy; continue with the defaults.

**Validate with the skill's script**, `scripts/validate_policy.py`, the one maintained validation path
(SKILL.md gives the exact command). It reads every `POL-*.md` in the folder and:
- skips each document whose `status` is not `approved`, printing `ignored <file> (status <status>) (SV-06)`;
- validates each approved document in full against `references/schemas/policy.schema.json` and
  `common.schema.json`, unchanged copies of the framework schemas: every frontmatter key and its type,
  date patterns, `authors`, link records, and each setting's fields, key, class, value type and range,
  `applies_when` and `overridable_by`;
- checks dates against the calendar through the schema's date format, so `2026-13-45`, `2026-02-30`
  and `2025-02-29` fail in `created`, `updated` and a non-null `approved_on`, as `schema` errors;
- then applies SV-01 to SV-06 and SV-08 below, including SV-02 and SV-04's cross-layer clause across documents;
- prints one line per error, `<file>: <part>: <field>: <message> (<rule>)`, where `<part>` is
  `frontmatter`, `document` or a setting such as `SET-01 (interview.max_calls)`, and `<rule>` is
  `schema` or `SV-NN`.

Act on its exit code:

| Exit | Meaning | Do |
|---|---|---|
| 0 | Every approved document is valid, or none exists | Continue to R2. Record each `ignored` line in the resolution line, without its `(SV-06)` suffix |
| 1 | An approved document is invalid | **Stop** before asking anything or writing any file ("Stopping on a policy error"), naming every error the script printed |
| 2, or the script can't be run at all (no shell, no `python3`) | Validation can't run | If any `POL-*.md` has `status: approved`, **stop**: say that validation couldn't run, quote the script's message, and write nothing. Never validate by reading the files instead, and never skip validation. If none is approved, continue with the defaults and record the ignored documents |

Never edit a policy document, and never repair one so that it passes.

**Semantic rules**

| Rule | Requirement | On failure |
|---|---|---|
| SV-01 | Setting IDs are unique within a document | Stop |
| SV-02 | At most one approved document per scope | Stop |
| SV-03 | At most one active `interview.max_calls` setting per document | Stop |
| SV-04 | At most one active `architecture.mandated_platforms` setting per capability per document. Across layers, a project setting for the same capability overrides only if the organization setting's `overridable_by` includes `project` | Stop |
| SV-05 | Only `status: active` settings take part. A deprecated setting stays in its document for traceability, but it never applies and is never linked | Skip it (the script notes it) |
| SV-06 | Only approved documents take part | Skip and report |
| SV-07 | Local preference entries follow the local format rules. The workflow checks these, not the script | Ignore and report |
| SV-08 | At most one active setting per `testing.*` key per document | Stop |

Two settings are "for the same capability" when their `capability` strings match, ignoring case
and surrounding spaces.

## R2. Resolve the unconditional settings

Resolve these before the first question, so the interview budget is known.

**`interview.max_calls`**
1. Start from the default (`defaults.md`): `8`, with `overridable_by: [project, local]`.
2. If the organization policy has an active setting, it replaces the default.
3. If the project policy has an active setting, it replaces the current value. That is allowed
   only when the current value is the default or its setting's `overridable_by` includes
   `project`. If not, **stop**: it is a forbidden override (ADR-003 A4).
4. Apply a local preference last, only when the current setting's `overridable_by` includes
   `local` (see "Local preferences").

**`architecture.mandated_platforms`**
1. Collect every active setting from the organization and project policies.
2. When both layers mandate the same capability, the project setting replaces the organization's
   only if the organization setting's `overridable_by` includes `project`. Otherwise **stop** with
   SV-04, naming both settings.
3. Each remaining setting applies to the document as a mandated platform (R5).

**The testing settings** (`testing.*`, ADR-005). A workflow that resolves the testing keys resolves each
one as `interview.max_calls` is resolved in steps 1 to 3, starting from its default in `defaults.md`:
the most specific layer the organization setting allows wins. There is no local step: testing keys are
organizational policy, so a local entry for one is ignored and reported. A workflow that doesn't
resolve the testing keys validates them in R1 and records nothing for them.

## Local preferences

Read `.claude/devforgeai.local.md` if it exists. Its YAML frontmatter is its whole content:
`devforgeai_local: 1`, then one entry per interaction-default key. For example:

```yaml
---
devforgeai_local: 1
interview.max_calls: 5
---
```

Use an entry only when all of these hold:
- its key is an interaction default (v1: only `interview.max_calls`);
- its value has the right type and range (an integer from 1 to 20);
- the effective setting's `overridable_by` includes `local`.

Ignore and report any other entry, and a file without `devforgeai_local: 1`. A local file never
stops the workflow. Record each value used as `<key>=<value> (local)` in the resolution line, never
as a link. Record each ignored entry as `ignored .claude/devforgeai.local.md <key> (<reason>)`.

## R3. Establish the operating context

Take the operating context (`local`, `internal`, `pilot` or `production`) from the request, the input
document, or the first interview question, in that order. If it is still unknown, it stays unknown:
the document keeps `operating_context: null`.

## R4. Apply the conditional settings

For each active `quality.required_categories` setting in an approved document:
- if its `applies_when.operating_context` includes the context, add its categories to the framework
  floor (`defaults.md`). Additions never remove a floor category, and settings from every layer add up;
- otherwise it doesn't apply: record `POL-NNN#SET-NN not applicable (<context>)`.

If the context is unknown, evaluate every setting **as `production`**, the fail-safe, and record
`operating context unknown, resolved as production`.

## R5. Record

Record each applied policy setting as a link carrying the policy document's `version`, in **exactly
one place**:

| Setting | Link | Where |
|---|---|---|
| A mandated platform (`architecture.mandated_platforms`) | `{id: POL-NNN, item: SET-NN, relation: constrains, version: <policy version>, hash: null}` | On the item it produced, never repeated in the frontmatter. The workflow's SKILL.md names that item |
| A setting that governs how the document is produced (`interview.max_calls`, `quality.required_categories`) | `{id: POL-NNN, item: SET-NN, relation: informed_by, version: <policy version>, hash: null}` | Document frontmatter `upstream` |
| A testing setting (`testing.*`), in a workflow that resolves the testing keys | `relation: constrains` in a context document; `relation: informed_by` in any other document | Document frontmatter `upstream` |

Defaults, local values, non-applicable settings, deprecated settings and ignored documents get
**no link**. The resolution line records them instead.

## The resolution line

The document's Change Log entry for this write ends with exactly one resolution line:

`Policy resolution: <entry>; <entry>; …`

Write the entries in this order, and never use a `|` character (the line sits in a table cell):

1. **Interview budget**, always one entry:
   - `interview.max_calls=<N> (default)`
   - `interview.max_calls=<N> (POL-NNN#SET-NN)`
   - `interview.max_calls=<N> (local)`
2. **Mandated platforms**, either:
   - `architecture.mandated_platforms=none (default)`, when none applies, or
   - one entry per applied setting: `architecture.mandated_platforms=<platform> for <capability> (POL-NNN#SET-NN)`.
3. **Required categories**, either:
   - `quality.required_categories=floor only (default)`, when no setting applies, or
   - one entry per applied setting, listing its categories in the setting's order:
     `quality.required_categories=+<category>,+<category> (POL-NNN#SET-NN)`.
4. `POL-NNN#SET-NN not applicable (<context>)` for each conditional setting that didn't apply.
5. `operating context unknown, resolved as production` when the context is unknown.
6. `ignored <file or local entry> (<reason>)` for each skipped document or local entry.
7. **Testing settings**, only in a workflow that resolves the testing keys: one entry per key, in the
   order of `defaults.md`, each `<key>=<value> (POL-NNN#SET-NN)` or `<key>=<value> (default)`. A list
   value is joined with `,` and no spaces; the threshold is a number or `none`. The defaults are written
   `testing.method=tdd (default)`, `testing.coverage_metric=line (default)`,
   `testing.coverage_threshold=none (default)`, `testing.coverage_scope=code roots (default)`,
   `testing.coverage_exclusions=generated,tests,fixtures roots (default)` and
   `testing.exception_approvers=story owner (default)`.

Examples:

- No policy:
  `Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default)`
- An organization policy, with operating context `internal`:
  `Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=Org A Identity Platform (OIDC) for identity and authentication (POL-001#SET-01); quality.required_categories=+compliance,+accessibility (POL-001#SET-02)`
- A permitted project override, and a conditional setting that doesn't apply:
  `Policy resolution: interview.max_calls=4 (POL-002#SET-01); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default); POL-001#SET-02 not applicable (internal)`
- Unknown context, plus a local value and a draft document:
  `Policy resolution: interview.max_calls=5 (local); architecture.mandated_platforms=none (default); quality.required_categories=+compliance (POL-001#SET-01); operating context unknown, resolved as production; ignored docs/specs/policy/POL-003.md (status draft)`
- A workflow that resolves the testing keys, with one testing setting:
  `Policy resolution: interview.max_calls=8 (default); architecture.mandated_platforms=none (default); quality.required_categories=floor only (default); testing.method=tdd (default); testing.coverage_metric=line (default); testing.coverage_threshold=90 (POL-001#SET-03); testing.coverage_scope=code roots (default); testing.coverage_exclusions=generated,tests,fixtures roots (default); testing.exception_approvers=story owner (default)`

## Stopping on a policy error

When R1 or R2 finds a violation, stop before asking anything or writing any file. The reply names, for
every error the script printed (and for a forbidden override R2 found):
- the policy file;
- the setting, as its `SET-NN` and its `key` (or both settings for a cross-layer conflict), or the
  frontmatter field for an error outside the settings;
- the field and the rule broken: `schema` with what is wrong, `SV-NN`, or `forbidden override`.

It then says that nothing was written. Never guess a value and never fall back silently. Examples:

- `Policy error in docs/specs/policy/POL-001.md, SET-01 (interview.max_calls): value 50 is outside 1–20 (schema). Nothing was written; fix the policy and run again.`
- `Policy error in docs/specs/policy/POL-001.md, frontmatter field updated: '2026-13-45' is not a 'date' (schema). Nothing was written.`
- `Policy validation couldn't run (jsonschema is not installed), and docs/specs/policy/ holds approved policy, so nothing was written. Install the Python packages PyYAML and jsonschema, then run again.`
- `Policy error: docs/specs/policy/POL-002.md SET-01 (architecture.mandated_platforms, identity and authentication) overrides docs/specs/policy/POL-001.md SET-01, whose overridable_by doesn't include project (SV-04). Nothing was written.`
