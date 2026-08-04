#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Builds the documentation site for the checked-out commit into one directory.
#
#   scripts/build-docs-site.sh                       # artifacts/docs-site
#   scripts/build-docs-site.sh <output-directory>
#
# The output is one version of the published site: the pages under `docs/`, the API reference generated from the XML
# comments in `src/`, and the search index over both. It is self-contained apart from the version selector, which reads
# a manifest the site root carries — `scripts/compose-docs-site.sh` writes that, and this script knows nothing about
# the other versions. docs/operations/documentation-site.md describes how the two fit together and what the workflow
# adds around them.
#
# Serving the result locally is `dotnet docfx serve <output-directory>`; `dotnet docfx docfx/docfx.json --serve` rebuilds
# and serves in one step while a page is being written.
#
# The restore is deliberate rather than left to docfx. Generating the API reference loads every project through
# MSBuild, and an unrestored project fails there with an error about a missing assets file that says nothing about
# documentation. It is not locked, unlike the verification scripts: this script also runs against older release tags,
# where the lock files record what that tag pinned, and a lock file is only a claim about its own commit.
#
# **A link docfx cannot resolve fails the build.** A relative link between two published pages is rewritten to the page
# it points at, and one that resolves to nothing is left as written — which reaches a reader as a 404 rather than as a
# broken build. That is the failure this refuses to publish, and it is why a link out of the published set is written
# as an absolute GitHub URL: docs/AGENTS.md states which form belongs where, and this is what enforces it. docfx
# reports plenty else worth reading and nothing else worth stopping for, so the other warnings stay warnings.

readonly configuration_file='docfx/docfx.json'
readonly solution_file='MailFathom.slnx'
readonly generated_metadata_directory='docfx/api'
readonly unresolved_link_codes='InvalidFileLink|InvalidBookmark|InvalidExternalBookmark'
# The one published page no change here may correct. `CHANGELOG.md` is written by the release pull request and by
# nothing else — it is a statement about a release rather than about a change — so a link in it is fixed by the next
# release rather than by whoever notices it. Failing every documentation build until then would stop the site from
# publishing over a file the build is not allowed to touch.
readonly link_gate_exemption='"file": ?"\.\./CHANGELOG\.md"'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'build-docs-site.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"
repository_root="$(pwd -P)"

output_directory="${1:-artifacts/docs-site}"
mkdir --parents "$output_directory"
output_directory="$(cd "$output_directory" && pwd -P)"

# The directory is emptied below, so it has to be one this script owns. A caller that names the checkout — or the
# filesystem root, which `mkdir --parents` accepts without complaint — gets a refusal rather than a deletion.
if [[ "$output_directory" == '/' || "$output_directory" == "$repository_root" ]]; then
  printf 'build-docs-site.sh writes into a directory of its own and empties it first, so %s is refused.\n' \
    "$output_directory" >&2
  exit 1
fi

if [[ ! -f "$configuration_file" ]]; then
  printf 'This commit carries no %s, so it predates the documentation site.\n' "$configuration_file" >&2
  exit 1
fi

# A type deleted since the last local build leaves its page behind otherwise, and the stale page keeps appearing in the
# navigation and the search index until somebody notices it describes something that no longer exists.
rm --recursive --force "$generated_metadata_directory" "$output_directory"

build_log="$(mktemp)"
trap 'rm --force "$build_log"' EXIT

dotnet tool restore
dotnet restore "$solution_file"
dotnet docfx "$configuration_file" --output "$output_directory" --log "$build_log"

# The log is one JSON object per line, and each diagnostic docfx classifies carries the code the line below matches.
# Reading it rather than the console output is what makes this exact: the same sentence appears at several severities,
# and a message match would fail a build over a link docfx resolved and merely commented on.
if unresolved_links="$(
  grep --extended-regexp "\"code\": ?\"($unresolved_link_codes)\"" "$build_log" |
    grep --invert-match --extended-regexp "$link_gate_exemption"
)"; then
  printf '\nThe build resolved no target for these links, so publishing it would put a 404 behind each one:\n\n' >&2
  printf '%s\n' "$unresolved_links" >&2
  printf '\nA link between two published pages stays relative; a link to anything the site does not carry — an\n' >&2
  printf 'architectural decision record, a specification, a deployment asset, a source file — is written as an\n' >&2
  printf 'absolute https://github.com/Krzysztof318/MailFathom URL so that it works in both renderings.\n' >&2
  exit 1
fi

printf '\nThe documentation site is in %s. Serve it with:\n  dotnet docfx serve %s\n' \
  "$output_directory" "$output_directory"
