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
| `CiEditor` | A signed-in session with the Admin or the Editor role (creating, changing, retiring and restoring CIs, US-11) |
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
password-only sign-in, every Admin, AuditReader and CiEditor endpoint 403 to a signed-in Viewer, and every
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
| `POST /api/v1/account/password` | SignedIn | Own password change (DEC-042; ASVS 5.0 6.2.2, 6.2.3): the current password (failures count toward lockout) and a new one under the policy; other sessions end, this one continues |
| `GET /api/v1/users`, `GET /api/v1/users/{id}`, `GET /api/v1/users/export` | Admin | Screen 17's list, one person (with ETag), CSV export (formula cells neutralized) |
| `POST /api/v1/users` | Admin | Creates an invitation (Editor, Viewer or Auditor only); the link token and code are shown once. Under dual control (another Admin could approve, DEC-042) the link only: the approver gets the code |
| `PUT /api/v1/users/{id}/roles` | Admin | Needs If-Match (428 without, 412 when stale); Admin only for someone who has signed in, and not while a password-and-two-factor reset is open; nobody removes their own Admin; the last enabled Admin keeps it; Collector is refused |
| `POST /api/v1/users/{id}/lock`, `/unlock`, `/disable`, `/enable` | Admin | Not on yourself; the last enabled Admin cannot be disabled; lock and disable end the person's sessions |
| `POST /api/v1/users/{id}/two-factor-reset` | Admin | Not on yourself, nor while a password reset or a request waits; clears the authenticator and recovery codes, ends sessions, issues a link and code |
| `POST /api/v1/users/{id}/invitation`, `DELETE /api/v1/users/{id}/invitation` | Admin | A new invitation link (the old one stops working; under dual control a new request); revoke, which removes the unused account |
| `POST /api/v1/users/{id}/password-reset` | Admin | DEC-042. Not on yourself, nor on a disabled or not-yet-set-up account, nor while a request waits. Password only: the old password stops working, sessions end, a link and code are shown once. With `alsoTwoFactor` (never for an Admin): under dual control a request (link only) that changes nothing until approved; with no other Admin, the password and authenticator are cleared and the link and code shown |
| `POST /api/v1/users/{id}/request/approve` | Admin | Another Admin approves a waiting request (not their own, not about themselves): the code is made and shown once; a password-and-two-factor reset takes effect then |
| `POST /api/v1/users/{id}/request/decline`, `/request/cancel` | Admin | Decline (another Admin) or cancel (the one who asked) a waiting request; an invitation's unused account is removed |
| `GET /api/v1/users/approvals` | Admin | How many requests the caller could approve now, for the nav count |
| `GET /api/v1/audit` | AuditReader | Screen 16: audit entries, newest first, filtered by time, actor, what an entry is about (a person, or since slice 5b `ci:{id}`, `type:{number}` or `relationship-type:{number}`, since slice 6 `relationship:{id}`), action, outcome and IP prefix, paged by entry number; every filter is validated and passed as a parameter. Reading writes nothing |
| `GET /api/v1/audit/catalog` | AuditReader | The actions with their group, label and outcome, and the retention period `migrate` set (DEC-041) |
| `GET /api/v1/audit/people` | AuditReader | Only the people who appear in the log as actor or person affected (for the filters), not the user directory, which only an Admin sees |
| `GET /api/v1/cis` | SignedIn | Screen 4: CIs whose name contains `q` (any case), of the `type`s given, retired ones only with `retired=true`, in one of six `sort` orders, paged by an opaque `cursor`; the first page also has the total and the count per type. Every parameter is validated and passed as a parameter (DEC-044) |
| `GET /api/v1/cis/{id}` | SignedIn | Screen 5: one CI with its attributes (a number as a decimal string with every digit, DEC-044 item 12), the CIs its attributes link to, who changed it last and its ETag; 404 `not-found` for an unknown ID |
| `POST /api/v1/cis` | CiEditor | Creates a CI (Active) of a type, with its name and attribute values checked against the type (a number as a JSON number or a decimal string, kept exactly or refused: `TooPrecise`, `NumberOutOfRange`); audited `ci.created` in the same transaction |
| `PUT /api/v1/cis/{id}` | CiEditor | Changes the name and attribute values (all of them: `attributes` is required, `{}` clears every value, an attribute not sent becomes unset; a partial save, PATCH, comes in slice 8, #48); needs the ETag in If-Match (428 without it, 412 if changed meanwhile), 409 `retired` for a retired CI; audited `ci.changed` with the fields changed, not their values; a save that changes nothing writes nothing |
| `POST /api/v1/cis/{id}/retire` | CiEditor | Retires the CI (US-11, US-12); If-Match as above; audited `ci.retired` |
| `POST /api/v1/cis/{id}/restore` | CiEditor | Makes a retired CI Active again (DEC-044 item 2); If-Match as above; audited `ci.restored` |
| `GET /api/v1/types` | SignedIn | The CI types (with whether a custom one is hidden) and their attributes, and the relationship types with their descriptions (US-10); each attribute and relationship type has its ETag, and who changed it last once someone has |
| `GET /api/v1/types/usage` | Admin | Screen 14's counts: CIs per type (retired ones included), CIs with a value per attribute and per choice, relationships per relationship type (DEC-045) |
| `POST /api/v1/types` | Admin | Adds a custom CI type: a name (a capital, then letters and digits; unique in any case; it never changes), one of PRD §9.3's six groups and an icon (DEC-045 item 1); audited `type.added` |
| `DELETE /api/v1/types/{type}` | Admin | Deletes a custom type with no CIs (retired ones included) that no other type's attribute and no relationship type names, with its attributes; 409 `built-in`, `has-cis` or `referenced`; audited `type.deleted` |
| `POST /api/v1/types/{type}/hide` | Admin | Hides a custom type (DEC-045 item 5): no new CI of it (`POST /cis` answers `HiddenType`), its CIs keep working; 409 `built-in` or `hidden`; audited `type.hidden` |
| `POST /api/v1/types/{type}/show` | Admin | Shows a hidden type again; 409 `not-hidden`; audited `type.shown` |
| `POST /api/v1/types/{type}/attributes` | Admin | Adds a custom attribute to a type (DEC-044 items 4 and 8), under the type's lock; audited `type.attribute.added` |
| `PUT /api/v1/types/{type}/attributes/{name}` | Admin | Changes a custom attribute's label, required flag and choices (all three; DEC-045 item 2); a choice some CI holds can't be removed (`ChoiceInUse`); needs the attribute's ETag in If-Match (428, 412); 409 `built-in`; audited `type.attribute.changed` with the fields changed |
| `DELETE /api/v1/types/{type}/attributes/{name}` | Admin | Deletes a custom attribute and takes its values out of every CI of the type in the same transaction (DEC-045 items 2 and 6; the definition's and each CI's history keep them, item 8); If-Match as above; answers the number of values removed; audited once, `type.attribute.deleted`, with that count |
| `POST /api/v1/relationship-types` | Admin | Adds a custom relationship type: a name (small letters, digits and `_`), its From and To types (or any), its impact (`Down` or `None`) and a description of up to 400 characters (DEC-045 item 4); audited `relationship-type.added` |
| `PUT /api/v1/relationship-types/{name}` | Admin | Changes a custom relationship type's impact and description; If-Match as above; 409 `built-in`; audited `relationship-type.changed` |
| `DELETE /api/v1/relationship-types/{name}` | Admin | Deletes a custom relationship type no relationship uses; If-Match as above; 409 `built-in` or `in-use`; audited `relationship-type.deleted` |
| `GET /api/v1/cis/{id}/relationships` | SignedIn | Screen 5's Relationships tab (DEC-046): what the CI depends on (`dependencies`) and what depends on it (`dependents`), retired CIs included, each sorted by relationship type then the related CI's name; without `direction` the first page of both with their totals, with `direction` one page after an opaque `cursor`; `limit` 1 to 200 (50 by default); 404 `not-found` for an unknown CI |
| `GET /api/v1/cis/{id}/redundancy-groups` | SignedIn | The redundancy groups on the CI's relationships as the dependent, each with how many use it (the panel's suggestions; texts that differ only in case are one group, DEC-046 item 2) |
| `GET /api/v1/relationships/{id}` | SignedIn | One relationship: its type, both CIs, its redundancy group, when it was added (`createdAt`), who changed it last and its ETag; 404 `not-found` |
| `POST /api/v1/relationships` | CiEditor | Adds a relationship of a type from one CI (the dependent) to another: both must exist and not be retired, have the type's From and To types (`owned_by`'s To a Team or a Person), and differ (`Self`); an optional redundancy group (trimmed, empty is none, at most 200 characters, no control characters); the relationship type and the CIs are locked (shared) until it commits; 409 `exists` for the same from, type and to; audited `relationship.added` with both CIs' IDs, names and types |
| `PUT /api/v1/relationships/{id}` | CiEditor | Changes the redundancy group only (its type and ends never change; absent or empty means none); If-Match as above; works on a relationship with a retired CI; audited `relationship.changed` with the group before and after; a save that changes nothing writes nothing |
| `DELETE /api/v1/relationships/{id}` | CiEditor | Deletes the relationship (DEC-046 item 1: its history keeps it); If-Match as above; audited `relationship.deleted`, recording it whole, as no row is left to look up |
| `GET /api/v1/graph` | SignedIn | Screen 6 (US-40, DEC-046 items 4 and 5): the CIs and relationships `depth` hops (1 to 6, 3 by default) from the CI `ci`, in `direction` `both`, `dependencies` or `dependents`, of the `type`s and `relationshipType`s given, retired CIs only with `retired=true`, more than 10 neighbours grouped unless `group=false`; at most 5,000 relationships, a deeper hop past that left out and `truncated` set; every parameter validated; 404 `not-found` for an unknown CI |
| `POST /api/v1/graph/export` | SignedIn | CSV of every relationship the graph read lists, with its parameters (422 `export-too-large` when the read is cut short, with nothing recorded); writes a `relationship.exported` entry (the focus CI, the filters and the count) in the same transaction (DEC-041 item 3); the count is in `X-Goldmark-Entries`; formula cells neutralized |
| `POST /api/v1/audit/export` | AuditReader | CSV of the filtered entries, at most 100,000 (422 `export-too-large` beyond, with nothing recorded); writes an `audit.exported` entry with the filters and the count in the same transaction; the count is also in the `X-Goldmark-Entries` header; formula cells neutralized |

If a richer health report is ever needed (component status, versions), it goes on a separate
endpoint that requires the Admin role.
