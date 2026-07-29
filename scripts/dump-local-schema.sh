#!/usr/bin/env bash
set -euo pipefail

# Dumps the schema of the orchestrated local PostgreSQL database so a migration can be reviewed as SQL rather than only
# as generated C#. It reads only the schema, never any row, so the output carries no mail data and no personal data.
#
# The credential is read from the AppHost's user secrets, which is where Aspire keeps the password it generates for the
# local container. Nothing here writes a credential anywhere, and the dump is written to standard output so it is never
# left behind in the working tree by accident.

app_host_user_secrets_id='mailmcp-apphost-6f1c0a24-8a2c-4f5c-9d3a-4c2d0f8b71e5'
postgres_image='pgvector/pgvector:0.8.2-pg17'
database_name='mailmcp'

secrets_file="${HOME}/.microsoft/usersecrets/${app_host_user_secrets_id}/secrets.json"
if [[ ! -f "$secrets_file" ]]; then
  printf 'No AppHost user secrets at %s. Start the orchestration once with `aspire start` first.\n' "$secrets_file" >&2
  exit 1
fi

container_id="$(docker ps --filter "ancestor=${postgres_image}" --format '{{.ID}}' | head -1)"
if [[ -z "$container_id" ]]; then
  printf 'No running %s container. Start the orchestration with `aspire start --apphost src/AppHost/AppHost.csproj`.\n' "$postgres_image" >&2
  exit 1
fi

# The file is UTF-8 with a BOM, which python's default JSON decoder rejects, so it is read as utf-8-sig.
postgres_password="$(python3 -c "
import io
import json
import sys

with io.open(sys.argv[1], encoding='utf-8-sig') as secrets:
    print(json.load(secrets)['Parameters:postgres-password'])
" "$secrets_file")"

docker exec --env "PGPASSWORD=${postgres_password}" "$container_id" \
  pg_dump --username postgres --dbname "$database_name" --schema-only --no-owner --no-privileges
