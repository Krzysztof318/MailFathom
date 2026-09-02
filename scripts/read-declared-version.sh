#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Prints the version MailFathom declares, so nothing that names an artifact has to restate it.
#
# `Version.props` is the one place a version number is written, per
# docs/decisions/0004-versioning-and-release-policy.md. Every build path that has to put that number somewhere — an
# image tag, an OCI label, a chart's appVersion, a release tag — reads it from here rather than from a literal of its
# own, so the artifact and the assemblies inside it cannot disagree.
#
#   scripts/read-declared-version.sh              # 0.1.0
#   scripts/read-declared-version.sh nightly.41   # 0.1.0-nightly.41
#
# The optional argument is the prerelease suffix continuous integration supplies as `VersionSuffix`, and it is appended
# exactly the way MSBuild composes `Version` from `VersionPrefix` and `VersionSuffix`. The suffix is validated against
# SemVer's prerelease grammar rather than passed through, because a value MSBuild would accept and an OCI tag would
# reject is a failure worth having here instead of at the registry.
#
# The file is parsed rather than evaluated through MSBuild deliberately: this has to answer before a restore, on a
# machine that may have no SDK at all, and reading one element is not worth a workspace load.

readonly version_prefix_file='Version.props'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  # Not every caller runs inside a worktree — the container build context carries no .git — so the script falls back to
  # its own location rather than refusing.
  repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
fi

version_suffix="${1-}"

if [[ ! -f "$repository_root/$version_prefix_file" ]]; then
  printf 'read-declared-version.sh found no %s under %s.\n' "$version_prefix_file" "$repository_root" >&2
  exit 1
fi

version_prefix="$(
  sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$repository_root/$version_prefix_file" | head -n 1
)"

if [[ -z "$version_prefix" ]]; then
  printf '%s declares no <VersionPrefix>, so no build output can be named after it.\n' "$version_prefix_file" >&2
  exit 1
fi

if [[ ! "$version_prefix" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  printf 'The declared version %s is not a three-part version. Helm rejects anything else for version and appVersion.\n' \
    "$version_prefix" >&2
  exit 1
fi

if [[ -z "$version_suffix" ]]; then
  printf '%s\n' "$version_prefix"
  exit 0
fi

if [[ ! "$version_suffix" =~ ^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$ ]]; then
  printf 'The prerelease suffix %s is not a SemVer prerelease identifier.\n' "$version_suffix" >&2
  exit 1
fi

printf '%s-%s\n' "$version_prefix" "$version_suffix"
