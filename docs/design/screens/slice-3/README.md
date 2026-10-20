# Slice 3 screens (approved, DEC-040)

Screens 1 (sign-in), 1b (accept invitation / set up two-factor again), 2 (first-run setup), 17
(Users & roles) and the self-service two-factor page, answering the slice-3 screen handoff
(HANDOFF rule 3). Built on the approved design system (`../../system/`, DEC-039). Approved by the
owner on 2026-10-20 as published on the Claude Design canvas
https://claude.ai/artifact/Fmq9Gc7pAKZ1XMHc2zVy8k (version 29). The UI code must match these boards.

These files are the canvas's source, copied from its published version and checked file by file
against the source the session published from. Like the other mockups they need the Claude Design
runtime and render unstyled when opened directly; review them on the canvas. `canvas.json` is the
canvas index (board positions and titles).

| Screen | Boards | Components |
|---|---|---|
| 1 Sign-in | `Main.dc.html` (light, credentials), `SignInDark.dc.html` (dark, code), `SignInPhone.dc.html`, `SignInStates.dc.html` | `SignInCard.dc.html` |
| 2 First-run setup | `Setup.dc.html`, `SetupSteps.dc.html`, `SetupStates.dc.html` | `WizardCard.dc.html` |
| 1b Invitation and reset | `Invite.dc.html`, `InviteSteps.dc.html`, `InviteStates.dc.html`, `InvitePhone.dc.html` | `WizardCard.dc.html` |
| 17 Users & roles | `Users.dc.html`, `UsersDark.dc.html`, `UsersAdd.dc.html`, `UsersInvite.dc.html`, `Users1024.dc.html`, `UsersReset.dc.html`, `UsersStates.dc.html` | `UsersPage.dc.html`, `UsersPanel.dc.html` |
| Two-factor page | `TwoFactor.dc.html`, `TwoFactorDark.dc.html`, `TwoFactor1024.dc.html`, `TwoFactorFlows.dc.html` | `TwoFactorPage.dc.html`, `TwoFactorDialog.dc.html` |
| Notes | `Notes.dc.html`: Fluent v9 components per screen, changes to the handoff, notes for the build | |

Where the backend settled a detail the boards show as a sample, the backend wins (DEC-040):
recovery codes are Identity's format `XXXXX-XXXXX` (the boards show `7KQ2-M9XD`); revoking an unused
invitation removes the account it created; after a two-factor reset, sign-in with the right password
says "Two-factor was reset for this account. Open the link your administrator gave you."

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas,
pull the file back here first.
