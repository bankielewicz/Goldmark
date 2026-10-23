# Repository Guidelines

## Current State and Sources of Authority

Goldmark CMDB has a working baseline through slice 4b: a .NET 10 HTTPS API, health endpoints, Serilog JSON logging, a React SPA on Fluent UI v9, Docker Compose with SQL Server 2025 Express or an existing SQL Server 2025, DbUp migrations and least-privilege runtime access, sign-in with TOTP, invitations, roles and deny-by-default endpoint access. It now includes the audit log (filters, CSV export and bounded retention), password recovery and dual control, and a Windows service distribution with Windows authentication to SQL Server and credential rotation. The current slice-5 branch adds the first CMDB features: CI types and attributes, manual create/edit/retire/restore, name search and CI detail pages. Collectors, relationship management, history UI and impact remain future work; it is not production-ready.

Start slice work with `docs/HANDOFF.md` (status, working rules, next slice and gotchas), `docs/DECISIONS.md` (approved decisions), and the relevant requirements in `docs/PRD.md`. The PRD remains DRAFT v0.2; decisions override conflicting PRD text. DEC-021 and the handoff supersede PRD §11's build plan. Preserve requirement and decision IDs. `docs/specs/PRD-draft.md` is a local draft copy, not the current authority entry point.

As of 2026-10-23, slices 1, 2, 2b, 2c, 3, 4, 4a and 4b are merged (PRs #1, #14, #17, #22, #24, #32, #38 and #39). Slice 5 is approved and implemented on `slice/05-ci-crud`, with PR #49 open; DEC-044 records its scope and approved screens. Slice 5b (Types & attributes administration, screen 14) follows and still needs the owner's go-ahead. Reconcile status with the handoff's §1 table and §3 notes and current GitHub history; this snapshot does not authorize the next slice. Licensing and DCO remain pending; no license has been granted.

## Project Structure

- `Goldmark.slnx`: the API, its in-process test project and its SQL Server integration test project.
- `src/Goldmark.Api/`: configuration validation, HTTPS/security headers, health/readiness (schema version and startup rights checks), SQL access through `DbSession` (`Data/`), sign-in and recovery (`Auth/`, `Users/`), audit queries/export/retention (`Audit/`), CI types, attributes and CRUD (`Inventory/`), Windows service and SPA hosting (`Hosting/`), embedded migrations, and the `healthcheck`, `bootstrap-db`, `migrate` and `rotate-db-credentials` CLI commands (`Commands/ContainerCommands.cs`).
- `tests/Goldmark.Api.Tests/`: xUnit v3 in-process tests, including HTTP tests via `WebApplicationFactory`. No Docker needed.
- `tests/Goldmark.Api.IntegrationTests/`: migrations, principal audits, runtime permissions, authentication/recovery, audit retention, credential rotation and CI APIs against real SQL Server 2025 (Testcontainers, one container per test collection; DEC-027). Normally needs Docker. `WindowsBaselineTests` uses SQL Server installed on a Windows runner; `PerformanceTests` is opt-in with `GOLDMARK_PERF=1`.
- `tests/sql-gate/`: proves that non-constant SQL passed to `DbSession` fails the build (NFR-SEC-3).
- `db/`: forward-only migrations (`migrations/NNNN_lower_snake.sql`), their hash manifest `migrations.sha256`, and `check-migrations.sh` with its self-test (DEC-028).
- `tests/smoke/compose-smoke.sh`: real Compose, SQL Server and browser smoke tests.
- `tests/windows/`: SQL Server Express setup and Windows service installation, upgrade, refusal, rotation and uninstall smoke tests; separate from the Linux Testcontainers suite.
- `web/`: React 19 + TypeScript + Vite browser-only SPA; `src/`, Node policy/build-output tests in `scripts/` and `tests/`, Playwright tests in `e2e/`.
- `deploy/`: Compose, required environment-variable template, SQL Server entrypoint and health/TLS scripts; `deploy/windows/` builds the self-contained `win-x64` zip and supplies install, uninstall and credential-rotation scripts.
- `.github/workflows/ci.yml`: dependency policy, web, .NET, SQL Server integration tests, migration immutability, Compose smoke, Windows service, dependency review and CodeQL gates. `.github/workflows/performance.yml` measures CI list/search/detail p95 on 25,000 synthetic CIs on demand.
- `docs/ops/`, `docs/security/`: operational and endpoint-access guidance.
- `docs/integration/deep-links.md`: the v1 route contract for CI pages and search, with later tabs/routes explicitly reserved.
- `docs/design/screens/`: the original 21-screen mockups and state boards are design input, not approved designs; prompts are in `docs/specs/claude-design-prompts.md`. The approved design system is in `docs/design/system/` (DEC-039). Approved screen boards are in `slice-3/`, `slice-4/`, `slice-4a/` and `slice-5/` beneath `docs/design/screens/` (DEC-040, DEC-041, DEC-042 and DEC-044); each README records scope and deviations. Ignored `tmp/` references are not application code or approval evidence.

Add `Goldmark.Core`, `.Data`, `.Jobs`, `.Contracts`, `collectors/` and `spec/` only when the authorized slice needs them. Do not scaffold empty projects or install future dependencies.

## Workflow and Design Gates

Follow the owner rules in `docs/HANDOFF.md` §2. For a new slice session, confirm the next slice and open decisions in 3–5 lines and wait for the owner's go-ahead. The build uses an explicit **APPROVED** gate before the next phase, slice or step; existing authorization in the current session still applies.

- Branch from latest `main` as `slice/<nn>-<short-name>`, one PR per slice. Implement acceptance criteria with tests, run lint and the full suite, self-review and fix, then publish only within the authorized scope. Never auto-merge; pull `main` after the owner merges.
- Check the active branch before every commit when sessions share a folder; use separate worktrees for concurrent work. Preserve existing edits and untracked local drafts.
- After the initial push, allow at most two review-fix rounds. Report later findings with a fix-now/defer recommendation and follow the handoff's issue-routing rule rather than starting another round without authorization.
- Before UI code, provide the batched **DESIGN HANDOFF — paste into Claude Design** for all screens in the slice and wait for the design result. Backend/API work may proceed within the authorized slice. Existing mockups do not bypass this gate.
- Fluent UI v9 runs under the served CSP and Trusted Types policy with a per-response style nonce for the SPA shell (DEC-037). Any other relaxation of the policy goes to the owner; do not weaken it to make something work. Screen handoffs build on the approved design system (`docs/design/system/`).
- Update `docs/HANDOFF.md` in each slice PR; record decisions and deviations in `docs/DECISIONS.md`. Keep chat updates short. Ask before changing scope or adding unanticipated dependencies; the handoff requires owner approval for every new npm dependency.
- Every mutation must write tested audit records (US-07); every UI function must be available through `/api/v1` (US-50); every endpoint must declare and test its access policy (M6). For owner decisions, explain a concrete example and give lettered options with a recommendation.
- Keep both Docker and Windows service deployments working (DEC-030, DEC-043): provisioning stays in the CLI, settings are source-agnostic, platform defaults live in `Configuration/DefaultPaths.cs`, and Windows APIs are guarded by `OperatingSystem.IsWindows()`. Evaluate effective rights with the service's own token; administrator impersonation misses Windows group rights.

## Toolchain, Build and Local Development

Use the repository pins: .NET SDK **10.0.402** (`global.json`, roll-forward disabled), Node **24.21.0** (`.node-version`), and pnpm **11.28.5** (`web/package.json`, Corepack integrity pin). `global.json` selects Microsoft.Testing.Platform; use `dotnet test --project` or `--solution`, not the older positional syntax.

The following commands match the committed CI configuration; listing them is not evidence that they passed in the current environment. Run from the repository root in Bash unless a `cd` is shown.

```sh
node web/scripts/check-dependency-policy.mjs web
node --test "web/scripts/*.test.mjs"

dotnet restore Goldmark.slnx --locked-mode
dotnet format Goldmark.slnx --verify-no-changes --no-restore
dotnet build Goldmark.slnx -c Release --no-restore
tests/sql-gate/check-sql-gate.sh Release
dotnet test --project tests/Goldmark.Api.Tests -c Release --no-build              # in-process, no Docker
dotnet test --project tests/Goldmark.Api.IntegrationTests -c Release --no-build   # Docker

db/check-migrations.test.sh
db/check-migrations.sh origin/main
```

`dotnet test --solution Goldmark.slnx` runs both test projects, so it needs Docker; CI runs them as separate jobs. The first integration run pulls the SQL Server image (about 1.5 GB). One class or method: add `--filter-class '<Namespace.Class>'` or `--filter-method '*Name*'` to a `--project` command. Never edit a merged migration; add a new script and append its `sha256sum` line to `db/migrations.sha256`.

For the slice-5 performance criteria, after the Release build, run:

```sh
GOLDMARK_PERF=1 dotnet test --project tests/Goldmark.Api.IntegrationTests -c Release --no-build --filter-class Goldmark.Api.IntegrationTests.PerformanceTests
```

This requires Docker and seeds 25,000 CIs: list/search p95 must be at most 500 ms and detail at most 300 ms (NFR-PERF-2, DEC-044). The ordinary suite skips it; an ordinary suite pass is not performance evidence. The Performance workflow is on demand, not a per-PR gate.

Front end, after the dependency-policy checks pass:

```sh
cd web
corepack enable pnpm
pnpm install --frozen-lockfile
pnpm run policy:installed
pnpm run lint
pnpm run typecheck
pnpm run format:check
pnpm run build
pnpm test
pnpm audit --audit-level low
```

For local API development, configure `Goldmark:Database:Password` with user secrets (see `README.md`), then run `dotnet run --project src/Goldmark.Api`. Without reachable SQL Server, readiness is unhealthy. To serve the SPA from the API, build `web/` and copy `web/dist/` contents to `src/Goldmark.Api/wwwroot/`. `pnpm dev` in `web/` runs Vite; use the served API/Compose build to verify production security headers.

For the full deployment, follow `README.md`: Docker Engine 25+, Compose v2.24+, x64 and about 4 GB free RAM; copy `deploy/.env.example` to `deploy/.env`, restrict its permissions and fill all required values. Run `docker compose -f deploy/docker-compose.yml up -d --build`, then use `https://localhost:8443/`. Never commit secrets or `.env` files. Do not remove data volumes during routine shutdown.

From the repository root, with web test tooling installed and Chrome/Chromium available:

```sh
GOLDMARK_E2E_CHANNEL=chrome tests/smoke/compose-smoke.sh
# Alternatively set GOLDMARK_E2E_CHROMIUM=/path/to/chromium.
```

The smoke script generates test secrets, uses port 18443, runs Playwright and removes its test stack/volumes afterward. Never run two smoke tests concurrently (shared project `goldmark-smoke`) or edit the script while it runs. Direct `pnpm e2e` requires `GOLDMARK_BASE_URL` and `GOLDMARK_TLS_SPKI_SHA256`; the smoke script supplies them. A skipped browser check is not a full smoke pass.

For the Windows distribution, follow `docs/ops/windows-service.md`. After the policy-checked web build, `deploy/windows/New-GoldmarkZip.ps1 -Output out` builds the zip. The `windows-service` CI job runs the Windows build and in-process suite, installs SQL Server 2025 Express with `tests/windows/Install-SqlExpress.ps1`, runs `WindowsBaselineTests`, then `tests/windows/Test-WindowsService.ps1 -Zip out/goldmark-win-x64.zip -SqlCertificateFile <sql.crt>`. These are Windows PowerShell commands; the install smoke needs elevation and a prepared SQL instance/certificate. From WSL, use a separate copy for Windows builds rather than sharing `bin/obj`. Domain-dependent gMSA/DPAPI-NG and domain-user upgrade checks need separate manual evidence; the CI runner has no domain.

## Coding, Dependencies and Security

Follow `.editorconfig`, `tests/.editorconfig`, `web/eslint.config.js` and `web/.prettierrc.json`: UTF-8/LF, four-space C#, two spaces otherwise; file-scoped C# namespaces, private fields `_camelCase`, constants/static readonly fields PascalCase. Use PascalCase types/components and camelCase TypeScript functions. .NET warnings/analyzers and ESLint warnings fail the build. Preserve test naming conventions (`*Tests.cs`, `*.test.mjs`, `*.spec.ts`).

Keep the frontend browser-only: no Next.js, SSR, RSC or Node.js runtime. Add-ons use platform contracts, never code loaded into the CMDB UI. Preserve CSP, Trusted Types, HTTPS, SQL TLS pinning and secret-free frontend builds. Every endpoint needs an explicit access classification, an update to `docs/security/endpoint-access.md`, and coverage in `EndpointInventoryTests`. Authorization is deny-by-default: map each endpoint with `RequireAccess(AccessPolicy.X)` or `AllowAnonymousByDesign(rationale)`, or it is refused. Report vulnerabilities privately per `SECURITY.md`.

Preserve the slice-5 contracts (DEC-044): signed-in users read CIs; `CiEditor` means Admin or Editor; only Admins add custom attributes. Updates, retire and restore require `If-Match` (428 missing, 412 stale); `PUT` replaces all attribute values, including unsetting omitted ones. Partial saves/PATCH are deferred to slice 8 (#48); attribute administration UI and editing/removal are slice 5b; saved filters, column chooser and search CSV export are slice 20 (#45–#47). Keep search state in the URL and route behavior aligned with `docs/integration/deep-links.md`.

- npm: exact versions, a justified `web/dependency-allowlist.json` entry, and committed `package.json`/`pnpm-lock.yaml` changes together. pnpm 11 policy lives in `web/pnpm-workspace.yaml`; preserve the strict seven-day release age, blocked lifecycle scripts and registry/trust controls. Check the installed tree before running installed tooling.
- NuGet: central versions in `Directory.Packages.props`, versionless project references, source mappings in `NuGet.config`, and committed per-project lockfiles. For an approved change, regenerate with `dotnet restore Goldmark.slnx -p:RestoreLockedMode=false --force-evaluate`, then verify locked restore. Preserve signature validation and NuGetAudit failures at all severities.
- Keep container images and GitHub Actions pinned by digest/SHA. Do not weaken validation scripts, tests or security controls to obtain a pass.

## Verification and Pull Requests

For behavior changes, establish a meaningful failing test, implement, then refactor; report when the failing test cannot be established or run. Cover the slice's acceptance criteria and run the applicable checks above plus required CI gates before declaring completion. Current suites are xUnit (in-process and SQL Server integration), the migration checker's self-test, the NFR-SEC-3 gate self-test, Node's test runner, Playwright/Compose with axe accessibility checks, Windows PowerShell smoke tests and opt-in performance tests. Pester and shared C#/PowerShell identity vectors remain future work. There is no numerical coverage threshold.

Bind evidence to the actual revision/working tree: report commands, outcomes and acceptance criteria as PASS, FAIL, NOT_RUN or BLOCKED. Distinguish local checks from GitHub CI and document skipped checks; historical handoff results do not qualify new changes. Documentation-only updates should check referenced paths, commands and whitespace without claiming application tests ran.

Use concise imperative commit subjects with the existing `docs:`, `fix:`, `feat:` or `chore:` style. PRs include what changed, how to test, related requirement/issue IDs, an acceptance checklist, review findings (fixed and remaining), and screenshots for UI changes. Do not introduce DCO/license requirements before the owner's licensing decision.

## Environment Troubleshooting and Shared Papercuts

`docs/HANDOFF.md` §5 records prior environment issues. Reverify them locally: its `/root/.goldmark-env`, `/opt/` tooling, proxy workarounds and GitHub credential limitations describe another sandbox and may not apply here. Keep local proxy/CA workarounds outside the repository; never disable TLS verification or commit sandbox-specific Dockerfiles.

When tooling fails unexpectedly, consult `C:/Users/bryan/.codex/code/papercuts.md` and `C:/Users/bryan/.claude/code/papercuts.md` (under `/mnt/c/Users/bryan/` in WSL); verify recorded fixes locally. If Claude's log is unavailable, continue. Append time-consuming obstacles only to the Codex log: `date · symptom · fix or unresolved status · project`. Preserve entries, avoid duplicates and secrets, and reference relevant Claude entries without copying its log wholesale. Respect the active write permissions. If blocked by tooling, credentials or network access, state exactly what is needed.
