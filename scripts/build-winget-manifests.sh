#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Renders the three manifest files the Windows Package Manager takes for a released `mfctl`.
#
# The community repository accepts a multi-file manifest set — version, defaultLocale, and installer — laid out under
# `manifests/m/MailFathom/mfctl/<version>/`. This produces exactly that set and nothing else, which is what keeps a
# submission to one package version, as microsoft/winget-pkgs requires.
#
# Usage:
#   scripts/build-winget-manifests.sh <binaries-directory>
#   scripts/build-winget-manifests.sh <binaries-directory> <output-directory>
#   scripts/build-winget-manifests.sh <binaries-directory> <output-directory> <version> <release-date>
#
# The binaries directory is what `Build the CLI binaries` produced: the release's own bytes, before anything publishes
# them. Each `InstallerSha256` is computed from those, rather than by downloading the asset back from the release page,
# so the hash a Windows operator's `winget` compares against is the hash of the file this pipeline built. A download
# would agree in every case except the one worth catching.
#
# Nothing here reaches the network and nothing reads a credential, so the set can be produced and read on any machine
# holding the binaries. Submitting it is a separate act — see .github/workflows/submit-winget-manifest.yml.

readonly package_identifier='MailFathom.mfctl'
readonly manifest_schema_version='1.12.0'
readonly release_download_base='https://github.com/Krzysztof318/MailFathom/releases/download'

# The runtime identifiers a release attaches for Windows, each paired with the architecture winget names it by. Written
# as an ordered list rather than an associative array so the installer entries come out in the same order every time:
# a manifest that reorders itself between versions is a diff nobody can read.
readonly installer_architectures=(
  'win-x64:x64'
  'win-arm64:arm64'
)

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'build-winget-manifests.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

if [[ $# -lt 1 ]]; then
  printf 'build-winget-manifests.sh takes the directory holding the built binaries as its first argument.\n' >&2
  exit 1
fi

binaries_directory="$1"
output_directory="${2:-artifacts/winget}"
package_version="${3:-$(bash scripts/read-declared-version.sh)}"
# The date winget shows beside the version. It defaults to today because the only caller runs on the day it publishes;
# a manifest rendered later for a version released earlier passes the real date rather than relabelling it.
release_date="${4:-$(date --utc +%F)}"

if [[ ! -d "$binaries_directory" ]]; then
  printf 'No such directory: %s. Point this at the binaries `Build the CLI binaries` produced.\n' \
    "$binaries_directory" >&2
  exit 1
fi

package_directory="$output_directory/manifests/m/MailFathom/mfctl/$package_version"
mkdir --parents "$package_directory"

# Built up as the installer entries are hashed, then written into the installer manifest in one place. Each entry is
# rendered here rather than in a template so that a missing binary fails before any file is written, instead of leaving
# a partial manifest set that looks submittable.
installer_entries=''

for architecture_mapping in "${installer_architectures[@]}"; do
  runtime_identifier="${architecture_mapping%%:*}"
  winget_architecture="${architecture_mapping##*:}"

  binary_name="mfctl-$package_version-$runtime_identifier.exe"
  binary_path="$binaries_directory/$binary_name"

  if [[ ! -f "$binary_path" ]]; then
    printf 'The release attaches %s and it is not in %s, so the manifest would name a download that does not exist.\n' \
      "$binary_name" "$binaries_directory" >&2
    exit 1
  fi

  # Upper case is what every manifest in the community repository carries; the schema accepts either, and matching what
  # a reviewer is used to reading costs nothing.
  binary_checksum="$(sha256sum "$binary_path" | cut --delimiter=' ' --fields=1 | tr '[:lower:]' '[:upper:]')"

  installer_entries+="- Architecture: $winget_architecture"$'\n'
  installer_entries+="  InstallerUrl: $release_download_base/v$package_version/$binary_name"$'\n'
  installer_entries+="  InstallerSha256: $binary_checksum"$'\n'

  printf '  %-9s %s\n' "$winget_architecture" "$binary_checksum" >&2
done

# The heredoc below supplies the line break after the last entry, so carrying a second one here would leave a blank
# line in the middle of the installer manifest.
installer_entries="${installer_entries%$'\n'}"

# `ManifestType: version` — the file that names the package and points at its default locale.
cat > "$package_directory/$package_identifier.yaml" << YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.$manifest_schema_version.schema.json

PackageIdentifier: $package_identifier
PackageVersion: $package_version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifest_schema_version
YAML

# `ManifestType: defaultLocale` — everything `winget show` renders. The URLs point at the documentation site rather than
# at the repository, because somebody reading a package listing wants the readable form.
#
# `PackageName` is the name `winget search` and `winget list` print in their `Name` column, so it carries the product
# rather than the command: a row reading `mfctl` tells somebody scanning a listing nothing about what they found. The
# command name reaches them by the two routes winget has for it — `Moniker`, which is what `winget install mfctl`
# resolves, and `Commands` in the installer manifest, which is what puts `mfctl` on the `PATH`. `Publisher` names the
# project rather than the person for the same reason the identifier's first segment does: winget's convention is
# `Publisher.Package`, and the two are read together. `Author` is where the person stays.
cat > "$package_directory/$package_identifier.locale.en-US.yaml" << YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.$manifest_schema_version.schema.json

PackageIdentifier: $package_identifier
PackageVersion: $package_version
PackageLocale: en-US
Publisher: MailFathom
PublisherUrl: https://krzysztof318.github.io/MailFathom/
PublisherSupportUrl: https://github.com/Krzysztof318/MailFathom/issues
Author: Krzysztof Kasprowicz
PackageName: MailFathom CLI
PackageUrl: https://github.com/Krzysztof318/MailFathom
License: AGPL-3.0-only
LicenseUrl: https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE
Copyright: Copyright © 2026 Krzysztof Kasprowicz
CopyrightUrl: https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE
ShortDescription: The administrative command-line client for MailFathom, a self-hosted AI-native mail service.
Description: |-
  mfctl administers a MailFathom deployment over HTTP. It signs in against the deployment's administrative endpoint,
  keeps a profile for each deployment you administer, checks that a stored credential still works, and completes the
  OAuth authorization a mailbox needs — without ever reading the service's configuration, opening its database, or
  touching its secret store.

  MailFathom itself runs on Linux, as a container or as a native process, and synchronizes IMAP mailboxes into a
  PostgreSQL database you own, indexes them, and serves them to AI agents over the Model Context Protocol. mfctl is
  the client you administer such a deployment from, which is why it ships for Windows as well.

  No mfctl binary carries a code signature. Every release publishes a checksum file covering all of them, and the hash
  in this manifest is the one winget verifies the download against.
Moniker: mfctl
Tags:
- ai
- cli
- email
- imap
- mail
- mcp
- model-context-protocol
- self-hosted
ReleaseNotesUrl: https://github.com/Krzysztof318/MailFathom/releases/tag/v$package_version
Documentations:
- DocumentLabel: Administering a deployment
  DocumentUrl: https://krzysztof318.github.io/MailFathom/operations/admin-endpoint.html
- DocumentLabel: Installation
  DocumentUrl: https://krzysztof318.github.io/MailFathom/users/installation.html
ManifestType: defaultLocale
ManifestVersion: $manifest_schema_version
YAML

# `ManifestType: installer` — a bare executable is `portable`, and `Commands` is what makes it reachable as `mfctl`
# rather than under the versioned file name the release attaches it as.
cat > "$package_directory/$package_identifier.installer.yaml" << YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.$manifest_schema_version.schema.json

PackageIdentifier: $package_identifier
PackageVersion: $package_version
InstallerType: portable
Commands:
- mfctl
ReleaseDate: $release_date
Installers:
$installer_entries
ManifestType: installer
ManifestVersion: $manifest_schema_version
YAML

printf '\nRendered the %s manifest set for %s under %s.\n' \
  "$package_identifier" "$package_version" "$package_directory" >&2

printf 'identifier=%s\n' "$package_identifier"
printf 'package-directory=%s\n' "$package_directory"
