#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Report which open pull requests to `main` touch paths related to the change in the working tree.
#
# `Coherence` stops a pull request once a related change has landed under it. A related change that
# is still open cannot be seen at merge time at all, because it is not on `main` yet, so this asks the
# same question earlier: before the change is published, while writing it consistently with the other
# one costs the least. `$finish-change` runs it, and the section it prints goes into the pull request
# body so a reviewer knows which in-flight changes this one must agree with.
#
# The relation is not implemented here. `.github/coherence/relate-changes.sh` owns it, and this calls
# it exactly once, with every open pull request's paths as one "landed" list and this change's paths
# as the "changed" one, then attributes each pair to the pull requests that touch its landed side. One
# call is what keeps the cost flat however many pull requests are open: the index behind the relation
# costs the same for two paths as for two thousand.
#
# The `describes:` markers are read from this working tree, not from each open pull request's tree,
# so a page an open pull request adds that describes a path changed here is not paired until it
# lands — and `Coherence` pairs it then. It reports and never gates, and needs read access alone, so
# it runs in the fork role as it does in the owner's checkout.
#
# Usage: report-related-pull-requests.sh

set -euo pipefail

repository_root="$(git rev-parse --show-toplevel)"
cd "$repository_root"

# shellcheck source=scripts/resolve-base-remote.sh
source scripts/resolve-base-remote.sh

if ! base_remote="$(resolve_base_remote)"; then
  base_remote_resolution_hint >&2
  exit 1
fi

base_ref="$base_remote/main"
if ! merge_base="$(git merge-base "$base_ref" HEAD 2> /dev/null)"; then
  printf 'The base ref %s does not resolve. Fetch it and run this again.\n' "$base_ref" >&2
  exit 1
fi

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

# Against the working tree rather than `HEAD`, because this runs before the change is committed, and
# from the merge base rather than the base tip, so what `main` gained since is not read as this
# change's own.
{
  git diff --name-only --no-renames "$merge_base"
  git ls-files --others --exclude-standard
} | sort -u > "$work_directory/changed"

current_branch="$(git branch --show-current)"

# ponytail: `gh` reads at most 100 open pull requests and 100 files of each; page through the REST
# files endpoint if either ceiling is ever reached.
gh pr list --repo "$CANONICAL_REPOSITORY" --base main --state open --limit 100 \
  --json number,title,headRefName,files > "$work_directory/open.json"

jq --arg branch "$current_branch" 'map(select(.headRefName != $branch))' \
  "$work_directory/open.json" > "$work_directory/others.json"

open_count="$(jq 'length' "$work_directory/others.json")"

jq -r '.[].files[]?.path' "$work_directory/others.json" | sort -u > "$work_directory/landed"

bash .github/coherence/relate-changes.sh \
  "$repository_root" "$work_directory/landed" "$work_directory/changed" "$work_directory/pairs.json"

# One row per open pull request rather than per pair: a shared page relates to every path either
# side changed, so pairs multiply quickly, and what a reader acts on is which pull request to open
# and which of its files to read there.
jq -r --slurpfile others "$work_directory/others.json" '
  def code_list: map("`\(.)`") | join(", ");
  . as $pairs
  | $others[0] | sort_by(.number)
  | map(. as $pull_request
      | ($pairs | map(select(.landed as $landed | any($pull_request.files[]?; .path == $landed)))) as $related
      | select($related | length > 0)
      | "| #\(.number) \(.title | gsub("\\|"; "\\|")) | \($related | map(.landed) | unique | code_list) | \($related | map(.changed) | unique | code_list) | \($related | length) |")
  | .[]
' "$work_directory/pairs.json" > "$work_directory/rows"

if [[ ! -s "$work_directory/rows" ]]; then
  printf 'No open pull request to main is related to this change (%s open besides it).\n' "$open_count"
  exit 0
fi

printf '## Related open pull requests\n\n'
printf 'Each row is an open pull request changing paths `Coherence` relates to paths this one changes. Whichever merges second has to agree with the first.\n\n'
printf '| Pull request | Its related paths | Paths here | Pairs |\n'
printf '|---|---|---|---|\n'
cat "$work_directory/rows"
