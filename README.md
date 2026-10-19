# Goldmark CMDB

> **Status: under development. Not for production use.**
> Slices 1–2b of the MVP: an HTTPS host with health endpoints, an empty web UI,
> a Docker Compose deployment with SQL Server 2025 Express or an existing SQL
> Server 2025, and the baseline database schema with separate migration and
> runtime logins, audited on every start. There are no CMDB features yet.

Goldmark (a working codename) is a planned self-hosted configuration
management database (CMDB) for Windows- and VMware-heavy IT estates:

- automatic, read-only collection from vCenter, Windows (CIM), Active
  Directory and SQL Server, reconciled into one CI per real-world thing;
- typed relationships with redundancy-aware impact analysis;
- point-in-time history of every CI and relationship;
- a versioned API and webhooks for downstream tools.

The product requirements are in [docs/PRD.md](docs/PRD.md) (a draft).
Approved scope, technical decisions and deviations from the PRD are in
[docs/DECISIONS.md](docs/DECISIONS.md). Build status, the slice plan and working
rules for contributors are in [docs/HANDOFF.md](docs/HANDOFF.md).

## Run it with Docker Compose

Requirements: Docker Engine 25+ with Compose v2.24+, on x64 (SQL Server has no
ARM64 image), and about 4 GB of free RAM.

```sh
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
# Edit deploy/.env and fill in every REQUIRED value (there are no defaults):
#   SQLSERVER_ACCEPT_EULA=Y   (accepts the SQL Server Express license terms)
#   SQLSERVER_SA_PASSWORD=... GOLDMARK_DB_PASSWORD=... GOLDMARK_DB_MIGRATION_PASSWORD=...
#   (three different passwords, e.g. each from: openssl rand -base64 30)
docker compose -f deploy/docker-compose.yml up -d --build
```

The first start takes a few minutes (image build, SQL Server initialization).
Then open <https://localhost:8443/>. Your browser will warn about the
self-signed certificate that Goldmark generated for this installation; see
[docs/ops/tls-certificates.md](docs/ops/tls-certificates.md) to trust it or
replace it with your own.

| URL | Meaning |
|---|---|
| `https://localhost:8443/health/live` | `Healthy` when the app process is serving requests |
| `https://localhost:8443/health/ready` | `Healthy` when the app can query its database and the schema version matches this build, otherwise `503 Unhealthy` |

What starts:

| Service | Purpose |
|---|---|
| `sql-volume-init` | One-shot: prepares the SQL Server TLS volumes (no network, `CAP_CHOWN` only) |
| `sqlserver` | SQL Server 2025 **Express**, internal network only, encryption required (TDS 8 strict) |
| `db-init` | One-shot, on every start: audits the SQL Server instance and both logins (the principal audit), creates the `Goldmark` database, the least-privilege runtime login and the migration login (`db_owner` of that database only), then applies the schema migrations as the migration login. It refuses, changing nothing, if the audit finds a problem or if a login or user already exists and was not created by Goldmark (see `GOLDMARK_DB_ADOPT_EXISTING_LOGIN`) |
| `app` | Goldmark (HTTPS on port 8443), non-root, read-only root filesystem. Holds only the runtime login: DML on Goldmark's tables, no DDL, no UPDATE or DELETE on the audit log |

Stop with `docker compose -f deploy/docker-compose.yml down`. Add `-v` to also
delete all data, including the database and certificates.

Without the required values, Compose refuses to start and names the missing
variable. Every variable is documented in
[deploy/.env.example](deploy/.env.example). Backups are not covered yet (US-60, slice 24).

### With an existing SQL Server

To use a SQL Server 2025 instance you already run, fill in the "Existing SQL
Server" section of `deploy/.env` (host, a dedicated administrator login with
CONTROL SERVER, never `sa`, and the server certificate) and use the other
Compose file:

```sh
docker compose -f deploy/docker-compose.external-sql.yml up -d --build
```

A DBA can also create the database and both logins in advance
(`GOLDMARK_DB_BOOTSTRAP_MODE=Existing`); `db-init` then creates nothing. On a
shared server, `db-init` refuses findings such as `guest` access to other
databases until they are fixed or accepted by ID. Details, the DBA script and
every finding: [docs/ops/external-sql-server.md](docs/ops/external-sql-server.md).

## Develop

Pinned toolchain (CI and the Dockerfile use exactly these):

| Tool | Version | Pinned in |
|---|---|---|
| .NET SDK | 10.0.402 | `global.json` |
| Node.js | 24.21.0 | `.node-version`, `web/package.json` `engines` |
| pnpm | 11.28.5 | `web/package.json` `packageManager` (enable with `corepack enable pnpm`) |

### Back end

```sh
dotnet restore Goldmark.slnx --locked-mode
dotnet build Goldmark.slnx -c Release
dotnet test --project tests/Goldmark.Api.Tests -c Release              # in-process, no Docker
dotnet test --project tests/Goldmark.Api.IntegrationTests -c Release   # needs Docker (SQL Server 2025)
dotnet format Goldmark.slnx --verify-no-changes   # lint / format check
```

The integration tests start one SQL Server 2025 container (the image pinned in
`deploy/docker-compose.yml`, with the same TLS entrypoint) and remove it
afterwards; the first run pulls about 1.5 GB.

### Database migrations

Schema changes are forward-only T-SQL scripts in `db/migrations/`
(`NNNN_lower_snake.sql`), embedded in the API and applied by the `migrate`
command in `db-init` (DbUp, one transaction per script). **Never edit a
merged script**: add a new one and record its hash, which CI checks:

```sh
sha256sum db/migrations/0003_example.sql >> db/migrations.sha256
db/check-migrations.sh origin/main
```

The app's readiness turns `503` until the database has exactly the schema
version the build expects.

To run the API locally (`dotnet run --project src/Goldmark.Api`), give it a
database password, e.g. `dotnet user-secrets --project src/Goldmark.Api set
Goldmark:Database:Password '<password>'`. Without a reachable SQL Server it
still starts; `/health/ready` reports `Unhealthy`. In Development it writes
its certificate to `src/Goldmark.Api/.goldmark/tls/` (git-ignored). Build the
web app first (below) and copy `web/dist/*` to `src/Goldmark.Api/wwwroot/` to
serve the UI from the API.

### Front end

```sh
cd web
node scripts/check-dependency-policy.mjs   # dependency policy (no install needed)
pnpm install --frozen-lockfile
pnpm run lint && pnpm run typecheck && pnpm run format:check
pnpm run build && pnpm test
```

### End-to-end smoke test

`tests/smoke/compose-smoke.sh` starts the real Compose stack with generated
secrets on port 18443, checks HTTPS, health (including readiness failure and
recovery when SQL Server stops), the UI, security headers, SQL Server Express,
least privilege and logs, runs the browser test, and tears everything down.

```sh
GOLDMARK_E2E_CHANNEL=chrome tests/smoke/compose-smoke.sh
# or: GOLDMARK_E2E_CHROMIUM=/path/to/chromium tests/smoke/compose-smoke.sh
```

### Changing dependencies

Dependencies are deliberately few and locked. Add one only when a slice uses
it, and ask first if the PRD did not anticipate it.

- **npm:** add the package to `web/dependency-allowlist.json` with a
  justification, then `pnpm add --save-exact <name>`. pnpm refuses versions
  published less than 7 days ago and never runs install scripts. Commit
  `package.json`, `pnpm-lock.yaml` and the allow-list together; CI rejects
  anything not on the allow-list and any SSR/RSC package.
- **NuGet:** add a `PackageVersion` to `Directory.Packages.props` and a
  versionless `PackageReference` to the project, then
  `dotnet restore Goldmark.slnx -p:RestoreLockedMode=false --force-evaluate`.
  Commit the updated `packages.lock.json` files. Restore fails on any package
  with a known vulnerability, or without a valid nuget.org signature.

## Repository layout

```
src/Goldmark.Api/        ASP.NET Core host: HTTPS, health, logging, SPA hosting, container commands
db/migrations/            forward-only schema migrations (DbUp) and their hash manifest
tests/Goldmark.Api.Tests/ in-process tests (xUnit v3)
tests/Goldmark.Api.IntegrationTests/ tests against a real SQL Server 2025 container
tests/smoke/              Compose smoke test (real containers + browser)
web/                      React + TypeScript + Vite SPA, browser-only; dependency policy
deploy/                   docker-compose.yml (bundled SQL Server), docker-compose.external-sql.yml,
                          .env.example, SQL Server scripts
docs/                     PRD, decisions, operations and security notes
```

Other projects from the PRD's proposed layout (`Goldmark.Core`, `.Data`,
`.Jobs`, `.Contracts`, `collectors/`, `spec/`) are added by the slices that
need them.

## Security

See [SECURITY.md](SECURITY.md) to report a vulnerability, and
[docs/security/endpoint-access.md](docs/security/endpoint-access.md) for
endpoint access policies.

## Licensing

No license has been granted yet. The licensing decision is pending.
