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

# Formatting runs in the fast loop as well as the final gate. Style diagnostics such as IDE0005 are
# reported by `dotnet format` rather than by the build, so leaving them to full verification means
# discovering them only after tool restore and the whole coverage collection have already run.
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
