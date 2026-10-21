# Slice 4 screen (approved, DEC-041)

Screen 16, the audit log, answering the slice-4 screen handoff (HANDOFF rule 3). Built on the approved
design system (`../../system/`, DEC-039) and matching the approved slice-3 boards (`../slice-3/`,
DEC-040): the same tokens, shell and table pattern. Approved by the owner on 2026-10-21 as published
on the Claude Design canvas https://claude.ai/artifact/8oEjLkZAiNJ14qTT3ssJjs (version 13,
`1792581498-3ae5`). The UI code must match these boards.

These files are the canvas's source, the published version's files, unchanged. Like the other
mockups they need the Claude Design runtime and render unstyled when opened directly; review them on
the canvas. `canvas.json` is the canvas index (board positions and titles).

| Board | File |
|---|---|
| Admin view, light and dark at 1440, an entry expanded | `Main.dc.html`, `AdminDark.dc.html` |
| Auditor view, light and dark, Outcome filter set | `Auditor.dc.html`, `AuditorDark.dc.html` |
| 1024: icon rail, IP address in the details | `Audit1024.dc.html` |
| Export confirmation over the page | `AuditExport.dc.html` |
| States, light and dark (loading, empty, error, 403, retention notice, names, tooltips, keyboard) | `AuditStates.dc.html`, `AuditStatesDark.dc.html` |
| Export dialog states, light and dark | `AuditDialogs.dc.html`, `AuditDialogsDark.dc.html` |
| Component: the page the boards share | `AuditPage.dc.html` |
| Notes: Fluent v9 components, changes, build notes | `Notes.dc.html` |

Changes from the handoff, all on the Notes board:

- **Expandable rows use Fluent's Table, not DataGrid**, which has no expandable rows. There is one
  expander button per row and a details row spanning the columns.
- **The IP address header has an info button with a Tooltip:** "The address that connected to
  Goldmark. Behind a reverse proxy, this is the proxy's address." (DEC-041 item 4, #31)
- **A "Link code wrong" entry was added to the sample data**, so the Failed filter shows three
  entries.

As for slice 3, the shell's search box and As-of control stay absent until slices 5 and 7.

Where the built UI (`web/src/pages/AuditPage.tsx`) differs from the boards, and why:

- **The actor-type badge is Fluent's `Badge appearance="tint" color="subtle"`.** That is the look the
  boards draw, the same as the role badges on screen 17; the Notes board's "outline" was a slip in
  naming the component.
- **The custom time range uses the browser's own date-and-time fields**, read as UTC, because Fluent v9's
  core has no date picker; a date-picker package would be a new dependency.
- **"Show this person in Users & roles" opens screen 17 filtered by that username** (`/users?q=`).
- **Each expand button names its entry:** "Show details for entry 48213: Roles changed at 16:03:40",
  not the board's "Show details for the entry at 16:03:40". Several entries are often written in the
  same second, and screen readers must tell them apart (NFR-A11Y-1; owner, DEC-041 item 7).

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas,
pull the file back here first.
