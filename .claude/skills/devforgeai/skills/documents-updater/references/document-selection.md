# Document selection

Read at steps 3 and 4. Covers what deserves documentation, which documents change, when to create
one, and which template to start from.

## Contents

1. What deserves documentation
2. Which documents to update
3. Finding affected documents
4. Missing documents
5. Where new documents go
6. Project profiles
7. Document templates

## 1. What deserves documentation

Include a change when it affects what a reader can do, how they must do it, or what they need to
maintain or operate the project.

| Change | Treatment |
|---|---|
| New capability or meaningful behavior change | Explain the capability and update the relevant usage guidance |
| Defect with observable impact | Describe the corrected behavior and the condition it affected |
| Compatibility break, removal or deprecation | Make the impact prominent, with the required action or replacement |
| Setup, defaults, configuration, API, CLI or supported environment | Update the exact instructions and the reference material |
| Operational behavior, recovery or deployment | Update the runbook or deployment guide |
| Contributor workflow or maintained architecture | Update contributor or architecture documentation that has become inaccurate |
| Formatting, internal renaming, routine cleanup, extra tests | Usually omit from reader-facing summaries, unless there is a material audience impact |
| Dependency or lockfile updates | Report a consequential compatibility, security, behavior or build requirement; omit routine churn |
| Documentation-only corrections | Correct the content; add a changelog entry only when the correction is itself significant or the repository requires one |

Never infer better speed, reliability, security, completeness or production readiness from an
implementation change. Cite a measured result, or describe the concrete behavior without the
benefit ("processes files in parallel", not "faster").

## 2. Which documents to update

Review `README.md` and `CHANGELOG.md` on every run. Update any document whose content must change.

| Document | Update when | Content |
|---|---|---|
| `README.md` | The overview, capabilities, prerequisites, quick start, essential configuration or documentation navigation changes | Accurate current guidance, with links to detail |
| `CHANGELOG.md` or the release-note source | The work produces a notable change for the project's readers | Brief entries grouped by the repository's release convention |
| Usage guides, tutorials, examples | An affected task or example now works differently | Correct steps, inputs and expected results |
| API, CLI, configuration or schema reference | A public interface, default, constraint or format changes | Exact names, accepted values, defaults, compatibility and examples |
| Migration or upgrade guide | Existing users must act | Who is affected, what to change, and in what order |
| Deployment, operations, troubleshooting | Installation, monitoring, failure handling or recovery changes | The operational procedure readers need |
| `CONTRIBUTING.md` or development setup | Build, test, local setup or contribution practice changes | Updated contributor instructions |
| Architecture docs and ADRs | Documented structure or an accepted decision changes | The current structure. Preserve historical decisions and follow the supersession process |
| Specifications and task records | Their recorded status or contract needs an authorized update | Evidence-supported status. Keep acceptance requirements and unresolved gaps |

**Governed documents.** When the repository versions and approves its specifications, ADRs or task
records (DevForgeAI's `docs/specs/` does: a version bump, a new `updated` date and Change Log rows,
one of them the owner's approval), change them only when the task authorizes it, and only through
that process. Never change an accepted requirement's contract.

## 3. Finding affected documents

The diff doesn't list every document that describes the change. Search for each affected:
- command, subcommand and option (`--exclude`, `deploy`);
- configuration key and environment variable (`cache.max_size`, `API_URL`);
- path, file name and module name that readers type;
- concept or feature name.

Search Markdown and other doc sources (`*.md`, `*.mdx`, `*.rst`, `docs/**`, docstrings rendered
into docs), including package READMEs in a monorepo. Update every hit that is now inaccurate.

**Generated documents.** If a document or release note is generated (the file says so; the build
config lists it; it sits in an output folder), edit its source or add the required change fragment
(for example `changelog.d/`, `.changeset/`, `newsfragments/`), then follow the existing generation
process. Never hand-edit generated output.

## 4. Missing documents

| Condition | Handling |
|---|---|
| A canonical document exists under another name or location | Update it and link to it. Never create a competing copy |
| No README or equivalent exists | Create a concise entry point from the README profile (§6). Inspect the repository broadly enough to describe its purpose, prerequisites and first-use path |
| No changelog or release-note source exists | Create `CHANGELOG.md` only when there are verified notable changes to record. Put them in an `Unreleased` section; never reconstruct old releases |
| There are no verified changelog entries | Leave the changelog absent unless the user or the repository requires an initial scaffold. Say that no verified entries are available |
| A guide, reference or runbook is missing | Create it when a concrete reader task needs detail that would overload the README. Use the matching template and link it from the right entry point |
| Information needed for accurate instructions is missing | Complete the supported sections, and name the gap under `Action required`. Ask only for what is essential |

A new document describes the whole current project, not just the diff: a recent diff can't
establish the installation process, the project's purpose or its supported interface. For an
existing document, focus on the changed behavior and on stale sections.

## 5. Where new documents go

1. The repository's existing layout: an existing `docs/` tree, a documentation site's source folder,
   a `doc/` folder, or a package's own folder in a monorepo.
2. Otherwise `README.md` and `CHANGELOG.md` at the root, `CONTRIBUTING.md` at the root, and
   everything else in `docs/<kebab-case-topic>.md` (for example `docs/configuration.md`,
   `docs/migrating-to-2.0.md`).
3. ADRs follow the repository's numbering and folder. With none, use `docs/adr/NNNN-<title>.md`
   starting at `0001`. Record a decision only when the user or an accepted document made it. Its
   status is `Proposed` unless acceptance is recorded.

Link every new document from the README's documentation section or another entry point. A root
`README.md` or `CHANGELOG.md` needs no link (output-rules.md §3).

## 6. Project profiles

Pick the profile from what the project is. A hybrid can use several, for example a CLI that is also
a library. All README profiles start with a short purpose statement, the intended audience, and any
material maturity or availability constraint. Next comes the shortest supported path to a useful
result, then documentation links. `assets/readme.md` holds the quick-start plan for each profile.

| Profile | Recognize it by | README emphasis | Supporting documents when needed |
|---|---|---|---|
| `cli` | A console entry point, an argument parser, a `bin/` | Install, prerequisites, first command, expected output | Command reference, configuration, troubleshooting |
| `library` | A package manifest with no entry point; public modules; used as a dependency | Supported runtimes, installation, minimal working example | API reference, examples, compatibility and migration |
| `service` | An HTTP or RPC server, routes, a Dockerfile, env-based configuration | Local startup, configuration, first request, dependencies | API contract, deployment, operations, architecture |
| `web-app` | A frontend framework, pages or components, a dev server | Purpose, local startup, primary user flow | Configuration, contributor setup, deployment; screenshots when they explain usage |
| `data` | Migrations, schemas, SQL, ETL or pipeline definitions | Supported engines, permissions, setup, first operation | Schema or data contracts, operational limits, migration, recovery |
| `infrastructure` | Terraform, Pulumi, Helm, Ansible or CloudFormation files | Prerequisites, deployment scope, plan or preview, apply | Configuration, deployment, runbook, teardown where supported |
| `agent-framework` | Skills, agents, prompts, specs, a plugin manifest | Execution environment, workflow entry point, implemented status, first task | Workflow guide, skill or command catalog, artifact contracts, validation and governance |
| `docs-research` | Mostly prose, papers, notebooks or reference material | Scope, reading path, how to use the material | Methodology, references, contribution or reproduction instructions |
| `monorepo` | Several packages or services with their own manifests | Workspace map, links to each component's entry point | Component READMEs with their own profiles; shared setup and contribution guidance |

## 7. Document templates

The templates give the structure within a profile. Keep tutorials, how-to guides, reference and
explanation separate when their readers or purposes differ. A short guide can stay inside an
existing document when a separate file would only add navigation.

For an existing document, adapt its established structure. For a new one, use the repository's
approved template if it has one, otherwise the asset below. The section orders are starting points:
omit sections that don't apply, and add what the actual task needs.

| Document | Asset | Section order |
|---|---|---|
| README | `assets/readme.md` | Purpose → prerequisites → quick start → common use → documentation links |
| Changelog | `assets/changelog.md` | Unreleased entries → existing releases, newest first |
| Tutorial | `assets/tutorial.md` | Learning goal → prerequisites → guided example → expected result → next task |
| How-to guide | `assets/how-to.md` | Task and applicability → prerequisites → steps → verify the result → troubleshooting |
| API or CLI reference | `assets/reference.md` | Interface → parameters and defaults → returns or exit codes → errors → examples |
| Configuration reference | `assets/configuration.md` | Where configuration lives → precedence → settings and defaults → examples |
| Migration guide | `assets/migration.md` | Affected users and versions → prerequisites → changes → migration steps → verify → recovery |
| Runbook | `assets/runbook.md` | Trigger or symptom → prerequisites → diagnosis → procedure → verify → recovery and escalation |
| Architecture explanation | `assets/architecture.md` | Scope → components and responsibilities → interactions → constraints → decision links |
| ADR | `assets/adr.md` | Status → context → decision → consequences → related decisions |
| Contributor guide | `assets/contributing.md` | Local setup → development workflow → checks → contribution process |
| Specification or workflow | `assets/specification.md` | Purpose → inputs → required behavior or steps → outputs → acceptance evidence → known gaps |

`assets/adr.md` and `assets/specification.md` are plain fallbacks for ordinary repositories. In a
DevForgeAI project, ADRs and specifications are typed planning documents with their own templates
and skills; follow the repository's format and governance there, never these assets.

**Template syntax.** `{{…}}` marks a placeholder to replace with verified content, and
`<!-- guide: … -->` is an instruction to follow and then delete. Neither may remain in a delivered
document; `check_docs.py` reports both.
