#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Write one `Status` value on the roadmap board, on every issue merging a pull request will close.
#
# Three writes share this walk today: `Fathom review` announcing that a review is running and
# recording its verdict, and `Apply pull request rules` moving an item whose pull request stopped
# merging. They differ in which value they write and in which statuses they may write it over;
# everything else is the same — collect what the pull request closes, resolve the field and its
# option by name, find the item on *this* board, and mutate it. That is why the walk lives here
# rather than once per caller, where the second copy is the one that stops matching the first.
#
# Both lists of statuses are arguments, because which statuses a write may act on is the caller's
# statement about its own authority rather than a property of writing. They are the two directions
# of one question and a caller uses whichever one says what it means:
#
# - The preserved list is what a write refuses to overwrite. `Done` is the merge and the close and
#   `Blocked` is the one status a hand writes, and neither is a statement a review gets to erase
#   from either end of itself.
# - The required list is what a write may act on and nothing else. A rule that says *an approved
#   change stopped merging* is only true of an item that is currently approved, so it names the one
#   status it is entitled to move and leaves every other item alone rather than enumerating the
#   statuses it would otherwise trample.
#
# Failures are graded the way the workflows they run in are: neither gates anything, so a red run
# over a board a hand can correct is noise. A missing field or a renamed option is loud, because it
# is a configuration defect that leaves the column empty while the run reports success; a single
# issue that could not be read or moved is a warning naming that issue.
#
# Usage: write-board-status.sh <status> [preserved-statuses] [required-statuses]
#
# Environment:
#   GH_TOKEN                the workflow token, which reads the pull request
#   BOARD_TOKEN             a classic token carrying the `project` scope, used for the project calls
#                           and nothing else; empty means the board is left alone
#   REPOSITORY              owner/name of the repository the pull request belongs to
#   PULL_REQUEST_NUMBER     the pull request whose closing references state the contract
#   BOARD_OWNER             the login the project belongs to
#   BOARD_NUMBER            the project number in that login's namespace
#   STATUS_FIELD            the single-select field to write
#   CLOSING_ISSUES_SCRIPT   path to `collect-closing-issues.sh`
#   CLOSING_ISSUE_LIMIT     how many closing issues to act on
#   BOARD_WRITE_LIMIT_SECONDS  how long the walk over those issues may take

set -euo pipefail

status="${1:?the status to write is required}"
preserved_statuses="${2:-}"
required_statuses="${3:-}"

: "${BOARD_OWNER:?BOARD_OWNER must name the login the project belongs to}"
: "${BOARD_NUMBER:?BOARD_NUMBER must name the project}"
: "${STATUS_FIELD:?STATUS_FIELD must name the field to write}"
: "${REPOSITORY:?REPOSITORY must name the repository}"
: "${PULL_REQUEST_NUMBER:?PULL_REQUEST_NUMBER must name the pull request}"
: "${CLOSING_ISSUES_SCRIPT:?CLOSING_ISSUES_SCRIPT must point at collect-closing-issues.sh}"

if [[ -z "${BOARD_TOKEN:-}" ]]; then
  echo '::notice::BOARD_PROJECT_TOKEN is not set, so no board status was written.'
  exit 0
fi

# Resolved as a sibling for the reason `collect-closing-issues.sh` resolves it that way: the bound
# belongs to the call rather than to whichever workflow made it. Every project call below goes
# through it, the mutation included — it writes an option id this run already read, so repeating it
# converges on the same value rather than adding a second record.
call_github_api="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/call-github-api.sh"

issues_file="$(mktemp)"

# A pull request that closes nothing moves nothing, which is the ordinary shape of a release's
# changelog half and of any change opened without a contract. The reviewer says so about the
# contract it could not read; this says so about the board and ends green.
"$CLOSING_ISSUES_SCRIPT" "$REPOSITORY" "$PULL_REQUEST_NUMBER" "${CLOSING_ISSUE_LIMIT:-0}" \
  > "$issues_file"

if [[ ! -s "$issues_file" ]]; then
  echo '::notice::The pull request closes no issue, so there is no board item to move.'
  exit 0
fi

field_file="$(mktemp)"

# The field and its options are read once for the whole run rather than per issue, and they are read
# by name because a name is what the workflow can state and an id is not.
if ! GH_TOKEN="$BOARD_TOKEN" "$call_github_api" graphql \
    -f owner="$BOARD_OWNER" \
    -F number="$BOARD_NUMBER" \
    -f field="$STATUS_FIELD" \
    -f query='
      query($owner: String!, $number: Int!, $field: String!) {
        user(login: $owner) {
          projectV2(number: $number) {
            id
            field(name: $field) {
              ... on ProjectV2SingleSelectField { id options { id name } }
            }
          }
        }
      }' > "$field_file"; then
  echo '::error::The board could not be read. Check that BOARD_PROJECT_TOKEN still carries the project scope.'
  exit 1
fi

project_id="$(jq -r '.data.user.projectV2.id // ""' "$field_file")"
field_id="$(jq -r '.data.user.projectV2.field.id // ""' "$field_file")"
option_id="$(
  jq -r --arg name "$status" \
    '[.data.user.projectV2.field.options[]? | select(.name == $name) | .id][0] // ""' \
    "$field_file"
)"

# A missing field or a renamed option is a configuration defect rather than a transient failure, so
# it fails loudly and names the value it looked for. The alternative is a run that reports success
# while the column it was added to write stays empty.
if [[ -z "$project_id" || -z "$field_id" || -z "$option_id" ]]; then
  printf '::error::The board has no %s option named %s on project %s.\n' \
    "$STATUS_FIELD" "$status" "$BOARD_NUMBER"
  exit 1
fi

moved=0
unmoved=''

# The third loop in this pipeline that calls once per record, and a retry budget is per call: this
# one spends two of them per issue, the item read and the mutation, so a project endpoint that has
# started stalling turns a walk over five issues into minutes rather than the second it takes when
# the API answers. Neither workflow that calls this declares `timeout-minutes`, and
# `Apply pull request rules` calls it once per open pull request, so the degradation multiplies
# there rather than being capped by the job.
#
# The bound is therefore the window plus one issue's calls: the check is made before an issue is
# read, so an issue already in flight finishes, and what a caller can predict is that the walk stops
# asking for more work once the window is gone. What it buys is a board write that gives up rather
# than one that holds a job open, which is the right trade for a step that gates nothing — the
# issues it did not reach are named, and a hand moves them.
board_write_limit_seconds="${BOARD_WRITE_LIMIT_SECONDS:-120}"
board_write_started_at="$(date -u +%s)"

while IFS= read -r issue_number; do
  [[ -n "$issue_number" ]] || continue

  if (( $(date -u +%s) - board_write_started_at >= board_write_limit_seconds )); then
    unmoved="${unmoved:+$unmoved, }$issue_number"
    continue
  fi

  item_file="$(mktemp)"

  # Per-issue failures are warnings rather than errors. This workflow gates nothing, so a red run
  # over a board that can be corrected by hand is noise a reader has to dismiss, and the warning
  # still says which issue was not moved.
  if ! GH_TOKEN="$BOARD_TOKEN" "$call_github_api" graphql \
      -f owner="${REPOSITORY%/*}" \
      -f name="${REPOSITORY#*/}" \
      -F number="$issue_number" \
      -f field="$STATUS_FIELD" \
      -f query='
        query($owner: String!, $name: String!, $number: Int!, $field: String!) {
          repository(owner: $owner, name: $name) {
            issue(number: $number) {
              projectItems(first: 20) {
                nodes {
                  id
                  project { id }
                  status: fieldValueByName(name: $field) {
                    ... on ProjectV2ItemFieldSingleSelectValue { name }
                  }
                }
              }
            }
          }
        }' > "$item_file"; then
    printf '::warning::Issue %s could not be read from the board.\n' "$issue_number"
    continue
  fi

  # The item on *this* board. An issue can sit on several projects, and the status of one says
  # nothing about the others.
  entry="$(
    jq -r --arg project "$project_id" \
      '[.data.repository.issue.projectItems.nodes[]?
        | select(.project.id == $project)
        | "\(.id) \(.status.name // "")"][0] // ""' \
      "$item_file"
  )"

  if [[ -z "$entry" ]]; then
    printf '::warning::Issue %s is not on project %s, so nothing was moved.\n' \
      "$issue_number" "$BOARD_NUMBER"
    continue
  fi

  item_id="${entry%% *}"
  current_status="${entry#* }"

  # The allowlist is asked first, because a caller that states one has said what it is entitled to
  # move and everything outside that list is outside its authority — including the statuses the
  # blocklist names, which is why a caller passing a required list need not repeat them.
  if [[ -n "$required_statuses" && ",${required_statuses}," != *",${current_status},"* ]]; then
    printf '::notice::Issue %s is %s rather than %s, so it is left where it stands.\n' \
      "$issue_number" "${current_status:-in no status}" "$required_statuses"
    continue
  fi

  if [[ -n "$preserved_statuses" && ",${preserved_statuses}," == *",${current_status},"* ]]; then
    printf '::notice::Issue %s is %s, which this write does not overwrite.\n' \
      "$issue_number" "$current_status"
    continue
  fi

  if [[ "$current_status" == "$status" ]]; then
    printf 'Issue %s is already %s.\n' "$issue_number" "$status"
    continue
  fi

  if ! GH_TOKEN="$BOARD_TOKEN" "$call_github_api" graphql \
      -f project="$project_id" \
      -f item="$item_id" \
      -f field="$field_id" \
      -f option="$option_id" \
      -f query='
        mutation($project: ID!, $item: ID!, $field: ID!, $option: String!) {
          updateProjectV2ItemFieldValue(input: {
            projectId: $project
            itemId: $item
            fieldId: $field
            value: {singleSelectOptionId: $option}
          }) {
            projectV2Item { id }
          }
        }' --silent; then
    printf '::warning::Issue %s could not be moved to %s.\n' "$issue_number" "$status"
    continue
  fi

  moved=$(( moved + 1 ))
  printf 'Issue %s moved from %s to %s.\n' \
    "$issue_number" "${current_status:-no status}" "$status"
done < "$issues_file"

if [[ -n "$unmoved" ]]; then
  printf '::warning::Writing the board took longer than %ss, so issues %s were left where they stand.\n' \
    "$board_write_limit_seconds" "$unmoved"
fi

printf 'Moved %s issues to %s.\n' "$moved" "$status"
