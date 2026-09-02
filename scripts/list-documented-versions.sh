#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Prints the release tags the documentation site carries a version for, newest first.
#
#   scripts/list-documented-versions.sh          # v0.4.0
#                                                # v0.3.0
#
# One version per minor line, at its newest patch, because a patch release exists to correct the line it belongs to:
# `0.2.1` documents what `0.2.0` documents plus what was wrong with it, so publishing both would offer a reader a
# choice between a page and its correction. Prereleases are left out entirely — nothing installs one deliberately, and
# `latest` already describes the unreleased state from the default branch.
#
# A tag is listed only when it carries the site definition itself. Every version is built from its own commit, with its
# own navigation and its own API surface, which is the property that makes a page on the site true of the release it is
# filed under. Rendering an older tag's pages through today's configuration would produce navigation naming pages that
# release never had, so the releases that predate this configuration are not published at all and stay readable in the
# repository at their tag.
#
# The site the workflow assembles is `latest` plus this list, and nothing else is retained: a version disappears from
# the selector when its line takes a patch, and the whole site is rebuilt from the repository on every publish rather
# than accumulated in place. That is what makes the published site a function of the tags rather than of the order the
# deployments happened to run in — a site rebuilt from scratch today is byte-for-byte the site that would be rebuilt
# tomorrow from the same tags.
#
# The tags are read from whatever the caller's checkout holds. A shallow clone or a fetch without `--tags` therefore
# publishes fewer versions rather than failing, which is why the workflow fetches them explicitly.

readonly release_tag_pattern='^v[0-9]+\.[0-9]+\.[0-9]+$'
readonly site_definition='docfx/docfx.json'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'list-documented-versions.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

# `sort --version-sort` orders the tags the way their versions compare rather than the way their text does, so 0.10.0
# lands above 0.9.0 instead of below it. Reading them oldest first lets the deduplication below keep the last one it
# sees for each line, which is that line's newest patch.
mapfile -t release_tags < <(git tag --list 'v*' | grep --extended-regexp "$release_tag_pattern" | sort --version-sort)

declare -A newest_patch_of_line=()
declare -a documented_lines=()

for release_tag in "${release_tags[@]}"; do
  if ! git cat-file -e "$release_tag:$site_definition" 2>/dev/null; then
    continue
  fi

  minor_line="${release_tag%.*}"

  if [[ -z "${newest_patch_of_line[$minor_line]-}" ]]; then
    documented_lines+=("$minor_line")
  fi

  newest_patch_of_line["$minor_line"]="$release_tag"
done

for ((index = ${#documented_lines[@]} - 1; index >= 0; index--)); do
  printf '%s\n' "${newest_patch_of_line[${documented_lines[index]}]}"
done
