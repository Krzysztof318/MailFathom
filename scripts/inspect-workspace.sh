#!/usr/bin/env bash
set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'inspect-workspace.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

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

if git show-ref --verify --quiet refs/remotes/origin/main; then
  if git merge-base --is-ancestor origin/main HEAD; then
    contains_origin_main='yes'
  else
    contains_origin_main='no'
  fi
else
  contains_origin_main='unknown (origin/main is unavailable)'
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
printf 'Contains origin/main: %s\n' "$contains_origin_main"
printf 'Working tree: %s\n' "$working_tree_state"
printf 'Registered worktrees: %s\n' "$registered_worktree_count"
printf '.NET SDK: %s\n' "$dotnet_sdk_version"
