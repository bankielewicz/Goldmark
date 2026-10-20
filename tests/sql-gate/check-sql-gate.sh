#!/usr/bin/env bash
# NFR-SEC-3 build gate self-test (DEC-040): SQL handed to DbSession must be a compile-time
# constant. Builds tests/sql-gate/fixture twice against the real Goldmark.Api:
#   - without Violations.cs it must build (constant SQL, values as parameters, is accepted);
#   - with it, the build must fail with CA1857 on exactly the lines marked "// violation" and no
#     other error, so a gate that silently stopped firing (or a broken fixture) is caught.
# Usage: tests/sql-gate/check-sql-gate.sh [configuration]   (default Release, as CI builds)
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture="${repo}/tests/sql-gate/fixture"
configuration="${1:-Release}"
failures=0

dotnet restore "${fixture}" --locked-mode >/dev/null

if dotnet build "${fixture}" -c "${configuration}" --no-restore -nologo -v q >/dev/null 2>&1; then
  echo "ok    constant SQL builds"
else
  echo "WRONG constant SQL failed to build:"; dotnet build "${fixture}" -c "${configuration}" --no-restore -nologo -v q 2>&1 | grep -E "error" | sort -u
  failures=$((failures + 1))
fi

set +e
output="$(dotnet build "${fixture}" -c "${configuration}" --no-restore -nologo -v q -p:IncludeViolations=true 2>&1)"
code=$?
set -e
# Each error once (MSBuild repeats them in its summary), as "Violations.cs(line): CODE".
errors="$(grep -oE '[A-Za-z]+\.cs\([0-9]+,[0-9]+\): error [A-Z]+[0-9]+' <<<"${output}" | sed -E 's/,[0-9]+\): error /): /' | sort -u)"
expected="$(grep -n '// violation$' "${fixture}/Violations.cs" | cut -d: -f1 | sed 's/.*/Violations.cs(&): CA1857/' | sort -u)"

if [[ ${code} -ne 0 && "${errors}" == "${expected}" ]]; then
  echo "ok    every marked violation fails the build with CA1857 ($(wc -l <<<"${expected}" | tr -d ' ') calls)"
else
  echo "WRONG violations: exit ${code}"; echo "expected:"; echo "${expected}"; echo "got:"; echo "${errors:-(none)}"
  failures=$((failures + 1))
fi

if [[ ${failures} -gt 0 ]]; then
  echo "check-sql-gate: ${failures} failure(s)"; exit 1
fi
echo "check-sql-gate: OK"
