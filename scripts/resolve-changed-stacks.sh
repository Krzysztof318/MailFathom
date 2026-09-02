#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom


# Which of the two stacks a change reaches. Sourced by the verification scripts; it defines a matcher
# and two path lists and runs nothing on its own.
#
# The repository carries two stacks that share no solution, no build contract, and no package pins,
# so a change to one of them proves nothing about the other and building the other proves nothing
# about the change. `ci.yml` has decided that from the changed paths since the client existed, in the
# `detect-changes` job. These lists are that decision brought to the gates a developer runs, so the
# question is answered the same way in both places and never by whoever is at the keyboard.
#
# The lists are `ci.yml`'s own `build:` and `frontend:` filters, restated rather than read: a shell
# script cannot ask `dorny/paths-filter` what it would match, and parsing a workflow's YAML at gate
# time would make every run depend on a block that exists to be read by GitHub. What keeps the two
# copies one decision is `scripts/test-agent-workflow.sh`, which extracts both filters from `ci.yml`
# and fails when either disagrees with the array below it.

# `ci.yml`'s `build:` filter. Everything the service solution compiles from, plus the shared files
# above both stacks that decide how it compiles at all.
service_stack_filter=(
  'backend/src/**'
  'backend/tests/**'
  'backend/tools/**'
  '.config/**'
  '.editorconfig'
  '.github/workflows/build-test-format-and-migrations.yml'
  '.github/workflows/ci.yml'
  'backend/Directory.Build.props'
  'backend/Directory.Build.targets'
  'backend/Directory.Packages.props'
  'backend/MailFathom.slnx'
  'NuGet.config'
  'Version.props'
  'global.json'
)

# `ci.yml`'s `frontend:` filter. It names nothing under `backend/` and the list above names nothing
# under `frontend/`, which is what makes a change to one stack cost nothing in the other; the three
# entries they share are the files above both stacks that both genuinely read, and a change to one of
# those runs both flows. `scripts/read-declared-version.sh` is in this list and not in the one above
# because the two stacks read `Version.props` by different routes: MSBuild imports it for the service,
# while `frontend/src/Client.App/vite.config.ts` and `frontend/src-tauri/run-tauri.ts` shell out to that
# script, so the client is the one stack build it can break. `scripts/build-schema-artifact.sh` and
# `scripts/build-winget-manifests.sh` read it too and are not why it is here: neither belongs to a
# stack, and the publication channels that run them filter nothing.
client_stack_filter=(
  'frontend/**'
  '.editorconfig'
  '.github/workflows/build-test-frontend.yml'
  '.github/workflows/ci.yml'
  'Version.props'
  'scripts/read-declared-version.sh'
)

# Whether any of the paths after `--` is matched by any of the patterns before it. Two forms occur in
# either list and no others: a directory pattern ending in `/**`, which matches everything beneath
# that directory, and a literal path, which matches itself and nothing else. That is what
# `dorny/paths-filter` makes of the same two forms, so a nested `.editorconfig` is reached through
# the directory pattern above it rather than through the root entry — the same asymmetry `ci.yml`
# has, and the reason the full gate's own shared-style-input list is a separate question.
change_reaches() {
  local pattern path
  local -a patterns=()

  while (($# > 0)); do
    if [[ "$1" == '--' ]]; then
      shift
      break
    fi

    patterns+=("$1")
    shift
  done

  for path in "$@"; do
    for pattern in "${patterns[@]}"; do
      if [[ "$pattern" == */'**' ]]; then
        if [[ "$path" == "${pattern%'**'}"* ]]; then
          return 0
        fi
      elif [[ "$path" == "$pattern" ]]; then
        return 0
      fi
    done
  done

  return 1
}

change_reaches_service_stack() {
  change_reaches "${service_stack_filter[@]}" -- "$@"
}

change_reaches_client_stack() {
  change_reaches "${client_stack_filter[@]}" -- "$@"
}
