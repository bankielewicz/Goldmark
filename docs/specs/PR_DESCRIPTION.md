# Slice 1: scaffold, supply-chain controls, HTTPS host, health, Compose with SQL Server 2025 Express

**Branch:** `claude/slice-01-scaffold` → `main` · **Do not auto-merge.**
Scope: PRD slice 1 as approved on 2026-10-16. The PRD itself stays a draft (DEC-001).
Decisions and deviations: `docs/DECISIONS.md`.

## What changed

Commits are split by concern, in this order:

| Commit | Concern |
|---|---|
| `1782333` | PRD v0.2 stored verbatim (plus a labelled pointer to DECISIONS.md) |
| `7518078` | Toolchain pins (.NET SDK 10.0.402, Node 24.21.0) and NuGet lockdown (nuget.org only, source mapping, required repository signatures, lock files + locked restore, NuGetAudit as errors), committed **before the first restore** |
| `6a16e07` | npm policy committed **before any install**: pnpm 11.28.5 pinned by sha512; 7-day `minimumReleaseAge` (strict); `trustPolicy: no-downgrade`; `blockExoticSubdeps`; `ignoreScripts`; `strictDepBuilds`; allow-list with justifications; dependency-free policy checker + 21 tests |
| `691cbf4` | React 19 / TypeScript 6.0 / Vite 8 SPA shell. Lockfile generated under the policy (it picked react 19.3.0 over the 6-day-old 19.3.1). ESLint security bans, Prettier, CSP-clean build checks, Playwright browser test |
| `54f11b2` | ASP.NET Core host: HTTPS only with a first-start per-installation certificate; HSTS policy; `/health/live` and `/health/ready`; Serilog JSON; security headers; SPA hosting; `healthcheck` and `bootstrap-db` container commands; 76 in-process tests |
| `17aaad5` | Dockerfile (digest-pinned, chiseled non-root runtime) and Compose: SQL Server 2025 **Express**, TDS 8 strict only, pinned SQL certificate, least-privilege runtime login |
| `b0ca493` | `tests/smoke/compose-smoke.sh`: 36 checks against the real stack, including the browser check |
| `24dd5be` | CI (read-only token, SHA-pinned actions, no secrets) and Dependabot with a 7-day cooldown |
| `59e5e5c` | Fix: relative certificate paths resolve against the content root; dev launch profile |
| `629f0c9` | README, SECURITY.md, DECISIONS.md, TLS/HSTS docs, endpoint-access policy |
| `62029f6` | Review fixes: security headers kept on error responses; operator-certificate path validation |

## How to test

```sh
# Unit / in-process
dotnet restore Goldmark.slnx --locked-mode && dotnet build Goldmark.slnx -c Release
dotnet test --solution Goldmark.slnx -c Release
dotnet format Goldmark.slnx --verify-no-changes
cd web && node scripts/check-dependency-policy.mjs && corepack enable pnpm \
  && pnpm install --frozen-lockfile && pnpm run lint && pnpm run typecheck \
  && pnpm run format:check && pnpm run build && pnpm test && cd ..

# Real deployment (needs Docker, x64, Chrome or Chromium)
GOLDMARK_E2E_CHANNEL=chrome tests/smoke/compose-smoke.sh

# Manual
cp deploy/.env.example deploy/.env   # fill in the REQUIRED values
docker compose -f deploy/docker-compose.yml up -d --build
# open https://localhost:8443/  ·  /health/live  ·  /health/ready
```

## Acceptance criteria

Full table in `docs/DECISIONS.md` → "Slice 1 acceptance mapping". "Local" means
run in the agent's sandbox at `62029f6`.

- [x] `docker compose up` serves both health endpoints and the empty SPA (local smoke 36/36)
- [ ] **CI green: not observed.** The agent cannot see Actions on this private repo; please check this PR's checks
- [x] An unlisted npm dependency fails the CI policy step (unit tests + live demo below)
- [x] CI defines build, lint, tests and dependency-policy checks; least-privilege permissions
- [ ] **CodeQL and dependency review: UNMET** (DEC-018). An informational probe job reports availability in its job summary
- [x] Dockerfile; Compose with SQL Server 2025 **Express** (EngineEdition 4, major version 17, verified live)
- [x] Serilog JSON; no credentials, auth headers or cookies in logs (in-process test + live container logs)
- [x] NFR-SEC-9 web controls: frozen lockfile, no install scripts, release-age delay, allow-list, SSR/RSC ban, secret-free build job
- [x] SDK, Node and pnpm pinned; controls committed before installs; lockfiles; locked/frozen installs from then on
- [x] Only dependencies this slice uses (Dapper, DbUp, Testcontainers and Pester deferred)
- [x] Base and database images pinned by digest; only static assets copied from the web build
- [x] Required secrets, no default passwords: Compose refuses to start; the app exits 2 naming only the missing settings
- [x] Liveness independent of SQL Server; readiness runs a real query as a least-privilege login (not `sa`), returns 503 while SQL Server is stopped, and recovers
- [x] Health endpoints' access policy identified (anonymous by design; metadata + inventory test; `docs/security/endpoint-access.md`)
- [ ] Readiness checks schema compatibility: **deferred to slice 2**, as instructed
- [x] HTTPS by default; per-installation certificate persisted outside the image (survives container re-creation); trust/replacement and HSTS documented separately
- [x] CSP + Trusted Types + ESLint controls. No incompatibility: verified in a real browser
- [x] In-process tests (79) **and** a real Compose smoke test with health checks and bounded waits
- [x] `SECURITY.md`
- [ ] US-01 parts outside slice 1: DbUp with a migration login and DML-only runtime rights (slice 2); "use an existing SQL Server" (slice 2)

## Executed evidence (local, HEAD `62029f6`)

Logs are attached separately: `local-verification.log`, `compose-smoke.log`, `policy-demos.log`.

- Policy check passed; policy tests 21/21; `pnpm install --frozen-lockfile`
  ("Lockfile passes supply-chain policies"); lint, typecheck and format clean;
  build OK; dist tests 4/4; `pnpm audit`: no known vulnerabilities.
- `dotnet restore --locked-mode` (signatures required, NuGetAudit) OK;
  `dotnet format --verify-no-changes` clean; Release build 0 warnings, 0 errors; tests **79/79**.
- Compose smoke: **36/36 PASS**, including:
  - SQL Server `4|Express Edition (64-bit)|17`;
  - runtime login privileges `0000`, DDL denied; non-strict TDS client rejected;
  - readiness 503 within 60 s of stopping SQL Server, liveness stays 200, readiness back to 200 after restart;
  - certificate served == certificate in volume, unchanged after re-creating the container; key `600` owned by uid 1654;
  - TLS 1.1 refused; no secret in any container log;
  - Playwright: SPA renders with zero CSP/Trusted Types violations, in a secure context, and a raw `innerHTML` sink is blocked.
- Live demos:
  - `pnpm add left-pad` → policy step fails ("not in dependency-allowlist.json");
  - `next` in the lockfile → fails;
  - `dangerouslySetInnerHTML` → ESLint error.

## Unresolved or unexecuted checks

1. **GitHub Actions has not been observed** (the agent has no API access to the private repo). Expectations:
   - `security-feature-probe` is informational. It should report dependency review / code scanning as NOT AVAILABLE.
   - `compose-smoke` pulls about 2.4 GB of SQL Server image.
2. **The committed Dockerfile was not built byte-for-byte locally.** The sandbox's TLS proxy required a copy with the proxy CA mounted as a BuildKit secret on the two download steps (the diff is only those mounts). CI builds the committed file.
3. **CodeQL and dependency review are not running** (private personal repo). Substitutes that do run: NuGetAudit, `pnpm audit`, Dependabot, the policy checker, SHA-pinned actions.
4. **Dependabot config** is only validated by GitHub once it is on the default branch. Dependabot alerts need the dependency graph enabled (repository settings).
5. **Browser coverage:** Chromium only (local Chromium 141; CI Google Chrome). Edge and Firefox are not run.
6. **Not executed:** a Windows dev host (PKCS#12 certificate path); an operator-supplied certificate inside Compose (covered in-process only); HSTS in a real browser (in-process only).
7. **NuGet revocation checks** ran offline locally (CRL hosts unreachable); CI checks online.

## Self-review findings

**Fixed in this PR**

- Security headers were missing on 500 responses: the exception handler clears headers. They are now set in `OnStarting`. A regression test fails on the old code.
- A missing operator certificate file crashed with a stack trace. It is now a clean startup validation error.
- `Microsoft.Data.SqlClient` refuses globalization-invariant mode. Found by the smoke test; the runtime image is now the chiseled `-extra` variant with ICU.
- The SPA fallback logged every route as `/index.html`. Request logs now use the original path (never the query string).
- Serilog's static logger was disposed across hosts, and request logs went to the unconfigured static logger. Each host now owns its logger explicitly.
- `dotnet run` from the repo root used Production settings and wrote the development certificate to `/var/lib/goldmark`. Fixed (content-root paths, launch profile).
- `System.Security.Cryptography.Pkcs` 9.0.18 (pulled in by SqlClient) has GHSA-846f-f764-76mx (high). Pinned to 10.0.13, a recorded exception to the 7-day rule.

**Remaining / risks**

- Fluent UI v9 runtime styles (Griffel) are unverified against `style-src 'self'` and `trusted-types 'none'`. They must be tested before slice 5 adopts Fluent.
- Secrets reach containers as environment variables from `deploy/.env` (`chmod 600` is documented). Docker secrets are deferred.
- The SQL healthcheck uses `sa` inside the SQL container (loopback, strict TLS, pinned certificate).
- No reverse-proxy / forwarded-headers support yet (documented in `hsts.md`).

## Deviations from the PRD (all in DECISIONS.md)

- Repository: private `bankielewicz/Goldmark`, not public `Goldmark-cmdb`.
- No LICENSE, NOTICE, CONTRIBUTING (DCO) or CODE_OF_CONDUCT; licensing is pending.
- One API project only; the other projects arrive with their slices.
- Playwright lives in `web/e2e`, not `tests/e2e`.
- pnpm settings live in `web/pnpm-workspace.yaml` (required by pnpm 11).
- The banned-package list is extended (`@react-router/*`, `@vitejs/plugin-rsc`).
- `trusted-types 'none'` (stricter than the PRD).
- HSTS is automatic: off with the self-signed certificate.
- SQL Server forces TDS 8 strict (stricter than the PRD).
- TypeScript is 6.0, not 7.
- `/api/v1/health` is not implemented; the decision is deferred to slice 8.
- The external-SQL Compose file is deferred to slice 2.
