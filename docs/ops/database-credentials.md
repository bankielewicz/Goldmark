# Database credentials

Goldmark generates its own SQL Server logins instead of taking passwords from
`deploy/.env` (slice 2c, DEC-034). This page explains what is generated, where it is
kept and who can read it. Losing a credential: [database-recovery.md](database-recovery.md).
An existing SQL Server: [external-sql-server.md](external-sql-server.md).

## What `db-init` generates

| Login | Rights | Credential file | Volume (bundled Compose file) | Mounted by |
|---|---|---|---|---|
| Runtime, `goldmark_app_<suffix>` | DML on Goldmark's tables through `goldmark_runtime` (DEC-025) | `credential.json` | `goldmark-db-runtime` | `db-init` (read-write), `app` (read-only) |
| Migration, `goldmark_migrator_<suffix>` | `db_owner` of the Goldmark database only (DEC-024) | `migration.json` | `goldmark-db-admin` | `db-init` only |
| Administrator, `goldmark_admin_<suffix>` (bundled SQL Server only) | `sysadmin` | `admin.json` | `goldmark-db-admin` | `db-init` only |

- **Names:** `goldmark_`, the purpose, `_` and 10 random lowercase letters and digits,
  for example `goldmark_app_k7q2m9xz4p`. A DBA can see what each login is for; nobody
  can guess it the way people guess `sa`.
- **Passwords:** 32 random letters and digits, with at least one upper-case letter,
  lower-case letter and digit (SQL Server's password policy, `CHECK_POLICY = ON`,
  `CHECK_EXPIRATION = OFF`).
- **Files:** JSON, `{"user": "...", "password": "..."}`, mode `0600` in a `0700`
  directory owned by the app user (uid 1654), written atomically. The paths are
  settings (`Goldmark:Database:CredentialFile`, `Goldmark:Migration:CredentialFile`,
  `Goldmark:Bootstrap:AdminCredentialFile`), so another deployment can keep them
  elsewhere: on the Windows service the password is encrypted to the service account (DEC-043).
- **The app** reads its login from `credential.json` at start and never receives a
  password in its environment. If the file is missing or invalid, it exits 2 naming
  `Goldmark:Database:CredentialFile`.

`db-init` writes a credential file **before** it changes the login, so a run that
stops halfway leaves a file the next run applies. The administrator is the exception:
it is created first and its file written afterwards, because `db-init` never falls
back to `sa` while that file exists.

## `sa` (bundled SQL Server)

- On the **first run** `db-init` signs in as `sa` (`SQLSERVER_SA_PASSWORD`), does its
  work, creates the generated administrator, writes `admin.json`, signs in with it and
  **disables `sa`**.
- **Later runs** sign in with `admin.json` and never try `sa`. Each run disables `sa`
  again if something re-enabled it.
- The Goldmark database is owned by `sa` (disabled), not by the generated
  administrator, so a recovery that replaces the administrator cannot orphan the owner.
- **`SQLSERVER_SA_PASSWORD` stays in `deploy/.env`.** The SQL Server image needs
  `MSSQL_SA_PASSWORD` on every start, even with `sa` disabled; the recovery uses it
  too. `db-init` receives it on every run but uses it only while `admin.json` does not
  exist. The app never receives it.
- `bootstrap-db` refuses `sa` as the administrator in any other setup (exit 2).

The generated administrator's name is recorded on the Goldmark database (extended
property `goldmark:admin-login`, the login's SID). A recovery reuses that login only if
it is still a SQL login with the `goldmark_admin_` prefix that already holds
`sysadmin`; otherwise it creates a new one. `sysadmin` is never added to a login that
already exists, because the migration login (`db_owner`) could change the property.

## The SQL Server container's healthcheck

The healthcheck signs in with its own CONNECT-only login,
`goldmark_health_<suffix>`, never with `sa`. The SQL Server container creates it
itself (`deploy/sqlserver/health-login.sh`) as soon as SQL Server accepts connections
on the first start, signed in as `sa`, and keeps its credential in the volume only that
container mounts (`sql-tls-private`, file `healthcheck.credential`). The container
reports healthy only once that login works, so `db-init` (which waits for a healthy
SQL Server and then disables `sa`) cannot run before it exists.

## Rotation

Credentials are not rotated automatically (owner decision 3A): `docker compose up`
reruns `db-init`, and the app reads its file only when it starts, so rotating on every
run would break the app's new connections until it restarted. A password changes when:

- **a credential file is lost:** the next `db-init` keeps the login's name and gives it
  a new password (the login is found by Goldmark's marker on its database user);
- **a password is given in settings** (`GOLDMARK_DB_USER` and `GOLDMARK_DB_PASSWORD`,
  or the migration pair, with the external-SQL file): `db-init` sets it and rewrites the
  file. Restart the app afterwards (`docker compose … up -d --force-recreate app`).

- **you rotate them** (slice 4b, DEC-043 item 7): `rotate-db-credentials` gives every login Goldmark
  generated (runtime, migration, the bundled administrator) a new password in one run, each file before
  its login, and audits it as System ("Database passwords changed"). A password given in settings is
  yours to change there. Then restart the app, which reads its file only at start:

  ```sh
  docker compose -f deploy/docker-compose.yml run --rm db-init rotate-db-credentials
  docker compose -f deploy/docker-compose.yml restart app
  ```

  If it stops part-way, run it again, or let the next `db-init` apply the files it already wrote.
  The administrator's new password waits in `admin.json.new` until its login has changed; if a run stopped
  between the two, the next `rotate-db-credentials` or `db-init` finds that the login takes the waiting
  password and moves it into place. Only one rotation runs at a time: a second one is refused with
  nothing changed. On Windows:
  `Rotate-GoldmarkCredentials.ps1` ([windows-service.md](windows-service.md)).

## Upgrading a stack created before generated credentials

A bundled stack from before slice 2c has `goldmark_app` and `goldmark_migrator` with
passwords from `deploy/.env`, and `sa` enabled. After the upgrade, the first `db-init`
finds both logins by their marker, keeps their names, gives them generated passwords,
writes the files, creates the administrator and disables `sa` (owner decision 1A).
Delete `GOLDMARK_DB_USER`, `GOLDMARK_DB_PASSWORD`, `GOLDMARK_DB_MIGRATION_USER` and
`GOLDMARK_DB_MIGRATION_PASSWORD` from `deploy/.env`: the bundled file no longer reads
them.

## Who can read the credentials

Anyone who can read Docker's volumes: root and members of the `docker` group on the
host, who can already read the database files themselves. Protect the host
accordingly (NFR-SEC-6). Back up `goldmark-db-admin` with the database: without it,
the recovery in [database-recovery.md](database-recovery.md) is needed.
