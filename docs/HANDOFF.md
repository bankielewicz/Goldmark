# Handoff: state of the build

Read this first when resuming work in a new session. It is updated in every
slice PR (status, open issues, new gotchas). Decisions and their rationale stay
in [`DECISIONS.md`](DECISIONS.md); requirements are in [`PRD.md`](PRD.md)
(draft v0.2; the build plan below supersedes PRD §11, see DEC-021).

**As of 2026-10-19:** slices 1 (PR #1) and 2 (PR #14) are merged, with the
handoff and docs PRs #11, #13 and #16. Slice 2b (`slice/02b-existing-sql`) is in
review. Next: **slice 2c** (generated database credentials), once 2b is merged.
Its notes are in §3. Docker Compose is the first distribution, not the target
deployment (DEC-030, rule 8).

## 1. Slice plan and status

Approved by the owner on 2026-10-19 (DEC-021, amended the same day; slice 2c
added by the owner during slice 2b, DEC-032). One PR
per slice, in this order. "Screens" are the PRD §7 UI inventory numbers; a
slice with screens needs a design handoff before any UI code (rule 3 below).

| # | Status | Branch | Scope | Done when | Screens |
|---|---|---|---|---|---|
| 1 | **done** (PR #1) | `claude/slice-01-scaffold` | Skeleton, CI, Dockerfile, Compose (SQL Server 2025 Express), `/health/*`, Serilog JSON, web skeleton with NFR-SEC-9 controls | `docker compose up` serves health + empty SPA; CI green; unlisted npm dependency fails CI | — |
| 2 | **done** (PR #14) | `slice/02-db-baseline` | DbUp baseline: Tenant, CiType, Ci (temporal), RelationshipType, Relationship (temporal), AuditLog; `migrate` command in `db-init`; `goldmark_migrator` + runtime role; readiness schema-version check; migration immutability check; #7. DEC-022…DEC-029 | Migrations idempotent; runtime login cannot run DDL or UPDATE/DELETE on AuditLog (tested against real SQL Server 2025); #7 closed | — |
| 2b | **in review** | `slice/02b-existing-sql` | US-01 "use an existing SQL Server" (`docker-compose.external-sql.yml`); audit of adopted/shared-instance principals (#2, #3, #4), run whenever a principal is adopted or revalidated, in **both** bundled and external deployments; Mode `Existing` for logins a DBA creates; no `sa` as administrator outside the bundled stack; #15. DEC-030…DEC-033 | US-01 existing-SQL criteria; #2–#4 and #15 closed | — |
| 2c | **next** | `slice/02c-generated-credentials` | Generated administrator, migration and runtime credentials (prefixed random names, random passwords) stored in volumes; the app reads its password from a file; a CONNECT-only login for the SQL container's healthcheck; `sa` disabled after the first run, with a tested recovery (DEC-032) | `deploy/.env` holds only the `sa` password the image needs; `sa` disabled; recovery documented and tested | — |
| 3 | pending | `slice/03-auth` | **Fluent UI v9 CSP/Trusted Types verification first** (if it fails: stop, bring options, build no screens); design-system handoff (rule 4); first-run setup, local auth, TOTP, roles, deny-by-default policy, authz enumeration test | Fluent verified under the served CSP; US-02/03/04 | 1, 2, 17 |
| 4 | pending | `slice/04-audit-log` | Audit log infrastructure (writes, filters, CSV export, retention path that is not the runtime login) + screen; audit events for sign-in and admin actions from slice 3 | US-07 for the log itself; later mutations audited per rule 7 | 16 |
| 5 | pending | `slice/05-ci-crud` | CI types/attributes metadata, CI CRUD API, list/detail UI, deep-link routes (`/ci/{id}`, `/ci/{id}?tab=`, `/search?q=`), `docs/integration/deep-links.md`, friendly not-found for unknown Ids; Dapper introduced here with the NFR-SEC-3 build gate (analyzer/Semgrep rule failing on interpolated SQL in Dapper calls, plus a test proving it fails) | US-10; US-11 for CIs; US-53 except `/impact` and alias redirect; NFR-SEC-3 gate; search p95 on 25k synthetic CIs | 4, 5 |
| 5b | pending | `slice/05b-types-admin` | Types & attributes admin screen, including relationship types and their impact propagation | Admin can manage CI types, attributes and relationship types through the UI | 14 |
| 6 | pending | `slice/06-relationships` | Relationships CRUD, relationship tab, graph explorer | US-11 for relationships; US-40 | 5 (tab), 6 |
| 7 | pending | `slice/07-history` | `SESSION_CONTEXT` ChangedBy, history tab with diff, `asOf` on reads | US-42/43 | 4 (as-of picker), 5 (history tab), 6 (as-of control) |
| 8 | pending | `slice/08-api-tokens-openapi` | Scoped hashed API tokens, `spec/openapi/v1.json`, contract tests, Deprecation/Sunset, `docs/integration/api-stability.md`; `/api/v1/health` decision | US-06/54; US-50 infrastructure (later UI functions keep parity per rule 7); CI fails on an unreviewed breaking OpenAPI diff | 18 |
| 9 | pending | `slice/09-identity-spec` | `identity-vectors.json`, C# + PowerShell normalization, both suites | All §5.2 vectors pass in both languages | — |
| 10 | pending | `slice/10-ingest-api` | Ingest API (async batches, parts, idempotency), CiIdentifier matching, change suppression | US-20/21; re-ingest → 0 history rows | — |
| 11 | pending | `slice/11-provenance` | Provenance, precedence, overrides | US-22, FR-RC-3/6 | 5 (badges, override) |
| 12 | pending | `slice/12-review-queue` | Review queue API + UI, merge/alias, redirect from merged Ids | US-23, FR-RC-5, US-53 | 9 |
| 13 | pending | `slice/13-sources-collectors` | Sources & collectors, registration, tokens, run summaries, dashboard health tiles | US-30/62 | 12, 3 |
| 14 | pending | `slice/14-vcenter-collector` | vCenter collector | US-31/24 | — |
| 15 | pending | `slice/15-windows-collector` | Windows collector, ID-VMOS linking, platform classification | US-32; M1 | — |
| 16 | pending | `slice/16-ad-collector` | AD collector, cluster classification, OU exclusions, data-quality lists | US-33 | — |
| 17 | pending | `slice/17-sql-collector` | SQL collector: instances, DBs, AGs, replicas, listeners | US-34 | — |
| 18 | pending | `slice/18-fc-storage` | FC LUN paths, storage groups, mapping screen | US-35; M4 | 10 |
| 19 | pending | `slice/19-impact-engine` | Impact engine, redundancy semantics, explanation paths, `/impact?ci={id}[&ci=…]` deep link, CI detail "Impact" tab | US-41; M8; US-53 for `/impact` | 5 (Impact tab), 7 |
| 20 | pending | `slice/20-business-services` | Business services, ownership, lifecycle | US-12/13/14 | 4 (lifecycle/owner filters), 5 (lifecycle/owner edit), 8 |
| 21 | pending | `slice/21-csv-import` | CSV import wizard + quickstart sample dataset | US-25; M5 | 13 |
| 22 | pending | `slice/22-webhooks` | Outbox, webhooks, delivery log | US-51 | 19 |
| 23 | pending | `slice/23-data-quality-history` | Data quality + global change history, dashboard data-quality counts and recent-changes feed | Screens 11, 15 and the dashboard additions | 3 (counts, recent changes), 11, 15 |
| 24 | pending | `slice/24-release-hardening` | ASVS L2 checklist, threat model, SBOM, cosign, signed collector module, backup + restore doc; #5, #6; relative-link check (#12) | US-60, NFR-SEC-7; #5, #6 and the #12 link check closed | — |
| 25 | pending (P1) | `slice/25-oidc` | OIDC sign-in (Entra) + group→role mapping | US-05 | 1 (Microsoft sign-in), 17 (group mappings) |
| 26 | pending (P1) | `slice/26-ethernet-neighbors` | Ethernet neighbor links (ID-CDP) + NetworkDevice placeholders, non-switch neighbor summary | US-36; M3 | 11 (CDP summary) |
| 27 | pending (P1) | `slice/27-fc-hba-rdm` | FC HBA inventory + RDM→VM links | US-37 | — |
| 28 | pending (P1) | `slice/28-backup-scenarios-replay` | Built-in backup job, saved impact scenarios, event replay, pseudonymized export | US-61/44/52; NFR-SEC-6 pseudonymized export (keyed HMAC, as in the readiness probe) | 7 (saved scenarios), 19 (replay) |
| 29 | pending (P1) | `slice/29-app-registrations` | App registrations, app tokens, annotations API, badges and "Open in {app}" links, Apps & integrations screen | US-55/57, FR-EX-3; a sample external app (test fixture) can annotate and link without any CMDB UI code change | 5 and 6 (badges, links), 21 |
| 30 | pending (P1) | `slice/30-change-feed` | Change feed + published `Goldmark.Contracts` NuGet and TypeScript types | US-56, FR-EV-5, FR-EX-2; the sample app resyncs correctly after a simulated outage | — |
| P1 | pending (P1) | slice to be set by the owner | Settings screen (DEC-021); its OIDC and backup sections depend on slices 25 and 28 | Screen 20 | 20 |

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
5. **Models and sessions.** Opus for slices 2b, 3, 19 and 24 (security- or
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
   - keep chat updates short; details go in PR descriptions and `DECISIONS.md`.
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

## 3. Next slice (2c): notes

Slice 2b's decisions are DEC-030…DEC-033. For 2c (DEC-032):

- **Generated credentials.** `bootstrap-db` generates the administrator,
  migration and runtime credentials (a recognisable prefix plus a random suffix
  for names, random passwords) and writes them to a path from its settings: a
  volume in Docker, DPAPI or the installer later (rule 8). The administrator and
  migration credentials go to a volume only `db-init` mounts; the runtime
  credential goes to one the app mounts read-only. `GOLDMARK_DB_PASSWORD` and
  `GOLDMARK_DB_MIGRATION_PASSWORD` leave `.env`.
- **`sa`.** The first run connects as `sa`, creates the generated administrator,
  then disables `sa`. `AllowSaAdmin` (bundled Compose file only) then goes away.
  The SQL Server image needs `MSSQL_SA_PASSWORD` on every start even with `sa`
  disabled (an image constraint: document it).
- **Lockout risk.** If the generated administrator's volume is lost and `sa` is
  disabled, nobody can sign in as sysadmin (on Linux there is no
  local-administrator fallback in single-user mode). Whether
  `mssql-conf set-sa-password` re-enables a disabled `sa` is unmeasured. A tested
  recovery comes before disabling `sa`.
- **Healthcheck.** `deploy/sqlserver/healthcheck.sh` signs in as `sa`; it needs a
  CONNECT-only login instead.
- The external-SQL file keeps the operator's own administrator (no `sa`);
  generation there applies to the migration and runtime logins in Mode `Create`.
- The principal audit already treats every login it manages the same way, so it
  needs no change for generated names.

## 4. Open issues and owners

| Item | Owner slice |
|---|---|
| #7 `bootstrap-db`: validate `Database:Port`; reject `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA` | 2 (closed by PR #14) |
| #2 guest access to other databases on a shared SQL Server | 2b (closed by the slice-2b PR) |
| #3 wildcard linked-server and remote-login mappings | 2b (closed by the slice-2b PR) |
| #4 audit of adopted and shared-instance principals beyond database scope | 2b (closed by the slice-2b PR) |
| #15 `migrate`: runtime user found by name, adopted user may have another name | 2b (closed by the slice-2b PR: `bootstrap-db` refuses a differently named user, DEC-032) |
| Generated credentials, `sa` disabled in the bundled deployment | 2c (DEC-032) |
| Windows service distribution, installer wizard, Windows authentication, availability groups | owner: place in the plan (DEC-030) |
| Fluent UI v9 (Griffel) unverified against `style-src 'self'` and `trusted-types 'none'` | 3 (must pass before any screen is built) |
| #5 renew the generated TLS certificate without a restart | 24 |
| #6 owner-only ACL for the generated key on Windows dev hosts | 24 |
| #12 docs-only CI gating (`deferred`, no slice) and the relative-link check | link check: 24 |
| Dependabot PRs #8 + #10 (`github/codeql-action` init/analyze 4.38.1 → 4.38.3) | owner: merge **together**, see §5.2 |
| Dependabot PR #9 (`web-dev-tooling` group, 3 updates) | owner: merge when CI (incl. the npm policy job) is green |
| Licensing (LICENSE, NOTICE, CONTRIBUTING/DCO, CODE_OF_CONDUCT) | owner decision (DEC-001) |

Labels in use: `slice-2` (covers 2, 2b and 2c), `deferred`.

## 5. Environment gotchas

### 5.1 GitHub access from the agent

- **Status:** the owner is updating the GitHub MCP credential to a
  fine-grained PAT with Contents RW, Pull requests RW, Issues RW, Actions R,
  Checks R, Commit statuses R, Metadata R on `bankielewicz/Goldmark`. Once a
  session confirms writes work (open a PR, comment, create an issue), stop
  generating `gh` scripts and update this section.
- Until then, the GitHub MCP integration is **read-only**: every write returns
  `403 Resource not accessible by integration`. Workaround: the agent writes an
  idempotent `gh` script (`DRY_RUN=1` first) and the owner runs it.
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
  `codeql-action/init` and `/analyze` arrive as separate PRs (#8, #10) and
  must be merged together. A `groups:` entry for `github/codeql-action*`
  would fix that (small change; ask before making it).
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
  failed in `InitializeAsync`; the rerun passed. Rerun before debugging.
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

### 5.5 The agent's sandbox (local verification)

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

## 6. References

- PR #1 (slice 1) description: evidence, review history, deviations.
- `docs/DECISIONS.md`: DEC-001…DEC-033, the slice-1, slice-2 and slice-2b
  acceptance mappings.
- `docs/ops/tls-certificates.md`, `docs/ops/hsts.md`,
  `docs/ops/external-sql-server.md` (existing SQL Server, Mode Existing, every
  audit finding), `docs/security/endpoint-access.md`.
