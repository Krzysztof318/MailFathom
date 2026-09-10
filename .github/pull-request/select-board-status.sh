#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Print the board status a pull request's current state earns, or nothing.
#
# This is the one place those conditions live, the way `select-labels.sh` is the one place the label
# conditions live, so a new rule is an edit here rather than another workflow with another trigger
# and another run for one small thing. `Apply pull request rules` holds none of them: it collects the
# state, calls this once per pull request, and writes whatever comes back.
#
# The answer is one line of three tab-separated fields, and the caller passes the last two to
# `write-board-status.sh` unread:
#
#   <status>  <required-statuses>  <preserved-statuses>
#
# A rule states its own authority in those two fields rather than leaving it to the caller, because
# what a rule may overwrite follows from what the rule means. Both are comma-separated and either may
# be empty: an empty required list means any status may be moved, and an empty preserved list means
# none is refused.
#
# Four rules are implemented, in this order:
#
# - A check that failed on the head earns `Changes requested`, whatever the review said. The column
#   says the change is waiting on the agent rather than on the owner, and a red pipeline is exactly
#   that whether the objection was written by a reader or produced by a build — so this rule names no
#   required status and describes whatever item it finds, the way a review's verdict does.
#
# - A head the reviewer withheld approval on earns `Changes requested` as well. That verdict is
#   published as a `COMMENT` review on purpose, so that it cannot block a merge, and GitHub's
#   built-in `Code changes requested` workflow therefore never fires for it — the column that says a
#   change is waiting on the agent has no other writer.
#
# - An approved head earns `Ready to merge` where it still merges and `Conflicts` where it does not.
#   Both say the reviewer and the pipelines agree about the change and differ only in who acts next —
#   the owner pressing the button, or the agent rebasing — so the two carry one authority. That is
#   what moves an approval that met a conflict before its pipelines finished, which never reached
#   `Ready to merge` and would otherwise sit in `In review`.
#
# - A pull request that no longer merges and that none of the rules above describes moves the issues
#   it closes from `Ready to merge` to `Conflicts`. That is a draft, and a head no review has answered:
#   neither carries a verdict of its own, so what the rule can say is only that a change which had been
#   ready has stopped merging. `Ready to merge` says the change is waiting on nothing but the owner
#   pressing the button, and a conflict is precisely the discovery that it is not, so the item has to
#   leave that column for one that says a rebase is owed. From `Ready to merge` and from nowhere else:
#   an item still being written, already blocked, or already done says nothing about whether a
#   conflict is news, and a rule that moved those would be reporting the same conflict on every push
#   to the base branch for as long as it went unresolved.
#
# **Every rule but a draft's is decided only once every pipeline outside the ignored set has
# finished**, which is the single condition stated at the point the counts are read rather than
# repeated in each of them. Every verdict claims something about the whole state of the change: `Ready to merge` and
# `Conflicts` say nothing is left to wait for but one act, and `Changes requested` says what is owed is
# an answer from the agent — and a run still in flight can add to what that answer has to cover. So a
# review published minutes before `Required CI` finishes moves nothing until it does, and the pipeline
# that concludes last raises the event that asks again. `CodeQL` is outside that wait for the reason it
# is outside every other rule here: merging does not wait on it, so neither does the column.
#
# Rules are read top to bottom and the first match wins, because the field holds one value. Order is
# therefore a decision, and whether the change still merges is asked after the verdicts rather than
# before them. A conflict is not an answer to what a reader or a pipeline objected to: an item whose
# reviewer withheld approval on a conflicting head owes the answer and the rebase both, and the column
# that says the agent owes something is `Changes requested`. Asked first, the conflict rule would print
# a status whose write is refused everywhere but `Ready to merge`, and the verdict it shadowed would
# never be published.
#
# Usage: select-board-status.sh <pull-request-json-file>
#
# The JSON file holds one object with the fields the collecting caller read from GitHub — `number`,
# `mergeable`, `isDraft`, `state`, `labels`, `headRefOid`, `reviews`, and `checks` — which is what
# lets this be run by hand and tested against a fixture without a token. A `review` is
# `{author, state, commit}` and a `check` is `{workflow, name, status, conclusion}`, both as GitHub
# spells them.

set -euo pipefail

# The account the reviewer's approvals are authored by, which is the App's slug rather than its
# display name. `Fathom review` names the same login for the same reason, and the two have to agree:
# an approval by anybody else is a person's opinion about the code, and the board column this rule
# writes claims the pipeline agrees with it as well.
readonly REVIEWER_LOGIN='fathom-reviewer[bot]'

# The checks the verdicts do not read, matched against a check's workflow and against its own name so
# that either spelling is enough. `CodeQL` publishes under both — a run of the workflow and an
# aggregate check of its own — and is here because it is not required to merge: a finding there is
# worth acting on and does not make the change unmergeable, so reading it would hold every pull
# request out of `Ready to merge` for a question the ruleset does not ask.
#
# `Apply pull request rules` is here because it is the workflow deciding this, and nothing it
# publishes says whether the change builds: its checks are a label and a board write. Today they
# would not block the answer anyway — the labelling run on the head has concluded long before, and a
# run started by `workflow_run` is attributed to the default branch rather than to the head — but a
# rule that read them would be deciding partly from its own run, which is a state to refuse outright
# rather than one to keep re-deriving as safe.
readonly IGNORED_CHECKS='["CodeQL","Apply pull request rules"]'

# A check that failed, as GitHub concludes one. `SKIPPED` and `NEUTRAL` are the ordinary shape of a
# job a path filter turned off, and `CANCELLED` is what a superseded run leaves behind on a head that
# has since been reviewed again — none of the three is a pipeline that says no.
readonly FAILED_CONCLUSIONS='["FAILURE","TIMED_OUT","ACTION_REQUIRED","STARTUP_FAILURE"]'

pull_request_file="${1:?the pull request JSON file is required}"

[[ -s "$pull_request_file" ]] || exit 0

# `CONFLICTING` and `MERGEABLE` are the two answers, and `UNKNOWN` is neither. GitHub computes
# mergeability asynchronously and reports `UNKNOWN` while it is doing so, which is the state every
# open pull request passes through in the seconds after something merges into the base — the exact
# moment this pipeline runs. Reading it as a conflict would move an item on every merge, and reading
# it as mergeable would claim a change is waiting on nothing from the absence of an answer; the caller
# waits for the answer instead, and reports the pull requests it never got one for.
mergeable="$(jq -r '.mergeable // "UNKNOWN"' "$pull_request_file")"

# A draft is work being written rather than a change asking for anything, and its checks are red as
# often as not while it is. None of the verdicts reads one, which is what keeps the item where the
# work put it until the pull request is marked ready — while the last rule still does, because a
# change converted back to draft from `Ready to merge` owes the rebase when its branch stops merging
# exactly as it did before.
if [[ "$(jq -r '.isDraft // false' "$pull_request_file")" != 'true' ]]; then
  # The two counts the verdicts are decided from, read in one pass so that the ignored set is applied
  # once. `pending` is what makes any verdict premature, and `failed` is what turns a change back.
  IFS=$'\t' read -r failed pending <<< "$(
    jq -r --argjson ignored "$IGNORED_CHECKS" --argjson failures "$FAILED_CONCLUSIONS" '
      [.checks[]? | select(([.workflow // "", .name // ""] | any(. as $named | $ignored | index($named))) | not)]
      | [([.[] | select(.conclusion as $conclusion | $failures | index($conclusion))] | length),
         ([.[] | select((.status // "COMPLETED") != "COMPLETED")] | length)]
      | @tsv' \
      "$pull_request_file"
  )"

  # No verdict is published while a pipeline is still deciding, and neither is the conflict rule's
  # answer: a head whose pipelines have not finished is one a verdict may still be about to arrive on.
  # This is the one place that wait is expressed, so every rule below it is a question about a
  # finished state rather than a copy of the same condition.
  if (( pending > 0 )); then
    exit 0
  fi

  if (( failed > 0 )); then
    printf 'Changes requested\t\tDone,Blocked\n'
    exit 0
  fi

  # The reviewer's verdict on the head in front of us, as the one word it is published as: `COMMENTED`
  # where approval was withheld and `APPROVED` where it was given. GitHub keeps a review against the
  # commit it was written on, so a push that carries either forward without a re-review is a stale
  # verdict, and the comparison against the head is the whole of what refuses one.
  #
  # `latestReviews` is what the state was read from, which is one review per author, so this is the
  # reviewer's current word rather than the first one it ever said.
  verdict="$(
    jq -r --arg login "$REVIEWER_LOGIN" '
      . as $pull_request
      | [.reviews[]? | select(.author == $login and .commit == $pull_request.headRefOid) | .state]
      | last // ""' \
      "$pull_request_file"
  )"

  if [[ "$verdict" == 'COMMENTED' ]]; then
    printf 'Changes requested\t\tDone,Blocked\n'
    exit 0
  fi

  if [[ "$verdict" == 'APPROVED' && "$mergeable" == 'MERGEABLE' ]]; then
    printf 'Ready to merge\t\tDone,Blocked\n'
    exit 0
  fi

  if [[ "$verdict" == 'APPROVED' && "$mergeable" == 'CONFLICTING' ]]; then
    printf 'Conflicts\t\tDone,Blocked\n'
    exit 0
  fi
fi

if [[ "$mergeable" == 'CONFLICTING' ]]; then
  printf 'Conflicts\tReady to merge\t\n'
  exit 0
fi
