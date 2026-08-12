# Deploying with Docker Compose

<!-- describes: deploy/compose/** -->

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
openssl rand -base64 33 | tr -d '\n'  > secrets/mailfathom/mcp-workstation-key
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
else.

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

## Uninstalling

```bash
docker compose down --remove-orphans                # keeps the mail
docker compose down --volumes --remove-orphans      # destroys it
```

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
which takes tens of seconds — and MailFathom refuses to serve while it cannot reach one. There is no `depends_on` in
either direction, deliberately: a dependency on a service behind a profile is one Compose resolves differently depending
on which profiles are active, so `restart: unless-stopped` is what carries MailFathom's startup refusal into a retry
until the model has loaded. `docker compose ps` shows the analyzer's own health check while that is happening.

To use an analyzer you already operate, set `MAILFATHOM_PERSONAL_DATA_ANALYZER` to its address and leave
`COMPOSE_PROFILES` alone — nothing is then started for it. Keep that address **inside your own network**: the point of
scanning is that content is inspected before it leaves the trust boundary, and the feature page states what pointing it
outside gives up.

The analyzer is attached to the `backend` network alone and publishes no port. It receives mail content in the clear and
answers where the identifiers in it are, so nothing outside the host can reach it and MailFathom asks it over plain HTTP.

## Bounds

`.env.example` documents every knob: log rotation, CPU and memory limits for all three services, the published bind
address and port, the database and role names, the volume name, and the four personal-data settings. Each is the value the
Compose file already applies, so an unset variable and the documented value mean the same thing.

The analyzer's memory limit is the one worth reading before it is lowered: it defaults to two gigabytes because the model
is held for the life of the container, and below roughly one it is killed while loading — which reaches MailFathom as an
analyzer that never became ready.

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
