#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Print the issue numbers merging a pull request will close, one per line, each number once.
#
# The issues a change closes are its stated contract, and merging it acts on every one of them: the
# reviewer is held to that list and the board is written across it. Both of those are statements
# about somebody else's issue, so the list has to be the one GitHub will actually act on rather than
# a reading of the body that agrees with it most of the time.
#
# That is why this asks GitHub. `closingIssuesReferences` is the resolved answer — the same list the
# pull request's own sidebar shows under *Development* and the same one the merge acts on — so a
# keyword this repository failed to match, a spelling it matched too eagerly, and a link somebody
# added through the interface rather than through the body all arrive here as GitHub sees them. The
# parsing this replaces reproduced GitHub's rules in a pattern of our own and agreed with it on every
# pull request this repository has opened; what it could not do is stay agreeing, and the cost of the
# first disagreement is a board write against an issue the change never claimed.
#
# A reference to an issue in another repository is left out, which is the one place this narrows what
# GitHub returns. Every caller acts on the result within this repository — it moves an item on this
# project's board, or holds the change to an acceptance list it fetched from here — and an issue in
# another project is neither of those things.
#
# A limit bounds how many are printed, because a caller performs work per line. What it cut is stated
# on standard error rather than dropped: an issue that closes on merge with its acceptance list
# unread, or with its board item left behind, is exactly the failure a silent ceiling produces.
# Standard error is the channel for it because standard output is the list itself, and callers
# redirect the two to different places.
#
# Usage: collect-closing-issues.sh <repository> <pull-request-number> [limit]
#
# Environment:
#   GH_TOKEN  a token that can read the pull request

set -euo pipefail

repository="${1:?the repository is required}"
pull_request_number="${2:?the pull request number is required}"
limit="${3:-0}"

# The caller's ceiling is applied after the answer arrives rather than through `first:`, because what
# it reports has to be the number of issues this pull request closes *here* — which is only known
# after the issues in other repositories have been dropped. The page bound below is separate and
# deliberately larger than any ceiling a caller passes: a pull request closing more issues than this
# is one whose contract nobody could read anyway.
page_limit=50

references_file="$(mktemp)"
numbers_file="$(mktemp)"
call_error_file="$(mktemp)"
trap 'rm -f "$references_file" "$numbers_file" "$call_error_file"' EXIT

# Resolved as a sibling rather than taken from the caller, so every workflow that runs this script
# gets the same bound on the same call without having to know the helper exists. What it retries and
# what it refuses to is written there.
call_github_api="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/call-github-api.sh"

# The helper's own standard error is held back and forwarded only if the call finally fails, because
# on this script that channel is data rather than a log — the note above about what the ceiling cut,
# which `Fathom review` redirects into `truncation.txt` and pastes verbatim into the published review
# body. A recovered retry writing `failed on attempt 1 of 4` there would arrive in the review under
# the heading reserved for what was dropped, and be read as coverage the pass did not have.
if ! "$call_github_api" graphql \
  -f owner="${repository%/*}" \
  -f name="${repository#*/}" \
  -F number="$pull_request_number" \
  -F first="$page_limit" \
  -f query='
    query($owner: String!, $name: String!, $number: Int!, $first: Int!) {
      repository(owner: $owner, name: $name) {
        pullRequest(number: $number) {
          closingIssuesReferences(first: $first) {
            nodes { number repository { nameWithOwner } }
          }
        }
      }
    }' > "$references_file" 2> "$call_error_file"; then
  cat "$call_error_file" >&2
  exit 1
fi

jq -r --arg repository "$repository" '
  [.data.repository.pullRequest.closingIssuesReferences.nodes[]?
   | select(.repository.nameWithOwner == $repository)
   | .number]
  | unique
  | .[]' "$references_file" > "$numbers_file"

closing_issues="$(wc -l < "$numbers_file")"

if (( limit > 0 && closing_issues > limit )); then
  head -n "$limit" "$numbers_file"

  printf 'The pull request closes %d issues and this run covers the first %d.\n' \
    "$closing_issues" "$limit" >&2
else
  cat "$numbers_file"
fi
