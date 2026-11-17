# Repository Guidelines

## Scope and approvals

Start with `docs/HANDOFF.md` for slice status and owner rules. Follow `docs/DECISIONS.md` where it amends `docs/PRD.md`. Preserve requirement IDs. Summarize the next slice and open decisions in 3–5 lines. Wait for **APPROVED** before each new phase, slice or step. Before UI work, submit one design handoff covering all slice screens and wait for the design result.

## Project structure

- `src/Goldmark.Api/` holds the ASP.NET Core API. Its `Inventory/` directory implements configuration items, types and relationships.
- `web/src/` holds the React, TypeScript and Fluent UI SPA. Use the approved design assets in `docs/design/system/`.
- Add forward-only SQL scripts under `db/migrations/` as `NNNN_lower_snake.sql` and append their hashes to `db/migrations.sha256`.
- `deploy/` contains Compose and Windows service distributions. Support both when changing configuration or startup.

## Build and run

Use the versions pinned in `global.json`, `.node-version` and `web/package.json`. Run these commands from the repository root.

```sh
dotnet restore Goldmark.slnx --locked-mode
dotnet build Goldmark.slnx -c Release --no-restore
dotnet run --project src/Goldmark.Api
```

Configure the database password with user secrets as described in `README.md`. Start the SPA from `web/`.

```sh
node scripts/check-dependency-policy.mjs
corepack enable pnpm
pnpm install --frozen-lockfile && pnpm run policy:installed
pnpm dev
```

## Style and security

Follow `.editorconfig` (four spaces for C#, two elsewhere). Use file-scoped namespaces, `_camelCase` private mutable fields and PascalCase components. Check formatting with `dotnet format` and Prettier. Get owner approval for new npm dependencies.

Test audit records for every mutation (US-07). Expose UI functions through `/api/v1` (US-50). Declare each endpoint’s access policy, document it in `docs/security/endpoint-access.md` and cover it in `EndpointInventoryTests` (M6).

## Testing

Start behavior changes with a failing test. Run xUnit v3 (`*Tests.cs`) with `dotnet test --project tests/Goldmark.Api.Tests -c Release`. Use `tests/Goldmark.Api.IntegrationTests` for SQL Server 2025 tests with Docker. In `web/`, run `pnpm run lint`, `pnpm run typecheck`, `pnpm run format:check`, then `pnpm run build && pnpm test` for Node tests (`*.test.mjs`). Run Playwright (`*.spec.ts`) through `GOLDMARK_E2E_CHANNEL=chrome tests/smoke/compose-smoke.sh` from the root. Run the applicable gates in `.github/workflows/ci.yml`. Record each criterion as PASS, FAIL, NOT_RUN or BLOCKED against the tested revision.

## Commits and pull requests

Branch from latest `main` as `slice/<nn>-<short-name>`. Use imperative subjects such as `fix: preserve relationship direction`. Open one PR per slice. Update the handoff, link requirements and issues, include test commands and acceptance results, summarize fixed and remaining findings, and attach UI screenshots. After two review-fix rounds, take further findings to the owner. The owner merges.
