#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-fast.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# Resolved from this script's own location rather than from the repository it verifies, because the
# workflow contract suite runs the committed scripts against a fixture checkout that carries none.
# shellcheck source=scripts/resolve-base-remote.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-base-remote.sh"
# shellcheck source=scripts/list-branch-changes.sh
source "$(dirname "${BASH_SOURCE[0]}")/list-branch-changes.sh"
# shellcheck source=scripts/resolve-changed-stacks.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-changed-stacks.sh"
# shellcheck source=scripts/verification-record.sh
source "$(dirname "${BASH_SOURCE[0]}")/verification-record.sh"

# The integration branch is never the subject of a change, so a run there reports on code nobody is
# about to modify. verify-full.sh cannot catch this through its base check: origin/main is trivially
# its own ancestor. A detached HEAD is left alone, because it is not a branch anyone pushes to.
current_branch="$(git symbolic-ref --quiet --short HEAD || true)"
if [[ "$current_branch" == 'main' || "$current_branch" == 'master' ]]; then
  printf 'verify-fast.sh must not run on %s. Switch to the branch that carries the change.\n' \
    "$current_branch" >&2
  exit 1
fi

verification_digest="$(resolve_verification_digest)"

# What the branch changed is read here rather than beside the flows below, because one of the things
# it decides is whether this run happens at all. It costs a handful of git invocations.
if ! changed_paths="$(list_changed_paths)"; then
  printf 'verify-fast.sh cannot determine which files the branch changed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

if ! removed_paths="$(list_removed_or_renamed_paths)"; then
  printf 'verify-fast.sh cannot determine which files the branch removed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

# Which stack's flow this change earns, decided from the paths rather than by whoever started the
# run — the same question `ci.yml` answers in `detect-changes`, from the same two lists.
# `scripts/resolve-changed-stacks.sh` carries them and the reasoning. Removals feed the decision
# beside the changes, because deleting the last file of a project moves what the solution builds as
# surely as editing one does, and `list_changed_paths` deliberately reports no deletion.
mapfile -t touched_paths < <(printf '%s\n%s\n' "$changed_paths" "$removed_paths" | grep -Ev '^$' | sort --unique)

run_service_stack=''
run_client_stack=''

if change_reaches_service_stack "${touched_paths[@]}"; then
  run_service_stack='yes'
fi

if change_reaches_client_stack "${touched_paths[@]}"; then
  run_client_stack='yes'
fi

# `--include` selects within the workspace `dotnet format` loaded, so this names the files the service
# solution actually holds. Everything outside `frontend/` belongs to it, which is where a `.cs` file
# above both stacks was already going, and `frontend/` holds no C# at all while the client stack
# carries no build.
mapfile -t changed_service_csharp_files < <(printf '%s\n' "$changed_paths" | grep -E '\.cs$' | grep -Ev '^frontend/' | sort --unique)

# The full gate builds, tests, and verifies the formatting this loop repairs, in whichever stacks
# this change reaches, so a green run of it over this content leaves nothing here to find. The
# implication runs one way only: passing the loop says nothing about coverage or the contract suite.
if [[ -z "${VERIFY_FORCE:-}" ]]; then
  for proving_gate in 'verify-fast' 'verify-full'; do
    if verification_already_recorded "$proving_gate" "$verification_digest"; then
      report_verification_already_recorded 'verify-fast' "$proving_gate"
      exit 0
    fi
  done
fi

# The service stack's flow. Locked mode here and not only in the final gate, for the same reason
# formatting runs here: a pin moved without regenerating the lock files fails restore with NU1004,
# and discovering that after the whole coverage collection has already run wastes the loop this
# script exists to shorten. Regenerate with `dotnet restore backend/MailFathom.slnx --force-evaluate`
# as part of the change that moves the pin.
#
# One formatting pass, and a repairing one, because this is the only step in either gate that
# rewrites a file rather than reporting on it. What it repairs is the part of formatting the build
# cannot see: `backend/Directory.Build.props` sets `EnforceCodeStyleInBuild` beside
# `TreatWarningsAsErrors` and `.editorconfig` gives the IDE rules severity `warning`, so the Release
# build above already fails on `IDE0055`, `IDE0005`, and `IDE0073` with the file and line — while the
# ordering of using directives and a missing final newline are `dotnet format`'s own passes and
# appear nowhere in a build.
#
# So a second `--verify-no-changes` pass here would restate the build's verdict at the cost of
# another full workspace load. What it could still report is a diagnostic with no code fix, which
# this pass could not act on either — `IDE0060` is the one that reaches this repository, and the
# Release build passes over it despite `.editorconfig` setting it to `warning`. Naming a defect the
# loop cannot repair belongs to the full gate and to `CI`, which verify instead of repairing, and
# which is where a change that was never run through this loop is caught.
#
# The loop formats only what the branch changed. `dotnet format` reloads the MSBuild workspace on
# every invocation and analyzes whatever is in scope, so the whole solution costs several times what
# a handful of files costs. Splitting the run into the `whitespace`, `style`, and `analyzers`
# subcommands does not help: it pays that workspace load three times.
if [[ -n "$run_service_stack" ]]; then
  dotnet restore backend/MailFathom.slnx --locked-mode
  dotnet build backend/MailFathom.slnx --configuration Release --no-restore
  dotnet test --solution backend/MailFathom.slnx --configuration Release --no-build

  if ((${#changed_service_csharp_files[@]} > 0)); then
    dotnet format backend/MailFathom.slnx --no-restore --include "${changed_service_csharp_files[@]}"
  fi
fi

# The client stack's flow, which is nothing today and says so. The Uno Platform client was withdrawn
# and the client is being rebuilt in React, so `frontend/` holds two placeholder directories, no
# solution, and no build — exactly what the `Frontend` job of `CI` reports. The detection above is
# kept whole rather than deleted, because it is what the flow that replaces this is hung on.
if [[ -n "$run_client_stack" ]]; then
  printf 'This change reaches the client stack, which carries no build yet. See frontend/src/README.md.\n'
fi

# A change no build reads — documentation, a skill, a deployment asset — runs neither flow and says
# so, because a gate that prints nothing and exits zero reads as a gate that was not run.
if [[ -z "$run_service_stack" && -z "$run_client_stack" ]]; then
  printf 'This change reaches neither stack, so no solution was restored, built, or tested. Run scripts/verify-full.sh for what the whole-tree contracts have to say about it.\n'
fi

# Records nothing when that pass rewrote a file, because the build and the tests above ran against
# content this working tree no longer holds.
record_verification 'verify-fast' "$verification_digest"
