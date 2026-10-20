# Goldmark design system (approved, DEC-039)

The base for every screen handoff (HANDOFF rules 3 and 4). Approved by the owner on
2026-10-20 as published on the Claude Design canvas
https://claude.ai/artifact/7xy5Ar6Ag7yKpAHYnXMdxg. These files are the canvas's source: like the
mockups in `../screens/`, they need the Claude Design runtime and render unstyled when opened
directly. `canvas.json` is the canvas index (board positions and titles).

| Board | File |
|---|---|
| Tokens · colour (brand ramp, brand tokens, status, neutrals) | `Main.dc.html` |
| Tokens · type, spacing, shape, elevation, density | `Foundations.dc.html` |
| App shell (component used by the five shell boards) | `Shell.dc.html`, `ShellDark.dc.html`, `Shell1024.dc.html`, `Shell1024Overlay.dc.html`, `ShellSparse.dc.html` |
| Patterns | `DataTable.dc.html`, `Forms.dc.html`, `Overlays.dc.html`, `Feedback.dc.html`, `States.dc.html` (supersedes `../screens/00-SharedStates.dc.html`), `Secrets.dc.html`, `Badges.dc.html`, `Unauthenticated.dc.html` |
| Reference | `Accessibility.dc.html`, `ComponentMap.dc.html` (element → Fluent v9 component, deviations, changes to the mockups) |

Token values and contrast ratios were read from the installed `@fluentui/react-components` 9.74.9
themes and component source, not typed from memory. When a board changes, edit the file here and
publish it to the canvas; if someone edits the canvas, pull the file back here first.
