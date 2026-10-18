#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# List what one side of a change newly defines: the functions, types, methods, and exports on the
# lines it adds, and every shell script it adds whole.
#
# This is the prefilter in front of the model `Duplication` asks. It decides whether a model is
# asked at all, never whether two definitions are the same, so it is tuned to let a candidate
# through rather than to judge one: a definition listed here that duplicates nothing costs one model
# call answered `none`, and a definition missed here is a duplicate nobody looks at. Workflows are
# read for shell functions too, because their `run:` blocks define helpers of their own.
#
# Generated code is left out — EF Core migrations and designer files, source-generator output, and
# rendered golden manifests — since nobody writes it and nobody can be asked not to repeat it.
#
# Each line of the output is `<path>\t<name>`, sorted and unique, and the output is empty when the
# side defines nothing. It reads git objects only, so it runs in any checkout holding both commits.
#
# Usage: list-new-definitions.sh <from-commit> <to-commit> <output>

set -euo pipefail

from_commit="${1:?the commit the side starts from is required}"
to_commit="${2:?the commit the side ends at is required}"
output_file="${3:?the output path is required}"

is_generated='(^|/)Migrations/|\.Designer\.cs$|\.g\.cs$|/ci/golden/|\.d\.ts$'

{
  git diff --no-ext-diff --no-renames --diff-filter=A --name-only "$from_commit" "$to_commit" \
    | grep -E '\.sh$' \
    | grep -vE "$is_generated" \
    | awk '{ name = $0; sub(/.*\//, "", name); print $0 "\t" name }' \
    || true

  git diff --no-ext-diff --no-renames --no-color --unified=0 "$from_commit" "$to_commit" \
    | IS_GENERATED="$is_generated" awk '
        function emit(name) { if (name != "") print path "\t" name }

        /^\+\+\+ / {
          path = substr($0, 7)
          kind = ""
          if (path ~ ENVIRON["IS_GENERATED"]) next
          if (path ~ /\.(sh|bash|ya?ml)$/) kind = "shell"
          else if (path ~ /\.cs$/) kind = "csharp"
          else if (path ~ /\.(ts|tsx|js|mjs|cjs)$/) kind = "script"
          else if (path ~ /\.rs$/) kind = "rust"
          next
        }

        !/^\+/ || kind == "" { next }

        {
          line = substr($0, 2)

          if (kind == "shell" && match(line, /^[[:space:]]*(function[[:space:]]+)?[A-Za-z_][A-Za-z0-9_]*[[:space:]]*\(\)/)) {
            name = substr(line, RSTART, RLENGTH)
            sub(/^[[:space:]]*(function[[:space:]]+)?/, "", name)
            sub(/[[:space:]]*\(\)$/, "", name)
            emit(name)
          }

          if (kind == "csharp") {
            if (match(line, /(class|record|struct|interface|enum)[[:space:]]+[A-Z][A-Za-z0-9_]*/)) {
              name = substr(line, RSTART, RLENGTH)
              sub(/^[a-z]+[[:space:]]+/, "", name)
              emit(name)
            } else if (line ~ /^[[:space:]]*(public|internal|private|protected)[[:space:]]/ \
                && match(line, /[A-Z][A-Za-z0-9_]*[[:space:]]*(<[^>]*>)?[[:space:]]*\(/)) {
              name = substr(line, RSTART, RLENGTH)
              sub(/[[:space:]]*(<[^>]*>)?[[:space:]]*\($/, "", name)
              emit(name)
            }
          }

          if (kind == "script") {
            if (match(line, /function\*?[[:space:]]+[A-Za-z_$][A-Za-z0-9_$]*/)) {
              name = substr(line, RSTART, RLENGTH)
              sub(/^function\*?[[:space:]]+/, "", name)
              emit(name)
            } else if (match(line, /^[[:space:]]*export[[:space:]]+(default[[:space:]]+)?(abstract[[:space:]]+)?(const|let|class|type|interface|enum)[[:space:]]+[A-Za-z_$][A-Za-z0-9_$]*/)) {
              name = substr(line, RSTART, RLENGTH)
              sub(/.*[[:space:]]/, "", name)
              emit(name)
            } else if (match(line, /^[[:space:]]*const[[:space:]]+[A-Za-z_$][A-Za-z0-9_$]*[[:space:]]*=[[:space:]]*(async[[:space:]]*)?(\(|[A-Za-z_$][A-Za-z0-9_$]*[[:space:]]*=>)/)) {
              name = substr(line, RSTART, RLENGTH)
              sub(/^[[:space:]]*const[[:space:]]+/, "", name)
              sub(/[[:space:]]*=.*$/, "", name)
              emit(name)
            }
          }

          if (kind == "rust" && match(line, /fn[[:space:]]+[A-Za-z_][A-Za-z0-9_]*/)) {
            name = substr(line, RSTART, RLENGTH)
            sub(/^fn[[:space:]]+/, "", name)
            emit(name)
          }
        }
      '
} | LC_ALL=C sort -u > "$output_file"
