#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-full.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

mapfile -t untracked_files < <(git ls-files --others --exclude-standard)
if ((${#untracked_files[@]} > 0)); then
  printf 'Untracked files must be staged or removed before full verification:\n' >&2
  printf '  %s\n' "${untracked_files[@]}" >&2
  exit 1
fi

if ! git fetch --quiet origin main; then
  printf 'verify-full.sh cannot fetch origin main. Restore access to the remote instead of verifying against a stale base.\n' >&2
  exit 1
fi

if ! git show-ref --verify --quiet refs/remotes/origin/main; then
  printf 'Fetching origin main left no refs/remotes/origin/main. Restore the standard origin fetch refspec so the remote-tracking base exists.\n' >&2
  exit 1
fi

if ! git merge-base --is-ancestor origin/main HEAD; then
  printf 'HEAD does not contain the current origin/main. Rebase the branch onto the fetched base before verifying.\n' >&2
  exit 1
fi

bash scripts/test-agent-workflow.sh
dotnet tool restore
dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
git diff --check origin/main...HEAD
git diff --cached --check
git diff --check
