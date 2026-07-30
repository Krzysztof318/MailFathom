#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-full.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

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

# The explicit destination refspec is what makes the next check meaningful. A bare
# `git fetch origin main` only writes FETCH_HEAD, so a repository whose
# remote.origin.fetch is missing or remapped would keep a stale
# refs/remotes/origin/main and pass the base check against it.
if ! git fetch --quiet origin '+refs/heads/main:refs/remotes/origin/main'; then
  printf 'verify-full.sh cannot fetch origin main. Restore access to the remote instead of verifying against a stale base.\n' >&2
  exit 1
fi

if ! git merge-base --is-ancestor origin/main HEAD; then
  printf 'HEAD does not contain the current origin/main. Rebase the branch onto the fetched base before verifying.\n' >&2
  exit 1
fi

bash scripts/test-agent-workflow.sh
dotnet tool restore
dotnet restore MailMcp.slnx --locked-mode
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release
dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic
# Two dots, not three. The ancestor check above already proves origin/main is reachable from HEAD,
# so the two forms agree today; three dots would diff from the merge base and silently keep agreeing
# if that check were ever relaxed, which is exactly the drift this gate exists to catch.
git diff --check origin/main..HEAD
git diff --cached --check
git diff --check
