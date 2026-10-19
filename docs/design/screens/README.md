# Goldmark screen mockups (design review)

These 21 `.dc.html` files are the source for the screen mockups of PRD §7
(prompts: `docs/specs/claude-design-prompts.md`). They are design references,
not application code. Sample data is fictional.

The same files are published to two Claude Design canvases:

- Screens at 1440 px plus Shared states: https://claude.ai/artifact/TEcmVAhX1j92bYb1GGnrPZ
- Per-screen states plus 1024 px frames: https://claude.ai/artifact/NkAPSmEKo35iDztQCMbPRt

Each file is responsive: below 1100 px it switches to the 1024 layout (icon
nav, overlay drawers, dropdowns). The copies in this folder carry the 1024
canvas's preview size (`$preview` width 1024); the 1440 canvas uses its own
frame sizes. Apart from `$preview`, both canvases hold identical files.

When a screen changes, edit the file here and publish it to both canvases.
If someone edits a canvas directly, pull that file back here first.

Not stored here (they exist only on the canvases): the six per-screen state
boards (`ST1`–`ST6`), the Shared states board and each canvas's `canvas.json`.

Status: review input, not approved designs. They are a starting point for
the design-system handoff and the per-slice design handoffs in
`docs/HANDOFF.md` (rules 3 and 4), which still happen. Where `DECISIONS.md`
or `HANDOFF.md` differs from a mockup, those documents win.

Checked against `DECISIONS.md` and `HANDOFF.md` on 2026-10-19:

- DEC-021 moved Settings (#20) to P1. The mockups now tag the whole Settings
  screen and its nav item P1, like Apps & integrations.
- Fluent UI v9 is unverified under the served CSP (DEC-017; first step of
  slice 3). The mockups imitate Fluent styling in plain markup; if Fluent
  fails, the component choices change.
- The "As of" control appears on every mockup, but slice 7 adds it (screens
  4, 5, 6). Screens built in slices 3–6 ship without it until then.
- P1 elements tagged in the mockups match the P1 slices (25, 28, 29) and
  Settings. Screen 14 is built in slice 5b; its design is unaffected.

`Main.dc.html` is screen 3, Dashboard. The file name is required by the
canvas format.
