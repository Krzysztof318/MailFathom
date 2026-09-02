#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Index the obligations a pull request triggers in the rest of the repository.
#
# `Fathom review` reads a change as a diff, and a whole class of defect here is invisible in one: a
# source file that changed while no test followed it, a page under `docs/` that still describes the
# behavior the change replaced, a moved pin with no row in `THIRD_PARTY_LICENSES.md`. The defect is
# the *absence* of a second file from the diff, so a reviewer reading only the diff cannot see it.
#
# This produces the list of second files. It reads the base checkout and the collected `files.json`
# and calls no API, which is what lets the contract suite run it against a fixture tree. Nothing it
# emits is a finding: it is where to look, and the reviewer confirms or drops each row against the
# code. A row that becomes a finding without that confirmation is a defect in the review.
#
# The two kinds of edge it follows are recorded differently on purpose.
#
# A production file to its test is *derived*, never recorded, and each stack states the rule the
# derivation reads. In the service, `AGENTS.md` requires one primary type per file and a file name
# that matches it, and `backend/tests/<Boundary>.UnitTests/` mirrors `backend/src/<Boundary>/`, so
# the edge is a type name searched for across the test tree. In the client,
# `frontend/tests/AGENTS.md` puts the test beside the source and names it after it, so the edge is
# one path rather than a search: `session.ts` is covered by `session.test.ts` in the same directory
# and `App.tsx` by `App.test.tsx`. Both mappings already exist as rules; a written-down copy could
# drift from one, and a derived one cannot.
#
# The two derivations are exact in different ways, and the section below says so per entry rather
# than leaving the reviewer to assume. A type name is a *search*, so it over-reports — a test naming
# `Result` in passing is listed as covering it — and `referencing_test_count` is the warning. A
# sibling path is a *lookup*, so it is either there or it is not, and an empty list under a client
# entry means exactly that no file with that name exists.
#
# A source path to the page that documents it is *declared*, because nothing derives it:
# documentation is written about configuration keys and behavior rather than about type names, so no
# name match finds the edge. Each page states its own subject in a `describes:` marker, which lives
# in the file somebody edits rather than in a central index that would go stale silently and
# conflict on every merge. `scripts/test-agent-workflow.sh` fails when a page carries no marker and
# when a marker names a pattern matching nothing, so both ways the declaration can rot are loud.
#
# Usage: index-obligations.sh <repository-root> <files.json> <output.json>

set -euo pipefail

repository_root="${1:?the repository root is required}"
files_json="${2:?the collected files.json is required}"
output_file="${3:?the output path is required}"

# Ceilings, for the reason every other ceiling in this workflow exists: the reviewer's context is
# finite, and an index long enough to exhaust it produces a silently partial review. What is left out
# is recorded in `notes` and reaches the review body through the reviewer's summary.
#
# The documentation section needs none of its own. It holds at most one entry per changed path, and
# the collection step already caps `files.json`, so a second limit here would be a branch nothing can
# reach — which is worse than no branch, because it reads as a bound somebody verified.
max_test_entries=80

# A per-entry ceiling as well as a per-section one, because the two bound different things. A type
# whose name is common — `Program`, `Result` — is matched as a whole word in every test that names
# it, and the count is a property of the name rather than of the change, so eighty entries of twenty
# references each is a different document from eighty entries of six hundred.
max_references_per_type=20

# How far into a page a `describes:` marker is looked for. `scripts/test-agent-workflow.sh` holds
# the same number and fails a page whose marker sits below it, so the bound is a rule rather than a
# scan that happens to stop early.
marker_preamble_lines=15

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

notes=()

# Every path the change touches, and the paths it touches with a status that leaves a file behind.
# `removed` is kept out of the second list because a deleted production file owes no test.
jq -r '.[].filename' "$files_json" | sort -u > "$work_directory/changed-paths"
jq -r '.[] | select(.status != "removed") | .filename' "$files_json" | sort -u > "$work_directory/present-paths"

path_is_changed() {
  grep -qxF "$1" "$work_directory/changed-paths"
}

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

# Migrations are excluded: `AGENTS.md` makes them append-only and generated, so a migration owes no
# unit test and a reviewer asked to check for one would raise the same wrong finding on every schema
# change.
grep -E '^backend/src/.+\.cs$' "$work_directory/present-paths" \
  | grep -vE '/Migrations/' \
  > "$work_directory/changed-sources" || true

# The type each changed file declares, which `AGENTS.md` guarantees is the file name. `sort -u`
# because two boundaries may both declare a type of the same name, and one pattern is enough to find
# either.
sed -E 's|.*/||; s|\.cs$||' "$work_directory/changed-sources" | sort -u > "$work_directory/type-names" || true

: > "$work_directory/type-references"

if [[ -s "$work_directory/type-names" ]]; then
  # One pass over the test tree rather than one grep per changed type. `-w` gives the whole-word
  # match that keeps `EmailSummary` from matching `StoredEmailSummary`, and `-F` keeps a type name
  # that happens to contain a regular-expression character from being read as a pattern.
  if [[ -d "$repository_root/backend/tests" ]]; then
    (
      cd "$repository_root"
      grep -roFw --include='*.cs' -f "$work_directory/type-names" backend/tests 2>/dev/null || true
    ) > "$work_directory/base-references"

    # `path:match` becomes `match<TAB>path`, which is the direction the lookup below reads.
    sed -E 's|^([^:]*):(.*)$|\2\t\1|' "$work_directory/base-references" >> "$work_directory/type-references" || true
  fi

  # A test the pull request adds is not in the base tree, so the base pass above cannot see it — and
  # a change that adds a class together with its test is the case where reporting a missing test
  # would be most obviously wrong. The added lines of every changed test carry the same reference,
  # so they are searched as one file of `path<TAB>line` records.
  jq -r '
    .[]
    | select(.filename | startswith("backend/tests/"))
    | select(.patch != null)
    | .filename as $path
    | .patch
    | split("\n")[]
    | select(startswith("+"))
    | "\($path)\t\(.)"
  ' "$files_json" > "$work_directory/added-test-lines" || true

  if [[ -s "$work_directory/added-test-lines" ]]; then
    grep -Fw -f "$work_directory/type-names" "$work_directory/added-test-lines" 2>/dev/null \
      | while IFS=$'\t' read -r path line; do
          grep -oFw -f "$work_directory/type-names" <<< "$line" \
            | while IFS= read -r match; do
                printf '%s\t%s\n' "$match" "$path"
              done
        done >> "$work_directory/type-references" || true
  fi
fi

sort -u "$work_directory/type-references" -o "$work_directory/type-references"

# Whether the change touched a file is recorded against that file rather than against the entry it
# belongs to. An entry-level flag would have to mean "any of these", and any-of-these answers the
# wrong question twice over: a change that updates one of two pages would read as having discharged
# its obligation to both, and a change that happens to touch an unrelated test naming the same type
# would read as having tested the behavior that moved.
with_changed_flag() {
  local path

  while IFS= read -r path; do
    [[ -n "$path" ]] || continue

    local changed='false'
    if path_is_changed "$path"; then
      changed='true'
    fi

    jq -nc --arg path "$path" --argjson changed "$changed" \
      '{path: $path, changed_by_this_pull_request: $changed}'
  done | jq -sc .
}

references_for_type() {
  local type_name="$1"

  awk -F'\t' -v wanted="$type_name" '$1 == wanted { print $2 }' "$work_directory/type-references" \
    | sort -u \
    | awk -v limit="$max_references_per_type" 'NR <= limit' \
    | with_changed_flag
}

count_references_for_type() {
  awk -F'\t' -v wanted="$1" '$1 == wanted { print $2 }' "$work_directory/type-references" \
    | sort -u \
    | grep -c . \
    || true
}

: > "$work_directory/tests.ndjson"

test_entry_count=0

while IFS= read -r source_path; do
  [[ -n "$source_path" ]] || continue

  if (( test_entry_count >= max_test_entries )); then
    notes+=("The change touches more source files than the ${max_test_entries} this index covers, so the tests section is partial.")
    break
  fi

  type_name="$(basename "$source_path" .cs)"
  boundary="$(cut -d'/' -f3 <<< "$source_path")"
  reference_count="$(count_references_for_type "$type_name")"

  if (( reference_count > max_references_per_type )); then
    notes+=("${type_name} is named by ${reference_count} tests and the first ${max_references_per_type} are listed; a name that common says little about what covers the behavior.")
  fi

  jq -nc \
    --arg path "$source_path" \
    --arg type "$type_name" \
    --arg status "$(jq -r --arg path "$source_path" 'map(select(.filename == $path)) | .[0].status // ""' "$files_json")" \
    --arg project "backend/tests/${boundary}.UnitTests" \
    --argjson count "$reference_count" \
    --argjson tests "$(references_for_type "$type_name")" \
    '{path: $path, type: $type, status: $status, expected_test_project: $project,
      referencing_test_count: $count, referencing_tests: $tests}' \
    >> "$work_directory/tests.ndjson"

  test_entry_count=$(( test_entry_count + 1 ))
done < "$work_directory/changed-sources"

# The client's half of the same section, into the same list and under the same ceiling, because what
# bounds it is the reviewer's context rather than which stack an entry came from.
#
# Only `frontend/src/<package>/src/` is production source. A package's `vite.config.ts`,
# `vitest.setup.ts`, and `tsconfig.json` sit above that directory, so scoping to it excludes the
# build's own configuration without naming any of it. Three kinds inside it are excluded for the
# reason migrations are excluded above — a test for one is not a thing that can exist, so an index
# that asked for one would produce the same wrong finding on every change of that kind: a test file
# itself, a `.d.ts` declaration, which carries no behavior to reach, and an `index.ts` or
# `index.tsx`, which re-exports and is proven by whether its consumers still compile.
grep -E '^frontend/src/[^/]+/src/.+\.(ts|tsx)$' "$work_directory/present-paths" \
  | grep -vE '\.test\.(ts|tsx)$' \
  | grep -vE '\.d\.ts$' \
  | grep -vE '(^|/)index\.(ts|tsx)$' \
  > "$work_directory/changed-client-sources" || true

# The test beside a client source, as a lookup rather than a search. It is either in the base tree or
# added by this change, and the second case is the one a base-tree pass alone would get wrong — a
# change adding a module together with its test is exactly where reporting a missing test is most
# obviously wrong. A candidate this change *removed* is neither: it is in the base tree and gone from
# the branch, so it is skipped rather than listed as covering anything.
sibling_tests_for_source() {
  local source_path="$1"
  local directory module_name candidate extension

  directory="$(dirname "$source_path")"
  module_name="$(basename "$source_path")"
  module_name="${module_name%.tsx}"
  module_name="${module_name%.ts}"

  for extension in ts tsx; do
    candidate="$directory/${module_name}.test.${extension}"

    if grep -qxF "$candidate" "$work_directory/changed-paths" \
      && ! grep -qxF "$candidate" "$work_directory/present-paths"; then
      continue
    fi

    if [[ -f "$repository_root/$candidate" ]] \
      || grep -qxF "$candidate" "$work_directory/present-paths"; then
      printf '%s\n' "$candidate"
    fi
  done
}

while IFS= read -r source_path; do
  [[ -n "$source_path" ]] || continue

  if (( test_entry_count >= max_test_entries )); then
    notes+=("The change touches more source files than the ${max_test_entries} this index covers, so the tests section is partial.")
    break
  fi

  # The module rather than a type: a `.ts` file declares no single one, and what the file is named
  # after is what `frontend/tests/AGENTS.md` names the test after and what `describe` states.
  module_name="$(basename "$source_path")"
  module_name="${module_name%.tsx}"
  module_name="${module_name%.ts}"

  # The package the test belongs to, which is what `backend/tests/<Boundary>.UnitTests` is for the
  # service. A client test lives inside the package it covers rather than in one of its own, for the
  # resolver reason `frontend/tests/AGENTS.md` gives.
  client_package="$(cut -d'/' -f1-3 <<< "$source_path")"

  sibling_tests_json="$(sibling_tests_for_source "$source_path" | with_changed_flag)"

  jq -nc \
    --arg path "$source_path" \
    --arg type "$module_name" \
    --arg status "$(jq -r --arg path "$source_path" 'map(select(.filename == $path)) | .[0].status // ""' "$files_json")" \
    --arg project "$client_package" \
    --argjson tests "$sibling_tests_json" \
    '{path: $path, type: $type, status: $status, expected_test_project: $project,
      referencing_test_count: ($tests | length), referencing_tests: $tests}' \
    >> "$work_directory/tests.ndjson"

  test_entry_count=$(( test_entry_count + 1 ))
done < "$work_directory/changed-client-sources"

# ---------------------------------------------------------------------------
# Documentation
# ---------------------------------------------------------------------------

# A `describes:` pattern is matched as a regular expression over the path list, because no shell
# construct gives the two meanings this needs at once: `**` crosses directory separators and `*`
# does not, which is what makes `backend/src/*/*.csproj` mean the project files and `backend/src/**` mean everything
# under the boundary.
#
# What this has to agree with is git's own `:(glob)` pathspec, because that is what
# `scripts/test-agent-workflow.sh` resolves every marker against — so a pattern this converter reads
# more narrowly than git does is one the contract suite calls valid while the index silently skips
# the paths it covers.
#
# Agreeing means `**` bounded by slashes matches *zero* directories as well as many: git documents
# `a/**/b` as matching `a/b`, so `backend/src/**/*Options.cs` has to credit `backend/src/FooOptions.cs` and not only
# `backend/src/Host/Configuration/McpOptions.cs`. Turning the `**` alone into `.*` and leaving both slashes
# where they were would require a directory between them, so the slash is taken into the rewrite:
# `/**/` becomes `/(.*/)?` as one unit, and a leading `**/` becomes `(.*/)?` for the same reason at
# the front of a pattern.
#
# Each rewrite is parked as a byte no path contains and restored at the end, so the narrower pattern
# can never consume the wider one first.
glob_to_regex() {
  printf '%s' "$1" | awk '
    {
      pattern = $0
      gsub(/[.^$+(){}|\[\]\\]/, "\\\\&", pattern)
      sub(/^\*\*\//, "\003", pattern)
      gsub(/\/\*\*\//, "\002", pattern)
      gsub(/\*\*/, "\001", pattern)
      gsub(/\*/, "[^/]*", pattern)
      gsub(/\?/, "[^/]", pattern)
      gsub(/\001/, ".*", pattern)
      gsub(/\002/, "/(.*/)?", pattern)
      gsub(/\003/, "(.*/)?", pattern)
      printf "^%s$", pattern
    }'
}

: > "$work_directory/documentation-edges"

if [[ -d "$repository_root/docs" ]]; then
  while IFS= read -r document; do
    relative_document="${document#"$repository_root"/}"

    # Only the preamble is read for a marker. A page that documents the convention writes the
    # syntax out as an example — `docs/AGENTS.md` does — and a scan of the whole file would read
    # that example as a declaration, so every path the example names would acquire a document that
    # says nothing about it. The preamble is where a real marker goes and where an example does not:
    # the longest one here is an ADR, whose front matter puts the heading on line 10.
    marker="$(head -n "$marker_preamble_lines" "$document" \
      | grep -m 1 -oE '<!--[[:space:]]*describes:[^>]*-->' 2>/dev/null || true)"
    [[ -n "$marker" ]] || continue

    patterns="$(sed -E 's|^<!--[[:space:]]*describes:[[:space:]]*||; s|[[:space:]]*-->$||' <<< "$marker")"
    [[ "$patterns" == 'none' ]] && continue

    while IFS= read -r pattern; do
      pattern="$(sed -E 's|^[[:space:]]+||; s|[[:space:]]+$||' <<< "$pattern")"
      [[ -n "$pattern" ]] || continue

      printf '%s\t%s\n' "$relative_document" "$(glob_to_regex "$pattern")" \
        >> "$work_directory/documentation-edges"
    done < <(tr ',' '\n' <<< "$patterns")
  done < <(find "$repository_root/docs" -type f -name '*.md' | sort)
fi

: > "$work_directory/documentation.ndjson"

while IFS= read -r changed_path; do
  [[ -n "$changed_path" ]] || continue

  # A change to the documentation itself owes no documentation, and a marker that covered `docs/`
  # would otherwise make every page describe every other one.
  [[ "$changed_path" == docs/* ]] && continue

  describing_documents="$(
    awk -F'\t' -v path="$changed_path" '$2 != "" && path ~ $2 { print $1 }' \
      "$work_directory/documentation-edges" \
      | sort -u
  )"

  [[ -n "$describing_documents" ]] || continue

  jq -nc \
    --arg path "$changed_path" \
    --argjson documents "$(with_changed_flag <<< "$describing_documents")" \
    '{path: $path, describing_documents: $documents}' \
    >> "$work_directory/documentation.ndjson"
done < "$work_directory/changed-paths"

# ---------------------------------------------------------------------------
# Registers
# ---------------------------------------------------------------------------

# A register is a file that has to gain a row when something else changes, and the pair is written
# out rather than derived because there is nothing to derive it from. Only a pair whose trigger
# actually moved is emitted, so an unrelated change produces an empty section rather than a list of
# obligations it never incurred.
: > "$work_directory/registers.ndjson"

emit_register() {
  local trigger_description="$1" register="$2" trigger_matched="$3"

  [[ "$trigger_matched" == 'true' ]] || return 0

  local register_changed='false'
  if path_is_changed "$register"; then
    register_changed='true'
  fi

  jq -nc \
    --arg trigger "$trigger_description" \
    --arg register "$register" \
    --argjson register_changed "$register_changed" \
    '{trigger: $trigger, register: $register, trigger_changed: true,
      register_changed: $register_changed}' \
    >> "$work_directory/registers.ndjson"
}

dependency_pins_changed='false'
if grep -qxF 'backend/Directory.Packages.props' "$work_directory/changed-paths" \
  || grep -qE '(^|/)packages\.lock\.json$' "$work_directory/changed-paths"; then
  dependency_pins_changed='true'
fi

emit_register 'a dependency pin moved in backend/Directory.Packages.props or a packages.lock.json' \
  'THIRD_PARTY_LICENSES.md' "$dependency_pins_changed"

# The client's pin families, as a pair of their own rather than folded into the one above, so the
# trigger names what actually moved. `frontend/AGENTS.md` puts the npm pin in each package's own
# `package.json` and the resolution in `frontend/pnpm-lock.yaml`; the desktop shell's crate pins
# sit in `frontend/src-tauri/Cargo.toml` with the resolution in `Cargo.lock` beside it. Both require
# the same register row a service pin does under ADR 0016, and both oblige one thing a service pin
# does not: the register records each of those graphs as a census, and `scripts/update-dependencies.sh`
# rewrites pins without recomputing one. So the survey catches a pin that is behind and the review
# catches a census that no longer describes what the lock file resolves.
client_npm_pins_changed='false'
if grep -qE '^frontend/(src/[^/]+/)?package\.json$' "$work_directory/changed-paths" \
  || grep -qxF 'frontend/pnpm-lock.yaml' "$work_directory/changed-paths"; then
  client_npm_pins_changed='true'
fi

client_pins_changed="$client_npm_pins_changed"
if grep -qE '^frontend/src-tauri/Cargo\.(toml|lock)$' "$work_directory/changed-paths"; then
  client_pins_changed='true'
fi

emit_register 'a client dependency pin moved in a frontend package.json, in frontend/pnpm-lock.yaml, or in the desktop shell'"'"'s Cargo.toml or Cargo.lock' \
  'THIRD_PARTY_LICENSES.md' "$client_pins_changed"

# The npm family alone obliges a second register, which is the only one in this repository that is
# not a file a reader consults: the bundle carries it. `pnpm build` copies
# `frontend/src/Client.App/public/` verbatim into the output, the image and every desktop package
# redistribute that output, and the notice reproduces the licence text of the packages inside it
# under names and versions of their own. So a moved npm pin can leave a published artifact naming a
# version it no longer carries, which no register row and no census would show.
#
# The crate family is excluded rather than forgotten. The desktop shell's crates are a separate
# component under separate terms, they reach no bundle, and `THIRD_PARTY_LICENSES.md` carries them as
# a closure row of their own — so a `Cargo.toml` bump owes the register above and nothing here.
emit_register 'an npm pin that can reach the client bundle moved in a frontend package.json or in frontend/pnpm-lock.yaml' \
  'frontend/src/Client.App/public/THIRD-PARTY-NOTICES.txt' "$client_npm_pins_changed"

exception_added='false'
if jq -e '[.[] | select(.status == "added") | select(.filename | test("^backend/src/.*Exception\\.cs$"))] | length > 0' \
     "$files_json" > /dev/null; then
  exception_added='true'
fi

emit_register 'a new exception type was added under backend/src/' \
  'backend/src/Domain/Failures/MailFathomErrorCode.cs' "$exception_added"

# ---------------------------------------------------------------------------

: > "$work_directory/notes"

if (( ${#notes[@]} > 0 )); then
  printf '%s\n' "${notes[@]}" > "$work_directory/notes"
fi

notes_json="$(jq -R . < "$work_directory/notes" | jq -sc .)"

jq -n \
  --slurpfile tests "$work_directory/tests.ndjson" \
  --slurpfile documentation "$work_directory/documentation.ndjson" \
  --slurpfile registers "$work_directory/registers.ndjson" \
  --argjson notes "$notes_json" \
  '{tests: $tests, documentation: $documentation, registers: $registers, notes: $notes}' \
  > "$output_file"

printf 'Indexed %s source files, %s described paths, and %s register obligations.\n' \
  "$(jq '.tests | length' "$output_file")" \
  "$(jq '.documentation | length' "$output_file")" \
  "$(jq '.registers | length' "$output_file")"
