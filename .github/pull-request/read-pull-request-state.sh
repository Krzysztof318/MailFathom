#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Print the state `select-board-status.sh` decides a board status from, as one JSON object.
#
# Every rule there is a question about the same few facts — does the change still merge, is it a
# draft, did the reviewer approve *this* head, and what did the pipeline conclude — so they are read
# in one call rather than one call per rule. The shape is the same whichever way this is asked:
#
#   {"total": <how many pull requests matched>, "pull_requests": [ {...}, ... ]}
#
# and each pull request carries `number`, `mergeable`, `isDraft`, `state`, `labels`, `headRefOid`,
# `reviews`, and `checks`. A review is `{author, state, commit}` and a check is
# `{workflow, name, status, conclusion}`, both as GitHub spells them, which is what lets a fixture
# stand in for the API in the contract suite.
#
# Two ways to ask, because two events answer different questions:
#
# - **Without a number**, every open pull request against the base branch, most recently updated
#   first. That is the push-to-`main` sweep: a merge is what makes another branch stop merging and
#   GitHub raises nothing on the branch it happened to, so all of them are read from the other side.
# - **With a number**, that one pull request, and nothing at all where it has since closed or is
#   against another base. That is a `workflow_run` conclusion, which is about the head of exactly one
#   change; sweeping every open pull request for it would read the same rollup for every other one on
#   every pipeline that finishes anywhere in the repository.
#
# The checks are the last commit's rollup, which is what a reader sees on the pull request: every
# check run on that head and every commit status beside them. A commit status has no `conclusion` of
# its own, so it is mapped onto the two fields a check run carries rather than left as a second shape
# every rule would have to know about.
#
# Usage: read-pull-request-state.sh <repository> <base-branch> <limit> [pull-request-number]
#
# Environment:
#   GH_TOKEN  a token that can read the pull requests

set -euo pipefail

repository="${1:?the repository is required}"
base_branch="${2:?the base branch is required}"
limit="${3:?the pull request limit is required}"
pull_request_number="${4:-}"

# Resolved as a sibling for the reason every other script here resolves it that way: the retry bound
# belongs to the call rather than to whichever workflow made it.
call_github_api="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/call-github-api.sh"

# One ceiling for the checks on a head, and it is a page rather than a rule: a pull request here
# carries around thirty, and a change that somehow produced more would be read as though the ones
# past the page had not run. What it cut is said on standard error, because standard output is the
# answer and a caller redirects the two to different places.
check_limit=100

# The fields both queries select. Written once because two copies of a projection are two things to
# keep agreeing, and the rules read every field in it whichever way the state was asked for.
pull_request_fields='
  fragment pullRequestState on PullRequest {
    number
    mergeable
    isDraft
    state
    baseRefName
    headRefOid
    labels(first: 20) { nodes { name } }
    latestReviews(first: 20) { nodes { author { __typename login } state commit { oid } } }
    commits(last: 1) {
      nodes {
        commit {
          statusCheckRollup {
            contexts(first: $checks) {
              totalCount
              nodes {
                __typename
                ... on CheckRun {
                  name
                  status
                  conclusion
                  checkSuite { workflowRun { workflow { name } } }
                }
                ... on StatusContext { context state }
              }
            }
          }
        }
      }
    }
  }'

# The projection onto the shape the rules read. A commit status reports one `state` where a check run
# reports a status and a conclusion, so `PENDING` and `EXPECTED` become a check that has not
# finished and everything else one that has, which is the same distinction asked of both.
projection='
  def pull_request_state:
    {
      number,
      mergeable,
      isDraft,
      state,
      headRefOid,
      labels: [.labels.nodes[].name],
      reviews: [
        .latestReviews.nodes[]?
        # An App authors a review as `fathom-reviewer[bot]` everywhere a person reads one — the
        # review itself, the REST payload, the prompt naming its own previous verdicts — and as
        # `fathom-reviewer` here, because GraphQL keeps the suffix out of `Bot.login`. The rules read
        # one spelling, so the suffix is put back where the two forms meet rather than left for every
        # caller to remember which API it came from.
        | {
          author: (.author.login + (if .author.__typename == "Bot" then "[bot]" else "" end)),
          state: .state,
          commit: .commit.oid
        }
      ],
      checks: [
        .commits.nodes[0].commit.statusCheckRollup.contexts.nodes[]?
        | if .__typename == "CheckRun"
          then {
            workflow: (.checkSuite.workflowRun.workflow.name // ""),
            name: .name,
            status: .status,
            conclusion: (.conclusion // "")
          }
          else {
            workflow: "",
            name: .context,
            status: (if .state == "PENDING" or .state == "EXPECTED" then "IN_PROGRESS" else "COMPLETED" end),
            conclusion: (if .state == "SUCCESS" then "SUCCESS"
                         elif .state == "FAILURE" or .state == "ERROR" then "FAILURE"
                         else "" end)
          }
          end
      ],
      checksTotal: (.commits.nodes[0].commit.statusCheckRollup.contexts.totalCount // 0)
    };'

answer_file="$(mktemp)"

if [[ -n "$pull_request_number" ]]; then
  "$call_github_api" graphql \
    -f owner="${repository%/*}" \
    -f name="${repository#*/}" \
    -F number="$pull_request_number" \
    -F checks="$check_limit" \
    -f query="
      query(\$owner: String!, \$name: String!, \$number: Int!, \$checks: Int!) {
        repository(owner: \$owner, name: \$name) {
          pullRequest(number: \$number) { ...pullRequestState }
        }
      }
      $pull_request_fields" \
    --jq '.data.repository.pullRequest' > "$answer_file"

  # A pull request that closed while its pipeline was still running, or one against another base, is
  # read out here rather than in the query: asking GitHub by number answers whichever state and base
  # it is in, and neither is a pull request whose merge this pipeline has anything to say about.
  # `total` is then what matched, which is one or none, and the sweep's is what is open against the
  # base — the number the caller reports its ceiling against.
  state="$(
    jq --arg base "$base_branch" "$projection"'
      [. | select(. != null and .state == "OPEN" and .baseRefName == $base) | pull_request_state]
      | {total: length, pull_requests: .}' \
      "$answer_file"
  )"
else
  "$call_github_api" graphql \
    -f owner="${repository%/*}" \
    -f name="${repository#*/}" \
    -f base="$base_branch" \
    -F first="$limit" \
    -F checks="$check_limit" \
    -f query="
      query(\$owner: String!, \$name: String!, \$base: String!, \$first: Int!, \$checks: Int!) {
        repository(owner: \$owner, name: \$name) {
          pullRequests(states: OPEN, baseRefName: \$base, first: \$first,
                       orderBy: {field: UPDATED_AT, direction: DESC}) {
            totalCount
            nodes { ...pullRequestState }
          }
        }
      }
      $pull_request_fields" \
    --jq '.data.repository.pullRequests' > "$answer_file"

  state="$(
    jq "$projection"'
      {total: .totalCount, pull_requests: [.nodes[] | pull_request_state]}' \
      "$answer_file"
  )"
fi

jq -r --argjson limit "$check_limit" \
  '.pull_requests[] | select(.checksTotal > $limit)
   | "::notice::Pull request #\(.number) carries \(.checksTotal) checks and this run read the first \($limit)."' \
  <<< "$state" >&2

jq 'del(.pull_requests[].checksTotal)' <<< "$state"
