#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-fast.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# The integration branch is never the subject of a change, so a run there reports on code nobody is
# about to modify. verify-full.sh cannot catch this through its base check: origin/main is trivially
# its own ancestor. A detached HEAD is left alone, because it is not a branch anyone pushes to.
current_branch="$(git symbolic-ref --quiet --short HEAD || true)"
if [[ "$current_branch" == 'main' || "$current_branch" == 'master' ]]; then
  printf 'verify-fast.sh must not run on %s. Switch to the branch that carries the change.\n' \
    "$current_branch" >&2
  exit 1
fi

dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet test --solution MailMcp.slnx --configuration Release --no-build

# Formatting runs in the fast loop as well as the final gate. Style diagnostics such as IDE0005 are
# reported by `dotnet format` rather than by the build, so leaving them to full verification means
# discovering them only after tool restore and the whole coverage collection have already run.
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
