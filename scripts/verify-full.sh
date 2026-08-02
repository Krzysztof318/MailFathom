#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-full.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# Resolved from this script's own location rather than from the repository it verifies, because the
# workflow contract suite runs the committed scripts against a fixture checkout that carries none.
# shellcheck source=scripts/resolve-base-remote.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-base-remote.sh"

# Refusing here, before the fetch and before any dotnet invocation, is what makes the refusal
# meaningful: the base check below passes trivially on main, because origin/main is its own
# ancestor. A detached HEAD is left alone, because it is not a branch anyone pushes to.
current_branch="$(git symbolic-ref --quiet --short HEAD || true)"
if [[ "$current_branch" == 'main' || "$current_branch" == 'master' ]]; then
  printf 'verify-full.sh must not run on %s. Switch to the branch that carries the change.\n' \
    "$current_branch" >&2
  exit 1
fi

mapfile -t untracked_files < <(git ls-files --others --exclude-standard)
if ((${#untracked_files[@]} > 0)); then
  printf 'Untracked files must be staged or removed before full verification:\n' >&2
  printf '  %s\n' "${untracked_files[@]}" >&2
  exit 1
fi

if ! base_remote="$(resolve_base_remote)"; then
  base_remote_resolution_hint >&2
  exit 1
fi

# The explicit destination refspec is what makes the next check meaningful. A bare
# `git fetch <remote> main` only writes FETCH_HEAD, so a repository whose
# remote.<remote>.fetch is missing or remapped would keep a stale
# refs/remotes/<remote>/main and pass the base check against it.
if ! git fetch --quiet "$base_remote" "+refs/heads/main:refs/remotes/$base_remote/main"; then
  printf 'verify-full.sh cannot fetch %s main. Restore access to the remote instead of verifying against a stale base.\n' \
    "$base_remote" >&2
  exit 1
fi

if ! git merge-base --is-ancestor "$base_remote/main" HEAD; then
  printf 'HEAD does not contain the current %s/main. Rebase the branch onto the fetched base before verifying.\n' \
    "$base_remote" >&2
  exit 1
fi

bash scripts/test-agent-workflow.sh
dotnet tool restore
dotnet restore MailFathom.slnx --locked-mode
dotnet build MailFathom.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release
dotnet format MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic
# Two dots, not three. The ancestor check above already proves the base is reachable from HEAD, so
# the two forms agree today; three dots would diff from the merge base and silently keep agreeing
# if that check were ever relaxed, which is exactly the drift this gate exists to catch.
git diff --check "$base_remote/main..HEAD"
git diff --cached --check
git diff --check
