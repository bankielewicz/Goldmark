# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read first

Goldmark is a self-hosted CMDB being built in slices. Three documents rule the work, in this order of precedence:

1. `docs/HANDOFF.md`: current status, the slice plan (branch names, scope, screens per slice), the owner's working rules, open issues and environment gotchas. Start every session here.
2. `docs/DECISIONS.md`: DEC-001 onward. Where it narrows or deviates from the PRD, it wins.
3. `docs/PRD.md`: requirements, stored verbatim as a draft (v0.2). Requirement IDs (US-xx, FR-xx, NFR-xx, M-x) are referenced everywhere; keep them when editing.

Working rules from HANDOFF that change how you act:

- **Gated flow.** Never start the next slice or phase until the owner replies "APPROVED". One PR per slice, branched from the latest `main` as `slice/<nn>-<short-name>`. Never auto-merge.
- **At most two review-fix rounds per PR** (Codex, CodeQL, self-review). Later findings go to the owner as fix-now/defer recommendations, then become issues.
- **Design handoff before any UI code.** For a slice with screens, output one "DESIGN HANDOFF — paste into Claude Design" block covering all of the slice's screens and wait. A design-system handoff comes first, before slice 3's screens.
- **Every slice, in addition to its own criteria** (rule 7): new mutations write audit records and are tested for it; new UI functions are also reachable through `/api/v1`; new endpoints declare an access policy and join the authorization enumeration test.
- **Ask before** adding a dependency the PRD didn't anticipate or changing scope. Update `docs/HANDOFF.md` in every slice PR.

## Commands

Toolchain is pinned and must match exactly: .NET SDK 10.0.402 (`global.json`, `rollForward: disable`), Node 24.21.0 (`.node-version`), pnpm 11.28.5 via Corepack (`corepack enable pnpm`; ignore pnpm's upgrade banner). On Linux/WSL the SDK needs ICU (`libicu`); don't work around it with invariant globalization, which `Microsoft.Data.SqlClient` refuses.

Back end (solution `Goldmark.slnx`; tests use xUnit v3 on Microsoft Testing Platform):

```sh
dotnet restore Goldmark.slnx --locked-mode
dotnet format Goldmark.slnx --verify-no-changes --no-restore   # lint/format, as CI runs it
dotnet build Goldmark.slnx -c Release --no-restore
dotnet test --project tests/Goldmark.Api.Tests -c Release --no-build              # in-process, no Docker, seconds
dotnet test --project tests/Goldmark.Api.IntegrationTests -c Release --no-build   # real SQL Server 2025 via Docker, about 2 min
# One test class or method (xUnit v3 filters; same options for either project):
dotnet test --project tests/Goldmark.Api.Tests -c Release --filter-class 'Goldmark.Api.Tests.HealthEndpointTests'
dotnet test --project tests/Goldmark.Api.Tests -c Release --filter-method '*Readiness*'
```

`dotnet test --solution Goldmark.slnx` runs both projects, so it needs Docker; CI runs them as separate jobs. The first integration run pulls the SQL Server image (about 1.5 GB).

Migrations (`db/`, CI job `migrations`):

```sh
db/check-migrations.test.sh             # the checker's own tampering cases
db/check-migrations.sh origin/main      # manifest, hashes, numbering; base revision's scripts unchanged
# Adding one: create db/migrations/NNNN_lower_snake.sql, then
sha256sum db/migrations/NNNN_lower_snake.sql >> db/migrations.sha256
```

Front end (run in `web/`):

```sh
node scripts/check-dependency-policy.mjs   # npm policy; CI runs it before any install
pnpm install --frozen-lockfile && pnpm run policy:installed
pnpm run lint && pnpm run typecheck && pnpm run format:check
pnpm run build && pnpm test                # test = policy-checker tests + built-dist checks
node --test scripts/check-dependency-policy.test.mjs   # one test file
```

End to end: `tests/smoke/compose-smoke.sh` builds and starts the real Compose stack on port 18443 with generated secrets, runs about 36 checks plus the Playwright browser test, and tears down. It needs a Chromium: `GOLDMARK_E2E_CHANNEL=chrome` or `GOLDMARK_E2E_CHROMIUM=/path/to/chrome`. Takes 4–5 minutes; never run two at once (shared Compose project `goldmark-smoke`). `web/e2e` cannot run on its own: Playwright needs `GOLDMARK_BASE_URL` and `GOLDMARK_TLS_SPKI_SHA256`, which the smoke script provides.

Run the stack: `cp deploy/.env.example deploy/.env`, fill in the REQUIRED values (no defaults), then `docker compose -f deploy/docker-compose.yml up -d --build` and open `https://localhost:8443/`.

## Architecture

One ASP.NET Core project for now (`src/Goldmark.Api`, DEC-005). The PRD's other projects (`Goldmark.Core`, `.Data`, `.Jobs`, `.Contracts`, `collectors/`, `spec/`) are added by the slices that need them; check HANDOFF before creating one.

- **One image, several commands.** `Program.cs` hands any positional first argument to `Commands/ContainerCommands.cs` before a web host starts, so a mistyped command exits 2 instead of starting a server. `healthcheck` (the image `HEALTHCHECK`) runs alone; database commands chain in-process and stop at the first failure: `db-init` runs `bootstrap-db migrate` (the image has no shell). Exit codes for database commands: 0 ok, 1 SQL/connection error, 2 invalid configuration, 3 refused. Docker reserves 2 for healthchecks, so `healthcheck` returns 1 for every failure.
- **Fail fast on configuration.** `ConfigurationPreflight` and `ValidateOnStart` options make startup exit 2 listing setting names only. The .NET binder's own error message contains the value, so never print it. `WebApplicationFactory` settings aren't visible to `builder.Configuration` before `Build()`, so preflight is unit-tested directly and smoke-tested in the container.
- **Composition.** `Hosting/GoldmarkApplication.cs` (`AddGoldmark` / `UseGoldmark`) wires everything. HTTPS only: Kestrel listens on one TLS port with a certificate from `TlsCertificateProvider` (operator-supplied, or generated on first start and persisted in a volume); there is no HTTP listener. HSTS is automatic (on with an operator certificate, off with self-signed; DEC-007).
- **Security headers are outermost.** `SecurityHeadersMiddleware` runs first so error responses get them too. The CSP is strict (`style-src 'self'`, `require-trusted-types-for 'script'`, `trusted-types 'none'`; DEC-017). The SPA shell alone adds a fresh per-response style nonce (`style-src 'self' 'nonce-…'`, DEC-037) so Fluent UI's runtime `<style>` elements load: `SpaShell` renders `index.html` with the nonce in `<meta property="csp-nonce">`, `no-store` and no validators, HTML is never a static file, and the app passes the nonce to Griffel with `createStyleRenderer()` (`web/src/styleNonce.ts`). Any other relaxation goes back to the owner.
- **SPA hosting.** The React app (`web/`, browser-only, no SSR/RSC) is built to `web/dist` and served from `wwwroot` by `SpaHosting`. Catch-alls return 404 for unknown `/health/*` and `/api/*` paths so they never get the SPA shell with 200.
- **Every endpoint declares its access policy; anything else is denied.** Map an endpoint with `RequireAccess(AccessPolicy.X)` (sets the authorization policy and `EndpointAccess` metadata together) or `AllowAnonymousByDesign(rationale)`. The fallback policy (`DenyUnclassifiedEndpoints`) refuses any endpoint without one. `EndpointInventoryTests` (the M6 enumeration test: inventory, 401 without credentials, 400 without anti-forgery) and `docs/security/endpoint-access.md` must list it: change all three together. `AccountAndAccessTests` repeats the sweep with real sessions (403 for a Viewer). A minimal-API lambda whose only parameter is `HttpContext` binds as a `RequestDelegate` and its returned result is dropped: give it a second parameter.
- **Sign-in (slice 3, DEC-038, DEC-040).** ASP.NET Core Identity's `UserManager` over a Dapper `UserStore` (`src/Goldmark.Api/Auth/`): Identity's hasher, lockout and security stamps; Goldmark's own TOTP provider (±1 step, a step never reused), sign-in steps, setup and invitation flows. Sessions are server-side (`SessionTicketStore`, the cookie handler's `ITicketStore` over `cmdb.UserSession`), re-checked on every request; the two endpoints that sign in carry `StartsNewSession`, so each sign-in gets a new session key. Three `__Host-` cookie schemes: `Goldmark.Session`, `Goldmark.TwoFactor` (password verified), `Goldmark.Enrolment` (setup/invitation/reset/new authenticator in progress, state carried encrypted in the cookie). Every state-changing `/api/v1` request needs the anti-forgery header `X-Goldmark-Antiforgery`. Every mutation writes `cmdb.AuditLog` through `AuditTrail` in the same transaction (`DbSession.InTransactionAsync`). The setup token is the one secret ever logged (event 3001). Data Protection keys live in `Goldmark:DataProtection:KeyDirectory` (0700/0600).
- **Data access through `DbSession` only (NFR-SEC-3).** `src/Goldmark.Api/Data/DbSession.cs` is the one file that may use Dapper; its SQL parameters are `[ConstantExpected]`, so CA1857 fails the build on SQL built from runtime values. Keep SQL in `const` strings; pass values as parameters. `SqlGateTests` and `tests/sql-gate/check-sql-gate.sh` guard this; never suppress CA1857.
- **Database and logins.** SQL Server 2025 Express in Compose, TDS 8 strict with a pinned certificate. Three principals with different passwords: `sa` (bootstrap only), `goldmark_migrator` (`db_owner` of the Goldmark database only, used by `migrate`, never reaches the app container) and the runtime login (member of the `goldmark_runtime` role: DML only, no DDL, explicit `DENY UPDATE, DELETE` on the audit log). `bootstrap-db` creates both logins and manages only principals whose `goldmark:managed` extended property names their purpose (`runtime-login`, `migration-login`); it refuses others unless `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`. DDL can't take parameters: identifiers and passwords go through `QUOTENAME` in `sp_executesql`.
- **Migrations are forward-only and immutable.** T-SQL scripts in `db/migrations/` are embedded in the API and applied by DbUp, one transaction per script, journal `dbo.SchemaVersions`. DbUp records names, not content, so a merged script is never edited: add a new one and its hash (DEC-028). The schema version is the newest script's number; `/health/ready` returns 503 unless the database is at exactly the version the build expects (DEC-023), and returns status text only. When testing permissions, check effective rights, not grant rows: a column-level `GRANT` to a role overrides that role's table-level `DENY` on SQL Server 2025, hence `migrate`'s per-column audit-log check.
- **Tests.** `tests/Goldmark.Api.Tests/Infrastructure/GoldmarkFactory.cs` hosts the app in-process in the Production environment over `https://localhost` (the cookies are Secure) with a fake SPA build and a database port where nothing listens, so readiness must fail. `tests/Goldmark.Api.IntegrationTests/Infrastructure/AuthScenario.cs` hosts it against a provisioned database and drives setup, invitations and sign-in through the API (`ApiClient` handles cookies and anti-forgery; `Authenticator` never reuses a time step). `TestSecrets` values must never appear in logs or responses; `LoggingTests` checks this. `tests/Goldmark.Api.IntegrationTests` starts one SQL Server container per test collection (`SqlServerFixture`): the image digest is read from `deploy/docker-compose.yml` and the real `deploy/sqlserver/sql-entrypoint.sh` runs, so tests pin the generated certificate like the app does; each test uses its own database and login names. The Testcontainers resource reaper is disabled (DEC-027). Analyzer CA1711 rejects type names ending in `Collection`, so xUnit collection definitions end in `Tests`.

## Build and supply-chain rules

- Warnings are errors and code style is enforced at build time (`Directory.Build.props`, `.editorconfig`: file-scoped namespaces, 4-space C#, 2-space everything else).
- NuGet: central versions in `Directory.Packages.props`, nuget.org only with source mapping and required signatures, locked restore, NuGetAudit failing on any known vulnerability. Changing a package means regenerating and committing `packages.lock.json` (see README "Changing dependencies").
- npm: every package needs an entry with a justification in `web/dependency-allowlist.json`; pnpm settings live in `web/pnpm-workspace.yaml` (pnpm 11 ignores non-auth `.npmrc` settings). Versions younger than 7 days fail to install and install scripts never run. SSR/RSC packages are banned.
- Base images are pinned by digest. Don't rely on Dockerfile file modes for secrets; the app enforces them at start.

## Design mockups

`docs/design/screens/` holds the 21 Claude Design mockups plus their state boards (`00-SharedStates`, `ST1`–`ST6`): review input, not approved designs. The files need the Claude Design runtime, so opened directly in a browser they render unstyled with every state showing; review them on the canvases. They wait for slice 3: with `docs/specs/claude-design-prompts.md` they are the starting point for the design-system handoff (rule 4) and for each slice's design handoff (rule 3). They are published to two canvases listed in that folder's README; when a screen changes, edit the file there and publish it to both canvases. `docs/specs/claude-design-prompts.md` holds the prompts they were built from.
