#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# Writes the SQL for a migration range to standard output, so a migration is reviewed as the statements PostgreSQL will
# actually run rather than as the C# EF generated. The generated C# hides the destructive operation, the rewrite EF
# inferred from a rename, the lock a column change takes, and the type PostgreSQL settled on.
#
# Usage:
#   scripts/script-migration.sh                      the newest migration, from the one before it
#   scripts/script-migration.sh <from>               from that migration to the newest one
#   scripts/script-migration.sh <from> <to>          an explicit range
#
# `0` is EF's name for an empty database, so `scripts/script-migration.sh 0` produces the whole schema from nothing.
#
# This runs dotnet-ef directly, which the repository otherwise forbids. The rule exists because a command that touches
# the database has to see the server and the connection string the orchestration issues, and every such command goes
# through the AppHost's migration resource for that reason. This one opens no connection: it reads the migration
# assembly and prints SQL, and it produces identical output against a database that does not exist. Routing it through
# the orchestration would mean starting PostgreSQL and a container to answer a question about files in the checkout.
#
# The output is written to standard output rather than to a file, so a review artifact is never left behind in the
# working tree. It carries schema only and no row, so it holds no mail data and no personal data.

startup_project='src/Host/Host.csproj'
migrations_project='src/Infrastructure/Infrastructure.csproj'
migrations_directory='src/Infrastructure/Persistence/Migrations'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'script-migration.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

# Migration files sort chronologically by the timestamp EF prefixes them with, so the last two entries are the newest
# migration and the one it follows.
mapfile -t existing_migrations < <(
  find "$migrations_directory" -maxdepth 1 -name '*_*.cs' ! -name '*.Designer.cs' -printf '%f\n' | sort
)

if [[ "${#existing_migrations[@]}" -eq 0 ]]; then
  printf 'There are no migrations to script.\n' >&2
  exit 1
fi

newest_migration="${existing_migrations[-1]%.cs}"

if [[ "${#existing_migrations[@]}" -ge 2 ]]; then
  migration_before_the_newest="${existing_migrations[-2]%.cs}"
else
  migration_before_the_newest='0'
fi

from_migration="${1:-$migration_before_the_newest}"
to_migration="${2:-$newest_migration}"

printf 'Scripting %s → %s\n' "$from_migration" "$to_migration" >&2

# The design-time factory needs a connection string to construct the context even though nothing connects, so a value
# is supplied only when the environment carries none. Pointing it at a port nothing listens on makes the absence of a
# connection observable: if this ever starts requiring a database, it fails here rather than silently reading one.
if [[ -z "${ConnectionStrings__mailfathom:-}" && -z "${MAILFATHOM_DESIGN_TIME_CONNECTION_STRING:-}" ]]; then
  export MAILFATHOM_DESIGN_TIME_CONNECTION_STRING='Host=127.0.0.1;Port=1;Database=unreachable-by-design;Username=none;Timeout=2'
fi

# dotnet-ef is a manifest-local tool, so it exists for this checkout only once the manifest has been restored. Both this
# and the build below write to standard error, because standard output carries the SQL and nothing else: a reviewer
# redirects it into a file, and a restore banner in there would be part of the artifact.
dotnet tool restore >&2

# Built separately and quietly for the same reason: dotnet-ef writes the build banner to standard output too.
dotnet build "$startup_project" --nologo --verbosity quiet >&2

dotnet ef migrations script "$from_migration" "$to_migration" \
  --project "$migrations_project" \
  --startup-project "$startup_project" \
  --no-build
