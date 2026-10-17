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

# Against the working tree rather than `HEAD`, so an edit not yet committed is part of the change, and
# from the merge base rather than the base tip, so what `main` gained since is not read as this
# change's own.
{
  git diff --name-only --no-renames "$merge_base"
  git ls-files --others --exclude-standard
} | sort -u > "$work_directory/changed"

current_branch="$(git branch --show-current)"

# The pull request this branch publishes is identified by the owner of the repository it is pushed
# to as well as by its branch name, because another fork may use the same name for something
# related. A detached `HEAD` has no pull request of its own, so nothing is excluded then.
push_remote="$(git config "branch.$current_branch.pushRemote" || git config remote.pushDefault || git config "branch.$current_branch.remote" || printf 'origin')"
push_owner="$(git remote get-url "$push_remote" 2> /dev/null | sed -E 's#\.git/?$##; s#/+$##; s#.*[:/]([^/:]+)/[^/]+$#\1#' | tr '[:upper:]' '[:lower:]' || true)"

gh pr list --repo "$CANONICAL_REPOSITORY" --base main --state open --limit 100 \
  --json number,headRefName,headRepositoryOwner,files > "$work_directory/open.json"

# This reads at most 100 open pull requests, its own `--limit`, and `gh` returns at most 100 files of
# each, so a reply at either ceiling may be missing exactly the pull request or the path that relates.
if (( "$(jq 'length' "$work_directory/open.json")" >= 100 )); then
  printf 'At least 100 pull requests are open against main and only the first 100 were read, so this report may be incomplete.\n' >&2
fi
jq -r '.[] | select((.files // []) | length >= 100) | "Pull request \(.number) changes at least 100 files and only the first 100 were read, so this report may be incomplete."' \
  "$work_directory/open.json" >&2

jq --arg branch "$current_branch" --arg owner "$push_owner" '
  map(select($branch == "" or .headRefName != $branch or ((.headRepositoryOwner.login // "") | ascii_downcase) != $owner))
' "$work_directory/open.json" > "$work_directory/others.json"

open_count="$(jq 'length' "$work_directory/others.json")"

jq -r '.[].files[]?.path' "$work_directory/others.json" | sort -u > "$work_directory/landed"

bash .github/coherence/relate-changes.sh \
  "$repository_root" "$work_directory/landed" "$work_directory/changed" "$work_directory/pairs.json"

# One row per open pull request, its pairs folded beneath it: a shared page relates to every path
# either side changed, so pairs multiply quickly, and a reader first wants to know which pull request
# to open. Everything a row prints comes from somebody else's pull request, so it is written as code
# spans and no title is printed at all — the link already renders as one, and a title would carry
# closing keywords, mentions, and markup into the body it is pasted into. The link is a full URL
# rather than `#<n>`, because the labelling steps read every `#<n>` in a body as an issue it refers
# to, and a related pull request would hand its labels to this one.
jq -r --arg repository_url "https://github.com/$CANONICAL_REPOSITORY" --slurpfile others "$work_directory/others.json" '
  def code: gsub("`"; "") | gsub("\\|"; "\\|") | "`\(.)`";
  . as $pairs
  | $others[0] | sort_by(.number)
  | map(. as $pull_request
      | ($pairs | map(select(.landed as $landed | any($pull_request.files[]?; .path == $landed)))) as $related
      | select($related | length > 0)
      | "| \($repository_url)/pull/\(.number) | \($related | map(.landed) | unique | map(code) | join(", ")) | <details><summary>\($related | length)</summary>\($related | map("\(.landed | code) → \(.changed | code) (\(.reason))") | join("<br>"))</details> |")
  | .[]
' "$work_directory/pairs.json" > "$work_directory/rows"

if [[ ! -s "$work_directory/rows" ]]; then
  printf 'No open pull request to main is related to this change (%s open besides it).\n' "$open_count"
  exit 0
fi

printf '## Related open pull requests\n\n'
printf 'Each row is an open pull request changing paths `Coherence` relates to paths this one changes, with every pair it makes: its path, then the path here. Whichever merges second has to agree with the first.\n\n'
printf '| Pull request | Its related paths | Pairs |\n'
printf '|---|---|---|\n'
cat "$work_directory/rows"
