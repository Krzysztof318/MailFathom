#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Decides whether an annotated tag may be published as a release, and prints the version it releases.
#
# The release workflow runs this on the tagged commit before it builds anything, because every disagreement it can
# catch is one that would otherwise become a published artifact nobody can unpublish. What it asserts is what
# docs/decisions/0004-versioning-and-release-policy.md requires of a release tag:
#
#   * the tag is annotated, and reads v<major>.<minor>.<patch> with no prerelease identifier and no build metadata;
#   * the tagged commit is reachable from the default branch or from the release branch of its own line;
#   * the version equals the <VersionPrefix> the tagged commit declares, so the artifact and the tree agree;
#   * the version is ahead of the highest existing tag on the same major.minor line, rather than on any line, because
#     v0.2.1 cut after 0.3.0 has shipped is the ordinary shape of a hotfix rather than a regression;
#   * CHANGELOG.md carries a section for exactly this version, and that section says something.
#
#   scripts/assert-release-tag.sh v0.1.0    # prints 0.1.0
#
# It reads the working tree it runs in, so the caller checks out the tag first. Every failure names the tag, what was
# expected, and what was found, because the reader is someone who has just pushed a tag and needs to know whether to
# delete it.

# The refs a release commit may be reachable from. `main` produces every major and minor release; a patch is cut from
# the permanent branch of its own line and is by construction not reachable from `main`, so a check that accepted only
# `main` would reject every patch the policy requires.
readonly default_branch_ref='refs/remotes/origin/main'
readonly release_branch_ref_pattern='refs/remotes/origin/release/*'

fail() {
  printf 'assert-release-tag.sh: %s\n' "$1" >&2
  exit 1
}

if [[ $# -ne 1 ]]; then
  fail 'expects exactly one argument, the tag to release, for example v0.1.0.'
fi

release_tag="$1"

if [[ ! "$release_tag" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  fail "$release_tag is not a release tag. A release carries no prerelease identifier and no build metadata, so the tag reads v<major>.<minor>.<patch> and nothing else."
fi

release_major="${BASH_REMATCH[1]}"
release_minor="${BASH_REMATCH[2]}"
release_patch="${BASH_REMATCH[3]}"
release_version="${release_major}.${release_minor}.${release_patch}"

if ! tag_object_type="$(git cat-file -t "refs/tags/$release_tag" 2>/dev/null)"; then
  fail "$release_tag does not exist in this checkout. Fetch the tags before asserting one."
fi

# An annotated tag carries the tagger, the date, and the message that make a release attributable; a lightweight tag is
# a branch name that happens to look like a version.
if [[ "$tag_object_type" != 'tag' ]]; then
  fail "$release_tag is a lightweight tag. A release tag is annotated: git tag --annotate $release_tag --message '...'."
fi

tagged_commit="$(git rev-list -n 1 "refs/tags/$release_tag")"

reachable_from=''

if git merge-base --is-ancestor "$tagged_commit" "$default_branch_ref" 2>/dev/null; then
  reachable_from='origin/main'
else
  while IFS= read -r release_branch_ref; do
    if git merge-base --is-ancestor "$tagged_commit" "$release_branch_ref" 2>/dev/null; then
      reachable_from="${release_branch_ref#refs/remotes/}"
      break
    fi
  done < <(git for-each-ref --format='%(refname)' "$release_branch_ref_pattern")
fi

if [[ -z "$reachable_from" ]]; then
  fail "$release_tag points at $tagged_commit, which is reachable from neither origin/main nor any origin/release/* branch. A release is published from reviewed history, so a tag on a commit that never merged is refused rather than built."
fi

declared_version="$(git show "$tagged_commit:Directory.Build.props" |
  sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' | head -n 1)"

if [[ "$declared_version" != "$release_version" ]]; then
  fail "$release_tag releases $release_version, but the tagged commit declares <VersionPrefix>${declared_version:-nothing}</VersionPrefix>. The artifact would carry a version its own source tree does not, so rebuilding from the revision it records would produce a different one."
fi

# Only the tags on this release's own major.minor line, because that is the line the new patch has to be ahead of.
highest_patch_on_line=-1

while IFS= read -r existing_tag; do
  [[ "$existing_tag" =~ ^v${release_major}\.${release_minor}\.([0-9]+)$ ]] || continue

  existing_patch="${BASH_REMATCH[1]}"

  if ((existing_patch > highest_patch_on_line)); then
    highest_patch_on_line="$existing_patch"
    highest_tag_on_line="$existing_tag"
  fi
done < <(git tag --list "v${release_major}.${release_minor}.*")

if ((highest_patch_on_line >= release_patch)) && [[ "${highest_tag_on_line:-}" != "$release_tag" ]]; then
  fail "$release_tag does not advance the ${release_major}.${release_minor}.x line, whose highest tag is ${highest_tag_on_line}. A release number is never reused and never moves backwards."
fi

# The same reading the release notes are composed from, so a release cannot be described one way to the workflow and
# another way to whoever reads the published notes.
if ! "$(dirname "${BASH_SOURCE[0]}")/read-changelog-section.sh" "$release_version" > /dev/null; then
  fail "$release_tag cannot be released: the changelog does not describe $release_version. The reason is above."
fi

printf 'assert-release-tag.sh: %s releases %s from %s, reachable from %s.\n' \
  "$release_tag" "$release_version" "$tagged_commit" "$reachable_from" >&2

printf '%s\n' "$release_version"
