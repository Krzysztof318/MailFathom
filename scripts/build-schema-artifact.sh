#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Produces the schema artifact a released installation applies, and the checksum that identifies it.
#
# The artifact is one idempotent SQL script carrying every migration this build defines, guarded so that a database
# already holding some of them takes only what it is missing. It is what an operator reads, takes a backup against, and
# runs deliberately — see docs/operations/database-schema.md for the apply path and why the alternatives were refused.
#
# Usage:
#   scripts/build-schema-artifact.sh                                  artifacts/schema, named after the declared version
#   scripts/build-schema-artifact.sh <output-directory>
#   scripts/build-schema-artifact.sh <output-directory> <version>     the release pipeline passes the tag's version
#
# The SQL comes from `aspire publish` rather than from `dotnet ef migrations script`, because the app model already
# declares which context, which migrations project, and which options the artifact is generated with —
# `PublishAsMigrationScript(idempotent: true)` in backend/src/AppHost/Program.cs. A second invocation written here could drift
# from that declaration and nothing would notice until an operator applied the result.
#
# Nothing here reaches a database. Generating the script compares the migration assembly against nothing, so this runs
# on a machine with no PostgreSQL and produces identical output against a database that does not exist. The artifact
# carries schema only and no row, so it holds no mail data and no personal data.
#
# Two key=value lines go to standard output for the release pipeline to read; everything a human reads goes to standard
# error, so a caller can capture the first without filtering the second.

readonly apphost_project='backend/src/AppHost/AppHost.csproj'
readonly aspire_pin_file='backend/Directory.Packages.props'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'build-schema-artifact.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

output_directory="${1:-artifacts/schema}"
artifact_version="${2:-$(bash scripts/read-declared-version.sh)}"

# The Aspire CLI is a developer-machine install rather than a manifest tool, so its absence is reported with the command
# that fixes it. The version comes from the pinned hosting packages instead of being written here, because a CLI on a
# different Aspire generation from the app model it publishes is exactly the mismatch a restated number would hide.
if ! command -v aspire > /dev/null 2>&1; then
  pinned_aspire_version="$(
    sed -n 's:.*<PackageVersion Include="Aspire.Hosting.AppHost" Version="\([^"]*\)".*:\1:p' "$aspire_pin_file" |
      head -n 1
  )"

  printf 'The Aspire CLI is not on PATH. Install the version this repository pins:\n' >&2
  printf '  dotnet tool install --global Aspire.Cli --version %s\n' "$pinned_aspire_version" >&2
  exit 1
fi

publish_directory="$(mktemp --directory)"
trap 'rm --recursive --force "$publish_directory"' EXIT

printf 'Building the schema artifact for %s with %s.\n' "$artifact_version" "$(aspire --version)" >&2

# The EF Core tooling the migration resource drives is a manifest-local tool, so it exists for this checkout only once
# the manifest has been restored.
dotnet tool restore >&2

aspire publish \
  --apphost "$apphost_project" \
  --output-path "$publish_directory" \
  --non-interactive \
  --nologo >&2

# One script, because the app model declares one migration resource. Asserting the count rather than taking the first
# match is what makes a second resource — or a publish that produced nothing at all — fail here instead of shipping an
# artifact that covers part of the schema.
mapfile -t published_scripts < <(find "$publish_directory/efmigrations" -maxdepth 1 -name '*.sql' -type f | sort)

if [[ "${#published_scripts[@]}" -ne 1 ]]; then
  printf 'The publish produced %d SQL scripts under efmigrations/, and the artifact is exactly one.\n' \
    "${#published_scripts[@]}" >&2
  exit 1
fi

published_script="${published_scripts[0]}"

# The guard that makes the artifact re-runnable, asserted rather than assumed. Losing `idempotent: true` in the app
# model would produce a script that still applies cleanly to an empty database and fails against every other one, which
# is a defect an operator would find rather than this pipeline.
if ! grep --quiet 'IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory"' "$published_script"; then
  printf 'The generated script carries no migration-history guard, so it is not idempotent. Check that\n' >&2
  printf 'PublishAsMigrationScript in %s still requests it.\n' "$apphost_project" >&2
  exit 1
fi

mkdir --parents "$output_directory"

artifact_path="$output_directory/mailfathom-schema-$artifact_version.sql"
checksum_path="$artifact_path.sha256"

# Copied without its byte-order mark, which is the one edit this makes to what the publish produced. EF Core writes the
# script as UTF-8 with one, and psql does not skip it: the mark arrives as part of the first token, the apply stops on a
# syntax error at the first CREATE, and nothing in that message names a character nobody can see. That command is what
# docs/operations/database-schema.md gives an operator, so the mark makes the documented path from a downloaded release
# to a schema fail on its first statement. Only the first line is touched, so the SQL itself is byte-identical.
sed '1s/^\xef\xbb\xbf//' "$published_script" > "$artifact_path"

# Asserted rather than trusted, because the failure it guards against is silent at this end and fatal at the operator's:
# a build that ships a marked artifact again would look exactly like this one until somebody ran psql against it.
if [[ "$(head --bytes=3 "$artifact_path" | od --address-radix=n --format=x1 | tr --delete ' ')" == 'efbbbf' ]]; then
  printf 'The artifact still begins with a byte-order mark, which psql does not skip. Check that this\n' >&2
  printf 'script strips it from %s.\n' "$published_script" >&2
  exit 1
fi

# Written in sha256sum's own format and with the bare file name, so an operator verifies it from the directory the two
# files were downloaded into: `sha256sum --check mailfathom-schema-<version>.sql.sha256`. It covers the file above, so
# what an operator checksums is what they apply.
(cd "$output_directory" && sha256sum "$(basename "$artifact_path")" > "$(basename "$checksum_path")")

artifact_checksum="$(cut --delimiter=' ' --fields=1 < "$checksum_path")"

# The migration range the artifact covers, read back from the artifact itself rather than from the migrations directory:
# what a release records has to describe the file that shipped. Every history guard names its migration twice, so the
# identifiers are deduplicated while keeping the order the script applies them in.
mapfile -t artifact_migrations < <(
  sed -n 's/.*"MigrationId" = '"'"'\([^'"'"']*\)'"'"'.*/\1/p' "$artifact_path" | awk '!seen[$0]++'
)

if [[ "${#artifact_migrations[@]}" -eq 0 ]]; then
  printf 'The generated script applies no migration, so there is nothing to release.\n' >&2
  exit 1
fi

{
  printf '\n%s\n' "$artifact_path"
  printf '  sha256      %s\n' "$artifact_checksum"
  printf '  migrations  %d, from %s to %s\n' \
    "${#artifact_migrations[@]}" "${artifact_migrations[0]}" "${artifact_migrations[-1]}"
} >&2

printf 'artifact=%s\n' "$artifact_path"
printf 'checksum=%s\n' "$artifact_checksum"
printf 'migrations=%s\n' "${artifact_migrations[*]}"
