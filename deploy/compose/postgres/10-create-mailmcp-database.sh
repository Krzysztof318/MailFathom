#!/usr/bin/env bash
set -euo pipefail

# Runs once, from the PostgreSQL image's own initialization hook, on an empty data directory and while a superuser is
# the one connected. It exists so that MailMcp never has to be one.
#
# Two things need a superuser and only these two: creating a role, and installing the `vector` extension. Doing both
# here leaves the schema migration and the running service able to connect as an ordinary role that owns its own
# database and nothing else. The migration's `CREATE EXTENSION IF NOT EXISTS vector` then finds the extension already
# present and succeeds without the privilege it would otherwise need.
#
# A data directory that already exists is never re-initialized, so editing this file changes nothing about a running
# deployment. Apply the same statements by hand when a role has to be added to one.

readonly database_name="${MAILMCP_DATABASE:-mailmcp}"
readonly database_role="${MAILMCP_DATABASE_ROLE:-mailmcp}"
readonly password_file='/run/secrets/mailmcp-database-password'

if [[ ! -r "$password_file" ]]; then
  printf 'The MailMcp database password was not mounted at %s.\n' "$password_file" >&2
  exit 1
fi

# One trailing newline is stripped, because a secret file written by an editor almost always ends with one and an
# untrimmed byte becomes part of the password. This is the same rule MailMcp's own secret resolution applies.
database_password="$(cat -- "$password_file")"
database_password="${database_password%$'\n'}"

if [[ -z "$database_password" ]]; then
  printf 'The MailMcp database password at %s is empty.\n' "$password_file" >&2
  exit 1
fi

# The password reaches psql as a variable rather than as interpolated SQL, so a value containing a quotation mark
# cannot end the string it was meant to be inside. `:'name'` is psql's own quoting of a variable as a literal.
psql --no-psqlrc --quiet --set ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set database_role="$database_role" \
  --set database_name="$database_name" \
  --set database_password="$database_password" <<'INITIALIZE_ROLE_AND_DATABASE'
CREATE ROLE :"database_role" WITH LOGIN PASSWORD :'database_password';
CREATE DATABASE :"database_name" WITH OWNER :"database_role";
INITIALIZE_ROLE_AND_DATABASE

# Connected to the new database, still as the superuser, because installing an extension is the second thing an
# ordinary role may not do. Nothing else in the schema is created here: the tables, indexes, and constraints are the
# migration's, and creating any of them now would leave two mechanisms describing one schema.
psql --no-psqlrc --quiet --set ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$database_name" \
  --command 'CREATE EXTENSION IF NOT EXISTS vector;'

printf 'Created database %s owned by role %s, with the vector extension installed.\n' "$database_name" "$database_role"
