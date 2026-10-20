#!/bin/bash
# Docker HEALTHCHECK for the bundled SQL Server: a real query over a TDS 8 strict-encrypted
# loopback connection, validating the server certificate by pinning it (no -C /
# TrustServerCertificate). It signs in with the CONNECT-only login health-login.sh creates,
# never as sa (DEC-034); until that login exists the container is not healthy.
# The password goes through SQLCMDPASSWORD so it is not visible in the process list.
set -euo pipefail
credential=/goldmark/sql-tls-private/healthcheck.credential
[[ -s "${credential}" ]] || exit 1
{ read -r user; read -r password; } < "${credential}"
SQLCMDPASSWORD="${password}" exec /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U "${user}" \
  -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F "${GOLDMARK_SQL_TLS_HOSTNAME:-sqlserver}" \
  -l 5 -t 5 -b -h -1 -Q "SET NOCOUNT ON; SELECT 1" -o /dev/null
