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

Known differences from later decisions: the mockups follow the prompts as
written in slice 1. DEC-021 (2026-10-19) moved Settings (#20) to P1; the
Settings mockup still shows it as P0 with P1 sections.

`Main.dc.html` is screen 3, Dashboard. The file name is required by the
canvas format.
