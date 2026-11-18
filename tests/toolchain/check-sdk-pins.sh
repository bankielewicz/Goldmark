#!/usr/bin/env bash
# The .NET SDK that global.json asks for (rollForward: disable) must be the one the Dockerfile's SDK image has, or
# every image build fails at its first dotnet command (DEC-004). Dependabot updates the two separately (the image is an
# ARG default), so CI checks that they agree before anything builds.
# Usage: tests/toolchain/check-sdk-pins.sh [repository root]
set -euo pipefail

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
sdk="$(sed -n 's/^ *"version": *"\([0-9][0-9.]*\)".*/\1/p' "${root}/global.json" | head -n 1)"
image="$(sed -n 's|^ARG SDK_IMAGE=mcr\.microsoft\.com/dotnet/sdk:\([0-9][0-9.]*\)-.*@sha256:[0-9a-f]\{64\}$|\1|p' "${root}/Dockerfile")"

if [[ -z "${sdk}" ]]; then
  echo "global.json names no SDK version" >&2
  exit 1
fi
if [[ -z "${image}" ]]; then
  echo "Dockerfile has no ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:<version>-...@sha256:<digest>" >&2
  exit 1
fi
if [[ "${sdk}" != "${image}" ]]; then
  echo "global.json asks for .NET SDK ${sdk}, but the Dockerfile's SDK image is ${image}: change both together (DEC-004)" >&2
  exit 1
fi
echo "SDK pins agree: ${sdk} in global.json and the Dockerfile's SDK image"
