#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Prints what CHANGELOG.md says about one released version, and fails when it says nothing.
#
# Two callers read the same section for two purposes, which is why this is a script rather than an inline extraction in
# either: `assert-release-tag.sh` refuses to publish a release the changelog does not describe, and the release workflow
# uses the same text as the GitHub release's notes. Composing them from anything else — a list of pull-request titles,
# say — would produce notes that cannot state the one sentence a MailFathom release note most needs to, which is whether
# the release can be deployed over the previous release's data.
#
#   scripts/read-changelog-section.sh 0.1.0
#
# The section is everything between `## [<version>]` and the next second-level heading. A heading with nothing under it
# is the shape a release preparation takes when the reading was skipped, so it fails as firmly as a missing one.
# See docs/decisions/0004-versioning-and-release-policy.md.

readonly changelog_file='CHANGELOG.md'

fail() {
  printf 'read-changelog-section.sh: %s\n' "$1" >&2
  exit 1
}

if [[ $# -ne 1 ]]; then
  fail 'expects exactly one argument, the released version, for example 0.1.0.'
fi

released_version="$1"

if [[ ! -f "$changelog_file" ]]; then
  fail "there is no $changelog_file here, so nothing states what this release contains."
fi

section_heading="## [${released_version}]"

if ! grep -qF "$section_heading" "$changelog_file"; then
  fail "$changelog_file carries no '${section_heading}' section. The changelog is written by the release preparation pull request, and its merge commit is what gets tagged."
fi

section_body="$(awk -v heading="$section_heading" '
  index($0, heading) == 1 { in_section = 1; next }
  in_section && /^## / { exit }
  in_section { print }
' "$changelog_file")"

if [[ -z "${section_body//[[:space:]]/}" ]]; then
  fail "the '${section_heading}' section of $changelog_file is empty. A release that lists no change is one whose contents were never read."
fi

printf '%s\n' "$section_body"
