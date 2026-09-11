#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Drives the real client against a real deployment filled with fabricated mail.
#
# It runs on request and nowhere else — no verification gate calls it, no pull-request workflow does, and `End-to-end
# client` is manual dispatch only. What it answers is the one question neither committed suite can: whether a person can
# sign in to a MailFathom that was stood up the way an operator stands one up, and read the mail it synchronized.
#
# The deployment is assembled in the order an operator assembles one, and every step below is named so that a failure
# says which of them broke. That ordering is the whole design: a corpus that never reached the mailbox and a client that
# cannot draw a list are two different defects, and a run reporting only "the specs failed" would confuse them.
#
#   1. the client's bundle, built exactly as the published image builds it
#   2. the schema artifact, built exactly as a release builds it
#   3. the database and the mail server, started from this repository's own app model
#   4. the schema artifact applied to that database
#   5. MailFathom published and started against both servers, serving the bundle from its client endpoint
#   6. the mailbox of the one user a fresh database holds, recorded through the administrative API
#   7. a client credential provisioned through the same API
#   8. the corpus replayed into the mailbox over SMTP
#   9. the account's synchronization run reporting no failed folder
#  10. the end-to-end specs, in a real browser, against all of it
#
# Everything it touches is fabricated and thrown away: the mailbox is a container, its mail is a corpus this repository
# generated, and the credential exists for the length of one run. Nothing here reaches a real mailbox, and nothing it
# produces is anybody's personal data — which is why this run keeps its traces and screenshots and the pull-request
# browser suite does not.
#
# Usage:
#   scripts/run-end-to-end-client.sh                 the whole run
#   scripts/run-end-to-end-client.sh --grep thread   trailing arguments are forwarded to Playwright

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'run-end-to-end-client.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# What the app model decides, read out of backend/src/AppHost/OrchestrationContract.cs rather than written here. The
# ports say where the two servers are published, the mailbox values say what the mail server was configured with, and
# the data key is what a synthetic deployment seals its rows with — every one of them would be a silent failure if this
# script and the app model held two copies that drifted. A missing constant stops the run naming it.
read_orchestration_constant() {
  local declared_type="$1" constant_name="$2" pattern value

  case "$declared_type" in
    int) pattern="s/.*public const int $constant_name = \([0-9]\+\);.*/\1/p" ;;
    *) pattern="s/.*public const string $constant_name = \"\([^\"]*\)\";.*/\1/p" ;;
  esac

  value="$(sed -n "$pattern" backend/src/AppHost/OrchestrationContract.cs | head -n 1)"

  if [[ -z "$value" ]]; then
    printf 'OrchestrationContract declares no %s, so this script cannot know what the app model started.\n' \
      "$constant_name" >&2
    exit 1
  fi

  printf '%s' "$value"
}

imap_port="$(read_orchestration_constant int EndToEndClientImapPort)"
smtp_port="$(read_orchestration_constant int EndToEndClientSmtpPort)"
postgres_port="$(read_orchestration_constant int EndToEndClientPostgresPort)"
mail_server_api_port="$(read_orchestration_constant int EndToEndClientMailServerApiPort)"
mailbox_login="$(read_orchestration_constant string MailServerAccountUserName)"
mailbox_address="$(read_orchestration_constant string MailServerAccountEmailAddress)"
mailbox_password="$(read_orchestration_constant string MailServerAccountPassword)"
postgres_user_name="$(read_orchestration_constant string PostgresUserName)"
postgres_password="$(read_orchestration_constant string PostgresPassword)"
database_name="$(read_orchestration_constant string DatabaseResourceName)"
data_encryption_key_id="$(read_orchestration_constant string DataEncryptionKeyId)"
data_encryption_key_name="$(read_orchestration_constant string DataEncryptionKeyName)"
data_encryption_key_material="$(read_orchestration_constant string DataEncryptionKeyMaterial)"

# MailFathom's own three sockets, which this script owns because this script starts the host. They are stated rather
# than found for the reason the app model's are: a browser and a shell both have to reach them, and neither can ask an
# orchestration that is not running the host.
readonly client_endpoint_port=24180
readonly admin_endpoint_port=24181
readonly health_endpoint_port=24182

readonly loopback='127.0.0.1'
readonly client_origin="http://$loopback:$client_endpoint_port"
readonly admin_origin="http://$loopback:$admin_endpoint_port"
readonly health_origin="http://$loopback:$health_endpoint_port"

# Fabricated, and none of them outlives the run: the credential is provisioned into a database that is deleted with its
# container, and the administrative key authenticates one process on loopback.
readonly client_username='end-to-end'
readonly client_password='end-to-end-password'
readonly admin_api_key_name='end-to-end-admin'
readonly admin_api_key='end-to-end-only-admin-api-key'
readonly account_identifier='end-to-end'

readonly corpus_archive='backend/tools/SyntheticMail/corpora/office-en.zip'

container_runtime="${MAILFATHOM_CONTAINER_RUNTIME:-docker}"

if ! command -v "$container_runtime" > /dev/null; then
  printf 'The end-to-end client run needs a container runtime. %s was not found on PATH; set MAILFATHOM_CONTAINER_RUNTIME to the one to use.\n' \
    "$container_runtime" >&2
  exit 1
fi

# The same prefix and the same identifier the integration suite uses, because the app model names its ephemeral
# resources from them and both runs select the same two servers. One identifier per run keeps a second run — or a
# concurrent integration suite — from being destroyed by this one's cleanup.
readonly ephemeral_resource_prefix='mailfathom-integrationtests'
ephemeral_run_identifier="$(od -A n -N 4 -t x1 /dev/urandom | tr -d ' \n')"
export MAILFATHOM_INTEGRATIONTESTS_RUN_ID="$ephemeral_run_identifier"
ephemeral_run_prefix="${ephemeral_resource_prefix}-${ephemeral_run_identifier}"

work_directory="$(mktemp --directory)"
run_directory='artifacts/end-to-end-client'
app_model_pid=''
host_pid=''
current_step='starting up'

step() {
  current_step="$1"
  printf '\n== %s ==\n' "$1" >&2
}

# Every failure is reported against the step it happened in, which is the difference between "the corpus never arrived"
# and "the client could not draw the list" — two defects a bare non-zero exit would report identically.
finish() {
  local exit_code=$?

  if ((exit_code != 0)); then
    printf '\nend-to-end-client: failed while %s (exit %d).\n' "$current_step" "$exit_code" >&2
    printf 'The service and orchestration logs of this run are under %s/.\n' "$run_directory" >&2
  fi

  if [[ -n "$host_pid" ]]; then
    kill "$host_pid" 2> /dev/null || true
    wait "$host_pid" 2> /dev/null || true
  fi

  if [[ -n "$app_model_pid" ]]; then
    kill "$app_model_pid" 2> /dev/null || true
    wait "$app_model_pid" 2> /dev/null || true
  fi

  remove_ephemeral_resources

  rm --recursive --force "$work_directory"

  exit "$exit_code"
}

# Removed whether the run passed, failed, or was interrupted, and named by this run's own identifier rather than by the
# shared prefix, so a concurrent integration suite keeps its containers.
remove_ephemeral_resources() {
  local ephemeral_containers ephemeral_volumes

  mapfile -t ephemeral_containers < <(
    "$container_runtime" ps --all --quiet --filter "name=^${ephemeral_run_prefix}"
  )

  if ((${#ephemeral_containers[@]} > 0)); then
    "$container_runtime" rm --force --volumes "${ephemeral_containers[@]}" > /dev/null || true
  fi

  mapfile -t ephemeral_volumes < <(
    "$container_runtime" volume ls --quiet --filter "name=^${ephemeral_run_prefix}"
  )

  if ((${#ephemeral_volumes[@]} > 0)); then
    "$container_runtime" volume rm "${ephemeral_volumes[@]}" > /dev/null || true
  fi
}

trap finish EXIT

# Waits for a condition rather than for a duration, which is the rule every wait in this script keeps: a sleep long
# enough for a slow machine is time every fast run pays, and a sleep short enough for a fast one is a flake.
#
# The third argument is the process whose readiness is being waited for, or nothing. A process that has already exited
# is not something more waiting will fix — without this, a host that failed its own startup validation is reported five
# minutes later as a host that was slow, which is the wrong defect and the wrong log to go and read. `kill -0` answers
# here because these are this script's own children in this script's own process namespace.
wait_until() {
  local description="$1" attempts="$2" watched_process="$3"
  shift 3

  for ((attempt = 1; attempt <= attempts; attempt++)); do
    if "$@" > /dev/null 2>&1; then
      return 0
    fi

    if [[ -n "$watched_process" ]] && ! kill -0 "$watched_process" 2> /dev/null; then
      printf '%s stopped before it was ready.\n' "$description" >&2

      return 1
    fi

    sleep 1
  done

  printf '%s did not happen within %d seconds.\n' "$description" "$attempts" >&2

  return 1
}

rm --recursive --force "$run_directory"
mkdir --parents "$run_directory"

step 'building the client bundle'

# The same two commands the image's client stage runs, so what the specs drive is the directory a published image
# serves rather than a development server's on-demand transform of the same source.
pnpm --dir frontend install --frozen-lockfile
pnpm --dir frontend run build

step 'building the schema artifact'

# The artifact a release publishes and an operator applies, built by the script that publishes it. Nothing else in this
# repository applies it at startup, which is exactly why this run does.
schema_output="$(bash scripts/build-schema-artifact.sh "$work_directory/schema")"
schema_artifact="${schema_output#*artifact=}"
schema_artifact="${schema_artifact%%$'\n'*}"

if [[ ! -s "$schema_artifact" ]]; then
  printf 'The schema build produced no artifact to apply.\n' >&2
  exit 1
fi

step 'starting the orchestrated database and mail server'

# The repository's own app model, selected onto the topology that starts those two servers and no MailFathom at all.
# Nothing here declares a container: the images, their pins, and their configuration are the app model's, so this run
# exercises the same PostgreSQL and the same mail server every other run of this repository exercises.
dotnet run --project backend/src/AppHost/AppHost.csproj -- EndToEndClient=true \
  > "$run_directory/orchestration.log" 2>&1 &
app_model_pid=$!

postgres_container="${ephemeral_run_prefix}-postgres"

wait_until 'The orchestrated PostgreSQL server' 300 "$app_model_pid" \
  "$container_runtime" exec "$postgres_container" pg_isready --username "$postgres_user_name"

wait_until 'The orchestrated mail server' 300 "$app_model_pid" \
  curl --fail --silent "http://$loopback:$mail_server_api_port/api/service/readiness"

step 'applying the schema artifact'

# Applied through the container rather than from the host, so the run needs no psql of its own — the same route
# `docs/operations/database-schema.md` gives an operator whose database is not reachable from where they stand.
# ON_ERROR_STOP is not optional: without it psql reports a failure and carries on to the next statement.
#
# The password is passed by name rather than by value, which is what keeps it out of the command line every process on
# this machine can read: `--env PGPASSWORD` with nothing after it forwards whatever this shell holds.
export PGPASSWORD="$postgres_password"

# The database itself, because nothing else creates it here: the app model's migration resource is what creates it in
# every other topology, and this one starts no MailFathom to run one.
"$container_runtime" exec --interactive --env PGPASSWORD "$postgres_container" \
  psql --username "$postgres_user_name" --dbname postgres --command "CREATE DATABASE $database_name" > /dev/null

"$container_runtime" exec --interactive --env PGPASSWORD "$postgres_container" \
  psql --username "$postgres_user_name" --dbname "$database_name" --set ON_ERROR_STOP=on --quiet < "$schema_artifact"

unset PGPASSWORD

step 'publishing and starting MailFathom'

host_directory="$work_directory/host"

dotnet publish backend/src/Host/Host.csproj \
  --configuration Release \
  --output "$host_directory" \
  --nologo

# Where the image puts it, so the host resolves the bundle beneath its own content root exactly as the container does.
mkdir --parents "$host_directory/wwwroot"
cp --recursive frontend/src/Client.App/dist/. "$host_directory/wwwroot"

# The deployment's own configuration, and every value in it is one an operator writes. Two of them are opt-ins the
# product refuses by default and this deployment states deliberately: the page is served over a loopback socket in the
# clear. Stating them is the point rather than a shortcut — a deployment that serves a page without TLS has to say so,
# and this is what saying so looks like. The mailbox is not among them: a deployment declares no mail account in its own
# file, so this one is recorded through the administrative API a few steps below, opt-ins and all.
#
# Started from the published directory, which is what the container does and what makes the web root resolve at all: the
# host reads its content root from the process's working directory, so one started from the repository looks for the
# bundle there and refuses at startup to serve a client it is in fact carrying.
env --chdir="$host_directory" \
  ConnectionStrings__mailfathom="Host=$loopback;Port=$postgres_port;Database=$database_name;Username=$postgres_user_name;Password=$postgres_password" \
  DataEncryption__ActiveKeyId="$data_encryption_key_id" \
  DataEncryption__Keys__0__KeyId="$data_encryption_key_id" \
  DataEncryption__Keys__0__Material__Name="$data_encryption_key_name" \
  DataEncryption__Keys__0__Material__SecretReference="plaintext:$data_encryption_key_material" \
  MailSynchronization__Enabled='true' \
  ClientEndpoint__Enabled='true' \
  ClientEndpoint__BindAddress="$loopback" \
  ClientEndpoint__Port="$client_endpoint_port" \
  ClientEndpoint__Authentication__0__Method='password' \
  ClientEndpoint__Application__Enabled='true' \
  ClientEndpoint__Application__AllowClearText='true' \
  AdminEndpoint__Enabled='true' \
  AdminEndpoint__BindAddress="$loopback" \
  AdminEndpoint__Port="$admin_endpoint_port" \
  AdminEndpoint__Authentication__0__ApiKey__Name="$admin_api_key_name" \
  AdminEndpoint__Authentication__0__ApiKey__SecretReference="plaintext:$admin_api_key" \
  HealthEndpoints__BindAddress="$loopback" \
  HealthEndpoints__Port="$health_endpoint_port" \
  dotnet "$host_directory/MailFathom.Host.dll" \
  > "$run_directory/service.log" 2>&1 &
host_pid=$!

wait_until 'MailFathom' 300 "$host_pid" curl --fail --silent "$health_origin/started"

step 'recording the mailbox'

# The host started serving the one user a fresh database is seeded with, and no mailbox: no configuration source
# declares one, because a mailbox is a row in that user's record, and it arrives here the way an operator writes it,
# through the administrative API. That ordering is the point rather than a detour around a missing setting — the write
# that commits the record publishes it to the running roster, so the mailbox is served without a restart.
served_user="$(
  curl --fail --silent --header "Authorization: Bearer $admin_api_key" "$admin_origin/api/admin/users" \
    | jq --raw-output '[.users[] | select(.served)] | if length == 1 then .[0].id else ("expected one served user, found " + (length | tostring) | halt_error(1)) end'
)"

# The mailbox is a declaration in that user's record, written against the version the record stands at. The three
# transport opt-ins are here rather than in the environment for one reason: the mail server beside this run speaks no
# TLS, and a deployment reaching a clear-text server has to say so wherever the mailbox is declared.

record_version="$(
  curl --fail --silent --header "Authorization: Bearer $admin_api_key" \
    "$admin_origin/api/admin/users/$served_user/record" \
    | jq --raw-output '.version'
)"

# A refusal answers 200 with the reasons rather than a failing status, because every one of them is something the caller
# composes the next attempt from. So the outcome is read rather than the status code.
mailbox_declaration="$(
  curl --fail --silent --show-error \
    --header "Authorization: Bearer $admin_api_key" \
    --header 'Content-Type: application/json' \
    --data "$(
      jq --null-input \
        --argjson version "$record_version" \
        --arg accountId "$account_identifier" \
        --arg host "$loopback" \
        --argjson port "$imap_port" \
        --arg userName "$mailbox_login" \
        --arg password "$mailbox_password" \
        '{
           version: $version,
           account: ({
             AccountId: $accountId,
             DisplayName: "End-to-end mailbox",
             Host: $host,
             Port: $port,
             UserName: $userName,
             Secrets: {
               Password: {
                 Name: "end-to-end-mailbox-password",
                 SecretReference: ("plaintext:" + $password)
               }
             },
             TransportSecurity: {
               ConnectionSecurity: "None",
               AllowInsecureConnection: true,
               AllowClearTextAuthenticationOverUnencryptedConnection: true
             }
           } | tojson)
         }'
    )" \
    "$admin_origin/api/admin/users/$served_user/record/mail-accounts"
)"

printf '%s' "$mailbox_declaration" \
  | jq --exit-status 'if .committed then true else (("the deployment refused the mailbox: " + (.messages | join(" "))) | halt_error(1)) end' \
  > /dev/null

step 'provisioning the client credential'

# Through the administrative API rather than through the database, so the credential this run signs in with was created
# by the same route an operator creates one — the same password policy, the same hashing, the same audit record.
curl --fail --silent --show-error --output /dev/null \
  --header "Authorization: Bearer $admin_api_key" \
  --header 'Content-Type: application/json' \
  --data "$(
    jq --null-input --arg username "$client_username" --arg password "$client_password" \
      '{method: "password", username: $username, password: $password, permissions: null}'
  )" \
  "$admin_origin/api/admin/users/$served_user/credentials"

step 'replaying the corpus into the mailbox'

# The corpus is delivered rather than generated: what it says was decided once, when it was exported, so two runs fill
# the mailbox identically and a difference between two runs is a difference in the code.
#
# The credential file is written into this run's own directory rather than beside the built command, because the tool
# refuses a password on a command line and this password belongs to a container that is about to be destroyed. The
# login is the bare local part and the address is the whole string, which is how the app model configured the server;
# authenticating as the address would have it create a second mailbox and drop the connection.
cat > "$work_directory/synthetic-mail.local.json" << JSON
{
  "host": "$loopback",
  "port": $smtp_port,
  "security": "Unsecured",
  "address": "$mailbox_address",
  "userName": "$mailbox_login",
  "password": "$mailbox_password",
  "mailbox": {
    "host": "$loopback",
    "port": $imap_port,
    "security": "Unsecured",
    "address": "$mailbox_address",
    "userName": "$mailbox_login",
    "password": "$mailbox_password",
    "sentFolder": "INBOX"
  }
}
JSON

dotnet run --project backend/tools/SyntheticMail/SyntheticMail.csproj --configuration Release -- \
  replay "$corpus_archive" "$mailbox_address" --config "$work_directory/synthetic-mail.local.json"

step 'waiting for the account to synchronize'

# The run's own report rather than a duration: every folder of the account has to have reached a state that is not
# Failing, and at least one of them has to have synchronized, before a spec is allowed to read a list. A wait on a
# clock would report a slow first synchronization as a client that draws nothing.
account_synchronized() {
  curl --fail --silent --user "$client_username:$client_password" "$client_origin/api/client/folders" \
    | jq --exit-status '
        [.accounts[].folders[]] as $folders
        | ($folders | length) > 0
        and ([$folders[] | select(.synchronizationState == "Failing")] | length) == 0
        and ([$folders[] | select(.synchronizationState == "Synchronized")] | length) > 0
      ' > /dev/null
}

wait_until 'The account synchronization run' 600 "$host_pid" account_synchronized

step 'driving the client in a browser'

MAILFATHOM_CLIENT_ORIGIN="$client_origin" \
  MAILFATHOM_CLIENT_USERNAME="$client_username" \
  MAILFATHOM_CLIENT_PASSWORD="$client_password" \
  pnpm --dir frontend run test:end-to-end "$@"

current_step='reporting the result'

printf '\nThe end-to-end client run passed. It gates nothing.\n' >&2
