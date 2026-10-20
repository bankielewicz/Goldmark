#!/bin/bash
# Ensures the CONNECT-only login the container healthcheck signs in with (slice 2c, DEC-034),
# so the healthcheck never uses sa. sql-entrypoint.sh starts this in the background on every
# start, and it exits as soon as that login signs in.
#
# Its credential file lives in the private volume only this container mounts. When the file is
# missing, or its login does not sign in (for example a new data volume), this script signs in as
# sa (MSSQL_SA_PASSWORD) and creates or resets the login. The healthcheck reports healthy only
# once the login works, so db-init, which waits for a healthy container and then disables sa,
# cannot run before it. If the file is lost after sa was disabled, the container stays unhealthy
# until sa is re-enabled (docs/ops/database-recovery.md); the next start then repairs the login.
set -uo pipefail

credential=/goldmark/sql-tls-private/healthcheck.credential
sqlcmd=/opt/mssql-tools18/bin/sqlcmd
host="${GOLDMARK_SQL_TLS_HOSTNAME:-sqlserver}"
umask 077

# Letters and digits only, so nothing needs quoting; each pipeline reads all of openssl's output.
random() { local chars; chars="$(openssl rand -base64 192 | tr -dc "$1")"; printf '%s' "${chars:0:$2}"; }

if [[ -s "${credential}" ]]; then
  { read -r name; read -r password; } < "${credential}"
else
  name="goldmark_health_$(random 'a-z0-9' 10)"
  password="$(random 'A-Za-z0-9' 29)Aa1"
  # The file first: if creating the login fails, the next attempt uses the same values.
  printf '%s\n%s\n' "${name}" "${password}" > "${credential}.tmp"
  mv -f "${credential}.tmp" "${credential}"
fi

# Passwords go through SQLCMDPASSWORD and sqlcmd variables taken from the environment, never argv.
connect() { # user password query
  SQLCMDPASSWORD="$2" GOLDMARK_HEALTH_NAME="${name}" GOLDMARK_HEALTH_PASSWORD="${password}" "${sqlcmd}" \
    -S localhost -U "$1" -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F "${host}" -l 5 -t 30 -b -h -1 -Q "$3" 2>&1
}

# Identifiers and the password through QUOTENAME inside sp_executesql, as bootstrap-db does.
ensure_login="$(cat <<'SQL'
SET NOCOUNT ON;
DECLARE @name sysname = N'$(GOLDMARK_HEALTH_NAME)', @password nvarchar(128) = N'$(GOLDMARK_HEALTH_PASSWORD)', @sql nvarchar(max);
IF SUSER_ID(@name) IS NULL
    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''')
        + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
ELSE
    SET @sql = N'ALTER LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') + N';';
EXEC sys.sp_executesql @sql;
SQL
)"

deadline=$((SECONDS + 600))
while true; do
  connect "${name}" "${password}" "SET NOCOUNT ON; SELECT 1" > /dev/null && exit 0
  if out="$(connect sa "${MSSQL_SA_PASSWORD}" "${ensure_login}")"; then
    echo "goldmark: the CONNECT-only healthcheck login ${name} is ready"
    exit 0
  fi
  if [[ "${out}" == *"The account is disabled"* ]]; then
    echo "goldmark: the healthcheck login does not sign in and sa is disabled, so it cannot be repaired;" \
      "this container stays unhealthy. Recovery: docs/ops/database-recovery.md" >&2
    exit 1
  fi
  if (( SECONDS > deadline )); then
    echo "goldmark: the healthcheck login was not ready within 600 s" >&2
    exit 1
  fi
  sleep 2
done
