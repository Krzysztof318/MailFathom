#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom


# Which unit suites a change can have broken. Sourced by the fast loop after
# `scripts/resolve-changed-stacks.sh`, whose `change_reaches_service_stack` it calls to decide which
# changed paths it has to have an answer about; it defines two functions and runs nothing on its own.
#
# The solution's whole suite is fifteen thousand tests and about eighty seconds of a machine several
# sessions share, and almost none of it reads the project a change touched. What decides here is what
# each test project names for itself, read out of its own `Include` attributes: a `ProjectReference`
# to the project under test, or a `Compile` linking one of its files in. Both are how a suite here
# reaches production code and the second is not an edge case — `AppHost.UnitTests` holds no project
# reference at all and reaches its subject by linking three files, which a rule written around
# project references alone would have missed.
#
# The relation is direct rather than transitive, and that is the whole of what this narrowing costs:
# a change to `Domain` runs `Domain.UnitTests` and not the `Application.UnitTests` that exercises the
# same types one layer up. `CI` runs every suite on the pull request, so what stays local is an
# earlier verdict rather than the verdict, and the fast loop says as much when it narrows.
#
# A unit-test project is one whose name ends `.UnitTests`. That is the rule
# `backend/Directory.Build.props` sets `IsUnitTestProject` by, and reading the same convention here
# rather than restating an exclusion list is what keeps this from ever selecting `Benchmarks`, which
# is a report rather than a suite, or `IntegrationTests`, which needs Docker and a PostgreSQL
# container and which a solution-wide `dotnet test` skips for that reason.

# The unit-test projects the changed paths reach, one per line, or a non-zero status meaning the
# change cannot be narrowed and the whole solution answers for it. Run from the repository root.
#
# A path the service build reads that no test project names is what returns that status: a package
# pin, a shared build property, the solution file, the SDK pin, a source file no suite links. Each of
# those can move the verdict on a project nothing in the change touched, so the conservative answer is
# the whole solution rather than a guess at which suites read it.
resolve_changed_unit_suites() {
  local changed_path project_file project_directory include include_path covered index matched
  local -a selected=()

  index=''

  for project_file in backend/tests/*.UnitTests/*.csproj; do
    [[ -e "$project_file" ]] || continue

    project_directory="$(dirname "$project_file")"
    index+="$project_file"$'\t'"$project_directory/"$'\n'

    # A package reference carries a package name in the same attribute, and a name never holds a
    # separator, which is what tells the two apart without parsing the element around it.
    while read -r include; do
      [[ "$include" == *[\\/]* ]] || continue

      include_path="$(realpath -m --relative-to="$PWD" "$project_directory/${include//\\//}")"

      if [[ "$include_path" == *.csproj ]]; then
        index+="$project_file"$'\t'"$(dirname "$include_path")/"$'\n'
      else
        index+="$project_file"$'\t'"$include_path"$'\n'
      fi
    done < <(grep -o 'Include="[^"]*"' "$project_file" 2> /dev/null | sed 's/^Include="//; s/"$//' || true)
  done

  for changed_path in "$@"; do
    # Every path the service build reads is asked about, not only the ones under `backend/`.
    # `global.json` pins the SDK, `NuGet.config` decides what restores, and `.config/**` decides what
    # is run and measured — each can move the verdict on every project while naming none of them, so
    # each has to reach the refusal below rather than be skipped as uninteresting. What this passes
    # over is a path no service build reads at all: a page, a client file, a deployment asset.
    change_reaches_service_stack "$changed_path" || continue

    matched=''

    while IFS=$'\t' read -r project_file covered; do
      [[ -n "$project_file" ]] || continue

      if [[ "$covered" == */ ]]; then
        [[ "$changed_path" == "$covered"* ]] || continue
      else
        [[ "$changed_path" == "$covered" ]] || continue
      fi

      matched='yes'
      selected+=("$project_file")
    done <<< "$index"

    [[ -n "$matched" ]] || return 1
  done

  if ((${#selected[@]} == 0)); then
    return 1
  fi

  printf '%s\n' "${selected[@]}" | sort --unique
}

# How many unit suites the solution holds, which is what makes the count the fast loop prints mean
# something: two of fourteen is a narrowing worth reading, and two of two is not one at all.
count_unit_suites() {
  local project_file
  local count=0

  for project_file in backend/tests/*.UnitTests/*.csproj; do
    [[ -e "$project_file" ]] || continue
    count=$((count + 1))
  done

  printf '%s\n' "$count"
}
