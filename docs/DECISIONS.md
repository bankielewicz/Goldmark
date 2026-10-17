# Decisions log

Running log of technical decisions, with rationale. Newest entries are added
at the end. `docs/PRD.md` is stored as received (draft v0.2). Where an entry
here narrows or deviates from the PRD, this file wins.

**Approval state (2026-10-16):** the product owner approved **slice 1 scope only**
(DEC-001), not the whole PRD. PRD assumption A1 (stack) is confirmed for this
scaffold. A2 (licensing, DCO) is pending. The other assumptions remain open.

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
  instead of an exception during Kestrel setup (review finding).
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
- **Pending:** a schema-version compatibility check joins readiness in slice 2,
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
- **Not added (deferred until used):** Dapper, DbUp, Testcontainers.

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
  - a banned package in `package.json` or anywhere in the lockfile;
  - a lockfile entry not resolved from the registry with an integrity hash;
  - a missing or weakened pnpm/npm setting, or any policy exclusion;
  - an unapproved build script;
  - install lifecycle scripts in `package.json`.

  It has 25 tests.
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
- **Everything is inspected before anything is changed** (review findings).
  No database, login or user is created or altered on a run that refuses.
  An existing login must be an enabled SQL login with nothing beyond the
  baseline measured for a fresh login on SQL Server 2025. Otherwise bootstrap
  **refuses (exit 3) and changes nothing**. The inspection runs as the
  administrator, in the Goldmark database or in master while that database
  does not exist yet, and covers:
  - server and database role memberships;
  - explicit grants at every scope (server, endpoints, logins, database,
    schemas, objects, users), so for example `CONTROL ON SCHEMA` and
    `IMPERSONATE ON LOGIN` are caught;
  - ownership of every catalog that records an owner: databases, endpoints,
    server principals, audits and event notifications on the server; objects,
    schemas, principals, types, XML schema collections, assemblies,
    certificates, asymmetric and symmetric keys, scoped credentials, full-text
    catalogs and stoplists, search property lists, Service Broker objects,
    event notifications and external languages, libraries and models in the
    database;
  - linked-server, remote-login and credential mappings of the login;
  - mappings into other databases;
  - non-default grants to `public`;
  - a database user with the login's name but another SID (for example a
    restored database). It is refused, never silently remapped.

  The baseline is the measured set for a fresh login: on the server, the
  login's `CONNECT SQL`, and public's `VIEW ANY DATABASE` and `CONNECT` on
  system endpoints; in the database, the user's `CONNECT`, and public's two
  `VIEW ANY COLUMN … KEY DEFINITION` grants and `SELECT` on system objects.
  DENYs are ignored because they only remove rights.
- **An existing login's password is never changed** unless
  `GOLDMARK_DB_ROTATE_PASSWORD=true` (`Goldmark:Bootstrap:RotatePassword`) is
  set for that run, and never for a login that fails inspection. A name that
  collides with another account is therefore left alone. If the configured
  password already matches, nothing is changed.
- After provisioning, the login is checked twice and bootstrap refuses on any
  excess: from the catalog (above), and as itself through its effective
  permissions (`sys.login_token`, `sys.user_token`, `fn_my_permissions`). The
  first fixed-role check passed a login holding `securityadmin` and
  `db_securityadmin` (review finding).
- Refuse rather than revoke: such rights were granted outside Goldmark.
  Slice 2 adds the runtime DML grants to the baseline.
- The connect timeout must be 1–120 s (0 means "forever" to SqlClient), and
  the whole wait is cancelled at `WaitSeconds` plus one connect timeout, so
  `db-init` cannot hang the stack.
- A pinned SQL certificate (`ServerCertificatePath`) must parse as PEM or
  DER at startup, not merely exist (review finding).
- The migration login and DML grants arrive with slice 2.
- The `sa` password exists only in the `sqlserver` and `db-init` containers.
  The app receives only the runtime login.
- Secrets reach containers as environment variables from `deploy/.env`
  (documented `chmod 600`). Docker secrets were considered and deferred: the
  SQL Server image takes its password from the environment anyway.
- The app refuses to start (exit 2, setting names only) without host, user or
  password, with `sa` as the runtime user, or with encryption other than
  `Strict`/`Mandatory`.

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
- **Risk for slice 5:** Fluent UI v9 injects styles at runtime (Griffel). It
  must be verified against `style-src 'self'` before adoption. If it needs a
  nonce or `unsafe-inline`, that comes back for a decision, not a silent change.

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
| Migration login, DML grants, readiness schema check | 2 |
| `docker-compose.external-sql.yml`; US-01 "use an existing SQL Server" | 2 |
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
| 18 | Readiness checks schema compatibility | **Deferred to slice 2** (instructed) | DEC-008 |
| 19 | HTTPS on by default; per-installation certificate persisted outside the image; replacement documented (US-01) | **Met (local)** | Smoke: served = volume; survives re-creation; key 0600; TLS < 1.2 refused |
| 20 | Certificate trust and HSTS documented separately | **Met** | `docs/ops/tls-certificates.md`, `docs/ops/hsts.md` |
| 21 | CSP, Trusted Types and ESLint controls; incompatibilities reported | **Met**; no incompatibility found | Browser test; ESLint config; Fluent UI risk noted (DEC-017) |
| 22 | Fast in-process HTTP tests **and** a real Compose smoke test with health checks and bounded waits | **Met** | 80 xUnit tests; `compose-smoke.sh` |
| 23 | Least-privilege workflow permissions | **Met** (static) | `ci.yml` |
| 24 | `SECURITY.md` | **Met** | `SECURITY.md` |
| 25 | US-01: DbUp migrations on start with a separate migration login; runtime login DML-only | **Unmet in slice 1** (slice 2) | DEC-015 |
| 26 | US-01: option to use an existing SQL Server | **Unmet in slice 1** (slice 2) | DEC-019 |
