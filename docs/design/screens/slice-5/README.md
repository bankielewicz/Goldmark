# Slice 5 screens (approved, DEC-044)

Configuration items, answering the slice-5 screen handoff (HANDOFF rule 3): screen 4 (CI search / list), the
New CI and Edit CI panel, screen 5 (CI detail, Attributes tab), the CI not-found page, the shell's Inventory ›
Search item and top-bar search, and the new entries of screen 16 (Audit log). Built on the approved design system
(`../../system/`, DEC-039) with the shell and patterns of the approved slice-3, 4 and 4a screens. Approved by the
owner on 2026-10-23 ("approved") as published on the Claude Design canvas
https://claude.ai/artifact/6juyvAr2JX1FtmkQZpyiz1 (version 47, `1792759579-3d94`); version 48
(`1792760074-a7ec`) then dropped one sentence for Admins on the no-attributes board (owner, 2026-10-23, "A. Drop
the Admin sentence (Recommended)"). The UI code must match these boards.

These files are the canvas's source, version 48's files, unchanged (49 files, compared byte for byte with the
canvas). Like the other mockups they need the Claude Design runtime and render unstyled when opened directly;
review them on the canvas. `canvas.json` is the canvas index (board positions and titles).

| Screen | Boards | Components |
|---|---|---|
| 4 Search | `Main.dc.html` (all CIs, as an Editor), `SearchDark.dc.html`, `SearchFiltered*.dc.html` ("erp", two types ticked), `SearchRetired.dc.html`, `SearchError.dc.html`, `SearchNoResults*.dc.html`, `SearchEmptyEditor.dc.html`, `SearchEmptyViewer.dc.html`, `SearchLoading.dc.html`, `Search1024Filters.dc.html` | `SearchPage.dc.html` |
| New CI panel | `NewCi.dc.html`, `NewCiDark.dc.html` (all six attribute kinds), `NewCiErrors.dc.html`, `NewCiLink.dc.html` (link to another CI), `NewCiDiscard.dc.html` | `SearchPage.dc.html`, `CiPanel.dc.html` |
| Edit CI panel | `EditCi.dc.html`, `EditConflict*.dc.html` (412), `EditReview*.dc.html` (their changes) | `CiPage.dc.html`, `CiPanel.dc.html` |
| 5 CI detail | `CiDetail*.dc.html` (VirtualMachine, as an Editor), `CiViewer.dc.html`, `CiServer.dc.html` (a built-in attribute), `CiNoAttributes.dc.html`, `CiRetire*.dc.html`, `CiRetired*.dc.html`, `CiLoading.dc.html`, `CiError.dc.html`, `Ci1024.dc.html` (More actions), `CiPhone.dc.html` | `CiPage.dc.html` |
| Shell, CI not found | `CiDetail.dc.html` (Inventory nav, top-bar search), `Shell1024Search.dc.html`, `NotFound.dc.html`, `NotFoundMalformed.dc.html`, `NotFoundDark.dc.html` | `TopBar.dc.html`, `Nav.dc.html`, `NotFoundPage.dc.html` |
| 16 Audit log, new entries | `AuditCi.dc.html` | `AuditPage.dc.html` |
| Notes | `Notes.dc.html`: Fluent v9 components, the owner's decisions, details settled on the boards, build notes | |

The owner's decisions the boards show (2026-10-23, DEC-044): search by name; saved filters, the column chooser
and CSV export later (#45, #46, #47, slice 20); retire and restore, retired CIs hidden unless shown; only the
attributes the PRD names built in; custom attributes added by Admins through the API (screen 14 in slice 5b);
names need not be unique, a CI's type can't change, no Edit while retired, Restore without confirmation; "CI
changed" lists the fields changed, not their values.

Details settled on the boards (Notes board, "Settled on these boards"), approved with them:

- the 412 dialog's wording is the design system's: "This CI changed while you were editing";
- the not-found heading is the design system's "We can't find that CI";
- reviewing their changes: each changed field shows a Changed badge, "Yours: …" and Use mine;
- the CI panel is a modal overlay drawer at every width (480 px); the filters drawer opens from the start side;
- the panel footer puts the primary button first;
- a retired CI offers Restore in the header and in its MessageBar;
- the type badge is subtle with the type's icon; Active is success, Retired is subtle;
- "Show N more types" counts the 20 built-in types with no CIs in the current search;
- screen 16's "Person affected" column becomes "Affected" (a person, or a CI with its type); its filter still
  filters people;
- on a phone the top bar keeps the search icon and the navigation is behind the menu button.

In the handoff (`tmp/screen-handoff-slice5.md`) but not drawn: the Changed column's Tooltip, a failed Load more,
the Toasts "… created", "… restored" and "Changes saved", and the panel's "retired while you were editing"
message. The type icons are stand-ins; the build picks one Fluent UI icon per built-in type.

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas, pull the file
back here first.
