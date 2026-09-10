#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
# shellcheck source=scripts/resolve-changed-unit-suites.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-changed-unit-suites.sh"
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

# The base is asked here rather than only in the full gate, because this loop is what a change is
# actually held to locally: the full gate's steps are all asserted again by `CI` on the pull request,
# and this question is not one of them. It is asked before the record is consulted for the reason the
# full gate gives about its own digest — whether the branch still contains `main` is a fact about the
# branch rather than about the tree, so no record can stand in for it — and before anything expensive
# runs, so a branch that has to be rebased learns that in a second rather than after a build.
#
# A checkout whose remotes name no MailFathom at all is the one case that carries on regardless, and
# it is a different case from a branch that fell behind: there is no base to be behind, so there is
# nothing to refuse against and nothing a refusal would teach that the hint does not. That is the
# fork whose second remote has not been added yet, and blocking it would stop a contributor from
# building and testing while they fix exactly what the hint names. The full gate still refuses it.
base_remote=''

if resolve_base_remote > /dev/null; then
  if ! base_remote="$(require_base_is_contained 'verify-fast.sh')"; then
    exit 1
  fi
else
  base_remote_resolution_hint
  printf 'The fast loop carries on without that answer; scripts/verify-full.sh and the pull request will not.\n'
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
# above both stacks was already going, and `frontend/` holds no C# at all — it is React, TypeScript,
# and the desktop shell's Rust crate, none of which this pass can open.
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

  # The solution is built whole and tested narrow, which is where the two costs actually sit: on the
  # owner's machine an incremental Release build is seconds and the whole suite is about eighty,
  # fifteen thousand tests of which a change reads a handful of projects' worth. Narrowing the build
  # instead would buy the seconds and give up the analyzer verdict `EnforceCodeStyleInBuild` and
  # `TreatWarningsAsErrors` produce over every project, which is the half of this step that answers
  # for the files the change did not open.
  #
  # `scripts/resolve-changed-unit-suites.sh` decides which suites, and refuses to narrow at all for a
  # change that reaches a shared build input — the whole solution then runs exactly as it did before.
  if changed_unit_suites="$(resolve_changed_unit_suites "${touched_paths[@]}")"; then
    mapfile -t unit_suites <<< "$changed_unit_suites"

    # Said rather than left to be inferred, because a run that tested a quarter of the suite and
    # printed nothing about it reads as a run that tested all of it. The pipeline runs every suite on
    # the pull request, so this is an earlier verdict withheld rather than a verdict lost.
    printf '%d of the %d unit suites can have been broken by this change and run here; the rest are left to the pipeline:\n' \
      "${#unit_suites[@]}" "$(count_unit_suites)"
    printf '  %s\n' "${unit_suites[@]}"

    for unit_suite in "${unit_suites[@]}"; do
      dotnet test --project "$unit_suite" --configuration Release --no-build
    done
  else
    dotnet test --solution backend/MailFathom.slnx --configuration Release --no-build
  fi

  if ((${#changed_service_csharp_files[@]} > 0)); then
    dotnet format backend/MailFathom.slnx --no-restore --include "${changed_service_csharp_files[@]}"
  fi
fi

# The client stack's flow. `frontend/` is a pnpm workspace, so every step runs from that directory and
# resolves against `frontend/pnpm-lock.yaml`; `--frozen-lockfile` fails rather than rewriting it, which
# is what locked mode is to the service restore above.
#
# The lint is the client's `TreatWarningsAsErrors`: every rule reports as an error and `--max-warnings 0`
# refuses a warning too, so there is no severity below failing. The type check is the closest thing the
# client has to the Release build, because Vite's build strips types rather than checking them, and
# `pnpm test` runs behind it for the same reason `dotnet test` runs behind the build above.
#
# The formatting pass repairs rather than reports, exactly as `dotnet format` does above, and for the same
# reason: this loop is where a file is rewritten and the full gate is where the same question is asked
# without one being touched.
if [[ -n "$run_client_stack" ]]; then
  if ! command -v pnpm > /dev/null 2>&1; then
    printf 'This change reaches the client stack, which needs Node and pnpm on the path. See docs/operations/local-development.md.\n' >&2
    exit 1
  fi

  pnpm --dir frontend install --frozen-lockfile
  pnpm --dir frontend run lint
  pnpm --dir frontend run typecheck
  pnpm --dir frontend run test
  pnpm --dir frontend run format
fi

# A change no build reads — documentation, a skill, a deployment asset — runs neither flow and says
# so, because a gate that prints nothing and exits zero reads as a gate that was not run.
if [[ -z "$run_service_stack" && -z "$run_client_stack" ]]; then
  printf 'This change reaches neither stack, so no solution was restored, built, or tested. The whole-tree contracts are what answers for it, and they run on the pull request; scripts/verify-full.sh is how to have that answer now.\n'
fi

# Whitespace errors in a file no formatter reads — a page, a workflow, a Helm template — are checked
# here rather than only in the full gate, because this loop is the gate a change is held to locally
# and nothing in the pipeline asks the question at all. It costs milliseconds and reads the diff
# rather than the tree. The committed range needs the base, so it is asked only where the base
# resolved; the other two are about this working tree and are always asked.
if [[ -n "$base_remote" ]]; then
  git diff --check "$base_remote/main..HEAD"
fi

git diff --cached --check
git diff --check

# Records nothing when that pass rewrote a file, because the build and the tests above ran against
# content this working tree no longer holds.
record_verification 'verify-fast' "$verification_digest"
