#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

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
# shellcheck source=scripts/list-branch-changes.sh
source "$(dirname "${BASH_SOURCE[0]}")/list-branch-changes.sh"

# Whether the change moved something every file's formatting is read against. `.editorconfig` carries
# the rules, the three shared MSBuild files carry the analyzer pins and the properties that decide
# which of them run, `global.json` pins the SDK that supplies the formatter, and the solution decides
# which projects are in scope at all — so any one of them can move the verdict on a file the change
# never touched. That is the case the scoped verification below cannot see, and the only one that
# still earns a pass over the whole solution here. It is the same list `ci.yml` gives its `format:`
# filter, for the same reason.
names_shared_style_input() {
  local changed_path

  for changed_path in "$@"; do
    case "$changed_path" in
      .editorconfig | */.editorconfig) return 0 ;;
      Directory.Build.props | Directory.Build.targets | Directory.Packages.props) return 0 ;;
      global.json | MailFathom.slnx) return 0 ;;
    esac
  done

  return 1
}

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

if ! changed_paths="$(list_changed_paths)"; then
  printf 'verify-full.sh cannot determine which files the branch changed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

if ! removed_paths="$(list_removed_or_renamed_paths)"; then
  printf 'verify-full.sh cannot determine which files the branch removed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

mapfile -t changed_csharp_files < <(printf '%s\n' "$changed_paths" | grep -E '\.cs$' | sort --unique)
# Both lists feed this one, because a shared style input that was deleted decides as much as one that
# was edited: the files under a removed `.editorconfig` are read against the rules above it from that
# commit on, and none of them had to be touched for their verdict to move.
mapfile -t touched_other_paths < <(printf '%s\n%s\n' "$changed_paths" "$removed_paths" | grep -Ev '(^$|\.cs$)' | sort --unique)

# What the contract suite asserts is a property of the whole tree — a licensing header in every
# `.yml`, `.sh`, Helm template, and `SKILL.md`, a `describes:` marker resolving to something, a
# table-of-contents entry behind every published page — and none of those can be moved by adding or
# editing a C# file. Removing or moving one can, because a marker and an entry name a path, so a
# deletion or a rename runs the suite whatever it touched. `CI` runs it unconditionally on every pull
# request including a draft and on every push to `main`, which is what makes skipping it here an
# earlier verdict withheld rather than a verdict lost: the tree a merge produces is the one no
# pull-request run was ever shown, and that run is in the pipeline rather than in this script.
if ((${#touched_other_paths[@]} > 0)) || [[ -n "$removed_paths" ]]; then
  bash scripts/test-agent-workflow.sh
fi

dotnet tool restore
dotnet restore MailFathom.slnx --locked-mode
dotnet build MailFathom.slnx --configuration Release --no-restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release

# Verifying rather than repairing, which is the whole difference between this gate and the fast loop:
# a change that never went through the loop is caught here rather than quietly rewritten at the point
# it is about to be committed.
#
# The scope follows what can actually move a verdict. Formatting is a property of a file, so the
# files the branch changed are what this branch can have broken, and everything else was verified by
# whatever change last touched it — the same argument `ci.yml` makes for asking a pull request rather
# than a push. A shared style input is the exception, because it changes the answer for files this
# branch never opened, and it is the one case that still costs the whole solution.
if names_shared_style_input "${touched_other_paths[@]}"; then
  dotnet format MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic
elif ((${#changed_csharp_files[@]} > 0)); then
  dotnet format MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic \
    --include "${changed_csharp_files[@]}"
fi
# Two dots, not three. The ancestor check above already proves the base is reachable from HEAD, so
# the two forms agree today; three dots would diff from the merge base and silently keep agreeing
# if that check were ever relaxed, which is exactly the drift this gate exists to catch.
git diff --check "$base_remote/main..HEAD"
git diff --cached --check
git diff --check
