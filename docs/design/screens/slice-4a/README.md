# Slice 4a screens (approved, DEC-042)

Password recovery (issue #25), answering the slice-4a screen handoff (HANDOFF rule 3): the
administrator's password reset and the password-and-two-factor reset on screen 17, their two flows on
the link page (screen 1b), dual control for invitations and the combined reset, the sign-in caption,
and Change password for a signed-in person. Built on the approved design system (`../../system/`,
DEC-039) and extending the approved slice-3 boards (`../slice-3/`, DEC-040) with their components.
Approved by the owner on 2026-10-21 ("approved.") as published on the Claude Design canvas
https://claude.ai/artifact/Bzpjzj8maiwQGfPJ31KCy3 (version 28, `1792617023-b865`). The UI code must
match these boards.

These files are the canvas's source, the published version's files, unchanged (48 files, sizes checked
against the published listing). Like the other mockups they need the Claude Design runtime and render
unstyled when opened directly; review them on the canvas. `canvas.json` is the canvas index (board
positions and titles).

| Screen | Boards | Components |
|---|---|---|
| 17 Reset password | `Main.dc.html` (row menu), `UsersResetPw*.dc.html` (confirmation, light and dark, with and without "Also reset two-factor"), `UsersPwLink.dc.html`, `UsersPwTfLink.dc.html`, `UsersAccount.dc.html` (Account column wordings, disabled-account guard), `UsersSelf.dc.html`, `Users1024Pw.dc.html`, `UsersAdminTarget.dc.html` | `UsersPage.dc.html`, `UsersPanel.dc.html` |
| 17 Dual control | `UsersRequestLink.dc.html`, `UsersSingleAdmin.dc.html`, `UsersAwaiting.dc.html`, `UsersApproverMenu.dc.html`, `UsersRequesterMenu.dc.html`, `UsersApprove*.dc.html`, `UsersCode*.dc.html`, `UsersDecline.dc.html` | `UsersPage.dc.html`, `UsersPanel.dc.html` |
| 1b Link page | `ResetPw.dc.html`, `ResetPwDark.dc.html`, `ResetPwSteps.dc.html`, `ResetPwStates.dc.html`, `ResetPwPhone.dc.html`, `ResetBothSteps.dc.html`, `LinkPending.dc.html` | `WizardCard.dc.html` |
| 1 Sign-in | `SignIn.dc.html`, `SignInDark.dc.html`, `SignInPhone.dc.html` | `SignInCard.dc.html` |
| Account menu, Change password | `AccountMenu*.dc.html`, `ChangePw.dc.html`, `ChangePwDark.dc.html`, `ChangePwDone.dc.html`, `ChangePw1024.dc.html`, `ChangePwStates.dc.html` | `TwoFactorPage.dc.html`, `ChangePasswordDialog.dc.html` |
| 16 Audit log, new entries | `AuditPw.dc.html` | `AuditPage.dc.html` |
| Notes | `Notes.dc.html`: Fluent v9 components, the owner's decisions, details settled on the boards, build notes | |

The owner's decisions the boards show (2026-10-21, DEC-042): the combined reset is offered for anyone
but an Admin; the password-only reset needs the person's authenticator or a recovery code before the
new password takes effect; the old password stops working at once; Change password for signed-in
people; dual control for invitations and the combined reset, with the single-Admin fallback.

Details settled on the boards (Notes board, "Settled on these boards"), approved with them:

- the shell is the sparse nav as built (no search box, no As-of control; Audit log added in slice 4);
- with dual control, a combined reset changes nothing on the account until another Admin approves;
- a request waits up to the link lifetime for approval, and the link's lifetime counts from the
  approval;
- issuing a reset ends a lockout from failed attempts; an administrator's lock stays;
- the sign-in caption is text, not a link: "Forgot your password? Ask an administrator to reset it.";
- a lockout in the Change password dialog keeps the person signed in, as on the two-factor page;
- the audit action codes on the boards are samples.

In the handoff (`tmp/screen-handoff-slice4a.md`) but not drawn: the Add user panel's button reading
"Create and ask for approval", the Account filter value "Awaiting approval", the "Request declined"
Toast, and the reset variant of the Approve dialog's wording. The recovery setting
(`GOLDMARK_RECOVER_ADMIN`, DEC-042 item 6) has no screen.

When a board changes, edit the file here and publish it to the canvas; if someone edits the canvas,
pull the file back here first.
