#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'inspect-workspace.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

# Resolved from this script's own location rather than from the repository it inspects, because the
# workflow contract suite runs the committed scripts against a fixture checkout that carries none.
# shellcheck source=scripts/resolve-base-remote.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-base-remote.sh"

branch_name="$(git branch --show-current)"
if [[ -z "$branch_name" ]]; then
  branch_name='detached HEAD'
fi

git_directory="$(cd "$(git rev-parse --git-dir)" && pwd -P)"
git_common_directory="$(cd "$(git rev-parse --git-common-dir)" && pwd -P)"
if [[ "$git_directory" == "$git_common_directory" ]]; then
  worktree_kind='primary checkout'
else
  worktree_kind='linked worktree'
fi

if upstream_name="$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null)"; then
  :
else
  upstream_name='none'
fi

if base_remote="$(resolve_base_remote)"; then
  base_branch="$base_remote/main"

  if git show-ref --verify --quiet "refs/remotes/$base_branch"; then
    if git merge-base --is-ancestor "$base_branch" HEAD; then
      contains_base='yes'
    else
      contains_base='no'
    fi
  else
    contains_base="unknown ($base_branch is unavailable)"
  fi
else
  base_branch='unresolved'
  contains_base='unknown (no remote points at the upstream repository)'
fi

dirty_paths="$(git status --porcelain)"
if [[ -z "$dirty_paths" ]]; then
  working_tree_state='clean'
else
  dirty_path_count="$(printf '%s\n' "$dirty_paths" | wc -l)"
  working_tree_state="dirty ($dirty_path_count paths)"
fi

registered_worktree_count="$(
  git worktree list --porcelain |
    awk '$1 == "worktree" { count++ } END { print count + 0 }'
)"

if command -v dotnet >/dev/null 2>&1; then
  if dotnet_sdk_version="$(dotnet --version 2>/dev/null)" && [[ -n "$dotnet_sdk_version" ]]; then
    :
  else
    dotnet_sdk_version='unavailable (dotnet --version failed)'
  fi
else
  dotnet_sdk_version='unavailable'
fi

printf 'Repository: %s\n' "$repository_root"
printf 'Branch: %s\n' "$branch_name"
printf 'Worktree: %s\n' "$worktree_kind"
printf 'Upstream: %s\n' "$upstream_name"
printf 'Base branch: %s\n' "$base_branch"
printf 'Contains base branch: %s\n' "$contains_base"
printf 'Working tree: %s\n' "$working_tree_state"
printf 'Registered worktrees: %s\n' "$registered_worktree_count"
printf '.NET SDK: %s\n' "$dotnet_sdk_version"
