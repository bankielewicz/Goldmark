<!-- guide: Fallback README. Use it only when the repository has no README template of its own.
     Pick the project profile (document-selection.md §6), follow its quick-start plan below, and keep
     only the sections this project needs. Replace every placeholder with verified content or delete
     it, and delete every guide comment. -->

# {{Project name}}

{{One or two sentences: what the project does and the result it gives its reader.}}

{{Who it is for.}} {{Maturity or availability constraint, if any, for example "Unreleased: install from source until the first release."}}

<!-- guide: With no tag, release record or package-index listing, the project is unreleased. Say so
     here only when readers might expect a published package; per-feature qualifiers are needed
     only once releases exist (output-rules.md §1). Never claim a published version, a listing or a
     support level the repository doesn't show. -->

## Prerequisites

- {{Runtime, tool, account or permission, with the minimum supported version}}

## Quick start

<!-- guide: The shortest supported path to a useful result. Plan by profile:
     cli             install → first command → expected output
     library         supported runtimes → install → minimal working example
     service         local startup → essential configuration → first request → dependencies
     web-app         local startup → the primary user flow
     data            supported engines → required permissions → setup → first operation
     infrastructure  deployment scope → plan or preview → apply (→ teardown, where supported)
     agent-framework execution environment → workflow entry point → first task
     docs-research   scope → reading path → how to use the material
     monorepo        replace this section with "Workspace map" below
     Name the shell and the working directory when they matter, and show the expected result. -->

1. {{Step}}

   ```bash
   {{command from the repository}}
   ```

2. {{Step, with its expected result}}

## Usage

<!-- guide: Common tasks beyond the quick start: a sentence or a short example each, linking to the
     full guide or reference. Delete this section when the quick start covers everything. -->

## Workspace map

<!-- guide: Monorepo only. One row per component, linking to its own README. -->

| Component | Path | Purpose |
| --- | --- | --- |
| {{name}} | [{{path}}]({{path}}/README.md) | {{purpose}} |

## Status

<!-- guide: Agent-framework projects, or any project with parts that are specified but not built.
     Say what is implemented, tested and released, versus only specified, as far as evidence supports. -->

## Configuration

<!-- guide: Only the essential settings, with their defaults. Link to the configuration reference for
     the rest. -->

## Documentation

- [{{Document title}}]({{docs/path.md}}): {{what the reader finds there}}

## Contributing

<!-- guide: One sentence linking to CONTRIBUTING.md, only if it exists. -->

## License

<!-- guide: Only if the repository has a license file: name the license exactly as that file does and
     link to it. Never choose or guess a license. -->
