# Deploying with Docker Compose

<!-- describes: deploy/compose/**, scripts/quick-start-compose.sh -->

`deploy/compose/` is the supported Compose deployment: MailFathom and PostgreSQL, with the schema applied as an explicit
operator action that nothing in the deployment performs. It is the shape to use for self-hosting on one machine.

Everything below assumes `deploy/compose/` as the working directory.

**Every command here is `docker compose`, and Docker is what this deployment is verified on.** Nothing in
`compose.yaml` is Docker-specific and the same file runs under `podman compose`, with two rootless-Podman facts worth
knowing before you start, because neither announces itself:

- The account inside the container maps to a subordinate uid rather than to yours, so the file modes below are what
  makes the deployment start rather than a hardening preference. They are the modes Docker needs as well and for the
  same reason — the account is nobody's host user in either runtime — and getting them right is also what keeps a
  `--userns` mapping out of it, which matters because `podman-compose` puts a project's services in one pod and
  `--userns` cannot be combined with a pod.
- Rootless Podman publishes no port below 1024 until `net.ipv4.ip_unprivileged_port_start` allows it. This deployment
  publishes 8080 and 8081, so what meets that limit is a reverse proxy terminating TLS on 443 in front of MailFathom
  rather than MailFathom itself.

## Trying it first, with one command

`scripts/quick-start-compose.sh` performs everything on this page that is typed rather than decided: it asks where the
mailbox lives, generates the credentials, writes the configuration, sets the modes, starts the stack, offers the schema
step, and reports the two probes.

```bash
scripts/quick-start-compose.sh
```

**It ends with an address for a chat client and nothing for a browser.** No image carries MailFathom's own client
today — the Uno Platform one was withdrawn and the React one has not landed — so the script prepares no client
credential and writes no client switch, and says so in its closing report.

The administrative endpoint is still served whether or not you would otherwise have asked for it, because the MCP key
is minted over it and there is no other way to reach one. `--admin-endpoint off` together with `--mcp-authentication
api-key` is refused naming the conflict rather than quietly overridden.

**It also relaxes the platform's TLS policy for this deployment**, by copying
[`deploy/openssl/legacy-mail-server.cnf.example`](https://github.com/Krzysztof318/MailFathom/blob/main/deploy/openssl/legacy-mail-server.cnf.example)
to `openssl-legacy.cnf` beside `compose.yaml` and naming it in `OPENSSL_CONF` through the same
`compose.override.yaml` the administrative port is published from. That is what makes a mailbox on a server offering
only a 1024-bit group, a 1024-bit key, or a SHA-1 signature reachable from a first run instead of failing it with a
handshake error that names nothing. It covers every TLS session the process makes, the database's included, it is
listed in the closing report, and `--no-legacy-tls` prepares the same deployment under the platform default —
[the platform TLS policy](platform-tls-policy.md) is the page.

**It prepares a deployment to evaluate MailFathom with, and that is not the recommended way to run one.** What it
produces serves this machine over plain HTTP, keeps its credentials in files under the checkout, narrows no grant, and
backs nothing up — enough to find out what the product does, and less than a deployment anybody depends on should have.
It prints that list when it finishes, and [installing MailFathom](../users/installation.md) is where the shape of a real
installation is chosen. The rest of this page is that path, and it stays the one this deployment is documented by: the
script writes the same files with the same values, so a deployment it prepared is read, changed, and upgraded exactly as
one prepared by hand.

Four things it will not decide for you, because each is a decision rather than a step:

- **It publishes nothing beyond loopback**, and offers no answer that would.
- **It configures no chat or embedding model**, so `ask_mail` is absent from what it prepares.
- **It applies the schema only to an empty database**, after asking, and against a database that already carries
  migrations it stops and hands the step back — that is an upgrade, and an upgrade takes a backup first.
- **It overwrites nothing.** An existing `.env`, configuration file, or secret stops the run naming the file, so it
  cannot replace the credentials of a deployment already prepared here.

**The MCP endpoint is the answer it asks for**, and it is the one with a cost worth stating before it is given. The
endpoint accepts an API key unless you ask for none, which is legal, announced with a startup warning, and the only
shape the chat clients with no field for a static header can connect to. The key itself is not written here: what a
client presents to that endpoint is a record beside the owner whose mail it reaches, minted with
`mfctl credential create` once the deployment is running, and the script prints that command when it finishes.

The administrative endpoint comes with what that credential needs rather than from an answer. Minting the MCP key is
an administrative operation and has no other way to reach one. So a default run serves that endpoint on port 8090
through a generated `compose.override.yaml`, with a generated key of its own; its own default port is 8080, which is
the socket the MCP endpoint is already served on, and `compose.yaml` publishes nothing for it. Only a run that asked
for no key at all — `--mcp-authentication none` — leaves it off, and that run is asked whether to serve it and whether
to serve it without a key.

**Every credential here crosses an unencrypted hop.** On a port published to `127.0.0.1` that hop is this machine.
MailFathom reports it at every startup, naming the surface and the port, and does not refuse it: this process reads the
scheme of its own socket and nothing beyond it, so it cannot tell this deployment from one exposed to a network. Moving
`MAILFATHOM_HTTP_BIND` off loopback without putting TLS in front is what makes the warning matter.

`--non-interactive` takes every answer as an argument instead, with `--password-file` for the mailbox credential, and
`--no-start` writes the files and stops. Both end before the schema — an unattended run declines that question the same
way it declines every other — so what either leaves is a prepared deployment nothing has been told about yet.
`scripts/quick-start-compose.sh --help` lists all of them.

## Before the first start

Three files have to exist. Two are database credentials the Compose file mounts as secrets; the third is the
configuration MailFathom reads.

```bash
cd deploy/compose

cp .env.example .env

mkdir -p secrets/mailfathom
chmod 700 secrets
chmod 711 secrets/mailfathom
chmod 755 config

openssl rand -base64 33 | tr -d '\n' > secrets/postgres-superuser-password
openssl rand -base64 33 | tr -d '\n' > secrets/mailfathom-database-password
chmod 444 secrets/postgres-superuser-password secrets/mailfathom-database-password

cp config/10-mailfathom.json.example config/10-mailfathom.json
$EDITOR config/10-mailfathom.json
chmod 644 config/10-mailfathom.json          # after the editor, which may have rewritten the file under your umask
```

**What is mounted has to be reachable by the container's own account; what is not mounted is what restricts access.**
MailFathom runs as an unprivileged account inside the container that corresponds to no user on the host — uid 1654,
with every capability dropped, so it holds no `DAC_OVERRIDE` to override a mode with. Compose bind-mounts with the
host's own permissions: outside Swarm it ignores `mode`, `uid`, and `gid`. Three consequences follow, and each is a
startup failure when it is missed:

- **`secrets/mailfathom` and `config` are the bind mounts, so that account needs the execute bit on both.** At `0700`
  it has none, and a directory is checked before the files inside it are, so every secret reference under it fails to
  resolve however those files are permissioned. What startup reports is material that could not be found rather than a
  permission error, because MailFathom collapses every file-system failure into one result so that no diagnostic
  quotes the path it was handed — which makes a mode the least visible way to break this deployment. `0711` is what
  the secrets directory takes: traversable by that account, and still not listable by anything but you. The
  configuration directory is *listed* rather than opened by name, because MailFathom layers in every `*.json` it finds
  there, so that one needs read as well, which is the `0755` above. It is set rather than assumed because Git records
  no directory mode: `config/` arrives with whatever `umask` the clone ran under, and a strict one leaves a directory
  the container cannot list.
- **The files inside both are read, so they have to be readable.** `0444` for a secret and `0644` for a configuration
  file. A `0600` or `0400` file presents to MailFathom as a secret reference that cannot be resolved, or crashes
  startup naming the configuration file it could not open, so a `umask` of `077` produces a deployment that will not
  start.
- **`secrets/` itself is mounted nowhere**, so its `0700` is the whole of the access control: only root and you reach
  through it at all, and that holds for `secrets/mailfathom/` underneath it whatever the mount needs. Loosening the
  mounted directory by one bit therefore costs nothing on the host.

Every file you add under `secrets/mailfathom/` needs the same `0444`.

`.env`, `secrets/`, and `config/*.json` are all ignored by Git. The two `.example` files are tracked and contain
placeholders only.

The extension of the configuration file matters. MailFathom layers in `*.json` and nothing else, so
`10-mailfathom.json.example` sits in the mounted directory without being read — and so does a `.bak` left behind after an
edit. Files are layered in file-name order, so `20-…` overrides `10-…`. See
[configuration sources](configuration-sources.md) for the full precedence.

### Credentials

Every credential MailFathom reads is a **reference** to a file, never a value in the configuration. Drop the material
into `secrets/mailfathom/`, which is mounted read-only at `/etc/mailfathom/secrets`, and name it from the configuration:

```bash
printf '%s' 'the-mailbox-password' > secrets/mailfathom/imap-primary-password
openssl rand -base64 32 | tr -d '\n'  > secrets/mailfathom/mailfathom-data-key   # only for an OAuth mailbox
chmod 444 secrets/mailfathom/*
```

```json
{
  "Secrets": {
    "Password": {
      "Name": "imap-primary-password",
      "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password"
    }
  }
}
```

That is the same path the Helm chart mounts its Secret at, so a `SecretReference` written for one deployment reads
correctly in the other. [Secret provisioning](secret-provisioning.md) is the full contract, including what a leaked
configuration file does and does not expose.

The two database credentials are Compose secrets rather than files in that directory, so the database superuser
password is never on a path the service can read.

**The encrypted systemd credentials the native installation uses do not reach this deployment**, and running it under
Podman does not change that: Compose starts no per-service systemd unit under either engine, so a `systemd-credential:`
reference resolves to nothing here. These files are protected by the host they sit on and by the `0700` on `secrets/`
above, which the page already names as the whole of the access control.
[What an encrypted credential is bound to](secret-provisioning.md#what-an-encrypted-credential-is-bound-to) states the
binding, and [Docker or Podman Compose](secret-provisioning.md#docker-or-podman-compose) why a container started this
way is on the far side of it.

What does reach the path is [the Podman Quadlet deployment](deployment-quadlet.md), where a `.container` file is a
systemd unit source and the credentials arrive as they do for a native service. It is an alternative to this file
rather than a successor to it — nothing here changes, and that page states what it asks of the host in return.

**The data-encryption key is `-base64 32`, not the `-base64 33` on every other line here.** It is the one credential
generated to a length rather than to a strength: the material has to decode to exactly 32 bytes, and startup refuses
anything else naming the setting instead of accepting a weaker key. It is needed only when an account authenticates
with OAuth, because the refresh token its authorization server rotates is the one value MailFathom seals today; a
deployment whose mailboxes all use a password needs no key and starts without one. `10-mailfathom.json.example` carries
the `DataEncryption` block commented out for exactly that reason — uncomment it when you configure an OAuth account.

Generate it once and **back it up with the database, not beside it**. The key is not in the database, nothing in
MailFathom regenerates it in any channel, and a database restored without it restores no sealed value — the failure
appears at the next read rather than at the moment of loss.
[The data-encryption key](secret-provisioning.md#the-data-encryption-key) covers rotation and what the ring is for.

## Starting

```bash
docker compose up -d postgres                              # creates the role, the database, and the vector extension
# apply the schema — see below
docker compose up -d                                       # starts MailFathom
```

The middle step is separate on purpose, and nothing in this deployment performs it. MailFathom never applies a schema
change while starting: it verifies the schema and refuses to serve against one it does not recognize. Bringing the
stack up after a version change therefore *tells* you a migration is outstanding rather than silently applying one.
Take a backup before you answer.

```
MailFathom.Application.Persistence.DatabaseSchemaOutOfDateException: The database has not applied 1 migration(s) this
build defines: 20260731132336_Initial.
```

The step is `mailfathom-schema-<version>.sql`, attached to the release you are installing. The database publishes no
port, and both database credentials are already mounted inside its container, so the shortest route is to run psql
there and hand it the script on standard input — which also keeps the credential off a command line:

```bash
docker compose exec --no-TTY postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
     --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'
```

**As `mailfathom`, never as `postgres`.** PostgreSQL makes the role that ran the DDL the owner of everything it
created, and ownership grants nothing to anybody else, so a schema applied by the superuser leaves MailFathom refusing
to start against a schema that plainly exists — `42501: permission denied for table __EFMigrationsHistory`, reported
as a schema of unknown shape. This deployment has one role that both applies the schema and serves requests, which is
exactly what the initialization script's out-of-band `CREATE EXTENSION` buys: the schema artifact's own
`CREATE EXTENSION IF NOT EXISTS vector` then finds the extension already present rather than needing a privilege
`mailfathom` does not have. [The role that applies it](database-schema.md#the-role-that-applies-it) states the other
arrangement and what it costs.

Read the SQL before applying it, and take a backup first. The script is idempotent, so running it against a database
that already carries some of its migrations applies only what is missing. [Applying the database
schema](database-schema.md) states the privileges it needs, the locks it takes, and what each startup failure means.

### What the first `up` of PostgreSQL does

`postgres/10-create-mailfathom-database.sh` runs once, from the image's own initialization hook, on an empty data
directory. It creates the `mailfathom` role and database, and installs the `vector` extension while a superuser is still
the one connected. MailFathom then connects as a role that owns its database and is not a superuser, and a schema step's
`CREATE EXTENSION IF NOT EXISTS vector` finds the extension already present rather than needing a privilege that role
does not have.

A data directory that already exists is never re-initialized, so editing that script changes nothing about a running
deployment.

## Checking it

```bash
docker compose ps                                    # PostgreSQL reports healthy; MailFathom reports running
curl -fsS http://127.0.0.1:8081/started              # has it finished coming up
curl -fsS http://127.0.0.1:8081/health               # readiness, including the database
curl -fsS http://127.0.0.1:8081/alive                # liveness, the process alone
docker compose logs -f mailfathom
```

The probes answer on **8081**, not on the port the MCP endpoint is served on. They carry no credential, so which network
their port is published to is what controls who may ask them; one of those paths asked on 8080 is answered with `404`,
and so is `/mcp` asked on 8081. `MAILFATHOM_HEALTH_BIND` and `MAILFATHOM_HEALTH_PORT` move the published address, and
[the health endpoints](health-endpoints.md) states what each probe consults and how to turn the surface off or serve it
over TLS.

The MailFathom container declares no Docker health check. Its image carries no shell and no HTTP client for one to run
in, so the endpoints above are asked from outside the container instead.

The MCP endpoint answers at `/mcp` and is off until the configuration enables it. Read
[the MCP endpoint](mcp-endpoint.md) before you do; an enabled endpoint must state how it is authenticated, and there is
no default.

## The network boundary

Both ports are published on **loopback** by default. MailFathom speaks plain HTTP and terminates no TLS, so publishing
the application port on another interface exposes synchronized mail without transport protection. Change
`MAILFATHOM_HTTP_BIND` only once a reverse proxy on the `frontend` network is what listens publicly, and give that
proxy the certificate. The public scheme and host survive the hop on their own, because MailFathom reads a forwarded
header from any peer until told otherwise — so tell it which proxy to believe, `ReverseProxy:TrustedProxies` naming the
address or the `frontend` subnet, and everything else on that network stops being able to set those headers.
[Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) is the page, and it
states what the unnamed default gives up.

The probe port is separate and stays loopback unless the machine asking is not this one. It answers without a
credential, so `MAILFATHOM_HEALTH_BIND` is the whole of its access control; a probe path is never served on the
application port, so widening one does not widen the other.

PostgreSQL publishes no port at all. It sits on `backend`, which is declared `internal`, so it is reachable from
MailFathom and from whatever else you attach to that network — a schema step or a backup container — and from nothing
else. The object store's S3 port is the same: it holds the payload of every stored message, and the only mapping the
`object-storage` profile publishes is its console's, on loopback, answering nothing until that console is switched on.

## Upgrading

Back up the database, then apply the new release's schema artifact, then bring the new image up. That order is the one
with no window in which nothing serves: the new image refuses to start against a schema that is behind it, and the
running one keeps serving against a schema that is ahead.

```bash
docker compose exec --no-TTY postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
     --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'                            # the version being upgraded to

docker compose pull                                        # or: docker compose build
docker compose up -d
```

Rolling back is the same sequence with the previous image. **A schema change is not rolled back by it.** A migration
only moves forward; returning to the earlier schema means restoring the database from the backup taken before the
migration, which is the reason applying one is a step you decide to take.
[Rolling back](database-schema.md#rolling-back) states when that is necessary and when rolling only the image back is
enough.

### Upgrading a deployment that ran PostgreSQL 17

The database image is now `pgvector/pgvector:0.8.6-pg18`, and PostgreSQL does not read a data directory written by an
earlier major version. **An existing volume is not upgraded in place by bringing the new image up**, and what that
costs is worth knowing before it happens rather than after: PostgreSQL 18 moved this image's `PGDATA` into a
version-specific subdirectory and the Compose file now mounts the volume at the parent that holds it, so the image
finds a PostgreSQL 17 data directory where it expects that parent and **refuses to start**. The container exits `1`
carrying the image's own message — *there appears to be PostgreSQL data in: `/var/lib/postgresql`* — the server never
listens, the health check never passes, and nothing that depends on it comes up. The attempt writes nothing, so the
old data directory is intact and still dumpable afterwards.

Move the data across a dump, which is the migration path between majors. The dump has to come from a running
PostgreSQL 17 server, and the upgraded Compose file no longer starts one, so the first step is getting that server
back — against the same volume, mounted where 17 expects it:

```bash
docker compose down

docker run --detach --name mailfathom-pg17 \
  --volume mailfathom-postgres-data:/var/lib/postgresql/data \
  pgvector/pgvector:0.8.2-pg17                      # whichever image the deployment ran before the upgrade

docker exec mailfathom-pg17 \
  pg_dump --username mailfathom --format custom --exclude-extension=vector mailfathom > mailfathom-pg17.dump
docker rm --force mailfathom-pg17

docker volume rm mailfathom-postgres-data           # or MAILFATHOM_POSTGRES_VOLUME, if you named it
docker compose up -d postgres                       # initializes 18 and re-creates the role, database, and extension

docker compose exec -T postgres pg_restore --username mailfathom --dbname mailfathom < mailfathom-pg17.dump
docker compose up -d
```

Two details of those commands are what keep the restore clean, and both follow from the database being owned by an
unprivileged role. The dump **excludes the `vector` extension**, because the extension belongs to initialization rather
than to the data: a superuser installs it, `mailfathom` does not own it, and a dump carrying it makes `pg_restore`
fail on `COMMENT ON EXTENSION` with `must be owner of extension vector`. The restore takes **no `--clean`**, because
the database it restores into was created moments earlier and holds nothing to drop — and `--clean` would reach for
that same extension. Either one left in place ends the restore at exit `1` with the rows already in, which reads like a
failed migration and is not one.

Neither `pg_dump` nor `pg_restore` passes a password, because both reach the server over its Unix socket from inside
the container, where the image's own `initdb` left local connections trusted. Nothing on the `backend` network can
take that path: `pg_hba.conf` answers a connection arriving from there with `scram-sha-256`.

Resynchronizing from IMAP instead of restoring is also a complete answer, and a slower one: delete the volume, bring the
deployment up, apply the schema artifact, and let synchronization refill it. What it costs beyond time is everything
that is not in the mailbox — the answering audit trail and the embeddings, which are regenerated rather than refetched.

## Backup and what survives removal

The synchronized mail lives in a named volume, `mailfathom-postgres-data` by default.

```bash
# Back up. It reads the database, not the volume's files, so the dump is consistent.
docker compose exec -T postgres pg_dump --username mailfathom --format custom mailfathom > mailfathom-$(date +%F).dump

# Restore into a database that has already been created and migrated.
docker compose exec -T postgres pg_restore --username mailfathom --dbname mailfathom --clean --if-exists < mailfathom-2026-07-31.dump
```

| Command | The volume |
| --- | --- |
| `docker compose down` | Survives. The mail is still there on the next `up`. |
| `docker compose down --volumes` | **Destroyed.** Rebuilding it costs a full IMAP resynchronization. |

The files under `secrets/` and `config/` are yours and are never touched by either.

### With content in a bucket, the dump above is only half of it

`MAILFATHOM_CONTENT_STORAGE=ObjectStorage` makes the two stores one backup taken in two places. The content rows point
at objects in the bucket by a locator nothing recomputes, so a `pg_dump` alone restores a database naming objects it
does not carry.

1. **Take the dump above**, exactly as before.
2. **Back up the bucket** — everything beneath `MAILFATHOM_OBJECT_STORAGE_KEY_PREFIX`, or the whole bucket where
   MailFathom has it to itself. Your provider's own tooling does this; nothing here takes a bucket backup.
3. **Restore the database first, then the bucket.** In that order the window between them is a database pointing at
   objects not back yet, which reads as content temporarily unavailable. The other order leaves objects nothing points
   at, and the reclamation sweep is entitled to delete those once they are older than `MinimumObjectAge` — so restoring
   the bucket first can destroy part of what you are restoring.

A store the `object-storage` profile runs is no different in that order, and the second step is a copy out of it. It is
the S3 API that answers, so the same client does both directions — and `docker compose down --volumes` destroys
`mailfathom-silo-data` alongside the database's, which is a second thing that table is about. Copying the volume's files
underneath a running server is not a backup: the pool has its own metadata in `.minio.sys`, and a copy taken while the
server is writing is a copy of neither state.

```bash
mkdir -p mailfathom-content-backup

# The scoped key, read back from the two files the provisioning step wrote rather than from shell variables, which
# belonged to that session and are gone by the time a backup runs.
access_key_id="$(cat secrets/mailfathom/mailfathom-object-storage-access-key-id)"
secret_access_key="$(cat secrets/mailfathom/mailfathom-object-storage-secret-access-key)"

# Out. A throwaway container on the same internal network, with the destination bind-mounted, so nothing is staged in
# the store's own tmpfs and no port is published to reach it.
docker compose run --rm --no-deps --entrypoint sh \
  --volume "$PWD/mailfathom-content-backup:/backup" \
  silo -s "$access_key_id" "$secret_access_key" <<'BACKUP'
set -eu
mcli --insecure alias set store https://silo:9000 "$1" "$2"
mcli --insecure mirror --remove store/mailfathom-content /backup
BACKUP

# Back in. The same run without --remove, which there would delete from the store rather than from the copy.
docker compose run --rm --no-deps --entrypoint sh \
  --volume "$PWD/mailfathom-content-backup:/backup" \
  silo -s "$access_key_id" "$secret_access_key" <<'RESTORE'
set -eu
mcli --insecure alias set store https://silo:9000 "$1" "$2"
mcli --insecure mirror /backup store/mailfathom-content
RESTORE
```

The scoped access key is enough for both directions, which is why the root credential does not appear here.
`--insecure` because the certificate is one you issued and this client carries only the public roots the image ships —
the name is right, the authority is one nothing here was told about, and it is MailFathom rather than a backup script
that has to validate it. `--remove` on the way out makes the copy match the bucket rather than accumulate objects the
sweep has already reclaimed.

Take both from the same moment where you can. A row whose object is missing is not a corrupt deployment: the read
reports that message's content as unavailable and names it, every other message keeps working, and
[when the local copy is unusable](../features/email-content.md#when-the-local-copy-is-unusable) states what a client
sees. That message is re-fetchable from the mail server like any other; what is not is anything derived from it.

## Uninstalling

```bash
docker compose down --remove-orphans                # keeps the mail
docker compose down --volumes --remove-orphans      # destroys it
```

## The error a rootless-Podman teardown reports

Taking the deployment down under Podman can end on this, and everything it reports on has already happened:

```text
Error: removing container <id> network: 1 error occurred:
        * rootless netns: kill network process: permission denied
```

The containers are gone, the networks are gone, and the volume survives exactly as it does on any other `down`. What
failed is the last step of the teardown rather than the teardown. Rootless Podman holds the network namespace its
containers share open with a `pasta` process, and finishes by sending that process `SIGTERM` — and here the kernel
refused the signal. No file mode is involved in that refusal. `pasta` runs confined by an AppArmor profile, that profile
accepts a signal from an *unconfined* sender and from nobody else, and on a distribution that also ships an AppArmor
profile for `podman` the sender is no longer unconfined, so no rule matches and the signal is denied. Podman reads that
as the distribution's policy rather than as a defect of its own, and
[closed the report on those terms](https://github.com/containers/podman/issues/27372).

What it costs is one `pasta` process per teardown, left running with nothing to serve. `pgrep --list-full pasta` finds
them: the ones Podman started carry `rootless-netns` in their arguments. Killing them by hand works, because your own
shell is not what the policy refuses.

Making the error stop is a change to the host's AppArmor policy, and the one thing there not to guess at is which
profile refused whom. The denial says both:

```bash
journalctl --dmesg --grep 'apparmor="DENIED".*operation="signal"'   # /var/log/audit/audit.log where auditd runs
```

`profile=` names the process that was signalled and `peer=` the one that signalled it. What allows that pair is a rule
in the profile `profile=` names — `signal (receive) set=("term") peer=<peer>,` added inside the `pasta` profile, which
the `passt` package installs as `/etc/apparmor.d/usr.bin.pasta`, and applied with
`apparmor_parser --replace /etc/apparmor.d/usr.bin.pasta`. That file belongs to the package, so a later upgrade of it
asks what to do with the edit.

## The image

`MAILFATHOM_IMAGE` defaults to `mailfathom:local`, a name no registry can serve, so `docker compose up --build` builds this
checkout and nothing is ever pulled by accident. Point it at a published release to run one instead —
`ghcr.io/krzysztof318/mailfathom:<version>` or `docker.io/krzysztof318/mailfathom:<version>`, the same digest either
way — naming an immutable tag and never a moving one, and set `MAILFATHOM_PULL_POLICY=missing` in the same edit so the
two decisions stay one decision.

### Nightly builds

Nightly builds are unsupported development output: whatever `main` was the night it was built, carrying no release
promise and possibly expecting a schema no published migration produces. Nothing in `compose.yaml` names a nightly
image, so a deployment that does not name the overlay cannot reach one however it is configured.

Read [what a nightly build risks](container-image.md#what-a-nightly-build-risks) before using one. The short of it is
that a nightly has no upgrade path in either direction, that a database it has touched may not be usable by a release,
and that the tag you deployed is deleted once thirty newer nightlies exist. Name the exact `-nightly.<n>-<short revision>` identifier
rather than the moving `nightly` tag, so what is running does not change under you. That identifier is also what makes
a nightly a nightly: both registries carry both channels, so the reference says which one it is and the hostname says
nothing. `MAILFATHOM_NIGHTLY_REGISTRY` selects between them and defaults to `ghcr.io`. The package is public, so
nothing has to be logged in to before the pull.

Using one is deliberately awkward:

```bash
MAILFATHOM_NIGHTLY_ACKNOWLEDGED=i-understand-this-is-unsupported \
MAILFATHOM_NIGHTLY_TAG=<the nightly identifier> \
docker compose -f compose.yaml -f compose.nightly.yaml up -d
```

Leaving either variable out fails immediately with the reason. Containers started this way carry
`io.mailfathom.release-channel=nightly`, which is the value the image itself carries under the same name, so one started
months ago still says what it is and says it the same way `docker image inspect` does.

Compose can require that the acknowledgement *has* a value and nothing more — it has no equality operator — so the
phrase above is the one to use rather than one that is checked. The Helm chart does compare it exactly, because a
template function can. Neither is a security control: what both buy is that nobody reaches a nightly image without
reading a sentence saying it is not a release.

## The client

**No image carries MailFathom's own client today.** The Uno Platform client whose bundle used to travel inside the
image was withdrawn and the client is being rebuilt in React, so the variable below fails at startup on every current
release. It stays in `.env` as the plumbing the rebuilt client lands against, and what the rest of this section
describes is the contract it carries once an image has a bundle again:

```dotenv
MAILFATHOM_CLIENT=true
```

That variable writes two settings, and the second is a statement about this deployment rather than a convenience.
MailFathom refuses to serve a page over a clear-text socket unless a deployment has said that something in front of it
terminates TLS — the page, and every token a browser then sends back, cross whatever hop is between it and the person —
and this container speaks plain HTTP by design. So enabling the client here asserts that the reverse proxy the section
on [the network boundary](#the-network-boundary) describes exists. Keep `MAILFATHOM_HTTP_BIND` on loopback until it
does.

Beside it, `ClientEndpoint:Enabled` has to be on in the configuration under `./config`: the page is served on that
surface's listeners and calls its routes. The page turned on while the endpoint is off is refused at startup,
naming both; the reverse — the endpoint serving its routes with no page in front of them — is an ordinary
deployment and starts. What the page then has to present is unchanged by serving it here — [the client
endpoint](client-endpoint.md#serving-the-client-from-the-deployment) is the page.

## Personal-data scanning

The stack has a third service, and it is not started. `presidio-analyzer` sits behind a Compose profile, so
`docker compose up` brings up the two services it always did: no image is pulled, no container exists, and none of its
two gigabytes is held. That is the product's default — [sensitive-content
scanning](../features/sensitive-content-scanning.md) records what the feature hides and what each category costs.

Switching it on is **two** settings in `.env` rather than one, because Compose cannot make an environment entry
conditional on a profile. The profile decides whether the analyzer container exists; the switch decides whether
MailFathom asks it. Either one alone is a deployment that does not work:

```dotenv
COMPOSE_PROFILES=personal-data-scanning
MAILFATHOM_PERSONAL_DATA_SCANNING=true
```

Then `docker compose up -d`. The first start is slow — the analyzer loads a language model before it answers anything,
which takes tens of seconds — and MailFathom answers `Unhealthy` on `/health` while it cannot reach one. It does not
exit and it is not restarted: it becomes ready by itself once the model has loaded. There is no `depends_on` in either
direction, deliberately, and none is needed: a dependency on a service behind a profile is one Compose resolves
differently depending on which profiles are active, and nothing about the start sequence has to be ordered when the
application waits on its own probe. `docker compose ps` shows the analyzer's own health check while that is happening.

To use an analyzer you already operate, set `MAILFATHOM_PERSONAL_DATA_ANALYZER` to its address and leave
`COMPOSE_PROFILES` alone — nothing is then started for it. Keep that address **inside your own network**: the point of
scanning is that content is inspected before it leaves the trust boundary, and the feature page states what pointing it
outside gives up.

The analyzer is attached to the `backend` network alone and publishes no port. It receives mail content in the clear and
answers where the identifiers in it are, so nothing outside the host can reach it and MailFathom asks it over plain HTTP.

**`MAILFATHOM_PERSONAL_DATA_LANGUAGE` is not a language switch on its own.** It states the first language the analyzer is
asked in, and the pinned image is built for English alone — one model, and a recognizer registry declaring English.
Naming another code leaves MailFathom unready rather than scanning in that language, because the analyzer answers that
it recognises nothing for it. A second language means an image built for it, named in `MAILFATHOM_PRESIDIO_IMAGE`;
[the analyzer's languages](personal-data-analyzer-languages.md) records what that takes and which identifiers each
language reaches.

**A second language is a second line in `compose.yaml` rather than a second variable.** MailFathom takes a list of
languages, which reaches .NET configuration as indexed environment keys, and Compose expands no variable into several of
them — so the shipped file writes `SensitiveContent__PersonalDataAnalyzer__Languages__0` from the variable above and
carries a commented `__1` line beside it to uncomment. The numbering starts at zero and leaves no gap, because a gap ends
the bound list at it and everything after is dropped. One scan then asks the analyzer once per language, one call after
the other, inside the single `SensitiveContent:ScanTimeout` budget rather than one budget each.

## Spam scanning

The stack has a fourth service, `spamassassin`, and it is not started either. It sits behind its own Compose profile, so
`docker compose up` pulls no image for it and holds none of its memory. [Spam
classification](../features/spam-classification.md) records what a classification holds and what the scanner adds.

Switching it on is two settings, for exactly the reason the analyzer's are two — the profile decides whether the
container exists, the switch decides whether MailFathom asks it, and either one alone is a deployment that does not
work:

```dotenv
COMPOSE_PROFILES=spam-scanning
MAILFATHOM_SPAM_SCANNING=true
```

`COMPOSE_PROFILES` takes a list, so several at once are
`COMPOSE_PROFILES=personal-data-scanning,spam-scanning,object-storage` — the third being
[the object store](#running-an-object-store-beside-mailfathom), which this file can also start.

The first start is slow here too: the daemon compiles its rule corpus before it listens, and MailFathom refuses to serve
while no daemon answers, so `restart: unless-stopped` carries that refusal into a retry exactly as it does for the
analyzer. `docker compose ps` shows the daemon's own health check while that is happening.

To use a daemon you already operate, set `MAILFATHOM_SPAM_SCANNER_HOST` to its address and leave `COMPOSE_PROFILES`
alone — nothing is then started for it. Keep that address **inside your own network**: the daemon is sent whole messages
unredacted, and the feature page states what pointing it outside gives up.

The daemon publishes no port and it accepts a connection from any address it can be reached from, so what limits who may
ask it is the network it is on. It is attached to `frontend` as well as `backend`, and that is the one deliberate
difference from the analyzer: the daemon fetches its rule updates on start and daily afterwards, and a corpus frozen at
the image's build scores today's mail worse than a fresh one. Nothing derived from the owner's mail goes out that route
— `MAILFATHOM_SPAM_SCANNER_DNS_CHECKS` is `0`, which keeps the blocklist rules that would send sending addresses and URI
host names to third parties switched off, and the image bundles no plugin that reports anything anywhere. Remove
`frontend` from the service to take the egress away and keep the corpus the image shipped with.

## Storing message content in a bucket

The raw MIME of every message lives in PostgreSQL beside the metadata unless `MAILFATHOM_CONTENT_STORAGE` says
otherwise. `ObjectStorage` writes new payloads into an S3-compatible bucket instead; the metadata, the indexes, the
embeddings, and every job still go through PostgreSQL, so this is a decision about payload bytes and about nothing else.
The endpoint is one you operate or rent, or the one
[the `object-storage` profile](#running-an-object-store-beside-mailfathom) starts beside MailFathom. Either way its
bucket exists before MailFathom writes to it: nothing here creates one.

```dotenv
MAILFATHOM_CONTENT_STORAGE=ObjectStorage
MAILFATHOM_OBJECT_STORAGE_ENDPOINT=https://objects.example.test
MAILFATHOM_OBJECT_STORAGE_BUCKET=mailfathom-content
```

The credential is two files rather than two variables, for the reason no credential is in `.env`: it goes into
`./secrets/mailfathom/`, the directory MailFathom reads its own secrets from, beside the mailbox passwords.

```bash
printf %s '<access key id>' > secrets/mailfathom/mailfathom-object-storage-access-key-id
printf %s '<secret access key>' > secrets/mailfathom/mailfathom-object-storage-secret-access-key
chmod 444 secrets/mailfathom/mailfathom-object-storage-*
```

`printf %s` rather than `echo`, so the file holds exactly what the endpoint issued. MailFathom strips one trailing
newline when it decodes material as text, so `echo` would work as well — but only one, and a signature derived from a
credential with a stray byte in it reaches you as every request being rejected rather than as a credential it could not
read. The access key identifier is a secret like the secret beside it — it names an identity at the endpoint, and it is
one half of what an attacker needs. Both are read before every request, so rotating a key at the endpoint and replacing
the file takes effect on the next call with nothing to restart.

MailFathom refuses to start under this backend without an address, a bucket, and both files, rather than acquiring the
host's own identity from the environment, and it refuses an endpoint that is not `https` — a request carries a signature
and, on a write, the message itself. An endpoint you run yourself, whose certificate this host's trust store does not
answer for, needs its authority as a third file and the two `TrustAnchor` lines in `compose.yaml` uncommented; leaving
them commented is what an endpoint the host already trusts wants, because an empty reference is a credential MailFathom
cannot read rather than an anchor it does not need. No setting anywhere turns validation off instead — see
[platform TLS policy](platform-tls-policy.md).

`MAILFATHOM_OBJECT_STORAGE_KEY_PREFIX` is what makes a bucket MailFathom shares with something else safe, and nothing
here can check that two deployments sharing one arranged disjoint prefixes; the reclamation sweep lists beneath that
prefix and nowhere else. The timeouts, the sweep, and the bounds on moving what is already stored are ordinary
configuration and belong in `./config` beside the
mailboxes — [`ContentStorage`](configuration-runtime.md#contentstorage) holds every key, and
[where a payload is kept](../features/email-content.md#where-a-payload-is-kept) states what a content row records.

**Switching is a move, not a setting.** The variable decides only where the next write goes: every content row names the
store holding its own payload, so setting it moves nothing already stored and clearing it re-encodes nothing. Carrying
what is already in the database into the bucket is an operator's act with its own controls —
[moving stored content into the bucket](moving-stored-content.md) is that operation. Until it has run, and after a
partial run, the deployment reads from both stores and keeps needing both: pointing it at a bucket it no longer has
leaves the deployment unready rather than the mail unreadable.

## Running an object store beside MailFathom

An operator who wants payload bytes out of PostgreSQL and does not already run object storage has nothing to point the
endpoint above at. The `object-storage` profile is that answer: it starts one [Silo](https://github.com/pgsty/silo)
container on a named volume, on the internal network and reachable from nothing outside the host. Silo is PGSTY's
maintained fork of the open-source MinIO server, keeping one release line alive after upstream ended community
distribution — the same image, at the same pin, that the Helm chart, the Quadlet units, and the
[integration suite](local-development.md#the-object-storage-endpoint) use, so what runs here is the server the S3
adapter was verified against.

**One container, one volume, and that is the whole of it.** No erasure coding, no second node, no replication, no
failover. What it protects against is a container being replaced; a disk that fails takes the payloads with it, which is
what makes [the backup order](#with-content-in-a-bucket-the-dump-above-is-only-half-of-it) the thing standing between
this and losing them.

Enabling it is the profile beside the settings the section above already needs — Compose cannot make an environment
entry conditional on a profile, so the profile decides whether the store exists and these decide whether MailFathom
writes to it:

```dotenv
COMPOSE_PROFILES=object-storage
MAILFATHOM_CONTENT_STORAGE=ObjectStorage
MAILFATHOM_OBJECT_STORAGE_ENDPOINT=https://silo:9000
MAILFATHOM_OBJECT_STORAGE_BUCKET=mailfathom-content
```

**It answers over TLS, and there is no way to run it otherwise.** MailFathom refuses a plain `http` endpoint and
validates what it is presented, with no setting anywhere that turns that off, so the store terminates TLS itself with a
certificate covering `silo` — the service name it is reached at on the internal network. No public authority issues one
for that, so it is signed by an authority of your own, and that authority is the third file in
`./secrets/mailfathom/` the section above describes, with the two `TrustAnchor` lines in `compose.yaml` uncommented.

Four files have to exist before the container starts, and they are in `./secrets/` rather than `./secrets/mailfathom/`
on purpose: that second directory is bind-mounted into the MailFathom container, and the root credential administers the
whole store. A missing one fails the container rather than starting a server that would serve plain HTTP or accept a
credential nobody chose.

```bash
printf %s "$(openssl rand -hex 12)" > secrets/silo-root-access-key-id
printf %s "$(openssl rand -base64 32)" > secrets/silo-root-secret-access-key
cp /path/to/silo.crt secrets/silo-public.crt
cp /path/to/silo.key secrets/silo-private.key
chmod 400 secrets/silo-*
```

The root secret must be at least eight characters, which is the server's own rule.

**Neither the bucket nor the access key MailFathom presents is created by the store**, so both are provisioned once
after it is healthy, with the management client the image already carries. Until they exist MailFathom reports itself
unready rather than storing mail: its startup probe writes and deletes one object of its own, so read permission alone
is not enough to become ready.

```bash
docker compose up -d silo

access_key_id="$(openssl rand -hex 12)"
secret_access_key="$(openssl rand -base64 32)"

docker compose exec -T silo sh -s "$access_key_id" "$secret_access_key" <<'PROVISION'
set -eu

# --insecure throughout: the call is to the container's own loopback address, which the certificate names nowhere. What
# it names is `silo`, and that is the name MailFathom validates.
mcli --insecure alias set store https://127.0.0.1:9000 \
  "$(cat /run/secrets/silo-root-access-key-id)" \
  "$(cat /run/secrets/silo-root-secret-access-key)"

mcli --insecure mb --ignore-existing store/mailfathom-content

# The four operations the adapter performs and nothing else: it lists beneath its prefix to reclaim released objects,
# and it gets, puts, and deletes one object at a time. It never creates a bucket and never touches another one.
cat > /tmp/mailfathom-content.json <<'POLICY'
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": [ "s3:ListBucket" ], "Resource": [ "arn:aws:s3:::mailfathom-content" ] },
    { "Effect": "Allow", "Action": [ "s3:GetObject", "s3:PutObject", "s3:DeleteObject" ], "Resource": [ "arn:aws:s3:::mailfathom-content/*" ] }
  ]
}
POLICY

mcli --insecure admin policy create store mailfathom-content /tmp/mailfathom-content.json
mcli --insecure admin user add store "$1" "$2"
mcli --insecure admin policy attach store mailfathom-content --user "$1"
PROVISION

printf %s "$access_key_id" > secrets/mailfathom/mailfathom-object-storage-access-key-id
printf %s "$secret_access_key" > secrets/mailfathom/mailfathom-object-storage-secret-access-key
chmod 444 secrets/mailfathom/mailfathom-object-storage-*
```

**The console is not exposed.** It is a management interface over every object the store holds — bucket contents, access
keys, the server's own configuration — so it is a second surface onto the mail rather than a convenience, and with
`MAILFATHOM_SILO_CONSOLE` at its default the server never starts that listener; the published port is on loopback and
answers nothing. Setting it to `on` starts it, on `127.0.0.1:9001` unless `MAILFATHOM_SILO_CONSOLE_BIND` says otherwise
— and it is bound there because what authenticates at it is the root credential, which is the whole store rather than
the one bucket. Nothing terminates TLS in front of it; the store's own certificate names `silo`, so a browser reaching
it through the loopback mapping will report a name mismatch.

**Upgrading it is a variable and a restart**, and deliberately not part of a MailFathom upgrade: the two have no version
relationship, and doing both at once makes a failure ambiguous. Back the bucket up first — the on-disk format is the
server's, and a version that will not start against it leaves the payloads reachable only by going back to the previous
tag, which is the same one-variable change in reverse.

```dotenv
MAILFATHOM_SILO_IMAGE=docker.io/pgsty/silo:RELEASE.<newer>
```

```bash
docker compose up -d silo
```

The store is unreachable while the container is replaced, and MailFathom reports itself unready and stores nothing new
for that window rather than failing a read of what is already in PostgreSQL. Read the upstream release notes between the
two tags before moving one: Silo's release line is a MinIO one, named by timestamp rather than by version.

**Nothing of Silo is in MailFathom's image or in this repository.** `compose.yaml` names an image your host pulls from
PGSTY's own registry. Silo is AGPL-3.0-or-later, which
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) records
together with the reading it is used under. Its own lifecycle — upgrades, its configuration, its users beyond the one
above — is yours; MailFathom manages none of it.

## Bounds

`.env.example` documents every knob: log rotation, CPU and memory limits for all five services, the published bind
address and port, the database and role names, both volume names, the four personal-data settings, the seven spam
settings, the six content-storage settings, and the four the object store adds. Each is the value the Compose file
already applies, so an unset variable and the documented value mean the same thing.

The analyzer's memory limit is the one worth reading before it is lowered: it defaults to two gigabytes because the model
is held for the life of the container, and below roughly one it is killed while loading — which reaches MailFathom as an
analyzer that never became ready. The spam daemon's is modest by comparison and defaults to 512 megabytes, which holds
the compiled corpus and a child per scan. The object store's is modest for a different reason — it holds no index in
memory and streams what it serves, so what it wants is disk, and that is the volume rather than a limit.

## Related

- [Applying the database schema](database-schema.md) — the release artifact, the privileges it needs, and the three
  startup failures it answers
- [The container image](container-image.md) — what is inside it, how it runs, and why it carries no schema tool
- [Podman Quadlet](deployment-quadlet.md) — the same stack as rootless systemd units, for encrypted systemd credentials
- [Kubernetes and Helm](deployment-kubernetes.md) — the same contract in the other shape
- [The platform TLS policy](platform-tls-policy.md) — for a mail server whose handshake the container's own OpenSSL
  refuses; the file has to be mounted into the container and named in the service's `environment:` block
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
