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
# shellcheck source=scripts/verification-record.sh
source "$(dirname "${BASH_SOURCE[0]}")/verification-record.sh"

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
      backend/Directory.Build.props | backend/Directory.Build.targets | backend/Directory.Packages.props) return 0 ;;
      global.json | backend/MailFathom.slnx) return 0 ;;
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

verification_digest="$(resolve_verification_digest)"

# The base is deliberately absent from that digest, and the two checks above are why: whether the
# branch still contains the current `origin/main` is asked afresh on every run, before this point is
# reached, so a record can never stand in for it. Folding the base in would instead retire a record
# every time somebody else merged, which is the one thing that happens constantly here and proves
# nothing about this branch's own content.
expensive_steps_ran='yes'

if [[ -z "${VERIFY_FORCE:-}" ]] && verification_already_recorded 'verify-full' "$verification_digest"; then
  report_verification_already_recorded 'verify-full' 'verify-full'
  expensive_steps_ran=''
  # The whitespace checks below still run: they are the one part of this gate that reads the base
  # rather than the tree, and they cost nothing.
else
  # What the contract suite asserts is a property of the whole tree — a licensing header in every
  # `.yml`, `.sh`, Helm template, and `SKILL.md`, a `describes:` marker resolving to something, a
  # table-of-contents entry behind every published page — and all but one of those are carried by a
  # file no C# change can move. Removing or moving one can, because a marker and an entry name a
  # path, so a deletion or a rename runs the suite whatever it touched. The exception is the NUL-byte
  # sweep, which reads every tracked text file rather than a list of extensions and therefore reads
  # `.cs` as well: for a C#-only branch that one verdict is deferred rather than absent, which is the
  # single place this skip withholds an answer somebody here could have wanted. `CI` runs the suite
  # unconditionally on every pull request including a draft and on every push to `main`, which is
  # what makes skipping it here an earlier verdict withheld rather than a verdict lost: the tree a
  # merge produces is the one no pull-request run was ever shown, and that run is in the pipeline
  # rather than in this script.
  #
  # It runs beside the dotnet chain rather than in front of it. The suite builds the repositories it
  # tests under its own temporary directory and fakes `dotnet` with a symlink to itself, so it reads
  # nothing this chain writes and writes nothing this chain reads — and at 109 s against the chain's
  # 105 s on the owner's machine, running the two in sequence doubled the gate for no verdict.
  # What that trades away is the guarantee that a failing suite spends no build: both are already
  # running when the suite fails. The gate still fails, and it now fails having reported both
  # answers rather than only the first. The opposite order keeps its old shape below, because a
  # broken build is not a tree worth reporting contract findings about.
  workflow_contracts_status=0
  workflow_contracts_output="$(mktemp)"
  workflow_contracts_pid=''

  if ((${#touched_other_paths[@]} > 0)) || [[ -n "$removed_paths" ]]; then
    bash scripts/test-agent-workflow.sh > "$workflow_contracts_output" 2>&1 &
    workflow_contracts_pid=$!
  fi

  # A chain that fails must not leave the suite running behind the prompt.
  trap '[[ -n "$workflow_contracts_pid" ]] && kill "$workflow_contracts_pid" 2> /dev/null; rm -f "$workflow_contracts_output"' EXIT

  # The chain runs in a subshell so its failure can be carried out to the suite below rather than
  # exiting the script where it happens. `errexit` is what makes it stop at its own first failure,
  # and the two `set` lines around the subshell are what preserve it: bash disables `errexit` for a
  # command that is an operand of `||`, *including* a subshell that re-enables it internally, so
  # `( set -e; … ) || status=$?` would run the whole chain past a failed build. Standalone, with
  # `set +e` holding the parent, the inner `set -e` applies. `run_test` in the contract suite is
  # built the same way for the same reason.
  dotnet_chain_status=0
  set +e
  (
    set -e
    dotnet tool restore
    dotnet restore backend/MailFathom.slnx --locked-mode
    dotnet build backend/MailFathom.slnx --configuration Release --no-restore
    dotnet msbuild .config/CodeCoverage.proj -t:Collect -p:Configuration=Release

    # Verifying rather than repairing, which is the whole difference between this gate and the fast
    # loop: a change that never went through the loop is caught here rather than quietly rewritten at
    # the point it is about to be committed.
    #
    # The scope follows what can actually move a verdict. Formatting is a property of a file, so the
    # files the branch changed are what this branch can have broken, and everything else was verified
    # by whatever change last touched it — the same argument `ci.yml` makes for asking a pull request
    # rather than a push. A shared style input is the exception, because it changes the answer for
    # files this branch never opened, and it is the one case that still costs the whole solution.
    if names_shared_style_input "${touched_other_paths[@]}"; then
      dotnet format backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic
    elif ((${#changed_csharp_files[@]} > 0)); then
      # The one case the loop settles for this gate. Its repairing pass is the same tool over the
      # same file set from the same `list_changed_paths`, and a record exists only where that pass
      # left every file byte-for-byte as it found them — so asking again whether they need rewriting
      # has one possible answer. The whole-solution branch above is never settled this way, because
      # the loop never formatted that scope.
      if [[ -z "${VERIFY_FORCE:-}" ]] \
        && verification_already_recorded 'verify-fast' "$verification_digest"; then
        printf 'Formatting: the fast loop repaired exactly this content at %s without rewriting a file, so the verifying pass over the same files is skipped.\n' \
          "$(verification_recorded_at 'verify-fast')"
      else
        dotnet format backend/MailFathom.slnx --no-restore --verify-no-changes --verbosity diagnostic \
          --include "${changed_csharp_files[@]}"
      fi
    fi
  )
  dotnet_chain_status=$?
  set -e

  # A chain that failed is answered now rather than after the suite has finished saying something
  # about a tree whose build is broken. That is what keeps a compile error a twenty-second answer
  # instead of a hundred-second one, and it is the half of *stop at the first failure* that survives
  # running the two together.
  if [[ -n "$workflow_contracts_pid" ]]; then
    if ((dotnet_chain_status != 0)); then
      kill "$workflow_contracts_pid" 2> /dev/null || true
      wait "$workflow_contracts_pid" 2> /dev/null || true
      printf 'The workflow contract suite was still running when the chain above failed, so its verdict was not collected. Fix that failure and run the gate again.\n' >&2
    else
      wait "$workflow_contracts_pid" || workflow_contracts_status=$?
      cat "$workflow_contracts_output"
    fi

    workflow_contracts_pid=''
  fi

  if ((dotnet_chain_status != 0)) || ((workflow_contracts_status != 0)); then
    exit 1
  fi
fi

# Two dots, not three. The ancestor check above already proves the base is reachable from HEAD, so
# the two forms agree today; three dots would diff from the merge base and silently keep agreeing
# if that check were ever relaxed, which is exactly the drift this gate exists to catch.
git diff --check "$base_remote/main..HEAD"
git diff --cached --check
git diff --check

# Only a run that did the work re-stamps the record. A skipped run rewriting it would push the
# recorded time forward on every rerun, and that time is the whole evidence the skip message offers:
# it has to name the run that actually built, tested, and measured this content, not the last one
# that agreed it had already been done.
if [[ -n "$expensive_steps_ran" ]]; then
  record_verification 'verify-full' "$verification_digest"
fi
