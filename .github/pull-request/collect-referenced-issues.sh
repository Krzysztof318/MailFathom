#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Print the issue numbers a pull request body refers to, one per line, each number once.
#
# This is the superset of `collect-closing-issues.sh`, and the two exist side by side because they
# answer different questions. That one asks *what does merging this complete*, and it asks GitHub,
# because the answer is the list a merge will act on. This one asks *what is this change about*, and
# a mention answers it: a pull request that says "part of #123" or "the ceiling #124 asked for" is a
# change somebody would want read in the light of that issue whether or not merging closes it. There
# is no resolved answer to ask GitHub for, which is why this one is a reading of the body and the
# other is not. Labelling reads this one; the reviewer's collection and every board write read the
# other, because what they act on is only what the merge closes.
#
# Both spellings are matched: `#123` and the full URL to an issue in this repository are one thing to
# GitHub. A cross-repository `owner/repo#123` is deliberately neither — the number belongs to another
# project's namespace, and reading the `#123` out of it would resolve to whichever local issue
# happens to hold that number, which is a label earned from an issue nobody named.
#
# Every keyword is matched too, by construction: `Closes #123` contains `#123`, so nothing has to
# know which keywords GitHub acts on to see the issue behind one.
#
# A limit bounds how many are printed, because the caller fetches one issue per line and a body is
# untrusted text that can name five hundred references as easily as five. What the limit cut is
# stated on standard error rather than dropped, so a caller can report it; standard output is the
# list alone.
#
# Usage: collect-referenced-issues.sh <body-file> [repository] [limit]

set -euo pipefail

body_file="${1:?the pull request body file is required}"
repository="${2:-}"
limit="${3:-0}"

[[ -s "$body_file" ]] || exit 0

# What may stand before a `#` for the number after it to be an issue in this repository. A letter or
# a digit means `owner/repo#123` or a word running into it; a `/` means the same reference written
# with the separator adjacent; `_`, `.`, and `-` are the remaining characters a repository name is
# spelled with. Anything else — whitespace, a bracket, a comma, the start of the line — is a
# reference standing on its own.
#
# `[^...]` rather than a word boundary, which POSIX ERE has no portable spelling for. The character
# is captured into the match and discarded by the digit extraction below, and excluding digits is
# what keeps that extraction from reading the boundary character as part of the number.
reference_start='(^|[^[:alnum:]/_.-])'

{
  grep -oE "${reference_start}#[0-9]+" "$body_file" \
    | grep -oE '[0-9]+' \
    || true

  if [[ -n "$repository" ]]; then
    # The URL form, anchored to this repository so a link to another project's issue is not read as
    # one of ours. `[0-9]+$` reads the number off the end of the matched URL rather than the first
    # digits in it, which would otherwise be whatever digits the owner's login happens to contain.
    grep -oiE "${reference_start}https://github\.com/${repository}/issues/[0-9]+" "$body_file" \
      | grep -oE '[0-9]+$' \
      || true
  fi
} | awk -v limit="$limit" '
  !seen[$0]++ {
    kept++

    if (limit > 0 && kept > limit) {
      dropped++
      next
    }

    print
  }

  END {
    if (dropped > 0) {
      printf "The pull request body refers to %d issues and this covers the first %d.\n", \
        kept, limit > "/dev/stderr"
    }
  }'
