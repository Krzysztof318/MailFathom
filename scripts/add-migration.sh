#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# Appends one EF Core migration for the current model. It preserves every migration already in the repository: the
# baseline is immutable, and a migration records how a schema got from one shape to the next rather than describing the
# shape it currently has.
#
# Nothing in this repository deletes a migration. The workflow that regenerated a single `Initial` baseline is gone,
# because a migration identity that can be regenerated is one a database may have recorded and no later migration can
# reach. Clearing local data is a different operation and keeps its own command, which drops the database and replays
# the migrations rather than touching the files:
#
#     aspire resource mailfathom-migrations ef-database-reset --apphost backend/src/AppHost/AppHost.csproj --non-interactive
#
# Nothing here starts the orchestration, and nothing here touches a database. Generating a migration compares the EF
# Core model against the committed model snapshot and writes files, which is a question about the checkout and has the
# same answer whether or not PostgreSQL is running. Applying the result is a separate, deliberate step against the
# orchestrated database, because that is the step whose outcome depends on what the database already holds:
#
#     aspire resource mailfathom-migrations ef-database-update --apphost backend/src/AppHost/AppHost.csproj --non-interactive
#
# That separation is the point rather than an optimization. A migration that generates cleanly and fails to apply is
# the case worth seeing, and folding the two together hides which of them a failure came from.
#
# The script is deliberately not a substitute for reading the result. It stops after generating the migration and
# leaves the review to a human; nothing here decides that a migration is correct.

startup_project='backend/src/Host/Host.csproj'
migrations_project='backend/src/Infrastructure/Infrastructure.csproj'
migrations_directory='backend/src/Infrastructure/Persistence/Migrations'
migrations_output_directory='Persistence/Migrations'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'add-migration.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

report() {
  printf '\n▸ %s\n' "$1"
}

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

migration_name="${1:-}"

# The name is the migration's permanent identity: it is written into __EFMigrationsHistory on every database that
# applies it, so it cannot be corrected later without touching every installation that already ran it.
if [[ -z "$migration_name" ]]; then
  fail "Usage: scripts/add-migration.sh <MigrationName>

The name describes what the migration does to the schema, in PascalCase, for example AddEmailRetentionPolicy.
It becomes part of a permanent identity, so it is chosen once and never renamed."
fi

if [[ ! "$migration_name" =~ ^[A-Z][A-Za-z0-9]*$ ]]; then
  fail "'${migration_name}' is not a usable migration name. Use PascalCase letters and digits, for example AddEmailRetentionPolicy."
fi

# `Initial` is the baseline. EF would refuse the duplicate anyway, but refusing it here says why rather than reporting
# a name collision, because the reason someone reaches for it is usually the retired regeneration habit.
if [[ "$migration_name" == 'Initial' ]]; then
  fail "'Initial' is the baseline migration and is immutable. A model change adds a new migration beside it with a name
describing the change."
fi

if compgen -G "${migrations_directory}/*_${migration_name}.cs" >/dev/null; then
  fail "A migration named '${migration_name}' already exists. Choose a name describing this change."
fi

# The migration this one will follow, read before the new file exists, because it names the range the review has to
# cover. Migration files sort chronologically by the timestamp EF prefixes them with.
migration_this_one_follows='0'

mapfile -t existing_migrations < <(
  find "$migrations_directory" -maxdepth 1 -name '*_*.cs' ! -name '*.Designer.cs' -printf '%f\n' | sort
)

if [[ "${#existing_migrations[@]}" -gt 0 ]]; then
  migration_this_one_follows="${existing_migrations[-1]%.cs}"
fi

# A migration is generated for exactly one text search configuration, because the value is compiled into the search
# vector's stored generated column, and the host refuses to run against an index built under a different one.
if [[ -n "${Persistence__TextSearchConfiguration:-}" ]]; then
  report "Generating against Persistence__TextSearchConfiguration=${Persistence__TextSearchConfiguration}."
fi

# The design-time factory needs a connection string to construct the context even though nothing connects, so a value
# is supplied only when the environment carries none. Pointing it at a port nothing listens on makes the absence of a
# connection observable: if generation ever starts requiring a database, it fails here rather than silently reaching
# whatever database the developer's environment happened to name.
if [[ -z "${ConnectionStrings__mailfathom:-}" && -z "${MAILFATHOM_DESIGN_TIME_CONNECTION_STRING:-}" ]]; then
  export MAILFATHOM_DESIGN_TIME_CONNECTION_STRING='Host=127.0.0.1;Port=1;Database=unreachable-by-design;Username=none;Timeout=2'
fi

# dotnet-ef is a manifest-local tool, so it exists for this checkout only once the manifest has been restored. A
# developer with a global install would not notice the difference, which is exactly why this is not left to chance.
report 'Restoring the local tools.'
dotnet tool restore

# The startup project is what dotnet-ef loads MailFathom.Infrastructure.dll from, so building Infrastructure alone would
# leave a stale assembly there and the tool would read the model the previous build produced.
report 'Building the startup project so the tool sees the current model.'
dotnet build "$startup_project" --nologo --verbosity quiet

report "Generating the ${migration_name} migration."
dotnet ef migrations add "$migration_name" \
  --project "$migrations_project" \
  --startup-project "$startup_project" \
  --output-dir "$migrations_output_directory" \
  --no-build

# EF's generator writes no file header and IDE0073 is an error in this directory like everywhere else, so the formatter
# runs here rather than being left to the verification loop, which formats only after a successful build that a missing
# header would fail. The solution is the scope and the files are named individually: an --include filter evaluated
# against another project's workspace silently matches nothing, and so does one given a directory.
report 'Writing the copyright and license header into the generated files.'
dotnet format backend/MailFathom.slnx --no-restore --include "$migrations_directory"/*.cs

report 'Generated. Review it as SQL, then apply it to the orchestrated database:'
printf '\n    scripts/script-migration.sh %s\n' "$migration_this_one_follows"
printf '    aspire resource mailfathom-migrations ef-database-update --apphost backend/src/AppHost/AppHost.csproj --non-interactive\n'
printf '    scripts/dump-local-schema.sh\n\n'
