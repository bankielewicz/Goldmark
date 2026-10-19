#!/usr/bin/env bash
# Migration immutability check (DEC-028). DbUp records only script names, never their
# content, so an edited migration would silently never run on databases that already
# applied it. This check keeps db/migrations forward-only:
#   1. db/migrations.sha256 lists every script, in order, numbered 0001, 0002, ... with
#      no gaps, named NNNN_lower_snake.sql;
#   2. every listed script exists with exactly that SHA-256, and no script is unlisted;
#   3. with a base revision (CI passes the PR base or the previous main commit), the base's
#      manifest must be an unchanged prefix of this one: applied scripts are never edited,
#      renamed, removed or reordered; new scripts are only appended.
#
# Usage: db/check-migrations.sh [base-revision]
# Adding a migration: create db/migrations/NNNN_name.sql, then
#   sha256sum db/migrations/NNNN_name.sql >> db/migrations.sha256
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
manifest=db/migrations.sha256
base="${1:-}"

fail() { echo "check-migrations: FAIL: $*" >&2; exit 1; }

[[ -f "${manifest}" ]] || fail "${manifest} is missing"

expected=1
listed=()
while IFS= read -r line || [[ -n "${line}" ]]; do
  [[ "${line}" =~ ^[0-9a-f]{64}\ \ db/migrations/([0-9]{4})_[a-z0-9_]+\.sql$ ]] \
    || fail "malformed manifest line: '${line}' (expected '<sha256>  db/migrations/NNNN_name.sql')"
  number=$((10#${BASH_REMATCH[1]}))
  ((number == expected)) || fail "migration numbers must be contiguous from 0001: expected $(printf '%04d' "${expected}"), found ${BASH_REMATCH[1]}"
  expected=$((expected + 1))
  listed+=("${line#*  }")
done < "${manifest}"
((${#listed[@]} > 0)) || fail "${manifest} lists no migrations"

for file in db/migrations/*; do
  printf '%s\n' "${listed[@]}" | grep -qxF "${file}" || fail "${file} is not listed in ${manifest} (append its sha256sum line)"
done

if ! sha256sum --check --strict --quiet "${manifest}" >&2; then
  fail "a migration differs from its recorded hash. Applied migrations must never change: revert the edit and add a new migration instead"
fi

if [[ -n "${base}" ]]; then
  if base_manifest="$(git show "${base}:${manifest}" 2>/dev/null)"; then
    base_lines="$(printf '%s\n' "${base_manifest}" | sed '/^$/d' | wc -l)"
    if [[ "$(head -n "${base_lines}" "${manifest}")" != "$(printf '%s\n' "${base_manifest}" | sed '/^$/d')" ]]; then
      fail "the migrations recorded at ${base} were changed, renamed, removed or reordered. Only append new migrations"
    fi
    echo "check-migrations: ${base_lines} migration(s) from ${base} unchanged"
  else
    echo "check-migrations: ${base} has no ${manifest}; nothing to compare"
  fi
fi

echo "check-migrations: OK (${#listed[@]} migration(s))"
