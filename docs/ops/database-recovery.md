# Recovering lost database credentials

With the bundled SQL Server, `sa` is disabled after the first start, and the only other
`sysadmin` is the generated administrator in the `goldmark-db-admin` volume
([database-credentials.md](database-credentials.md)). On Linux there is no local
administrator to fall back on, so this page is how you get back in. The procedure is
tested end to end by the Compose smoke test (`tests/smoke/compose-smoke.sh`).

Commands assume the repository root and the default project name `goldmark`
(volumes are named `goldmark_<volume>`). With another project name (`-p`), adjust the
volume names.

## What the recovery relies on (measured, SQL Server 2025 image)

- A disabled `sa` stays disabled across restarts, and a changed `MSSQL_SA_PASSWORD` is
  ignored after the first start.
- With SQL Server **stopped**, `mssql-conf -n set-sa-password`, run against its data
  volume, sets `sa`'s password to `MSSQL_SA_PASSWORD` **and re-enables `sa`**. After
  the next start, `sa` signs in.

## The administrator's credential is lost or no longer works

Symptoms: `db-init` exits 1 with `SQL Server error 18470` (sa is disabled and
`admin.json` is missing) or `18456` (the generated administrator in `admin.json`
cannot sign in), and names this page. The app does not start.

```sh
compose="docker compose -f deploy/docker-compose.yml"
$compose stop
# Only if admin.json still exists but no longer signs in: remove its volume. It also holds
# the migration login's file, which db-init regenerates (same login, new password).
$compose rm -f db-init
docker volume rm goldmark_goldmark-db-admin
# Reset and re-enable sa with SQLSERVER_SA_PASSWORD from deploy/.env. SQL Server must be
# stopped: never run two SQL Server processes on one data volume.
$compose run --rm --no-deps --entrypoint /opt/mssql/bin/mssql-conf sqlserver -n set-sa-password
$compose up -d
```

`db-init` then signs in as `sa`, finds the generated administrator (by its SID on the
Goldmark database), gives it a new password, writes `admin.json` and disables `sa`
again. Its log says `reset the password of the generated administrator '…'` and
`disabled sa`. Back up the new `goldmark-db-admin` volume.

If you no longer know `SQLSERVER_SA_PASSWORD`, set a new one in `deploy/.env` first:
`mssql-conf` uses the value in the file.

## The runtime login's credential is lost

`goldmark-db-runtime` was removed or emptied. `db-init` can recover it on its own:

```sh
compose="docker compose -f deploy/docker-compose.yml"
$compose stop app db-init
$compose rm -f app db-init
docker volume rm goldmark_goldmark-db-runtime   # if it still exists
$compose up -d
```

`db-init` keeps the runtime login's name, gives it a new password and writes the file;
the app starts with it.

## The SQL Server healthcheck's credential is lost

The `sql-tls-private` volume was lost (it also holds SQL Server's TLS key, which is
regenerated). With `sa` disabled, the container cannot recreate its healthcheck login
and stays unhealthy; its log says so and names this page. Run the administrator
procedure above without removing `goldmark-db-admin`: once `sa` is re-enabled, the
SQL Server container repairs the healthcheck login, and `db-init` disables `sa` again.

## Last resort

If `mssql-conf` cannot help, `sqlservr --force-setup` rebuilds the system databases:
every login is lost, but the Goldmark database's files survive and can be attached
again (`CREATE DATABASE … FOR ATTACH`), with its users left without logins (measured on
the pinned image). Goldmark has no automation for this; restore from a backup instead
where you can (backups: US-60, slice 24).
