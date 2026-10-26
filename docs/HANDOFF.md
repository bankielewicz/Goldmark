# Handoff: state of the build

Read this first when resuming work in a new session. It is updated in every
slice PR (status, open issues, new gotchas). Decisions and their rationale stay
in [`DECISIONS.md`](DECISIONS.md); requirements are in [`PRD.md`](PRD.md)
(draft v0.2; the build plan below supersedes PRD §11, see DEC-021).

**As of 2026-10-23:** slices 1 (PR #1), 2 (PR #14), 2b (PR #17), 2c (PR #22), 3 (PR #24), 4
(PR #32), 4a (PR #38), 4b (PR #39) and **5 (PR #49)** are merged, with the handoff and docs PRs #11, #13, #16, #21 and #23, and the dependency PRs #28
(CodeQL action 4.38.3 and a Dependabot group), #29, #44, #50 (React 19.3.1 with React DOM, axe 4.14 and
Playwright 1.64.1, a Dependabot `react` group) and #51. Slice 3 brought first-run
setup, sign-in with TOTP, roles, invitations, deny by default and the M6 test, and the UI for its
screens (DEC-037 to DEC-040; two Codex review rounds, 5 then 4 findings, all fixed). Slice 4 brought
the audit log and screen 16 (DEC-041). Slice 4a brought password recovery and dual control (DEC-042).
Slice 4b brought the Windows service distribution (DEC-036, DEC-043). Slice 5 brought CIs with their types
and attributes, the CI API, search and each CI's page (DEC-044; merged 2026-10-23). Slice 5b brought the Types &
attributes screen and history for the definitions (DEC-045; merged 2026-10-23). **Slice 6**, relationships (approved
2026-10-24, DEC-046), is in progress on `slice/06-relationships`. Docker Compose is the first distribution, not the target deployment (DEC-030, rule 8).

## 1. Slice plan and status

Approved by the owner on 2026-10-19 (DEC-021, amended the same day; slice 2c
added by the owner during slice 2b, DEC-032; slice 4b placed after slice 2c, DEC-036). One PR
per slice, in this order. "Screens" are the PRD §7 UI inventory numbers; a
slice with screens needs a design handoff before any UI code (rule 3 below).

| # | Status | Branch | Scope | Done when | Screens |
|---|---|---|---|---|---|
| 1 | **done** (PR #1) | `claude/slice-01-scaffold` | Skeleton, CI, Dockerfile, Compose (SQL Server 2025 Express), `/health/*`, Serilog JSON, web skeleton with NFR-SEC-9 controls | `docker compose up` serves health + empty SPA; CI green; unlisted npm dependency fails CI | — |
| 2 | **done** (PR #14) | `slice/02-db-baseline` | DbUp baseline: Tenant, CiType, Ci (temporal), RelationshipType, Relationship (temporal), AuditLog; `migrate` command in `db-init`; `goldmark_migrator` + runtime role; readiness schema-version check; migration immutability check; #7. DEC-022…DEC-029 | Migrations idempotent; runtime login cannot run DDL or UPDATE/DELETE on AuditLog (tested against real SQL Server 2025); #7 closed | — |
| 2b | **done** (PR #17) | `slice/02b-existing-sql` | US-01 "use an existing SQL Server" (`docker-compose.external-sql.yml`); audit of adopted/shared-instance principals (#2, #3, #4), run whenever a principal is adopted or revalidated, in **both** bundled and external deployments; Mode `Existing` for logins a DBA creates; no `sa` as administrator outside the bundled stack; #15. DEC-030…DEC-033 | US-01 existing-SQL criteria; #2–#4 and #15 closed | — |
| 2c | **done** (PR #22) | `slice/02c-generated-credentials` | Generated administrator, migration and runtime credentials (prefixed random names, random passwords) stored in volumes; the app reads its password from a file; a CONNECT-only login for the SQL container's healthcheck; `sa` disabled after the first run, with a tested recovery (DEC-032) | `deploy/.env` holds only the `sa` password the image needs; `sa` disabled; recovery documented and tested; #18, #19 closed | — |
| 3 | **done** (PR #24) | `slice/03-auth` | **Fluent UI v9 CSP/Trusted Types verification first** (done: per-response style nonce, DEC-037); design-system handoff (done, DEC-039); screens approved (DEC-040, `docs/design/screens/slice-3/`); first-run setup, local auth, TOTP, roles, invitations, deny-by-default policy, authz enumeration test; audit rows for every slice-3 mutation; admin two-factor reset and a self-service two-factor page; Dapper and the NFR-SEC-3 gate, moved from slice 5 (DEC-038); backend and UI done (DEC-040); merged by PR #24 after two review rounds | Fluent verified under the served CSP; US-02/03/04 | 1, 2, 17 |
| 4 | **done** (PR #32, merged as `ecd7796`, DEC-041) | `slice/04-audit-log` | Audit log infrastructure (writes, filters, CSV export, retention path that is not the runtime login) + screen; audit events for sign-in and admin actions from slice 3 | US-07 for the log itself; later mutations audited per rule 7 | 16 |
| 4a | **done** (PR #38, merged as `18b46ed`, DEC-042) | `slice/04a-password-recovery` | Password recovery (#25): an Admin's "Reset password…" (optionally with two-factor, never for an Admin) through the link and separate code; dual control for invitations and the password-and-two-factor reset (the asker gets the link, another Admin approves and gets the code); the emergency-Admin guidance and the recovery setting `GOLDMARK_RECOVER_ADMIN` for the operator; Change password for the signed-in person; the approvals count | #25's criteria as decided (DEC-042): the reset ends sessions and the old password at once, needs the person's authenticator (or sets up a new one) and is audited; the recovery setting for an Admin; Change password (ASVS 5.0 6.2.2, 6.2.3); dual control; every new endpoint with an access policy and in the M6 enumeration test; boards approved | 1, 1b, 16 (entries), 17, account menu |
| 4b | **done** (PR #39, merged as `bc1e817`, DEC-043) | `slice/04b-windows-service` | Windows service distribution (DEC-036): service host, installer wizard, Windows authentication to SQL Server (gMSA; the principal audit for Windows logins and groups), the credential-file paths on a DPAPI-protected store, a log sink for a service (file or Event Log), service recovery in place of the Docker `HEALTHCHECK`, the `rotate-db-credentials` command, a Windows CI job. Ports what slices 3, 4 and 4a store (sign-in keys, audit log output, the recovery setting `Goldmark:Auth:RecoverAdmin`); #34, the retention period checked before `bootstrap-db` changes anything (DEC-042 item 1). Availability groups are not in it (#20); #6 moved here from slice 24 (DEC-043) | Owner, 2026-10-22 (DEC-043): (1) `Install-Goldmark.ps1` in a zip installs the service on Windows Server 2022/2025 against SQL Server 2025, Windows authentication or a SQL login; the operator's own Windows login runs `bootstrap-db migrate`; gMSA, or a domain user as fallback, `NT SERVICE\Goldmark` only with SQL Server on the same server, never LocalSystem or NetworkService; `/health/ready` 200 and the sign-in page; upgrade = run again; uninstall keeps the data. (2) Windows authentication: the service account's login in `goldmark_runtime`, no password, no DDL; the principal audit covers Windows logins and group rights; the password path only for SQL logins. (3) Folders for sign-in keys, TLS key, credential files, logs and settings: service account, SYSTEM and Administrators only, inheritance off, a start refuses wider; key ring and runtime credential file DPAPI-NG to the service account; administrator and migration passwords never stored on Windows; #6 closed. (4) JSON log files per day in `%ProgramData%\Goldmark\logs`; a configuration failure also to the Application log (setting names only); tested that 3001 and 3004 never reach the Event Log. (5) Service recovery restarts after a crash; a configuration error (exit 2) stops without a loop. (6) `rotate-db-credentials` (Docker and Windows): every generated SQL login, file before login, audited as System, then a restart (the wizard restarts the service); nothing to rotate under Windows authentication. (7) The recovery setting in the settings file from (3); the link's path in the log file. (8) #34's two criteria. (9) Windows CI (windows-2025): build, in-process tests, SQL Server 2025 Express on the runner, install smoke with Windows authentication as `NT SERVICE\Goldmark` (`/health/ready`, ACLs, log file), a SQL-login install with rotate, uninstall; the gMSA checked by hand per the ops guide. (10) `docs/ops/windows-service.md`; the installer's prompts as a text spec in the PR; rule 7; rule 8 both ways | — |
| 5 | **done** (PR #49, merged as `783f526`, DEC-044) | `slice/05-ci-crud` | CI types/attributes metadata (the built-in CI and relationship types, only the attributes the PRD names; the API to add custom attributes, its screen in 5b), CI CRUD API, list/detail UI with retire and restore (retired CIs hidden unless shown; other lifecycle changes in slice 20), deep-link routes (`/ci/{id}`, `/ci/{id}?tab=`, `/search?q=`), `docs/integration/deep-links.md`, friendly not-found for unknown Ids (Dapper and the NFR-SEC-3 gate arrive in slice 3, DEC-038) | US-10; US-11 for CIs; US-53 except `/impact` and alias redirect; search p95 on 25k synthetic CIs | 4, 5 |
| 5b | **done** (PR #54, merged as `7e9077a`, DEC-045) | `slice/05b-types-admin` | Types & attributes admin screen: custom CI types (add, hide, delete if unused), custom attributes (add, change, delete with typed confirmation), relationship types (built-in listed; custom added, edited, deleted if unused); promoted attributes wait (#53) | Admin can manage CI types, attributes and relationship types through the UI | 14 |
| 6 | **in progress** (approved 2026-10-24, DEC-046; boards approved 2026-10-25, `docs/design/screens/slice-6/`) | `slice/06-relationships` | Relationships CRUD (several CIs in one Add; deleting ends a relationship, its history keeps it), relationship tab, graph explorer (two walks plus redundancy-group members, many same-type neighbours grouped behind a switch, PNG and CSV export, depth 3); migration 0008 `Relationship.CreatedAt` for "Since" (DEC-046 items 1 to 14). Later: a phrase per direction for relationship types (#57), keyboard selection on the canvas (#58) | US-11 for relationships; US-40 | 5 (tab), 6 |
| 7 | pending | `slice/07-history` | `SESSION_CONTEXT` ChangedBy, history tab with diff, `asOf` on reads | US-42/43 | 4 (as-of picker), 5 (history tab), 6 (as-of control) |
| 8 | pending | `slice/08-api-tokens-openapi` | Scoped hashed API tokens, `spec/openapi/v1.json`, contract tests, Deprecation/Sunset, `docs/integration/api-stability.md`; `/api/v1/health` decision; the CI partial save, PATCH (#48, DEC-044 item 11) | US-06/54; US-50 infrastructure (later UI functions keep parity per rule 7); CI fails on an unreviewed breaking OpenAPI diff; #48 closed | 18 |
| 9 | pending | `slice/09-identity-spec` | `identity-vectors.json`, C# + PowerShell normalization, both suites | All §5.2 vectors pass in both languages | — |
| 10 | pending | `slice/10-ingest-api` | Ingest API (async batches, parts, idempotency), CiIdentifier matching, change suppression; how a collector ends the relationships it no longer sees, a policy (ServiceNow offers Keep, Delete, Delete relations, Mark absent, Mark retired), and what a person's removal of a discovered relationship means then (GLPI and Lansweeper treat it specially; a research session's note relayed by the owner, 2026-10-24, DEC-046 item 1) | US-20/21; re-ingest → 0 history rows | — |
| 11 | pending | `slice/11-provenance` | Provenance, precedence, overrides | US-22, FR-RC-3/6 | 5 (badges, override) |
| 12 | pending | `slice/12-review-queue` | Review queue API + UI, merge/alias, redirect from merged Ids | US-23, FR-RC-5, US-53 | 9 |
| 13 | pending | `slice/13-sources-collectors` | Sources & collectors, registration, tokens, run summaries, dashboard health tiles | US-30/62 | 12, 3 |
| 14 | pending | `slice/14-vcenter-collector` | vCenter collector | US-31/24 | — |
| 15 | pending | `slice/15-windows-collector` | Windows collector, ID-VMOS linking, platform classification | US-32; M1 | — |
| 16 | pending | `slice/16-ad-collector` | AD collector, cluster classification, OU exclusions, data-quality lists | US-33 | — |
| 17 | pending | `slice/17-sql-collector` | SQL collector: instances, DBs, AGs, replicas, listeners | US-34 | — |
| 18 | pending | `slice/18-fc-storage` | FC LUN paths, storage groups, mapping screen | US-35; M4 | 10 |
| 19 | pending | `slice/19-impact-engine` | Impact engine, redundancy semantics, explanation paths, `/impact?ci={id}[&ci=…]` deep link, CI detail "Impact" tab | US-41; M8; US-53 for `/impact` | 5 (Impact tab), 7 |
| 20 | pending | `slice/20-business-services` | Business services, ownership, lifecycle; the search toolbar: saved filters (#45), column chooser (#46), CSV export (#47) (DEC-044) | US-12/13/14; #45, #46, #47 closed | 4 (lifecycle/owner filters, toolbar), 5 (lifecycle/owner edit), 8 |
| 21 | pending | `slice/21-csv-import` | CSV import wizard + quickstart sample dataset | US-25; M5 | 13 |
| 22 | pending | `slice/22-webhooks` | Outbox, webhooks, delivery log | US-51 | 19 |
| 23 | pending | `slice/23-data-quality-history` | Data quality + global change history, dashboard data-quality counts and recent-changes feed | Screens 11, 15 and the dashboard additions | 3 (counts, recent changes), 11, 15 |
| 24 | pending | `slice/24-release-hardening` | ASVS L2 checklist, threat model, SBOM, cosign, signed collector module, backup + restore doc; #5; relative-link check (#12); trusted reverse proxies (#31, DEC-042 item 1); #33; #40 (owner, 2026-10-23) | US-60, NFR-SEC-7; #5, #31, #40 and the #12 link check closed | — |
| 25 | pending (P1) | `slice/25-oidc` | OIDC sign-in (Entra) + group→role mapping | US-05 | 1 (Microsoft sign-in), 17 (group mappings) |
| 25b | pending (P1) | `slice/25b-windows-sign-in` | Windows sign-in for domain users on the Windows service: Kerberos only, Goldmark's code still asked, accounts from mapped AD groups, self-service linking (#36, the owner's decisions there) | #36's acceptance criteria | 1, 1b, 17, account page |
| 25c | pending (P1) | `slice/25c-ldap-sign-in` | LDAP sign-in: a directory password over LDAPS, Active Directory and LDAPv3, every distribution, the #36 account model (#37) | #37's acceptance criteria | 1, 17, account page |
| 26 | pending (P1) | `slice/26-ethernet-neighbors` | Ethernet neighbor links (ID-CDP) + NetworkDevice placeholders, non-switch neighbor summary | US-36; M3 | 11 (CDP summary) |
| 27 | pending (P1) | `slice/27-fc-hba-rdm` | FC HBA inventory + RDM→VM links | US-37 | — |
| 28 | pending (P1) | `slice/28-backup-scenarios-replay` | Built-in backup job, saved impact scenarios, event replay, pseudonymized export | US-61/44/52; NFR-SEC-6 pseudonymized export (keyed HMAC, as in the readiness probe) | 7 (saved scenarios), 19 (replay) |
| 29 | pending (P1) | `slice/29-app-registrations` | App registrations, app tokens, annotations API, badges and "Open in {app}" links, Apps & integrations screen | US-55/57, FR-EX-3; a sample external app (test fixture) can annotate and link without any CMDB UI code change | 5 and 6 (badges, links), 21 |
| 30 | pending (P1) | `slice/30-change-feed` | Change feed + published `Goldmark.Contracts` NuGet and TypeScript types | US-56, FR-EV-5, FR-EX-2; the sample app resyncs correctly after a simulated outage | — |
| P1 | pending (P1) | slice to be set by the owner | Settings screen (DEC-021); its OIDC and backup sections depend on slices 25 and 28; audit retention only shown, not edited, while DEC-041 holds; changing the Windows service account or the database authentication on an upgrade (#41; owner, 2026-10-22 and 2026-10-23) | Screen 20; #41 closed | 20 |

P1 slices wait until slice 24 ships unless the owner pulls one forward.

## 2. Working rules (set by the owner)

1. **Gated APPROVED flow.** Never start the next phase, slice or step until
   the owner replies "APPROVED". Per slice: branch from the latest `main` as
   `slice/<nn>-<short-name>`; implement with tests for the acceptance
   criteria; run lint + the full test suite; self-review (correctness vs
   acceptance criteria, security, error handling, performance, readability,
   coverage) and fix; commit, push, open a PR whose description has: what
   changed, how to test, an acceptance checklist, review findings (fixed and
   remaining). Stop. After the owner merges, pull `main`. Never auto-merge.
2. **At most 2 review-fix rounds per PR.** After the initial push, fix review
   findings (Codex, CodeQL, self-review) in at most two rounds. Anything
   raised after that is listed for the owner with a fix-now/defer
   recommendation and, unless the owner says otherwise, becomes an issue
   labelled for the owning slice (or `deferred`) instead of another round.
   Precedent: PR #1 ended this way (#2–#4, #7).
3. **Design handoffs, batched.** Before any user-facing screen or component,
   stop and output one block titled "DESIGN HANDOFF — paste into Claude
   Design" covering **all** the slice's screens together (not one stop per
   screen): for each, its name and purpose, user story, every element and
   state (empty, loading, error, success), data shown, actions and navigation,
   responsive requirements, style direction from the PRD. Wait for mockups or
   a description; match them; ask when ambiguous. Backend/API work for the
   slice may proceed before the handoff; UI code may not.
4. **Design-system handoff before slice 3's screens.** After the Fluent CSP
   verification passes and before slice 3's screen handoff, output a
   design-system handoff: app shell and navigation, layout, tokens (colour,
   type, spacing), Fluent UI v9 component choices, table/form/empty-state
   patterns, focus/contrast rules from PRD §6.3, and the CSP and Trusted Types
   constraints. Wait for the result. Starting points:
   [`docs/specs/claude-design-prompts.md`](specs/claude-design-prompts.md) (the
   21-screen prompts) and the mockups and state boards in
   [`docs/design/screens/`](design/screens/). They are review input, not
   approved designs.
5. **Models and sessions.** Opus for slices 2b, 2c, 3, 19 and 24 (security- or
   logic-heavy); Sonnet for the rest. The owner sets the model per session.
   One new session per slice: start by reading this file, `DECISIONS.md` and
   `PRD.md`, confirm the next slice and open decisions in 3–5 lines, and wait
   for the go-ahead. Update this file in every slice PR (status, next slice,
   open issues, new gotchas).
6. **Standing rules from the brief:**
   - never commit secrets, tokens or `.env` files;
   - ask before adding a dependency the PRD didn't anticipate, or changing scope;
   - if blocked (install failure, missing credential, network block), stop and
     say exactly what is needed;
   - keep chat updates short; details go in PR descriptions and `DECISIONS.md`;
   - when asking the owner for a decision, explain it in plain terms with a
     concrete example, and give lettered options with a recommendation (owner,
     2026-10-20).
7. **Every slice, in addition to its own criteria** (from Codex on PR #11):
   - every new mutation writes audit records and tests them (US-07; slice 4
     builds the log itself);
   - every new UI function is available through `/api/v1` (US-50; slice 8
     builds the API infrastructure);
   - every new endpoint declares an access policy and is in the
     authorization enumeration test (M6, from slice 3).
8. **No new Linux- or Docker-only assumptions in application code** (owner,
   DEC-030). Docker Compose is the first distribution; the target is a Windows
   service with a dedicated, shared or availability-group SQL Server and Windows
   authentication. Keep provisioning in `bootstrap-db` (a plain CLI command),
   settings source-agnostic, and checks based on effective rights.

## 3. Next slice: notes

Slice **6** (`slice/06-relationships`, approved 2026-10-24, DEC-046) is **in progress**, with its approved screens in
`docs/design/screens/slice-6/` (canvas version `1792972526-db8a`). The decisions are DEC-046 items 1 to 16, with the
owner's words.

**What it adds:**
- **Migration 0008:** a nullable `CreatedAt` on `cmdb.Relationship` (default `SYSUTCDATETIME()`), filled from each
  relationship's earliest version, current and history rows alike, without versioning them (DEC-046 item 13).
- **API** (`Inventory/RelationshipEndpoints.cs`, `RelationshipStore.cs`; reads for everyone signed in, changes
  `CiEditor`): `GET /cis/{id}/relationships` (the tab's two sections, 50 then up to 200 a page after an opaque
  cursor, each with its total), `GET /cis/{id}/redundancy-groups` (the panel's suggestions), `GET`, `POST`, `PUT`
  (the redundancy group only) and `DELETE /relationships/{id}` with If-Match. The graph (`Inventory/GraphEndpoints.cs`,
  `GraphStore.cs`): `GET /graph` and `POST /graph/export` (CSV, audited). Four audit actions in the group
  "Relationships" (`relationship.added`, `.changed`, `.deleted`, `.exported`), a `Relationship` audit target and the
  filter `target=relationship:{id}`.
- **Web:** the CI page's Relationships tab (`?tab=relationships`, `web/src/ui/RelationshipsTab.tsx`) with its count,
  the Add and Edit panel (`web/src/ui/RelationshipPanel.tsx`: several CIs at once, a partial add keeps the panel
  open), the Delete dialog and View in graph; screen 6 at `/graph` (`web/src/pages/GraphPage.tsx`, the canvas in
  `web/src/graph/GraphCanvas.tsx`, the address and words in `web/src/graph/graphData.ts`), Inventory › Graph explorer
  in the nav; screen 16's relationship entries. The type icons the canvas draws are SVG files generated from the
  installed Fluent icons (`web/scripts/graph-icons.mjs`; `web/tests/graph-icons.test.mjs` fails on drift).

**For the slices that follow:**
- A relationship add locks, in this order: the relationship type's row (shared), the two CIs' type rows by ID
  (shared, as a CI save does), then both CI rows (shared, as a CI save's link check does). So an Admin's change to a
  type is met at the type, a retire of either CI waits for the add or the add sees the CI retired, and a relationship
  type's delete waits or the add finds the type gone. Slice 10's collectors and slice 21's import add relationships
  the same way. A change or delete locks the relationship's row only; a relationship with a retired CI can still be
  changed and deleted.
- Redundancy group texts are matched in .NET by `Inventory/RedundancyGroups` (trimmed, ordinal ignoring case, never
  otherwise normalised; DEC-046 item 2), never by a SQL collation; slice 19's impact engine uses it too.
- The graph walks hop by hop: two one-way walks, each with its own visited set, more than 10 like neighbours of a CI
  grouped before the next hop and never expanded, redundancy-group members after the dependents walk at one hop
  past their dependent. It stops before a hop that would pass 25,000 relationships (`GraphStore.MaxRelationships`,
  the design target's CI count; owner, DEC-046 item 7) and says `truncated`; a truncated graph's export is refused.
  An estate past 25,000 needs a server-paged table view (a keyset cursor, as Search has), not a larger number.
  Measured on the synthetic estate (25,000 CIs, 51,915 relationships; `PerformanceTests`, `GOLDMARK_PERF=1`): at
  depth 6 the largest read is 4,373 relationships (a team owning 3,520 CIs, grouping off), 84 ms at most, never
  truncated (DEC-046 item 7). `SyntheticInventory.SeedRelationshipsAsync` gives the estate its relationships.
  The CI types and relationship types filters stop the walk as Show retired does (DEC-046 item 15), so every line,
  row and CSV row is a real relationship; slice 19's impact walk follows the same rule.
- Deleting a relationship copies its last version to the history with the last writer's `ChangedBy`; who ended it is
  the `relationship.deleted` entry's actor (DEC-046 item 1). Slice 7's history view reads it from the audit log.
- Cytoscape.js needs `ensureCytoscapeStylesheet()` before its first `cytoscape()` (DEC-046 item 3); its node icons are
  images, so a new type icon key needs `node web/scripts/graph-icons.mjs`.

Slice **5b** (`slice/05b-types-admin`, approved 2026-10-23, DEC-045) is **merged** (PR #54, `7e9077a`), with its approved screens in
`docs/design/screens/slice-5b/` (DEC-045 item 7). The decisions are DEC-045, with the owner's words.

**What it adds:**
- **Migration 0007:** `IsHidden` on `cmdb.CiType` (with a check on the six §9.3 groups), `Description` on
  `cmdb.RelationshipType` (the 17 built-in descriptions), `ChangedBy` on both definition tables, and system
  versioning for `cmdb.CiType`, `cmdb.AttributeDef` and `cmdb.RelationshipType` (DEC-045 item 8): earlier
  versions and deleted definitions stay in `*History`. Rows that existed start their period at 1900-01-01.
- **API** (`Inventory/TypeEndpoints.cs`, Admin): `GET /types/usage` (counts, retired CIs included);
  `POST /types`, `DELETE /types/{type}` (409 `built-in`, `has-cis`, `referenced`), `POST /types/{type}/hide`
  and `/show`; `PUT` and `DELETE /types/{type}/attributes/{name}` (If-Match = the attribute's `eTag`; a choice
  a CI holds can't be removed, `ChoiceInUse`; a delete takes the values out of every CI in one statement and
  answers `valuesRemoved`); `POST /relationship-types`, `PUT` and `DELETE /relationship-types/{name}`.
  `POST /cis` refuses a hidden type (`HiddenType`). Nine new audit actions (group "Types & attributes"), a
  `RelationshipType` audit target, and the audit filter `target=ci:{id}`, `type:{number}`,
  `relationship-type:{number}` besides a person's ID.
- **Web:** screen 14 at `/types` (`web/src/pages/TypesPage.tsx`, the panels in `web/src/ui/TypePanels.tsx`),
  Administration › Types & attributes in the nav (Admin only; others get the 403 page); hidden types left out of
  the New CI picker and Search's type filter; screen 16 shows the new entries, opens a type in Types &
  attributes and shows all entries about a CI, a type or a relationship type.

**For the slices that follow:**
- A type's row in `cmdb.CiType` is the lock for its definitions: a CI save reads the type with `HOLDLOCK`
  inside its transaction (`CiTypeCatalog.LockForSaveAsync`), before the CI's own row; an Admin's change takes
  it with `XLOCK, HOLDLOCK, ROWLOCK` first (`LockForChangeAsync`). New writers of CIs (slice 10's ingest,
  slice 21's import) take the shared lock the same way, by the type's ID (a lookup by name locks only the name
  index's entry). While an Admin's change holds the row, every search, CI page and new CI waits, whatever its
  type: each reads the whole type list, which scans `cmdb.CiType`. Measured with
  `PerformanceTests.Deleting_an_attribute_many_CIs_hold_and_what_waits_for_it` (deleting an attribute that 4,000,
  then 10,000 of 25,000 synthetic CIs hold; 2026-10-24, two runs). The result, whatever the hardware: the
  rewrite escalated to a table lock on `cmdb.Ci` at both sizes (`index_lock_promotion_count`, asserted by the
  test), and every inventory page waits while the change runs. The times are an order of magnitude on a fast
  desktop, not a bound: the delete request (with its anti-forgery token's round trip) took 103 to 135 ms; a
  Server's page waited up to 96 ms against at most 23 alone; a save of a Server waited in three of the four
  measurements, up to 112 ms against 42 alone (in the fourth its longest save fell outside the lock). Host: an AMD Ryzen 9 9900X
  with 20 logical CPUs and about 70 GiB RAM seen by WSL2, SQL Server 2025 Express in Docker under WSL2, warm
  cache, one read loop and one save loop at a time. Estimates, not measured: a small shop's VM (PRD G6) is likely
  2 to 5 times slower, and real CIs with 30 to 50 attributes make the per-row `JSON_MODIFY` dearer. The
  Performance workflow's runner (4 vCPU, shared) gives slower numbers; those are the ones CI reproduces.
  Inferred, not exercised: a wait past SqlClient's default 30 s command timeout would answer 500.
  Retire and restore take it too (after PR #54's Codex review: holding the CI's row while reading the type deadlocked with an attribute delete).
  A save's link check still reads a linked CI's type after locking that CI; a deadlock there is retried once on
  both sides, then answered 412.
- Slice 6: deleting a relationship type counts its relationships first and the foreign key refuses it if one
  is created meanwhile (547, answered 409 `in-use`); slice 6 checks `owned_by`'s Team or Person end itself.
- Slice 7's history and "as of" views read a definition as of the CI version's time from the `*History`
  tables (`FOR SYSTEM_TIME AS OF`); a version's `ValidFrom` is also its ETag.
- An Admin's change that waits for saves can meet a CI version newer than its own transaction (13535): it runs
  once more, then answers 412 (`TypeEndpoints.RetryOnConflictAsync`).

Slice **5** (`slice/05-ci-crud`, approved 2026-10-23, DEC-044) is **merged** (PR #49, `783f526`), with its
approved screens in `docs/design/screens/slice-5/` (DEC-044 item 10). The decisions are DEC-044, with the
owner's words; the deep-link contract is `docs/integration/deep-links.md`.

**What it adds:**
- **Migration 0006:** `cmdb.AttributeDef` (an attribute per CI type: name, label, kind, required, choices, linked
  type), a §9.3 group on `cmdb.CiType`, the 20 built-in types, only the attributes the PRD names, and the 17
  §9.4 relationship types.
- **API:** `GET /cis` (name contains `q` in any case, `type`s, `retired`, six orders, an opaque keyset cursor,
  the total and the count per type on the first page), `GET /cis/{id}` with its ETag, `POST /cis`,
  `PUT /cis/{id}` (If-Match: 428 without, 412 changed, 409 `retired`; `attributes` required), `POST /cis/{id}/retire` and `/restore`;
  `GET /types`, `POST /types/{type}/attributes` (Admin, add only). New policy `CiEditor` (Admin or Editor).
  Every change audited in its transaction (`ci.created`, `ci.changed` with the fields' labels, `ci.retired`,
  `ci.restored`, `type.attribute.added`).
- **Web:** Search (screen 4) with the type filter and Show retired, the CI page (screen 5, Attributes tab), the
  New CI / Edit CI panel with all six attribute kinds, the 412 review ("Use mine"), the CI not-found page, the
  Inventory › Search nav item and the top-bar search. Screen 16's "Person affected" column (and the CSV's)
  is "Affected": a person, a CI with its type, or a CI type.
- **Performance:** `SyntheticInventory` (25,000 CIs in the §1.4 proportions) and `PerformanceTests`, run on
  request by the **Performance** workflow (`workflow_dispatch`), the p95 table in the job summary (DEC-044 item 7).
  First run on GitHub, at `783f526` (run 40014744706, 2026-10-23, passed): p95 64 ms for the list, 91 and 93 ms
  for the searches, 41 ms for newest changes first (budget 500 ms each), 4 ms for a CI's detail (budget 300 ms).

**For the slices that follow:**
- A CI's version is its `ValidFrom` (the table is system-versioned), sent as text and compared with
  `CONVERT(datetime2(7), @stamp, 126)`: Dapper sends a .NET `DateTime` as `datetime`, which loses the precision.
  SQL Server's 13535 (a transaction older than the row's version) answers 412 too.
- `Ci.ChangedBy` holds the person's account ID as text; slice 7's `SESSION_CONTEXT` should write the same.
- Names are matched and ordered with `COLLATE Latin1_General_100_CI_AS`, whatever the database's collation
  (DEC-030: a DBA's server); `q` escapes `%`, `_`, `[` and `\` (`ESCAPE '\'`).
- Collector slices add their attributes by migration as built-in rows of `cmdb.AttributeDef` (DEC-044 item 3).
  A required attribute added to a type with CIs is enforced the next time each is saved.
- `owned_by` and `depends_on` are seeded with null ends: slice 6 checks that `owned_by` points at a Team or a
  Person when it creates relationships.
- Audit targets beyond people: `TargetType` `Ci` (the CI's ID) and `CiType` (the type's ID); `AuditLog`
  resolves both.
- Search keeps its whole state in the page address and its columns are a data-driven array: #45 (saved
  filters), #46 (column chooser) and #47 (CSV export, reusing the list's query) attach there in slice 20.
- `PUT /cis/{id}` replaces every value: an attribute not sent is unset. Slice 8 adds the partial save,
  `PATCH` with JSON Merge Patch (#48, DEC-044 item 11): merge the stored values with the patch, then run the
  same checks (`AttributeValues.Check`, `CheckAsync`).
- A number attribute is a .NET `decimal`, stored as a JSON number, sent as a decimal string with every digit,
  accepted as either (DEC-044 item 12): slice 8's OpenAPI declares it `type: string, format: decimal`, slice
  10's ingest keeps the same check (`AttributeValues.ExactDecimal`), and no screen makes it a JavaScript number.
- A save holds a shared lock on the CIs it newly links to until it commits (`LinkedForSaveSql`), so a retire
  can't slip in between; a save SQL Server picks as a deadlock victim runs once more, then answers 412
  (`CiEndpoints.RetryOnDeadlockAsync`, DEC-044 item 13). New saves that check links go through both. A CI
  can't link to itself (`LinkedCiSelf`).
- `/api/v1/cis` is a real route now: tests use `/api/v1/no-such-resource` for an unknown one.
- The CI page has one tab; an unknown `?tab=` is corrected to the CI's address. Slices 6, 7, 10 and 19 add
  their tabs (`docs/integration/deep-links.md` lists the values).
- Retire or Restore after someone else changed the CI: a "This CI changed meanwhile" dialog loads their
  version (the boards' 412 dialog is the panel's).

Slice **4b** (`slice/04b-windows-service`, approved 2026-10-22, DEC-036, with #34 and #6) is **merged** (PR #39,
`bc1e817`). The decisions are DEC-043, with the owner's words; the operator guide is
`docs/ops/windows-service.md`.

**What it adds:**
- **The Windows service** (`Microsoft.Extensions.Hosting.WindowsServices`): `Install-Goldmark.ps1` in a
  self-contained `win-x64` zip (`deploy/windows/`), with `Uninstall-Goldmark.ps1` and
  `Rotate-GoldmarkCredentials.ps1`; a gMSA, a domain user or (local SQL Server only) `NT SERVICE\Goldmark`.
- **Windows authentication** to SQL Server (`Goldmark:Database:Authentication`), migrations as the
  installing administrator (`Goldmark:Migration:Login: Administrator`, no migration login), and the
  service's own rights check at each start (`Goldmark:Database:CheckRightsAtStart`).
- **Secrets at rest:** owner-only folders everywhere (`PrivateDirectory`; on Windows a protected ACL and a
  wider folder refused, #6), the key ring and a SQL login's password encrypted to the service account.
- **Logs** as JSON files per day (`Goldmark:Logging:Directory`); refusals in the Application log only.
- **`rotate-db-credentials`** on Docker and Windows; **#34**: `bootstrap-db` checks the retention period.
- The audit's baseline gains SQL Server 2025's Windows-only grants (`*.windows.txt`, DEC-043 item 12).

**For the slices that follow:**
- Rule 8 holds both ways: per-platform defaults live in `DefaultPaths`; Windows APIs behind
  `OperatingSystem.IsWindows()`.
- As a Windows service, anything that fails after the host starts looks like a crash and
  restart-on-failure loops: validate in `Program.cs` before `RunAsync`, refuse through
  `WindowsService.RefuseStart`, or stop with `ServiceBase.ExitCode` set (DEC-043 item 11).
- `EXECUTE AS LOGIN` doesn't see a Windows login's group rights; only the login's own token does.
- Test on Windows from WSL in a copy of the tree (`rsync` to `tmp/`), never the shared `bin/obj`;
  Windows PowerShell 5.1 drops empty native arguments, misparses `"$name:"`, and turns native stderr
  into errors under `Stop`.
- The Windows CI job installs SQL Server 2025 Express on the runner; it can't run Linux containers.

Slice **4a** (`slice/04a-password-recovery`, approved 2026-10-21, issue #25) is **merged** (PR #38,
`18b46ed`). The decisions are DEC-042, with the owner's words.

**What it adds:**
- **Reset password** on Users & roles: the old password stops working at once and every session ends;
  the person gets the link and separate code and confirms the new password with their own
  authenticator or a recovery code. "Also reset two-factor" runs the invitation's steps instead,
  never for an Admin.
- **Dual control** for invitations and the password-and-two-factor reset: the Admin who asks gets the
  link, another enabled Admin approves and gets the code (made then, hashed under a per-link key
  encrypted with Data Protection, scheme 2). With no other Admin the one who asks gets both,
  audited "only-admin". Waiting requests show as a count on the nav item, a MessageBar for
  approvers, and the requester's time left; an ended request can be asked again.
- **Change password** from the account menu (`POST /api/v1/account/password`); other sessions end.
- **The recovery setting** in place of an operator command: `GOLDMARK_RECOVER_ADMIN` and the
  operator's `GOLDMARK_RECOVER_CODE`, then a restart, log a one-hour link's path (event 3004; never
  the code). Every start ends any unused recovery link. The README prescribes an
  emergency Admin not tied to a person.
- Migration **0005**: `UserAccount.PasswordResetAt`, link purposes `PasswordReset` and
  `CombinedReset`, nullable `CreatedBy` (only a recovery link has none), approval and code-key columns.

**For the slices that follow:**
- Never null `PasswordHash` to reset: `IsPending` is `PasswordHash is null`. Use `PasswordResetAt`.
- A request that ended unapproved stays an open row (`RevokedAt` null): check `links.IsExpired` or
  `WaitingRequestAsync`, or a guard blocks for good.
- Under dual control `POST /api/v1/users` returns no code, so `AuthScenario.InviteAsync` only works
  with one Admin.
- Dual control counts an approver only if they can sign in (no unfinished password reset, an
  authenticator, not locked by an administrator; the automatic lockout never counts). With none, a
  request is refused with the reasons (owner, 2026-10-22, "C. Refuse, say why").
- The SPA must not poll: every request counts as activity, so a timer would keep an idle session
  alive past `SessionIdleMinutes`.
- Stopping a background integration run leaves its SQL Server container (no reaper, DEC-027):
  `docker ps -a --filter label=org.testcontainers=true`.

Slice **4** (`slice/04-audit-log`, approved 2026-10-20) is **merged** (PR #32). The owner's
decisions (DEC-041):
- old entries are deleted by a daily backend job through a bounded procedure (A);
- retention has a floor of 365 days (A);
- exports are audited, reads are not (A).

**Backend built:**
- **Migration 0004:**
  - `cmdb.AuditRetention`, which the runtime login can only read.
  - `cmdb.PurgeAuditLog`, which takes no parameters, keeps the floor in its code and commits each
    batch with a `System` entry. The runtime login gets EXECUTE on it and still has no DELETE on
    the table; the deletion works through ownership chaining, and the tests measure it.
- **`migrate`** writes `Goldmark:Audit:RetentionDays` (in `db-init`'s environment,
  `GOLDMARK_AUDIT_RETENTION_DAYS`) and audits each change. Its runtime check and both of the
  principal audit's allow-lists allow that one grant.
- **`AuditRetentionJob`** starts one minute after the app, gated on readiness, then runs daily.
- **Policy `AuditReader`** (Admin or Auditor).
- **Endpoints:** `GET /api/v1/audit` (filters, entry-number cursor, total), `/audit/catalog`,
  `/audit/people` (only people in the log) and `POST /audit/export` (CSV, at most 100,000, writes
  `audit.exported`).
- **Shared CSV helper:** `Security/Csv.cs`, also used by the users export.

**Screen 16 approved** by the owner on 2026-10-21 (DEC-041 item 5). The boards are in
`docs/design/screens/slice-4/`.

**UI built:** `web/src/pages/AuditPage.tsx` at `/audit`, with an Administration › Audit log nav item
for Admins and Auditors and the 403 state for everyone else.
- Filters are kept in the page address.
- Entries expand into a list of their details, written out in words.
- "Load more" pages with the cursor.
- Export is a dialog that downloads the CSV and shows the count from `X-Goldmark-Entries`.
- The differences from the boards are in that folder's README.
- `web/e2e/auth.spec.ts` covers it with axe in both themes.
- Fluent's multiselect Dropdown exposes its options as `menuitemcheckbox`, not `option`; Playwright
  tests must select them by that role.

**PR #32, review round 1** (2026-10-21; self-review, 15 findings, and Codex, 4, two of them the same
as self-review ones). Fixed:
- The purge job retries after any error, not only a SQL error: an unhandled one would stop the host.
- An export that grows past 100,000 between the count and the read is refused, never cut short. The
  read takes one entry more than the limit.
- `0004` (not merged yet, so edited in place, its hash line replaced):
  - Purge batches of 4,000, under SQL Server's 5,000-lock escalation threshold, so audit writes
    aren't held up by a table lock.
  - No seeded retention row: `migrate` sets the period and audits it without a `before`.
- A list request names every bad parameter at once.
- Docs give the retention range as 365 to 36500, or 0.
- The page:
  - An unknown `range` (even `constructor`) falls back to the default.
  - "Load more" and failures for replaced filters are dropped.
  - A dropped connection while reading an answer is an `ApiError` (`api.ts`).
  - The export's Cancel stays available while preparing, as on the board, and aborts the request.
    An export the server already ran is still recorded.
  - The export uses the query of the list shown.
  - A removed person a filter names keeps a label in the pickers.

Not changed:
- Codex's "index on `At`": `IX_AuditLog_At` exists (0001).
- A fast client clock shifts only the start of a preset range (no `to` is sent).

Left to the owner, decided 2026-10-21 (DEC-041 item 6):
- Actions missing from the catalog count as Succeeded but can't be filtered: recorded, no code.
- The people list and the count scan the log: deferred to #33 (slice 24).
- The CSV has no BOM: fixed in round 2, for both exports.

**PR #32, review round 2** (2026-10-21; Codex, 2 findings on `cbc9416`, both valid), the last allowed
(rule 2). Fixed, each with a Playwright test that holds the list's network answers:
- Choosing an incomplete custom range now starts a new list. An answer for the previous filters that
  was still in flight is dropped instead of showing old entries.
- "Load more" belongs to the list it continues. A continuation of a replaced list leaves the new list's
  button enabled and can't clear it.
- Also in this round: both CSV exports start with the UTF-8 byte-order mark (DEC-041 item 6).

Anything raised after round 2 goes to the owner as fix-now or defer, then becomes an issue.

**After both rounds** (`/code-review`, 10 findings, all valid, none security; owner, 2026-10-21, DEC-041
item 7):
- Fixed as one change:
  - custom-range years outside 2000 to 9998;
  - unexpected failures end in an error state;
  - unique expand-button names;
  - `\z` in the IP pattern;
  - one download helper;
  - `AuditQuery.None` moved into the test.
- Deferred: #34 (retention checked before `bootstrap-db`), #35 (ISO 8601 times, slice 8),
  cancellation added to #33 (slice 24).

Slice **3** (`slice/03-auth`) is **merged** (PR #24); what it built, for the slices that follow. **Backend** (DEC-040): migration 0003, Dapper
behind `DbSession` with the CA1857 gate, Identity on Dapper stores, server-side sessions, setup,
invitations and two-factor reset with a separate code, Users & roles with its guards, the
self-service two-factor page's API, deny-by-default and the M6 enumeration test, audit rows, the
`goldmark-keys` volume. **UI built** (DEC-040): screens 1, 1b, 2, 17 and the two-factor page on
the approved boards (`docs/design/screens/slice-3/`, with the four deviations its README lists), an
in-repo router, axe checks in Playwright (`@axe-core/playwright`, approved) and the theme contrast
test. **PR #24**, merged. Review round 1 (Codex, 5 findings, all fixed with tests): failed attempts are
counted atomically and never shorten a lock; sign-out reports success only once the server session is
gone; a recovery code spent on a new authenticator is audited in the same transaction; unknown-name
failures keep no idle expiry; after a lost key ring, a recovery-code sign-in goes straight to a new
authenticator (DEC-040 notes). Review round 2 (Codex, 4 findings, all fixed with tests): session rows
are written in the endpoint's transaction, so a recovery code is spent only with its session and
sign-out is audited only with the deleted session; the lost-key setup keeps a Retry; links state the
configured lifetime and wrong-code limit. Both rounds are used (rule 2): later findings go to the owner
as fix-now or defer.

The Fluent UI v9 verification failed under
`style-src 'self'`; the owner chose a per-response style nonce (DEC-037, options 1B and 2A),
which is built and tested, and passes with a real server in Chrome 155.0.8059.84, Edge
155.0.4283.62 and Firefox 158.0.1 (DEC-037): **the Fluent gate is passed.** The design-system handoff (rule 4)
is approved (DEC-039), and so are the slice's screens (rule 3, DEC-040). The
owner's slice-3 decisions are DEC-038: audit rows now, invitation links,
Net.Codecrete.QrCodeGenerator, Dapper and the NFR-SEC-3 gate now, the key ring as files, account
and session defaults, the setup token logged once. Screens create Fluent's
renderer with `createStyleRenderer()` (`web/src/styleNonce.ts`) inside `RendererProvider`.

Running the qualification page: build the sampler as a web root
(`pnpm exec vite build --config vite.config.ts --outDir <dir> --emptyOutDir e2e/fluent-check/app`
in `web/`), start the API with `ASPNETCORE_WEBROOT=<dir>`, open it in the browser and read the
"Self-check" result; in Playwright, `GOLDMARK_FLUENT_CHECK_DIRECT=1` runs `fluent.spec.ts` against it.

Slices 3 and 4 come before the Windows service (slice 4b, DEC-036), which ports what
they store. Keep it cheap (rule 8): sign-in keys (ASP.NET Core Data Protection,
NFR-SEC-5) and anything else persisted go to a path from settings, not a Docker-only
location, and nothing new assumes Linux or Docker.

Slice 2c left these for slice 4b:

- **Rotation command.** Done in 4b (PR #39): `rotate-db-credentials` (DEC-043 item 7).
- **Credential storage on Windows.** Done in 4b (PR #39): the password is encrypted to the
  service account (DEC-043 items 5 and 10).

And for slice 24:

- **Backups (US-60, slice 24)** must include the `goldmark-db-admin` volume, or a
  restore needs the `sa` recovery (`docs/ops/database-recovery.md`), and the
  `goldmark-keys` volume, restored as a pair with the database (DEC-038 item 5):
  without it every authenticator must be set up again with a recovery code.

## 4. Open issues and owners

| Item | Owner slice |
|---|---|
| #7 `bootstrap-db`: validate `Database:Port`; reject `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA` | 2 (closed by PR #14) |
| #2 guest access to other databases on a shared SQL Server | 2b (closed by PR #17) |
| #3 wildcard linked-server and remote-login mappings | 2b (closed by PR #17) |
| #4 audit of adopted and shared-instance principals beyond database scope | 2b (closed by PR #17) |
| #15 `migrate`: runtime user found by name, adopted user may have another name | 2b (closed by PR #17: `bootstrap-db` refuses a differently named user, DEC-032) |
| Generated credentials, `sa` disabled in the bundled deployment | 2c (done by PR #22: DEC-034) |
| #18 principal audit: securables in the Goldmark database owned by `public` | 2c (closed by PR #22: DEC-035) |
| #19 principal audit: the database owner's effective CONTROL SERVER, honouring `DENY` | 2c (closed by PR #22: DEC-035) |
| Rotation command for generated credentials | 4b (done by PR #39: DEC-043 item 7) |
| #20 principal audit: availability-group permission targets shown as `#id` | the availability-group slice (`deferred`, DEC-030) |
| Windows service distribution, installer wizard, Windows authentication | 4b (PR #39: DEC-043) |
| Availability groups (and #20) | owner: place in the plan (DEC-030, DEC-036) |
| Fluent UI v9 under the CSP: per-response style nonce, qualified in Chrome, Edge and Firefox (DEC-037) | 3 (done) |
| #5 renew the generated TLS certificate without a restart | 24 |
| #6 owner-only ACL for the generated key on Windows dev hosts | 4b (owner, 2026-10-22, DEC-043; was 24; closed by PR #39) |
| #12 docs-only CI gating (`deferred`, no slice) and the relative-link check | link check: 24 |
| #27 TypeScript 7, once typescript-eslint supports it (Dependabot ignores TypeScript majors until then) | any web slice, when supported |
| #33 audit log: the people list and the count scan the whole log; abandoned requests aren't cancelled (DEC-041 items 6, 7) | 24 |
| #34 db-init: the audit retention period is range-checked only by `migrate`, after `bootstrap-db` has run (DEC-041 item 7) | 4b (DEC-042 item 1; closed by PR #39) |
| #35 audit API: `from` and `to` are parsed leniently, not as strict ISO 8601 (DEC-041 item 7) | 8 |
| #31 trusted reverse proxies: the audit log's IP address and the sign-in rate limit use the TCP peer, so behind a proxy they show the proxy (DEC-041 item 4) | 24 (DEC-042 item 1) |
| #25 forgotten passwords: an administrator's reset via link and code; an operator command for the last administrator (DEC-040 item 9) | 4a (DEC-042), PR #38; the command became the recovery setting (item 6) |
| #36 Windows sign-in (Kerberos) for domain users on the Windows service | 25b (owner, 2026-10-21; DEC-042 item 12) |
| #37 LDAP sign-in: a directory password over LDAPS, every distribution | 25c (owner, 2026-10-21; DEC-042 item 12) |
| #40 Windows: a private folder where the service account can't write passes the start check, and the write fails later | 24 (`deferred`, 2026-10-22, PR #39 review round 1; placed by the owner, 2026-10-23, "A. Slice 24") |
| #41 Windows installer: changing the service account, or the database authentication, on an upgrade (both refused before anything changes until then; the owner plans post-MVP admin settings for database authentication) | P1 settings slice (`deferred`, 2026-10-22, PR #39 review round 1; placed by the owner, 2026-10-23, "A. With post-MVP settings") |
| #45 search: saved filters per user; #46 column chooser; #47 CSV export of the CI list (screen 4's toolbar) | 20 (owner, 2026-10-23, DEC-044) |
| #48 CI API: a partial save, `PATCH /api/v1/cis/{id}` with JSON Merge Patch (slice 5's PUT replaces every value) | 8 (owner, 2026-10-23, DEC-044 item 11) |
| #53 promoted attributes (FR-CM-2): computed column and index, applied by `migrate`, with the Promoted column on screen 14 | unplaced: the first slice that filters on attribute values (owner, 2026-10-23, DEC-045 item 3) |
| #57 relationship types: a phrase for each direction ("runs on" / "hosts"), on screen 14 and the CI page's tab | the next slice that touches screen 14 (owner, 2026-10-25, DEC-046 item 8) |
| #58 graph explorer: keyboard selection on the canvas (the table view is the equivalent meanwhile) | unplaced: an accessibility pass (owner, 2026-10-25, DEC-046 item 9) |
| Dependabot PRs #8 + #10 (`github/codeql-action` init/analyze 4.38.1 → 4.38.3): each fails alone | replaced by PR #28, merged: both bumps and a Dependabot group (owner, 2026-10-20: "A. One combined PR"); close #8 and #10 if still open |
| Dependabot PR #9: TypeScript 7 (typescript-eslint doesn't support it) and `@types/node` 26 (Node is 24) broke the Web job | closed (owner, 2026-10-20: "A. Close, add ignore rules"); Dependabot ignores both majors; #27 tracks TypeScript 7 |
| Dependabot PRs #42 (axe 4.14, Playwright 1.64.1) and #43 (React 19.3.1 without React DOM): axe 4.14 enables Label in Name, which the account button failed; React DOM throws error 527 for any other React version. With them, `RightsAtStartTests` raced on fast runners (main red after #44) | replaced by PR #50, merged as `53d65b4` (owner, 2026-10-23, all "A"): both bumps, the `react` Dependabot group, the account button named "AP, Account: …", the test fixed |
| Licensing (LICENSE, NOTICE, CONTRIBUTING/DCO, CODE_OF_CONDUCT) | owner decision (DEC-001) |

Labels in use: `slice-2` (covers 2, 2b and 2c), `deferred`.

## 5. Environment gotchas

### 5.1 GitHub access from the agent

- **Writes work through the `gh` CLI on the owner's WSL machine** (signed in as
  `bankielewicz`, `repo` scope): slice 2c's session opened PR #22 with it, so the
  agent opens PRs itself (an idempotent script, `DRY_RUN=1` first) instead of
  handing the owner scripts. The sandbox has no such login: there, hand the owner
  the script as before.
- The GitHub MCP integration is unreliable: read-only in earlier sessions (every
  write returned `403 Resource not accessible by integration`), and it failed to
  connect in slice 2c ("Authorization header is badly formatted"). The owner may
  update its credential to a fine-grained PAT (Contents RW, Pull requests RW,
  Issues RW, Actions R, Checks R, Commit statuses R, Metadata R on
  `bankielewicz/Goldmark`).
- `git push` works over HTTPS for any branch name (`claude/…`, `slice/…`,
  `docs/…` all verified).
- Public CI pages can be read without auth: fetch
  `https://github.com/bankielewicz/Goldmark/actions/runs/<run>/job/<job>` and
  read the `annotation-message` elements. The REST API is rate-limited from
  the sandbox. Step summaries (smoke results) need a signed-in browser.
- **Codex** reviews every push automatically (code review + security review,
  usually 5–10 min). Its progress is in the "Codex Review Summary" PR comment;
  findings arrive as review threads. It raises new P2s on almost every round,
  hence rule 2.

### 5.2 CI quirks

- 13 checks per PR: npm policy (before install), Web, .NET (in-process tests
  only), SQL Server integration tests (Testcontainers, about 2 min), Migration
  immutability, Compose smoke, Dependency review, CodeQL ×3 + the CodeQL
  status, code scanning, submit-nuget. Smoke is the long pole.
- The `migrations` job needs `fetch-depth: 0` to read the base revision's
  `db/migrations.sha256`; it compares with the PR base, or with
  `github.event.before` on pushes to `main`.
- GitHub's Docker did not apply `COPY --chmod` to the TLS directory (it was
  `0755`); the app forces `0700` at start (`a583c22`). Don't rely on image
  modes for secrets.
- The Compose "required secrets" error text differs between Compose
  versions; the smoke test checks behaviour, not text (`09ce13d`).
- Dependency review passes but warns about OpenSSF Scorecard < 3 on ~10
  transitive ESLint dev dependencies (warn-only).
- Dependabot: npm/NuGet/Actions/Docker have a 7-day cooldown matching the
  pnpm release-age rule. The GitHub Actions ecosystem has **no group**, so
  `codeql-action/init` and `/analyze` arrived as separate PRs (#8, #10) that
  each failed CodeQL ("Loaded a configuration file for version '4.38.3', but
  running version '4.38.1'"). A `codeql-action` group now keeps them in one PR
  (owner, 2026-10-20). npm ignores: TypeScript majors (#27) and `@types/node`
  majors above the Node in `.node-version`; raise that with Node.
- The smoke test writes its results to the job step summary
  (`GITHUB_STEP_SUMMARY`) and failures as annotations.

### 5.3 SQL Server container and `bootstrap-db`

- SQL Server 2025 **Express** (EngineEdition 4, major 17), TDS 8 **strict**
  forced; clients must pin the generated certificate. In-container `sqlcmd`:
  `/opt/mssql-tools18/bin/sqlcmd -S localhost -U <u> -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F sqlserver -l 10 -h -1 -W -b`
  with the password in `SQLCMDPASSWORD`.
- The SQL entrypoint regenerates the certificate when cert and key don't
  match (interrupted renewal).
- DDL cannot take parameters: identifiers and passwords go through
  `QUOTENAME` inside `sp_executesql`. `QUOTENAME` returns NULL for input over
  128 characters, hence the 128-character password limit.
- System catalog views have different collations; any `UNION` across them
  needs `COLLATE DATABASE_DEFAULT` (otherwise SQL error 451).
- Measured baseline for a fresh CONNECT-only login on SQL Server 2025:
  server `CONNECT SQL`, `VIEW ANY DATABASE`; database `CONNECT` plus
  `VIEW ANY COLUMN ENCRYPTION KEY DEFINITION` and
  `VIEW ANY COLUMN MASTER KEY DEFINITION` (granted to `public`). Slice 2 adds
  the `goldmark_runtime` role membership and its grants to this baseline
  (`VerifySql`).
- `bootstrap-db` manages only principals carrying the `goldmark:managed`
  extended property; its value names the purpose (`runtime-login`,
  `migration-login`). Others are refused (exit 3) unless
  `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`. Exit codes (all database commands):
  0 ok, 1 SQL/connection error, 2 invalid configuration, 3 refused.
- `db-init` runs `bootstrap-db migrate` (chained in-process; the image has no
  shell). Any positional first argument is a command: a typo exits 2.
- **The principal audit runs on every `bootstrap-db` run** (DEC-031), so any
  instance-wide state a test or an operator leaves behind (for example `guest`
  `CONNECT` in some database) makes every later run refuse. Integration tests that
  change instance-wide state live in `InstanceAuditTests` (their own container)
  and undo the change in `finally`.
- **Express opens closed databases slowly, and new databases get `AUTO_CLOSE
  ON`.** `HAS_DBACCESS` and any query into a database open it. The audit makes
  one administrator pass over other databases per run; `migrate` skips it.
  `bootstrap-db` creates the Goldmark database with `AUTO_CLOSE OFF`.
- **SQL Server image updates:** the audit's measured baseline of `public`/`guest`
  grants (`src/Goldmark.Api/Commands/Audit/`) must match the pinned image. When a
  Dependabot bump changes it, `The_system_database_baseline_matches_the_pinned_SQL_Server_image`
  fails and writes the measured file to the temp directory: review it, then replace the
  data lines.
- SQL Server 2025 no longer creates remote servers (`sp_addserver`, error 15663);
  `sp_addremotelogin` still works on a linked server with RPC enabled.
- `guest`'s `CONNECT` in model is copied into every new database.
- Two SQL Server containers starting at the same moment crashed during setup in
  the sandbox (errno 11 in lsass); the fixture starts containers one at a time.
  Once, in the sandbox, a container also crashed a minute into startup
  (`AppLoader.exe` fatal error 0xC0000001) and every test of its collection
  failed in `InitializeAsync`; the rerun passed. Rerun before debugging. During
  slice 2b this happened four times in all, once to the smoke test's
  `sqlserver` ("dependency failed to start: … unhealthy"); stopping `dockerd`,
  pruning containers, volumes and networks and starting it again fixed it each time.
- Effective permissions: an object-level `DENY` on a role beats a user-level
  `GRANT`, so `GRANT UPDATE ON cmdb.AuditLog TO goldmark_app` changes nothing
  (correctly accepted by `migrate`); removing the role's `DENY` is what grants
  it (refused). **But** a column-level `GRANT UPDATE (col)` to the role that
  holds the table `DENY` overrides it (measured on SQL Server 2025), hence
  `migrate`'s per-column check. Test effective rights, not grant rows.
- `FOR SYSTEM_TIME ALL` leaves out zero-duration versions (two changes in the
  same tick). To count history in tests, query the history table directly.
- DbUp creates its journal (`dbo.SchemaVersions`) before running the first
  script, so a migration can `GRANT` on it. Scripts run one transaction each,
  with variable substitution off.
- `Microsoft.Data.SqlClient` refuses globalization-invariant mode; the runtime
  image is the chiseled `-extra` (ICU) variant.
- **Generated credentials (slice 2c, DEC-034).** In the bundled stack `sa` is
  **disabled** after the first `db-init`; sign in as the generated administrator
  (`goldmark-db-admin` volume, `admin.json`) or as the runtime login
  (`goldmark-db-runtime`, `credential.json`). The app image has no shell: read a
  volume with the SQL image, `docker run --rm --user 1654:1654 -v
  goldmark_goldmark-db-admin:/c:ro --entrypoint cat <sql image> /c/admin.json`.
  The SQL healthcheck's login is in `sql-tls-private/healthcheck.credential`.
- **`sa` measured on the pinned image:** disabled stays disabled across restarts;
  a changed `MSSQL_SA_PASSWORD` is ignored after the first start;
  `mssql-conf -n set-sa-password` with SQL Server stopped re-enables `sa` (the
  recovery, `docs/ops/database-recovery.md`). `sqlservr --force-setup` run directly
  never exits (it keeps serving): run it detached if ever needed.
- **Ownership (measured, #18):** ownership by `public` or by a role gives every
  member CONTROL. `public` is a keyword: `ALTER AUTHORIZATION … TO [public]`.
- **Impersonation (measured, #19):** a disabled login can be impersonated
  (`EXECUTE AS LOGIN`); a login denied CONTROL SERVER cannot be impersonated into
  master at all (error 916), which the owner check reports.
- Integration tests: the fixture's administrator is the sysadmin `it_dba`
  (`AsAdminAsync`), not `sa`. `GeneratedAdministratorTests` disable `sa` in a third
  container of their own and re-enable it per test (`EnableSaAsync`, what
  `mssql-conf` does). A container's tmpfs mounts are emptied on restart, so tests that
  restart SQL Server need volumes.

### 5.4 .NET and tooling

- Pins: .NET SDK 10.0.402 (`global.json`), Node 24.21.0, pnpm 11.28.5 via
  Corepack (sha512-pinned `packageManager`). NuGet: nuget.org only, source
  mapping, signatures required, lock files, `--locked-mode`, NuGetAudit as
  errors. `System.Security.Cryptography.Pkcs` is pinned to 10.0.13 (a recorded
  exception to the 7-day rule, GHSA-846f-f764-76mx). New NuGet packages need
  source-mapping entries and central versions.
- pnpm 11 reads settings from `web/pnpm-workspace.yaml`, not `.npmrc`.
  `minimumReleaseAge` is strict (7 days): a version younger than that fails
  the install. Any new npm dependency needs an allow-list entry
  (`web/dependency-allowlist.json`) and owner approval.
- The lockfile checker rejects anything outside the YAML subset pnpm writes.
  If a pnpm upgrade changes the lockfile format, the checker needs updating
  first.
- The configuration binder's error message **contains the value**: never print
  it (`ConfigurationPreflight` reports the setting path only).
- `WebApplicationFactory` settings are not visible to `builder.Configuration`
  before `Build()`, so preflight checks in `Program.cs` are unit-tested
  directly and smoke-tested in the container.
- `X509Certificate2Collection.ImportFromPemFile` silently skips blocks that are
  not valid base64, but throws on valid base64 that is not valid DER.
- Docker reserves exit code 2 for `HEALTHCHECK` commands; `healthcheck`
  returns 1 for every failure.
- Integration tests (Testcontainers): the fixture reads the SQL image from
  `deploy/docker-compose.yml` and runs the real `sql-entrypoint.sh`, with the
  TLS directories on tmpfs. Docker's archive API cannot read files on tmpfs,
  so the certificate is read with `exec cat`. The resource reaper (Ryuk) is
  disabled (DEC-027). SQL Server is ready in about 10 s.
- Analyzer CA1711 rejects type names ending in `Collection` (xUnit collection
  definitions are named `…Tests`).
- **The SPA shell is a template** (DEC-037): `SpaShell` reads `wwwroot/index.html` once, at the
  first shell request. After replacing the web root (a rebuilt `web/dist`, a qualification build),
  restart the app, or it keeps sending the old page. HTML is never a static file, so
  `/index.html` is a 404.
- **Griffel picks right-to-left classes where `useStyles()` runs**, not where the class is used:
  a component inside `<FluentProvider dir="rtl">` must call the hook itself.
- **Playwright's `route.fetch()` does not use the browser's SPKI pin** (it fails with
  "self-signed certificate"); capture headers from a browser navigation instead.
- **`pnpm add` in `/mnt/c/...` is slow** (Fluent UI's 88 packages took 12 minutes, with no output
  through a pipe); run it in the background.
- **A minimal-API lambda whose only parameter is `HttpContext` binds as a `RequestDelegate`**: its
  returned `IResult` is dropped and the response is an empty 200. Give it a second parameter (slice 3:
  the anti-forgery token endpoint).
- **The cookie handler renews the request's session key on `SignInAsync`** when the request already
  has a live session (`ITicketStore.RenewAsync`, not `StoreAsync`), so the key would never rotate at
  sign-in. Endpoints that sign in carry `StartsNewSession`; `SessionTicketStore` then ends the
  presented session and the handler stores a new key (DEC-040). It ends it for any request to those
  two routes, even one that then fails: a signed-in test client that posts to
  `/api/v1/enrolment/complete` is signed out (same-origin only, SameSite=Strict), and its anti-forgery
  token is refused once, so retry with a fresh one.
- **Smoke-script checks run in the script's shell only when passed as a function name**
  (`check "…" my_function`): a `bash -c "…"` string cannot see the script's functions (`api`,
  `curl_tls`, `totp`), so the check fails with "command not found".
- **The fallback authorization policy also applies to requests with no endpoint**: without
  `DenyUnclassifiedEndpoints` a missing asset or a wrong method would be 401 instead of 404/405, and
  without an explicit `UseRouting()` after `UseStaticFiles()` the static files would sit behind it.
- **Antiforgery refuses plain HTTP** when its cookie is `Secure` (an `InvalidOperationException`, so a
  500): in-process test clients use `https://localhost` (`GoldmarkFactory`, `GoldmarkApp`).
- **CA1857 and `[ConstantExpected]`** are the NFR-SEC-3 gate: a `static readonly` string, a ternary of
  two literals and a forwarded parameter without the attribute all fail; `const` strings, constant
  concatenation and constant interpolation pass (measured, DEC-040).
- **Identity's lockout counter is per account**; the unknown-name counter (`UnknownUserThrottle`) is in
  memory, so a restart forgets it. Tests that need several codes for one person within 30 seconds use
  `Authenticator.NextCodeAsync()`, which never reuses a step (Goldmark refuses a reused code).

### 5.5 The agent's sandbox (local verification)

- **Slice 2c ran on the owner's WSL** (`/mnt/c/Projects/CMDB`), not the sandbox: the
  pinned SDK, Node and pnpm were installed, Docker Desktop was up, and the
  committed Dockerfile builds as-is (no TLS-inspecting proxy). Smoke there:
  `GOLDMARK_E2E_CHANNEL=chrome tests/smoke/compose-smoke.sh` (`/usr/bin/google-chrome`).

- Run `source /root/.goldmark-env` first (Node at `/opt/node24`, .NET at
  `/opt/dotnet`). Use `NUGET_CERT_REVOCATION_MODE=offline` locally only (CRL
  hosts are unreachable); CI checks online.
- **A new session's sandbox may be fresh** (slice 2 found no
  `/root/.goldmark-env`, no .NET SDK, Node 22 only). The Microsoft download
  hosts (`dot.net`, `builds.dotnet.microsoft.com`) do not resolve. Recovery:
  - Node: `nodejs.org/dist/v24.21.0/node-v24.21.0-linux-x64.tar.xz`, verified
    against `SHASUMS256.txt`, unpacked to `/opt/node24`; then
    `corepack enable --install-directory /opt/node24/bin`.
  - .NET: pull the digest-pinned SDK image from the Dockerfile, then
    `docker create` + `docker cp <id>:/usr/share/dotnet /opt/dotnet`.
  - Recreate `/root/.goldmark-env`: `DOTNET_ROOT=/opt/dotnet`,
    `PATH=/opt/node24/bin:/opt/dotnet:$PATH`,
    `NODE_EXTRA_CA_CERTS=/etc/ssl/certs/ca-certificates.crt`.
- The sandbox sits behind a TLS-inspecting proxy, so the committed Dockerfile
  cannot build as-is locally. Build from a copy that mounts the proxy CA as a
  BuildKit secret on the two network steps (`pnpm install`, `dotnet restore`):
  `--mount=type=secret,id=sandbox_ca,target=/tmp/sandbox-ca.pem` plus
  `NODE_EXTRA_CA_CERTS` / `SSL_CERT_FILE`. Keep the copy **outside the repo**
  (e.g. `/tmp/Dockerfile.sandbox`) and pass it with `-f`; run from the repo
  root (the checkout path differs between sandboxes) so `.` is the build
  context:
  `docker build -f /tmp/Dockerfile.sandbox --secret id=sandbox_ca,src=/etc/ssl/certs/ca-certificates.crt -t goldmark:local .`.
  Regenerate the copy whenever the Dockerfile changes. Never commit it
  (DEC-020).
- The Docker daemon dies often. Restart:
  `(setsid nohup dockerd > /tmp/dockerd.log 2>&1 < /dev/null &); sleep 7`.
  Testcontainers will need the daemon up; restart it before integration runs.
- Smoke locally: `GOLDMARK_SMOKE_NO_BUILD=1
  GOLDMARK_E2E_CHROMIUM=/opt/pw-browsers/chromium-1194/chrome-linux/chrome
  tests/smoke/compose-smoke.sh` (about 4–5 minutes; run in the background).
  - Never run two smoke runs at once (shared Compose project `goldmark-smoke`).
  - Don't edit `compose-smoke.sh` while it runs (bash reads it incrementally).
  - Manual cleanup: `docker compose down` needs the env file, so remove by
    label instead: `docker rm -f $(docker ps -aq --filter label=com.docker.compose.project=goldmark-smoke)`
    and the same for volumes and networks.
  - Each PASS line appears twice in the log (live and in the summary); count
    unique lines.
- **Two sessions in one working folder share HEAD** (slice 4). Another session ran `git switch -c` in
  `/mnt/c/Projects/CMDB` mid-slice, so later commits landed on its branch. They were recovered with
  `git branch -f`, `git rebase --onto origin/main <its commit>`, and its branch reset to its origin.
  Check `git branch --show-current` before every commit when another session may share the folder,
  or give each session its own worktree (`git worktree add`).

## 6. References

- PR #1 (slice 1) description: evidence, review history, deviations.
- `docs/DECISIONS.md`: DEC-001…DEC-041, the slice-1, slice-2, slice-2b,
  slice-2c, slice-3 and slice-4 acceptance mappings.
- `docs/ops/tls-certificates.md`, `docs/ops/hsts.md`,
  `docs/ops/external-sql-server.md` (existing SQL Server, Mode Existing, every
  audit finding), `docs/ops/database-credentials.md` (generated logins, `sa`),
  `docs/ops/database-recovery.md` (lost credentials), `docs/security/endpoint-access.md`.
