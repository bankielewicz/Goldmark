#!/usr/bin/env bash
# Self-test for db/check-migrations.sh: each tampering case must fail, each legal change pass.
# Runs in a throw-away git repository; never touches the real one.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT
failures=0

setup() {
  rm -rf "${scratch}/r" && mkdir -p "${scratch}/r/db/migrations"
  cd "${scratch}/r"
  cp "${repo}/db/check-migrations.sh" db/
  printf 'CREATE TABLE a (x int);\n' > db/migrations/0001_first.sql
  printf 'CREATE TABLE b (x int);\n' > db/migrations/0002_second.sql
  sha256sum db/migrations/*.sql > db/migrations.sha256
  git init -q . && git -c user.name=t -c user.email=t@t add -A && git -c user.name=t -c user.email=t@t commit -qm base
}

expect() { # pass|fail description command...
  local want="$1" what="$2"; shift 2
  if "$@" >/dev/null 2>&1; then got=pass; else got=fail; fi
  if [[ "${got}" == "${want}" ]]; then echo "ok    ${what} (${got})"; else echo "WRONG ${what}: expected ${want}, got ${got}"; failures=$((failures + 1)); fi
}

setup; expect pass "unchanged scripts" bash db/check-migrations.sh HEAD

setup; printf 'CREATE TABLE c (x int);\n' > db/migrations/0003_third.sql; sha256sum db/migrations/0003_third.sql >> db/migrations.sha256
expect pass "a new migration appended" bash db/check-migrations.sh HEAD

setup; printf -- '-- edited\n' >> db/migrations/0001_first.sql
expect fail "an applied script edited" bash db/check-migrations.sh HEAD

setup; printf -- '-- edited\n' >> db/migrations/0001_first.sql; sha256sum db/migrations/*.sql > db/migrations.sha256
expect fail "an applied script edited together with its manifest line" bash db/check-migrations.sh HEAD
expect pass "  (the same edit without a base passes the local checks)" bash db/check-migrations.sh

setup; git mv db/migrations/0002_second.sql db/migrations/0002_renamed.sql; sha256sum db/migrations/*.sql > db/migrations.sha256
expect fail "an applied script renamed" bash db/check-migrations.sh HEAD

setup; rm db/migrations/0002_second.sql; sha256sum db/migrations/*.sql > db/migrations.sha256
expect fail "an applied script removed" bash db/check-migrations.sh HEAD

setup; printf 'x\n' > db/migrations/0003_unlisted.sql
expect fail "a script not in the manifest" bash db/check-migrations.sh HEAD

setup; printf 'x\n' > db/migrations/0004_gap.sql; sha256sum db/migrations/0004_gap.sql >> db/migrations.sha256
expect fail "a gap in the numbering" bash db/check-migrations.sh HEAD

setup; printf 'x\n' > db/migrations/0003_Bad-Name.sql; sha256sum db/migrations/0003_Bad-Name.sql >> db/migrations.sha256
expect fail "a badly named script" bash db/check-migrations.sh HEAD

setup; sed -i '1d' db/migrations.sha256
expect fail "a manifest line removed" bash db/check-migrations.sh HEAD

setup; git rm -q db/migrations.sha256 && git -c user.name=t -c user.email=t@t commit -qm 'no manifest'; sha256sum db/migrations/*.sql > db/migrations.sha256
expect pass "a base without a manifest (first introduction)" bash db/check-migrations.sh HEAD

if ((failures > 0)); then echo "${failures} case(s) wrong"; exit 1; fi
echo "All check-migrations cases behave as expected."
