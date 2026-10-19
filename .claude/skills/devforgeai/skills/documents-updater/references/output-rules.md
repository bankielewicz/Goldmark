# Output rules

Read before step 5. It holds the rules for what the skill delivers and ends with the self-check list
used at step 6.

## Contents

1. README
2. Changelog
3. New documents
4. Proposal mode
5. Completion response
6. The validator script
7. Self-check list

## 1. README

- Describe the current supported state at the target. The README is not a history.
- Keep the overview and the getting-started path easy to scan: purpose, audience, and any maturity
  constraint, then prerequisites, then the shortest path to a useful result, then links.
- Preserve release and branch qualifiers, so an unreleased capability isn't presented as available
  in the latest published version ("On `main`, not yet released: `--exclude` patterns"). When the
  repository has published releases (tags or release records; a version number in a manifest
  such as `pyproject.toml` or `package.json` is not one) and the README now describes
  unreleased behavior, add such a qualifier, unless the README already says it tracks the
  development branch. With no published release, no qualifier is needed.
- Never append a work-session report, a commit inventory or an implementation diary.
- When the README grows past what a first-time reader needs, move detail into a guide or reference
  and link to it.

## 2. Changelog

**Use the existing format and release tooling.** Match its headings, category names, entry style
and link style exactly. With release-note fragments or a generator, add a fragment instead of
editing the output.

**With no convention**, use Keep a Changelog 1.1.0:

```markdown
# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added

- Added `--exclude PATTERN` to skip matching files during a sync.
```

- One `Unreleased` section, first, above every release. Write it `## [Unreleased]` only when a
  matching link definition (`[Unreleased]: <compare URL>`) exists or can be built from a real remote
  and tag; otherwise `## Unreleased`.
- Categories in this order, only those with entries: `Added`, `Changed`, `Deprecated`, `Removed`,
  `Fixed`, `Security`.
- Add the "adheres to Semantic Versioning" sentence only when the repository says it follows SemVer.

**Entries.**
- One entry per meaningful outcome, following writing-standards.md ("Concise reporting").
- Before adding an entry, read the existing `Unreleased` entries. If one already describes the same
  change, merge into it instead of adding a second.
- **Compatibility breaks** are prominent within the existing format: prefix the entry with
  `**Breaking:**` (or the repository's marker), name the affected behavior and the required action,
  and link to migration details when the action takes more than one sentence.
- **Security entries** follow the project's disclosure rules (for example, no exploit detail before
  an advisory is public).

**Never:**
- assign a version, write a release date, or move entries into a release section;
- mark the work as released;
- edit a published release section, except for an explicitly requested factual correction. Published
  sections stay byte-identical;
- reconstruct old releases from assumptions.

## 3. New documents

- Start from the repository's template or, failing that, the asset named in document-selection.md §7.
- Replace every `{{placeholder}}` with verified content, or delete the line or section it sits in.
- Delete every `<!-- guide: … -->` comment and every section that doesn't apply. Leave no empty
  heading.
- Never invent commands, URLs, credentials, owners, licenses, support guarantees or release history.
  If an essential fact is unknown, leave the instruction out and name the gap under
  `Action required`.
- A first-time reader must be able to find the purpose, the prerequisites, the next action and the
  expected result without this conversation.
- Link the new document from an entry point (usually the README's documentation section). A root
  `README.md` or `CHANGELOG.md` is itself an entry point: link a new root changelog from the README
  only when the README already has a documentation or links section.

## 4. Proposal mode

Modify no repository file. In the reply, before the completion response, give for each document:
- **An existing document:** a unified diff (a `diff` code block with `---`/`+++` headers and enough
  context to apply it), or the exact heading of the section to replace followed by its full
  replacement text.
- **A new document:** its path and its full proposed content.

Proposals are complete and exact: never "consider adding a section about …". They follow every rule
in this file, and the self-check list applies to them, except the items that need written files.

## 5. Completion response

The reply ends with this block, and nothing follows it:

```text
Result: updated | proposed | no_change | partial | blocked
Scope: <comparison point and target; uncertainty only if material>
Documents: <paths created, updated or proposed>
Highlights:
- <meaningful outcome>
Validation: <checks performed and relevant limitations>
Action required: <migration, unresolved discrepancy or essential clarification>
```

| Result | Use when |
|---|---|
| `updated` | Every supported update or creation was applied |
| `proposed` | Proposal mode, with every edit fully prepared |
| `no_change` | No edit is warranted: every document is accurate and nothing notable is undocumented |
| `partial` | The supported updates are done, but an issue remains (an unknown baseline, missing information, validation errors, uncertain attribution) |
| `blocked` | No accurate update can proceed |

- Omit empty fields. Name documents by relative path.
- One to three highlights, each a reader-facing outcome; more only when a required user action
  needs them.
- `Validation` names what was run (`check_docs.py`, the repository's docs build, `git diff --check`)
  and says whether the documents were previewed rendered or reviewed as source. It names any example
  that was not run. With `no_change`, it names the documents reviewed; running `check_docs.py` on
  unchanged documents is optional.
- `Action required` holds migrations readers must perform, discrepancies with accepted
  requirements, uncertain attributions, and the questions whose answers the skill still needs.
- Never paste the evidence inventory, and never list every implementation change.

## 6. The validator script

Run it as SKILL.md step 6 shows (`scripts/check_docs.py` in the skill's directory), from the
repository root, on every Markdown file you created or edited. It prints
`path:line: error|warning: message` and exits 1 if there is any error. Fix errors in lines you
added or changed; an error in a line you didn't touch is pre-existing, so leave it and report it
under `Validation`. It checks:

- exactly one H1 (or a front-matter `title`), and no skipped heading level;
- code fences that are closed;
- empty sections (a heading followed directly by a heading of the same or a higher level);
- leftover `{{placeholders}}` and `<!-- guide: -->` comments outside code;
- relative links and images whose file doesn't exist, and `#anchors` that match no heading;
  empty image alt text (warning);
- in a file named `CHANGELOG*.md`: more than one `Unreleased` section, an `Unreleased` section that
  isn't first, a duplicate category in one release, a duplicate entry under `Unreleased`, and
  categories outside Keep a Changelog (warning).

It can't judge truth, scope, tone or completeness; the self-check list covers those.

## 7. Self-check list

Check each item and fix what fails. In proposal mode, skip items 1 and 13, and check the others
against the proposal.

1. `check_docs.py` reports no errors in the lines you created or changed; pre-existing errors in
   untouched lines are reported, not fixed.
2. Each edited document, compared with its pre-edit content, changes only the sections the scope
   requires; unrelated content, formatting and the user's staging choices are unchanged.
3. Every substantive claim matches the repository at the target, and its status (specified,
   implemented, tested, released) is supported by evidence.
4. No speed, reliability, security, completeness or readiness claim lacks a measured result.
5. No invented commands, URLs, credentials, owners, licenses, support guarantees, versions, dates
   or release status.
6. Commands, options, configuration keys, defaults, paths and examples agree across every edited
   document, and every document that mentions an affected name was checked.
7. README: the current state only, with release qualifiers kept, and no session report or commit
   list.
8. Changelog: the existing format (or Keep a Changelog), one `Unreleased` section, categories with
   entries only, no version or date added, published sections byte-identical, breaking changes
   prominent with the required action.
9. No duplicate entries, and a second run with the same evidence would add nothing and change
   nothing.
10. New documents: from a template, with no placeholder, guide comment or empty section left, and
    linked from an entry point (a root README or changelog excepted, §3). A first-time reader can
    find the purpose, prerequisites, next action and expected result.
11. Governed documents changed only with authorization, and through the repository's process.
12. No code, staging, commit, push, tag, release or deployment change was made outside the user's
    task.
13. The whitespace checks are clean (`git diff --check` and `git diff --cached --check` for tracked
    documents, `git diff --no-index --check /dev/null <file>` for new ones), and the repository's
    own documentation checks pass, if it defines any.
14. The completion response ends the reply, with the right `Result` and no empty fields.
