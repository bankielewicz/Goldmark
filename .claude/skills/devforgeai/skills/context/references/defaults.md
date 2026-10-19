# Framework defaults (configuration contract v1)

The most general policy layer (ADR-003 A2, A3). Approved organization and project policy, and local
preferences where allowed, override these values as `policy.md` describes. Each skill that resolves
policy keeps a byte-identical copy of this file.

## Settings exposed in v1

| Key | Class | Default | `overridable_by` of the default | Merge rule |
|---|---|---|---|---|
| `interview.max_calls` | interaction default | `8` calls, each within the host's per-call question limit | `project`, `local` | The most specific layer that is allowed to set it wins |
| `architecture.mandated_platforms` | organizational policy | none | — | Collected across layers; a project replaces an organization mandate for the same capability only when the organization setting allows `project` |
| `quality.required_categories` | organizational policy | none beyond the floor below | — | Additive only: added to the floor, never removing a floor category |

## Testing settings (ADR-005)

Organizational policy. Only a workflow that resolves the testing keys resolves them; every other
workflow still validates them (R1) and records no testing entry.

| Key | Class | Default | `overridable_by` of the default | Merge rule |
|---|---|---|---|---|
| `testing.method` | organizational policy | `tdd` | — | The most specific layer the organization setting allows wins |
| `testing.coverage_metric` | organizational policy | `line` | — | The same |
| `testing.coverage_threshold` | organizational policy | none: coverage isn't enforced, and reports say "no coverage threshold set" | — | The same |
| `testing.coverage_scope` | organizational policy | every `holds: code` root in the project's `source-tree.md` | — | The same |
| `testing.coverage_exclusions` | organizational policy | none beyond the `generated`, `tests` and `fixtures` roots | — | The same |
| `testing.exception_approvers` | organizational policy | the story's `owner` | — | The same |

## Quality floor per operating context

A **framework requirement**, not a setting: policy can add categories (`quality.required_categories`)
but can never remove one of these. These are the NFR categories a workflow must ask about, or mark as
unanswered, for each operating context.

| Operating context | Required NFR categories |
|---|---|
| `local` | constraint |
| `internal` | constraint, security, privacy |
| `pilot` | constraint, security, privacy, reliability, observability, compliance |
| `production` | constraint, security, privacy, reliability, observability, compliance, performance, accessibility |
| unknown | the `production` set (the fail-safe), with the document's `operating_context` left `null` |

## Values that are not settings

Nothing overrides these; they change only through DevForgeAI's own specifications.

| Class | Values |
|---|---|
| Framework requirement | The quality floor above; validation: one initial check and at most three repair cycles; ID-only file names; provenance fields; the AI never decides scope, priority or release; the eval threshold (0.8) |
| Document contract | MoSCoW `priority` (`must`, `should`, `could`, `wont`); `stage`, `operating_context` and `release` values; BRN `disposition`; NFR categories; item ID prefixes |
| Platform limit | The host's question tool. In Claude Code, AskUserQuestion takes at most 4 questions per call, with 2–4 options each |
