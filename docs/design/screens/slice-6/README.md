# Slice 6 screens (approved, DEC-046)

Relationships, answering the slice-6 screen handoff (HANDOFF rule 3):
- screen 5's new Relationships tab, with the Add relationship and Edit relationship panel and the Delete dialog;
- screen 6, the graph explorer, with its table view;
- the shell's Inventory › Graph explorer item;
- the new entries of screen 16 (Audit log).

The boards are built on the approved design system (`../../system/`, DEC-039) with the shell and patterns of the
approved slice 3, 4, 4a, 5 and 5b screens.

The canvas was reviewed against the early mockups (`../05-CIDetail.dc.html`, `../06-Graph.dc.html`,
`../ST3-GraphImpact.dc.html`) and 11 other CMDBs, by a second Claude session. The owner decided the findings
(DEC-046 items 4 to 14), and the boards were revised to match. The owner approved them on 2026-10-25 ("approved")
as published on the Claude Design canvas https://claude.ai/artifact/LoG2K8RMUfYCK5sHPMeWAV (version
`1792943153-8870`). The UI code must match these boards.

These files are that version's files, unchanged: 38 files, checked against the canvas's SHA-256 of each. Like the
other mockups they need the Claude Design runtime and render unstyled when opened directly; review them on the
canvas. `canvas.json` is the canvas index (board positions and titles). Every board is a small wrapper that sets the
properties of a page component:
- `CiPage.dc.html`: ci, role, tab, rels, panel, dialog, menu, tip, dark, width, height;
- `GraphPage.dc.html`: state, view, drawer, dark, width;
- `AuditPage.dc.html`.

| Screen | Boards | Components |
|---|---|---|
| 5 Relationships tab | `Main.dc.html` (app-erp-db01 as an Editor), `RelsDark.dc.html`, `RelsViewer.dc.html` (a type's description Tooltip), `RelsEmpty.dc.html` (hcx-ma-01), `RelsLoading.dc.html`, `RelsError.dc.html`, `Rels1024.dc.html` (More actions open), `RelsMany.dc.html` (esx-mx-014's 124 dependents, "Show 74 more") | `CiPage.dc.html` |
| Add and Edit relationship | `AddRel*.dc.html` (runs_on, light and dark), `AddRelGroups.dc.html` (two CIs at once, the redundancy group's suggestions), `AddRelErrors.dc.html`, `AddRelPartial.dc.html` (one of two already existed; the panel stays open), `EditRel.dc.html`, `EditRelConflict.dc.html` (412) | `CiPage.dc.html`, `RelPanel.dc.html` |
| Deleting a relationship | `DeleteRel.dc.html`, `DeleteRelDark.dc.html` | `CiPage.dc.html` |
| 6 Graph explorer | `Graph.dc.html` (3 hops from app-erp-db01 plus redundancy groups, ERP selected), `GraphDark.dc.html`, `GraphTable.dc.html`, `GraphGroups.dc.html` (CL-PROD-02 with Group many neighbours on, a group selected), `GraphNoFocus.dc.html`, `GraphLoading.dc.html`, `GraphTooLarge.dc.html` (grouping off), `GraphEmpty.dc.html`, `GraphError.dc.html`, `GraphNotFound.dc.html`, `Graph1024.dc.html` (Filters drawer) | `GraphPage.dc.html` |
| Shell | `NavRail.dc.html` (the 1024 rail's Tooltip); every board (Inventory › Graph explorer, for everyone signed in) | `Nav.dc.html`, `TopBar.dc.html` |
| 16 Audit log, new entries | `AuditRel.dc.html` ("Relationship deleted" expanded) | `AuditPage.dc.html` |
| Notes | `Notes.dc.html`: the owner's decisions, the details settled on the boards with their reasons, what isn't drawn, build notes | |

## The owner's decisions the boards show

The decisions are DEC-046 items 1 to 14 (2026-10-24 and 2026-10-25):
1. Deleting a relationship ends it, and its history keeps it.
2. The redundancy group is an optional text, grouped by dependent CI and text, compared ignoring case.
3. Cytoscape.js with its built-in layouts.
4. The graph walks away from the focus CI in each direction, plus the other members of any redundancy group it
   reaches, each at its true hop.
5. More than 10 neighbours of one type are grouped into one counted node, behind a "Group many neighbours" switch.
6. Several CIs can be added at once.
7. The graph exports as PNG and CSV.
8. A phrase per direction for relationship types waits (#57).
9. No keyboard selection on the canvas in slice 6 (#58).
10. The graph opens at depth 3.
11. "View in graph" comes after Edit and Retire….
12. Both screens show 50 rows, then "Show N more".
13. "Since" comes from a new `CreatedAt` column (migration 0008).
14. The graph's card has no Owner line.

## Details settled on the boards (approved with them)

The Notes board lists each detail with its reason. In brief:
- **The address** holds the whole graph view and joins `docs/integration/deep-links.md`.
- **Retired CIs** are hidden unless shown; the focus CI shows anyway.
- **The 500-CI limit** comes with the "too large" state's three ways out.
- **Line styles:** Impact None draws dotted; a redundancy group draws dashed with its name, never a hull.
- **The CI page's tab** counts both sections, retired CIs included, sorted by type then name.
- **Retired CIs and relationships:** Edit and Delete stay on relationships with a retired CI; there's no Add on a
  retired CI's page.
- **Redundancy suggestions** come from the dependent CI's relationships.
- **After a partial add:** what was added leaves the picker, and Cancel reads Close.
- **Export file names** say what they hold.
- **Source** reads "Manual" until slice 10.
- **The audit group** "Relationships", with its three actions.

One more detail, from the report the owner approved: after an add, the tab behind the panel refreshes at once,
even when the panel stays open after a partial add. The `AddRelPartial` board draws the tab as it was before the add.

## In the handoff (`tmp/screen-handoff-slice6.md`, with its revisions section), not drawn

- the Toasts;
- a panel's saving state and the discard dialog (slice 5's);
- the Edit panel when the relationship was deleted meanwhile, and after "Review their changes";
- the relationship type list open, and the other field messages;
- Show retired on, and the Concentric and Force layouts;
- "… within the current filters" with Clear filters;
- the phone layouts;
- the table view's "Show N more" and its grouped and too-large variants;
- the impact question ("what breaks if this CI fails?"): screen 7, slice 19.

The audit action codes on the Audit board are samples; the backend names them.

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas, pull the file
back here first.
