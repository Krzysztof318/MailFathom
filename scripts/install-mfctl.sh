#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Installs the released `mfctl` command on Linux, verified against the checksum file the release attaches.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/scripts/install-mfctl.sh | bash
#   bash scripts/install-mfctl.sh --version 0.5.0 --directory /usr/local/bin
#
# This is the only script in the repository written to run on somebody else's machine, which decides most of its shape.
# It reads no repository state and is never invoked by a workflow, so it works the same fetched over HTTP as it does
# from a checkout, and it installs into the user's own directory and never invokes `sudo` — an installer that asks for
# root to place one file has asked for more than it needs.
#
# What it will not do is install something it could not check. No `mfctl` binary carries a code signature, so the
# checksum file attached beside them is the whole of what tells a genuine download from a tampered one, and a script
# that skipped it would be a worse path than the manual one it replaces rather than a shorter one.

readonly repository='Krzysztof318/MailFathom'
readonly release_base="https://github.com/$repository/releases"

usage() {
  cat << 'TEXT'
Installs the MailFathom administrative command, mfctl, on Linux.

  --version <version>    The release to install, for example 0.5.0. Defaults to the newest release.
  --directory <path>     Where to install it. Defaults to ~/.local/bin.
  --help                 Print this and exit.

MFCTL_VERSION and MFCTL_INSTALL_DIR set the same two things, and an argument wins over either.

mfctl and the deployment it administers have to agree on major.minor, so pass --version when you are
installing the command for a deployment that is not on the newest release.
TEXT
}

requested_version="${MFCTL_VERSION:-}"
install_directory="${MFCTL_INSTALL_DIR:-$HOME/.local/bin}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      [[ $# -ge 2 ]] || { printf '--version takes the release to install, for example 0.5.0.\n' >&2; exit 1; }
      requested_version="$2"
      shift 2
      ;;
    --directory)
      [[ $# -ge 2 ]] || { printf '--directory takes the path to install into.\n' >&2; exit 1; }
      install_directory="$2"
      shift 2
      ;;
    --help | -h)
      usage
      exit 0
      ;;
    *)
      printf 'Unrecognized argument: %s\n\n' "$1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

for required_command in curl sha256sum install uname; do
  if ! command -v "$required_command" > /dev/null 2>&1; then
    printf 'This script needs %s and it is not on the PATH.\n' "$required_command" >&2
    exit 1
  fi
done

# A release attaches Linux and Windows binaries; the Windows ones are for a machine this script does not run on, and
# there is no macOS build at all. Saying which platforms exist is the useful failure, because the alternative is a
# download that 404s under a name the reader has no way to check against.
operating_system="$(uname -s)"
if [[ "$operating_system" != 'Linux' ]]; then
  printf 'This script installs the Linux binaries and this is %s.\n' "$operating_system" >&2
  printf 'A release publishes mfctl for linux-x64, linux-arm64, win-x64, and win-arm64, and for nothing else: %s\n' \
    "$release_base" >&2
  exit 1
fi

machine="$(uname -m)"
case "$machine" in
  x86_64 | amd64) runtime_identifier='linux-x64' ;;
  aarch64 | arm64) runtime_identifier='linux-arm64' ;;
  *)
    printf 'No mfctl binary is published for %s. A release carries linux-x64 and linux-arm64: %s\n' \
      "$machine" "$release_base" >&2
    exit 1
    ;;
esac

# The redirect `/releases/latest` serves is what names the newest release, rather than the REST API, which answers the
# same question and rate-limits an unauthenticated caller by IP address while doing it.
if [[ -z "$requested_version" ]]; then
  if ! latest_url="$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$release_base/latest")"; then
    printf 'Could not ask %s which release is newest. Pass --version to install a particular one.\n' \
      "$release_base/latest" >&2
    exit 1
  fi

  requested_version="${latest_url##*/tag/}"
fi

# Written with a leading `v` on a tag and without one everywhere else, so both are accepted and neither reaches the
# asset names below.
version="${requested_version#v}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  printf 'Not a release version: %s. It looks like 0.5.0, and %s lists the ones there are.\n' \
    "$requested_version" "$release_base" >&2
  exit 1
fi

readonly asset_name="mfctl-$version-$runtime_identifier"
readonly checksum_name="mfctl-$version.sha256"
readonly download_base="$release_base/download/v$version"

download_directory="$(mktemp --directory)"
trap 'rm --recursive --force "$download_directory"' EXIT

printf 'Installing mfctl %s (%s) into %s\n' "$version" "$runtime_identifier" "$install_directory" >&2

for asset in "$asset_name" "$checksum_name"; do
  if ! curl -fsSL --output "$download_directory/$asset" "$download_base/$asset"; then
    printf 'Could not download %s from %s.\n' "$asset" "$download_base" >&2
    printf 'Check that %s is a release that exists and that it publishes %s: %s\n' \
      "$version" "$runtime_identifier" "$release_base" >&2
    exit 1
  fi
done

# `--ignore-missing` is what lets one checksum file cover four binaries: it checks the one that was downloaded and says
# nothing about the three platforms that were not. It is also why the check runs from the download directory — the file
# names each entry as `./mfctl-…`, relative to where the release built them.
if ! ( cd "$download_directory" && sha256sum --check --ignore-missing --quiet "$checksum_name" ); then
  printf '\n%s does not match the checksum %s publishes for it, so nothing was installed.\n' \
    "$asset_name" "$version" >&2
  printf 'That is either a download that went wrong or a file that is not the one this release attached. Try again, '  >&2
  printf 'and take it up at %s/issues if it happens twice.\n' "https://github.com/$repository" >&2
  exit 1
fi

mkdir --parents "$install_directory"
install --mode 755 "$download_directory/$asset_name" "$install_directory/mfctl"

printf 'Installed mfctl %s to %s\n' "$version" "$install_directory/mfctl" >&2

# An installation the shell cannot find is the one failure this script can produce while succeeding, so it is reported
# rather than left for the reader to discover at their next prompt.
case ":$PATH:" in
  *":$install_directory:"*)
    printf 'Run `mfctl login --endpoint https://host:port` to reach a deployment.\n' >&2
    ;;
  *)
    printf '\n%s is not on your PATH, so `mfctl` will not resolve yet. Add it in your shell profile:\n\n' \
      "$install_directory" >&2
    printf '  export PATH="%s:$PATH"\n\n' "$install_directory" >&2
    printf 'Until then the command is %s.\n' "$install_directory/mfctl" >&2
    ;;
esac
