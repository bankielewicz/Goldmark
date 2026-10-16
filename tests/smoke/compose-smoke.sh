#!/usr/bin/env bash
# Slice 1 Compose smoke test: runs the real deployment (deploy/docker-compose.yml)
# with SQL Server 2025 Express and verifies, against live containers:
#   - Compose refuses to start without the required secrets
#   - the app fails fast (exit 2) when started without its database settings
#   - HTTPS with the generated per-installation certificate, persisted in a
#     volume (survives container re-creation); TLS < 1.2 and plain HTTP refused
#   - /health/live and /health/ready, including readiness failure and recovery
#     when SQL Server is stopped and started again, while liveness stays up
#   - the SPA shell, its hashed assets, client-route fallback and API 404s
#   - security headers (CSP with Trusted Types) on real responses
#   - SQL Server really runs as Express (EngineEdition 4) and SQL Server 2025
#   - the runtime login is least-privileged and SQL Server enforces TDS 8 strict
#   - db-init is idempotent
#   - logs are JSON and contain none of the generated secrets
#   - the SPA renders in a real browser under the served CSP (Playwright)
#
# Usage: tests/smoke/compose-smoke.sh
# Env:   GOLDMARK_SMOKE_PORT (default 18443)
#        GOLDMARK_SMOKE_NO_BUILD=1    use an existing goldmark:local image
#        GOLDMARK_SMOKE_KEEP=1        leave the stack running afterwards
#        GOLDMARK_E2E_CHANNEL=chrome  or GOLDMARK_E2E_CHROMIUM=/path/to/chromium
#        GOLDMARK_SMOKE_SKIP_BROWSER=1  skip the browser check (reported as SKIPPED)
set -uo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="${repo}/tests/smoke/.work"
port="${GOLDMARK_SMOKE_PORT:-18443}"
base="https://localhost:${port}"
project="goldmark-smoke"

rm -rf "${work}"
mkdir -p "${work}"
chmod 700 "${work}"

secret() { printf '%s-Aa9' "$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | cut -c1-28)"; }
sa_password="$(secret)"
app_password="$(secret)"
cat > "${work}/.env" <<EOF
SQLSERVER_ACCEPT_EULA=Y
SQLSERVER_SA_PASSWORD=${sa_password}
GOLDMARK_DB_PASSWORD=${app_password}
GOLDMARK_HTTPS_PORT=${port}
GOLDMARK_TLS_HOSTNAMES=localhost,127.0.0.1
EOF
chmod 600 "${work}/.env"

compose=(docker compose -p "${project}" -f "${repo}/deploy/docker-compose.yml" --env-file "${work}/.env")

results=()
failures=0
pass() { results+=("PASS  $1"); echo "PASS  $1"; }
fail() { results+=("FAIL  $1${2:+ — $2}"); echo "FAIL  $1${2:+ — $2}"; failures=$((failures + 1)); }
skip() { results+=("SKIP  $1${2:+ — $2}"); echo "SKIP  $1${2:+ — $2}"; }
check() { local name="$1"; shift; if "$@"; then pass "${name}"; else fail "${name}"; fi; }

cleanup() {
  if ((failures > 0)); then
    echo "== Container logs (last 150 lines each)"
    "${compose[@]}" logs --no-color --tail 150 2>&1
  fi
  if [[ "${GOLDMARK_SMOKE_KEEP:-}" == 1 ]]; then
    echo "Stack left running (project ${project})."
  else
    "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
  fi
}
trap cleanup EXIT

# Polls `$@` once a second until it succeeds or `timeout` seconds pass.
wait_until() {
  local timeout="$1"; shift
  local deadline=$((SECONDS + timeout))
  until "$@"; do
    ((SECONDS >= deadline)) && return 1
    sleep 1
  done
}

curl_tls() { curl --silent --show-error --max-time 10 --cacert "${work}/app-cert.pem" "$@"; }
status_of() { curl_tls -o /dev/null -w '%{http_code}' "${base}$1" 2>/dev/null; }
is_status() { [[ "$(status_of "$1")" == "$2" ]]; }
sql_as() { # user password query
  docker exec -e SQLCMDPASSWORD="$2" "${project}-sqlserver-1" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "$1" -d Goldmark -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F sqlserver \
    -l 10 -h -1 -W -b -Q "SET NOCOUNT ON; $3" 2>&1
}

echo "== Goldmark Compose smoke test (project ${project}, port ${port})"

# --- Secrets are mandatory -------------------------------------------------------
missing_output="$(docker compose -p "${project}-nosecrets" -f "${repo}/deploy/docker-compose.yml" --env-file /dev/null config 2>&1)"
if [[ $? -ne 0 && "${missing_output}" == *"SQLSERVER_ACCEPT_EULA"* ]]; then
  pass "compose refuses to run without required secrets"
else
  fail "compose refuses to run without required secrets" "${missing_output:0:200}"
fi

# --- Build and start -------------------------------------------------------------
if [[ "${GOLDMARK_SMOKE_NO_BUILD:-}" != 1 ]]; then
  if ! "${compose[@]}" build app; then
    fail "image builds"
    printf '%s\n' "${results[@]}"
    exit 1
  fi
fi

fast_fail_output="$(docker run --rm goldmark:local 2>&1)"
fast_fail_code=$?
if [[ ${fast_fail_code} -eq 2 && "${fast_fail_output}" == *"Goldmark:Database:Password is required"* ]]; then
  pass "app exits 2 with setting names when database settings are missing"
else
  fail "app exits 2 with setting names when database settings are missing" "exit ${fast_fail_code}"
fi

"${compose[@]}" up -d --no-build
app_healthy() { [[ "$(docker inspect -f '{{.State.Health.Status}}' "${project}-app-1" 2>/dev/null)" == healthy ]]; }
if wait_until 360 app_healthy; then
  pass "stack starts and the app container becomes healthy (bounded: 360 s)"
else
  fail "stack starts and the app container becomes healthy (bounded: 360 s)"
  "${compose[@]}" ps -a
  printf '%s\n' "${results[@]}"
  exit 1
fi

# --- HTTPS with the persisted, generated certificate -----------------------------
docker cp "${project}-app-1:/var/lib/goldmark/tls/goldmark.crt" "${work}/app-cert.pem" >/dev/null
fingerprint() { openssl x509 -noout -fingerprint -sha256 -in "$1" | cut -d= -f2; }
served_fingerprint() {
  openssl s_client -connect "127.0.0.1:${port}" -servername localhost </dev/null 2>/dev/null \
    | openssl x509 -noout -fingerprint -sha256 | cut -d= -f2
}
first_fp="$(fingerprint "${work}/app-cert.pem")"
check "served certificate is the generated certificate persisted in the volume" [ "$(served_fingerprint)" == "${first_fp}" ]
check "TLS validates against the generated certificate (curl --cacert, host name localhost)" is_status /health/live 200
check "TLS 1.1 is refused" bash -c "! openssl s_client -connect 127.0.0.1:${port} -tls1_1 </dev/null >/dev/null 2>&1"
check "TLS 1.2 is accepted" bash -c "openssl s_client -connect 127.0.0.1:${port} -tls1_2 </dev/null >/dev/null 2>&1"
check "plain HTTP is not served" bash -c "[[ \"\$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:${port}/health/live)\" != 200 ]]"
check "container healthcheck command succeeds (loopback HTTPS pinned to the certificate)" \
  docker exec "${project}-app-1" dotnet /app/Goldmark.Api.dll healthcheck
sql_image="$(docker inspect -f '{{.Config.Image}}' "${project}-sqlserver-1")"
# Inspect as the app's uid: the directory is 0700, so no other user can even list it.
key_mode="$(docker run --rm --network none --user 1654:1654 -v "${project}_goldmark-tls:/tls:ro" --entrypoint stat "${sql_image}" -c '%a %u' /tls/goldmark.key 2>&1)"
other_user="$(docker run --rm --network none --user 10001:10001 -v "${project}_goldmark-tls:/tls:ro" --entrypoint stat "${sql_image}" -c '%a' /tls/goldmark.key 2>&1)"
check "TLS key directory is closed to other users" bash -c "[[ '${other_user}' == *'Permission denied'* ]]"
check "TLS private key in the volume is mode 600, owned by the app user (got ${key_mode})" [ "${key_mode}" == "600 1654" ]

# --- Health endpoints ------------------------------------------------------------
check "/health/live returns 200 Healthy" bash -c "[[ \"\$(curl -s --max-time 10 --cacert '${work}/app-cert.pem' ${base}/health/live)\" == Healthy ]]"
check "/health/ready returns 200 Healthy (real query as the runtime login)" is_status /health/ready 200

# --- SPA and headers -------------------------------------------------------------
curl_tls -D "${work}/root.headers" -o "${work}/root.html" "${base}/" >/dev/null
check "/ serves the SPA shell" grep -q '<div id="root"></div>' "${work}/root.html"
check "CSP enforces Trusted Types and forbids inline script" bash -c "grep -i '^content-security-policy:' '${work}/root.headers' | grep -q \"require-trusted-types-for 'script'\" && ! grep -i '^content-security-policy:' '${work}/root.headers' | grep -q unsafe-inline"
check "X-Content-Type-Options, X-Frame-Options and Referrer-Policy are set" bash -c "grep -qi '^x-content-type-options: nosniff' '${work}/root.headers' && grep -qi '^x-frame-options: DENY' '${work}/root.headers' && grep -qi '^referrer-policy: no-referrer' '${work}/root.headers'"
check "no Server header" bash -c "! grep -qi '^server:' '${work}/root.headers'"
check "no HSTS with the self-signed certificate (auto mode)" bash -c "! grep -qi '^strict-transport-security:' '${work}/root.headers'"
asset="$(grep -o 'src="/assets/[^"]*\.js"' "${work}/root.html" | head -1 | cut -d'"' -f2)"
curl_tls -D "${work}/asset.headers" -o /dev/null "${base}${asset}" >/dev/null
check "hashed JS asset ${asset:-?} is served immutable" bash -c "grep -q ' 200' '${work}/asset.headers' && grep -qi 'cache-control: public, max-age=31536000, immutable' '${work}/asset.headers'"
check "client route /ci/{id} falls back to the SPA shell" is_status /ci/0190a7b2-0000-7000-8000-000000000000 200
check "unknown API route returns 404" is_status /api/v1/cis 404

# --- SQL Server: Express, version, least privilege, strict TLS ------------------
edition="$(sql_as sa "${sa_password}" "SELECT CONCAT(CONVERT(int, SERVERPROPERTY('EngineEdition')), '|', CONVERT(nvarchar(128), SERVERPROPERTY('Edition')), '|', CONVERT(nvarchar(16), SERVERPROPERTY('ProductMajorVersion')));")"
echo "      SQL Server: ${edition}"
check "SQL Server runs as Express (EngineEdition 4)" bash -c "[[ '${edition}' == 4\|Express* ]]"
check "SQL Server major version is 17 (SQL Server 2025)" bash -c "[[ '${edition}' == *\|17 ]]"
privileges="$(sql_as goldmark_app "${app_password}" "SELECT CONCAT(ISNULL(IS_SRVROLEMEMBER('sysadmin'),0), ISNULL(IS_MEMBER('db_owner'),0), ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE'),0), ISNULL(HAS_PERMS_BY_NAME(NULL,NULL,'CONTROL SERVER'),0));")"
check "runtime login is not sysadmin/db_owner and cannot CREATE TABLE or CONTROL SERVER (got ${privileges})" [ "${privileges}" == 0000 ]
check "runtime login is denied DDL" bash -c "! sql_out=\$(docker exec -e SQLCMDPASSWORD='${app_password}' ${project}-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U goldmark_app -d Goldmark -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F sqlserver -b -Q 'CREATE TABLE dbo.smoke_probe (x int);' 2>&1)"
check "SQL Server rejects a non-strict (TDS 7.x) encrypted client" bash -c "! docker exec -e SQLCMDPASSWORD='${app_password}' ${project}-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U goldmark_app -d Goldmark -Nm -J /goldmark/sql-tls-private/sqlserver.crt -F sqlserver -l 5 -b -Q 'SELECT 1' >/dev/null 2>&1"
rerun="$("${compose[@]}" run --rm --no-deps db-init 2>&1)"
rerun_code=$?
if [[ ${rerun_code} -eq 0 && "${rerun}" == *"verified runtime login"* && "${rerun}" != *"created database"* ]]; then
  pass "db-init is idempotent (re-run succeeds without re-creating the database)"
else
  fail "db-init is idempotent (re-run succeeds without re-creating the database)" "exit ${rerun_code}"
fi

# --- Readiness failure and recovery ----------------------------------------------
"${compose[@]}" stop sqlserver >/dev/null 2>&1
if wait_until 60 is_status /health/ready 503; then pass "readiness turns 503 after SQL Server stops (bounded: 60 s)"; else fail "readiness turns 503 after SQL Server stops (bounded: 60 s)"; fi
check "liveness stays 200 while SQL Server is down" is_status /health/live 200
check "readiness body discloses nothing while failing" bash -c "[[ \"\$(curl -s --max-time 15 --cacert '${work}/app-cert.pem' ${base}/health/ready)\" == Unhealthy ]]"
"${compose[@]}" start sqlserver >/dev/null 2>&1
if wait_until 240 is_status /health/ready 200; then pass "readiness recovers to 200 after SQL Server restarts (bounded: 240 s)"; else fail "readiness recovers to 200 after SQL Server restarts (bounded: 240 s)"; fi

# --- Certificate persists across container re-creation ---------------------------
"${compose[@]}" up -d --no-build --no-deps --force-recreate app >/dev/null 2>&1
if wait_until 120 app_healthy; then
  check "TLS certificate survives app container re-creation (same fingerprint)" [ "$(served_fingerprint)" == "${first_fp}" ]
else
  fail "re-created app container becomes healthy (bounded: 120 s)"
fi
check "app runs as non-root (uid 1654) on a read-only root filesystem" bash -c "[[ \"\$(docker inspect -f '{{.Config.User}} {{.HostConfig.ReadonlyRootfs}}' ${project}-app-1)\" == '1654 true' ]]"

# --- Logs ------------------------------------------------------------------------
"${compose[@]}" logs --no-color --no-log-prefix app > "${work}/app.log" 2>&1
"${compose[@]}" logs --no-color db-init sqlserver sql-volume-init > "${work}/other.log" 2>&1
check "app logs are JSON lines" python3 -c "
import json,sys
lines=[l for l in open('${work}/app.log') if l.strip()]
assert lines, 'no log lines'
for l in lines: json.loads(l)
"
check "no secret appears in any container log" bash -c "! grep -qF -e '${sa_password}' -e '${app_password}' '${work}/app.log' '${work}/other.log'"

# --- Browser: SPA renders under the served CSP / Trusted Types ---------------------
if [[ "${GOLDMARK_SMOKE_SKIP_BROWSER:-}" == 1 ]]; then
  skip "SPA renders in a browser under CSP + Trusted Types" "GOLDMARK_SMOKE_SKIP_BROWSER=1"
elif [[ -z "${GOLDMARK_E2E_CHANNEL:-}${GOLDMARK_E2E_CHROMIUM:-}" ]]; then
  fail "SPA renders in a browser under CSP + Trusted Types" "set GOLDMARK_E2E_CHANNEL or GOLDMARK_E2E_CHROMIUM"
else
  spki="$(openssl x509 -pubkey -noout -in "${work}/app-cert.pem" | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | base64)"
  if (cd "${repo}/web" && GOLDMARK_BASE_URL="${base}" GOLDMARK_TLS_SPKI_SHA256="${spki}" pnpm exec playwright test); then
    pass "SPA renders in a browser under CSP + Trusted Types (Playwright)"
  else
    fail "SPA renders in a browser under CSP + Trusted Types (Playwright)"
  fi
fi

echo
echo "== Summary"
printf '%s\n' "${results[@]}"
if ((failures > 0)); then
  echo "${failures} check(s) failed."
  "${compose[@]}" ps -a
  exit 1
fi
echo "All checks passed."
