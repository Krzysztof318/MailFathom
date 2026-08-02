#!/usr/bin/env bash
# Print the issue numbers a pull request body closes, one per line, each number once.
#
# The issues a change closes are its stated contract, and merging it marks every one of them done —
# so a reviewer that sees only some of them can approve a change that closes an issue it did not
# finish. That makes "which issues" a decision worth pinning rather than a grep written inline.
#
# Every keyword GitHub acts on is matched, not the three a body usually spells: GitHub closes an
# issue on `close`, `closes`, `closed`, `fix`, `fixes`, `fixed`, `resolve`, `resolves`, and
# `resolved`, so a body saying `Fixed #123` closes 123 whatever this script thinks. Matching fewer
# than GitHub does means the merge closes an issue the review never read.
#
# Both spellings of the reference are matched for the same reason: `#123` and the full URL to an
# issue in this repository are one thing to GitHub. A cross-repository `owner/repo#123` is
# deliberately not, because this reviewer reads one repository and an issue it cannot fetch is not
# something it can hold the change to.
#
# A bare `#123` with no keyword before it is a mention rather than a contract — "depends on #123",
# "as #123 describes" — and GitHub closes nothing on it. It is left out for that reason: the question
# here is what merging this pull request completes.
#
# A limit bounds how many are printed, because the caller fetches one issue per line and a body is
# untrusted text that can name five hundred references as easily as five. What the limit cut is
# stated on standard error rather than dropped: this workflow's own rule is that every ceiling
# reports what it discarded, and a reference nobody was told about is an issue that closes on merge
# with its acceptance list unread. Standard error is the channel for it because standard output is
# the list itself, and the caller redirects the two to different places.
#
# Usage: collect-closing-references.sh <body-file> [repository] [limit]

set -euo pipefail

body_file="${1:?the pull request body file is required}"
repository="${2:-}"
limit="${3:-0}"

[[ -s "$body_file" ]] || exit 0

keywords='close[sd]?|fix(e[sd])?|resolve[sd]?'

{
  # `-i` for the keyword, which a body writes as `Closes` as often as `closes`. The separator is
  # whitespace or a colon, which is what GitHub accepts between the keyword and the reference.
  grep -oiE "(${keywords})[[:space:]]*:?[[:space:]]*#[0-9]+" "$body_file" \
    | grep -oE '[0-9]+' \
    || true

  if [[ -n "$repository" ]]; then
    # The URL form, anchored to this repository so a link to another project's issue is not read as
    # a contract this change can be held to. `[0-9]+$` reads the number off the end of the matched
    # URL rather than the first digits in it, which would otherwise be whatever digits the owner's
    # login happens to contain.
    grep -oiE "(${keywords})[[:space:]]*:?[[:space:]]*https://github\.com/${repository}/issues/[0-9]+" \
      "$body_file" \
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
      printf "The pull request body closes %d issues and this review covers the first %d.\n", \
        kept, limit > "/dev/stderr"
    }
  }'
