#!/usr/bin/env bash
set -euo pipefail

: "${FAKE_DOTNET_LOG:?FAKE_DOTNET_LOG must identify the invocation log}"

printf '%s\n' "$*" >> "$FAKE_DOTNET_LOG"

if [[ -n "${FAKE_DOTNET_FAIL_MATCH:-}" && "$*" == *"$FAKE_DOTNET_FAIL_MATCH"* ]]; then
  exit 19
fi

if [[ "$*" == '--version' ]]; then
  printf '10.0.110\n'
fi
