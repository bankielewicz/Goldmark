# Slice 5b screens (approved, DEC-045)

Types & attributes, answering the slice-5b screen handoff (HANDOFF rule 3): screen 14 (its CI types and
Relationship types tabs, the New CI type, Add attribute, Edit attribute and New relationship type panels, the
delete and hide dialogs), the shell's Administration › Types & attributes item, and the new entries of screen 16
(Audit log). Built on the approved design system (`../../system/`, DEC-039) with the shell and patterns of the
approved slice-3, 4, 4a and 5 screens. Approved by the owner on 2026-10-23 ("design canvas has my approval") as
published on the Claude Design canvas https://claude.ai/artifact/FcVKh7SEKGpTH9ev6WeSQK (version 37,
`1792788486-fca8`); version 38 (`1792789350-e8a1`) then added one sentence to the typed confirmation for an
attribute in use, "Each of those CIs will show as changed by you." (owner, 2026-10-23, "A. Rewrite now, say so
(Recommended)", DEC-045 item 6). The UI code must match these boards.

These files are the canvas's source, version 38's files, unchanged (36 files, compared byte for byte with the
canvas). Like the other mockups they need the Claude Design runtime and render unstyled when opened directly;
review them on the canvas. `canvas.json` is the canvas index (board positions and titles). Every board is a small
wrapper that sets `TypesPage.dc.html`'s properties (type, tab, panel, dialog, menu, tip, view, dark).

| Screen | Boards | Components |
|---|---|---|
| 14 CI types tab | `Main.dc.html` (VirtualMachine, as an Admin), `TypesDark.dc.html`, `TypesServer.dc.html` (built-in attributes only, the locked action's Tooltip), `TypesPrinter.dc.html` (a custom type, its More actions menu), `TypesPrinterUsed.dc.html` (Delete type disabled, with the reason), `TypesHidden.dc.html` (a hidden custom type, Show again), `TypesEmpty.dc.html`, `TypesNoMatch.dc.html`, `TypesLoading.dc.html`, `TypesError.dc.html`, `Types1024.dc.html` (the type Combobox) | `TypesPage.dc.html` |
| Panels | `NewType.dc.html`, `NewTypeError.dc.html`, `AddChoices*.dc.html` (List of choices), `AddLink.dc.html` (Link to a CI), `AddErrors.dc.html`, `EditChoice.dc.html` (a choice in use, the Required hint), `EditConflict.dc.html` (412) | `TypesPage.dc.html`, `TypePanel.dc.html` |
| Deleting and hiding | `DeleteUnused.dc.html` (plain confirmation), `DeleteTyped*.dc.html` (typed confirmation), `DeleteType.dc.html`, `HideType.dc.html` | `TypesPage.dc.html` |
| 14 Relationship types tab | `Rels*.dc.html`, `NewRel.dc.html`, `DeleteRel.dc.html` | `TypesPage.dc.html`, `TypePanel.dc.html` |
| Shell | every screen-14 board (the Administration group, Types & attributes first, Admin only) | `Nav.dc.html`, `TopBar.dc.html` |
| 16 Audit log, new entries | `Audit.dc.html` ("Custom attribute deleted" expanded) | `AuditPage.dc.html` |
| Notes | `Notes.dc.html`: the details settled on the boards | |

The owner's decisions the boards show (2026-10-23, DEC-045): custom CI types added, deleted only when unused,
hidden when they have CIs; custom attributes added, changed (label, required, choices) and deleted with a typed
confirmation when in use, the values leaving every CI in one change; promoted attributes later (#53); relationship
types listed, custom ones added, changed (impact, description) and deleted when unused. The Notes board's "four
answers" are DEC-045 items 1 to 4; items 5 and 6 came later and are on the boards.

Details settled on the boards (Notes board), approved with them:

- names never change once created: a type starts with a capital (`Printer`), an attribute with a small letter
  (`costCentre`), a relationship type is small letters, digits and `_` (`replicates_to`);
- an attribute's label, required flag and choices change, its kind and link target don't; a choice in use can't
  be removed; a custom type's group and icon are fixed after creation; a custom relationship type's impact and
  description change, its name and From and To types don't;
- built-in items are locked, their actions shown disabled with the reason in a Tooltip; the type list badges
  custom types only, the selected type's header always shows its badge;
- deleting something that holds values needs its name typed, something unused a plain confirmation; the audit log
  has one entry with the count ("812 values removed"), not one per CI;
- a type with CIs, or one an attribute or a relationship type points to, can't be deleted; deleting a type
  deletes its attributes;
- a hidden type leaves the New CI type picker and Search's type filter and moves to a "Hidden" group; its CIs keep
  working and stay in search results; no new CI of it can be created; links naming it still work; hiding needs a
  plain confirmation;
- turning Required on doesn't touch existing CIs; two Admins editing one attribute get slice 5's 412 dialog;
- `/types`, `/types?type=VirtualMachine` and `/types?tab=relationships` are Admin only (others get the 403 page);
- "Any type" is the first option for a link's target type and a relationship type's From and To.

In the handoff (`tmp/screen-handoff-slice5b.md`) but not drawn: the 403 page for other roles (the shared one),
the Toasts ("Printer added", "Changes saved", "Cost centre deleted (812 values removed)"), a panel's saving state
and its failure MessageBar, the discard dialog (slice 5's), the Edit relationship type panel (the New panel with
Name, From and To read only), and the 1024 navigation rail. The audit action codes on the Audit board are
samples; the backend names them.

As built (`web/src/pages/TypesPage.tsx`, `web/src/ui/TypePanels.tsx`, `web/src/pages/AuditPage.tsx`), the
differences from the boards: the choice list's Add button is named "Add choice" (the panel's own Add saves the
attribute); the Impact column's explanation is a Tooltip on an info button beside the header; "Show again" in the
More actions menu shows the type at once, as the MessageBar's button does; "Any type" has the generic type icon; a
type's count of CIs links to Search with retired CIs shown, as the count includes them; the audit details of the
slice-5b entries leave out the type, which Affected names; the audit filter's label reads "Affected" when it names a
CI, a type or a relationship type, and a CI's entry offers "Show all entries about" too (slice 5's board had it).

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas, pull the file
back here first.
