#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

# Dumps the schema of the orchestrated local PostgreSQL database so a migration can be reviewed as SQL rather than only
# as generated C#. It reads only the schema, never any row, so the output carries no mail data and no personal data.
#
# The container and the credential both come from the orchestration this AppHost is running, which is what makes the
# dump describe this checkout's database. Nothing here writes a credential anywhere, and the dump is written to standard
# output so it is never left behind in the working tree by accident.

app_host_project='backend/src/AppHost/AppHost.csproj'
postgres_resource='postgres'
database_name='mailfathom'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'dump-local-schema.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

# Asking the orchestration rather than Docker is the whole point of this step. Every worktree runs the same pgvector
# image with its own container, its own data volume, and its own schema, and the AppHost's UserSecretsId is committed,
# so a sibling checkout's container answers to the same password under the same database name. Selecting by image would
# then dump a schema belonging to another checkout and present it for review with nothing failing. `aspire describe` is
# scoped to the AppHost project it is given, so it names the container this checkout owns or reports none at all.
resource_description="$(aspire describe "$postgres_resource" \
  --apphost "$app_host_project" --non-interactive --nologo --format Json 2>/dev/null)"

# The password is read from the same description rather than from the AppHost's user secrets, so both facts describe
# one container: the secrets store holds the password every checkout on this machine shares, which cannot say which
# container it opens. The two values are read as separate lines because a generated password may contain spaces.
{
  read -r container_id
  read -r postgres_password
} < <(python3 -c "
import json
import sys

try:
    resources = json.loads(sys.stdin.read()).get('resources', [])
except json.JSONDecodeError:
    resources = []

container = next(
    (resource for resource in resources if resource.get('properties', {}).get('container.id')),
    {},
)

print(container.get('properties', {}).get('container.id', ''))
print(container.get('environment', {}).get('POSTGRES_PASSWORD', ''))
" <<<"$resource_description")

if [[ -z "${container_id:-}" ]]; then
  printf 'No running %s container for %s. Start the orchestration with `aspire start --apphost %s`.\n' \
    "$postgres_resource" "$app_host_project" "$app_host_project" >&2
  exit 1
fi

if [[ -z "${postgres_password:-}" ]]; then
  printf 'The %s resource reports no password, so its schema cannot be read. Restart the orchestration and try again.\n' \
    "$postgres_resource" >&2
  exit 1
fi

docker exec --env "PGPASSWORD=${postgres_password}" "$container_id" \
  pg_dump --username postgres --dbname "$database_name" --schema-only --no-owner --no-privileges
