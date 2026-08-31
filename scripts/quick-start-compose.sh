#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
# Project repository: https://github.com/Krzysztof318/MailFathom

set -euo pipefail

### Prepares a Docker Compose deployment to evaluate MailFathom with, asking for the mailbox it is to synchronize.
#
# What it ends with is an address a chat client connects to. It prepares no MailFathom client of its own: the Uno
# Platform one whose bundle used to travel inside the image was withdrawn, the client is being rebuilt in React, and a
# deployment that switched the page on against a current image would be refused at startup — so this prepares the MCP
# endpoint, and the client surface waits for something to call it.
#
# Usage:
#   scripts/quick-start-compose.sh
#   scripts/quick-start-compose.sh --provider fastmail --user-name you@fastmail.com \
#     --display-name 'Personal mail' --password-file /path/to/password --non-interactive
#
# **This is the fastest way to try MailFathom, and it is not the recommended way to run it.** What it produces is a
# deployment on one machine, reachable from that machine alone, with its credentials in files under this checkout and
# no TLS, no backup, and no narrowed grant — which is the right shape for finding out what the product does and the
# wrong shape for a deployment anybody depends on. docs/users/installation.md is where that decision is made, and the
# four shapes it routes to are what a real installation is built from; this script decides none of it for you.
#
# What it performs is what docs/operations/deployment-compose.md documents by hand, in that order: the three files that
# have to exist, the directory and file modes the container's own account needs, the database, the schema step, and the
# two probes. One thing it adds to that path, and the closing report says so: it writes openssl-legacy.cnf beside
# compose.yaml and names it in OPENSSL_CONF through compose.override.yaml, which lowers the platform's OpenSSL security
# level to 1 for every TLS session this process makes — the database's and the object store's included, not the mail
# sessions alone. That is what makes a mailbox on a server offering only a 1024-bit group, a 1024-bit key, or a SHA-1
# signature reachable from a first run rather than a handshake error naming nothing, and --no-legacy-tls prepares the
# same deployment under the platform default. Everything else it writes is a value the manual path writes too, so what
# it produces is an ordinary Compose deployment rather than a second shape of one — every setting stays editable, and
# what makes it an evaluation is what nobody has configured yet rather than anything it did.
#
# Two of those steps are why this exists rather than a shorter page. A mode left at the clone's umask reaches startup
# as material that could not be found, because MailFathom collapses every file-system failure into one result so that
# no diagnostic quotes the path it was handed — so the modes are set here rather than asked for. And the schema step is
# separate on purpose: a deployment that skips it starts and refuses to serve, naming a migration rather than the step
# that applies it.
#
# Beyond that TLS policy, it decides nothing the deployment's own defaults decide. It publishes no port on an address
# other than loopback, enables no scanner, configures no model, and turns no authentication off that was not asked for
# by name. The one place it writes outside deploy/compose/ is nowhere: every file it creates is under that directory, and
# it creates none of them until every question has been answered.
#
# It reads a checkout, so unlike scripts/install-mfctl.sh it is not fetched over HTTP and run — `git clone` comes
# first, and the file is here to be read before it is run.

readonly repository='Krzysztof318/MailFathom'
readonly release_base="https://github.com/$repository/releases"
readonly documentation_base='https://krzysztof318.github.io/MailFathom'

# The port compose.yaml publishes, which every surface served on the container's own 8080 answers on: the MCP endpoint
# and the client's routes.
readonly published_port='8080'

# The administrative endpoint's own default port is 8080, which is the socket the MCP endpoint is already served on, and
# compose.yaml publishes nothing for it. So enabling it here means stating a port of its own and publishing that one —
# see the note on the missing mapping in compose.yaml.
readonly admin_port='8090'

usage() {
  cat << 'TEXT'
Prepares a MailFathom Docker Compose deployment to evaluate, and starts it.

The fastest way to try MailFathom on one machine. Not the recommended way to run one: what it
prepares has no TLS, no backup, and credentials in files under this checkout.

  --provider <name>        gmail, fastmail, icloud, yahoo, zoho, or custom. Decides the address.
  --host <host>            The IMAP host, for a custom provider.
  --port <port>            The IMAP port, for a custom provider. Defaults to 993.
  --user-name <address>    What the mailbox authenticates as.
  --display-name <name>    What an assistant calls this mailbox. Required, and never an identifier.
  --account-id <id>        The name every tool argument and log line uses. Defaults to 'primary'.
  --password-file <path>   Where to read the mailbox password from, instead of asking for it.
  --mcp-authentication <api-key|none>
                           Whether the MCP endpoint requires a generated key. Defaults to api-key.
  --admin-endpoint <off|api-key|none>
                           Whether the administrative endpoint is served, and how. Served with a
                           generated key when the MCP endpoint requires one, which is minted through
                           it. Defaults to off, and off with an MCP key is refused.
  --no-legacy-tls          Leave the platform's own TLS policy in force, instead of the relaxation
                           that reaches a mail server offering only weak parameters.
  --version <version>      The release to deploy, for example 0.6.0. Defaults to the newest.
  --no-start               Write the files and stop, without starting anything or applying a schema.
  --non-interactive        Ask nothing. Every answer above has to be given, and no schema is applied.
  --help                   Print this and exit.

The mailbox password is read without echo and written straight to its file, so it reaches neither the
shell history nor the process list. --password-file is the same thing for an unattended run.

Nothing is overwritten. An existing .env, configuration file, or secret stops the run naming the file.
TEXT
}

provider=''
imap_host=''
imap_port=''
user_name=''
display_name=''
account_id='primary'
password_file=''
mcp_authentication='api-key'
admin_endpoint='off'
# Whether the line above is the default or the operator's own answer. The MCP key this script prepares is minted over
# the administrative endpoint, so a default of `off` is corrected where it was asked for and an explicit one is refused.
admin_endpoint_chosen='no'
relax_tls_policy='yes'
requested_version=''
start_stack='yes'
interactive='yes'

while [[ $# -gt 0 ]]; do
  case "$1" in
    --provider | --host | --port | --user-name | --display-name | --account-id | --password-file | \
      --mcp-authentication | --admin-endpoint | --version)
      [[ $# -ge 2 ]] || { printf '%s takes a value.\n' "$1" >&2; exit 1; }
      case "$1" in
        --provider) provider="$2" ;;
        --host) imap_host="$2" ;;
        --port) imap_port="$2" ;;
        --user-name) user_name="$2" ;;
        --display-name) display_name="$2" ;;
        --account-id) account_id="$2" ;;
        --password-file) password_file="$2" ;;
        --mcp-authentication) mcp_authentication="$2" ;;
        --admin-endpoint) admin_endpoint="$2"; admin_endpoint_chosen='yes' ;;
        --version) requested_version="$2" ;;
      esac
      shift 2
      ;;
    --no-legacy-tls) relax_tls_policy='no'; shift ;;
    --no-start) start_stack='no'; shift ;;
    --non-interactive) interactive='no'; shift ;;
    --help | -h) usage; exit 0 ;;
    *)
      printf 'Unrecognized argument: %s\n\n' "$1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

# Resolved against the caller's own directory, because everything below runs from deploy/compose and a relative path
# would otherwise name a file there instead of the one they meant.
if [[ -n "$password_file" && "$password_file" != /* ]]; then
  password_file="$PWD/$password_file"
fi

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'quick-start-compose.sh runs from a checkout of %s, and this is not one.\n' "$repository" >&2
  printf 'Clone it first: git clone https://github.com/%s.git\n' "$repository" >&2
  exit 1
fi

readonly compose_directory="$repository_root/deploy/compose"
if [[ ! -f "$compose_directory/compose.yaml" ]]; then
  printf 'This checkout has no %s, so there is no Compose deployment to prepare.\n' 'deploy/compose/compose.yaml' >&2
  exit 1
fi

# Copied rather than written here, so the file this deployment runs under and the one the documentation reviews are one
# file. docs/operations/platform-tls-policy.md is where what it relaxes, and what that costs, is argued.
readonly legacy_tls_source="$repository_root/deploy/openssl/legacy-mail-server.cnf.example"
if [[ "$relax_tls_policy" == 'yes' && ! -f "$legacy_tls_source" ]]; then
  printf 'This checkout has no %s to prepare the TLS policy from.\n' 'deploy/openssl/legacy-mail-server.cnf.example' >&2
  printf 'Pass --no-legacy-tls to run under the platform default instead.\n' >&2
  exit 1
fi

cd "$compose_directory"

for required_command in openssl curl; do
  if ! command -v "$required_command" > /dev/null 2>&1; then
    printf 'This script needs %s and it is not on the PATH.\n' "$required_command" >&2
    exit 1
  fi
done

if [[ "$start_stack" == 'yes' ]] && ! docker compose version > /dev/null 2>&1; then
  printf 'This script needs Docker Compose v2 (`docker compose`) to start the deployment.\n' >&2
  printf 'Install it, or pass --no-start to write the files and start the stack yourself.\n' >&2
  exit 1
fi

# Every question is answered before anything is written, so a refusal here leaves the directory as it was found. An
# existing file is a deployment somebody already prepared, and replacing its credentials silently is the one failure
# this script must not have.
refuse_existing() {
  local existing_path="$1"

  if [[ -e "$existing_path" ]]; then
    printf 'deploy/compose/%s already exists, so this checkout already carries a prepared deployment.\n' \
      "$existing_path" >&2
    printf 'Remove it deliberately, or read %s/operations/deployment-compose.html to change it in place.\n' \
      "$documentation_base" >&2
    exit 1
  fi
}

refuse_existing '.env'
refuse_existing 'config/10-mailfathom.json'
refuse_existing 'secrets/postgres-superuser-password'
refuse_existing 'secrets/mailfathom-database-password'

if [[ "$interactive" == 'yes' && ! -r /dev/tty ]]; then
  printf 'There is no terminal to ask on. Pass --non-interactive with every answer, and --password-file for\n' >&2
  printf 'the credential: scripts/quick-start-compose.sh --help\n' >&2
  exit 1
fi

ask() {
  local prompt="$1"
  local default_value="${2:-}"
  local answer=''

  if [[ "$interactive" != 'yes' ]]; then
    printf '%s\n' "$default_value"
    return 0
  fi

  if [[ -n "$default_value" ]]; then
    read -r -p "$prompt [$default_value]: " answer < /dev/tty
  else
    read -r -p "$prompt: " answer < /dev/tty
  fi

  printf '%s\n' "${answer:-$default_value}"
}

confirm() {
  local prompt="$1"
  local answer=''

  [[ "$interactive" == 'yes' ]] || return 1

  read -r -p "$prompt [y/N]: " answer < /dev/tty
  [[ "$answer" == 'y' || "$answer" == 'Y' ]]
}

require_answer() {
  local value="$1"
  local what="$2"
  local argument="$3"

  if [[ -z "$value" ]]; then
    printf 'No %s was given. Pass %s, or run without --non-interactive to be asked for it.\n' \
      "$what" "$argument" >&2
    exit 1
  fi
}

# The addresses come from docs/users/mailbox-providers.md, which is where they are reviewed and dated. Three services
# are refused rather than configured: a mailbox that accepts no password cannot be prepared by a script that asks for
# one, and a configuration written anyway would fail at the first synchronization with an authentication error that
# says nothing about why.
resolve_provider() {
  case "$provider" in
    gmail) imap_host='imap.gmail.com'; imap_port="${imap_port:-993}" ;;
    fastmail) imap_host='imap.fastmail.com'; imap_port="${imap_port:-993}" ;;
    icloud) imap_host='imap.mail.me.com'; imap_port="${imap_port:-993}" ;;
    yahoo) imap_host='imap.mail.yahoo.com'; imap_port="${imap_port:-993}" ;;
    zoho) imap_host='imap.zoho.com'; imap_port="${imap_port:-993}" ;;
    custom)
      require_answer "$imap_host" 'IMAP host' '--host'
      imap_port="${imap_port:-993}"
      ;;
    google-workspace | outlook | exchange | microsoft365)
      local service_name
      case "$provider" in
        google-workspace) service_name='A Google Workspace' ;;
        outlook) service_name='An Outlook.com' ;;
        exchange) service_name='An Exchange Online' ;;
        microsoft365) service_name='A Microsoft 365' ;;
      esac

      printf '%s mailbox accepts no password, so this script cannot prepare it.\n' "$service_name" >&2
      printf 'It authenticates with an OAuth refresh token, obtained first: %s/operations/mailbox-oauth.html\n' \
        "$documentation_base" >&2
      exit 1
      ;;
    proton)
      printf 'Proton Mail is reached through the local bridge, which listens on the host rather than in a\n' >&2
      printf 'container and does not speak TLS on connect, so it needs a transport and a network this script\n' >&2
      printf 'does not write. Configure it by hand: %s/users/mailbox-providers.html\n' "$documentation_base" >&2
      exit 1
      ;;
    *)
      printf 'Unknown provider: %s\n' "$provider" >&2
      printf 'Choose gmail, fastmail, icloud, yahoo, zoho, or custom. Every service and what it accepts is at\n' >&2
      printf '%s/users/mailbox-providers.html\n' "$documentation_base" >&2
      exit 1
      ;;
  esac
}

if [[ "$interactive" == 'yes' ]]; then
  cat << TEXT

MailFathom quick start, for the Docker Compose deployment.

This is the fastest way to try MailFathom, and it is not the recommended way to run one. What it
prepares serves this machine over plain HTTP, keeps its credentials in files under this checkout, and
backs nothing up — enough to find out what the product does, and less than a deployment anybody
depends on should have. What that costs, and where to go instead, is printed at the end.

It prepares deploy/compose/ in this checkout: the credentials, the configuration, and the database.
It writes nothing until every question below is answered, and nothing outside that directory ever.

Where your mailbox lives decides its address and whether a password is accepted at all —
$documentation_base/users/mailbox-providers.html has every service.

TEXT

  [[ -n "$provider" ]] || provider="$(ask 'Provider (gmail, fastmail, icloud, yahoo, zoho, custom)' 'custom')"
fi

require_answer "$provider" 'provider' '--provider'

if [[ "$provider" == 'custom' && -z "$imap_host" && "$interactive" == 'yes' ]]; then
  imap_host="$(ask 'IMAP host')"
  imap_port="$(ask 'IMAP port' '993')"
fi

resolve_provider

if [[ ! "$imap_port" =~ ^[0-9]+$ ]] || ((imap_port < 1 || imap_port > 65535)); then
  printf 'Not a port number: %s\n' "$imap_port" >&2
  exit 1
fi

if [[ "$interactive" == 'yes' ]]; then
  [[ -n "$user_name" ]] || user_name="$(ask 'Mailbox user name, usually the address')"
  [[ -n "$display_name" ]] || display_name="$(ask 'What an assistant should call this mailbox' 'Personal mail')"
fi

require_answer "$user_name" 'mailbox user name' '--user-name'
require_answer "$display_name" 'display name' '--display-name'

# `AccountId` and every alias are names the operator chooses and every tool argument, log line, and error message then
# uses, so the characters are held to what reads back unambiguously in all three.
if [[ ! "$account_id" =~ ^[A-Za-z0-9._-]+$ ]]; then
  printf 'An account identifier holds letters, digits, dots, hyphens, and underscores: %s\n' "$account_id" >&2
  exit 1
fi

case "$mcp_authentication" in
  api-key | none) ;;
  *) printf 'The MCP endpoint takes api-key or none, not %s.\n' "$mcp_authentication" >&2; exit 1 ;;
esac

case "$admin_endpoint" in
  off | api-key | none) ;;
  *) printf 'The administrative endpoint takes off, api-key, or none, not %s.\n' "$admin_endpoint" >&2; exit 1 ;;
esac

if [[ "$interactive" == 'yes' ]]; then
  cat << TEXT

The MCP endpoint is what a chat client connects to. It is served over plain HTTP on 127.0.0.1 either
way — MailFathom terminates no TLS of its own — so what this decides is whether a client also has to
present a key.

  api-key  The endpoint accepts a key the running deployment mints for the owner, and the client
           sends it as a bearer credential. Minting it is one mfctl command against the
           administrative endpoint, which this script then turns on for you. Two popular chat
           clients offer no field for a key, and cannot connect to an endpoint configured this way.
  none     Anything that can reach 127.0.0.1:8080 reads your mail. Legal, announced with a startup
           warning, and reasonable only because the port is published on loopback alone.

TEXT

  if [[ "$mcp_authentication" == 'api-key' ]] && confirm 'Serve the MCP endpoint without authentication?'; then
    mcp_authentication='none'
  fi
fi

# The MCP key is not written into a file here: it is a record the running deployment mints over the administrative
# endpoint. So switching that endpoint off while a key is asked for produces a deployment nothing can be provisioned
# for. An operator who asked for that combination is told; a default that arrived at it is corrected, since nobody
# chose it.
if [[ "$admin_endpoint" == 'off' && "$mcp_authentication" == 'api-key' ]]; then
  if [[ "$admin_endpoint_chosen" == 'yes' ]]; then
    printf 'An MCP key is minted with mfctl over the administrative endpoint, so\n' >&2
    printf '%s\n' '--admin-endpoint off cannot be combined with it. Pass --admin-endpoint api-key, or ask for no' >&2
    printf '%s\n' 'key at all with --mcp-authentication none.' >&2
    exit 1
  fi

  admin_endpoint='api-key'
fi

if [[ "$interactive" == 'yes' ]]; then
  cat << TEXT

The administrative endpoint is what mfctl talks to: synchronization state, what was changed, what a
question read, the credentials an owner's clients present, and the operations that erase a folder. It
is served on its own port ($admin_port), also on 127.0.0.1 and also over plain HTTP.

An entry that writes no grant reaches every administrative operation, so a key here is as sensitive
as the mail it can dispose of.

TEXT

  # An authenticated MCP endpoint has nowhere else to get its key from: it is a record beside the owner, minted with
  # `mfctl credential create`, so the endpoint mfctl talks to is a prerequisite rather than an extra. The validation
  # above has already turned it on; this is where the operator is told why.
  if [[ "$mcp_authentication" == 'api-key' ]]; then
    printf 'It is on, because the MCP key you chose above is minted through it.\n\n' >&2
  fi

  if [[ "$admin_endpoint" == 'off' ]] && confirm 'Serve the administrative endpoint as well?'; then
    admin_endpoint='api-key'
  fi

  if [[ "$admin_endpoint" == 'api-key' ]] \
    && confirm 'Without authentication? Anything that reaches the port can then administer the service'; then
    admin_endpoint='none'
  fi
fi

mailbox_password=''
if [[ -n "$password_file" ]]; then
  if [[ ! -r "$password_file" ]]; then
    printf 'No readable file at %s.\n' "$password_file" >&2
    exit 1
  fi

  # One trailing newline is stripped, because a file written by an editor almost always ends with one and an untrimmed
  # byte becomes part of the password. This is the rule MailFathom's own secret resolution applies, and the sentinel is
  # what keeps it to exactly one.
  mailbox_password="$(cat -- "$password_file"; printf 'x')"
  mailbox_password="${mailbox_password%x}"
  mailbox_password="${mailbox_password%$'\n'}"
elif [[ "$interactive" == 'yes' ]]; then
  printf '\nThe password or app password for %s.\n' "$user_name" >&2
  printf 'It is not echoed, and it is written straight to a file rather than to a command line.\n' >&2
  read -r -s -p 'Mailbox password: ' mailbox_password < /dev/tty
  printf '\n' >&2
else
  printf 'No mailbox password. Pass --password-file, or run without --non-interactive to be asked for it.\n' >&2
  exit 1
fi

if [[ -z "$mailbox_password" ]]; then
  printf 'The mailbox password is empty.\n' >&2
  exit 1
fi

# The redirect `/releases/latest` serves is what names the newest release, rather than the REST API, which answers the
# same question and rate-limits an unauthenticated caller by IP address while doing it.
if [[ -z "$requested_version" ]]; then
  if ! latest_url="$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$release_base/latest")"; then
    printf 'Could not ask %s which release is newest. Pass --version to deploy a particular one.\n' \
      "$release_base/latest" >&2
    exit 1
  fi

  requested_version="${latest_url##*/tag/}"
fi

version="${requested_version#v}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  printf 'Not a release version: %s. It looks like 0.6.0, and %s lists the ones there are.\n' \
    "$requested_version" "$release_base" >&2
  exit 1
fi

# json_string below runs inside a command substitution, and an exit there is the substitution's rather than the script's,
# so a value carrying a control character would reach the file as broken JSON instead of stopping the run. The check
# therefore happens here, where it stops it.
for written_value in "$account_id" "$display_name" "$imap_host" "$user_name"; do
  if [[ "$written_value" == *[$'\x01'-$'\x1f']* ]]; then
    printf 'A control character cannot be part of a value written into the configuration: %s\n' "$written_value" >&2
    exit 1
  fi
done

readonly imap_password_name="imap-$account_id-password"
readonly admin_key_name='admin-workstation-key'
readonly schema_asset="mailfathom-schema-$version.sql"

# compose.yaml publishes no port for the administrative endpoint and mounts no file that need not exist, and it is a
# tracked file. An override is what states either without editing it — .gitignore already covers this path.
writes_override='no'
if [[ "$admin_endpoint" != 'off' || "$relax_tls_policy" == 'yes' ]]; then
  writes_override='yes'
fi
readonly writes_override

# Written into JSON as a string. Only these two characters can end a string early or start an escape, and a control
# character is refused rather than encoded, because a name carrying one is a mistake in every case that reaches here.
json_string() {
  local value="$1"

  if [[ "$value" == *[$'\x01'-$'\x1f']* ]]; then
    printf 'A control character cannot be part of %s.\n' "$value" >&2
    exit 1
  fi

  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '"%s"' "$value"
}

write_secret() {
  local secret_path="$1"
  local material="$2"

  printf '%s' "$material" > "$secret_path"
  chmod 444 "$secret_path"
}

refuse_existing "secrets/mailfathom/$imap_password_name"
[[ "$admin_endpoint" != 'api-key' ]] || refuse_existing "secrets/mailfathom/$admin_key_name"
[[ "$relax_tls_policy" != 'yes' ]] || refuse_existing 'openssl-legacy.cnf'
[[ "$writes_override" != 'yes' ]] || refuse_existing 'compose.override.yaml'

printf '\nPreparing deploy/compose for MailFathom %s.\n' "$version" >&2

mkdir -p secrets/mailfathom config

# What is mounted has to be reachable by the container's own account; what is not mounted is what restricts access.
# MailFathom runs as an unprivileged account that is nobody's host user and holds no capability to override a mode
# with, and Compose bind-mounts with the host's own permissions rather than applying `mode`. Git records no directory
# mode either, so config/ arrives with whatever umask the clone ran under.
chmod 700 secrets
chmod 711 secrets/mailfathom
chmod 755 config

write_secret 'secrets/postgres-superuser-password' "$(openssl rand -base64 33 | tr -d '\n')"
write_secret 'secrets/mailfathom-database-password' "$(openssl rand -base64 33 | tr -d '\n')"
write_secret "secrets/mailfathom/$imap_password_name" "$mailbox_password"

# The list names the methods this endpoint accepts and holds no credential: what a client presents resolves a record
# beside the owner whose mail it reaches, and that record is minted over the administrative endpoint below.
mcp_authentication_block='[]'
if [[ "$mcp_authentication" == 'api-key' ]]; then
  mcp_authentication_block=$(
    cat << 'JSON'
[
      { "Method": "api-key" }
    ]
JSON
  )
fi

# The client surface is deliberately left out of what this writes. Nothing calls it yet — the Uno Platform client was
# withdrawn and the React one has not shipped — so serving it would be a mail-reading endpoint published for nobody.
# docs/operations/client-endpoint.md is what turns it on by hand once something does.

admin_section=''
if [[ "$admin_endpoint" != 'off' ]]; then
  admin_authentication_block='[]'

  if [[ "$admin_endpoint" == 'api-key' ]]; then
    write_secret "secrets/mailfathom/$admin_key_name" "$(openssl rand -base64 33 | tr -d '\n')"
    admin_authentication_block=$(
      cat << JSON
[
      {
        "ApiKey": {
          "Name": "workstation",
          "SecretReference": "file:/etc/mailfathom/secrets/$admin_key_name",
          "Lifetime": "NoLimit"
        }
      }
    ]
JSON
    )
  fi

  # The port is stated here and published in compose.override.yaml below. The bind address is left at its default,
  # which is every address inside the container — that is what makes the published mapping reach it, and the mapping is
  # what restricts the endpoint to this machine.
  admin_section=$(
    cat << JSON
  "AdminEndpoint": {
    "Enabled": true,
    "Port": $admin_port,
    "Authentication": $admin_authentication_block
  },
JSON
  )
fi

cat > config/10-mailfathom.json << JSON
// Written by scripts/quick-start-compose.sh. It is ordinary configuration from here on: edit it, review it as a diff,
// and restart the deployment to apply a change. See $documentation_base/operations/configuration-reference.html
//
// Nothing here is a secret. Every credential is a reference to a file under ./secrets/mailfathom/, which compose.yaml
// mounts read-only at /etc/mailfathom/secrets.
{
  "MailSynchronization": {
    "Enabled": true,
    "Interval": "00:05:00",
    "Accounts": [
      {
        "AccountId": $(json_string "$account_id"),
        "DisplayName": $(json_string "$display_name"),
        "Host": $(json_string "$imap_host"),
        "Port": $imap_port,
        "UserName": $(json_string "$user_name"),
        "Secrets": {
          "Password": {
            "Name": $(json_string "$imap_password_name"),
            "SecretReference": $(json_string "file:/etc/mailfathom/secrets/$imap_password_name")
          }
        },
        "TransportSecurity": {
          "ConnectionSecurity": "TlsOnConnect"
        },
        "Folders": [
          { "Alias": "inbox", "SpecialUse": "Inbox" },
          { "Alias": "sent", "SpecialUse": "Sent" }
        ]
      }
    ]
  },
$admin_section
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": $mcp_authentication_block,
    "Cors": {
      "AllowedOrigins": []
    }
  }
}
JSON

chmod 644 config/10-mailfathom.json

cat > .env << ENV
# Written by scripts/quick-start-compose.sh. .env.example documents every other value this file can carry.
#
# The image is pinned to a release rather than tracking a moving tag: an immutable tag is what makes a deployment
# reproducible and an upgrade a decision. The pull policy below pairs with it, so Compose pulls this image instead of
# building the checkout — the two are one decision, which is why they move together.
MAILFATHOM_IMAGE=ghcr.io/krzysztof318/mailfathom:$version
MAILFATHOM_PULL_POLICY=missing
ENV

chmod 644 .env

# Copied rather than written, so the file this deployment runs under is the one the repository reviews and documents.
if [[ "$relax_tls_policy" == 'yes' ]]; then
  cp -- "$legacy_tls_source" openssl-legacy.cnf
  chmod 644 openssl-legacy.cnf
fi

if [[ "$writes_override" == 'yes' ]]; then
  {
    cat << 'YAML'
# Written by scripts/quick-start-compose.sh, and ignored by Git.
#
# What compose.yaml leaves out on purpose, because it is tracked and this is not: a published port for an
# administrative endpoint it keeps disabled, and a mount for a file that need not exist.
services:
  mailfathom:
YAML

    # The whole process reads its TLS parameters from this file, which is what reaches a mail server offering only
    # parameters the platform's own policy refuses — a 1024-bit Diffie-Hellman group, a 1024-bit RSA key, a SHA-1
    # signature. It relaxes nothing else: the protocol floor stays where it was, and certificate validation is
    # untouched. It applies to every TLS session this process makes, the database's included, which is why the closing
    # report names it and --no-legacy-tls leaves the platform default in force.
    if [[ "$relax_tls_policy" == 'yes' ]]; then
      cat << 'YAML'
    environment:
      OPENSSL_CONF: /etc/mailfathom/openssl-legacy.cnf
    volumes:
      - type: bind
        source: ./openssl-legacy.cnf
        target: /etc/mailfathom/openssl-legacy.cnf
        read_only: true
YAML
    fi

    # Loopback, like every other port this deployment publishes: mfctl reaches it from this machine, and nothing else
    # reaches it at all.
    if [[ "$admin_endpoint" != 'off' ]]; then
      cat << YAML
    ports:
      - "127.0.0.1:$admin_port:$admin_port"
YAML
    fi
  } > compose.override.yaml

  chmod 644 compose.override.yaml
fi

printf 'Wrote .env, config/10-mailfathom.json, and the credentials under secrets/.\n' >&2

if [[ "$relax_tls_policy" == 'yes' ]]; then
  printf 'Wrote openssl-legacy.cnf, which this deployment reads its TLS parameters from.\n' >&2
fi

report_connection() {
  printf '\nThe MCP endpoint answers at http://127.0.0.1:%s/mcp, over the Streamable HTTP transport.\n' \
    "$published_port" >&2

  if [[ "$mcp_authentication" == 'api-key' ]]; then
    printf 'It accepts a key, and the deployment mints one for the owner once it is running:\n\n' >&2
    printf '  mfctl credential create --method api-key\n\n' >&2
    printf 'That prints the key once and keeps only a digest of it. Give the client an Authorization\n' >&2
    printf 'header of `Bearer <key>` with what it printed.\n' >&2
  else
    printf 'It requires no credential, so a client needs the address alone.\n' >&2
  fi

  printf '\nWhich clients take which of those, by name: %s/users/mcp-clients.html\n' "$documentation_base" >&2

  printf '\nThere is no MailFathom client to open: the Uno Platform one was withdrawn and the React one has\n' >&2
  printf 'not shipped, so this deployment answers an assistant and nothing else.\n' >&2

  if [[ "$admin_endpoint" != 'off' ]]; then
    printf '\nThe administrative endpoint answers at http://127.0.0.1:%s/api/admin.\n' "$admin_port" >&2
    printf '  mfctl login --endpoint http://127.0.0.1:%s\n' "$admin_port" >&2

    if [[ "$admin_endpoint" == 'api-key' ]]; then
      printf '  cat %s/secrets/mailfathom/%s\n' "$compose_directory" "$admin_key_name" >&2
    fi

    printf 'Getting the command: %s/operations/admin-endpoint.html\n' "$documentation_base" >&2
  fi
}

# Said at the end rather than only at the start, because this is the point at which somebody has a working deployment
# and stops reading. Each line is a decision this script deliberately did not make, not a defect in what it produced.
report_what_an_evaluation_is_missing() {
  cat << TEXT >&2

This is a deployment to evaluate MailFathom with. Before it is one anybody depends on:

  Transport   Both ports are published on 127.0.0.1 and MailFathom terminates no TLS, so a client on
              another machine has nothing to connect to and nothing protecting it if it did. Every
              credential presented crosses that hop readable, which the host says at every startup.
              Put a TLS-terminating proxy in front, or configure McpEndpoint:Https.
              $documentation_base/operations/mcp-endpoint.html
TEXT

  if [[ "$relax_tls_policy" == 'yes' ]]; then
    cat << TEXT >&2
  TLS policy  This deployment reads its TLS parameters from ./openssl-legacy.cnf, which lowers the
              platform's security level to 1 — a 1024-bit Diffie-Hellman group, a 1024-bit RSA key,
              and a SHA-1 signature become acceptable — so that a mail server offering only those can
              be reached at all. Every TLS session the process makes is covered, the database's
              included, and nothing here needed it unless your mailbox did. Delete the file and the
              lines naming it in compose.override.yaml, or prepare with --no-legacy-tls.
              $documentation_base/operations/platform-tls-policy.html
TEXT
  fi

  cat << TEXT >&2
  Credentials They are files under this checkout, protected by the mode on secrets/ and nothing else.
              The Podman Quadlet shape encrypts them as systemd credentials bound to the machine, and
              Kubernetes mounts a Secret you manage.
              $documentation_base/operations/secret-provisioning.html
  Grants      Every credential this script configures reaches its whole surface, and so does one minted
              with no --permission. A deployment with more than one client narrows each one.
              $documentation_base/operations/permissions.html
  Backups     Nothing here backs the database up, and the mail in it costs a full resynchronization
              to rebuild — the audit trail and the embeddings do not come back at all.
              $documentation_base/operations/database-schema.html

Which shape a real installation takes is a decision this script did not make for you:
$documentation_base/users/installation.html
TEXT
}

if [[ "$start_stack" != 'yes' ]]; then
  printf '\nNothing was started. From %s:\n\n' "$compose_directory" >&2
  printf '  docker compose up -d postgres\n' >&2
  printf '  # apply %s — %s/operations/deployment-compose.html\n' "$schema_asset" "$documentation_base" >&2
  printf '  docker compose up -d\n' >&2
  report_connection
  report_what_an_evaluation_is_missing
  exit 0
fi

printf '\nStarting PostgreSQL.\n' >&2
docker compose up -d postgres

printf 'Waiting for it to report healthy.\n' >&2
for _ in $(seq 1 60); do
  if [[ "$(docker compose ps --format '{{.Health}}' postgres 2> /dev/null)" == 'healthy' ]]; then
    break
  fi

  sleep 2
done

if [[ "$(docker compose ps --format '{{.Health}}' postgres 2> /dev/null)" != 'healthy' ]]; then
  printf 'PostgreSQL did not become healthy. `docker compose logs postgres` says why.\n' >&2
  exit 1
fi

# Run inside the container, where both credentials are already mounted and the socket is local, so no password reaches
# a command line and no port has to be published to ask this.
psql_in_container() {
  docker compose exec --no-TTY postgres sh -c \
    'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
       --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on '"$1"
}

applied_migrations="$(psql_in_container "--tuples-only --no-align --command \
  \"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'\"" | tr -d '[:space:]')"

if [[ "$applied_migrations" != '0' ]]; then
  # Not an empty database, so this is an upgrade rather than a first installation. An upgrade takes a backup first and
  # is never something a convenience script decides to perform.
  printf '\nThis database already carries migrations, so the schema step is an upgrade and yours to take.\n' >&2
  printf 'Back the database up, read the SQL, and apply it: %s/operations/database-schema.html\n' \
    "$documentation_base" >&2
  exit 1
fi

printf '\nThe database is empty, so it needs the release schema before MailFathom will serve.\n' >&2
printf 'MailFathom never applies a migration while starting: it verifies the schema and refuses one it\n' >&2
printf 'does not recognize, which is why this is a step rather than something that happens on its own.\n\n' >&2
printf '  %s/download/v%s/%s\n\n' "$release_base" "$version" "$schema_asset" >&2

if ! confirm "Download that file, verify it against its checksum, and apply it to this empty database?"; then
  printf '\nNothing was applied. Download it, read it, and apply it from deploy/compose:\n\n' >&2
  printf '  curl -LO %s/download/v%s/%s\n' "$release_base" "$version" "$schema_asset" >&2
  printf "  docker compose exec --no-TTY postgres sh -c \\\\\n" >&2
  printf "    'PGPASSWORD=\"\$(cat /run/secrets/mailfathom-database-password)\" exec psql \\\\\n" >&2
  printf "       --username \"\$MAILFATHOM_DATABASE_ROLE\" --dbname \"\$MAILFATHOM_DATABASE\" --set ON_ERROR_STOP=on' \\\\\n" >&2
  printf "    < %s\n" "$schema_asset" >&2
  printf '\nThen: docker compose up -d\n' >&2
  report_connection
  report_what_an_evaluation_is_missing
  exit 0
fi

work_directory="$(mktemp --directory)"
trap 'rm --recursive --force "$work_directory"' EXIT

for asset in "$schema_asset" "$schema_asset.sha256"; do
  if ! curl -fsSL --output "$work_directory/$asset" "$release_base/download/v$version/$asset"; then
    printf 'Could not download %s. Check that %s is a release that exists: %s\n' "$asset" "$version" "$release_base" >&2
    exit 1
  fi
done

# The checksum file names its file as the release built it, so the check runs from the directory it downloaded into.
if ! (cd "$work_directory" && sha256sum --check --ignore-missing --quiet "$schema_asset.sha256"); then
  printf '\n%s does not match the checksum %s publishes for it, so nothing was applied.\n' \
    "$schema_asset" "$version" >&2
  exit 1
fi

printf 'Applying the schema as the role MailFathom connects as.\n' >&2

# As `mailfathom`, never as `postgres`: PostgreSQL makes the role that ran the DDL the owner of what it created, and a
# schema applied by the superuser leaves MailFathom refusing to start against a schema that plainly exists.
psql_in_container '' < "$work_directory/$schema_asset" > /dev/null

printf 'Starting MailFathom.\n' >&2
docker compose up -d

printf 'Waiting for the probes.\n' >&2
started='no'
for _ in $(seq 1 60); do
  if curl -fsS 'http://127.0.0.1:8081/started' > /dev/null 2>&1; then
    started='yes'
    break
  fi

  sleep 2
done

if [[ "$started" != 'yes' ]]; then
  printf '\n/started never answered. The refusal names the setting that caused it:\n\n' >&2
  printf '  docker compose logs mailfathom\n' >&2
  exit 1
fi

if curl -fsS 'http://127.0.0.1:8081/health' > /dev/null 2>&1; then
  printf '\nMailFathom %s is running and ready.\n' "$version" >&2
else
  printf '\nMailFathom %s started, and /health is not ready yet — the first synchronization is running.\n' "$version" >&2
fi

report_connection

printf '\nThe first synchronization takes a while on a large mailbox. Every tool result carries how fresh\n' >&2
printf 'the local copy is: %s/users/usage.html\n' "$documentation_base" >&2

report_what_an_evaluation_is_missing
