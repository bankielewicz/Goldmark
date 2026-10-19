# Writing standards

Read before step 5. Apply these to new and changed sections only; avoid unrelated visual rewrites.
When the repository has its own style guide, it wins.

## Layout

Use a restrained, consistent Markdown layout that reads well on GitHub and in a local editor.

- **One title, logical headings.** Each document has one H1 (or a front-matter `title`), and
  headings never skip a level. Use descriptive, sentence-case headings ("Configure the proxy", not
  "Proxy Configuration Options").
- **Purpose first.** Put the purpose, who it applies to, and the reader's next action near the top.
- **Short units.** Short paragraphs; ordered lists for procedures, one action per step; compact
  tables for comparisons and reference fields. Avoid oversized tables.
- **Prerequisites before commands.** Name the shell or language, the working directory, required
  substitutions, and the expected result whenever the reader needs them to act.
- **Usable examples.** Complete enough for their purpose, with a language on every code fence.
  Explain each placeholder (`<your-token>`: the API token from Settings → Tokens). Never insert a
  real secret.
- **Links.** Meaningful link text ("see the `[configuration reference](docs/configuration.md)`",
  never "click here"). Relative links for files in the repository. Add a contents list or
  documentation index only when it helps navigation.
- **Images and diagrams** only when they explain a relationship or a task better than text. Give
  images alt text, and keep an equivalent explanation in the text.
- **No decoration.** No decorative badges, repeated banners, excessive emphasis or unverified status
  indicators (build, coverage or version badges the repository can't back).
- **One vocabulary.** Use one term per concept, command and status throughout. Keep each
  operational fact in one maintained place and link to it.

## Concise reporting

For changelog entries, release notes and summary sections:

- Lead with the changed capability or behavior.
- One sentence per item where practical; add a second only for a necessary condition, action or
  limitation.
- Aim for about 15–35 words per changelog entry, as a default and not a hard limit.
- Group implementation steps that deliver the same result into one item.
- Use exact technical names when the reader needs them to act (`--exclude`, `API_URL`).
- Leave individual files, functions, commits and test cases out of summaries unless they matter to
  the reader.
- Replace vague claims ("various improvements", "better performance") with the concrete outcome.
- Keep required migration, compatibility and operational details, even when they make an entry
  longer.
- Link to deeper guidance instead of duplicating it.

Illustrative wording, not claims about any repository:

| Raw implementation detail | Reader-facing entry |
|---|---|
| Added a parser, renderer and tests for an output format. | Added CSV export for filtered monitoring results. |
| Changed timer handling and added a regression test. | Fixed dashboard refreshes stopping after a temporary connection failure. |
| Renamed a required environment variable and updated validation. | **Breaking:** Replaced `API_HOST` with `API_URL`, which takes a full URL; update deployment environments before upgrading. |
| Added a worker pool around image resizing. | Added `--workers N` to resize up to N images at the same time. |

The last row says what the option does. It doesn't say "faster", because nothing measured it.

## Words to check before delivery

| Avoid | Unless | Otherwise write |
|---|---|---|
| faster, quicker, speeds up, more efficient, N% | A measurement in the repository or the session supports it | The concrete behavior (parallel, cached, streamed) |
| more reliable, robust, stable, secure, hardened | Evidence shows the failure mode and its fix | The corrected behavior and the condition it affected |
| tested, passes, verified | A test run passed in this session or in a recorded result | Nothing, or "adds tests for …" in contributor docs only |
| released, available in vX, now live | A tag, published version or release record includes it | "Unreleased", or leave the status to the changelog section |
| production-ready, complete, fully supports | The project's own status claims it, with evidence | The implemented scope and its known limits |
