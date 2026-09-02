#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom


# What the branch touched. Sourced by the verification scripts; it defines functions and runs nothing
# on its own, and it expects `resolve-base-remote.sh` to have been sourced before it.
#
# Both gates now decide something from this list — the fast loop which files it formats, the full
# gate which files it verifies and whether the workflow contract suite has anything to report — so
# the list is defined once. Two definitions would drift into disagreeing about what a change is,
# which is the one thing a gate must not be uncertain about.

# The branch base, preferred as the remote-tracking ref of whichever remote is the upstream
# repository and falling back to the local branch, so the loop keeps working offline. Printing
# nothing when neither exists widens the caller's scope to the uncommitted work alone rather than
# failing: in the fast loop the base only decides which files are formatted, so a missing one costs a
# narrower scope rather than a wrong verdict. The full gate never reaches this state, because it
# fetches the base and refuses a branch that does not contain it before it asks anything here.
resolve_branch_base() {
  local base_remote
  local candidate_ref

  if base_remote="$(resolve_base_remote)"; then
    candidate_ref="refs/remotes/$base_remote/main"

    if git rev-parse --verify --quiet "$candidate_ref" > /dev/null; then
      printf '%s\n' "$candidate_ref"
      return 0
    fi
  fi

  if git rev-parse --verify --quiet 'refs/heads/main' > /dev/null; then
    printf 'refs/heads/main\n'
  fi
}

# Every path this branch touched and that still exists: committed since the base, staged, modified,
# or newly added. Deletions are filtered out because a removed file cannot be formatted, and a rename
# arrives as its destination for the same reason. Each command reports its own failure, because
# errexit does not apply to a function called in a condition and the caller would otherwise read a
# truncated list as "nothing changed": a shallow clone whose merge base with the base branch is
# unavailable fails exactly that way, and a gate would then narrow itself to nothing rather than
# saying it could not tell what changed.
list_changed_paths() {
  local branch_base

  branch_base="$(resolve_branch_base)"

  git diff --name-only --diff-filter=ACMR HEAD || return 1
  git ls-files --others --exclude-standard || return 1

  if [[ -n "$branch_base" ]]; then
    git diff --name-only --diff-filter=ACMR "$branch_base...HEAD" || return 1
  fi
}

# The paths the branch removed or moved. A caller reads the emptiness rather than the names: a
# `describes:` marker, a table-of-contents entry, and a link all name a path, and a file that stops
# being where it was is what leaves one of them resolving to nothing — while the files that remain
# say nothing about it. A rename Git did not detect arrives as a deletion beside an addition, which
# answers the same way. This is a list rather than a predicate for the reason the failures above are
# reported: a git invocation that failed must not be readable as "nothing was removed", which is the
# answer that would let a gate skip the check it exists to make.
list_removed_or_renamed_paths() {
  local branch_base

  branch_base="$(resolve_branch_base)"

  git diff --name-only --diff-filter=DR HEAD || return 1

  if [[ -n "$branch_base" ]]; then
    git diff --name-only --diff-filter=DR "$branch_base...HEAD" || return 1
  fi
}
