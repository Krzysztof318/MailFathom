#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Report what the working tree obliges the rest of the repository to do.
#
# `Fathom review` indexes this on every published pull request, and by then the change is written.
# `$review-change` is the review an agent performs before that, on a diff that is still being
# corrected, so the same question is worth asking there — the whole point of the index is that the
# defect is the *absence* of a second file, and that costs the least to fix while the first file is
# still open.
#
# The indexing itself is not implemented here. `.github/fathom-review/index-obligations.sh` owns it,
# and this is the adapter that gives it a local diff instead of a pull request: the pipeline hands it
# the `files.json` GitHub returns, and this builds a document of the same shape from
# `git diff <base>`. One implementation, two callers, so a rule cannot hold in review and lapse in the
# pipeline.
#
# It reports and never gates. Nothing it prints is a finding — the index is derived from file names
# and declared markers, so it points at obligations a change does not always incur — and a row
# becomes a finding only by being confirmed in the file it points at. Exiting non-zero on a row would
# turn "look here" into "this is wrong", which is the one thing this must not assert.
#
# Usage: review-obligations.sh [base-ref]

set -euo pipefail

base_ref="${1:-origin/main}"

repository_root="$(git rev-parse --show-toplevel)"
index_script="$repository_root/.github/fathom-review/index-obligations.sh"

if ! git rev-parse --verify --quiet "$base_ref" > /dev/null; then
  printf 'The base ref %s does not resolve. Fetch it, or name another one.\n' "$base_ref" >&2
  exit 1
fi

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

files_json="$work_directory/files.json"
obligations_json="$work_directory/obligations.json"

# The comparison runs against the working tree rather than against `HEAD`, because a review here
# happens while the change is still being written: what is committed, what is staged, and what is
# neither are one change to the reader, and reviewing only the committed part would report
# obligations the uncommitted part has already discharged.
: > "$work_directory/records"

while IFS=$'\t' read -r status path renamed_path; do
  [[ -n "$status" ]] || continue

  local_status='modified'
  previous_path=''

  case "${status:0:1}" in
    A) local_status='added' ;;
    D) local_status='removed' ;;
    R)
      local_status='renamed'
      previous_path="$path"
      path="$renamed_path"
      ;;
    *) local_status='modified' ;;
  esac

  # The patch carries one thing the index needs and the name list cannot: whether a test this change
  # adds names the type of a source file it also changed. A removed file has no patch worth reading.
  #
  # It reaches `jq` as a file rather than as an argument, because Linux caps a *single* argv string
  # at 32 pages — 128 KiB — however much room the whole argument list has. A patch past that fails
  # `execve` with `E2BIG`, which under `set -e` ends the run rather than dropping one record: the
  # whole reading is lost on exactly the large change most likely to owe something.
  : > "$work_directory/patch"
  if [[ "$local_status" != 'removed' ]]; then
    git diff "$base_ref" -- "$path" | sed -n '/^@@/,$p' > "$work_directory/patch"
  fi

  # `--rawfile` reads the file verbatim, trailing newline and all, where the command substitution it
  # replaces dropped one. The trailing newlines are stripped back off so the record keeps the shape
  # the pipeline's own `files.json` has, which is what lets both callers share one index.
  jq -nc \
    --arg filename "$path" \
    --arg previous "$previous_path" \
    --arg status "$local_status" \
    --rawfile patch "$work_directory/patch" \
    '{filename: $filename,
      previous_filename: (if $previous == "" then null else $previous end),
      status: $status,
      patch: ($patch | sub("\n+$"; "") | if . == "" then null else . end)}' \
    >> "$work_directory/records"
done < <(git diff --name-status --find-renames "$base_ref")

jq -s . "$work_directory/records" > "$files_json"

# A file git does not track yet is in no diff, and a new class that owes a test is exactly the shape
# it takes. `scripts/verify-full.sh` refuses a leftover untracked path for the same reason, so saying
# it here costs one line and closes the gap where this report would otherwise describe less than the
# change while looking complete.
untracked="$(git ls-files --others --exclude-standard)"

if [[ -n "$untracked" ]]; then
  printf 'These paths are untracked, so no diff contains them and nothing below accounts for them:\n' >&2
  printf '  %s\n' $untracked >&2
  printf 'Stage them and run this again.\n\n' >&2
fi

if [[ "$(jq 'length' "$files_json")" == '0' ]]; then
  printf 'Nothing has changed against %s, so there is nothing to be obliged by.\n' "$base_ref"
  exit 0
fi

"$index_script" "$repository_root" "$files_json" "$obligations_json" > /dev/null

printf 'Obligations this change triggers, against %s.\n' "$base_ref"

printf '\nTests\n'
# The two stacks are read differently and the sentences say which, because an empty list means a
# different thing in each: the service's edge is a type name searched for across its test tree, so
# nothing found may mean the name is simply not written there, while the client's is the sibling file
# `frontend/tests/AGENTS.md` requires, so nothing found means that file does not exist.
jq -r '
  if (.tests | length) == 0 then
    "  No production file under backend/src/ or under a client package src/ changed."
  else
    .tests[]
    | (.referencing_tests | map(select(.changed_by_this_pull_request))) as $touched
    | (.path | startswith("frontend/")) as $client
    | if (.referencing_tests | length) == 0 then
        if $client then
          "  \(.path)\n    No \(.type).test.ts or \(.type).test.tsx sits beside it."
        else
          "  \(.path)\n    Nothing under backend/tests/ names \(.type)."
        end
      elif ($touched | length) == 0 then
        (if $client then
          "  \(.path)\n    The test beside it is not in this change:\n"
        else
          "  \(.path)\n    \(.referencing_test_count) test file(s) name \(.type) and this change touches none:\n"
        end)
        + (.referencing_tests | map("      \(.path)") | join("\n"))
      else
        "  \(.path)\n    Covered: this change touches "
        + ($touched | map(.path) | join(", ")) + "."
      end
  end' "$obligations_json"

printf '\nDocumentation\n'
jq -r '
  if (.documentation | length) == 0 then
    "  No changed path is covered by a describes: marker."
  else
    .documentation[]
    | . as $entry
    | (.describing_documents | map(select(.changed_by_this_pull_request | not))) as $untouched
    | if ($untouched | length) == 0 then
        "  \(.path)\n    Covered: every page describing it is in this change."
      else
        "  \(.path)\n" + ($untouched | map("      \(.path) — not in this change") | join("\n"))
      end
  end' "$obligations_json"

printf '\nRegisters\n'
jq -r '
  if (.registers | length) == 0 then
    "  This change triggers none."
  else
    .registers[]
    | "  \(.trigger)\n    \(.register) — "
      + (if .register_changed then "in this change." else "not in this change." end)
  end' "$obligations_json"

notes="$(jq -r '.notes[]' "$obligations_json")"
if [[ -n "$notes" ]]; then
  printf '\nNotes\n'
  printf '  %s\n' "$notes"
fi

printf '\nNone of this is a finding. A row becomes one only by being confirmed in the file it points\n'
printf 'at: the behavior no test reaches, the sentence that stopped being true, the row that is\n'
printf 'missing. A rename owes no test, and a page whose marker covers a path may say nothing about\n'
printf 'the part that moved.\n'
