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

bash scripts/test-agent-workflow.sh
dotnet tool restore
dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
if ! git show-ref --verify --quiet refs/remotes/origin/main; then
  printf 'verify-full.sh requires refs/remotes/origin/main.\n' >&2
  exit 1
fi

git diff --check origin/main...HEAD
git diff --cached --check
git diff --check
