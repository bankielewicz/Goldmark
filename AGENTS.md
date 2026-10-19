# Repository Guidelines

## Current State and Sources of Authority

Goldmark CMDB has a working slice-1 skeleton: a .NET 10 HTTPS API, health endpoints, Serilog JSON logging, an empty React SPA, and Docker Compose with SQL Server 2025 Express. There are no CMDB features or authentication yet; it is not production-ready.

Start slice work with `docs/HANDOFF.md` (status, working rules, next slice and gotchas), `docs/DECISIONS.md` (approved decisions), and the relevant requirements in `docs/PRD.md`. The PRD remains DRAFT v0.2; decisions override conflicting PRD text. DEC-021 and the handoff supersede PRD §11's build plan. Preserve requirement and decision IDs. `docs/specs/PRD-draft.md` is a local draft copy, not the current authority entry point.

As of 2026-10-19, slice 1 is merged and slice 2 (database baseline) is next. Its approved decisions are in `docs/HANDOFF.md` §3; they are not implemented yet. Read the current handoff rather than assuming this snapshot authorizes the next slice. Licensing and DCO remain pending; no license has been granted.

## Project Structure

- `Goldmark.slnx`: API and API test projects.
- `src/Goldmark.Api/`: configuration validation, HTTPS/security headers, health/readiness, SQL connections, SPA hosting, and `bootstrap-db`/`healthcheck` commands.
- `tests/Goldmark.Api.Tests/`: xUnit v3 tests, including in-process HTTP tests via `WebApplicationFactory`.
- `tests/smoke/compose-smoke.sh`: real Compose, SQL Server and browser smoke tests.
- `web/`: React 19 + TypeScript + Vite browser-only SPA; `src/`, Node policy/build-output tests in `scripts/` and `tests/`, Playwright tests in `e2e/`.
- `deploy/`: Compose, required environment-variable template, SQL Server entrypoint and health/TLS scripts.
- `.github/workflows/ci.yml`: dependency policy, web, .NET, Compose smoke, dependency review and CodeQL gates.
- `docs/ops/`, `docs/security/`: operational and endpoint-access guidance.
- `docs/design/screens/`: 21 HTML mockups and their review status; prompts are in `docs/specs/claude-design-prompts.md`. These and ignored `tmp/` references are design input, not application code or approved designs.

Add `Goldmark.Core`, `.Data`, `.Jobs`, `.Contracts`, `collectors/`, `db/migrations/` and `spec/` only when the authorized slice needs them. Do not scaffold empty projects or install future dependencies.

## Workflow and Design Gates

Follow the owner rules in `docs/HANDOFF.md` §2. For a new slice session, confirm the next slice and open decisions in 3–5 lines and wait for the owner's go-ahead. The build uses an explicit **APPROVED** gate before the next phase, slice or step; existing authorization in the current session still applies.

- Branch from latest `main` as `slice/<nn>-<short-name>`, one PR per slice. Implement acceptance criteria with tests, run lint and the full suite, self-review and fix, then publish only within the authorized scope. Never auto-merge; pull `main` after the owner merges.
- After the initial push, allow at most two review-fix rounds. Report later findings with a fix-now/defer recommendation and follow the handoff's issue-routing rule rather than starting another round without authorization.
- Before UI code, provide the batched **DESIGN HANDOFF — paste into Claude Design** for all screens in the slice and wait for the design result. Backend/API work may proceed within the authorized slice. Existing mockups do not bypass this gate.
- Slice 3 must first verify Fluent UI v9 under the served CSP and Trusted Types policy, then complete the design-system handoff before screen handoffs. If verification fails, stop and bring options to the owner; do not weaken the policy to make it work.
- Update `docs/HANDOFF.md` in each slice PR; record decisions and deviations in `docs/DECISIONS.md`. Keep chat updates short. Ask before changing scope or adding unanticipated dependencies; the handoff requires owner approval for every new npm dependency.

## Toolchain, Build and Local Development

Use the repository pins: .NET SDK **10.0.402** (`global.json`, roll-forward disabled), Node **24.21.0** (`.node-version`), and pnpm **11.28.5** (`web/package.json`, Corepack integrity pin). `global.json` selects Microsoft.Testing.Platform; use `dotnet test --solution`, not the older positional solution syntax.

The following commands match the committed CI configuration; listing them is not evidence that they passed in the current environment. Run from the repository root in Bash unless a `cd` is shown.

```sh
node web/scripts/check-dependency-policy.mjs web
node --test "web/scripts/*.test.mjs"

dotnet restore Goldmark.slnx --locked-mode
dotnet format Goldmark.slnx --verify-no-changes --no-restore
dotnet build Goldmark.slnx -c Release --no-restore
dotnet test --solution Goldmark.slnx -c Release --no-build
```

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

## Coding, Dependencies and Security

Follow `.editorconfig`, `tests/.editorconfig`, `web/eslint.config.js` and `web/.prettierrc.json`: UTF-8/LF, four-space C#, two spaces otherwise; file-scoped C# namespaces, private fields `_camelCase`, constants/static readonly fields PascalCase. Use PascalCase types/components and camelCase TypeScript functions. .NET warnings/analyzers and ESLint warnings fail the build. Preserve test naming conventions (`*Tests.cs`, `*.test.mjs`, `*.spec.ts`).

Keep the frontend browser-only: no Next.js, SSR, RSC or Node.js runtime. Add-ons use platform contracts, never code loaded into the CMDB UI. Preserve CSP, Trusted Types, HTTPS, SQL TLS pinning and secret-free frontend builds. Every endpoint needs an explicit access classification, an update to `docs/security/endpoint-access.md`, and coverage in `EndpointInventoryTests`; deny-by-default authentication/authorization is planned for slice 3, not already implemented. Report vulnerabilities privately per `SECURITY.md`.

- npm: exact versions, a justified `web/dependency-allowlist.json` entry, and committed `package.json`/`pnpm-lock.yaml` changes together. pnpm 11 policy lives in `web/pnpm-workspace.yaml`; preserve the strict seven-day release age, blocked lifecycle scripts and registry/trust controls. Check the installed tree before running installed tooling.
- NuGet: central versions in `Directory.Packages.props`, versionless project references, source mappings in `NuGet.config`, and committed per-project lockfiles. For an approved change, regenerate with `dotnet restore Goldmark.slnx -p:RestoreLockedMode=false --force-evaluate`, then verify locked restore. Preserve signature validation and NuGetAudit failures at all severities.
- Keep container images and GitHub Actions pinned by digest/SHA. Do not weaken validation scripts, tests or security controls to obtain a pass.

## Verification and Pull Requests

For behavior changes, establish a meaningful failing test, implement, then refactor; report when the failing test cannot be established or run. Cover the slice's acceptance criteria and run the applicable checks above plus required CI gates before declaring completion. Current suites are xUnit, Node's test runner and Playwright/Compose. SQL Server Testcontainers is approved for slice 2 but not configured yet; Pester, shared C#/PowerShell identity vectors and axe remain future work. There is no numerical coverage threshold.

Bind evidence to the actual revision/working tree: report commands, outcomes and acceptance criteria as PASS, FAIL, NOT_RUN or BLOCKED. Distinguish local checks from GitHub CI and document skipped checks; historical handoff results do not qualify new changes. Documentation-only updates should check referenced paths, commands and whitespace without claiming application tests ran.

Use concise imperative commit subjects with the existing `docs:`, `fix:`, `feat:` or `chore:` style. PRs include what changed, how to test, related requirement/issue IDs, an acceptance checklist, review findings (fixed and remaining), and screenshots for UI changes. Do not introduce DCO/license requirements before the owner's licensing decision.

## Environment Troubleshooting and Shared Papercuts

`docs/HANDOFF.md` §5 records prior environment issues. Reverify them locally: its `/root/.goldmark-env`, `/opt/` tooling, proxy workarounds and GitHub credential limitations describe another sandbox and may not apply here. Keep local proxy/CA workarounds outside the repository; never disable TLS verification or commit sandbox-specific Dockerfiles.

When tooling fails unexpectedly, consult `C:/Users/bryan/.codex/code/papercuts.md` and `C:/Users/bryan/.claude/code/papercuts.md` (under `/mnt/c/Users/bryan/` in WSL); verify recorded fixes locally. If Claude's log is unavailable, continue. Append time-consuming obstacles only to the Codex log: `date · symptom · fix or unresolved status · project`. Preserve entries, avoid duplicates and secrets, and reference relevant Claude entries without copying its log wholesale. Respect the active write permissions. If blocked by tooling, credentials or network access, state exactly what is needed.
