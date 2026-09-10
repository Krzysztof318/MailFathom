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
# Three rules are implemented, in this order:
#
# - A pull request that no longer merges into its base moves the issues it closes from
#   `Ready to merge` to `Conflicts`. `Ready to merge` says the change is waiting on nothing but the
#   owner pressing the button, and a conflict is precisely the discovery that it is not, so the item
#   has to leave that column for one that says a rebase is owed. From `Ready to merge` and from
#   nowhere else: an item still being written, already blocked, or already done says nothing about
#   whether a conflict is news, and a rule that moved those would be reporting the same conflict on
#   every push to the base branch for as long as it went unresolved.
#
# - A check that failed on the head earns `Changes requested`, whatever the review said. The column
#   says the change is waiting on the agent rather than on the owner, and a red pipeline is exactly
#   that whether the objection was written by a reader or produced by a build — so this rule names no
#   required status and describes whatever item it finds, the way a review's verdict does.
#
# - An approved head whose checks have all finished without failing earns `Ready to merge`. That is
#   the rule this file exists for: the reviewer reads the diff and cannot see the pipeline, so an
#   approval published while `Required CI` is still running says nothing about whether the change
#   builds. Both halves are asked here, where both are visible, and `Fathom review` writes only the
#   verdict that is its own.
#
# Rules are read top to bottom and the first match wins, because the field holds one value. Order is
# therefore a decision: a more specific rule goes above a more general one, which is why the conflict
# rule — true only of an already approved item — is asked before the two that describe any item.
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

# The checks the two rules below do not read, matched against a check's workflow and against its own
# name so that either spelling is enough. `CodeQL` publishes under both — a run of the workflow and
# an aggregate check of its own — and is here because it is not required to merge: a finding there is
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

mergeable="$(jq -r '.mergeable // "UNKNOWN"' "$pull_request_file")"

# `CONFLICTING` and nothing else. GitHub computes mergeability asynchronously and reports `UNKNOWN`
# while it is doing so, which is the state every open pull request passes through in the seconds
# after something merges into the base — the exact moment this pipeline runs. Reading it as a
# conflict would move an item on every merge; the caller waits for the answer instead, and reports
# the pull requests it never got one for.
if [[ "$mergeable" == 'CONFLICTING' ]]; then
  printf 'Conflicts\tReady to merge\t\n'
  exit 0
fi

# A draft is work being written rather than a change asking for anything, and its checks are red as
# often as not while it is. Neither rule below reads one, which is what keeps the item where the work
# put it until the pull request is marked ready.
if [[ "$(jq -r '.isDraft // false' "$pull_request_file")" == 'true' ]]; then
  exit 0
fi

# The two counts the rules are decided from, read in one pass so that the ignored set is applied
# once. `failed` is what turns a change back, `pending` is what makes an approval premature.
IFS=$'\t' read -r failed pending <<< "$(
  jq -r --argjson ignored "$IGNORED_CHECKS" --argjson failures "$FAILED_CONCLUSIONS" '
    [.checks[]? | select(([.workflow // "", .name // ""] | any(. as $named | $ignored | index($named))) | not)]
    | [([.[] | select(.conclusion as $conclusion | $failures | index($conclusion))] | length),
       ([.[] | select((.status // "COMPLETED") != "COMPLETED")] | length)]
    | @tsv' \
    "$pull_request_file"
)"

if (( failed > 0 )); then
  printf 'Changes requested\t\tDone,Blocked\n'
  exit 0
fi

# The approval has to be of the head in front of us. GitHub keeps a review against the commit it was
# written on, so a push that carries an approval forward without a re-review is a stale verdict, and
# a stale verdict is the whole of what this comparison refuses.
approved="$(
  jq -r --arg login "$REVIEWER_LOGIN" '
    . as $pull_request
    | [.reviews[]?
       | select(.author == $login and .state == "APPROVED" and .commit == $pull_request.headRefOid)]
    | length > 0' \
    "$pull_request_file"
)"

# `MERGEABLE` rather than *not conflicting*: `UNKNOWN` is an answer GitHub has not finished computing
# and this column claims the change is waiting on nothing, which is a claim to make from an answer
# rather than from the absence of one.
if [[ "$approved" == 'true' && "$mergeable" == 'MERGEABLE' ]] && (( pending == 0 )); then
  printf 'Ready to merge\t\tDone,Blocked\n'
  exit 0
fi
