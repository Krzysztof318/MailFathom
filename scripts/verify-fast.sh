#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-fast.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet test --solution MailMcp.slnx --configuration Release --no-build
