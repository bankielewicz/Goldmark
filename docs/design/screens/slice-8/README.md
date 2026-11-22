# Slice 8 screens (approved, DEC-048)

API tokens, answering the slice-8 screen handoff (HANDOFF rule 3, `tmp/screen-handoff-slice8.md`):
- screen 18, API tokens (new): the list, Create token, the secret shown once, Revoke, and a token's usage;
- the navigation's Integration group, for Admins only, holding API tokens alone for now;
- a token as who made a change, on screen 16 (Audit log) and screen 5 (a CI's header, its History tab, the edit
  panel's "changed meanwhile" and a relationship's details).

The boards are built on the approved design system (`../../system/`, DEC-039) with the shell and patterns of the
approved slice 3 to 7 screens; the audit log and the CI page are the approved components with a token as who added.

The owner approved them on 2026-11-22 ("A. Approve as drawn (Recommended)"), with the Notes board's 16 defaults and
its design-system changes (DEC-048 item 7). Canvas: https://claude.ai/artifact/QYtrozdmDDxRUzxaWjmgNE (47 boards).

`slice-8-boards.html` is the canvas as the owner saved it on 2026-11-22: one self-contained file that opens in any
browser without the Claude Design runtime, every board in order (SHA-256
`741ae8e25b5dbfb259d51dc386bd5a04d8be53d2bffcec6ddf2a47ce91a4c3f0`). Earlier slices kept each board's `.dc.html`
source instead; the canvas remains the place to change a board.

## The boards

Light at 1440 px unless the title says otherwise.

- 18 · API tokens: all four statuses, "Expires in 9 days", the Last used Tooltip on focus; dark; the row menu of an
  active token (View usage, Revoke…) and of an expired one (View usage only); empty; loading; the list's error.
- Create token: as it opens (90 days, focus in Name); filled with a custom date; "Enter a name." and "Choose at
  least one scope." (light and dark); name taken and date out of range; the request failed.
- Shown once: the dialog closed, focus on the panel's heading; Copy token pressed (Toast "Copied to clipboard",
  Done still disabled); copied and stored, Done enabled (dark).
- Revoke: the confirmation (light and dark); it failed (the MessageBar in the dialog); done (Toast, the row reads
  Revoked).
- View usage: owner sync's last actions (light and dark); a new token, "Not used yet."; loading; error.
- 1024 px: Created by, Created and Last used leave the table; View usage as an overlay drawer. Phone: the table
  scrolls sideways in its box; Create token and View usage take the full width.
- Navigation: an Admin sees Integration › API tokens, an Editor doesn't; below 1100 px the icon rail with its
  Tooltip. An Editor who opens `/api-tokens` gets the existing 403 page.
- 16 · Audit log: filtered to owner sync from "Open in audit log", an entry expanded (light and dark); the new
  action group "API tokens" (created, revoked, used).
- 5 · CI detail: the header and History, "Changed Owner" by owner sync; a token as who, in every place a screen
  names who changed something (light and dark).
- Notes: the details to approve, the design-system changes, the Fluent v9 components.
- Components: the top bar, the navigation with the Integration group, the API tokens page, the dialog "Create
  token", the side panel "Usage of …", the audit log page, the CI detail page and the History tab.

## The Notes board's defaults (approved)

1. Row actions are in the "Actions for …" menu: View usage on every token, Revoke… only on an Active one.
2. "Revoke token" is the Overlays board's dialog: the brand primary button, the consequence in a warning
   MessageBar, focus starting on Cancel (Fluent v9 has no danger button).
3. "Copy token" is confirmed by a Toast "Copied to clipboard"; the button keeps focus.
4. "Expires in N days" from 14 days before, on Active tokens, severe (dark orange) with the Warning icon.
5. A preset counts from the moment of creation; a custom date (tomorrow to 365 days ahead) ends at the end of that
   day, UTC: "Expires 2027-02-20 14:05 UTC", "Expires at the end of 2027-02-20, UTC".
6. Token names are unique among all tokens, revoked and expired ones too, ignoring case and surrounding spaces, and
   up to 100 characters.
7. View usage is a side panel: InlineDrawer from 1100 px, OverlayDrawer below, full width on a phone; the token's
   facts, its last 20 actions and "Open in audit log".
8. A token as who: the Key icon, the name (semibold) and a subtle badge "API token"; in the audit log's Actor column
   the badge leads, as "Person" does; everywhere else it follows the name.
9. No "Bound source" column until collectors exist (slice 13).
10. The design-system additions below.
11. The shown-once panel sits above the table, with a visible label "Token" and a read-only Textarea, so the secret
    wraps on a phone; the new row is highlighted.
12. "Last used" is relative for a day ("9 min ago", "2 h ago"), then a date; "Never" in grey; the UTC time in a
    Tooltip on the focusable time and in the usage panel.
13. With no tokens, the header drops its description and button; the empty block says "No API tokens yet.", the
    description and the one "Create token".
14. Create token's errors appear on submit, under their fields, with focus on the first; "Couldn't create the
    token." is the shared MessageBar with Retry and the reference.
15. Scopes are a 2 × 2 grid of checkboxes (read and write side by side, for CIs and for relationships); the group's
    hint says what no token can reach; one column on a phone.
16. Expired (Clock) and Revoked (Prohibited) are both informative; Active is success.

## Design-system changes this slice needs

- Badge vocabulary: token statuses Active (success, CheckmarkCircle), Expired (informative, Clock), Revoked
  (informative, Prohibited), "Expires in N days" (severe, Warning), all tint with their icon; scopes as subtle badges
  with monospace text; the actor type "API token" beside Person, System and Not signed in.
- Shell navigation: the Integration group, between Inventory and Administration, for Admins only, holding only API
  tokens until the other Integration screens are built.
- Icons: Key (a token, in the navigation and as who), Prohibited (Revoked), Copy, Add.
- Secrets board, "Single value": a visible label "Token", a read-only Textarea, the visible label "Copy token", then
  the checkbox and Done.
- Audit log: the action group "API tokens" (created, revoked, used), the actor type "API token", and a token as a
  Tag "API token: owner sync" in the Actor filter.

## As built

Recorded when the screens are built.
