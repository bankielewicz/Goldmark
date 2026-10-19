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
- **Ask before** adding a dependency the PRD didn't anticipate or changing scope. Update `docs/HANDOFF.md` in every slice PR.

## Commands

Toolchain is pinned and must match exactly: .NET SDK 10.0.402 (`global.json`, `rollForward: disable`), Node 24.21.0 (`.node-version`), pnpm 11.28.5 via Corepack (`corepack enable pnpm`).

Back end (solution `Goldmark.slnx`; tests use xUnit v3 on Microsoft Testing Platform):

```sh
dotnet restore Goldmark.slnx --locked-mode
dotnet format Goldmark.slnx --verify-no-changes --no-restore   # lint/format, as CI runs it
dotnet build Goldmark.slnx -c Release --no-restore
dotnet test --solution Goldmark.slnx -c Release --no-build
# One test class or method (xUnit v3 filters; not yet run in this repo):
dotnet test --project tests/Goldmark.Api.Tests -c Release --filter-class 'Goldmark.Api.Tests.HealthEndpointTests'
dotnet test --project tests/Goldmark.Api.Tests -c Release --filter-method '*Readiness*'
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

- **One image, several commands.** `Program.cs` dispatches `healthcheck` (the image `HEALTHCHECK`) and `bootstrap-db` (run by the one-shot `db-init` container) before any web host starts. Slice 2 adds a `migrate` command run the same way. Exit codes carry meaning: 0 ok, 1 SQL/connection error, 2 invalid configuration, 3 refused; Docker reserves 2 for healthchecks, so `healthcheck` returns 1 for every failure.
- **Fail fast on configuration.** `ConfigurationPreflight` and `ValidateOnStart` options make startup exit 2 listing setting names only. The .NET binder's own error message contains the value, so never print it. `WebApplicationFactory` settings aren't visible to `builder.Configuration` before `Build()`, so preflight is unit-tested directly and smoke-tested in the container.
- **Composition.** `Hosting/GoldmarkApplication.cs` (`AddGoldmark` / `UseGoldmark`) wires everything. HTTPS only: Kestrel listens on one TLS port with a certificate from `TlsCertificateProvider` (operator-supplied, or generated on first start and persisted in a volume); there is no HTTP listener. HSTS is automatic (on with an operator certificate, off with self-signed; DEC-007).
- **Security headers are outermost.** `SecurityHeadersMiddleware` runs first so error responses get them too. The CSP is strict (`style-src 'self'`, `require-trusted-types-for 'script'`, `trusted-types 'none'`; DEC-017). Any UI library must work under it; Fluent UI v9 (runtime style injection) is unverified and must be checked as the first step of slice 3, and a needed nonce or `unsafe-inline` goes back to the owner.
- **SPA hosting.** The React app (`web/`, browser-only, no SSR/RSC) is built to `web/dist` and served from `wwwroot` by `SpaHosting`. Catch-alls return 404 for unknown `/health/*` and `/api/*` paths so they never get the SPA shell with 200.
- **Every endpoint declares its access policy.** Endpoints carry `EndpointAccess(...)` metadata. `EndpointInventoryTests` fails on any unclassified endpoint, and `docs/security/endpoint-access.md` must list it: change all three together. Slice 3 turns this into the deny-by-default M6 enumeration test.
- **Database.** SQL Server 2025 Express in Compose, TDS 8 strict with a pinned certificate. The app connects as a least-privilege runtime login; `bootstrap-db` creates it and manages only principals tagged with the `goldmark:managed` extended property, refusing others unless `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`. DDL can't take parameters: identifiers and passwords go through `QUOTENAME` in `sp_executesql`. Readiness (`/health/ready`) runs a real query and returns status text only.
- **Tests.** `tests/Goldmark.Api.Tests/Infrastructure/GoldmarkFactory.cs` hosts the app in-process in the Production environment with a fake SPA build and a database port where nothing listens, so readiness must fail. `TestSecrets` values must never appear in logs or responses; `LoggingTests` checks this.

## Build and supply-chain rules

- Warnings are errors and code style is enforced at build time (`Directory.Build.props`, `.editorconfig`: file-scoped namespaces, 4-space C#, 2-space everything else).
- NuGet: central versions in `Directory.Packages.props`, nuget.org only with source mapping and required signatures, locked restore, NuGetAudit failing on any known vulnerability. Changing a package means regenerating and committing `packages.lock.json` (see README "Changing dependencies").
- npm: every package needs an entry with a justification in `web/dependency-allowlist.json`; pnpm settings live in `web/pnpm-workspace.yaml` (pnpm 11 ignores non-auth `.npmrc` settings). Versions younger than 7 days fail to install and install scripts never run. SSR/RSC packages are banned.
- Base images are pinned by digest. Don't rely on Dockerfile file modes for secrets; the app enforces them at start.

## Design mockups

`docs/design/screens/` holds the 21 Claude Design mockups (review input, not approved designs). They are published to two canvases listed in that folder's README; when a screen changes, edit the file there and publish it to both canvases. `docs/specs/claude-design-prompts.md` holds the prompts they were built from.
