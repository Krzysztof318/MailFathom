#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Pair every path that landed under a pull request with every path the pull request changes that
# it is related to.
#
# Two changes git merges without a conflict can still disagree: a page one of them adds may describe
# a file the other one changes, or a rule one of them writes into an `AGENTS.md` may govern a file
# the other one changes without following it. No test sees either, so `Coherence` asks this instead,
# and a pair it returns is what somebody has to read before the pull request merges.
#
# A landed path and a changed path are related when
#
#   - they are the same prose file — a `*.md` anywhere, an `AGENTS.md` or an ADR among them, or
#     anything under `design/`. Shared code with no prose link is not a pair: the checks a merge
#     queue runs on the combined tree are what judge code against code;
#   - either one is a page whose `describes:` marker covers the other;
#   - either one is an `AGENTS.md` whose directory contains the other.
#
# The markers are read from the repository root given, which is the combined tree — the base with
# the pull request applied. That is what makes both orders a pair: a page that landed and describes
# a path this pull request changes, and a page this pull request adds that describes a path that
# landed. They are resolved by `.github/fathom-review/index-obligations.sh`, the one implementation
# of git's `:(glob)` semantics `Fathom review` already reads markers through, so a page the review
# says describes a path is a page this check pairs with it.
#
# It reads files and calls no API, which is what lets the contract suite run it against a fixture
# tree. The pairs come out as a JSON array of `{landed, changed, reason}`, empty when nothing is
# related. The reason carries the direction, because the message a reader acts on has to say which
# side holds the page or the `AGENTS.md`: `same prose file`, `landed describes changed`,
# `changed describes landed`, `landed governs changed`, or `changed governs landed`.
#
# Usage: relate-changes.sh <repository-root> <landed-paths> <changed-paths> <output.json>

set -euo pipefail

repository_root="${1:?the repository root is required}"
landed_paths="${2:?the file listing the paths that landed is required}"
changed_paths="${3:?the file listing the paths the pull request changes is required}"
output_file="${4:?the output path is required}"

index_script="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fathom-review/index-obligations.sh"

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

grep -v '^$' "$landed_paths" | sort -u > "$work_directory/landed" || true
grep -v '^$' "$changed_paths" | sort -u > "$work_directory/changed" || true

# Every path on either side goes to the index as one change, so its documentation section names the
# pages describing each of them. A page describing a landed path and a page describing a changed path
# are both in that answer, which is the whole of the relation in both directions.
sort -u "$work_directory/landed" "$work_directory/changed" \
  | jq -R . \
  | jq -s 'map({filename: ., status: "modified", patch: null})' \
  > "$work_directory/files.json"

bash "$index_script" "$repository_root" "$work_directory/files.json" "$work_directory/obligations.json" > /dev/null

jq -r '.documentation[] | .path as $described | .describing_documents[] | "\(.path)\t\($described)"' \
  "$work_directory/obligations.json" > "$work_directory/describes"

awk -F'\t' '
  function is_prose(path) {
    return path ~ /\.md$/ || path ~ /^design\//
  }

  function governs(instructions, path,    directory) {
    if (instructions !~ /(^|\/)AGENTS\.md$/) return 0
    directory = instructions
    sub(/\/?AGENTS\.md$/, "", directory)
    return directory == "" || index(path, directory "/") == 1
  }

  FILENAME == ARGV[1] { describes[$1 SUBSEP $2] = 1; next }
  FILENAME == ARGV[2] { landed[$0] = 1; next }
  FILENAME == ARGV[3] { changed[$0] = 1; next }

  END {
    for (l in landed) {
      for (c in changed) {
        reason = ""
        if (l == c) {
          if (is_prose(l)) reason = "same prose file"
        } else if ((l SUBSEP c) in describes) {
          reason = "landed describes changed"
        } else if ((c SUBSEP l) in describes) {
          reason = "changed describes landed"
        } else if (governs(l, c)) {
          reason = "landed governs changed"
        } else if (governs(c, l)) {
          reason = "changed governs landed"
        }
        if (reason != "") print l "\t" c "\t" reason
      }
    }
  }
' "$work_directory/describes" "$work_directory/landed" "$work_directory/changed" \
  | LC_ALL=C sort \
  | jq -R 'split("\t") | {landed: .[0], changed: .[1], reason: .[2]}' \
  | jq -s . \
  > "$output_file"
