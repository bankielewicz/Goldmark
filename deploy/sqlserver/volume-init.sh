#!/bin/bash
# One-shot volume preparation for the bundled SQL Server (DEC-014).
#
# Fresh Docker volumes are owned by root, but SQL Server runs as the mssql user
# (uid 10001). This container runs as root with no network and only CAP_CHOWN,
# and hands the two TLS volumes to mssql on first use. It never touches key
# material; sql-entrypoint.sh creates the certificate as the mssql user.
set -euo pipefail

mssql_uid=10001
prepare() { # directory mode
  if [[ "$(stat -c %u "$1")" != "${mssql_uid}" ]]; then
    chmod "$2" "$1"   # before chown: afterwards root has no CAP_FOWNER
    chown "${mssql_uid}:${mssql_uid}" "$1"
    echo "sql-volume-init: prepared $1"
  fi
}

prepare /goldmark/sql-tls-private 700
prepare /goldmark/sql-tls-public 755
