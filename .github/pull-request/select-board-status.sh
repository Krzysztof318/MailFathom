#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
# Exactly one rule is implemented today, and the shape is built for more:
#
# - A pull request that no longer merges into its base moves the issues it closes from
#   `Ready to merge` to `Conflicts`. `Ready to merge` says the change is waiting on nothing but the
#   owner pressing the button, and a conflict is precisely the discovery that it is not, so the item
#   has to leave that column for one that says a rebase is owed. From `Ready to merge` and from
#   nowhere else: an item still being written, already blocked, or already done says nothing about
#   whether a conflict is news, and a rule that moved those would be reporting the same conflict on
#   every push to the base branch for as long as it went unresolved.
#
# Rules are read top to bottom and the first match wins, because the field holds one value. Order is
# therefore a decision: a more specific rule goes above a more general one.
#
# Usage: select-board-status.sh <pull-request-json-file>
#
# The JSON file holds one object with the fields the collecting caller read from GitHub — `number`,
# `mergeable`, `isDraft`, `state`, and `labels` — which is what lets this be run by hand and tested
# against a fixture without a token.

set -euo pipefail

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
