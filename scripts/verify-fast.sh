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

# The branch base, preferred as the remote-tracking ref and falling back to the local branch, so the
# loop keeps working offline. Printing nothing when neither exists widens the scope below to the
# uncommitted work alone rather than failing the run.
resolve_branch_base() {
  local candidate_ref

  for candidate_ref in 'refs/remotes/origin/main' 'refs/heads/main'; do
    if git rev-parse --verify --quiet "$candidate_ref" > /dev/null; then
      printf '%s\n' "$candidate_ref"
      return 0
    fi
  done
}

# Every C# file this branch touches: committed since the base, staged, modified, or newly added.
# Deletions are filtered out because a removed file cannot be formatted.
list_changed_csharp_files() {
  local branch_base

  branch_base="$(resolve_branch_base)"

  {
    git diff --name-only --diff-filter=ACMR HEAD
    git ls-files --others --exclude-standard
    if [[ -n "$branch_base" ]]; then
      git diff --name-only --diff-filter=ACMR "$branch_base...HEAD"
    fi
  } | grep -E '\.cs$' | sort --unique
}

dotnet restore MailMcp.slnx
dotnet build MailMcp.slnx --configuration Release --no-restore
dotnet test --solution MailMcp.slnx --configuration Release --no-build

# Formatting runs in the fast loop as well as the final gate. Style diagnostics such as IDE0005 are
# reported by `dotnet format` rather than by the build, so leaving them to full verification means
# discovering them only after tool restore and the whole coverage collection have already run.
#
# The loop formats only what the branch changed. `dotnet format` reloads the MSBuild workspace on
# every invocation and analyzes whatever is in scope, so the whole solution costs about 70 seconds
# here while a handful of files costs about 30. Splitting the run into the `whitespace`, `style`,
# and `analyzers` subcommands does not help: it pays that workspace load three times. The final gate
# still formats the whole solution, so a defect outside the changed files cannot merge.
mapfile -t changed_csharp_files < <(list_changed_csharp_files)

if ((${#changed_csharp_files[@]} > 0)); then
  # Two passes, because neither reports what the other does. The first rewrites everything that has
  # a code fix, but exits 0 and names no file when a diagnostic has none. The second turns whatever
  # survived into the `file(line,col): error IDEnnnn` the loop can act on, and fails the run.
  dotnet format MailMcp.slnx --no-restore --include "${changed_csharp_files[@]}"
  dotnet format MailMcp.slnx --no-restore --verify-no-changes --verbosity diagnostic \
    --include "${changed_csharp_files[@]}"
fi
