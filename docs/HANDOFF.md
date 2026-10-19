# Handoff: state of the build

Read this first when resuming work in a new session. It is updated in every
slice PR (status, open issues, new gotchas). Decisions and their rationale stay
in [`DECISIONS.md`](DECISIONS.md); requirements are in [`PRD.md`](PRD.md)
(draft v0.2; the build plan below supersedes PRD §11, see DEC-021).

**As of 2026-10-19:** slice 1 merged (PR #1, `main` at `3821851`). Next: slice 2.

## 1. Slice plan and status

Approved by the owner on 2026-10-19 (DEC-021). One PR per slice, in this
order. "Screens" are the PRD §7 UI inventory numbers; a slice with screens
needs a design handoff before any UI code (rule 3 below).

| # | Status | Branch | Scope | Done when | Screens |
|---|---|---|---|---|---|
| 1 | **done** (PR #1) | `claude/slice-01-scaffold` | Skeleton, CI, Dockerfile, Compose (SQL Server 2025 Express), `/health/*`, Serilog JSON, web skeleton with NFR-SEC-9 controls | `docker compose up` serves health + empty SPA; CI green; unlisted npm dependency fails CI | — |
| 2 | **next** | `slice/02-db-baseline` | DbUp baseline: Tenant, CiType, Ci (temporal), RelationshipType, Relationship (temporal), AuditLog; migration vs runtime logins; readiness schema-version check; #7 | Migrations idempotent; runtime login cannot run DDL or UPDATE/DELETE on AuditLog (tested); #7 closed | — |
| 2b | pending | `slice/02b-existing-sql` | US-01 "use an existing SQL Server" (`docker-compose.external-sql.yml`); audit of adopted/shared-instance principals (#2, #3, #4) | US-01 existing-SQL criteria; #2–#4 closed | — |
| 3 | pending | `slice/03-auth` | First-run setup, local auth, TOTP, roles, deny-by-default policy, authz enumeration test | US-02/03/04 | 1, 2, 17 |
| 4 | pending | `slice/04-audit-log` | Audit log writes + screen | US-07 | 16 |
| 5 | pending | `slice/05-ci-crud` | CI types/attributes metadata, CI CRUD API, list/detail UI, deep-link routes (`/ci/{id}`, `/search?q=`); Types & attributes admin; Fluent UI CSP/Trusted Types verification | US-10/11/53; search p95 on 25k synthetic CIs | 4, 5, 14 |
| 6 | pending | `slice/06-relationships` | Relationships CRUD, relationship tab, graph explorer | US-40 | 5 (tab), 6 |
| 7 | pending | `slice/07-history` | `SESSION_CONTEXT` ChangedBy, history tab with diff, `asOf` on reads | US-42/43 | 5 (tab), "as of" controls |
| 8 | pending | `slice/08-api-tokens-openapi` | Scoped hashed API tokens, `spec/openapi/v1.json`, contract tests, Deprecation/Sunset, `docs/integration/api-stability.md`; `/api/v1/health` decision | US-06/50/54; CI fails on an unreviewed breaking OpenAPI diff | 18 |
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
| 19 | pending | `slice/19-impact-engine` | Impact engine, redundancy semantics, explanation paths | US-41; M8 | 7 |
| 20 | pending | `slice/20-business-services` | Business services, ownership, lifecycle | US-12/13/14 | 8 |
| 21 | pending | `slice/21-csv-import` | CSV import wizard + quickstart sample dataset | US-25; M5 | 13 |
| 22 | pending | `slice/22-webhooks` | Outbox, webhooks, delivery log | US-51 | 19 |
| 23 | pending | `slice/23-data-quality-history` | Data quality + global change history | Screens 11, 15 | 11, 15 |
| 24 | pending | `slice/24-release-hardening` | ASVS L2 checklist, threat model, SBOM, cosign, signed collector module, backup + restore doc; Settings screen; #5, #6 | US-60, NFR-SEC-7 | 20 |
| 25–30 | pending (P1) | `slice/<nn>-<name>` | OIDC, Ethernet neighbors, FC HBA/RDM, backup job/replay, app registrations/annotations, change feed (PRD §11) | PRD §11 | 1 (OIDC), 17, 21 |

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
   raised after that is listed for the owner and, unless the owner says
   otherwise, becomes an issue labelled for the owning slice (or `deferred`)
   instead of another round. Precedent: PR #1 ended this way (#7).
3. **Design handoffs, batched.** Before any user-facing screen or component,
   stop and output one block titled "DESIGN HANDOFF — paste into Claude
   Design" covering **all** the slice's screens together (not one stop per
   screen): for each, its name and purpose, user story, every element and
   state (empty, loading, error, success), data shown, actions and navigation,
   responsive requirements, style direction from the PRD. Wait for mockups or
   a description; match them; ask when ambiguous. Backend/API work for the
   slice may proceed before the handoff; UI code may not.
4. **Design-system handoff before slice 3.** Slice 3 has the first screens.
   Before its screen handoff, output a design-system handoff (tokens: colour,
   type, spacing; Fluent UI v9 component choices; focus/contrast rules from
   PRD §6.3; the CSP and Trusted Types constraints) and wait for the result.
   The 21-screen Claude Design prompts from slice 1 are a starting point
   (session deliverable `claude-design-prompts.md`, held by the owner).
5. **Model notes.** _Owner to fill in: the details of this rule were not in
   the session context available when this file was written._
6. **Standing rules from the brief:**
   - never commit secrets, tokens or `.env` files;
   - ask before adding a dependency the PRD didn't anticipate, or changing scope;
   - if blocked (install failure, missing credential, network block), stop and
     say exactly what is needed;
   - keep chat updates short; details go in PR descriptions and `DECISIONS.md`.

## 3. Open issues and owners

| Item | Owner slice |
|---|---|
| #2 guest access to other databases on a shared SQL Server | 2b |
| #3 wildcard linked-server and remote-login mappings | 2b |
| #4 audit of adopted and shared-instance principals beyond database scope | 2b |
| #7 `bootstrap-db`: validate `Database:Port`; reject `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA` | 2 |
| #5 renew the generated TLS certificate without a restart | 24 |
| #6 owner-only ACL for the generated key on Windows dev hosts | 24 |
| Dependabot PRs #8 + #10 (`github/codeql-action` init/analyze 4.38.1 → 4.38.3) | owner: merge **together**, see §4.2 |
| Dependabot PR #9 (`web-dev-tooling` group, 3 updates) | owner: merge when CI (incl. the npm policy job) is green |
| Fluent UI v9 (Griffel) unverified against `style-src 'self'` and `trusted-types 'none'` | 5 (must pass before Fluent is adopted) |
| Licensing (LICENSE, NOTICE, CONTRIBUTING/DCO, CODE_OF_CONDUCT) | owner decision (DEC-001) |

Labels in use: `slice-2` (now covers 2 and 2b), `deferred`.

## 4. Environment gotchas

### 4.1 GitHub access from the agent

- The agent's GitHub MCP integration is **read-only** on this repo. Reads work
  (PR, checks, review threads, issues). Every write (create PR/issue, comment,
  reply, resolve thread, label, edit PR) returns `403 Resource not accessible
  by integration`. Workaround used so far: the agent writes an idempotent
  `gh` script (`DRY_RUN=1` first) and the owner runs it.
- To let the agent write directly, the connector needs, on
  `bankielewicz/Goldmark`: Contents RW, Pull requests RW, Issues RW, Actions R,
  Checks R, Commit statuses R, Metadata R (fine-grained PAT sent as
  `Authorization: Bearer`, then a new session).
- `git push` works over HTTPS for any branch name (`claude/…`, `slice/…`,
  `docs/…` all verified). Opening the PR may still need the owner.
- Public CI pages can be read without auth: fetch
  `https://github.com/bankielewicz/Goldmark/actions/runs/<run>/job/<job>` and
  read the `annotation-message` elements. The REST API is rate-limited from
  the sandbox. Step summaries (smoke results) need a signed-in browser.
- **Codex** reviews every push automatically (code review + security review,
  usually 5–10 min). Its progress is in the "Codex Review Summary" PR comment;
  findings arrive as review threads. It raises new P2s on almost every round,
  hence rule 2.

### 4.2 CI quirks

- 11 checks per PR: npm policy (before install), Web, .NET, Compose smoke,
  Dependency review, CodeQL ×3 + the CodeQL status, code scanning,
  submit-nuget. A full run takes about 4 minutes; smoke is the long pole.
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

### 4.3 SQL Server container and `bootstrap-db`

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
  the runtime DML grants to this baseline (`VerifySql`).
- `bootstrap-db` manages only principals carrying the `goldmark:managed`
  extended property; others are refused (exit 3) unless
  `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`. Exit codes: 0 ok, 1 SQL/connection
  error, 2 invalid configuration, 3 refused.
- `Microsoft.Data.SqlClient` refuses globalization-invariant mode; the runtime
  image is the chiseled `-extra` (ICU) variant.

### 4.4 .NET and tooling

- Pins: .NET SDK 10.0.402 (`global.json`), Node 24.21.0, pnpm 11.28.5 via
  Corepack (sha512-pinned `packageManager`). NuGet: nuget.org only, source
  mapping, signatures required, lock files, `--locked-mode`, NuGetAudit as
  errors. `System.Security.Cryptography.Pkcs` is pinned to 10.0.13 (a recorded
  exception to the 7-day rule, GHSA-846f-f764-76mx).
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

### 4.5 The agent's sandbox (local verification)

- Run `source /root/.goldmark-env` first (Node at `/opt/node24`, .NET at
  `/opt/dotnet`). Use `NUGET_CERT_REVOCATION_MODE=offline` locally only (CRL
  hosts are unreachable); CI checks online.
- The sandbox sits behind a TLS-inspecting proxy, so the committed Dockerfile
  cannot build as-is locally. Build from a copy that mounts the proxy CA as a
  BuildKit secret on the two network steps (`pnpm install`, `dotnet restore`):
  `--mount=type=secret,id=sandbox_ca,target=/tmp/sandbox-ca.pem` plus
  `NODE_EXTRA_CA_CERTS` / `SSL_CERT_FILE`, and
  `docker build --secret id=sandbox_ca,src=/etc/ssl/certs/ca-certificates.crt -t goldmark:local`.
  Regenerate the copy whenever the Dockerfile changes. Never commit it
  (DEC-020).
- The Docker daemon dies often. Restart:
  `(setsid nohup dockerd > /tmp/dockerd.log 2>&1 < /dev/null &); sleep 7`.
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

## 5. References

- PR #1 (slice 1) description: evidence, review history, deviations.
- `docs/DECISIONS.md`: DEC-001…DEC-021 and the slice-1 acceptance mapping.
- `docs/ops/tls-certificates.md`, `docs/ops/hsts.md`,
  `docs/security/endpoint-access.md`.
