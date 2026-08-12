#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

# Print the labels a pull request should carry, one per line, each label once.
#
# A pull request's labels are derived rather than typed: what a change *is* is already recorded on
# the issues it closes, and a label nobody remembers to apply is a label that says nothing. This is
# the one place those conditions live, so a new one is added here and reaches every reader of a
# pull request at once — the list, a notification, a review that gates on it — instead of being
# re-derived by whoever happens to need it.
#
# Exactly one condition is implemented today, and the shape is built for more:
#
# - `security` when any issue the body **refers to** carries `security`. That label means *needs a
#   security review before it merges* (`docs/operations/issue-tracking.md` § "Labels"), which is a
#   property of the work rather than of the branch delivering it, so it is written where the work is
#   described and carried here to where the change is read.
#
# Referred to rather than closed, which is why this reads `collect-referenced-issues.sh` instead of
# the closing-reference parsing the reviewer's collection uses. A change that merely touches the work
# a security issue describes — "part of #123", "the ceiling #124 asked for" — is a change somebody
# wants read that way just as much as the one that finishes it, and the question here is what the
# change is about rather than what merging it completes.
#
# What this never does is remove a label. It answers *which labels does this change earn*, and a
# label a hand applied answers a different question that this script cannot see; the caller applies
# what is printed and leaves everything else alone.
#
# An issue that cannot be fetched — deleted, or in a repository this token does not reach — earns
# nothing and does not stop the walk. Reading a label is how a condition is decided, so an unreadable
# issue is a condition that was not met rather than one to guess at, and the reviewer that is handed
# the same unfetchable issue says so in its own summary.
#
# Usage: select-labels.sh <repository> <pull-request-number> <referenced-issues-script> [limit]

set -euo pipefail

repository="${1:?the repository is required}"
pull_request_number="${2:?the pull request number is required}"
referenced_issues_script="${3:?the referenced-issues script is required}"
reference_limit="${4:-10}"

security_label='security'

body_file="$(mktemp)"
trap 'rm -f "$body_file"' EXIT

# The body from the API rather than from an event payload, so this script says the same thing
# whichever event reached it and can be run by hand against a pull request number.
gh api "repos/${repository}/pulls/${pull_request_number}" --jq '.body // ""' > "$body_file"

# What the reference ceiling cut goes to standard error, where the caller decides whether to report
# it; standard output here is the label list alone.
while IFS= read -r issue_number; do
  [[ -n "$issue_number" ]] || continue

  if gh api "repos/${repository}/issues/${issue_number}" --jq '.labels[].name' \
       | grep -qxF "$security_label"; then
    printf '%s\n' "$security_label"
    break
  fi
done < <("$referenced_issues_script" "$body_file" "$repository" "$reference_limit")
