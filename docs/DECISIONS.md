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
| DEC-038 | Slice 3: authentication decisions (audit, invitations, QR, Dapper, keys, defaults, setup token) | Security |
| DEC-039 | Design system approved | Front end |
| DEC-040 | Slice 3: sign-in on Identity's plug-in points, screens approved, password list | Security |
| DEC-041 | Slice 4: audit retention by a bounded procedure, a 365-day floor, exports audited | Security |
| DEC-042 | Slice 4a: password recovery, dual control for links that need none of the person's factors | Security |
| DEC-043 | Slice 4b: the Windows service, its installer, secrets, logs and rotation | Deployment |

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
  that is not the runtime login (slice 4). Amended by DEC-041: the runtime login may start the
  bounded purge procedure, but never decides what it deletes.

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

## DEC-038 Slice 3: authentication decisions

- **Decision (owner, 2026-10-20, asked one at a time; options and examples in the session).**
  1. **Audit records now (1A).** Every slice-3 mutation and sign-in event writes a row to
     `cmdb.AuditLog` and is tested for it (HANDOFF rule 7): setup completed, sign-in succeeded or
     failed, account locked, TOTP or recovery-code failure, recovery code used, sign-out,
     invitation created, redeemed or revoked, roles changed, account locked, unlocked, disabled or
     enabled. Slice 4 adds the audit screen, filters, CSV export and the retention path.
  2. **New users get an invitation link plus a separate code (2A; hardened after a side agent noted
     that a link alone works like a password).** An admin creates the user; Goldmark shows, once, a
     link and a short code. The admin sends the link (chat, email) and tells the person the code
     another way (phone, in person). The link's token is random (at least 128 bits), carried in the
     URL fragment so it never reaches a server log or a referrer, stored only as a SHA-256 hash,
     single use, and expires after **24 hours** (a setting). The page asks for the code first; 5 wrong
     codes invalidate the link (audited). Only then does the person set their own password, enrol
     TOTP and get recovery codes; the admin never knows the password. **Invitations offer Editor,
     Viewer or Auditor only**: Admin is added on screen 17 after the person has signed in (audited),
     so a leaked invitation can never yield an Admin account. Unused links can be revoked. Goldmark
     sends no email.
  3. **QR code: `Net.Codecrete.QrCodeGenerator` 3.2.1 (3A, revised).** MIT, no dependencies, published
     2026-09-07, a .NET port of Project Nayuki's QR generator. QRCoder was the first choice but failed
     the owner's condition: on .NET 6 and later it depends on `System.Drawing.Common` 6.0.0. The server
     renders the QR code as SVG, served only to the user being enrolled, `Cache-Control: no-store`;
     the CSP forbids `data:` images. New NuGet dependency, approved by the owner.
  4. **Dapper now, with the NFR-SEC-3 build gate (4B).** Moved from slice 5: the user, role, session
     and audit queries use Dapper, and a build rule fails on string interpolation or concatenation
     inside Dapper calls, with a test proving the rule fails. Dapper is in the PRD (A1); its version is
     the newest that is at least 7 days old.
  5. **Key ring as files (5A).** ASP.NET Core Data Protection keys (cookies, antiforgery, encrypted
     TOTP secrets) live in a directory from settings. In Docker that is a volume only the app mounts,
     with `0700`/`0600` forced like the credential files (DEC-034); no certificate wrapping, because on
     Linux its key would sit beside them. The Windows service (4b) uses DPAPI. Backups (24) must
     include it: losing it signs everyone out and invalidates TOTP secrets; recovery codes, stored
     hashed, still sign users in to enrol again.
  6. **Defaults (6A), all settings.** Passwords 12–128 characters with no composition rules (NIST SP
     800-63B), checked against a bundled list of common passwords (ASVS L2; the list's source and
     licence are recorded when it is added); lockout after 5 failures for 15 minutes (US-03); sessions
     end after 30 minutes idle and 12 hours at most.
  7. **The setup token is logged, as the one exception (7A).** While no admin exists, each start
     generates a new random token (at least 128 bits), logs it once at Warning under its own event ID,
     and keeps only its hash in memory. It works once and is useless after setup completes or the
     process restarts. It is the only secret Goldmark ever logs; tests assert it is logged exactly
     once and that `TestSecrets` still never appear.
  8. **Admins can reset a user's two-factor (owner, A).** For a user who lost both the authenticator and
     the recovery codes: a confirmed, audited action on screen 17 clears their TOTP secret and recovery
     codes, signs them out everywhere, and issues a link and code exactly like an invitation's (same
     24-hour expiry, 5-try limit, revocable) to set up again; their password is kept. Nobody can reset
     their own two-factor this way.
  9. **Self-service two-factor page (owner, A; small scope addition).** From the account menu, a signed-in
     user can generate new recovery codes or move to a new authenticator, each confirmed with their
     password and a current code. Audited.
  10. **Collector is never given to a person (owner, A).** Screen 17 offers Admin, Editor, Viewer and
     Auditor; the API refuses Collector on user accounts. Collector belongs to service identities with
     API tokens (slices 8 and 13).
- **Scope change (4B, 9):** HANDOFF §1 moves Dapper and the NFR-SEC-3 gate from slice 5 to slice 3, and
  adds the self-service two-factor page to slice 3.

## DEC-039 Design system approved

- **Decision (owner, 2026-10-20, "approve as is").** The design-system canvas
  (https://claude.ai/artifact/7xy5Ar6Ag7yKpAHYnXMdxg; files in `docs/design/system/`) is the base for
  every screen handoff. It answers the design-system handoff (HANDOFF rule 4).
- **What it fixes:** Fluent's `createLightTheme` / `createDarkTheme` with one muted-gold brand ramp
  (`BrandVariants` 10–160, shade 80 `#8a5a00`) and Fluent's own token mapping; Fluent neutrals; status
  as `Badge appearance="tint"` with icon and word: danger (Down), **severe, dark orange (Degraded:
  yellow warning reads as the brand)**, success (OK), informative (Not seen); MessageBar with the
  `colorStatus*` tokens; Fluent's type ramp, spacing, radii and per-component shadows; a Compact
  density preference (Fluent size `small`); the app shell (NavDrawer inline at 1100 px and wider,
  icon rail and overlay below; top bar with search, an As-of slot for slice 7, theme toggle, account
  menu; skip link; Breadcrumb + h1 + Toolbar); the table, form, panel, dialog, feedback, state,
  secret, badge and sign-in patterns; the accessibility rules.
- **Measured:** every token value and contrast ratio was read from the installed themes and Fluent's
  component source (a side agent caught that the first draft used remembered values). Lowest text
  pair 4.8:1 (dark brand text and links). Slice 3 adds a unit test that recomputes the ratios from
  the themes and fails below 4.5:1 for text and 3:1 for UI parts.
- **Mockups:** where `docs/design/screens/` differs (colours, dark primary buttons, focus ring),
  this system wins; each screen is redrawn in its slice's handoff.

## DEC-040 Slice 3: sign-in on Identity's plug-in points, screens approved, password list

- **Decision (owner, 2026-10-20, asked after "approved" for the backend; options and examples in the
  session).**
  1. **Identity, with its gaps patched at its plug-in points (A).** ASP.NET Core Identity's own source
     (release/10.0) showed three gaps against ASVS L2: its authenticator provider accepts two time steps
     either side and the same code again; `SignInManager.SignOutAsync` only clears cookies; a wrong
     recovery code is not counted toward lockout. Goldmark keeps Identity's user manager, password
     hasher, lockout counter and security stamps on its own Dapper tables, replaces the authenticator
     token provider, keeps sessions on the server through the cookie handler's session store, and
     writes the two sign-in steps itself (`SignInManager` is not used).
  2. **The slice-3 screens canvas is approved as is (A):** version 29 of
     https://claude.ai/artifact/Fmq9Gc7pAKZ1XMHc2zVy8k, copied to `docs/design/screens/slice-3/` after
     checking every file against the published canvas. The UI must match it.
  3. **Revoking an unused invitation removes the account it created (A):** the user name is free again;
     the audit log keeps the invitation and its revocation.
  4. **After a two-factor reset, the right password points to the link (A):** "Two-factor was reset for
     this account. Open the link your administrator gave you." A wrong password still gets the generic
     answer.
  5. **The common-password list is Pwdb-Public's top 1M, 12 to 128 characters (A)** (DEC-038 item 6):
     ignis-sec/Pwdb-Public at commit `9b53917431ed5cd57cd6cbec8c3a19cf9a5cd8dd`,
     `wordlists/ignis-1M.txt` (SHA-256 `e9a88f67…a18c9fc8`), MIT, © 2020 Ata Hakçıl; 29,946 entries after
     lower-casing and dropping lines that are not valid UTF-8 (ASVS 5.0 6.2.4 asks for at least the
     3,000 most common passwords the policy allows; the 3,000th such entry is at rank 190,708). The list
     was compiled from leaked passwords; the licence covers the compilation. Rebuilt reproducibly by
     `tools/common-passwords/build-common-passwords.mjs`; the licence ships beside the app
     (`common-passwords.LICENSE.txt`). Alternatives offered: the top 10M (702k entries), xato's list
     (licence conflicted), NCSC's top 100k (1,212 entries, too few).
  6. **The UI step is approved** (owner, "APPROVED, start the UI", after the backend's verification).
  7. **Slice 3 routes with a small in-repo router, no new package (B).** React Router does not conflict
     with the host (the SPA fallback hands every non-file GET path to the shell; `/api` and `/health`
     stay server-side), but its 8.4+ releases depend on `@remix-run/route-pattern`, which the npm policy
     bans by the `@remix-run/` prefix; 8.5.0 is also under the 7-day age until 2026-10-26. Offered: 8.4.0
     with an exact exception to the ban, the in-repo router, or 8.3.1. Slice 5 decides React Router
     when it needs the deep links (US-53). Client routes must not end in a segment with a dot (the
     fallback is `:nonfile`).
  8. **Automated axe checks: `@axe-core/playwright` 4.13.0 (A)** (owner; NFR-A11Y-3): published
     2026-08-11, MPL-2.0, its only dependency axe-core 4.13.x, no install scripts; dev-only (the smoke
     test's Playwright run), never in the bundle or the image. `web/e2e/auth.spec.ts` signs in as the
     smoke test's administrator and checks sign-in, setup, an invalid link, the dashboard, Users &
     roles, the add-user panel and the two-factor page in light and dark: no serious or critical axe
     violation, no CSP or Trusted Types violation. The theme contrast test (DEC-039's promise) is
     `web/tests/contrast.test.mjs`: 51 checks of the pairs the screens use, recomputed from the brand
     ramp with Fluent's own theme functions; it caught failed-rule text in the password checklist
     using a danger token at 4.16:1 in dark, now the palette red the boards use (5.20:1).
  9. **Forgotten passwords: deferred, recorded (A)** (owner, 2026-10-20, review of PR #24: "A. Defer,
     record it"). Slice 3 has no password recovery: a two-factor reset keeps the password, and the
     PRD asks for none. Issue #25 holds both mechanisms (an administrator's reset through the link and
     separate code; an operator-only container command for when the only administrator is locked out);
     the owner places it in the plan. Until then the README advises keeping two administrators.
- **How the backend implements DEC-038 and the above** (reading PRD §8.1 "ASP.NET Core Identity"):
  - **NFR-SEC-3 gate without a new package.** Every Dapper call goes through `Data/DbSession.cs`, whose
    SQL arguments are `[ConstantExpected]`; the SDK's CA1857 is an error under warnings-as-errors, so
    interpolated, concatenated or variable SQL fails to build (constants, including constant
    interpolation, pass). `SqlGateTests` keep Dapper out of every other file and CA1857 unsuppressed;
    `tests/sql-gate/check-sql-gate.sh` (CI, .NET job) builds a fixture against the real `DbSession` and
    expects CA1857 on exactly its five marked violations.
  - **Passwords and lockout:** Identity's hasher (PBKDF2-HMAC-SHA512, 128-bit salt, 100,000 iterations:
    its v3 defaults, read from its source); 12 to 128 characters, no composition rules, the bundled
    list. Lockout after 5 failures for 15 minutes (settings), counting wrong passwords, codes and
    recovery codes. `GoldmarkUserManager` overrides `AccessFailedAsync` and `ResetAccessFailedCountAsync`
    with one SQL statement each (Codex, PR #24): Identity's own read the row, changed the count and wrote
    it back under the concurrency stamp, so of concurrent failures only one was counted. A failure never
    moves a lockout earlier, so an administrator's lock made meanwhile stands. Unknown, disabled and
    not-yet-set-up accounts fail exactly like a wrong password, including the lockout: their failures
    are counted in memory, keyed by a hash of the normalized name (what someone typed may be a password,
    so it is never stored or logged), with a dummy hash computed so the time taken matches. Like an
    account's count, they have no idle expiry (the cache runs on the app's clock); a restart, or a full
    cache (10,000 names), can forget them, which is accepted. An automatic lockout does not end existing
    sessions (anyone can cause one); an administrator's lock does.
  - **Codes:** `TotpTokenProvider` (Identity's `AuthenticatorTokenProvider` slot): RFC 6238, HMAC-SHA1
    (what authenticator apps require; CA5350 is suppressed on that one method with this reason), the
    current step or one either side, and a step is never accepted twice (`LastTotpTimeStep`, reset to
    the verified step when a new key is installed). Recovery codes: Identity's generator
    (`XXXXX-XXXXX`, no look-alike characters), ten, stored as PBKDF2 hashes, each works once.
  - **Sessions:** `SessionTicketStore` (the cookie handler's `ITicketStore`) over `cmdb.UserSession`:
    the cookie holds a 256-bit key, the row its SHA-256 hash and the ticket encrypted with Data
    Protection; 30 minutes idle and 12 hours at most, enforced on the server. Each request re-checks
    the person (exists, enabled, not locked by an administrator, two-factor set up, same security stamp)
    and reads roles fresh. Each sign-in gets a new session key and ends the one the browser presented
    (ASVS 5.0 V7.2.4; it also defeats session fixation): the cookie handler would otherwise renew the
    presented key, so the two endpoints that sign in (`StartsNewSession`) never continue it. A new
    authenticator keeps the current session and ends the others (V7.4.3). The cookie handler always
    calls the store's overloads that pass the request; they use the request's `DbSession`, so a session
    row is written or deleted in the endpoint's transaction (Codex, PR #24): a sign-in's session commits
    with the redeemed recovery code, the completed setup or link and their audit rows, and sign-out's
    audit row only with the deleted session.
  - **First-run setup:** a 160-bit token, logged once per start at Warning (event 3001, "Setup token")
    while setup is not complete, held only as a hash; the steps are carried in the encrypted enrolment
    cookie and nothing is written until the authenticator is proven, then one transaction creates the
    administrator, the role, the recovery codes and the `cmdb.Installation` row. The runtime login
    cannot change or delete that row (`DENY`, checked by `migrate`), so setup can never run again; a
    second browser finishing at the same time gets 409.
  - **Links (invitation, two-factor reset):** a 256-bit token in the URL fragment, stored as SHA-256; a
    6-character code from the same 26-character alphabet, stored as SHA-256 of token and code, so it
    cannot be tried without the token; 5 wrong codes revoke the link; 24 hours; one open link per
    person. Both limits are settings (`LinkCodeAttempts`, `LinkExpiryHours`): an issued link carries
    them, and the screens state the configured values (Codex, PR #24).
  - **Deny by default:** the fallback policy's requirement (`DenyUnclassifiedEndpoints`) refuses every
    application endpoint without a policy and lets a request that matched none through to its 404 (or
    routing's 405). The pipeline calls `UseRouting`, `UseRateLimiter`, `UseAuthentication` and
    `UseAuthorization` explicitly, after the security headers and static files.
  - **Anti-forgery:** ASP.NET Core antiforgery on every state-changing `/api/v1` request (header
    `X-Goldmark-Antiforgery`), tokens bound to the session's principal or to no one. Because signing in
    ends the presented session, a token issued for it is refused once there; the SPA refetches the
    token and retries once.
  - **Rate limit:** 30 requests per minute per client address on the endpoints that take a credential
    (`Goldmark:Auth:RequestsPerMinute`). The address is the TCP peer: forwarded headers are not
    trusted, so behind a reverse proxy every client shares the proxy's budget (raise the setting
    there; trusting a proxy's headers is a later, explicit setting).
  - **Data Protection keys** (DEC-038 item 5): `PrivateKeyRingRepository` at `Goldmark:DataProtection:
    KeyDirectory` (`/var/lib/goldmark/keys`, the `goldmark-keys` volume only the app mounts), directory
    0700, files 0600, written atomically. Keys are not encrypted at rest, by DEC-038 5A, so Data
    Protection logs "No XML encryptor configured" at Warning when it creates a key (every 90 days); that
    warning is expected. An authenticator key the ring can no longer decrypt (lost, or restored without
    the database's matching volume) makes a code fail like a wrong one, with Warning event 3003; a
    sign-in with a recovery code then starts setting up a new authenticator at once, so one recovery
    code is enough (Codex, PR #24). Back up and restore `goldmark-keys` together with the database.
  - **Audit actions:** `setup.completed`, `setup.token-rejected`, `auth.sign-in.succeeded`,
    `auth.sign-in.failed` (with the reason), `auth.account.locked-out`, `auth.recovery-code.used`,
    `auth.signed-out`, `user.invitation.created`/`reissued`/`revoked`/`redeemed`, `user.link.code-failed`,
    `user.link.invalidated`, `user.roles.changed` (before and after), `user.locked`/`unlocked`/
    `disabled`/`enabled`, `user.two-factor.reset`/`reset-completed`, `account.confirmation.failed`,
    `account.recovery-codes.regenerated`, `account.authenticator.changed`. Each is written in the same
    transaction as the change; none holds a secret.
  - **API answers** are Problem Details with a stable `code` the SPA maps to the boards' messages.
- **Dependencies:** Dapper 2.1.89 (published 2026-09-23, no dependencies on net10.0) and
  Net.Codecrete.QrCodeGenerator 3.2.1, both in DEC-038. The integration tests now also reference
  `Microsoft.AspNetCore.Mvc.Testing`, already pinned for the in-process tests. No other package.

## DEC-041 Slice 4: audit retention by a bounded procedure, a 365-day floor, exports audited

- **Decision (owner, 2026-10-20, asked in the HANDOFF rule-6 format).**
  1. **Old entries are deleted by a backend job through a bounded procedure (A).** The owner first
     asked: "Is the data stored in the database? If so, would a sql job that runs a stored procedure
     daily be adequate? Or should there be a purge/maintenance windows service? … Sounds like this
     should be some sort of backend process." After the answers below, they chose "A. Backend job +
     bounded procedure (Recommended)".
     - **What is built.** The operator sets the retention period. `migrate` writes it to a one-row
       table the runtime login can read but cannot change: `DENY INSERT, UPDATE, DELETE`, the
       `cmdb.Installation` pattern, checked by `migrate`. A migration creates a `dbo`-owned procedure
       in `cmdb` with static SQL only and **no parameters**: the cutoff comes only from that table.
       It re-checks the 365-day floor in its own code and deletes older entries in batches (to keep
       the transaction log small on Express). Each batch commits together with a `System` audit entry
       giving the cutoff and that batch's count, so no deletion commits without its entry; a run that
       deletes nothing writes nothing. The procedure manages its own transactions, so it is never
       called inside another one. `goldmark_runtime` gets `EXECUTE` on that one procedure. A hosted
       service in `Goldmark.Api` runs it once the database is ready (the readiness check: reachable,
       at the expected schema version) and then daily, on `TimeProvider`; it is idempotent. No
       `Goldmark.Jobs` project yet.
     - **What it amends.** DEC-025 asked for "a delete path that is not the runtime login". The
       runtime login now **starts** the purge but never **decides** what it deletes: a stolen app
       credential can only run, early, the deletion the operator already configured. Its rights on
       the table are unchanged: no `DELETE`, no `UPDATE`.
     - **Why not a SQL Agent job.** Express, which the bundled stack runs, has no SQL Server Agent.
       Microsoft's "Editions and supported features of SQL Server 2025" lists "SQL Server Agent:
       Enterprise Yes, Standard Yes, Express No". On other editions, Goldmark could not create the
       job: jobs live in `msdb`, and the migration login is `db_owner` of the Goldmark database only
       (DEC-024). On a full SQL Server a DBA may still schedule the procedure as an extra caller.
     - **Why not a purge Windows service.** Rule 8 forbids platform-only pieces, so it would need a
       Docker twin, and slice 4b (DEC-036) already turns the app itself into the Windows service. The
       same job runs unchanged in Docker, the 4b Windows service and on an external SQL Server.
     - **Rejected.** An operator-side command run with the migration login (narrowest, but nothing
       is deleted unless someone runs or schedules it). A maintenance process with its own delete-only
       login (more moving parts, and that login could delete today's entries).
     - **To be measured, not assumed (the DEC-025 habit).** The procedure's `DELETE` succeeds through
       ownership chaining while a direct `DELETE` as the runtime login is still refused. A period
       under the floor deletes nothing. `migrate`'s runtime check fails in both directions: if
       `EXECUTE` is granted on anything else or is missing on the procedure, and if the policy
       table's `DENY` is missing.
  2. **Retention has a floor of 365 days (A, "A. Floor at 365 days (Recommended)"; NFR-LOG-3).** The
     setting `Goldmark:Audit:RetentionDays` defaults to 365. A value under 365 is refused with exit 2,
     naming the setting but not its value; `0` keeps entries indefinitely. `migrate` reads the setting
     and writes it to the policy table, so it belongs in `db-init`'s environment. A change to it is
     itself audited (`System`, before and after). The audit screen shows the configured period.
  3. **Exports are audited, reads are not (A, "A. Exports only (Recommended)").** Each CSV export
     writes an entry with the filters and the row count. Browsing and filtering on screen write
     nothing. The export confirmation says the export is recorded.
  4. **"From where" is the connecting address; trusted proxies are deferred (A, "A. Defer, document,
     issue (Recommended)").** An entry's IP address is the TCP peer, as for the sign-in rate limit
     (DEC-040): X-Forwarded-For is not trusted, because any client can send it. Behind a reverse proxy,
     every entry therefore shows the proxy's address. The README and screen 16 say so. A
     trusted-proxy setting is #31, for the owner to place in the plan.
  5. **Screen 16 is approved as is (owner, 2026-10-21, "approved").** This is version 13 of
     https://claude.ai/artifact/8oEjLkZAiNJ14qTT3ssJjs, copied to `docs/design/screens/slice-4/`. The UI
     must match it. Changes from the handoff are on its Notes board: expandable rows use Fluent's
     Table rather than DataGrid; the IP address header has an info Tooltip; one sample entry was added.
  6. **PR #32 round 1's open items (owner, 2026-10-21, each "A … (Recommended)").**
     - Both CSV exports (audit log and users) start with the UTF-8 byte-order mark, so Excel on Windows
       reads non-ASCII names correctly ("A. Fix now, both exports"); in round 2.
     - The people list and the count read through the whole log; deferred to #33, for slice 24's
       performance work ("A. Defer to an issue").
     - An action the catalog doesn't list shows by its code, counts as Succeeded and can't be filtered.
       This is recorded only ("A. Record it, no code"): readiness refuses a database migrated by a newer
       build (DEC-023), and a test checks that every action this build writes is in the catalog.
  7. **Findings after both review rounds (`/code-review` on PR #32; owner, 2026-10-21, each "A …
     (Recommended)").**
     - Fixed now, as one change ("A. Fix the bundle now"):
       - a custom-range year outside 2000 to 9998 is a field message, never a request;
       - any unexpected failure ends in an error state with Retry;
       - each expand button names its entry ("Show details for entry 48213: Roles changed at
         16:03:40"), a change from the board's label so that screen readers can tell entries apart;
       - the IP prefix pattern ends with `\z`;
       - one download helper;
       - `AuditQuery.None` moved into the test.
     - Deferred:
       - the retention period checked before `bootstrap-db` changes anything, #34;
       - strict ISO 8601 times for API callers, #35, slice 8;
       - end-to-end cancellation of abandoned requests, added to #33 for slice 24. Rewriting the page's
         tested request counter was declined.
- **Consequences, noted when decided (side agent, 2026-10-20).**
  - **Changing retention means running `db-init` (`migrate`) again.** The value belongs to `db-init`'s
    environment; set on the app, it does nothing.
  - **The Settings screen (20, P1) cannot edit audit retention** as long as item 1 holds: it can only
    show the period. Editing it in the UI would need the app to write `cmdb.AuditRetention`, which is
    exactly what item 1 prevents. Changing that goes back to the owner.

## DEC-042 Slice 4a: password recovery, dual control for links that need none of the person's factors

- **Decision (owner, 2026-10-21, asked in the HANDOFF rule-6 format; each answer quoted).**
  1. **Placements.** Issue #25 (forgotten passwords) becomes its own slice, **4a**, before 4b ("A. Own
     slice, before 4b (Recommended)"), approved to start in a fresh session ("A. APPROVED: #25 first
     (Recommended)"). #31 (trusted proxies) goes to slice 24 ("A. Slice 24 (Recommended)"). #34 (the
     retention period checked before `bootstrap-db`) goes to slice 4b ("A. Slice 4b (Recommended)").
     The placements are commented on #25, #31 and #34 when the slice PR opens ("A. Comment on all
     three (Recommended)").
  2. **One "Reset password…" action, with "Also reset two-factor" for a person who lost both ("A. One
     action, a checkbox (Recommended)").** Otherwise a person who forgot their password and lost their
     authenticator could finish neither reset: the two-factor reset asks for the current password, the
     password reset for the authenticator. With the box ticked the link runs the invitation's steps,
     proof by the link and its separate code alone, the same level as enrolment (ASVS 5.0 6.4.4).
  3. **A password reset ends with the person's own authenticator ("A. Yes, then signed in
     (Recommended)").** New password, then a 6-digit code or a recovery code; nothing changes until it
     checks out, and wrong codes count toward lockout as at sign-in. ASVS 5.0 6.4.3: a password reset
     must not bypass two-factor.
  4. **The old password stops working at once ("A. Stops working at once (Recommended)").** Sessions
     end; the old password then fails like any wrong password, and those failures go through the
     unknown-user throttle, so the account does not lock before the person finishes the reset.
  5. **Change password for a signed-in person, in this slice ("A. Yes, in slice 4a (Recommended)";
     scope change, ASVS 5.0 6.2.2 and 6.2.3).** Current password, new password under the policy; other
     sessions end, this one continues.
  6. **The way back in when no Admin can sign in: emergency-Admin guidance plus a startup setting, not a
     command ("A + startup setting (Recommended)").** First built as a typed command, `recover-admin`
     (from #25's second criterion and the owner's answers "A. Password and two-factor (Recommended)" and
     "A. Path and code (Recommended)"); the owner then said "commands are not meant to be run on a server
     such as recover-admin ingrid.m. when was this designed?" and asked how professional applications,
     Active Directory and Microsoft Entra handle it. Researched (vendor documentation, 2026-10-21): every
     self-hosted product falls back to server access (Jira and Confluence's recovery mode is a startup
     property; GitLab, Grafana and Keycloak have commands); Entra recommends two or more emergency access
     accounts. So: the README prescribes Microsoft's pattern adapted to Goldmark (a second Admin not
     tied to a person, its password and recovery codes kept in a safe, its use visible in the audit log,
     checked every 90 days), and the last resort is the setup-token pattern #25 named: the operator sets
     `GOLDMARK_RECOVER_ADMIN=<username>` and restarts; Goldmark logs a one-time password-and-two-factor
     link and code for that enabled Admin, once per start, as it logs the setup token (DEC-038 item 7),
     audits it as System, and warns in the log while the setting is set. An administrator's lock is kept
     (the owner's chosen text said a lockout from failed attempts ends; a side agent noted the command
     had lifted every lock). Settled in the slice, open to veto: the link changes nothing on the account
     until it is used, so a setting left in place only prints a new link at each start (the previous one
     stops working) rather than resetting the Admin again; using it signs the Admin out everywhere and
     sets a new password and authenticator. The recovery link becomes the second secret Goldmark ever
     logs, after the setup token. Then a side agent noted that logs reach more people than the server's
     operators (`docker compose logs`, central log tools, saved files) and outlive the reset, so the
     owner was asked again (2026-10-21) and chose "A. Operator sets it in .env (Recommended)" and "A. 1
     hour, ended at a clean restart (Recommended)": the operator sets `GOLDMARK_RECOVER_CODE` (12 to 64
     letters and digits) with the username, the log carries the link's path only and never the code,
     the link works for one hour whatever the link lifetime setting says, and a start without the
     setting ends every unused recovery link (audited "Link stopped working", reason
     `recovery-setting-removed`). Five wrong codes still end it. Settled in the slice, open to veto (a
     side agent's note): issuing the link is audited "Recovery link issued", not "Password reset", since
     nothing changes until it is used, as a waiting combined reset is "Approval requested" (item 8); its
     use is "Password reset completed", Link issued by "server-setting". From the review of PR #38
     (Codex, reproduced): every start, not only one without the setting, ends every unused recovery
     link, whoever it was for (audited, reason `restart`), so a link lasts at most until the next start
     and a refused start ends the previous link too. The code reaches the app as an environment
     variable (readable with `docker inspect` by whoever holds the host, as the owner's choice implies);
     reading the log alone is still not enough.
  7. **The password-and-two-factor reset is never for an Admin ("A. Not for an Admin (Recommended)").**
     A link and code alone never yield an Admin session, as for invitations (DEC-038 item 2): Remove
     Admin first; add it again after their first sign-in. The recovery setting (item 6) is the one,
     deliberate, exception: whoever can set it and its code already holds the server, with the runtime
     login and the key ring.
  8. **Dual control for invitations and the password-and-two-factor reset ("B. Dual control now,
     both").** Asked after a side agent noted that the Admin who issues such a link holds both values
     and could finish it as the person; the owner: "how can we harden this as it doesn't seem secure?
     let's consult with /advisor for guidance/best practices". The Admin who asks gets the link only;
     another enabled Admin (not the one who asked, not the person) approves and gets the code, made at
     approval. With no such Admin the one who asks gets both, audited "only-admin" (the fallback the
     owner accepted with option B, as is the audited bypass of first removing another Admin's role).
     Not for the password-only and two-factor-only resets, which each still need one of the person's
     own factors. Settled on the boards and approved with them: a combined reset changes nothing on
     the account until approved; a request waits up to the link lifetime, and the link's own lifetime
     counts from the approval; the approving Admin in the samples is Morgan Okafor. An invitation that
     waits still writes "Invitation created" (detail: approval requested), so a filter on it finds every
     invitation; a combined reset writes "Approval requested", since nothing changed yet. From the
     review of PR #38 (Codex, reproduced; the owner, 2026-10-22, asked in the rule-6 format: "C. Refuse,
     say why (Recommended)"): an Admin who can't sign in to approve (password reset unfinished, no
     authenticator, or locked by an administrator; never the automatic lockout, which failed sign-ins in
     their name could trigger) leaves the request refused, naming who and why, rather than waiting for
     an approval that can't come or widening the fallback. Approve records the approval before it
     changes the account, so a request that ended meanwhile changes nothing. A waiting invitation can't
     be replaced by a new link either. A password-only reset needs the person's authenticator (its link
     ends with it), and Admin can't be added while a password reset is unfinished (item 7).
  9. **Waiting requests are visible ("A. Nav count + time left (Recommended)"; raised by a side agent:
     Goldmark sends no email).** Every Admin sees a count on the "Users & roles" nav item on every page;
     the requester's row shows the time left and, once it lapses, "approval request ended"; the README
     says to tell the approving Admin yourself.
  10. **Row 4a's "Done when" as proposed ("A. As proposed (Recommended)").**
  11. **The boards are approved (owner, 2026-10-21, "approved.").** Version 28 (`1792617023-b865`) of
      https://claude.ai/artifact/Bzpjzj8maiwQGfPJ31KCy3, copied to `docs/design/screens/slice-4a/`. The
      approvals count and the requester's time-left wording (item 9) came after; they get a board
      before their UI.
  12. **Windows and LDAP sign-in are filed, not built.** Asked whether Goldmark supports NT
      authentication: not in the PRD (A3: local accounts with TOTP; OIDC, slice 25). The owner's
      decisions are in #36 (Windows sign-in, Kerberos only, its own slice after 25) and #37 (LDAP
      sign-in over LDAPS, its own slice after #36).
- **How it is built.**
  - `cmdb.UserAccount.PasswordResetAt` (migration 0005) marks a reset password; the hash stays until
    the person chooses a new one. A link has a purpose (`PasswordReset`, `CombinedReset` added), and a
    link asked for under dual control has a token and no code (`ApprovalRequired`, `ApprovedAt`,
    `ApprovedBy`) until approved.
  - **Code hashes, two schemes.** Scheme 1 (0003) hashes the code with the token, which is never
    stored; it serves every code made when the token is known. A code made at approval cannot be, so
    scheme 2 is HMAC-SHA-256 under a random key of that link's own, stored encrypted with Data
    Protection: a copy of the database alone still gives no way to test codes. A shared key table was
    rejected: written once, it would break dual control for good if the key ring were lost. Losing the
    key ring fails approved, unused links like a wrong code, as DEC-038 item 5 accepts for
    authenticator keys.
  - **Guards, in the API whatever the UI shows.** A two-factor reset is refused while a password reset
    or a request waits. Admin is not added while a combined reset is open, and an approval is refused
    if the person became an Admin meanwhile. Nobody approves their own request or one about
    themselves.
  - The recovery setting is read by a hosted service once the database is ready; it issues the link
    through the same link store (scheme 1, no issuing account, the operator's code, one hour) and never
    resets the account itself. A link with no issuing account is a recovery link and only that: other
    links always name their issuer, and the redeem step checks a recovery link's code against the
    operator code's shape, every other code against the generated one's.
  - The nav's approvals count (item 9) is read when a page opens and after Users & roles changes
    something, never on a timer: every request counts as activity, so polling would keep an idle
    Admin signed in past `Goldmark:Auth:SessionIdleMinutes` (a side agent's note).

## DEC-043 Slice 4b: the Windows service, its installer, secrets, logs and rotation

- **Decision (owner, 2026-10-22, asked in the HANDOFF rule-6 format; each answer quoted).** Slice 4b
  was approved to start ("approved"). Its choices, left open by DEC-030, DEC-034 and DEC-036:
  1. **Service account: "A. gMSA, domain account fallback (Recommended)".** A gMSA where possible; a
     domain user is accepted (its password typed once into Windows' service settings, never stored by
     Goldmark); the virtual account `NT SERVICE\Goldmark` only when SQL Server is on the same server;
     never LocalSystem or NetworkService. Rejected: B, gMSA only (no workgroup installs); C, any account,
     because LocalSystem and NetworkService reach a remote SQL Server as the computer account
     (`CORP\APP01$`), which every other such service on that server shares: the known broadening
     DEC-037's rule avoids.
  2. **Migrations under Windows authentication: "A. The installing person, as themselves
     (Recommended)".** The installer runs `bootstrap-db` and `migrate` under the operator's own Windows
     login (with CONTROL SERVER: item 13 corrects the option's "or a `db_owner`"); no migration secret is stored
     on the server; the service account never gets DDL. The same reach Docker's `db-init` has with the
     administrator's file. Rejected: B, a second gMSA run through a scheduled task (more AD setup per
     install); C, a generated SQL login (needs mixed-mode authentication, a stored password).
  3. **Installer: "A. PowerShell wizard in a zip (Recommended)".** `Install-Goldmark.ps1` asks in the
     terminal, calls the program's own commands and creates the service with its recovery actions and
     ACLs; upgrading is running it again; an MSI can wrap it later. Its prompts are a text spec in the PR,
     not a Claude Design board (rule 3). The package `Microsoft.Extensions.Hosting.WindowsServices`
     (Microsoft; not in the shared framework) was approved with this answer. Rejected: B, an MSI with
     WiX (dialogs need a design handoff; WiX v6 and later binaries are under the Open Source Maintenance
     Fee EULA, which v7 enforces at build, a licensing question while Goldmark's own is open, DEC-001);
     C, a graphical setup program.
  4. **Logs: "A. JSON files, Administrators only (Recommended)".** The same JSON as on stdout, one file
     per day in `%ProgramData%\Goldmark\logs`, readable by the service account, SYSTEM and
     Administrators only. A start that fails on its configuration also writes the setting names to the
     Application log. Asked as a security choice: the log carries the setup token (event 3001) and the
     recovery link's path (3004), and older Microsoft documentation gives Interactive Users read access to
     the Application log; measured on Windows 11 build 26300 (2026-10-22, `wevtutil gl Application`): its
     channel access still grants `(A;;0x3;;;IU)`, read and write for Interactive Users. No new package: `Serilog.Sinks.File` is already
     in the lock file through `Serilog.AspNetCore`. Rejected: B, the Event Log only (new package; the
     two secrets readable by anyone signed in on the server); C, both.
  5. **Secrets at rest: "A. ACLs + DPAPI to the service account (Recommended)".** The folders for the
     sign-in keys, the TLS key, the credential files, the logs and the settings allow only the service
     account, SYSTEM and Administrators, with inheritance off, and a start refuses a wider one. The key
     ring and the runtime login's credential file are encrypted with DPAPI-NG to the service account's
     SID (Data Protection's own, in the framework). The administrator's and the migration login's
     passwords are never stored on Windows. That a restore on another server running as the same gMSA
     still reads them is inferred from Microsoft's documentation; neither this machine nor CI has a domain,
     so it is part of the gMSA check by hand. The consequence (a side agent's note): the service account
     is part of the backup. A key ring restored under another account, after a change of account (a
     domain user to a gMSA), or with the virtual account on a rebuilt server, can't be read, and everyone
     signs in with a recovery code and sets up two-factor again; the ops guide's backup and restore steps
     say so. Rejected: B, ACLs only
     (a copy off the server stays readable); C, DPAPI machine scope (any account on the server decrypts
     what it can read; a package for the credential file).
  6. **#6 moves into 4b: "A. Yes, close #6 in 4b (Recommended)".** A scope change: the ACL code of item 5
     protects the generated TLS key on Windows dev hosts too, and the Windows CI job checks it. HANDOFF
     moves #6 from slice 24 to 4b.
  7. **`rotate-db-credentials`: "A. All generated logins, then restart (Recommended)".** For SQL-login
     mode (Windows authentication has no passwords; a gMSA's rotates itself). Runtime, migration and the
     bundled administrator get new passwords in one run, each file written before its login changes, so
     the next run finishes an interrupted one (DEC-034). It prints that Goldmark must be restarted; the
     Windows wizard restarts the service itself. The SQL container's healthcheck login keeps its own
     repair script. Rejected: B, one login at a time; C, rotation without a restart.
  8. **Windows CI: "A. Build, tests, install smoke (Recommended)".** A windows-2025 runner builds, runs
     the in-process tests, installs SQL Server 2025 Express, installs the service with Windows
     authentication as `NT SERVICE\Goldmark` and checks `/health/ready`, the folder ACLs and the log
     file, installs again with a SQL login and rotates, and uninstalls. GitHub's Windows runners can't run
     Linux containers, so the Testcontainers suite stays on Linux; CI has no domain, so the gMSA path is
     checked by hand following the ops guide. Rejected: B, build and tests only; C, the integration suite
     on Windows too (a second database backend in its fixture).
  9. **Row 4b's "Done when": "A. Approve as written (Recommended)"**, recorded in HANDOFF §1. Settled in
     the slice, open to veto: the zip carries a self-contained build, so the server needs no .NET
     install; a rotation is audited as System.
  10. **The credential file when the installer can't encrypt to the service account: "A. Service
     encrypts it at start (Recommended)".** Measured on a Windows 11 host outside a domain (2026-10-22):
     DPAPI-NG `SID=` refuses to encrypt ("Encryption failed"), for the current account and for
     `NT SERVICE\Goldmark`'s SID; `LOCAL=user` and classic DPAPI work. So, in SQL-login mode, with a
     domain service account `bootstrap-db` encrypts the runtime file to that account (DPAPI-NG);
     otherwise it writes the file with the owner-only ACL (as on Linux) and the service replaces it with a
     copy encrypted to itself at its next start, which the wizard triggers at once. The plain copy exists
     only between an install or a rotation and that start. Rejected: B, SQL logins only with a domain
     account (CI has no domain, so that path would be checked by hand only); C, ACLs only for credential
     files. Settled in the slice, open to veto: the service writes the key ring itself, so it uses DPAPI-NG
     to a domain account and classic DPAPI to the virtual account; either way only the service account
     reads it.
  11. **Measured on the host with a probe service as `NT SERVICE\GoldmarkProbe` (2026-10-22, owner's
     permission "A. Yes, a UAC prompt per script (Recommended)"; everything removed afterwards):**
     - With restart-on-failure actions set, a configuration error found after the host started
       (`ValidateOnStart`) looks like a crash (event 7031) and restarts every 2 seconds without end (9
       starts in 14 s); one found before the host exists leaves the start hanging 30 s ("did not respond",
       7009/7000, exit code 0). So a refused start reports itself to Windows as stopped with an error, which
       failure actions don't restart (Done when item 5).
     - An administrator's `EXECUTE AS LOGIN = N'NT SERVICE\GoldmarkProbe'` showed only the login and
       `public`; the service's own token also held its local Windows group and that group's
       `VIEW SERVER STATE`. Impersonation misses rights granted through Windows groups, so under Windows
       authentication the service's own check at each start is the one that counts (settled, open to veto:
       told the owner 2026-10-22); `bootstrap-db`'s check by impersonation is an early warning only.
     - SQL Server gives a Windows login `CONTROL`, `ALTER`, `IMPERSONATE` and `VIEW DEFINITION` on each Windows
       group login in its own token, the same implicit rights it has on itself (measured: the owner's login on
       itself on the host; `NT SERVICE\Goldmark` on `BUILTIN\Users`, which a fresh SQL Server 2025 Express has,
       in CI on 2026-10-22, where the service refused its first start). They can't be revoked, so the service's
       check skips those group logins as securables, as it skips its own login; what such a group is granted is
       still refused (smoke phase 2). Owner, 2026-10-22: "a" (A. skip only the automatic rights on group logins
       the service belongs to), after Microsoft's guidance: group membership is its "preferred strategy" for
       access, and Express's `BUILTIN\Users` login can be safely removed. Not covered: a Windows group mapped as a
       user in Goldmark's own database, where the database-level check still refuses (unmeasured).
     - The service's check at each start also covers every other database its own token can enter (Codex on #39):
       an administrator's impersonation doesn't see a user that one of the account's Windows groups has there.
       Owner, 2026-10-22: "A. Refuse; acceptable if via group (Recommended)": a user for one of its groups (such as
       `NT AUTHORITY\Authenticated Users` in another application's database) is the finding
       `group-database-access`, which an operator can accept by ID like guest access; a user for the account itself
       stays `database-access`, never acceptable. The check runs at every start, so a later grant to a broad group
       stops Goldmark at its next restart until it is accepted or reverted.
     - The installer copies an operator's HTTPS certificate and key into `tls\`, and the pinned SQL Server certificate
       into `settings\` (Codex on #39: the originals may sit where others can read them). Owner, 2026-10-22: "A. Copy,
       document renewal (Recommended)": a renewal replaces the copies (installer again with the paths, or copy and
       restart from a renewal script); `docs/ops/windows-service.md` "Renewing certificates".
     - Codex round 2 on #39 (no owner decision needed, each a tightening): the installer trusts what is already in a
       data folder only if nobody but Administrators, SYSTEM, the installing account and (outside `settings`) the
       service's account may change it; the program folder lets only SYSTEM and Administrators change it and may not
       overlap the data folder; `bootstrap-db` and `rotate-db-credentials` share one lock (`goldmark:credentials`).
     - After the two review rounds, the owner's fix-now answers (2026-10-22): "A. Fix now (Recommended)" for
       `Rotate-GoldmarkCredentials.ps1` checking the settings as the installer does before asking for the SQL
       administrator's password; "Option a. Post mvp will expand upon admin/settings which can include configuration
       for database authentication." for refusing a change of `-Authentication` on an upgrade before anything changes
       (#41 covers both principal changes until then); "A. Fix both now (Recommended)" for full install and data paths
       and resetting an existing operator key copy's permissions.
     - The virtual account replaced a plain credential file an administrator wrote with a DPAPI-encrypted
       copy and read it back (item 10's fallback); DPAPI-NG `SID=` fails for it as well.
  12. **SQL Server 2025's own defaults on Windows: "A. Measure Windows defaults, add them (Recommended)"**
     (owner, 2026-10-22). The principal audit's baseline (DEC-031) was measured on the Linux image; on the owner's
     Windows SQL Server 2025 Developer, `public` also holds `SELECT` on `master.sys.dm_pal_cpu_stats`,
     `dm_pal_disk_stats`, `dm_pal_vm_stats`, `dm_pal_net_stats` and `msdb.dbo.external_libraries_installed`, so
     `bootstrap-db` refused before changing anything. The CI job measures a fresh SQL Server 2025 Express on
     Windows; exactly those grants join the baseline as Windows entries, and anything else is still refused or
     accepted by ID. Rejected: B, every Windows install accepts them by ID; C, a built-in exception for the
     `dm_pal_*` views without measuring. Measured by CI on PR #39 (2026-10-22): a fresh Express has the four
     `dm_pal_*` grants and `EXECUTE` for `public` on 20 SQL Server 2025 procedures in `master` (warehouse,
     managed delta table and clone metadata), not the `msdb` grant, which comes with the owner's Machine
     Learning Services; that installation accepts it by ID.
  13. **Who runs the installer: "A. Correct the record: CONTROL SERVER (Recommended)"** (owner, 2026-10-22).
     Item 2's option said "a sysadmin, or a db_owner after a DBA's Mode Existing setup"; in the build,
     `bootstrap-db` audits the instance before every change (DEC-031), which needs CONTROL SERVER, and the
     installer runs it on every install and upgrade. So the installing person needs CONTROL SERVER: a DBA runs
     the installer, or grants it for the run. `migrate` alone still runs as a `db_owner`. Rejected: B, an
     upgrade switch that runs `migrate` only (upgrades would no longer re-audit the instance).

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

## Slice 3 acceptance mapping

Same status vocabulary as slice 1. Local runs on the owner's WSL machine: 336 in-process tests, 178
integration tests (SQL Server 2025), Compose smoke 94 unique checks with Playwright 7/7 inside
(Chrome), web 48 policy tests and 56 dist and contrast tests, plus lint, typecheck and format.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | Fluent UI v9 verified under the served CSP and Trusted Types first (HANDOFF §1) | **Met** | DEC-037: per-response style nonce; real-server qualification in Chrome, Edge and Firefox; `fluent.spec.ts` in every smoke run |
| 2 | Design-system and screen handoffs before UI code (rules 3, 4) | **Met** | DEC-039 (design system), DEC-040 (screens approved, `docs/design/screens/slice-3/`) |
| 3 | US-02: one-time setup page protected by a setup token printed to the container log | **Met** | Event 3001 logged once per start while setup is not complete; integration `Setup_creates_the_first_administrator…`, `The_setup_token_is_logged_exactly_once…`; smoke: `docker compose logs app \| grep "Setup token"`, setup through the API |
| 4 | US-02: the setup page is disabled permanently after use | **Met** | `cmdb.Installation` row with `DENY UPDATE, DELETE` for the runtime login (checked by `migrate`); integration: 409 after setup and after a restart, no new token; smoke: 409, no token after the app is re-created |
| 5 | US-02: the admin enrols TOTP before reaching any other page | **Met** | Nothing is written until the authenticator code is verified; integration: no account row before verification |
| 6 | US-03: sign-in with username, password and TOTP | **Met** | Integration `Sign_in_needs_the_password_and_then_a_code_that_works_once`; Playwright signs in with a TOTP code |
| 7 | US-03: Identity's default hasher (PBKDF2) | **Met** | `UserManager` with Identity's `PasswordHasher` (v3: PBKDF2-HMAC-SHA512, 100,000 iterations, read from its source); integration: no password, code or key stored in clear |
| 8 | US-03: lockout after 5 failures for 15 minutes, configurable | **Met** | `Goldmark:Auth:LockoutThreshold`, `LockoutMinutes`; integration `Five_failures_lock_an_account_and_an_unknown_name_alike` (passwords, codes and recovery codes; unknown names alike) |
| 9 | US-03: recovery codes issued once and stored hashed | **Met** | Ten codes, PBKDF2 hashes, each works once; integration (setup, sign-in, regenerate) |
| 10 | US-04: roles Admin, Editor, Viewer, Collector (service-only), Auditor | **Met** | `cmdb.Role`; Collector refused by the API and by a CHECK constraint; integration `Invitations_never_grant_Admin_or_Collector…` |
| 11 | US-04 / M6: every endpoint declares a policy, endpoints without one fail closed, an automated test enumerates them | **Met** | `RequireAccess` / `AllowAnonymousByDesign`; fallback `DenyUnclassifiedEndpoints`; `EndpointInventoryTests` (inventory, 401 without credentials, 400 without anti-forgery, fallback denies everyone); `AccountAndAccessTests` (403 for a Viewer on every Admin endpoint, 401 for a password-only sign-in, 400 without the token) |
| 12 | DEC-038: audit rows for every slice-3 mutation and sign-in event | **Met** | `AuditTrail` in the same transaction; integration asserts each action and that no row holds a secret |
| 13 | DEC-038: invitation links with a separate code, 24 h, 5 tries, revocable, never Admin | **Met** | `UsersAndLinksTests` (code first, tries left, revoked after five, expiry, reissue, revoke removes the account) |
| 14 | DEC-038: admin two-factor reset; self-service two-factor page | **Met** | `Locking_disabling_and_resetting_two_factor_end_the_persons_sessions`; `AccountAndAccessTests` (new codes, new authenticator keeps this session and ends the others) |
| 15 | DEC-038: Dapper and the NFR-SEC-3 build gate | **Met** | `DbSession` with `[ConstantExpected]` (CA1857); `SqlGateTests`; `tests/sql-gate/check-sql-gate.sh` in CI |
| 16 | DEC-038: Data Protection keys at a settings path, 0700/0600 | **Met** | `PrivateKeyRingRepository`; unit test; smoke: modes, another uid cannot read, only the app mounts `goldmark-keys` |
| 17 | DEC-038: QR code as SVG, no-store, only to the person enrolling | **Met** | `QrCodeImage` (Net.Codecrete.QrCodeGenerator 3.2.1); unit and integration tests |
| 18 | Server-side sessions: sign-out ends them, idle 30 min, 12 h, new key at every sign-in (owner, DEC-040) | **Met** | `SessionTicketStore`; integration: copied cookie refused after sign-out, idle and absolute expiry, planted session key ended at sign-in; smoke: copied cookie refused |
| 19 | NFR-SEC-2: anti-forgery on cookie-authenticated mutations; SameSite; CSP | **Met** | `__Host-` cookies SameSite=Strict; `ApiAntiforgery`; M6 tests; smoke: POST without the header is 400 |
| 20 | Rule 7: every UI function is available through `/api/v1` | **Met** | The SPA calls only `/api/v1` (`web/src/api.ts`); `docs/security/endpoint-access.md` lists every endpoint |
| 21 | NFR-A11Y-1/3: WCAG 2.2 AA, axe checks in Playwright | **Met for slice 3's screens** | `web/e2e/auth.spec.ts`: sign-in, setup, invalid link, dashboard, Users & roles, add-user panel, two-factor page, light and dark; `web/tests/contrast.test.mjs` (51 pairs) |
| 22 | CI green | **Not executed** | Runs on the PR |

## Slice 4 acceptance mapping

Same status vocabulary as slice 1. Local runs on the owner's WSL machine:
- **2026-10-21:** 371 in-process tests; web 48 policy tests and 56 dist and contrast tests, lint,
  typecheck and format; Compose smoke, 97 unique checks with Playwright 8/8 inside (Chrome).
- **2026-10-20:** the full integration suite, 208 tests (SQL Server 2025), on the backend commit.
  Only `AuditLogApiTests` ran again on 2026-10-21, after the export's entry-count header was added.

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | US-07: see who changed what, when and from where, across sign-ins and admin actions | **Met** for what Goldmark records today (CI, relationship and token events come with their slices, rule 7) | Screen 16 (`AuditPage.tsx`) on `GET /api/v1/audit`; names resolved, removed accounts shown by their recorded user name; `AuditLogApiTests` |
| 2 | US-07: append-only, no UPDATE/DELETE permission for the app login | **Met**, with the purge as DEC-041 amends DEC-025 | `DENY UPDATE, DELETE` unchanged; `RuntimePermissionTests` (direct DELETE, dynamic SQL, ALTER PROCEDURE, the retention row all refused); `AuditRetentionTests`: the procedure deletes only past the period, never under 365 days |
| 3 | US-07: filterable | **Met** | Time, actor, action, person affected, IP prefix, outcome; every value validated and passed as a parameter (NFR-SEC-3); `AuditLogTests`, `AuditLogApiTests`; filters in the page address |
| 4 | US-07: CSV export | **Met** | `POST /api/v1/audit/export`, at most 100,000 entries, formula cells neutralized, audited in the same transaction (DEC-041 item 3); integration and Playwright |
| 5 | NFR-LOG-3: separate from operational logs, retained at least one year, configurable | **Met** | `cmdb.AuditLog`, not stdout; `Goldmark:Audit:RetentionDays` (365 to 36500, or 0), refused otherwise with exit 2; `AuditRetentionJob` daily; smoke: the runtime login can't change the period and can run the purge |
| 6 | HANDOFF slice 4: a retention path that is not the runtime login | **Met as amended** (owner, DEC-041 item 1) | The runtime login starts the bounded procedure; the period lives where only `migrate` writes; `migrate` and the principal audit refuse any other grant (`MigrateRefusalTests`, `PrincipalAuditTests`) |
| 7 | HANDOFF slice 4: audit events for slice 3's sign-ins and admin actions | **Met** (written since slice 3) | `AuditCatalog` labels every action; a test checks that every `AuditActions` constant is in it |
| 8 | Rule 7: new mutations write audit records, tested | **Met** | `audit.exported`, `audit.retention.purged`, `audit.retention.changed`, each in the change's own transaction; integration tests |
| 9 | Rule 7: every UI function is available through `/api/v1` | **Met** | The page calls only `/api/v1/audit*`; `docs/security/endpoint-access.md` |
| 10 | Rule 7 / M6: every new endpoint declares a policy and is in the enumeration test | **Met** | `AuditReader` (Admin or Auditor); `EndpointInventoryTests`; `AccountAndAccessTests` (403 for a Viewer, 401 password-only, 400 without anti-forgery) |
| 11 | Rule 3: design handoff before UI code | **Met** | Handoff 2026-10-20; boards approved 2026-10-21 (DEC-041 item 5, `docs/design/screens/slice-4/`) |
| 12 | NFR-A11Y-1/3: WCAG 2.2 AA, axe in Playwright | **Met for screen 16** | `web/e2e/auth.spec.ts` "slice 4 screen": the page, an expanded entry, a filter and the export dialog, light and dark |
| 13 | CI green | **Not executed** | Runs on the PR |

## Slice 4a acceptance mapping

Same status vocabulary as slice 1. Local runs on the owner's WSL machine, 2026-10-21 and 2026-10-22:
393 in-process tests; the full integration suite, 224 tests (SQL Server 2025), with review round 1's fixes; web 48
policy tests and 56 dist and contrast tests, lint, typecheck and format; Compose smoke, 97 unique checks with Playwright 10/10 inside (Chrome).

| # | Criterion (source) | Status | Evidence |
|---|---|---|---|
| 1 | #25: an administrator resets another person's password from Users & roles, through the invitation's link and separate code, signs them out everywhere, audited; the person chooses a new password and two-factor stays | **Met**, with the owner's additions: the person's own authenticator or a recovery code confirms it (DEC-042 item 3), and "Also reset two-factor" for a lost authenticator (item 2, never for an Admin, item 7) | `POST /api/v1/users/{id}/password-reset`; `/password-reset` link page; `PasswordRecoveryTests` (old password refused without locking, sessions ended, recovery-code confirm, atomicity with the audit row); Playwright "slice 4a screens" end to end |
| 2 | #25: an operator-only container command issues such a link for the last administrator, audited, no secret logged but the link | **Met as amended** (owner, DEC-042 item 6): no command, "A + startup setting", then "A. Operator sets it in .env" and "A. 1 hour, ended at a clean restart" | `GOLDMARK_RECOVER_ADMIN` + `GOLDMARK_RECOVER_CODE`; `RecoverAdminService` logs the path only (event 3004), audited "Recovery link issued"; refusals 3005/3006; a clean start ends the link; README's emergency-Admin guidance; `PasswordRecoveryTests` (code never logged or audited, 1-hour lifetime, a second start replaces the link, a clean start ends it, refusals) |
| 3 | #25: every new endpoint declares an access policy and joins the M6 enumeration test | **Met** | `EndpointInventoryTests`, `docs/security/endpoint-access.md`: password reset, request approve/decline/cancel, `GET /users/approvals`, `/enrolment/confirm`, `POST /account/password` |
| 4 | Rule 3 / #25: screens through a design handoff first | **Met** | Handoff `tmp/screen-handoff-slice4a.md`; boards approved 2026-10-21 (version 28, "approved.") and the addendum (version 29, "the addendum boards have been approved"); `docs/design/screens/slice-4a/` |
| 5 | Row 4a: the reset ends sessions and the old password at once | **Met** | `PasswordResetAt` (migration 0005); sign-in refuses it through the unknown-user throttle, so the account doesn't lock; `PasswordRecoveryTests` |
| 6 | Row 4a / owner: dual control for invitations and the password-and-two-factor reset (DEC-042 item 8) | **Met** | Requests (`ApprovalRequired`), approve/decline/cancel, the code made at approval under a per-link key (scheme 2); the single-Admin fallback audited "only-admin"; lapsed requests can be asked again; `PasswordRecoveryTests` |
| 7 | Owner: waiting requests are visible (DEC-042 item 9) | **Met** | The nav count (`GET /api/v1/users/approvals`, read when a page opens, never on a timer), the approvers' MessageBar, the requester's time left and "Ask again…"; Playwright (shaped answers: one Admin can't make such a request) |
| 8 | Owner / ASVS 5.0 6.2.2, 6.2.3: Change password for a signed-in person | **Met** | `POST /api/v1/account/password`, other sessions end; the account menu's dialog; `PasswordRecoveryTests` |
| 9 | Rule 7: new mutations write audit records, tested | **Met** | The new actions in `AuditCatalog` (catalog test); each flow's audit rows asserted in `PasswordRecoveryTests`; the audit page's words for the new details |
| 10 | Rule 7: every UI function is available through `/api/v1` | **Met** | The pages call only `/api/v1/users*`, `/links/*`, `/enrolment/*`, `/account/password` |
| 11 | NFR-A11Y-1/3: WCAG 2.2 AA, axe in Playwright | **Met for the slice's screens** | `web/e2e/auth.spec.ts` "slice 4a screens": sign-in caption, Change password, the link page (new password; authenticator, dark; done), the reset dialog (light and dark), the link panel, the link before approval, the nav count (dark, and the 1024 rail) |
| 12 | CI green | **Not executed** | Runs on the PR |

## Slice 4b acceptance mapping

Same status vocabulary as slice 1. Local runs, 2026-10-22: on the owner's WSL machine, 428 in-process tests
(10 Windows-only ones skipped) and the integration suite (232 tests, the Windows baseline one skipped); on the
owner's Windows 11 host, the same 428 in-process tests (2 POSIX-only ones skipped) and all 35 checks of
`tests/windows/Test-WindowsService.ps1` against SQL Server 2025 Developer with a test certificate; the Compose
smoke test, with a rotation check added, passes (Playwright 10/10).

| # | Criterion (row 4b, DEC-043) | Status | Evidence |
|---|---|---|---|
| 1 | The installer sets up the service on Windows Server against SQL Server 2025, Windows authentication or a SQL login; the installing person's login runs `bootstrap-db migrate`; gMSA, domain user, or `NT SERVICE\Goldmark` for a local SQL Server only, never LocalSystem or NetworkService; ready and the sign-in page; upgrade = run again; uninstall keeps the data | **Met on Windows 11**; Windows Server in CI (windows-2025); gMSA and domain user by hand | `deploy/windows/Install-Goldmark.ps1`; `Test-WindowsService.ps1` phases 1, 1b (an upgrade keeps the database and the sign-in keys), 3 and 4; the installer refuses LocalSystem, NetworkService and a virtual account for a remote server; it needs CONTROL SERVER (item 13) |
| 2 | Windows authentication: the service account's login in `goldmark_runtime`, no password, no DDL; the audit covers Windows logins and group rights; the password path only for SQL logins | **Met** | `bootstrap-db` creates `FROM WINDOWS` logins and checks them by impersonation; `RuntimeRightsCheck` checks as the service itself (a right through a Windows group is refused: smoke phase 2); `WindowsSettingsTests` |
| 3 | Owner-only folders, a start refuses a wider one; the key ring and a SQL login's password encrypted to the service account; administrator and migration passwords never stored on Windows; #6 | **Met**; DPAPI-NG to a gMSA by hand | `PrivateDirectory`, `CredentialProtector`, `KeyRingEncryption`; `WindowsProtectionTests` (run on Windows); smoke phase 1 (folders, encrypted keys) and 4 (the service encrypts its file) |
| 4 | JSON log files per day; a configuration failure in the Application log with setting names only; 3001 and 3004 never in the Event Log | **Met** | `LoggingSetup`; smoke phase 1 (the token is in the file, nothing in the Application log) and phase 2 (event 2 names `Goldmark:Https:Port`) |
| 5 | Restart after a crash; a configuration error stops without a loop | **Met** (measured) | DEC-043 item 11; `WindowsService.RefuseStart`; the rights refusal sets `ServiceBase.ExitCode`; smoke phase 2: both stop with exit code 13 and stay stopped past the restart delay |
| 6 | `rotate-db-credentials` on Docker and Windows: every generated SQL login, file before login, audited as System, then a restart; nothing under Windows authentication | **Met** | `RotateCredentialsCommand`; `CredentialRotationTests`, `GeneratedAdministratorTests`, `WindowsSettingsTests`; Compose smoke (the documented `docker compose run --rm db-init rotate-db-credentials`, then a restart); Windows smoke phase 4 (`Rotate-GoldmarkCredentials.ps1`) |
| 7 | The recovery setting in the settings file; the link's path in the log file | **Met** (configuration) | `Goldmark__SettingsFile` (`SettingsFile`); the installer keeps `Goldmark:Auth` on an upgrade; `docs/ops/windows-service.md`. The recovery flow itself is unchanged and tested in slice 4a |
| 8 | #34: `bootstrap-db` refuses an out-of-range retention period before connecting; a test proves nothing changes | **Met** | `AuditRetentionTests.Db_init_refuses_a_period_under_the_floor_before_bootstrap_db_changes_anything` (fails on the old code); `ContainerCommandsTests` |
| 9 | Windows CI (windows-2025): build, in-process tests, SQL Server 2025 Express, the install smoke, a SQL-login install with rotate, uninstall; the gMSA by hand | **Not executed** | Job `windows-service`; runs on the PR. Hand check: `docs/ops/windows-service.md` "Checking a gMSA by hand" |
| 10 | `docs/ops/windows-service.md`; the installer's prompts as a text spec; rule 7; rule 8 both ways | **Met** | The ops guide (parameters and prompts table); `database.credentials.rotated` in the audit catalog; `DefaultPaths`, Windows APIs behind `OperatingSystem.IsWindows()` |
| 11 | Rule 7 / M6: new endpoints | **Not applicable** | The slice adds no endpoint |
| 12 | CI green | **Not executed** | Runs on the PR |

## DEC-044 Slice 5: CIs, their types and attributes, search and detail

- **Decision (owner, 2026-10-23, asked in the HANDOFF rule-6 format; each answer quoted).** Slice 5
  was approved to start ("approved"). Its choices, left open by the slice plan (DEC-021) and the PRD:
  1. **Search toolbar later: "option 1 but document somehow whether github incident for enhancement or
     specification document that captures the roadmap, etc... consult with /advisor to ensure we don't
     incur any technical debt with any approach".** Option 1 was "All three later": screen 4's saved
     filters, column chooser and CSV export are not in slice 5. They go to slice 20, which next changes
     the search filters, as issues #45, #46 and #47 (label `enhancement`), placed in HANDOFF's slice 20
     row and §4 as #36 and #37 were. There is no separate roadmap document: HANDOFF §1 is the approved
     plan (DEC-021), and a second one would drift from it. Slice 5 leaves room for them without stubs:
     the search state lives in the page address, one query record serves the list (as `AuditQuery`
     serves the audit list and its export), and the list's columns are data-driven. Rejected: B, CSV
     export now; C, all three now.
  2. **Lifecycle: "A. Retire, Restore, hide (Recommended)".** New CIs are Active. Retire… (confirmed)
     and Restore; search hides retired CIs unless "Show retired" is on. Planned and Retiring, and the
     lifecycle filter, stay in slice 20 (US-12). Rejected: B, retire only, as the plan's row reads; C,
     the full lifecycle now.
  3. **Built-in attributes: "A. Only the PRD's ones (Recommended)".** The seed holds PRD §9.3's CI types
     and §9.4's relationship types, with only the attributes the PRD names (Server `platform`,
     Appliance `kind`, HypervisorHost `virtual`, NetworkDevice `role`, StorageArray `kind` and
     `family`). Each collector slice adds its own attributes by migration. Rejected: B, a starter set
     proposed now.
  4. **Custom attributes: "A. API in slice 5 (Recommended)".** An Admin adds a custom attribute (text,
     number, yes/no, date, a list of choices, a link to another CI) to a type through `/api/v1/types`,
     audited, and the CI form handles every data type. The screen is slice 5b's (screen 14). Rejected:
     B, all of it in slice 5b.
  5. **Open issues placed (the same day, before the approval).** "A. Keep as placed (Recommended)":
     #35 in slice 8; #5, #12's link check, #31 and #33 in slice 24; #36 in 25b; #37 in 25c; #27 when
     typescript-eslint supports TypeScript 7 (8.72.0 still requires TypeScript below 6.1); #20 waits for
     availability-group support, still unplaced (DEC-030). #40: "A. Slice 24 (Recommended)". #41's
     service-account change: "A. With post-MVP settings (Recommended)", the P1 settings slice, with the
     database-authentication change the owner placed there on 2026-10-22.
  6. **Search: "A. Names only (Recommended)".** The search box matches part of a CI's Name, any case,
     after the owner asked "when searching by name - what is the name of?" (each CI's Name field: a VM's
     name in vCenter, a server's computer name, an instance name, a team's name). Identifiers join
     search with slice 10. Rejected: B, names and text attribute values (reads every CI's JSON; the
     NFR-PERF-2 budget at risk).
  7. **The p95 proof: "A. On-demand CI job (Recommended)".** A generator seeds 25k synthetic CIs into
     SQL Server 2025 Express and a test measures the p95 of list, search and detail against NFR-PERF-2,
     failing over budget, in a job run on request (workflow_dispatch), not on every PR: the start of
     NFR-PERF-5's suite. Rejected: B, on every PR; C, measured once and recorded.
  8. **Custom attributes: "A. Add only (Recommended)".** Slice 5's API adds attributes; changing a
     label, the required flag or the choices, and removing one, come with screen 14 in slice 5b.
     Rejected: B, add and change; C, add, change and remove.
  9. **Defaults on the boards: "A. Keep all five (Recommended)".** Names need not be unique; a CI's
     type can't change; Edit isn't offered while a CI is retired; Restore needs no confirmation (Retire
     does); the "CI changed" audit entry lists the fields changed, not their values (slice 7's history
     shows them).
  10. **Screens approved: "approved".** The slice-5 canvas https://claude.ai/artifact/6juyvAr2JX1FtmkQZpyiz1
      (version 47, `1792759579-3d94`), copied to `docs/design/screens/slice-5/` with the details settled on the
      boards (its README). Then, after a side agent noted that the custom-attribute API needs a browser session
      until API tokens (slice 8): "A. Drop the Admin sentence (Recommended)": a type with no attributes shows
      only "This type has no attributes yet." (version 48, `1792760074-a7ec`, the copy kept). Rejected: B, point
      to the coming screen; C, keep the sentence. Slice 5b already follows slice 5 in the plan.
  11. **Saving a CI through the API: "A. Keep PUT, PATCH later (Recommended)".** After a side agent noted
      that a script which PUTs only the fields it knows erases the rest (for example a custom attribute
      added after the script was written), and that the screens send every field: `PUT /api/v1/cis/{id}`
      stays a full replace (HTTP's meaning; NetBox's PUT), and a partial save, `PATCH` with a JSON Merge
      Patch body (RFC 7396: a field left out stays, `null` clears it), comes with API tokens in slice 8
      (issue #48, label `enhancement`). Adding it then is additive under US-54; scripts can't call the API
      before slice 8. Rejected: B, add PATCH in slice 5 (a second save path before any script can use it);
      C, make PUT partial (not HTTP's meaning for PUT, and fixed in v1 for good).
  12. **Numbers keep every digit (Codex review round 1 on PR #49).** Codex found that the panel converted a
      typed number to a JavaScript number, which rounds beyond a double's precision (9007199254740993 was saved
      as ...992), and that editing another field rewrote a long stored number the same way. A first fix capped
      numbers at 15 significant digits; after a side agent noted it would refuse exact byte counts such as 1 PiB,
      the owner chose neither that nor "refuse only what a JavaScript number can't hold": "Please fix the precision
      loss without restricting the API's numeric range to JavaScript's limitations. [...] Keep numeric attributes
      numeric in the data model, and preserve supported .NET decimal values exactly through API responses,
      browser display/editing, and saves. Decimal strings for transport and form state are acceptable; users
      should not have to redefine byte counts as text attributes. Document any wire-format change and its
      compatibility implications. [...] Values outside the supported decimal range or precision should produce an
      explicit validation error rather than silent rounding." So: a number attribute is a .NET `decimal`, stored
      as a JSON number (SQL Server 2025's `json` keeps every digit, checked); `GET /api/v1/cis/{id}` and every
      CI response return it as a decimal string with its digits and scale as stored (`"9007199254740993"`,
      `"1.50"`); `POST` and `PUT` accept a JSON number or a decimal string; a value the decimal would round is
      `TooPrecise`, one beyond its range `NumberOutOfRange`, never rounded; the screens keep the text from
      response to form to request and never make it a JavaScript number. Compatibility: the change lands before
      any release and before API tokens (slice 8), so no client depends on numbers being JSON numbers; slice 8's
      OpenAPI document describes them as strings (`format: decimal`), and the request side stays lenient.
  13. **After review round 2 (a high-effort review of PR #49, 2026-10-23).** Eight findings, table in the PR.
      Round 2's shared lock on newly linked CIs (`fb2c84d`) let two saves that each newly link to the other's CI
      deadlock (one got a 500): "A. Retry once, then 412 (Recommended)", so a save SQL Server picks as a deadlock
      victim (1205) runs once more and a second loss answers 412 (reproduced: without the retry, a 500). A PUT
      without `attributes` read as "unset them all": "A. Require "attributes" (Recommended)" (400 `Required`; `{}`
      still clears every value, item 11 stands). "A. Fix all three now (Recommended)": a cursor without its sort's
      keys is `InvalidCursor`, adding an attribute answers `Location: /api/v1/types`, a CI can't link to itself
      (`LinkedCiSelf`); one more push, not a third review round. "A. No change, noted in DEC-044 (Recommended)":
      Retire and Restore resolving their services in one shared handler (a style choice), and an audit row's
      " ()" for a CI or type without a recorded name (unreachable while neither is deleted; the CSV's "Affected"
      header was approved with the boards, item 10).
- **Build notes (slice 5, not owner decisions; listed for review).**
  - A required attribute added to a type that already has CIs is enforced the next time each of them is
    saved; nothing is migrated.
  - `PUT /api/v1/cis/{id}` replaces the attribute values: an attribute not sent becomes unset (kept by the
    owner, item 11). A save that changes nothing writes no version and no audit entry. A link to another CI
    is checked (exists, not retired, the attribute's type) only when it changes, so retiring a CI never
    blocks saving the CIs that point at it.
  - Retire and Restore need If-Match like a change. When someone else changed the CI meanwhile, the page
    shows "This CI changed meanwhile" and loads their version (the boards' 412 dialog belongs to the panel,
    whose "Discard mine" and "Review their changes" don't fit a one-click action).
  - Screen 16's CSV column "Person affected" is "Affected" too, as the screen's column (item 10).
  - The search page ignores a `type` in a link that this Goldmark doesn't have, and the CI page corrects an
    unknown `?tab=` to the CI's own address (`docs/integration/deep-links.md`).
  - The filter panel's collapse button hides it at 1100 px and wider; the toolbar's Filters button brings it
    back.

## DEC-045 Slice 5b: the Types & attributes screen (14)

- **Status.** Approved by the owner on 2026-10-23 ("approved"); branch `slice/05b-types-admin` from
  `3915b0b`. HANDOFF row 5b: "Admin can manage CI types, attributes and relationship types through the UI"
  (screen 14; US-10, FR-CM-1, FR-CM-2, FR-IM-1, PRD §9.3 and §9.4).
- **The owner's answers (2026-10-23, AskUserQuestion; items 1 to 4 before the design handoff).**
  1. **Custom CI types: "A. Add; delete only if unused (Recommended)".** An Admin creates a type (name,
     group, icon). A custom type with no CIs can be deleted; built-in types stay locked. Its name, used by
     the API and links (`/search?type=`), never changes. Rejected: B, add only; C, no custom types.
  2. **Attributes: "A. Change; delete with confirmation (Recommended)".** Completes DEC-044 item 8: change
     the label, the required flag and the choices (a choice in use can't be removed); delete a custom
     attribute after typing its name, with the number of CIs that have a value shown first; its values
     leave the current CIs and each CI's history keeps them. Built-in attributes are locked; a kind never
     changes. Rejected: B, delete only unused ones; C, change only.
  3. **Promoted attributes: "A. Later, with attribute filters (Recommended)".** FR-CM-2's flag, computed
     column and index wait for the first slice that filters on attribute values (issue #53, unplaced):
     nothing filters on attributes yet, and the runtime login has no DDL rights, so promotion needs
     `migrate`. Rejected: B, the flag now, applied by `migrate`; C, a read-only column.
  4. **Relationship types: "A. View; add and edit custom (Recommended)".** All types listed with their
     descriptions; an Admin adds custom ones (name, from and to type or any, impact, description), changes
     a custom one's impact and description, and deletes an unused one. Built-in types are locked.
     Rejected: B, also change a built-in type's impact; C, view only until slice 6.
  5. **A custom type that has CIs: "A. Hide a custom type (Recommended)".** After a side agent noted that,
     with CIs never deleted (retired ones still count), a type's name fixed and a CI's type fixed (DEC-044 item
     9), a misspelled custom type ("Prnter") with one CI would stay in every list for good: an Admin can hide a
     custom type. A hidden type leaves the New CI type picker and Search's type filter and moves to a "Hidden"
     group on screen 14; its CIs keep working and stay in search results; no new CI of it can be created;
     links naming it still work; "Show again" undoes it; audited, in `/api/v1`. Rejected: B, a display label
     that can be renamed apart from the fixed name; C, no change (confirm the name at creation).
  6. **Deleting an attribute in use: "A. Rewrite now, say so (Recommended)".** After a side agent noted that
     removing the values updates each CI (with system versioning each gets a new `ValidFrom` and `ChangedBy`,
     moves to the top of Search's "newest changes first", and an edit open on one gets the 412): item 2
     stands. The values leave every CI in the one change, so each CI's history (and later the change feed)
     records the removal against the Admin at that moment; the typed confirmation adds "Each of those CIs will
     show as changed by you.". Rejected: B, the same with the boards' text unchanged; C, the values left
     stored and ignored (no CI touched; each value would drop out at that CI's next save, credited to whoever
     saved it, and would need a rule against reusing the name and versioned attribute definitions for US-42).
  7. **Screens approved: "design canvas has my approval".** The slice-5b canvas
     https://claude.ai/artifact/FcVKh7SEKGpTH9ev6WeSQK (version 37, `1792788486-fca8`); version 38
     (`1792789350-e8a1`) adds item 6's sentence. Copied to `docs/design/screens/slice-5b/` with the details
     settled on the boards (its README).
  8. **History of the definitions: "A. Version the definitions (Recommended)".** After a side agent noted that
     `cmdb.AttributeDef` keeps no history, so once an attribute is deleted or relabelled (or a choice removed,
     or a relationship type's impact changed) the CI history and "as of" views (US-42, US-43; slice 7) could no
     longer say what an old value meant: migration 0007 makes `cmdb.CiType`, `cmdb.AttributeDef` and
     `cmdb.RelationshipType` system-versioned, as `cmdb.Ci` and `cmdb.Relationship` are, so every earlier
     version and every deleted row stays in their history tables. A delete still does what items 2 and 6 say.
     An attribute's or a relationship type's version (its ETag) is its `ValidFrom`, as a CI's is. Rejected: B,
     deleted rows kept and marked (deletes only, not changes); C, no history for definitions.
  9. **The as-built differences from the boards (after a side agent noted they were chosen without asking).**
     Screen 14: "A. Keep all five (Recommended)": the choice list's Add button is named "Add choice" for screen
     readers, the Impact explanation is a Tooltip on an info button, "Show again" acts at once, "Any type" has the
     generic icon, a type's CI count links to Search with retired CIs shown. The audit filter: "A. Label
     "Affected" then (Recommended)" when it names a CI, a type or a relationship type (slice 5's "Person affected"
     otherwise). A CI's audit entry: "A. Keep it (Recommended)", "Show all entries about" as slice 5's board had
     it. From now on: "A. Ask before building it (Recommended)": a visible difference from an approved board is
     asked before the code, not listed afterwards. Rejected: matching the boards exactly; always "Person
     affected"; removing the CI's button; building first and listing it in the PR.
- **Design handoff:** `tmp/screen-handoff-slice5b.md` (screen 14, the Administration nav item, screen 16's
  new entries); the boards came back and were approved (item 7) before any UI code (rule 3).

## DEC-046 Slice 6: relationships, the relationship tab and the graph explorer

- **Status.** Approved by the owner on 2026-10-24 ("approved but run /precompact 1st"); branch
  `slice/06-relationships` from `992387f` (after PR #56). HANDOFF row 6: "Relationships CRUD, relationship tab,
  graph explorer", done when "US-11 for relationships; US-40" (screens 5's Relationships tab and 6; FR-CM-5,
  FR-CM-6, NFR-A11Y-2). The "as of" control on screen 6 is slice 7's (HANDOFF row 7).
  The boards were approved by the owner on 2026-10-25 ("approved") as canvas version `1792943153-8870`, after the
  review below; they are `docs/design/screens/slice-6/`.
- **The owner's answers (2026-10-24, AskUserQuestion).** The owner relayed a survey of other CMDBs and a check of
  the Cytoscape.js source, both done by a Claude research session from vendor documentation and source (its
  reports are kept outside the repository, in the git-ignored `tmp/slice6-research/`); the readings below are that
  session's, the choices are the owner's.
  1. **Removing a relationship: "A, delete; history keeps it."** The row is deleted and its history table keeps
     every version, so slice 7's "as of" view shows it before the delete. The reading relayed with it: FR-IM-4 speaks of
     "relationships ending before the as-of time", US-12's lifecycle belongs to CIs only, 0001 has no lifecycle
     column but `UQ_Relationship (from, type, to)`, which a retired row would block re-linking under; A departs
     only from US-11's word "retire". ServiceNow, iTop, NetBox and Device42 delete the link and rely on a log;
     BMC Helix and CMDBuild soft-delete; GLPI soft-deletes only agent-inventoried links. Condition: a DELETE on
     a system-versioned table copies the row to history as it was, so the history row's `ChangedBy` is the last
     writer, not the person who ended it. The ending's actor lives in `cmdb.AuditLog`, and slice 7's history view
     reads it from there (as slice 5b's deletes of attributes, types and relationship types already work). Never
     UPDATE `ChangedBy` and then DELETE: that leaves a zero-length history row. Rejected: B, a retired state like
     CIs. Slice 10 (discovery) decides how collectors end links (HANDOFF §3).
  2. **Redundancy group: "A, optional field now, with two conditions."** An optional "Redundancy group" text on a
     relationship (FR-CM-6), set when adding or editing it; editing a relationship means changing this text (its
     type and ends never change). The impact engine (slice 19) reads it; collectors and the CSV import write the
     same field. Conditions: (a) the form says impact analysis uses it later; (b) the grouping rule is written
     now, so slice 19 never normalises what Editors typed. **The rule:** the members of one redundancy group are
     the relationships that share their dependent CI (`FromCiId`) and their redundancy group text. The API trims
     the text, stores an empty one as none (`NULL`), and refuses control characters and more than 200 characters
     (the column is `nvarchar(200)`); texts are compared ignoring case (ordinal, as
     `StringComparison.OrdinalIgnoreCase`, the way names match in any case; owner, 2026-10-25, "A. Ignore case
     (Recommended)": "AG-ERP" and "ag-erp" are one group, the stored text keeps what was typed) and are never
     otherwise normalised (no whitespace folding, no accent folding). The panel offers the group texts already used
     on the dependent CI's relationships, so a typo
     doesn't split a group. Rejected: B, the field with slice 19.
  3. **The graph explorer: "A, Cytoscape.js."** The PRD's library, pre-approved in `web/dependency-allowlist.json`;
     version 3.34.4 (published 2026-10-15, no install scripts). The research session verified the 3.34.4 bundle and source:
     it injects one `<style id="__________cytoscape_stylesheet">` as the first child of `<head>` (one rule: its
     container class gets `position: relative`), with no nonce option, so the served CSP refuses it; everything
     else is `element.style` (allowed), with no `innerHTML` or other HTML sink and no `eval`; and it skips the
     injection when an element with that id already exists. Of the three options it set out for that element, the
     owner took (i), the one it recommended: the web app creates `<style id="__________cytoscape_stylesheet">` with
     the page's nonce (DEC-037) and that rule before the first `cytoscape()` call, and a test fails if the id
     changes. (iii), an upstream nonce option, is an errand for later, asked before anything is posted upstream.
     Rejected: (ii), accepting the violation; B, an in-repo SVG drawing with a fixed layout. Only the built-in
     layouts (`breadthfirst`, `concentric`, `cose`); no extensions (GLPI's `cytoscape-dagre` is not
     allow-listed). Verified under the served CSP and Trusted Types before the graph is built on it.
- **The board review (2026-10-25).** A second Claude session compared the canvas with the earlier mockups and 11
  products (`tmp/slice6-review-findings.md`, its reports in `tmp/slice6-research/`, both git-ignored). The owner
  answered rounds 1 and 3 in AskUserQuestion (round 3's reasons, the field evidence below, are that session's) and
  relayed round 2 and the grouping amendment from that session's text.
  4. **The graph's walk: "C. Two walks + group members."** The dependencies walk and the dependents walk each go
     away from the focus CI and never turn round; when the dependents walk reaches a CI through a relationship
     with a redundancy group, the other members of that group (same `FromCiId`, same text under item 2) and their
     far ends are shown too. A member reached that way shows its true hop: "A. Its true hop, 4"; the status line
     reads "up to 3 hops, plus redundancy groups". The boards' sample goes back to the earlier mockup's topology
     (app-erp-db01 ← os_of APP-ERP-DB01 ← hosted_on SQLPRD01\ERP ← replica_on AG-ERP, which also has
     SQLPRD02\ERP); the boards' `app-erp-db01 depends_on ERP-LSNR` goes. Rejected: A, the two walks alone (a group
     drawn with one member); B, every relationship at every hop (a host's or cluster's graph is too large at depth
     2 or 3).
  5. **Grouping: "A. Now, in slice 6"**, with the owner's amendment. More than 10 neighbours of one CI that share
     the direction, the relationship type and the CI type become one counted node ("124 VirtualMachines ·
     runs_on"); the walk stops there, runs hop by hop, and a group counts as one CI toward the 500-CI limit; the
     table view lists every member. Amendment: a Switch "Group many neighbours", on by default, beside Show
     retired; off, the walk goes through the members and the limit applies as before (one address parameter,
     `group=false`, one boolean on the graph read). The status line says when groups are present ("124 CIs in 1
     group"); a group's card offers "Show these 124 in the table view" and "Explore from esx-mx-014". "What breaks
     if this host fails?" is slice 19's question (US-41, screen 7), not screen 6's (US-40).
  6. **Adding several CIs at once: "A. Several at once."** The CI picker takes several CIs of the allowed type,
     with the same relationship type and redundancy group for all; the panel sends one audited request per CI,
     lists any that failed and stays open. No batch endpoint.
  7. **Export: "A. PNG and CSV now"**, amended the same day so the CSV follows DEC-041 item 3 ("exports are
     audited, reads are not"; a side agent noticed the first wording broke it): **"A. CSV on the server,
     audited."** `POST /api/v1/graph/export` takes the graph read's parameters and returns every relationship the
     table view lists (group members included) as CSV through `Security/Csv.cs`, under the audit export's rule
     (a row limit, refused rather than cut short, the count in `X-Goldmark-Entries`), and writes `graph.exported`
     (focus CI, depth, direction, filters, row count) in the same transaction; an access policy, the endpoint
     inventory test and `docs/security/endpoint-access.md`. The button reads "Export CSV…" and opens screen 16's
     confirmation ("The export is recorded in the audit log."). PNG stays in the browser (`cy.png`, downloaded
     from a `blob:` URL), unaudited: it is the pixels on screen, as a screenshot is. Rejected: B, both in the
     browser and unaudited (contradicts DEC-041 for no gain, the pattern exists); C, no CSV (the useful half of
     the export). No CSP change expected; confirmed in item 3's check.
  8. **A phrase per direction for relationship types: "B. Later, next slice touching screen 14"**, as #57.
  9. **Keyboard selection on the graph canvas: "A. Not in slice 6, reason on Notes"**: none of the 11 products
     documents it, and the table view is the accessible equivalent (NFR-A11Y-2); #58, for an accessibility pass.
  10. **The graph opens at depth 3: "A. 3."**
  11. **"View in graph" goes after Edit and Retire…: "A."** The approved slice 5 order stays; at 1024 it is the
      last item in More actions; Viewers see it alone. In the field nobody puts the graph before Edit.
  12. **Paging: "A. 50, then 'Show N more' on both"** (the CI page's tab and the graph's table view); each press
      adds up to 200. No norm in the field; the point is consistency between the two screens.
  13. **"Since" comes from a `CreatedAt` column: "B."** Migration 0008 adds a nullable `CreatedAt` to
      `cmdb.Relationship`, backfilled once from the history (each row's earliest `ValidFrom`) and set on insert;
      slice 10's collectors fill it as "first seen". Not A (the earliest history version per read):
      `cmdb.RelationshipHistory` has only the clustered index on the period columns, so the join would scan the
      history on every tab read, against NFR-PERF-1's 250k relationships with five years of history. ServiceNow,
      BMC, Freshservice, NetBox and i-doit keep a creation date that survives edits.
  14. **The graph's selected-CI card drops Owner: "A."** It shows type, lifecycle and hops; "Owned by" shows only
      when an `owned_by` relationship exists. A per-type list of card attributes, as iTop and ServiceNow have, is a
      later feature if wanted.
- **Not verified in that survey:** Device42's and Lansweeper's graph libraries (documentation blocked or
  silent), ServiceNow's relationship-form fields (third-party mirrors only); Freshservice and Jira Assets were not
  covered.
