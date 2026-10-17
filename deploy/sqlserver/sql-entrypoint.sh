#!/bin/bash
# Entrypoint wrapper for the bundled SQL Server Express container (DEC-014).
#
# Runs as the mssql user. On every start it ensures SQL Server has a TLS
# certificate for its Compose host name (regenerated within 30 days of expiry
# or when the host name changes), publishes only the public certificate to the
# shared volume so Goldmark can pin it (TrustServerCertificate is never used),
# requires encryption with TDS 8 strict mode for every client, and then starts
# SQL Server through the image's normal launcher. The private key never leaves
# the private volume, which only this container mounts.
set -euo pipefail

host="${GOLDMARK_SQL_TLS_HOSTNAME:-sqlserver}"
private_dir=/goldmark/sql-tls-private
public_dir=/goldmark/sql-tls-public
cert="${private_dir}/sqlserver.crt"
key="${private_dir}/sqlserver.key"

umask 077

needs_cert=false
if [[ ! -s "${cert}" || ! -s "${key}" ]]; then
  needs_cert=true
elif ! openssl x509 -checkend 2592000 -noout -in "${cert}" >/dev/null; then
  echo "goldmark: SQL Server TLS certificate expires within 30 days; regenerating"
  needs_cert=true
elif ! openssl x509 -noout -ext subjectAltName -in "${cert}" | grep -q "DNS:${host}\$"; then
  echo "goldmark: SQL Server TLS host name changed; regenerating"
  needs_cert=true
elif [[ "$(openssl x509 -noout -pubkey -in "${cert}" 2>/dev/null | openssl sha256)" != \
        "$(openssl pkey -in "${key}" -pubout 2>/dev/null | openssl sha256)" ]]; then
  # e.g. a renewal interrupted between writing the new key and the new certificate
  echo "goldmark: SQL Server TLS certificate and key do not match; regenerating"
  needs_cert=true
fi

if [[ "${needs_cert}" == true ]]; then
  echo "goldmark: generating SQL Server TLS certificate for ${host}"
  openssl req -x509 -newkey rsa:3072 -sha256 -days 397 -nodes \
    -subj "/CN=${host}" \
    -addext "subjectAltName=DNS:${host}" \
    -addext "basicConstraints=critical,CA:FALSE" \
    -addext "keyUsage=critical,digitalSignature,keyEncipherment" \
    -addext "extendedKeyUsage=serverAuth" \
    -keyout "${key}.tmp" -out "${cert}.tmp" 2>/dev/null
  mv -f "${key}.tmp" "${key}"
  mv -f "${cert}.tmp" "${cert}"
fi
chmod 600 "${key}"
chmod 644 "${cert}"
echo "goldmark: SQL Server TLS certificate SHA-256 $(openssl x509 -noout -fingerprint -sha256 -in "${cert}" | cut -d= -f2)"

# Publish the public certificate atomically for clients to pin.
install -m 644 "${cert}" "${public_dir}/sqlserver.crt.tmp"
mv -f "${public_dir}/sqlserver.crt.tmp" "${public_dir}/sqlserver.crt"

mssql_conf=/opt/mssql/bin/mssql-conf
"${mssql_conf}" set network.tlscert "${cert}" >/dev/null
"${mssql_conf}" set network.tlskey "${key}" >/dev/null
"${mssql_conf}" set network.tlsprotocols 1.2,1.3 >/dev/null
"${mssql_conf}" set network.forceencryption 1 >/dev/null
"${mssql_conf}" set network.forcestrict 1 >/dev/null

exec /opt/mssql/bin/launch_sqlservr.sh /opt/mssql/bin/sqlservr
