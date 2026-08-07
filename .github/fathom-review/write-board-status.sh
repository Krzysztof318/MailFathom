#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Write one `Status` value on the roadmap board, on every issue a pull request's body closes.
#
# `Fathom review` writes the board twice — once when the review starts and once when it concludes —
# and the two writes differ in exactly two things: which value they write, and which values they
# refuse to write over. Everything else is the same walk: read the body, collect what it closes,
# resolve the field and its option by name, find the item on *this* board, and mutate it. That is
# why it lives here rather than twice in the workflow, where the second copy would be the one that
# stops matching the first.
#
# The value and the preserved list are arguments because they are the caller's decision. A verdict
# must not drag a merged item back into review, so it names `Done,Blocked`; the write that announces
# a review is running says something true of any prior state, so it names nothing.
#
# Failures are graded the way the workflow they run in is: it gates nothing, so a red run over a
# board a hand can correct is noise. A missing field or a renamed option is loud, because it is a
# configuration defect that leaves the column empty while the run reports success; a single issue
# that could not be read or moved is a warning naming that issue.
#
# Usage: write-board-status.sh <status> [preserved-statuses]
#
# Environment:
#   GH_TOKEN                   the workflow token, which reads the pull request body
#   BOARD_TOKEN                a classic token carrying the `project` scope, used for the project
#                              calls and nothing else; empty means the board is left alone
#   REPOSITORY                 owner/name of the repository the pull request belongs to
#   PULL_REQUEST_NUMBER        the pull request whose body states the contract
#   BOARD_OWNER                the login the project belongs to
#   BOARD_NUMBER               the project number in that login's namespace
#   STATUS_FIELD               the single-select field to write
#   CLOSING_REFERENCES_SCRIPT  path to `collect-closing-references.sh`
#   CLOSING_REFERENCE_LIMIT    how many closing references to act on

set -euo pipefail

status="${1:?the status to write is required}"
preserved_statuses="${2:-}"

: "${BOARD_OWNER:?BOARD_OWNER must name the login the project belongs to}"
: "${BOARD_NUMBER:?BOARD_NUMBER must name the project}"
: "${STATUS_FIELD:?STATUS_FIELD must name the field to write}"
: "${REPOSITORY:?REPOSITORY must name the repository}"
: "${PULL_REQUEST_NUMBER:?PULL_REQUEST_NUMBER must name the pull request}"
: "${CLOSING_REFERENCES_SCRIPT:?CLOSING_REFERENCES_SCRIPT must point at collect-closing-references.sh}"

if [[ -z "${BOARD_TOKEN:-}" ]]; then
  echo '::notice::BOARD_PROJECT_TOKEN is not set, so no board status was written.'
  exit 0
fi

body_file="$(mktemp)"
issues_file="$(mktemp)"

gh api "repos/${REPOSITORY}/pulls/${PULL_REQUEST_NUMBER}" --jq '.body // ""' > "$body_file"

# A pull request that closes nothing moves nothing, which is the ordinary shape of a release's
# changelog half and of any change opened without a contract. The reviewer says so about the
# contract it could not read; this says so about the board and ends green.
"$CLOSING_REFERENCES_SCRIPT" "$body_file" "$REPOSITORY" "${CLOSING_REFERENCE_LIMIT:-0}" \
  > "$issues_file"

if [[ ! -s "$issues_file" ]]; then
  echo '::notice::The pull request closes no issue, so there is no board item to move.'
  exit 0
fi

field_file="$(mktemp)"

# The field and its options are read once for the whole run rather than per issue, and they are read
# by name because a name is what the workflow can state and an id is not.
if ! GH_TOKEN="$BOARD_TOKEN" gh api graphql \
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

while IFS= read -r issue_number; do
  [[ -n "$issue_number" ]] || continue

  item_file="$(mktemp)"

  # Per-issue failures are warnings rather than errors. This workflow gates nothing, so a red run
  # over a board that can be corrected by hand is noise a reader has to dismiss, and the warning
  # still says which issue was not moved.
  if ! GH_TOKEN="$BOARD_TOKEN" gh api graphql \
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

  if [[ -n "$preserved_statuses" && ",${preserved_statuses}," == *",${current_status},"* ]]; then
    printf '::notice::Issue %s is %s, which a review verdict does not overwrite.\n' \
      "$issue_number" "$current_status"
    continue
  fi

  if [[ "$current_status" == "$status" ]]; then
    printf 'Issue %s is already %s.\n' "$issue_number" "$status"
    continue
  fi

  if ! GH_TOKEN="$BOARD_TOKEN" gh api graphql \
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

printf 'Moved %s issues to %s.\n' "$moved" "$status"
