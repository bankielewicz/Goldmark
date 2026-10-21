# Endpoint access policy

Every endpoint declares its access policy (PRD US-04, M6), and anything else is denied: the
fallback authorization policy refuses every application endpoint without a policy, to everyone,
signed in or not (deny by default, NFR-SEC-2). A request that matches no endpoint gets its 404, and a
known path with the wrong method its 405. `EndpointInventoryTests` (in-process) and
`AccountAndAccessTests` (against SQL Server) are the M6 enumeration test; change this file, the
inventory in `EndpointInventoryTests` and the endpoint together.

An endpoint is either **anonymous by design** (`AllowAnonymousByDesign(rationale)`) or requires one
policy (`RequireAccess(policy)`, which sets the authorization policy and this metadata in one call):

| Policy | Who passes |
|---|---|
| `SignedIn` | A signed-in session (password and second factor), any role |
| `Admin` | A signed-in session with the Admin role |
| `AuditReader` | A signed-in session with the Admin or the Auditor role (the audit log, US-04, US-07) |
| `TwoFactorPending` | A password verified within the last 5 minutes, second factor not yet given (sign-in step 2 only) |
| `Enrolment` | An enrolment in progress, for 30 minutes: first-run setup after the setup token, an invitation or two-factor reset after its code, or a new authenticator after the password and a code |

The three cookies are `__Host-` (Secure, Path=/, no Domain), HttpOnly and SameSite=Strict, encrypted
by ASP.NET Core Data Protection. A session lives on the server (`cmdb.UserSession`): sign-out, an
administrator's lock or disable, and a two-factor reset end it there, and it ends after 30 minutes
idle or 12 hours (DEC-038, DEC-040). Every POST, PUT, PATCH and DELETE under `/api/v1` needs the
anti-forgery token in `X-Goldmark-Antiforgery` (from `GET /api/v1/auth/antiforgery`); API responses
are `Cache-Control: no-store`. Requests that take a credential are rate limited per client address
(`Goldmark:Auth:RequestsPerMinute`, 30 by default; HTTP 429).

The M6 tests check that every non-anonymous endpoint answers 401 without credentials and 401 to a
password-only sign-in, every Admin and AuditReader endpoint 403 to a signed-in Viewer, and every
state-changing endpoint 400 without the anti-forgery token.

## Anonymous by design

| Endpoint | Why anonymous | What it can reveal |
|---|---|---|
| `GET, HEAD /health/live` | Container orchestrators, load balancers and the image `HEALTHCHECK` call it without credentials | `Healthy` only; `Cache-Control: no-store` |
| `GET, HEAD /health/ready` | As above; readiness gates traffic | `Healthy` / `Unhealthy` only. No component names, versions, host names or error text; failure details go to the log (exception text at Debug only) |
| `GET, HEAD /health/{**path}` (unmatched health paths) | Catch-all `404`, so a mistyped probe path never gets the SPA shell with `200` | Nothing: a generic 404 |
| `* /api/{**path}` (unmatched API routes) | Catch-all Problem Details `404`, so unknown API paths never return the SPA shell | Nothing: a generic 404 |
| `GET, HEAD {**path:nonfile}` (SPA shell) | The browser must load the sign-in screen before the user is authenticated | Static HTML with no data, plus a fresh style nonce per response (DEC-037); `no-store` |
| Static files (`/assets/*`, `/favicon.svg`) | Same as the SPA shell (static file middleware, not an endpoint) | Compiled front-end code; no data or secrets. HTML is never a static file (DEC-037) |
| `GET /api/v1/auth/antiforgery` | The SPA needs an anti-forgery token before it can sign in | A token bound to the caller's own session, or to no one |
| `GET /api/v1/auth/session` | The SPA must know whether to show sign-in or setup | The caller's own state: setup status and, when signed in, their own id, user name, display name and roles |
| `POST /api/v1/auth/sign-in` | Sign-in step 1: the password is the credential | Only "incorrect", "locked" or "next: two-factor". Unknown, disabled and not-yet-set-up accounts answer exactly like a wrong password, including the lockout; a person locked by an administrator, or whose two-factor was reset, learns it only with the right password |
| `GET /api/v1/setup` | The SPA must know whether first-run setup is needed | `required`, `complete` or `unknown` |
| `POST /api/v1/setup/token` | First-run setup step 1: the setup token from the log is the credential | Whether the token is this process's; nothing once setup is complete (409) |
| `POST /api/v1/links/check` | An invitation or reset link's token is the credential | Whether the link is valid, expired, no longer valid (used and revoked look the same) or not active yet (awaiting another administrator's approval, DEC-042), its purpose and the tries left |
| `POST /api/v1/links/redeem` | The token plus its separate code are the credential; 5 wrong codes revoke the link | With the right code: the person's user name, display name and roles. A link awaiting approval has no code: 409 `link-pending`, and nothing counts as a wrong try |

## Requiring a policy

| Endpoint | Policy | Notes |
|---|---|---|
| `POST /api/v1/auth/sign-in/two-factor` | TwoFactorPending | A code works once; a wrong code or recovery code counts toward lockout. Starts a new session, ending the one the browser presented |
| `POST /api/v1/auth/sign-out` | SignedIn | Ends the session on the server |
| `GET /api/v1/enrolment` | Enrolment | The enrolment's next step and whose it is |
| `POST /api/v1/enrolment/account` | Enrolment | Setup only: the first administrator's user name, display name and password |
| `POST /api/v1/enrolment/password` | Enrolment | Invitation and password resets: a new password; two-factor reset: the current one (failures count toward lockout) |
| `POST /api/v1/enrolment/authenticator` | Enrolment | A new authenticator key, kept in the enrolment until a code proves it |
| `GET /api/v1/enrolment/authenticator.svg` | Enrolment | The QR code, SVG, `no-store`, only to the person enrolling |
| `POST /api/v1/enrolment/complete` | Enrolment | Verifies a code and commits the setup, invitation or reset in one transaction; returns 10 recovery codes once and signs in (a new session). Refused for a password reset, which keeps its authenticator |
| `POST /api/v1/enrolment/confirm` | Enrolment | Password reset only (DEC-042): a code from the person's own authenticator, or a recovery code (failures count toward lockout), then the new password, the used link and a new session commit together |
| `GET /api/v1/account/two-factor` | SignedIn | Own status: set up since, recovery codes remaining |
| `POST /api/v1/account/two-factor/recovery-codes` | SignedIn | Needs the password and a current code; returns 10 new codes once |
| `POST /api/v1/account/two-factor/authenticator` | SignedIn | Needs the password and a current code or a recovery code; starts the new-authenticator enrolment |
| `POST /api/v1/account/two-factor/authenticator/verify` | SignedIn | Installs the new authenticator; other sessions end, this one continues |
| `GET /api/v1/users`, `GET /api/v1/users/{id}`, `GET /api/v1/users/export` | Admin | Screen 17's list, one person (with ETag), CSV export (formula cells neutralized) |
| `POST /api/v1/users` | Admin | Creates an invitation (Editor, Viewer or Auditor only); the link token and code are shown once |
| `PUT /api/v1/users/{id}/roles` | Admin | Needs If-Match (428 without, 412 when stale); Admin only for someone who has signed in; nobody removes their own Admin; the last enabled Admin keeps it; Collector is refused |
| `POST /api/v1/users/{id}/lock`, `/unlock`, `/disable`, `/enable` | Admin | Not on yourself; the last enabled Admin cannot be disabled; lock and disable end the person's sessions |
| `POST /api/v1/users/{id}/two-factor-reset` | Admin | Not on yourself; clears the authenticator and recovery codes, ends sessions, issues a link and code |
| `POST /api/v1/users/{id}/invitation`, `DELETE /api/v1/users/{id}/invitation` | Admin | A new invitation link (the old one stops working); revoke, which removes the unused account |
| `GET /api/v1/audit` | AuditReader | Screen 16: audit entries, newest first, filtered by time, actor, person affected, action, outcome and IP prefix, paged by entry number; every filter is validated and passed as a parameter. Reading writes nothing |
| `GET /api/v1/audit/catalog` | AuditReader | The actions with their group, label and outcome, and the retention period `migrate` set (DEC-041) |
| `GET /api/v1/audit/people` | AuditReader | Only the people who appear in the log as actor or person affected (for the filters), not the user directory, which only an Admin sees |
| `POST /api/v1/audit/export` | AuditReader | CSV of the filtered entries, at most 100,000 (422 `export-too-large` beyond, with nothing recorded); writes an `audit.exported` entry with the filters and the count in the same transaction; the count is also in the `X-Goldmark-Entries` header; formula cells neutralized |

If a richer health report is ever needed (component status, versions), it goes on a separate
endpoint that requires the Admin role.
