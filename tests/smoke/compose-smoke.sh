#!/usr/bin/env bash
# Compose smoke test (slices 1-2): runs the real deployment (deploy/docker-compose.yml)
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
#   - db-init is idempotent; migrations are applied by the migration login, which
#     is db_owner of the Goldmark database only; its password never reaches the app
#   - the runtime login cannot UPDATE/DELETE/TRUNCATE the audit log or switch off
#     system versioning; readiness requires the schema version this build expects
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
migration_password="$(secret)"
cat > "${work}/.env" <<EOF
SQLSERVER_ACCEPT_EULA=Y
SQLSERVER_SA_PASSWORD=${sa_password}
GOLDMARK_DB_PASSWORD=${app_password}
GOLDMARK_DB_MIGRATION_PASSWORD=${migration_password}
GOLDMARK_HTTPS_PORT=${port}
GOLDMARK_TLS_HOSTNAMES=localhost,127.0.0.1
EOF
chmod 600 "${work}/.env"

compose=(docker compose -p "${project}" -f "${repo}/deploy/docker-compose.yml" --env-file "${work}/.env")

results=()
failures=0
# In GitHub Actions, failures also become annotations (readable without signing in
# to view logs) and the results go to the job summary. No secret ever appears in
# the reported text: the logs that are attached were checked for secrets above.
gha_error() {
  [[ -n "${GITHUB_ACTIONS:-}" ]] || return 0
  local msg="${2//'%'/'%25'}"
  msg="${msg//$'\r'/}"
  msg="${msg//$'\n'/'%0A'}"
  echo "::error title=Smoke test::${1//[:,]/ }%0A${msg}"
}
pass() { results+=("PASS  $1"); echo "PASS  $1"; }
fail() {
  results+=("FAIL  $1${2:+ — $2}"); echo "FAIL  $1${2:+ — $2}"; failures=$((failures + 1))
  gha_error "$1" "${2:-check failed}"
}
skip() { results+=("SKIP  $1${2:+ — $2}"); echo "SKIP  $1${2:+ — $2}"; }
check() { local name="$1"; shift; if "$@"; then pass "${name}"; else fail "${name}"; fi; }

cleanup() {
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    { echo "### Compose smoke test"; echo '```'; printf '%s\n' "${results[@]}"; echo '```'; } >> "${GITHUB_STEP_SUMMARY}"
  fi
  if ((failures > 0)); then
    echo "== Container logs (last 150 lines each)"
    "${compose[@]}" logs --no-color --tail 150 > "${work}/failure.log" 2>&1
    cat "${work}/failure.log"
    gha_error "results" "$(printf '%s\n' "${results[@]}")"
    gha_error "container logs (tail)" "$(tail -n 40 "${work}/failure.log" | cut -c1-300)"
  fi
  if [[ "${GOLDMARK_SMOKE_KEEP:-}" == 1 ]]; then
    echo "Stack left running (project ${project})."
  else
    "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
  fi
}
trap 'remove_decoys 2>/dev/null; cleanup' EXIT

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
# Compose versions differ in which missing variable they report first.
if [[ $? -ne 0 && "${missing_output}" =~ required\ variable\ (SQLSERVER_ACCEPT_EULA|SQLSERVER_SA_PASSWORD|GOLDMARK_DB_PASSWORD|GOLDMARK_DB_MIGRATION_PASSWORD)\ is\ missing ]]; then
  pass "compose refuses to run without required secrets"
else
  fail "compose refuses to run without required secrets" "${missing_output:0:200}"
fi

# --- Build context never contains git-ignored secrets ------------------------------
decoys=(
  "src/Goldmark.Api/appsettings.Production.local.json"
  "src/Goldmark.Api/secrets.json"
  "src/Goldmark.Api/smoke-decoy.p12"
  "src/Goldmark.Api/smoke-decoy.pfx"
  "src/Goldmark.Api/smoke-decoy.key"
  "src/Goldmark.Api/.goldmark/smoke-decoy.key"
  "web/.env"
  "web/smoke-decoy.pem"
)
remove_decoys() { for d in "${decoys[@]}"; do rm -f "${repo}/${d}"; done; rmdir "${repo}/src/Goldmark.Api/.goldmark" 2>/dev/null || true; }
for d in "${decoys[@]}"; do
  if [[ -e "${repo}/${d}" ]]; then echo "refusing to overwrite existing ${d}"; exit 1; fi
done
for d in "${decoys[@]}"; do mkdir -p "$(dirname "${repo}/${d}")"; echo "goldmark-smoke-decoy" > "${repo}/${d}"; done
context_dir="${work}/build-context"
printf 'FROM scratch\nCOPY . /\n' > "${work}/Dockerfile.context"
if docker build -q -f "${work}/Dockerfile.context" --output "type=local,dest=${context_dir}" "${repo}" >/dev/null 2>&1; then
  leaked="$(cd "${context_dir}" && for d in "${decoys[@]}"; do [[ -e "${d}" ]] && echo "${d}"; done)"
  if [[ -z "${leaked}" && -f "${context_dir}/src/Goldmark.Api/Program.cs" ]]; then
    pass "Docker build context excludes git-ignored secret files (${#decoys[@]} decoys)"
  else
    fail "Docker build context excludes git-ignored secret files" "leaked: ${leaked:-context export incomplete}"
  fi
else
  fail "Docker build context excludes git-ignored secret files" "context export failed"
fi
remove_decoys
rm -rf "${context_dir}"

# --- Build and start -------------------------------------------------------------
if [[ "${GOLDMARK_SMOKE_NO_BUILD:-}" != 1 ]]; then
  if ! docker compose --progress plain -p "${project}" -f "${repo}/deploy/docker-compose.yml" --env-file "${work}/.env" build app > "${work}/build.log" 2>&1; then
    cat "${work}/build.log"
    fail "image builds" "$(tail -n 40 "${work}/build.log" | cut -c1-300)"
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

# A value of the wrong type names the setting, never the value (it may be a secret). Exit 2,
# except healthcheck: Docker reserves 2 for HEALTHCHECK commands, so it exits 1.
typed_value="smoke-NEVER-ECHO-${RANDOM}"
for spec in ":2" "bootstrap-db:2" "healthcheck:1"; do
  command="${spec%%:*}" expected="${spec##*:}"
  typed_output="$(docker run --rm -e Goldmark__Https__Port="${typed_value}" goldmark:local ${command} 2>&1)"
  typed_code=$?
  if [[ ${typed_code} -eq ${expected} && "${typed_output}" == *"Goldmark:Https:Port has a value that is not valid"* && "${typed_output}" != *"${typed_value}"* ]]; then
    pass "a wrongly typed setting is exit ${expected} naming the setting, not the value (${command:-app})"
  else
    fail "a wrongly typed setting is exit ${expected} naming the setting, not the value (${command:-app})" "exit ${typed_code}"
  fi
done

"${compose[@]}" up -d --no-build
app_healthy() { [[ "$(docker inspect -f '{{.State.Health.Status}}' "${project}-app-1" 2>/dev/null)" == healthy ]]; }
# Stop waiting early if a one-shot dependency already failed.
init_failed() {
  local c
  for c in sql-volume-init db-init; do
    [[ "$(docker inspect -f '{{.State.Status}} {{.State.ExitCode}}' "${project}-${c}-1" 2>/dev/null)" =~ ^exited\ [1-9] ]] && return 0
  done
  return 1
}
app_healthy_or_init_failed() { app_healthy || init_failed; }
if wait_until 360 app_healthy_or_init_failed && app_healthy; then
  pass "stack starts and the app container becomes healthy (bounded: 360 s)"
else
  fail "stack starts and the app container becomes healthy (bounded: 360 s)" "$("${compose[@]}" ps -a 2>&1)"
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
# Inspect as the app's uid; then prove another uid cannot read the key.
key_mode="$(docker run --rm --network none --user 1654:1654 -v "${project}_goldmark-tls:/tls:ro" --entrypoint stat "${sql_image}" -c '%a %u' /tls /tls/goldmark.key 2>&1 | tr '\n' ' ' | sed 's/ $//')"
other_read="$(docker run --rm --network none --user 10001:10001 -v "${project}_goldmark-tls:/tls:ro" --entrypoint cat "${sql_image}" /tls/goldmark.key 2>&1)"
other_code=$?
if [[ ${other_code} -ne 0 && "${other_read}" != *"PRIVATE KEY"* ]]; then
  pass "another uid cannot read the TLS private key"
else
  fail "another uid cannot read the TLS private key" "cat exited ${other_code}"
fi
if [[ "${key_mode}" == "700 1654 600 1654" ]]; then
  pass "TLS key dir 700 and key 600, both owned by the app user"
else
  fail "TLS key dir 700 and key 600, both owned by the app user" "got: ${key_mode}"
fi

# --- Health endpoints ------------------------------------------------------------
check "/health/live returns 200 Healthy" bash -c "[[ \"\$(curl -s --max-time 10 --cacert '${work}/app-cert.pem' ${base}/health/live)\" == Healthy ]]"
check "/health/ready returns 200 Healthy (real query as the runtime login)" is_status /health/ready 200
check "a mistyped health path is 404, not the SPA shell" is_status /health/readiness 404

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
if [[ ${rerun_code} -eq 0 && "${rerun}" == *"verified runtime login"* && "${rerun}" != *"created database"* \
      && "${rerun}" == *"migrate: no migrations to apply"* ]]; then
  pass "db-init is idempotent (re-run creates nothing and applies no migration)"
else
  fail "db-init is idempotent (re-run creates nothing and applies no migration)" "exit ${rerun_code}"
fi

# --- Slice 2: migrations, migration login, runtime role --------------------------
db_init() { "${compose[@]}" run --rm --no-deps "$@" db-init 2>&1; }
expected_scripts="$(wc -l < "${repo}/db/migrations.sha256" | tr -d ' ')"
applied_scripts="$(sql_as sa "${sa_password}" "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName LIKE N'Goldmark.Migrations.%';")"
check "db-init applied every migration of this build (${applied_scripts:-?}/${expected_scripts})" [ "${applied_scripts}" == "${expected_scripts}" ]
app_env_clean() {
  ! docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "${project}-app-1" \
    | grep -qF -e "${migration_password}" -e "${sa_password}" -e 'Goldmark__Migration__' -e 'Goldmark__Bootstrap__'
}
check "the app container never receives the migration or sa credentials" app_env_clean
migrator_flags="$(sql_as goldmark_migrator "${migration_password}" "SELECT CONCAT(IS_MEMBER('db_owner'), IS_SRVROLEMEMBER('sysadmin'), (SELECT COUNT(*) FROM sys.login_token WHERE type = N'SERVER ROLE' AND name <> N'public'));")"
check "the migration login is db_owner of Goldmark, not sysadmin, no server roles (got ${migrator_flags})" [ "${migrator_flags}" == 100 ]
runtime_denied() { ! sql_as goldmark_app "${app_password}" "$1" >/dev/null; }
check "runtime login cannot UPDATE the audit log" runtime_denied "UPDATE cmdb.AuditLog SET Action = 'x';"
check "runtime login cannot DELETE from the audit log" runtime_denied "DELETE FROM cmdb.AuditLog;"
check "runtime login cannot TRUNCATE the audit log" runtime_denied "TRUNCATE TABLE cmdb.AuditLog;"
check "runtime login cannot switch off system versioning" runtime_denied "ALTER TABLE cmdb.Ci SET (SYSTEM_VERSIONING = OFF);"
appended="$(sql_as goldmark_app "${app_password}" "INSERT INTO cmdb.AuditLog (ActorType, Action) VALUES ('System', 'smoke.append'); SELECT COUNT(*) FROM cmdb.AuditLog WHERE Action = 'smoke.append';")"
check "runtime login can append to the audit log" [ "${appended}" == 1 ]
same_password="$(db_init -e Goldmark__Migration__Password="${app_password}")"; same_code=$?
if [[ ${same_code} -eq 2 && "${same_password}" == *"Goldmark:Migration:Password must differ from Goldmark:Database:Password"* ]]; then
  pass "db-init refuses a migration password equal to the runtime password (exit 2)"
else
  fail "db-init refuses a migration password equal to the runtime password (exit 2)" "exit ${same_code}"
fi

# Readiness requires exactly this build's schema version: a newer one (as after a newer
# build migrated the database) is not ready, and both versions are logged.
sql_as sa "${sa_password}" "INSERT INTO dbo.SchemaVersions (ScriptName, Applied) VALUES (N'Goldmark.Migrations.9999_smoke_future.sql', SYSUTCDATETIME());" >/dev/null
if wait_until 30 is_status /health/ready 503; then
  pass "readiness turns 503 when the database schema is newer than this build"
else
  fail "readiness turns 503 when the database schema is newer than this build"
fi
mismatch_logged() {
  "${compose[@]}" logs --no-color --no-log-prefix app 2>/dev/null \
    | grep -F '"DatabaseSchemaVersion":9999' | grep -qF "\"ExpectedSchemaVersion\":${expected_scripts}"
}
check "the schema mismatch is logged with both versions (9999 vs ${expected_scripts})" mismatch_logged
sql_as sa "${sa_password}" "DELETE FROM dbo.SchemaVersions WHERE ScriptName = N'Goldmark.Migrations.9999_smoke_future.sql';" >/dev/null
if wait_until 30 is_status /health/ready 200; then
  pass "readiness recovers once the schema version matches again"
else
  fail "readiness recovers once the schema version matches again"
fi

# --- bootstrap-db: only principals Goldmark created are managed ----------------------
login_password_works() { # login password: can it authenticate (to master, where every login can connect)?
  [[ "$(docker exec -e SQLCMDPASSWORD="$2" "${project}-sqlserver-1" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "$1" -d master -Ns -J /goldmark/sql-tls-private/sqlserver.crt -F sqlserver \
    -l 10 -h -1 -W -b -Q "SET NOCOUNT ON; SELECT 42;" 2>&1)" == 42 ]]
}
other_password="$(secret)"

marker="$(sql_as sa "${sa_password}" "SELECT COUNT(*) FROM sys.extended_properties WHERE class = 4 AND major_id = USER_ID('goldmark_app') AND name = N'goldmark:managed';")"
check "the runtime database user carries the goldmark:managed marker" [ "${marker}" == 1 ]

rotated="$(db_init -e Goldmark__Database__Password="${other_password}")"; rotated_code=$?
restored="$(db_init)"; restored_code=$?
if [[ ${rotated_code} -eq 0 && "${rotated}" == *"changed login password"* && ${restored_code} -eq 0 ]] \
   && login_password_works goldmark_app "${app_password}"; then
  pass "changing GOLDMARK_DB_PASSWORD rotates the managed login (rotated and back)"
else
  fail "changing GOLDMARK_DB_PASSWORD rotates the managed login" "exit ${rotated_code}/${restored_code}"
fi

# A login that exists but was not created by Goldmark: refused, nothing changed, no database created.
sql_as sa "${sa_password}" "USE master; CREATE LOGIN smoke_foreign WITH PASSWORD = N'${other_password}';" >/dev/null
refused="$(db_init -e Goldmark__Database__User=smoke_foreign -e Goldmark__Database__Name=GoldmarkForeignProbe -e Goldmark__Database__Password="${app_password}")"; refused_code=$?
foreign_state="$(sql_as sa "${sa_password}" "SELECT CASE WHEN DB_ID('GoldmarkForeignProbe') IS NULL THEN 'no-db' ELSE 'db' END;")"
if [[ ${refused_code} -eq 3 && "${refused}" == *"was not created by Goldmark"* && "${foreign_state}" == no-db ]] \
   && login_password_works smoke_foreign "${other_password}"; then
  pass "an existing login not created by Goldmark is refused with nothing changed (password, database)"
else
  fail "an existing login not created by Goldmark is refused with nothing changed" "exit ${refused_code}, ${foreign_state}"
fi

# Opt-in adoption takes it over: password set, user created and marked.
adopted="$(db_init -e Goldmark__Database__User=smoke_foreign -e Goldmark__Database__Password="${app_password}" -e Goldmark__Bootstrap__AdoptExistingLogin=true)"; adopted_code=$?
adopted_marker="$(sql_as sa "${sa_password}" "SELECT COUNT(*) FROM sys.extended_properties WHERE class = 4 AND major_id = USER_ID('smoke_foreign') AND name = N'goldmark:managed';")"
sql_as sa "${sa_password}" "DROP USER smoke_foreign; USE master; DROP LOGIN smoke_foreign;" >/dev/null
if [[ ${adopted_code} -eq 0 && "${adopted}" == *"adopting the existing login"* && "${adopted_marker}" == 1 ]]; then
  pass "GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true adopts and marks an existing login"
else
  fail "GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true adopts and marks an existing login" "exit ${adopted_code}"
fi

# A database user with the login's name that is not Goldmark's: refused before the login is created.
sql_as sa "${sa_password}" "CREATE USER smoke_collision WITHOUT LOGIN;" >/dev/null
refused="$(db_init -e Goldmark__Database__User=smoke_collision -e Goldmark__Database__Password="${other_password}")"; refused_code=$?
probe_login="$(sql_as sa "${sa_password}" "SELECT CASE WHEN SUSER_ID('smoke_collision') IS NULL THEN 'absent' ELSE 'present' END;")"
sql_as sa "${sa_password}" "DROP USER smoke_collision;" >/dev/null
if [[ ${refused_code} -eq 3 && "${refused}" == *"database user 'smoke_collision' already exists"* && "${probe_login}" == absent ]]; then
  pass "an existing database user not created by Goldmark is refused before the login is created"
else
  fail "an existing database user not created by Goldmark is refused" "exit ${refused_code}, login ${probe_login}"
fi

# A password SQL Server's policy rejects fails before any database, login or user exists.
# Fresh names for both logins, so the policy check (not an existing login) is what fails.
weak="$(db_init -e Goldmark__Database__User=smoke_weak -e Goldmark__Database__Name=GoldmarkWeakProbe -e Goldmark__Database__Password=aaaaaaaaaaaaaaaa \
  -e Goldmark__Migration__User=smoke_weak_migrator)"; weak_code=$?
weak_state="$(sql_as sa "${sa_password}" "SELECT CONCAT(CASE WHEN DB_ID('GoldmarkWeakProbe') IS NULL THEN 'no-db' ELSE 'db' END, ',', CASE WHEN SUSER_ID('smoke_weak') IS NULL AND SUSER_ID('smoke_weak_migrator') IS NULL THEN 'no-login' ELSE 'login' END);")"
if [[ ${weak_code} -eq 1 && "${weak_state}" == "no-db,no-login" ]]; then
  pass "a password rejected by SQL Server policy leaves no database or login behind"
else
  fail "a password rejected by SQL Server policy leaves no database or login behind" "exit ${weak_code}, ${weak_state}"
fi

# The post-provisioning effective-permission check still refuses a privileged runtime login.
sql_as sa "${sa_password}" "ALTER SERVER ROLE securityadmin ADD MEMBER goldmark_app; ALTER ROLE db_securityadmin ADD MEMBER goldmark_app;" >/dev/null
refused="$(db_init)"; refused_code=$?
sql_as sa "${sa_password}" "ALTER SERVER ROLE securityadmin DROP MEMBER goldmark_app; ALTER ROLE db_securityadmin DROP MEMBER goldmark_app;" >/dev/null
if [[ ${refused_code} -eq 3 && "${refused}" == *"server role securityadmin"* && "${refused}" == *"database role db_securityadmin"* ]]; then
  pass "the effective-permission check refuses a runtime login holding securityadmin/db_securityadmin"
else
  fail "the effective-permission check refuses a privileged runtime login" "exit ${refused_code}"
fi

accepted="$(db_init)"
if [[ $? -eq 0 && "${accepted}" == *"verified runtime login"* ]]; then
  pass "db-init accepts the login again once the extra roles are removed"
else
  fail "db-init accepts the login again once the extra roles are removed"
fi

# --- Readiness failure and recovery ----------------------------------------------
"${compose[@]}" stop sqlserver >/dev/null 2>&1
if wait_until 60 is_status /health/ready 503; then pass "readiness turns 503 after SQL Server stops (bounded: 60 s)"; else fail "readiness turns 503 after SQL Server stops (bounded: 60 s)"; fi
check "liveness stays 200 while SQL Server is down" is_status /health/live 200
check "readiness body discloses nothing while failing" bash -c "[[ \"\$(curl -s --max-time 15 --cacert '${work}/app-cert.pem' ${base}/health/ready)\" == Unhealthy ]]"
"${compose[@]}" start sqlserver >/dev/null 2>&1
if wait_until 240 is_status /health/ready 200; then pass "readiness recovers to 200 after SQL Server restarts (bounded: 240 s)"; else fail "readiness recovers to 200 after SQL Server restarts (bounded: 240 s)"; fi

# --- SQL Server certificate: an interrupted renewal (new key, old certificate) heals --
old_sql_fp="$(docker exec "${project}-sqlserver-1" openssl x509 -noout -fingerprint -sha256 -in /goldmark/sql-tls-private/sqlserver.crt | cut -d= -f2)"
docker exec "${project}-sqlserver-1" bash -c 'umask 077; openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out /goldmark/sql-tls-private/sqlserver.key 2>/dev/null'
"${compose[@]}" restart sqlserver >/dev/null 2>&1
sql_healthy() { [[ "$(docker inspect -f '{{.State.Health.Status}}' "${project}-sqlserver-1" 2>/dev/null)" == healthy ]]; }
if wait_until 240 sql_healthy; then
  new_sql_fp="$(docker exec "${project}-sqlserver-1" openssl x509 -noout -fingerprint -sha256 -in /goldmark/sql-tls-private/sqlserver.crt | cut -d= -f2)"
  check "SQL Server regenerates a mismatched certificate/key pair and starts healthy" [ "${new_sql_fp}" != "${old_sql_fp}" ]
  if wait_until 120 is_status /health/ready 200; then
    pass "readiness recovers with the regenerated SQL certificate (pin re-read from the shared volume)"
  else
    fail "readiness recovers with the regenerated SQL certificate (pin re-read from the shared volume)"
  fi
else
  fail "SQL Server regenerates a mismatched certificate/key pair and starts healthy" "not healthy within 240 s"
fi

# --- Certificate persists across container re-creation ---------------------------
"${compose[@]}" up -d --no-build --no-deps --force-recreate app >/dev/null 2>&1
if wait_until 120 app_healthy; then
  check "TLS certificate survives app container re-creation (same fingerprint)" [ "$(served_fingerprint)" == "${first_fp}" ]
else
  fail "re-created app container becomes healthy (bounded: 120 s)"
fi
check "app runs as non-root (uid 1654) on a read-only root filesystem" bash -c "[[ \"\$(docker inspect -f '{{.Config.User}} {{.HostConfig.ReadonlyRootfs}}' ${project}-app-1)\" == '1654 true' ]]"

# --- Logs ------------------------------------------------------------------------
"${compose[@]}" logs --no-color --no-log-prefix app > "${work}/app.log" 2> "${work}/compose-stderr.log"
"${compose[@]}" logs --no-color db-init sqlserver sql-volume-init > "${work}/other.log" 2>&1
json_result="$(python3 - "${work}/app.log" <<'PY'
import json, sys
lines = [l.rstrip("\n") for l in open(sys.argv[1]) if l.strip()]
if not lines:
    print("no log lines")
for n, line in enumerate(lines, 1):
    try:
        json.loads(line)
    except ValueError:
        print(f"line {n} is not JSON: {line[:200]}")
        break
PY
)"
if [[ -z "${json_result}" ]]; then pass "app logs are JSON lines"; else fail "app logs are JSON lines" "${json_result}"; fi
check "no secret appears in any container log" bash -c "! grep -qF -e '${sa_password}' -e '${app_password}' -e '${migration_password}' -e '${other_password}' '${work}/app.log' '${work}/other.log' '${work}/compose-stderr.log'"

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
