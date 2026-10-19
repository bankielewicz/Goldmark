# Decisions log

Running log of technical decisions, with rationale. Newest entries are added
at the end. `docs/PRD.md` is stored as received (draft v0.2). Where an entry
here narrows or deviates from the PRD, this file wins.

**Approval state (2026-10-19):** slice 1 is merged (PR #1); slice 2 is in review. The owner approved
the **amended MVP build plan** (DEC-021): PRD §11 slices 2–24 plus slices 2b
and 5b, in order, one PR each. The PRD text stays a draft (DEC-001); the build
plan in DEC-021 and `docs/HANDOFF.md` is the working plan. PRD assumption A1
(stack) is confirmed. A2 (licensing, DCO) is pending. The other assumptions remain open.

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
- **Removed and deferred to slice 2b** (DEC-021; issues labelled `slice-2`): the
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
| Audit of pre-existing / adopted principals and shared-instance conditions: `guest` access in other databases (#2), wildcard linked-server and remote mappings (#3), ownership and lower-scope grants, `guest`/`public` grants in system databases, password-policy/expiry/deny state (#4) | 2b |
| `docker-compose.external-sql.yml`; US-01 "use an existing SQL Server" | 2b |
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
  1. connects as the migration login and takes an exclusive application lock
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
     role's (DEC-025); otherwise exit 3.
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
  the audit log. A user-level `GRANT UPDATE` on the audit log is correctly
  accepted (the role's `DENY` still wins); removing the `DENY` is refused
  (tested). `bootstrap-db`'s own check now allows the `goldmark_runtime`
  membership.
- **Tested against SQL Server 2025** (21 denied statements, integration and
  smoke): DDL in `cmdb` and `dbo`, `DROP`, `ALTER`, index creation,
  `SYSTEM_VERSIONING = OFF`, DML on history tables, `UPDATE`/`DELETE`/
  `TRUNCATE`/`MERGE … DELETE`/`IDENTITY_INSERT` on the audit log, writes to
  the journal, `GRANT`, role and user changes. Each denied statement is
  followed by a check that nothing changed.
- **Out of scope (2b, #4):** auditing grants on other individual objects and
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
- **Coverage** (59 tests): fresh install, idempotent re-run, concurrent
  `migrate`, temporal tables, ownership by `dbo`, default tenant, 21 denied
  runtime statements, allowed DML with history, both logins' rights,
  refusal/adoption of an unmanaged migration login, swapped purposes, server
  role on the migrator, password rotation, newer/diverged schema, extra
  runtime grants, readiness healthy/older/newer with both versions logged.

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
155 in-process tests, 59 integration tests, 63 smoke checks, all passing.

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
| 10 | Runtime rights via `goldmark_runtime`, explicit DENY on AuditLog (owner, DEC-025) | **Met** | `0002_runtime_role.sql`; `migrate` verifies effective rights (integration: 6 tamper cases) |
| 11 | Integration tests: same SQL image digest as Compose, one container per collection (owner, DEC-027) | **Met** | Fixture reads `x-sql-image` from Compose; `SqlServerTests` collection |
| 12 | Migration immutability: hash manifest, CI fails on a changed script (owner, DEC-028) | **Met (local)**; CI on the PR | `db/check-migrations.test.sh` (12 cases), CI job `migrations` |
| 13 | #7: `Database:Port` validated; `dbo`, `guest`, `sys`, `INFORMATION_SCHEMA` rejected | **Met** | Unit tests (also `public`, `goldmark_runtime`, `sa`, for both logins) |
| 14 | CI green | **Pending** | The PR's checks |
