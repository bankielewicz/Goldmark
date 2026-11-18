#!/usr/bin/env bash
# Self-test for tests/toolchain/check-sdk-pins.sh: pins that agree pass; a split, a missing pin or an image without a
# digest fails. Each case gets its own folder in a throw-away directory; the repository is only read.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT
failures=0
cases=0
digest="sha256:$(printf '0%.0s' {1..64})"

expect() { # pass|fail description global.json-version Dockerfile-SDK-line
  local want="$1" what="$2" got dir
  cases=$((cases + 1))
  dir="${scratch}/case${cases}"
  mkdir "${dir}"
  printf '{\n  "sdk": {\n    "version": "%s",\n    "rollForward": "disable"\n  }\n}\n' "$3" > "${dir}/global.json"
  printf 'ARG NODE_IMAGE=node:24.21.0-trixie-slim@%s\n%s\n' "${digest}" "$4" > "${dir}/Dockerfile"
  if bash "${repo}/tests/toolchain/check-sdk-pins.sh" "${dir}" >/dev/null 2>&1; then got=pass; else got=fail; fi
  if [[ "${got}" == "${want}" ]]; then echo "ok    ${what} (${got})"; else echo "WRONG ${what}: expected ${want}, got ${got}"; failures=$((failures + 1)); fi
}

expect pass "global.json and the image name the same SDK" 10.0.404 "ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.404-noble@${digest}"
expect fail "global.json moved, the image didn't (PR #64)" 10.0.403 "ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402-noble@${digest}"
expect fail "an image without a digest" 10.0.404 "ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.404-noble"
expect fail "no SDK image" 10.0.404 "ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled-extra@${digest}"
expect fail "no SDK version in global.json" "" "ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.404-noble@${digest}"

if bash "${repo}/tests/toolchain/check-sdk-pins.sh" "${repo}" >/dev/null 2>&1; then
  echo "ok    the repository's own pins agree (pass)"
else
  echo "WRONG the repository's own pins disagree"
  failures=$((failures + 1))
fi

if [[ "${failures}" -gt 0 ]]; then echo "${failures} case(s) wrong"; exit 1; fi
echo "All cases as expected."
