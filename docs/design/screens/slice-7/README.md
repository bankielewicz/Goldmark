# Slice 7 screens (approved, DEC-047)

History and "as of", answering the slice-7 screen handoff (HANDOFF rule 3):
- the top bar's As-of control and its dialog "Show Goldmark as of", which work on screens 4, 5 and 6;
- screens 4 (Search), 5 (a CI's page) and 6 (Graph explorer) as of a past moment: read-only, under a banner;
- screen 5's new History tab: the CI's changes, newest first, with who made each and a diff.

The boards are built on the approved design system (`../../system/`, DEC-039) with the shell and patterns of the
approved slice 3 to 6 screens. The Search, CI and Graph page components are the approved ones with an as-of state
added; nothing changes on them when no moment is chosen, except the CI page's new History tab.

The owner approved them on 2026-11-19 ("A. Approve as drawn (Recommended)"), with the Notes board's 12 defaults, as
published on the Claude Design canvas https://claude.ai/artifact/BKdXWuabswWXgQ98WaNC7b (version
`1795092518-f2f7`). The same day the owner settled the two details the Notes board left open: the past-view banner
stays amber, Fluent's warning intent, as drawn ("A. Amber (warning), as drawn (Recommended)"; the design system's
"As of" state, drawn grey with the info intent, changes to match), and the search text in the count line takes
single quotes in a past view too ("A. Single ‘erp’ for both (Recommended)"), as slice 5 was approved and built. The
UI code must match these boards.

These files are version `1795092518-f2f7`'s files, unchanged: 45 files, checked against the canvas's SHA-256 of
each. Like the other mockups they need the Claude Design runtime and render unstyled when opened directly; review
them on the canvas. `canvas.json` is the canvas index (board positions and titles). Every board is a small wrapper
that sets the properties of a page component:
- `SearchPage.dc.html`: state, role, drawer, panel, asof (off, on, nothing), rotip, dialog, dark, width, height;
- `CiPage.dc.html`: ci, role, state, tab (attributes, relationships, history), rels, dialog, panel, toast, menu,
  tip, search, asof (off, on, notyet), rotip, hist-sel, hist-state, dark, width, height;
- `GraphPage.dc.html`: role, view, state (… notyet), drawer, dialog, asof, dark, width, height;
- `HistoryTab.dc.html` (inside the CI page): cols, ci, sel, state, mark (the "Shown above" badge), height;
- `TopBar.dc.html`: search, asof (now, on, early, disabled), tip; `AsOfDialog.dc.html`: mode; `Nav.dc.html`.

| Screen | Boards | Components |
|---|---|---|
| Top bar, the As-of control | `TopBarStates.dc.html` (Now, a moment chosen, disabled with its Tooltip on a screen with no past; dark; 1024 and phone) | `TopBar.dc.html` |
| The dialog "Show Goldmark as of" | `DialogStates.dc.html` (empty, typed with the local hint, reopened with Return to now, each error; dark), `SearchAsOfDialog.dc.html` (in place, over Search) | `AsOfDialog.dc.html` |
| 4 Search as of | `SearchAsOf.dc.html` ("erp" at 2026-10-06 14:00 UTC), `SearchAsOfDark.dc.html`, `SearchAsOfTip.dc.html` (New CI disabled, its Tooltip), `SearchNothing.dc.html` (nothing existed yet), `SearchAsOf1024.dc.html`, `SearchAsOfPhone.dc.html` | `SearchPage.dc.html` |
| 5 CI detail as of | `CiAsOf.dc.html` (Cost centre "Deleted since", Edit's Tooltip), `CiAsOfDark.dc.html`, `CiAsOfRels.dc.html` (runs_on esx-mx-011 "Ended since"), `CiNotYet.dc.html` (app-erp-db02 didn't exist yet), `CiAsOfPhone.dc.html`, `CiAsOfHistory.dc.html` (History marks the entry "Shown above") | `CiPage.dc.html` |
| 5 History tab | `Main.dc.html` (a CI change and its diff), `HistDark.dc.html`, `HistNewest.dc.html` (as the tab opens), `HistRel.dc.html`, `HistGroup.dc.html`, `HistUnknown.dc.html`, `HistAttrDel.dc.html`, `HistRetired.dc.html`, `HistCreated.dc.html`, `HistLoading.dc.html`, `HistError.dc.html`, `HistMore.dc.html` (esx-mx-014, Show 50 more, a Former user), `HistMoreLoading.dc.html`, `HistMoreError.dc.html`, `Hist1024.dc.html`, `HistPhone.dc.html` | `CiPage.dc.html`, `HistoryTab.dc.html` |
| 6 Graph explorer as of | `GraphAsOf.dc.html`, `GraphAsOfDark.dc.html`, `GraphAsOfTable.dc.html`, `GraphNotYet.dc.html`, `GraphAsOfExport.dc.html` (Export CSV… names the moment) | `GraphPage.dc.html` |
| Notes | `Notes.dc.html`: the details to approve with their reasons, three checks of the handoff, the design system changes, the Fluent components by board | |

## The owner's decisions the boards show

DEC-047 (2026-11-18 and 2026-11-19):
1. The reads behind the three screens answer for a past UTC moment, and a past view is read-only (items 2 to 5).
2. A CI's history lists its changes and its relationships', each with who made it and what differed (item 6).
3. Who ended a relationship is kept with its history and shown to everyone signed in (items 7 and 9).
4. The boards as drawn, the banner amber, single quotes in the count line (item 10).

## Details settled on the boards (approved with them)

The Notes board lists each with its reason. In brief:
1. **One As-of control, in the top bar**, for Search, a CI's page and the Graph explorer (HANDOFF row 7 now says so).
2. **On other screens** it's disabled ("This page always shows the present.") and the moment is kept for the
   browser session; a link without `asOf` opens the present.
3. **History** is a timeline with a detail pane from 1100 px; below that, one column where an entry expands in place.
4. **"Created"** lists every starting value, each as Added.
5. **No filter or search** inside a CI's history.
6. **Quick choices** 1 hour ago, Yesterday, 7 days ago: they fill the fields; Apply applies.
7. **Minutes, not seconds, in the picker**; a history entry's "View this CI as of this time" uses its exact moment.
8. **An ending nobody is on record for** reads "Unknown", with "The audit entry that recorded who ended it is no
   longer kept."
9. **"Ended since"** on a relationship in a past view (not in the handoff).
10. **Disabled actions stay visible and focusable**, with one Tooltip: "You're viewing a past time. Return to now to
    make changes."
11. **The banner's icon** is the clock-arrow; its intent warning (amber).
12. **Small wording:** the short label keeps the year when it isn't this year; "Nothing had been entered in Goldmark
    yet."; the "didn't exist" page says when the CI was created; a history entry's relationship keeps the
    present-tense Direction under an "Ended" badge.

The Notes board also corrected the handoff: one time zone for the local-time hints (UTC+2 in summer, UTC+1 in
winter), and the CI page's sample data follows its history (slice 6's boards are untouched).

## As built, the differences from the boards

- The count line's search text takes single quotes in a past view too: "1,231 CIs matched ‘erp’ at that time" (the
  board writes “erp”), as the present line does.
- The dialog's Date is the browser's date field, with the browser's calendar and date format, and its Time is typed
  as 24-hour HH:MM: no new package (owner, 2026-11-19, "A. Browser date field, typed time (Recommended)"; DEC-047
  item 10). The board draws Fluent's DatePicker and TimePicker, which aren't installed.
- A past Graph explorer's status line, table caption and export summary keep what the present ones say (", plus
  redundancy groups", "retired CIs left out") before "at that time" and "as of …"; the boards' text is shorter
  (owner, 2026-11-20, "A. Keep the full text (Recommended)"; DEC-047 item 11).

Where the boards are silent, the code does this (assumptions, DEC-047 item 11):
- The local-time hint names the date when the local day isn't the UTC day: "01:30 on 2026-10-07 your local time".
- A long value's "Show all" becomes "Show less" once opened; the boards draw only the folded state.
- A dependent's redundancy-group change reads "Changed redundancy group: depended on by X (type)", after the boards'
  "Added relationship: depended on by X (type)".
- Below 1100 px, with a moment chosen, the control's name is "As of: 06 Oct 14:00 (2026-10-06 14:00 UTC). Choose a
  point in time": the short label it shows, then the whole moment the boards give as its name, so the words on the
  button are in its name (WCAG 2.5.3; axe refuses the boards' name there).
- The timeline scrolls inside 70% of the window's height from 1100 px (the boards give a fixed artboard height).
  Below 1100 px the selected entry opens in place, as the Notes board's "Accordion, one panel open" says; the
  boards' markup draws the detail between two lists.
- The timeline's avatars are 20 px, the Notes board's size and one Fluent has (the template draws 18).
- In a past view the tab opens with the newest entry selected, as in the present (the handoff's rule); the
  `CiAsOfHistory` board happens to select the entry marked "Shown above".
- Errors use the shared error bar of the other screens, with its second text for a server error.
- A past view's Relationships tab with nothing in it reads "X had no relationships at that time."
- Below 1100 px a past view's More actions menu lists Edit and Retire… (or Restore) disabled, by the Notes board's
  rule 10; the menu isn't drawn open.
- On a CI that was retired at the moment shown, the note's Restore stays, disabled with the Tooltip (rule 10).
- The Graph explorer's "Choose another time" is a button that opens the dialog, as on Search's board (the Graph
  board draws a link).
- The diff table scrolls sideways in its own box when it is wider than the screen; the box is a Tab stop.

## What the API gives for them

- "It was created on …, after the time you're viewing": the 404 `not-found-then`'s `createdAt`.
- "Ended since": `endedSince` on a relationship read as of a moment.
- "Deleted since": the type's attributes then (`GET /api/v1/types?asOf=`) against now.
- "Shown above": the newest of the CI's own entries (created, changed, retired, restored) at or before the moment.
- "Former user": an entry's `by` with an `id` and no `displayName`.

## Changes to the design system (approved with the boards)

From the Notes board, to make in `../../system/` and on its canvas with slice 7's UI:
- States board, "As of": amber (warning) with the clock-arrow icon, the local-time hint and a 28 px "Return to now"
  (drawn grey, info, with a 24 px button and no hint).
- Shell board: the top bar's slot holds the As-of control (its states as on `TopBarStates.dc.html`).
- Badges: four subtle (tint-subtle) badges: "Deleted since", "Ended since", "Shown above", and the diff's "Added",
  "Removed" and "Changed" (one style; the word carries the meaning).
- Icons: one per kind of change (AddCircle, Edit, SubtractCircle, Archive, ArrowCounterclockwise, Link, ArrowSwap,
  LinkDismiss) and History (the clock-arrow), all bundled Fluent icons.

## In the handoff, not drawn

- Search's and the Graph explorer's loading, error and no-match states in a past view: as approved, with "at that
  time" where a count is named;
- Restore disabled on a CI that was retired at the chosen moment, and the More actions menu at 1024 with its
  disabled items (the Notes board's rule 10 covers them);
- impact "as of" (slice 19), source badges and a history filter by source (slices 10 and 11), the estate-wide change
  history, screen 15 (slice 23).

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas, pull the file
back here first.
