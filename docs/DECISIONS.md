# Decisions log

Running log of technical decisions, with rationale. Newest entries are added
at the end. `docs/PRD.md` is stored as received (draft v0.2). Where an entry
here narrows or deviates from the PRD, this file wins.

**Approval state (2026-10-19):** slices 1, 2, 2b and 2c are merged (PRs #1, #14, #17, #22); slice 3 is next. The owner approved
the **amended MVP build plan** (DEC-021): PRD §11 slices 2–24 plus slices 2b, 2c,
4b (DEC-036) and 5b, in order, one PR each. Docker Compose is the first distribution, not the
target deployment (DEC-030). The PRD text stays a draft (DEC-001); the build
plan in DEC-021 and `docs/HANDOFF.md` is the working plan. PRD assumption A1
(stack) is confirmed, with SQL Server 2025 as the minimum version (OQ-2 resolved, DEC-030). A2 (licensing, DCO) is pending. The other assumptions remain open.

| ID | Decision | Area |
|---|---|---|
| DEC-001 | Approved scope: slice 1 only; PRD stays a draft | Process |
| DEC-002 | Phase-2 scaffold = PRD slice 1; numbering kept | Process |
| DEC-003 | Repository, branches and bootstrap commit | Process |
| DEC-004 | Toolchain pins: .NET SDK 10.0.402, Node 24.21.0, pnpm 11.28.5 | Build |
| DEC-005 | One ASP.NET Core project for now | Structure |
| DEC-006 | HTTPS-only host; first-start certificate persisted in a volume | Security |
| DEC-007 | HSTS automatic: on with an operator certificate, off with self-signed | Security |
| DEC-008 | Health endpoints and their access policy | API |
| DEC-009 | NuGet supply-chain controls | Supply chain |
| DEC-010 | NuGet dependencies for slice 1 and the release-age rule | Supply chain |
| DEC-011 | npm supply-chain controls and dependency allow-list | Supply chain |
| DEC-012 | Front-end tooling choices | Front end |
| DEC-013 | Base images pinned by digest; chiseled-extra runtime | Containers |
| DEC-014 | Bundled SQL Server: Express, TDS 8 strict only, pinned certificate | Database |
| DEC-015 | Slice-1 database bootstrap and secrets handling | Database |
| DEC-016 | Logging | Observability |
| DEC-017 | Browser security headers; Trusted Types verified in a browser | Security |
| DEC-018 | CI design, and CodeQL / dependency review availability | CI |
| DEC-019 | Deferred to later slices | Scope |
| DEC-020 | Constraints of the build sandbox (not of the product) | Process |
| DEC-021 | Approved MVP build plan (amended PRD §11) | Process |
| DEC-022 | Migrations run in `db-init` (`migrate`), never in the app | Database |
| DEC-023 | Readiness requires the expected schema version | API |
| DEC-024 | Migration login: `db_owner` of the Goldmark database only | Database |
| DEC-025 | Runtime permissions through the `goldmark_runtime` role | Database |
| DEC-026 | Slice-2 NuGet dependencies: DbUp, Testcontainers | Supply chain |
| DEC-027 | Database integration tests against real SQL Server 2025 | Testing |
| DEC-028 | Applied migrations are immutable (hash manifest, CI) | Database |
| DEC-029 | Baseline schema (0001) choices | Database |
| DEC-030 | Deployment target: Docker first, Windows service and existing SQL Servers next | Process |
| DEC-031 | Principal audit of Goldmark's logins and the shared instance | Database |
| DEC-032 | `bootstrap-db`: administrator login, Mode Create/Existing, user names | Database |
| DEC-033 | `docker-compose.external-sql.yml` | Deployment |
| DEC-034 | Generated database credentials; `sa` disabled; tested recovery | Database |
| DEC-035 | Principal audit: securables owned by `public`, the owner's effective CONTROL SERVER | Database |
| DEC-036 | Windows service distribution placed as slice 4b | Process |
| DEC-037 | Fluent UI under the CSP: a per-response style nonce | Security |

---

## DEC-001 Approved scope: slice 1 only; PRD stays a draft

- **Decision.** Implement PRD slice 1 with the scope the product owner set on
  2026-10-16. The PRD itself remains *DRAFT v0.2*, stored unchanged in
  `docs/PRD.md` apart from a labelled note pointing here. The .NET 10 / SQL
  Server 2025 Express stack (A1) is confirmed for this scaffold.
- **Pending owner decisions.** Licensing: no `LICENSE`, `NOTICE`, `CONTRIBUTING.md`
  (DCO) or `CODE_OF_CONDUCT.md` until the licensing decision (A2, OQ-10). The README
  states that no license is granted yet.

## DEC-002 Phase-2 scaffold = PRD slice 1; numbering kept

- **Decision.** Scaffolding is merged into slice 1 (walking skeleton). Phase 3
  starts at slice 2. PRD slice numbers are unchanged.
- **Rationale.** Supply-chain controls must exist before the first install;
  the smoke test exercises real health endpoints; "done" is a working
  `docker compose up`.

## DEC-003 Repository, branches and bootstrap commit

- **Decision.** Target `bankielewicz/Goldmark` (deviation: the PRD names it
  `Goldmark-cmdb`). It was private during most of slice 1 and was made
  **public** by the owner on 2026-10-16, which matches the PRD's visibility.
  It remains unlicensed until the licensing decision (DEC-001). `main` was created with a single README-only
  commit (`git push` to `main` was permitted). All implementation is on
  `claude/slice-01-scaffold`, one PR, never auto-merged.
- **Note.** No session id was exposed to the agent; pushing
  `claude/slice-01-scaffold` was accepted.

## DEC-004 Toolchain pins

| Tool | Pin | Why this version |
|---|---|---|
| .NET SDK | 10.0.402, `rollForward: disable` (`global.json`) | Matches the digest-pinned SDK image; LTS. `global.json` also selects Microsoft.Testing.Platform for `dotnet test` (required by xUnit v3 4.x on the .NET 10 SDK). |
| Node.js | 24.21.0 (`.node-version`, `engines`) | Active LTS. 24.22.0 was 2 days old (7-day rule). Node 22 is in maintenance. Build-time only; no Node.js at runtime (D1). |
| pnpm | 11.28.5 by sha512 (`packageManager`, Corepack) | pnpm 11 ships supply-chain defaults (release age, `blockExoticSubdeps`, `strictDepBuilds`) and is mature (since 2026-04). pnpm 12 is a 7-week-old Rust rewrite that itself needs install scripts. 11.28.6 was 1 day old. |

## DEC-005 One ASP.NET Core project for now

- **Decision.** Only `src/Goldmark.Api` and `tests/Goldmark.Api.Tests` exist.
  `Goldmark.Core`, `.Data`, `.Jobs`, `.Contracts`, `collectors/` and `spec/`
  (PRD §10) are created by the slices that first need them. §10 remains the target layout.
- **Rationale.** No empty projects or unused references. Solution file: `Goldmark.slnx`.

## DEC-006 HTTPS-only host; first-start certificate persisted in a volume (US-01)

- Kestrel listens on **HTTPS only** (8443 in the container, non-root). There is
  no HTTP listener and no redirect. TLS 1.2 and 1.3 only. No `Server` header.
- Operator certificate: PEM certificate (+ chain) and unencrypted PEM key via
  `Goldmark:Https:CertificatePath` / `KeyPath`.
- Otherwise, on first start, generate a **per-installation** self-signed ECDSA
  P-256 certificate (397 days, serverAuth, SANs from `Goldmark:Https:Hostnames`).
  It is written atomically to `/var/lib/goldmark/tls` (named volume, outside the
  image) with a `0600` key in a `0700` directory owned by uid 1654. It is
  regenerated when within 30 days of expiry, when host names change or when
  the files are unreadable; each regeneration is logged as a warning with the
  new SHA-256 fingerprint.
- Relative paths resolve against the content root.
- The certificate directory is forced to `0700` on every start. CI showed that
  the image's `COPY --chmod` on a directory is not applied consistently across
  BuildKit versions (the mount point was `0755`).
- An operator certificate/key pair is loaded during startup validation, so a
  malformed, encrypted or mismatched pair fails cleanly with the setting names
  instead of an exception during Kestrel setup (review finding). The whole
  bundle is imported as well, so a corrupt intermediate also fails validation
  (review finding).
- **Known limits, deferred** (review findings, documented in
  `docs/ops/tls-certificates.md`):
  - The 30-day renewal check runs at startup only. A process that runs
    longer than the certificate's remaining life keeps serving the old one;
    restarting within the 30-day window renews it. Hot reload comes with
    later certificate-management work.
  - On Windows (dev hosts only), the generated directory and key inherit the
    parent directory's ACL; the `0700`/`0600` modes apply on Linux, which is
    what the container runs. An owner-only ACL is deferred until a Windows dev
    host is verified (CI is Linux-only).
- Docs: `docs/ops/tls-certificates.md` (trust and replacement).

## DEC-007 HSTS: automatic (clarifies NFR-SEC-2)

- **Decision.** `Goldmark:Https:Hsts:Enabled` unset means **on with an
  operator certificate, off with the generated self-signed certificate**.
  `true`/`false` overrides. `max-age` 365 days (configurable, 1–730),
  `includeSubDomains` off, `preload` never.
- **Rationale.** HSTS removes the browser's certificate-warning bypass, so with
  an untrusted self-signed certificate it would lock users out. Docs: `docs/ops/hsts.md`.

## DEC-008 Health endpoints and their access policy

- Both endpoints answer `GET` and `HEAD` only; other methods get `405` (review finding).
- Readiness is anonymous, so it is coalesced: concurrent requests share one
  in-flight probe and a result is reused for 5 s. However many requests
  arrive, SQL Server sees at most one probe connection every 5 s (review
  finding).
  - The shared probe runs on its own 10 s timeout, not on the initiating
    request's token, so a client that disconnects cannot get a cancellation
    cached as everyone's result (review finding).
  - Cache age uses the monotonic clock, so a wall-clock correction cannot keep
    a stale result alive (review finding).
- Any other `/health/...` path is a `404` (GET/HEAD), never the SPA shell, so a
  mistyped probe path cannot report `200` (review finding).
- `GET /health/live`: no dependencies (SQL Server down does not affect it).
- `GET /health/ready`: opens a fresh, **unpooled**, TDS 8 strict-encrypted
  connection as the least-privilege runtime login and runs `SELECT 1`. A 10 s
  check timeout and a 5 s connect timeout bound it. 503 when SQL Server is unavailable.
- Responses are `Healthy` / `Unhealthy` text only, `no-store`. Failure logs
  carry the exception type and SQL error number at Warning; exception text
  only at Debug.
- **Access policy:** both are **anonymous by design**, declared on the endpoint
  with `EndpointAccess` metadata and `AllowAnonymous`. Every slice-1 endpoint is
  classified in `docs/security/endpoint-access.md`, and `EndpointInventoryTests`
  fails on any unclassified endpoint (seed of the M6 enumeration test, slice 3).
- **Done in slice 2 (DEC-023):** a schema-version compatibility check joins readiness in slice 2,
  with the migrations.
- **Deviation:** PRD §5.7 also lists `health` among `/api/v1` resources. Only
  `/health/*` (US-01) exists; whether `/api/v1/health` is needed is deferred to slice 8 (API contract).

## DEC-009 NuGet supply-chain controls

- `NuGet.config`: only nuget.org, package source mapping for `*`, and
  `signatureValidationMode=require` with the nuget.org repository-signing
  certificates as trusted signers. The current certificate expires 2027-05-18;
  add its successor when nuget.org publishes it.
- `Directory.Build.props`: lock files for every project, **locked-mode restore
  always**, NuGetAudit (`all`, `low`) with NU1901–NU1905 as errors, analyzers
  (`latest-recommended`) and code style enforced, warnings as errors.
- `Directory.Packages.props`: central package management.
- Committed before the first restore (commit history shows the order).

## DEC-010 NuGet dependencies for slice 1 and the release-age rule

- **Rule.** Same as npm: do not adopt a version published less than 7 days
  ago. **Exception:** a first-party security fix that removes a known
  vulnerability flagged by NuGetAudit, recorded here.
- **Runtime:** `Serilog.AspNetCore` 10.0.0, `Microsoft.Data.SqlClient` 7.1.1
  (7.x no longer pulls in Azure identity packages). `System.Security.Cryptography.Pkcs`
  10.0.13 is pinned directly: SqlClient 7.1.1 depends on 9.0.18, which has
  GHSA-846f-f764-76mx (high) and GHSA-2fmv-rv2c-59x7 (moderate); 10.0.13
  (2026-10-13) is the first fixed version. *Exception to the 7-day rule.*
- **Tests:** `xunit.v3` 4.0.2 on Microsoft.Testing.Platform and
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.12. `Microsoft.NET.Test.Sdk` and
  `xunit.runner.visualstudio` were removed: they are VSTest packages that MTP does not need.
- **Not added (deferred until used):** Dapper (slice 5). DbUp and Testcontainers were added in slice 2 (DEC-026).

## DEC-011 npm supply-chain controls and dependency allow-list (NFR-SEC-9)

- **Location.** The npm project root is `web/`, so the PRD's paths hold:
  `web/dependency-allowlist.json`, `web/.npmrc`, and pnpm settings in
  `web/pnpm-workspace.yaml` (pnpm 11 reads non-auth settings only from there;
  `.npmrc` keeps the registry pin and `ignore-scripts` for anyone running npm).
- **pnpm settings:** `minimumReleaseAge: 10080` (7 days) with
  `minimumReleaseAgeStrict: true`, `trustPolicy: no-downgrade`,
  `blockExoticSubdeps: true`, `ignoreScripts: true`, `strictDepBuilds: true`
  with an empty `allowBuilds`, `engineStrict: true`, exact versions.
- **Settings allow-lists (review finding).** `.npmrc` may contain only
  `registry`, `ignore-scripts` and `engine-strict`, and `pnpm-workspace.yaml`
  only the settings above. Scoped registries (`@scope:registry`), auth tokens,
  custom CAs, `strict-ssl`, `registries`, `overrides` and policy exclusions are
  rejected; otherwise an allow-listed name@version could be fetched from
  another source.
- **`scripts/check-dependency-policy.mjs`** (no dependencies; runs before
  install in CI and in the Docker build) fails on any of the following:
  - a direct dependency that is not allow-listed, or is listed in the wrong section;
  - an unpinned or non-registry version;
  - a banned package anywhere in `package.json` or the lockfile (including
    aliases and peer names);
  - a missing or weakened pnpm/npm setting, or any setting outside the reviewed lists;
  - an unapproved build script;
  - install lifecycle scripts in `package.json`;
  - a lockfile that does not parse against the lockfile schema (owner
    decision, after two review findings). `pnpm-lock.yaml` is **parsed
    structurally** as the YAML subset pnpm 11 writes: block mappings, block
    sequences, and the flow forms used for `resolution`, `engines`, `cpu`,
    `os` and `libc`. Every node is validated against the schema of the five
    sections and their known keys. Each package needs exactly one
    `resolution: {integrity: sha512-…}`. Anything else fails rather than
    being read differently: unknown sections or keys, duplicate keys, double
    quotes or escapes, quoted plain keys, odd or jumping indentation, anchors,
    aliases, tags, extra documents, and protocols or URLs in values.
- **Second stage after install** (`scripts/check-installed-tree.mjs`): asks
  pnpm what it installed (`pnpm list --json --depth Infinity`). Every package
  must resolve to the registry tarball for its own name and version, and no
  banned name may appear. It runs before lint, build and tests in CI, before
  the smoke test uses Playwright, and in the Docker web stage. Install
  scripts are disabled, so nothing fetched has run before it.
- Together the two stages have 48 tests.
- **Banned packages:** the PRD's `next`, `react-server-dom-*`, `@remix-run/*`,
  extended with `@react-router/*` (framework/server packages; `react-router`
  stays allowed for library mode) and `@vitejs/plugin-rsc`.
- **Allow-list:** contains the PRD's runtime set. Only `react` and `react-dom`
  are installed; Fluent UI, icons, Cytoscape and React Router are approved but
  wait for their slices. Dev entries with justification: `vite`, `typescript`,
  `@types/react`, `@types/react-dom`, `@types/node`, `eslint`, `@eslint/js`,
  `typescript-eslint`, `prettier`, `@playwright/test`.
- **Evidence:** the first lockfile was resolved under the policy (pnpm chose
  react 19.3.0 over the 6-day-old 19.3.1).

## DEC-012 Front-end tooling choices

- Vite 8 with its built-in JSX transform. No `@vitejs/plugin-react`: it avoids
  a Babel dependency tree, at the cost of React Fast Refresh in development.
- TypeScript **6.0.3**, not 7.x: `typescript-eslint` 8.71 supports `<6.1`.
- No `eslint-plugin-react-hooks` until components use hooks.
- No Vitest. Policy and build-output tests use `node:test`; browser behaviour
  uses Playwright. Playwright tests live in `web/e2e/` (deviation from the
  PRD's `tests/e2e/`) so they share the policed npm root. Playwright never
  downloads browsers: CI uses the runner's Chrome, local runs point at any Chromium.
- ESLint bans `dangerouslySetInnerHTML`, `eval`, `new Function`,
  `innerHTML`/`outerHTML`/`insertAdjacentHTML`/`createContextualFragment`/
  `setHTMLUnsafe`, `document.write(ln)`, and `react-dom/server`/`static` imports.
- Production build: no inline scripts or styles, no `data:` URLs, no source
  maps, hashed assets (tested in `web/tests/dist.test.mjs`).

## DEC-013 Base images pinned by digest; chiseled-extra runtime

| Stage | Image |
|---|---|
| web build | `node:24.21.0-trixie-slim@sha256:173f1258…fb66` |
| API build | `mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1…5ffa` |
| runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra@sha256:3ed65580…4374` |
| database | `mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04@sha256:742481d1…bab2` |

- Runtime is **chiseled** (no shell or package manager), uid 1654. The
  `-extra` variant adds ICU because Microsoft.Data.SqlClient throws
  `Globalization Invariant Mode is not supported` (found by the smoke test).
- Only `web/dist` is copied from the web-build stage; that stage receives no
  secrets. `.dockerignore` is an allow-list. It also re-excludes every
  git-ignored secret pattern (keys, certificates, `.env`, `*.local.json`,
  `.goldmark/`) after the `!src/` and `!web/` re-includes (review finding).
  The smoke test plants decoy files and proves none reaches the build context.
- Container healthcheck: `dotnet Goldmark.Api.dll healthcheck` calls
  `/health/live` over loopback HTTPS and pins the certificate on disk.

## DEC-014 Bundled SQL Server: Express, TDS 8 strict only, pinned certificate

- `MSSQL_PID=Express`. `ACCEPT_EULA` and the `sa` password must be supplied
  (Goldmark cannot accept the licence for the operator). SQL Server is on an
  internal network and not published to the host.
- On every start the SQL Server container (as the mssql user) ensures a
  certificate for `sqlserver`, and regenerates it if it is expiring, has the
  wrong host name, or does not match its key (for example after a renewal was
  interrupted; review finding). It publishes only the public certificate to a
  shared volume and sets `forceencryption=1` and **`forcestrict=1`**, so only
  TDS 8 strict clients can connect. This is stricter than NFR-SEC-4's
  "supported". The private key stays in a volume that only SQL Server mounts.
- Clients pin that certificate (`ServerCertificate` / `sqlcmd -J`).
  `TrustServerCertificate` is never used, including by the SQL healthcheck.
- `sql-volume-init` runs as root only to `chown` fresh volumes: no network,
  `CAP_CHOWN` only, and it never touches key material.

## DEC-015 Slice-1 database bootstrap and secrets handling

- `bootstrap-db` (one-shot `db-init` container) idempotently creates the
  `Goldmark` database and the runtime login with **CONNECT only**. DDL takes
  QUOTENAME'd parameters.
- **Goldmark only manages principals it created** (owner decision after
  six Codex review rounds). The database user it creates carries the
  extended property `goldmark:managed`. If the configured runtime login, or
  a database user of that name, already exists without the marker, bootstrap
  **refuses (exit 3) and changes nothing**, before any database is created.
  The operator can opt in to taking it over with
  `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`; its password is then set to
  `GOLDMARK_DB_PASSWORD` and it is marked as managed.
- Login, user and marker are created in **one transaction**. The password is
  first checked against SQL Server's policy inside a rolled-back
  transaction, so a rejected password leaves no database, login or user
  behind. Changing `GOLDMARK_DB_PASSWORD` rotates the managed login's password.
- **Kept:** after provisioning, bootstrap connects as the runtime login and
  refuses (exit 3) if its *effective* permissions exceed the baseline
  measured for a fresh login on SQL Server 2025:
  - server roles besides `public` (`sys.login_token`);
  - database roles besides `public` (`sys.user_token`);
  - server permissions beyond `CONNECT SQL` and `VIEW ANY DATABASE`;
  - database permissions beyond `CONNECT` and public's two
    `VIEW ANY COLUMN … KEY DEFINITION` grants.

  The first fixed-role check passed a login holding `securityadmin` and
  `db_securityadmin` (review finding). Slice 2 adds membership of
  `goldmark_runtime` to the baseline (DEC-025).
- **Done in slice 2b (DEC-031):** the audit below now runs on every
  `bootstrap-db` run, before any change, from the administrator's side and as
  each login. *Slice-1 text, kept for the record:* **removed and deferred to slice 2b** (DEC-021; issues labelled `slice-2`): the
  administrator-side audit of pre-existing principals. That covered explicit
  grants below database scope, ownership of securables, linked-server, remote
  and credential mappings including wildcard mappings, `guest` access in
  other databases, `guest`/`public` grants in system databases, and
  password-policy, expiry and deny state of adopted logins (issues #2, #3,
  #4). It matters when adopting principals, sharing an instance, or when
  someone changes a managed login out of band. The bundled SQL Server is
  fresh. For a managed login the post-provisioning check still catches
  role memberships and server/database-scope permissions, but **not** grants
  below database scope or ownership (issue #4).
- Validation: the runtime password must differ from the administrator
  password, be 12–128 characters, and the runtime login must not be the
  admin login or `sa`. The database name must not be a system database.
  The connect timeout must be 1–120 s, and the whole wait is cancelled at
  `WaitSeconds` plus one connect timeout. A pinned SQL certificate must
  parse as PEM or DER at startup; `bootstrap-db` applies the same check before
  connecting (review finding).
- The migration login and DML grants arrived with slice 2 (DEC-024, DEC-025).
- The `sa` password exists only in the `sqlserver` and `db-init` containers.
  The app receives only the runtime login.
- Secrets reach containers as environment variables from `deploy/.env`
  (documented `chmod 600`). Docker secrets were considered and deferred: the
  SQL Server image takes its password from the environment anyway.
- The app refuses to start (exit 2, setting names only) without host, user or
  password, with `sa` as the runtime user, or with encryption other than
  `Strict`/`Mandatory`.
- A setting with a value of the wrong type (e.g. `Goldmark__Https__Port=abc`)
  is also exit 2, naming the setting path. The value is never printed, because
  the configuration binder's own message contains it and it may be a secret.
  The web host and `bootstrap-db` share this check; `healthcheck` reports it
  with exit 1, because Docker reserves exit 2 for health checks (review finding).

## DEC-016 Logging (NFR-LOG-1)

- Serilog `RenderedCompactJsonFormatter` to stdout. The W3C trace id (`@tr`) is
  the per-request correlation id.
- Request logs carry method, the original path (never the query string),
  status and duration. Headers, cookies and bodies are never logged (tested
  with secret-bearing headers, cookies and query strings).
- Health probes log at Debug. Each host owns its logger (`preserveStaticLogger`);
  request logging uses it explicitly.
- OpenTelemetry (NFR-LOG-2) is not part of slice 1.

## DEC-017 Browser security headers; Trusted Types verified

- On every response: CSP `default-src 'self'; script-src 'self'; style-src 'self';
  img-src 'self'; font-src 'self'; connect-src 'self'; object-src 'none';
  base-uri 'none'; form-action 'self'; frame-ancestors 'none';
  require-trusted-types-for 'script'; trusted-types 'none'`, plus `nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, COOP/CORP
  `same-origin` and a restrictive `Permissions-Policy`.
- `trusted-types 'none'` (stricter than the PRD) forbids creating any policy
  until the single reviewed policy module exists.
- **Verified in Chromium and Google Chrome** (Playwright against the Compose stack, locally and in CI): React 19 +
  Vite 8 render with zero CSP/Trusted Types violations, in a secure context,
  and a raw `innerHTML` assignment is blocked.
- **Risk for slice 3** (moved from slice 5 by DEC-021): Fluent UI v9 injects
  styles at runtime (Griffel). It must be verified against `style-src 'self'`
  and `trusted-types 'none'` before adoption, as the first step of slice 3.
  If it needs a nonce or `unsafe-inline`, that comes back for a decision, not
  a silent change.
- **Resolved in slice 3 (DEC-037):** Fluent UI fails under `style-src 'self'` (every
  runtime `<style>` element is blocked); the owner chose a per-response style nonce.
  The shell's `style-src` is now `'self' 'nonce-…'`; every other response keeps the
  policy above.

## DEC-018 CI design, and CodeQL / dependency review availability

- `.github/workflows/ci.yml`:
  - Workflow token `contents: read`; no job references secrets.
  - Checkouts use `persist-credentials: false`; all actions are pinned by commit SHA.
  - Jobs: `dependency-policy` (before any install), `web` (lint, typecheck,
    format, build, tests, `pnpm audit`), `dotnet` (locked restore + NuGetAudit,
    `dotnet format --verify-no-changes`, Release build, tests),
    `compose-smoke` (builds the committed Dockerfile; real stack; browser).
- Dependabot covers npm, NuGet, the .NET SDK, Docker, Compose and Actions,
  with a 7-day cooldown.
- **CodeQL and dependency review (NFR-SEC-7, slice 1).** While the repository
  was private (personal account, no GitHub Advanced Security), neither could
  run, and CodeQL was not run on private code without a licence. After the
  owner made the repository public, both were added as real gates:
  - `dependency-review` (PRs): fails on any known vulnerability, severity
    `low` or higher, in runtime, development or unknown scopes, across the npm
    and NuGet lock files.
  - `codeql`: C# (manual build with the pinned SDK), JavaScript/TypeScript
    and GitHub Actions workflows, `security-extended` queries, on every PR,
    on `main`, and weekly.
  - These also keep running: NuGetAudit, `pnpm audit`, Dependabot, the
    dependency policy check, SHA-pinned actions and locked restores.

## DEC-019 Deferred to later slices

| Item | Slice |
|---|---|
| Migration login, DML grants, readiness schema check; `bootstrap-db` port and reserved-name validation (#7) | 2 (done: DEC-022…DEC-029) |
| Audit of pre-existing / adopted principals and shared-instance conditions: `guest` access in other databases (#2), wildcard linked-server and remote mappings (#3), ownership and lower-scope grants, `guest`/`public` grants in system databases, password-policy/expiry/deny state (#4) | 2b (done: DEC-031) |
| `docker-compose.external-sql.yml`; US-01 "use an existing SQL Server" | 2b (done: DEC-032, DEC-033) |
| Generated administrator, migration and runtime credentials; `sa` disabled in the bundled deployment | 2c (done: DEC-034) |
| Windows service distribution, installer wizard, Windows authentication | 4b (DEC-036) |
| Availability groups (and #20) | owner to place (DEC-030) |
| Generated certificate renewal without a restart (#5, DEC-006) | 24 |
| Owner-only ACL for the generated key on Windows dev hosts (#6, DEC-006) | 24 |
| Types & attributes admin screen (UI #14; no slice in PRD §11) | 5b |
| Settings screen (UI #20; no slice in PRD §11) | P1 (DEC-021) |
| Fluent UI v9 verification under the served CSP and Trusted Types (DEC-017) | 3, first step |
| Authentication, deny-by-default fallback policy, M6 enumeration test | 3 |
| OpenAPI document, `/api/v1/health` decision | 8 |
| PowerShell module and Pester (no executable PowerShell exists yet) | 9 |
| Release workflow, SBOM, cosign signing, collector signing, backup script | 24 |
| LICENSE, NOTICE, CONTRIBUTING (DCO), CODE_OF_CONDUCT | owner's licensing decision |

## DEC-020 Constraints of the build sandbox (not of the product)

The agent's sandbox sits behind a TLS-inspecting proxy, and some hosts (CRL
endpoints, PowerShell Gallery) are unreachable. While the repository was
private, the agent's GitHub API token could not see it (public-repository scope only). For **local verification only**:

- The image was built from a copy of the committed Dockerfile that mounts the
  proxy CA as a BuildKit secret on the two network steps. The committed
  `Dockerfile` is unmodified, and CI builds it as-is; that build passed the
  full smoke test on GitHub Actions.
- NuGet restores ran with `NUGET_CERT_REVOCATION_MODE=offline`; CI uses online
  revocation checks.
- The browser check ran on Chromium 141 (Playwright build 1194) driven by
  Playwright 1.64.0; CI uses the runner's Google Chrome.

## DEC-021 Approved MVP build plan (amended PRD §11)

- **Decision (owner, 2026-10-19).** Build PRD §11 slices 2–24 in order, one
  PR each, gated by the owner's APPROVED. P1 slices 25–30 wait until slice 24
  ships unless the owner pulls one forward. The full list, branch names and
  status live in `docs/HANDOFF.md`, which is updated in every slice PR.
- **Amendments to PRD §11:**
  1. **Slice 2 is split.** Slice 2 keeps the PRD scope (DbUp baseline,
     migration vs runtime logins) plus the readiness schema-version check
     (DEC-008) and #7. New **slice 2b** covers US-01 "use an existing SQL
     Server" (`docker-compose.external-sql.yml`) and the audit of adopted and
     shared-instance principals (#2, #3, #4). The audit runs whenever
     `bootstrap-db` adopts or revalidates a principal, in **both** the bundled
     and the external-SQL deployment (adoption with
     `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true` and out-of-band changes to a
     managed login apply to the bundled SQL Server too; DEC-015). PRD
     numbering is unchanged.
  2. **Screens without a slice:** UI #14 (Types & attributes admin) gets its
     own **slice 5b** (`slice/05b-types-admin`), after slice 5. UI #20
     (Settings) becomes **P1**, built with the P1 slices.
  3. **#5 and #6** (TLS renewal without restart; Windows key ACL) go in slice 24.
  4. **Fluent UI verification moves to slice 3** (from slice 5). It is the
     first step of slice 3: verify Fluent UI v9 (Griffel) under the served CSP
     (`style-src 'self'`, `trusted-types 'none'`). If it fails, stop and bring
     options to the owner before building any screen (DEC-017).
- **Rationale.** Slice 2 as written plus the deferred bootstrap work was too
  large to review as one PR; the existing-SQL work and the principal audit
  belong together (pre-existing principals are the main case on an existing
  instance), and neither blocks the baseline schema. The two screens
  and the TLS items needed an owner so they are not lost. Slice 3 builds the
  first screens, so Fluent must be proven there, before any screen depends
  on it. Slice 5b and Settings as P1 are owner decisions (2026-10-19).
- **Working rules** for the build (gating, review rounds, design handoffs,
  models and sessions) are recorded in `docs/HANDOFF.md`.

## DEC-022 Migrations run in `db-init` (`migrate`), never in the app

- **Decision (owner, 2026-10-19).** A new `migrate` command in the same image
  applies the schema migrations with DbUp, as the migration login (DEC-024).
  The one-shot `db-init` container runs `bootstrap-db migrate`; the app
  depends on it with `condition: service_completed_successfully`. The app
  container holds only the runtime login.
- **Deviation from US-01.** US-01 says "the app runs DbUp migrations on start
  using a separate migration login". Here the *image* runs them on every
  `docker compose up`, but in `db-init`, not in the app process. Rationale:
  the app container never holds a credential that can run DDL, so a
  compromised app cannot alter the schema, the role grants or the audit log's
  protection. The outcome US-01 asks for (migrations on start, separate
  migration login, DML-only runtime) is met.
- **Command chaining.** Database commands can be chained
  (`bootstrap-db migrate`); they run in order and the first non-zero exit
  stops the chain. The chiseled image has no shell, so chaining is done by the
  program. Any positional first argument is a command, so a typo
  (`bootstrap-dbb`) exits 2 instead of starting a web host that never exits
  and blocks the app.
- **How `migrate` works:**
  0. verifies the login it connected as: it must carry the `migration-login`
     marker and hold `db_owner` and nothing at server level, also when
     `migrate` runs without `bootstrap-db`; otherwise exit 3 before anything is
     read or changed (Codex on #14);
  1. takes an exclusive application lock
     (`sp_getapplock`, session-owned, 120 s), so concurrent runs cannot apply
     a script twice (tested);
  2. refuses (exit 3, nothing changed) if the journal contains a script this
     build does not have: the database was migrated by a newer or different
     build;
  3. runs DbUp with the scripts embedded in the API assembly
     (`db/migrations/NNNN_name.sql`, logical name `Goldmark.Migrations.*`),
     journal `dbo.SchemaVersions`, **one transaction per script**, variable
     substitution off, 10-minute script timeout;
  4. adds the managed runtime user to `goldmark_runtime` (only a user carrying
     the `runtime-login` marker; otherwise exit 3);
  5. verifies, as the runtime login, that its effective rights are exactly the
     role's, with nothing extra and nothing missing (DEC-025); otherwise exit 3.
     The runtime/migration/admin passwords must all differ, also when
     `migrate` runs on its own (Codex on #14).
- **Exit codes** as for `bootstrap-db`: 0 ok, 1 SQL/connection error or failed
  script, 2 invalid configuration, 3 refused.
- **Not covered:** down-migrations (forward-only by design); a script that
  cannot run in a transaction would need DbUp's per-script options (none so
  far).

## DEC-023 Readiness requires the expected schema version

- **Decision (owner, 2026-10-19).** `/health/ready` is healthy only when the
  database's schema version **equals** the version the build expects. Older
  (migrations not applied) and newer (a newer build migrated it) are both
  `503`. Both values are logged at Warning (EventId 1003,
  `DatabaseSchemaVersion`, `ExpectedSchemaVersion`); the response stays
  `Unhealthy` with no detail.
- **Versions.** The build's version is the number of its newest embedded
  script; the database's is the newest `Goldmark.Migrations.NNNN_*` entry in
  the journal (0 if there is none, or if the journal is not visible). The
  probe reads the journal as the runtime login, which holds `SELECT` on it and
  nothing else in `dbo`. The query replaces slice 1's `SELECT 1`, so the probe
  is still one query on one unpooled connection, coalesced and cached (DEC-008).

## DEC-024 Migration login: `db_owner` of the Goldmark database only

- **Decision (owner, 2026-10-19).** `goldmark_migrator` (configurable,
  `GOLDMARK_DB_MIGRATION_USER`) is created by `bootstrap-db` and is a member of
  `db_owner` of the Goldmark database only: no server roles, not sysadmin, no
  rights in other databases. `db_ddladmin` plus reader/writer is not enough,
  because migrations must `GRANT`/`DENY`.
- **Secret.** New required `GOLDMARK_DB_MIGRATION_PASSWORD`. It reaches only
  `db-init`, never the app container (smoke-tested via `docker inspect`). It
  must be 12–128 characters and differ from the runtime and admin passwords
  (exit 2), so the app's credential can never authenticate as `db_owner`.
- **Same rules as the runtime login (DEC-015).** `goldmark:managed` marker,
  refusal of unmarked existing principals unless
  `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`, password rotation, policy probe
  before anything is created. Both logins, both users, both markers and the
  `db_owner` membership are created in **one transaction**.
- **Marker values name the purpose** (`runtime-login`, `migration-login`). A
  managed principal configured for the other purpose (for example swapped
  names) is refused, so the runtime login can never be turned into `db_owner`
  by a configuration mistake (tested).
- **Verified as the login** after provisioning: no server role besides
  `public`, no server permission beyond the CONNECT baseline, no database role
  besides `public` and `db_owner`, and `db_owner` present.
- **#7 (closed here).** `bootstrap-db` rejects `Database:Port` outside
  1–65535, and login names `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA`,
  `public`, `goldmark_runtime` and `sa` for both logins (exit 2). The two
  logins and the admin login must all differ.

## DEC-025 Runtime permissions through the `goldmark_runtime` role

- **Decision (owner, 2026-10-19).** Runtime rights are defined in a migration
  (`0002_runtime_role.sql`) on the database role `goldmark_runtime`, owned by
  `dbo`; `migrate` makes the runtime user a member. The role has:
  - `SELECT, INSERT, UPDATE, DELETE` on schema `cmdb` (the application tables);
  - an explicit `DENY UPDATE, DELETE` on `cmdb.AuditLog` (append-only, US-07);
  - `SELECT` on `dbo.SchemaVersions` (readiness, DEC-023);
  - no DDL, so also no `TRUNCATE`, no `ALTER TABLE`, no
    `SYSTEM_VERSIONING = OFF`, no `IDENTITY_INSERT`, no security administration.
- **Why schema-level DML:** new application tables in `cmdb` get DML without a
  grant per table, and an object-level `DENY` still wins over it.
- **Verified as the runtime login** by `migrate`: roles exactly `public` and
  `goldmark_runtime`; server and database permissions at the CONNECT
  baseline; schema `cmdb` permissions exactly the four DML ones; nothing on
  schema `dbo`; only `SELECT` on the journal; no effective `UPDATE`/`DELETE` on
  the audit log. It also checks the other direction: every expected grant must
  be present (the four DML rights on `cmdb`, `SELECT`/`INSERT` on the audit log,
  `SELECT` on the journal), so a revoked grant fails `db-init` instead of the
  application (Codex on #14). A user-level `GRANT UPDATE` on the audit log is
  correctly accepted (the role's `DENY` still wins); removing the `DENY` is
  refused (tested).
- **Column-level grants (measured on SQL Server 2025).** A column-level
  `GRANT UPDATE (col)` to the **role** that holds the table-level `DENY`
  overrides it, and the runtime login can then update that column. The same
  grant to the user does not. `migrate` therefore checks `UPDATE` on every
  audit-log column (`HAS_PERMS_BY_NAME … 'COLUMN'`); the integration tests
  prove both the override and the refusal (Codex on #14). `bootstrap-db`'s own check now allows the `goldmark_runtime`
  membership.
- **Tested against SQL Server 2025** (21 denied statements, integration and
  smoke): DDL in `cmdb` and `dbo`, `DROP`, `ALTER`, index creation,
  `SYSTEM_VERSIONING = OFF`, DML on history tables, `UPDATE`/`DELETE`/
  `TRUNCATE`/`MERGE … DELETE`/`IDENTITY_INSERT` on the audit log, writes to
  the journal, `GRANT`, role and user changes. Each denied statement is
  followed by a check that nothing changed.
- **Done in slice 2b (DEC-031, #4):** auditing grants on other individual objects and
  ownership chains. Audit-log retention (NFR-LOG-3) will need a delete path
  that is not the runtime login (slice 4).

## DEC-026 Slice-2 NuGet dependencies: DbUp, Testcontainers

- **Approved by the owner (2026-10-19):** `dbup-sqlserver` 7.2.0 (2026-02-24)
  and `Testcontainers.MsSql` 4.16.0 (2026-10-08), both past the 7-day rule.
  `dbup-sqlserver` requires `Microsoft.Data.SqlClient` ≥ 6.1.4; the 7.1.1 pin
  (DEC-010) satisfies it, so the runtime tree gains only `dbup-core` 6.1.1.
- **Test-only transitive packages** (via Testcontainers): `Docker.DotNet.Enhanced*`
  4.3.3, `SSH.NET` 2026.0.0, `BouncyCastle.Cryptography` 2.7.0, `SharpZipLib`
  1.4.2, `Microsoft.Bcl.AsyncInterfaces` 6.0.0 and older Windows-only shims.
  All are older than 7 days, signed by nuget.org and pass NuGetAudit. They are
  never in the image (the Dockerfile restores only `Goldmark.Api`).
- Dapper still waits for slice 5.

## DEC-027 Database integration tests against real SQL Server 2025

- **Decision (owner, 2026-10-19).** A separate project,
  `tests/Goldmark.Api.IntegrationTests`, runs against a real SQL Server
  container started by Testcontainers. One container per test collection;
  tests isolate themselves with their own database and login names. The
  in-process tests (`Goldmark.Api.Tests`) stay fast and need no Docker.
- **Same image as production.** The fixture reads the digest-pinned image from
  `deploy/docker-compose.yml` (`x-sql-image`), so Dependabot's bump moves both,
  and starts it through `deploy/sqlserver/sql-entrypoint.sh` and
  `healthcheck.sh`. The tests therefore run against TDS 8 strict with the
  generated certificate pinned, exactly as the app does, through the app's own
  `SqlConnectionFactory`.
- **Testcontainers' resource reaper (Ryuk) is disabled.** It is a privileged
  container with the Docker socket, pulled by tag; the fixture disposes its
  container itself. A crashed test run can leave a container behind locally;
  CI runners are ephemeral.
- **CI.** New job `db-integration` (about 2 minutes; the container is ready in
  about 10 s). The `.NET` job now runs `Goldmark.Api.Tests` only, so the SQL
  tests do not run twice.
- **Coverage** (68 tests): fresh install, idempotent re-run, concurrent
  `migrate`, temporal tables, ownership by `dbo`, default tenant, 21 denied
  runtime statements, allowed DML with history, both logins' rights,
  refusal/adoption of an unmanaged migration login, swapped purposes, server
  role on the migrator, password rotation, newer/diverged schema, extra
  runtime grants, revoked expected grants, column-level audit-log grants, an
  unmanaged or over-privileged migration login, readiness healthy/older/newer
  with both versions logged.

## DEC-028 Applied migrations are immutable (hash manifest, CI)

- **Decision (owner, 2026-10-19).** DbUp records script names only, so an
  edited script would never run where it was already applied. Applied
  scripts are never edited; new scripts are appended.
- `db/migrations.sha256` lists every script with its SHA-256, in order.
  `db/check-migrations.sh [base]` fails when:
  - a script is unlisted, or a listed one is missing or changed;
  - numbering is not contiguous from `0001`, or a name is not `NNNN_lower_snake.sql`;
  - with a base revision, the base's manifest is not an unchanged prefix of
    the current one. This catches an edit *together with* its manifest line,
    as well as renames, removals and reordering.
- CI job `migrations` runs a self-test (12 cases: each tampering case fails,
  appending passes) and then the check against the PR base (or the previous
  `main` commit on push). `.gitattributes` forces LF, so hashes match on every
  OS.
- In the product, `migrate` also refuses a database whose journal has a script
  the build does not contain (DEC-022).

## DEC-029 Baseline schema (0001) choices

- **Schema `cmdb`**, owned by `dbo`, for application tables; DbUp's journal
  stays in `dbo`. Separating them lets the runtime role have DML on all
  application tables without any write access to the journal. Every schema
  object and the role are owned by `dbo`, never by the migration login.
- Tables per PRD §9.2: `Tenant` (the single default tenant
  `00000000-0000-0000-0000-000000000001` is seeded), `CiType`, `Ci`
  (system-versioned, history `cmdb.CiHistory`), `RelationshipType`,
  `Relationship` (system-versioned, history `cmdb.RelationshipHistory`),
  `AuditLog`.
- `Ci.Id` and `Relationship.Id` are `uniqueidentifier` values supplied by the
  application (UUID v7, FR-CM-1). `Attributes` uses SQL Server 2025's native
  `json` type (default `{}`). `LifecycleState` and `ImpactPropagation` are
  constrained to the PRD's values. Relationships cannot point to themselves
  and are unique per (from, type, to).
- Left to later slices on purpose: built-in CI and relationship types (5),
  `ChangedBy` defaults from `SESSION_CONTEXT` (7), content-hash semantics
  (10), `Source` foreign key (13), promoted attributes and further indexes (5).

## DEC-030 Deployment target: Docker first, Windows service and existing SQL Servers next

- **Decision (owner, 2026-10-19).** Docker Compose on Linux is the **first
  distribution and the development harness, not the destination**. The target
  deployments are a Windows service (running under a domain account or gMSA)
  with a dedicated, shared or availability-group SQL Server, authenticated with
  Windows authentication (preferred) or a SQL login, set up by an installation
  wizard. This came out of the PRD's architecture discussion and was not carried
  into the PRD (the PRD's US-01 and NFR-REL-2 describe containers only).
- **SQL Server 2025 is the minimum version** (owner; resolves OQ-2 and risk R2).
  The native `json` type stays.
- **`bootstrap-db` is the provisioning engine for every deployment** (option A).
  It is a plain CLI command with no Docker dependency; Compose only supplies its
  settings. An installer calls the same commands `db-init` runs. A DBA-run script
  followed by verification (option B) is a mode of it: Mode `Existing`
  (DEC-032). Its jobs are *create* (deployment-specific), *adopt/mark* and
  *audit* (the same everywhere).
- **What a port changes** (recorded so later slices keep it cheap): the password
  path (`PWDCOMPARE`, rotation, the SQL-login-only refusal, `SqlConnectionFactory`
  always sending a password); availability groups (logins per replica, a
  DBA-created database, `MultiSubnetFailover`); secret storage (`.env` → DPAPI or a
  gMSA); log sink (stdout → file or Event Log); Docker `HEALTHCHECK` → service
  recovery. The schema, migrations, T-SQL checks and app code carry over.
- **Standing rule** (HANDOFF rule 8): no new Linux- or Docker-only assumptions in
  application code. The principal audit (DEC-031) reads effective rights from the
  login's own token, so it already works for Windows logins and group-granted rights.
- **Open:** the slice for availability groups. *(The Windows service distribution, the
  wizard and Windows authentication are slice 4b: DEC-036.)*

## DEC-031 Principal audit of Goldmark's logins and the shared instance

- **Decision (owner, 2026-10-19).** Closes #2, #3, #4. `bootstrap-db` audits both
  logins and the instance **on every run, in both deployments**, before it
  changes anything, and again afterwards for principals the run created. Code:
  `src/Goldmark.Api/Commands/PrincipalAudit.cs`.
- **Two layers** (owner, so it ports to Windows authentication):
  - *administrator side:* catalogs only an administrator can see: login state
    (disabled, `CHECK_POLICY` off, `CHECK_EXPIRATION` on, expired, locked,
    must-change, `DENY CONNECT SQL`), credential mappings, direct server roles and
    grants, ownership of server- and database-level securables, linked-server and
    remote-login mappings (login-specific and for all logins), users in other
    databases, `guest` access in other databases, server-level grants to `public`,
    grants to `public`/`guest` in master, msdb, tempdb and model compared with a
    **measured baseline**, grants to `public`/`guest` in the Goldmark database,
    its `TRUSTWORTHY`/`DB_CHAINING` options and the server's cross-database
    ownership chaining;
  - *as each login:* effective rights from its own token: databases it can enter
    (as `guest` or as a user, directly or through a group); rights on other logins,
    server roles and endpoints; for the runtime login, rights on any database
    principal, schema, object or other securable besides DML on schema `cmdb` and
    `SELECT` on the journal. `migrate` runs this layer too (without the pass over
    other databases).
- **Finding IDs** (owner): `kind:part[:part…]`. Parts keep letters, digits and
  `_ . - $ # @ \ / [ ]`; a space becomes `+`; anything else is percent-encoded, so
  an ID never contains a comma, colon or white space. Every finding names its fix.
- **Accepting findings** (owner: by ID). `Goldmark:Bootstrap:AcceptedFindings`
  (`GOLDMARK_DB_ACCEPTED_FINDINGS`), IDs separated by commas or white space.
  - Only findings about other databases or the instance are acceptable:
    `guest-connect`, `linked-login-all-logins`, `remote-login-all-logins`,
    `system-grant`, `cross-db-chaining`, `database-unreadable`.
  - Findings about Goldmark's own logins or database must be fixed. That includes
    server-level grants to `public`: every login inherits them, so they are rights
    of the runtime login like any other (DEC-015).
  - A malformed ID, or the ID of a must-fix kind, is exit 2.
  - An accepted finding is still reported on every run; an accepted ID that does
    not occur is a warning.
  - There is no "accept all" setting.
- **Measured baseline.** `src/Goldmark.Api/Commands/Audit/sql-server-2025-public-grants.txt`
  (embedded): 3,467 grants to `public`/`guest` in the four system databases on the
  pinned image, stored as the set every database has (model's) plus each system
  database's extras. The integration test fails, naming a measured file, when a
  SQL Server image update changes it. A fresh Goldmark database has exactly the
  "every database" set (tested). Other CUs or editions may differ slightly; those
  differences are acceptable `system-grant` findings.
- **Review round 1 (Codex on #17), fixed:**
  - grants are compared by grantee, permission, **securable class** and target, so a
    schema named like a baseline view is not mistaken for it;
  - securable targets are `QUOTENAME`'d in IDs, and every name in a suggested fix
    is quoted (`[...]`, `N'...'`), so pasting a fix cannot run injected SQL;
  - accepted IDs match **ordinally** (case-sensitive servers);
  - when adopting, a same-name user that would be remapped is audited **before**
    any change;
  - new must-fix kinds: `goldmark-database-owner`: the Goldmark database is owned
    by a login without CONTROL SERVER, for example a lower-privileged login that
    created it first and stayed its dbo (security review). `goldmark-database-principal`:
    another user or application role in it holds a role, a permission beyond
    `CONNECT`, or owns a schema or object.
- **Review round 2 (Codex on #17), fixed:**
  - the owner check follows server-role membership at any depth (CONTROL SERVER
    through a custom role is accepted);
  - `goldmark-database-principal` also covers ownership of roles and every other
    securable, and `CONNECT WITH GRANT OPTION`;
  - new must-fix `runtime-role-grant`: explicit grants to `goldmark_runtime` beyond
    migration 0002's, including on system objects, which the as-the-login layer
    skips. Keep it in step with new migrations, like `VerifyRuntimeSql`;
  - databases that are not online are reported as `database-unreadable`;
  - grant targets resolve every securable class to its quoted name;
  - an unknown finding kind in `AcceptedFindings` is not echoed.
- **No other principal in the Goldmark database** (owner, 2026-10-19, option A):
  `goldmark-database-principal` stays must-fix. A reporting account with a role in
  the Goldmark database is refused; integrations use the API (PRD D2, §9.5).
- **Rights come only through the role.** A direct grant to a Goldmark user other
  than `CONNECT` is a finding even when a role's `DENY` currently overrides it
  (for example `GRANT UPDATE ON cmdb.AuditLog` to the runtime user). `migrate`'s
  effective-rights check (DEC-025) still accepts that case; `bootstrap-db` does not.
- **Performance (measured).** On Express, new databases get `AUTO_CLOSE ON`, and
  opening a closed database is slow. `HAS_DBACCESS` and any query into a database
  open it. The audit therefore makes **one** administrator pass over other
  databases per run (guest access and users of both logins together), before any
  change. `migrate` skips the pass over other databases (`bootstrap-db` ran it).
  The database `bootstrap-db` creates gets `AUTO_CLOSE OFF` (DEC-032). With these,
  the integration suite went from 6 min 20 s (first version) to about 5 min for
  121 tests in two containers.
- **Measured on SQL Server 2025:**
  - remote servers can no longer be created (`sp_addserver`, error 15663), but
    `sp_addremotelogin` on a linked server with RPC still creates a remote login
    for all logins (tested);
  - a fresh login sees schemas regardless of rights, but sees other database
    principals, types, certificates and objects only with a permission on them;
  - `fn_my_permissions` on itself returns `CONTROL`, `ALTER`, `IMPERSONATE` and
    `VIEW DEFINITION` (expected, excluded);
  - `guest`'s `CONNECT` in model is copied into every new database (tested: the
    Goldmark database is then refused, and that cannot be accepted).
- **Not covered:** Windows logins and groups (SQL logins only until the Windows
  deployment, DEC-030); availability-group replicas other than the one connected
  to; databases that cannot be read (reported as `database-unreadable`).

## DEC-032 `bootstrap-db`: administrator login, Mode Create/Existing, user names

- **Decision (owner, 2026-10-19).**
- **Administrator login.**
  - `Goldmark:Bootstrap:AdminUser` has **no default**.
  - **`sa` is refused** (exit 2) unless `Goldmark:Bootstrap:AllowSaAdmin` is set.
    Only the bundled Compose file sets it, until slice 2c. Reason: `sa` is a
    well-known name with every right, which attackers try first; the CIS
    benchmark recommends disabling it. *(Slice 2c: `AllowSaAdmin` is gone; `sa` is
    accepted only as the initial administrator with `AdminCredentialFile`, DEC-034.)*
  - The administrator needs **CONTROL SERVER** (checked first, exit 3), because
    the audit reads every catalog and `PWDCOMPARE` reads password hashes.
  - It stays in `deploy/.env` and is used on every `db-init` run (owner: option
    A for Docker). Running without it after installation belongs to the Windows
    service deployment (DEC-030), where the wizard holds it once.
- **Mode `Existing`** (`Goldmark:Bootstrap:Mode`, owner's open question on logins
  that operators create in advance). A DBA creates the database, both logins (with
  the configured passwords), their users and the migration user's `db_owner`
  membership. `bootstrap-db` then:
  - creates and changes nothing except the `goldmark:managed` marker;
  - refuses (exit 3, nothing changed) if anything is missing or a password
    differs, and never changes a password;
  - guards provisioning server-side: it throws instead of creating or changing a
    login, user or membership.

  Mode `Create` (default) keeps the slice-2 behaviour. The DBA script is in
  `docs/ops/external-sql-server.md` and is tested.
- **#15 (owner: refuse).** A database user mapped to a Goldmark login must have
  the login's name (exact, case-sensitive). Otherwise `bootstrap-db` refuses
  before any change and prints `ALTER USER [x] WITH NAME = [login]`, for adopted
  and for managed users (an out-of-band rename). A login that owns the Goldmark
  database (mapped as `dbo`) is refused with the `ALTER AUTHORIZATION` fix.
  `migrate` keeps finding the user by name.
- **SQL Server 2025 or later is checked first** (`ProductMajorVersion` ≥ 17, exit 3,
  nothing changed), so an older server never gets a database and logins that the
  first migration then cannot use (Codex on #17).
- **`AUTO_CLOSE OFF`** on the database `bootstrap-db` creates. On Express, new
  databases close whenever their last connection ends. Without this, every
  unpooled readiness probe (DEC-008) and every audit pass would reopen the
  database. Mode `Existing` leaves the DBA's database as it is (the DBA script sets it).
- **Done in slice 2c (DEC-034).** *Slice-2b text, kept for the record:* **Slice 2c (owner, 2026-10-19), next after 2b:** generated administrator,
  migration and runtime credentials (random names with a recognisable prefix,
  random passwords), stored in volumes; the app reads its password from a file; a
  dedicated login for the SQL container's healthcheck; `sa` disabled.
  `bootstrap-db` writes to a configured path, so the mechanism ports
  (DPAPI/installer later). Risks to settle there:
  - a tested recovery if the generated administrator is lost (on Linux there is no
    local-administrator bypass);
  - the image needs `MSSQL_SA_PASSWORD` on every start even with `sa` disabled.

## DEC-033 `docker-compose.external-sql.yml`

- **Decision (owner, 2026-10-19; US-01 "use an existing SQL Server").**
  - **A standalone file with `db-init` and `app` only**, not an override:
    removing services through an override is awkward. It uses Compose's default
    network, because the bundled file's `internal` backend network could not
    reach an outside server.
  - **Same `deploy/.env`.** `.env.example` has an "Existing SQL Server" section.
    Required: host, administrator user and password, and the two Goldmark
    passwords.
- **TLS.** `GOLDMARK_DB_ENCRYPT` is `Strict` (default) or `Mandatory`.
  `GOLDMARK_SQL_SERVER_CERT_FILE` pins the server certificate:
  - it is mounted read-only, and `ServerCertificatePath` is set only when the
    variable is set (`${VAR:+…}`; otherwise `/dev/null` is mounted and ignored);
  - with no pin, the certificate must chain to a CA the container trusts;
  - `GOLDMARK_DB_HOST_NAME_IN_CERTIFICATE` covers a differing name;
  - `TrustServerCertificate` stays off.
- **Smoke test.** `tests/smoke/external-sql.network.yml` (test-only override)
  attaches the external file's services to the bundled stack's network. The
  bundled SQL Server then plays an existing server:
  - a dedicated `smoke_dba` administrator (`sa` refused);
  - a database and logins created in advance (Mode `Existing`);
  - the app ready on its own port, with no administrator or migration credential
    in its environment;
  - a `guest-connect` finding refused, then accepted by its ID.

## DEC-034 Generated database credentials; `sa` disabled; tested recovery

- **Decision (owner, 2026-10-19; slice 2c, planned in DEC-032).** Goldmark
  generates its own logins instead of taking passwords from `deploy/.env`. Owner choices:
  **1A** upgrade in place, **2A** name format, **3A** no automatic rotation, **4A** the SQL
  container creates its healthcheck login, **5A** Mode Existing writes the files from
  settings. Operator docs: `docs/ops/database-credentials.md`, `docs/ops/database-recovery.md`.
- **Measured first (pinned SQL Server 2025 image, before `sa` was disabled anywhere):**
  - a disabled `sa` stays disabled across restarts; a changed `MSSQL_SA_PASSWORD` is
    ignored after the first start (the password hash does not change);
  - with SQL Server stopped, `mssql-conf -n set-sa-password` (a one-off container on the
    same data volume) sets the password **and re-enables `sa`** (`is_disabled` 1 → 0, sign-in
    works). It runs `sqlservr --setup --reset-sa-password`;
  - `sqlservr --force-setup` rebuilds master and model (every login lost; run directly it
    does not exit but keeps serving). The user databases' files survive and attach again
    (`FOR ATTACH`) with their data, their users orphaned. Documented as a last resort only.
- **Credential files** (`src/Goldmark.Api/Configuration/CredentialFile.cs`): one JSON file
  per login, `{"user", "password"}`, `0600` in a `0700` directory (forced on every write),
  written atomically; contents never appear in messages. Paths are settings
  (`Goldmark:Database:CredentialFile`, `Goldmark:Migration:CredentialFile`,
  `Goldmark:Bootstrap:AdminCredentialFile`), so the Windows service can point them at a
  DPAPI store (rule 8). Names: `goldmark_<purpose>_` + 10 random `[a-z0-9]`; passwords: 32
  random letters and digits with every class.
- **Where a login comes from** (`bootstrap-db`, per login): settings, when a password is
  given (written to the file; a name without a password is exit 2) → else the file → else,
  in Mode Create, generated. When the file is missing but a user carrying the purpose's
  marker exists (upgrade from settings, lost volume), that login keeps its name and gets the
  generated password (two such users: exit 3). Files are written after the pre-change audit
  and **before** the logins change, so an interrupted run is finished by the next one.
  `migrate` and the app read the file; settings given as well must match it (exit 2).
- **Generated administrator** (bundled SQL Server only): with `AdminCredentialFile`,
  `AdminUser` must be `sa`; `sa` without it is exit 2 (`AllowSaAdmin` removed).
  - While `admin.json` is missing, `bootstrap-db` signs in as `sa`, does its work, then
    creates `goldmark_admin_<suffix>` (sysadmin) — SQL first, file second, so a failure in
    between is repaired by the next run, which still finds `sa` enabled — signs in with it,
    checks CONTROL SERVER, and disables `sa`.
  - While `admin.json` exists, `sa` is never tried; every run disables `sa` again if it was
    re-enabled. A login failure with the file present, or `sa` disabled (18470) without it,
    is exit 1 with the recovery steps; 18470 is not retried.
  - The administrator's SID is recorded on the Goldmark database (`goldmark:admin-login`). It
    is a hint only (the migration login is `db_owner` and can change it): it is reused, with a
    new password, only if it is a SQL login with the `goldmark_admin_` prefix that already holds
    sysadmin; otherwise a new administrator is created. sysadmin is never added to an existing
    login (tested with a tampered marker).
  - A database this mode creates is owned by `sa` (disabled), not by the generated login, so a
    recovery cannot orphan the owner.
- **Healthcheck login (4A):** `deploy/sqlserver/health-login.sh`, started in the background by
  the SQL entrypoint, creates or repairs `goldmark_health_<suffix>` (CONNECT SQL only) as `sa`
  while its credential (`sql-tls-private/healthcheck.credential`) is missing or does not sign
  in. `healthcheck.sh` uses only that login, so the container is not healthy, and `db-init`
  cannot run and disable `sa`, before it exists. With the file lost and `sa` disabled it stays
  unhealthy and logs the recovery. Passwords reach `sqlcmd` through the environment, not argv.
- **Compose.** Bundled: `goldmark-db-runtime` (db-init read-write, app read-only) and
  `goldmark-db-admin` (db-init only; migration and administrator). `deploy/.env` holds only
  `SQLSERVER_ACCEPT_EULA` and `SQLSERVER_SA_PASSWORD`, which the image needs on every start.
  External file: the same two volumes; Mode Create generates unless the four login settings
  are set; Mode Existing (5A) requires them and writes them to the files; the operator's
  administrator stays in `.env`. The image creates both mount points owned by uid 1654.
- **Upgrade (1A).** A pre-2c bundled stack: the first run finds `goldmark_app` and
  `goldmark_migrator` by marker, keeps the names, generates passwords, creates the
  administrator and disables `sa`. External installations: the passwords in `.env` seed the
  files. Tested in both integration collections.
- **Rotation (3A):** none automatic (`docker compose up` reruns `db-init`; the app reads its
  file only at start). A password changes when a file is lost, or when settings give one
  (`db-init` rotates the login and rewrites the file). A rotate command is later work.
- **Recovery (tested):** stop the stack; if `admin.json` exists but fails, remove the
  `goldmark-db-admin` volume; `docker compose run --rm --no-deps --entrypoint
  /opt/mssql/bin/mssql-conf sqlserver -n set-sa-password` with SQL Server stopped; start.
  The smoke test runs it end to end; integration tests cover each `bootstrap-db` path.
- **Not covered:** Windows authentication, DPAPI and the installer (DEC-030); availability
  groups (#20). Anyone with access to the Docker volumes can read the credentials, as they can
  read the database files.

## DEC-035 Principal audit: securables owned by `public`, the owner's effective CONTROL SERVER

- **Decision (owner: deferred from PR #17 to slice 2c; closes #18 and #19).**
- **#18, new must-fix kind `goldmark-public-owner:<db>:<kind>:<name>`:** any securable in the
  Goldmark database owned by `public` (every class in the ownership catalogs). Measured on
  SQL Server 2025:
  - `ALTER AUTHORIZATION ON SCHEMA::… TO [public]` and `ON ROLE::… TO [public]` work
    (`public` must be bracketed: it is a keyword);
  - ownership by `public` gives every user CONTROL: a CONNECT-only user deleted rows in a
    `public`-owned schema and joined a `public`-owned role; ownership by a role gives its
    members CONTROL;
  - a fresh database has no securable owned by `public`, so no built-in exception was needed
    (the issue allowed one for the built-in schemas).
- **#19, `goldmark-database-owner` honours DENY:** the owner's CONTROL SERVER is checked by
  impersonating it (`EXECUTE AS LOGIN` + `HAS_PERMS_BY_NAME`, in one `sp_executesql` batch run
  in master, which every login can enter). `sa` (0x01) is accepted without impersonation, so
  a disabled `sa` owner is fine; an owner that is not a login, or cannot be impersonated, is a
  finding. Measured: with CONTROL SERVER through a server role and a direct `DENY CONTROL
  SERVER`, impersonating the login fails (error 916, no access to master) and is reported;
  without the DENY it returns 1; a disabled login can still be impersonated.
- **Tests:** both `ALTER AUTHORIZATION … TO [public]` cases refused; the measurement itself;
  the role-plus-DENY owner refused, then accepted after `REVOKE`; the nested-role owner from
  #17 still accepted.

## DEC-036 Windows service distribution placed as slice 4b

- **Decision (owner, 2026-10-19, option B of three, asked at the end of slice 2c).** The
  Windows service distribution (DEC-030) becomes **slice 4b** (`slice/04b-windows-service`),
  after slice 4 and before slice 5. Rejected: A, now as slice 2d (it would delay slice 3's
  Fluent UI verification, the plan's largest open risk, and need a Windows CI job first);
  C, after slice 24 (about twenty slices tested only on Docker, so Windows problems surface
  late).
- **Rationale.** Slices 3 and 4 add what a Windows service stores differently: sign-in keys
  (ASP.NET Core Data Protection, NFR-SEC-5) and the audit log's output. Porting after them
  ports each once, while the codebase is still small, and the UI risk is tested first. Rule 8
  keeps the wait cheap.
- **Scope (from DEC-030, DEC-034):** service host; installer wizard; Windows authentication to
  SQL Server (gMSA), including the principal audit for Windows logins and groups and the
  password path (`PWDCOMPARE`, the SQL-login-only checks, `SqlConnectionFactory` always sending
  a password); the credential-file paths on a DPAPI-protected store; a log sink for a service
  (file or Event Log); service recovery in place of the Docker `HEALTHCHECK`; the
  `rotate-db-credentials` command (DEC-034, 3A); a Windows CI job. Its "done when" is set by
  the owner when the slice starts.
- **Not in it:** availability groups and #20, still for the owner to place (DEC-030).

## DEC-037 Fluent UI under the CSP: a per-response style nonce

- **Measured first (slice 3, before any screen).** A production build of a Fluent UI v9
  sampler (`@fluentui/react-components` 9.74.9, Griffel 1.21.4), served by the real API under
  the slice-1 policy, renders unstyled in Chrome 155: Griffel and FluentProvider create 22 empty
  `<style>` elements and fill them through `CSSStyleSheet.insertRule`, and `style-src 'self'`
  blocks creating them (22 `style-src-elem` violations, no sheets). Griffel then drops every rule
  silently (`createIsomorphicStyleSheet` falls back to an SSR buffer when `sheet` is null), so the
  check asserts computed styles, not only the absence of violations. Trusted Types caused nothing.
  The same result with the server sending the sampler itself and with the test swapping it in.
- **Decision (owner, 2026-10-20: 1B, 2A, after a Codex review).** Options measured:
  - **A** the hash of the empty string in `style-src`: works, but authorises *every* empty
    `<style>` element on the page, not only Fluent's; rejected by the owner as a known broadening
    when a narrower supported mechanism exists;
  - **B** a per-response nonce through Griffel's supported renderer option: **chosen**;
  - **C** `'unsafe-inline'`: breaks NFR-SEC-2; **D** replacing Fluent: changes PRD §8.1;
  - **E** Griffel build-time extraction plus a static theme without FluentProvider: not pursued
    and `@griffel/vite-plugin` not added (owner). It would not remove FluentProvider's runtime
    theme element anyway.
- **How it works** (`Hosting/SpaShell.cs`, `Security/SecurityHeadersMiddleware.cs`, `web/src/styleNonce.ts`):
  - every shell response (GET or HEAD on any client route) gets 256 bits from the OS CSPRNG,
    base64url-encoded, used once;
  - the nonce goes in `style-src` only (`style-src 'self' 'nonce-…'`); `script-src` stays `'self'`,
    and every other response, including error pages, keeps the slice-1 policy. The middleware adds
    it only to a 200 response that set it, and refuses anything but base64url of at least 128 bits;
  - the shell template is the built `index.html` with `<meta property="csp-nonce"
    nonce="__GOLDMARK_STYLE_NONCE__">`, read once; the server writes the nonce into that one
    attribute (a template without exactly one placeholder is a 500 without a nonce; no build is a 404);
  - the app reads it through the element's `nonce` property, never `content`; browsers hide the
    attribute value from the DOM, so CSS selectors cannot read it. `createStyleRenderer()` passes it
    to Griffel's `createDOMRenderer(document, { styleElementAttributes: { nonce } })` through
    `RendererProvider`; FluentProvider stamps its theme element from the same attributes;
  - the shell is `Cache-Control: no-store`, with no `ETag`, `Last-Modified` or `Accept-Ranges`, and
    every conditional or range request gets a complete 200, so a body and a policy from different
    responses can never be combined. Fingerprinted assets stay `immutable`;
  - HTML is never served as a static file (every HTML type is removed from the static file
    middleware's content types), so `/index.html`, case variants and Windows aliases such as 8.3
    names cannot return the template without a nonce;
  - the nonce is never logged or persisted: request logs carry method, path and status only, and
    `StyleNonceFeature` is not a record, so formatting it prints no value.
- **Tests:** in-process (`SpaHostingTests`, `SecurityHeadersTests`): a distinct nonce on 50
  consecutive responses, header and body equal, at least 128 bits; conditional and range requests
  (`If-None-Match`, `If-Modified-Since`, `If-Match`, `If-Unmodified-Since`, `Range` + `If-Range`)
  get a complete matching 200 with no validators; HEAD; `/index.html`, `/INDEX.HTML`, `/index.htm`
  and another `.html` file are 404; broken template and missing build carry no nonce; no nonce in
  the log; nonce only in `style-src`; malformed nonces refused. Browser (`web/e2e`): the shell's
  header and `meta.nonce` agree, the attribute reads as empty, a reload gets a new nonce, injected
  styles and `style` attributes stay blocked (`spa.spec.ts`); the Fluent check (`fluent.spec.ts`).
- **Fluent check** (`web/e2e/fluent-check/`): a sampler page with an in-page self-check that shows
  PASS or FAIL in any browser: the nonce on the page and on every style element, the attribute
  hidden, theme and component styles applied (computed values), keyframes running (a
  `CSSAnimation` and its `@keyframes` rule), Fluent's icon font loaded through `@font-face`, the
  theme switched after mount, menu/popover/dialog/drawer/toast portals styled and positioned, a
  portal in a nested dark theme, a portal into a custom mount node, mirrored right-to-left rules,
  zero violations during normal use, then positive controls: an injected style, an empty unnonced
  style, a wrong-nonce style, a `style` attribute and a Trusted Types policy are all blocked, and a
  style carrying the page nonce is applied. `fluent.spec.ts` adds focus trapping and focus return
  (dialog, drawer), menu keyboard navigation, tooltip, combobox and grid sorting, and requires the
  final violation list to be exactly the controls'. In CI the real server sends the real shell
  (its HTML, nonce and headers) and only the shell's script is swapped for the sampler's; that is a
  regression check, not delivery qualification. With `GOLDMARK_FLUENT_CHECK_DIRECT=1` the base URL
  is a real server whose web root is the sampler build.
- **Codex review findings fixed** before the gate: late violations could pass (now exact); keyframes
  were not proved (now a running animation and its rule); focus trapping was claimed, not tested;
  "every way" became "representative ways"; fonts, theme change after mount, nested-theme and
  custom-mount portals, right-to-left and Drawer added.
- **Qualification with the real server sending the sampler page (2A), 2026-10-20.** The API's Release
  build (Production environment) with the sampler build as its web root, on the owner's WSL machine;
  Edge and Firefox opened it from Windows by hand after the owner checked the certificate's SHA-256
  fingerprint (`4C:6C:AA:7F:…:FD:DC:D8:F2`). All three pass, so the gate is passed (2026-10-20):

  | Browser | Version | Result |
  |---|---|---|
  | Google Chrome (Linux, headless, Playwright) | 155.0.8059.84 | **Pass**: 12/12 self-checks, keyboard and pointer checks, exact violation list, new nonce on reload |
  | Microsoft Edge (Windows, 64-bit) | 155.0.4283.62 | **Pass**: 12/12 self-checks; exactly the five control violations |
  | Mozilla Firefox (Windows, 64-bit) | 158.0.1 | **Pass**: 12/12 self-checks; exactly the five control violations, so Trusted Types are enforced too |

- **Not covered:** Tabster's focus-outline feature writes a `<style>` with text and no nonce
  (`tabster` `Outline.js`); Fluent does not use it, and it would be blocked, visibly. Container
  queries (not adopted), web fonts other than Fluent's icon font, the Reporting API, and keyboard
  behaviour outside Chrome. The product app does not render Fluent yet; screens use
  `createStyleRenderer()` once the design handoff is approved.

---

## Slice 1 acceptance mapping

Status: **Met** = implemented and executed with evidence; **Unmet** = not
done; **Not executed** = implemented but the check has not run (and why).
"Local" means the agent's sandbox. For the evidence, see the PR description.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | `docker compose up` serves the health endpoints and the empty SPA (PRD §11 slice 1) | **Met** (local and CI) | Compose smoke test 36/36 locally and on GitHub Actions (PR #1, run 38981505549) |
| 2 | CI green (PRD §11) | **Met** | PR #1: all nine checks pass, including dependency review once the dependency graph was enabled |
| 3 | An unlisted npm dependency fails CI (PRD §11) | **Met (local)**: checker test + CI step before install | `check-dependency-policy.test.mjs`; demonstrated in the PR |
| 4 | CI: build, test (PRD §11) | Met (defined); run not executed | `ci.yml` |
| 5 | CI: CodeQL, dependency review (PRD §11, NFR-SEC-7) | **CodeQL met**: C#, JS/TS and Actions pass; its first run found a real issue, which was fixed. **Dependency review met** once the owner enabled the dependency graph | DEC-018 |
| 6 | Dockerfile; Compose with SQL Server 2025 **Express** (PRD §11) | **Met (local)** | Smoke test: EngineEdition 4, major version 17 |
| 7 | `/health/live`, `/health/ready` (US-01) | **Met** | 3 in-process tests + 5 smoke checks |
| 8 | Serilog JSON; no credentials, auth headers or cookies in logs (PRD §11, NFR-LOG-1) | **Met** | `LoggingTests`; smoke: JSON lines, no secret in any container log |
| 9 | NFR-SEC-9 web controls: frozen lockfile, no install scripts, release age, allow-list, SSR/RSC ban, secret-free build job | **Met** (local); CI not executed | DEC-011, `ci.yml` `web` job |
| 10 | SDK / Node / pnpm pinned; controls committed before install; lockfiles; frozen/locked installs | **Met** | DEC-004/009/011; commit order |
| 11 | Only dependencies this slice uses | **Met** | DEC-010/011 |
| 12 | Base and database images pinned by digest | **Met** | DEC-013 |
| 13 | Front-end build has no secrets; only static assets reach the runtime image | **Met** | Dockerfile, `ci.yml` |
| 14 | Required secrets, no default passwords; startup fails if missing (US-01) | **Met** | Compose refuses; app exits 2 (smoke); validator tests |
| 15 | Liveness independent of SQL Server | **Met** | In-process + smoke (SQL Server stopped) |
| 16 | Readiness runs a real query as a minimally privileged login, not `sa`; unhealthy when SQL Server is down; recovers | **Met (local)** | Smoke: 503 within 60 s, 200 after restart; privileges `0000`; DDL denied |
| 17 | Health endpoints' access policy identified | **Met** | DEC-008, `endpoint-access.md`, inventory test |
| 18 | Readiness checks schema compatibility | **Deferred to slice 2** (instructed); met in slice 2 | DEC-008, DEC-023 |
| 19 | HTTPS on by default; per-installation certificate persisted outside the image; replacement documented (US-01) | **Met (local)** | Smoke: served = volume; survives re-creation; key 0600; TLS < 1.2 refused |
| 20 | Certificate trust and HSTS documented separately | **Met** | `docs/ops/tls-certificates.md`, `docs/ops/hsts.md` |
| 21 | CSP, Trusted Types and ESLint controls; incompatibilities reported | **Met**; no incompatibility found | Browser test; ESLint config; Fluent UI risk noted (DEC-017) |
| 22 | Fast in-process HTTP tests **and** a real Compose smoke test with health checks and bounded waits | **Met** | 80 xUnit tests; `compose-smoke.sh` |
| 23 | Least-privilege workflow permissions | **Met** (static) | `ci.yml` |
| 24 | `SECURITY.md` | **Met** | `SECURITY.md` |
| 25 | US-01: DbUp migrations on start with a separate migration login; runtime login DML-only | **Unmet in slice 1**; met in slice 2, with migrations in `db-init` (DEC-022) | DEC-015, DEC-022, DEC-025 |
| 26 | US-01: option to use an existing SQL Server | **Unmet in slice 1** (slice 2b, DEC-021) | DEC-019 |

## Slice 2 acceptance mapping

Same status vocabulary as slice 1. "Integration" = `tests/Goldmark.Api.IntegrationTests`
(Testcontainers, SQL Server 2025); "smoke" = `tests/smoke/compose-smoke.sh`. Local runs:
156 in-process tests, 68 integration tests, 63 smoke checks, all passing.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | DbUp baseline: Tenant, CiType, Ci (temporal), RelationshipType, Relationship (temporal), AuditLog (PRD §11) | **Met** | `0001_baseline.sql`; integration: tables, temporal type and history tables, `dbo` ownership |
| 2 | Migrations idempotent (PRD §11) | **Met** | Integration: re-run applies nothing and leaves the journal identical; concurrent runs apply each script once. Smoke: re-run reports "no migrations to apply" |
| 3 | Runtime login cannot run DDL (PRD §11, NFR-SEC-4) | **Met** | Integration: 21 denied statements, each followed by a no-change check. Smoke: `CREATE TABLE` denied, privileges `0000` |
| 4 | Runtime login cannot UPDATE/DELETE the audit log (PRD §11, US-07) | **Met** | Integration: UPDATE, DELETE, TRUNCATE, MERGE…DELETE, IDENTITY_INSERT denied; INSERT/SELECT allowed. Smoke: the same against the Compose stack |
| 5 | Separate migration and runtime logins (US-01, NFR-SEC-4) | **Met** | DEC-024, DEC-025; integration `MigrationLoginTests`; smoke: migrator `db_owner`, not sysadmin, no server roles |
| 6 | Migrations run in `db-init`; the app holds only the runtime login (owner, DEC-022) | **Met**; deviation from US-01 wording recorded (DEC-022) | Smoke: the app container's environment has no migration or `sa` credential |
| 7 | App depends on `db-init` completing (owner, DEC-022) | **Met** | `deploy/docker-compose.yml` (`service_completed_successfully`); smoke: stack becomes healthy |
| 8 | Readiness requires schema version == the build's, logs both, 503 on any mismatch (owner, DEC-023) | **Met** | Integration: healthy / older (0) / newer (9999), EventId 1003 with both values. Smoke: 503 on a newer schema, logged, recovers |
| 9 | Migration password only in `db-init`; rejected if equal to the runtime or admin password (owner, DEC-024) | **Met** | Unit tests; smoke: exit 2 for an equal password; `docker inspect` of the app |
| 10 | Runtime rights via `goldmark_runtime`, explicit DENY on AuditLog (owner, DEC-025) | **Met** | `0002_runtime_role.sql`; `migrate` verifies effective rights in both directions, including per-column audit-log rights (integration: 12 tamper cases) |
| 11 | Integration tests: same SQL image digest as Compose, one container per collection (owner, DEC-027) | **Met** | Fixture reads `x-sql-image` from Compose; `SqlServerTests` collection |
| 12 | Migration immutability: hash manifest, CI fails on a changed script (owner, DEC-028) | **Met (local)**; CI on the PR | `db/check-migrations.test.sh` (12 cases), CI job `migrations` |
| 13 | #7: `Database:Port` validated; `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA` rejected | **Met** | Unit tests (also `public`, `goldmark_runtime`, `sa`, for both logins) |
| 14 | CI green | **Met** | PR #14: 12 checks green on the first push |

## Slice 2b acceptance mapping

Same status vocabulary as slice 1. Local runs (agent's sandbox), after review round 2: 208
in-process tests, 132 integration tests (two SQL Server 2025 containers), 72 smoke checks (63
before), all passing.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | US-01: option to use an existing SQL Server | **Met (local)** | `deploy/docker-compose.external-sql.yml`; smoke: external project against the bundled SQL Server as a stand-in (Mode Existing, `smoke_dba`), app ready on its own port |
| 2 | Logins that operators create in advance (owner's open question) | **Met** | Mode `Existing` (DEC-032); integration `ExistingSqlServerTests` (happy path + 5 refusals with nothing changed); smoke |
| 3 | Administrator login: no default, `sa` refused outside the bundled stack, CONTROL SERVER required (owner) | **Met** | Unit tests; integration (dedicated sysadmin works, `sa` exit 2, weak administrator exit 3 with nothing created); smoke (`sa` exit 2) |
| 4 | #2: every user database where `guest` has `CONNECT` is refused (exit 3, nothing changed) unless accepted; message names the database and the revoke; refused, then passes after `REVOKE` | **Met** | `InstanceAuditTests` (other database; model, which is then copied into the Goldmark database; the Goldmark database itself, not acceptable; the login's own view); smoke end to end with the ID |
| 5 | #3: linked-server mappings for the login and wildcard ones with a fixed credential; wildcard and login-specific remote logins; default self mapping not reported; exit 3 | **Met** | `InstanceAuditTests` (wildcard linked login, remote login, default mapping passes); `PrincipalAuditTests` (login-specific mapping) |
| 6 | #4: a read-only check per gap, before any change when adopting and again after provisioning; exit 3 with every finding named | **Met** | `PrincipalAuditTests`: 12 adopted-login cases (policy, expiration, must-change, disabled, `DENY CONNECT SQL`, credential, server grants and roles, mapped user, owned database, linked login) and 14 out-of-band changes to a managed login (roles, direct grants, `DENY CONNECT`, ownership, role grants on schemas, objects, types, certificates, users, `public` grants, `TRUSTWORTHY`) |
| 7 | #4: system-database `guest`/`public` grants compared with a measured SQL Server 2025 baseline, not skipped | **Met** | Embedded baseline (3,467 grants); `The_system_database_baseline_matches_the_pinned_SQL_Server_image`; `InstanceAuditTests` theory (msdb, master `xp_cmdshell`, tempdb) |
| 8 | #4: DEC-015 updated | **Met** | DEC-015, DEC-031 |
| 9 | #15: a login mapped to a differently named user is refused with a clear message (owner: refuse) | **Met** | `PrincipalAuditTests` (adopted `it_other_name`: refused, then adopted after the rename; managed user renamed out of band) |
| 10 | The audit runs whenever a principal is adopted or revalidated, for both logins, in both deployments (DEC-021) | **Met** | Every `bootstrap-db` run; both Compose files; integration covers both logins; smoke covers both files |
| 11 | Findings accepted by ID (owner) | **Met** | `FindingPolicyTests`, validation tests (exit 2 for malformed or must-fix IDs), integration accept-and-warn cycle, smoke |
| 12 | Codex round 1 (#17): securable class in baseline keys, older servers refused, adopted same-name user audited first, quoted fixes, exact ID matching, database owner and other principals | **Met** | Unit tests (quoting, exact matching); integration: schema named like a baseline view, adopted `db_owner` user refused with nothing changed, database created first by a `dbcreator` login refused, extra principal with a role refused. The version check has no test: it would need a pre-2025 image |
| 13 | Codex round 2 (#17): owner through server roles, role ownership and `CONNECT WITH GRANT OPTION`, grants to `goldmark_runtime` on system objects, databases not online, names for every securable class, unknown kinds not echoed | **Met** | Integration: nested server role with CONTROL SERVER accepted; role owned by another user refused; `CONNECT WITH GRANT OPTION` refused; `SELECT ON sys.objects` to the role refused; offline database refused, accepted, fixed; certificate grant named `[ItCert2]`; the login's own view still finds role-granted rights. Unit: unknown kind not echoed |
| 14 | CI green | **Met** | PR #17: 12 checks green on every push |
| 15 | Codex round 3 (past the two-round limit) | Owner: fix the docs example, defer the rest | Docs fixed in PR #17; #18 and #19 deferred to 2c, #20 to the availability-group slice |

## Slice 2c acceptance mapping

Same status vocabulary as slice 1. Local runs on the owner's WSL machine: 236 in-process
tests, 147 integration tests (three SQL Server 2025 containers), 82 smoke
checks, all passing.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | `deploy/.env` holds only the `sa` password the image needs (HANDOFF §1) | **Met** | `.env.example`, both Compose files; smoke: `.env` has only `SQLSERVER_ACCEPT_EULA` and `SQLSERVER_SA_PASSWORD`, Compose refuses without them, the app's environment has no password, db-init's none of the generated ones |
| 2 | Generated administrator, migration and runtime logins: prefix + random suffix, random passwords, in files whose path is a setting (DEC-032, owner 2A) | **Met** | `CredentialFile` unit tests (format, 0600/0700, atomic, no contents in messages); integration `GeneratedCredentialsTests`, `GeneratedAdministratorTests`; smoke: names, modes, another uid cannot read |
| 3 | Administrator and migration credentials in a volume only db-init mounts; the runtime one read-only in the app; the app reads its password from a file (DEC-032) | **Met** | Compose; smoke: mounts inspected; app unit tests (reads the file, refuses a missing or invalid one, no secret in logs) |
| 4 | First run: sa creates the generated administrator, then sa is disabled; `AllowSaAdmin` removed (DEC-032) | **Met** | Integration: administrator created (sysadmin), `sa` disabled, database owned by `sa`, later runs never try `sa`; smoke: `sa` sign-in refused (account disabled) |
| 5 | A tested recovery before disabling `sa`; measure `mssql-conf set-sa-password` (DEC-032) | **Met** | Measured first (DEC-034); `docs/ops/database-recovery.md`; smoke: administrator volume removed, db-init stops with the recovery steps, `mssql-conf` re-enables `sa`, db-init resets the same administrator and disables `sa`, app ready; integration: each `bootstrap-db` path, including a tampered marker |
| 6 | The SQL container's healthcheck uses a CONNECT-only login (DEC-032, owner 4A) | **Met** | `health-login.sh`, `healthcheck.sh`; measured restart and lockout paths; smoke: the login holds only CONNECT SQL, no user, `healthcheck.sh` never uses `sa` |
| 7 | The external-SQL file keeps the operator's administrator; generation in Mode Create (DEC-032); Mode Existing writes the files from settings (owner 5A) | **Met** | Integration: generation, settings written to files, Mode Existing; smoke: Mode Existing app reads its file, Mode Create generates both logins |
| 8 | Upgrade of a stack with passwords in `.env` (owner 1A) | **Met** | Integration in both collections: names kept, passwords replaced, `sa` disabled |
| 9 | No automatic rotation (owner 3A) | **Met** | Lost file → new password, same login; settings → rotated and file rewritten (integration, smoke) |
| 10 | #18: securables owned by `public` refused, measured first | **Met** | DEC-035; integration: schema and role cases refused, measurement test; fresh installation passes |
| 11 | #19: the owner's effective CONTROL SERVER, honouring DENY | **Met** | DEC-035; integration: role grant + direct DENY refused, then accepted after REVOKE; nested-role owner still accepted |
| 12 | Rule 7 (audit records, `/api/v1` parity, endpoint policies) | **Not applicable** | No application mutation, UI function or endpoint was added |
| 13 | CI green | **Not executed** | Runs on the PR |
