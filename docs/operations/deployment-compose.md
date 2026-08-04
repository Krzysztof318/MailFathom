# Deploying with Docker Compose

<!-- describes: deploy/compose/** -->

`deploy/compose/` is the supported Compose deployment: MailFathom, PostgreSQL, and a one-shot schema step that only ever
runs when an operator asks for it. It is the shape to use for self-hosting on one machine.

Everything below assumes `deploy/compose/` as the working directory.

## Before the first start

Three files have to exist. Two are database credentials the Compose file mounts as secrets; the third is the
configuration MailFathom reads.

```bash
cd deploy/compose

cp .env.example .env

mkdir -p secrets/mailfathom
chmod 700 secrets secrets/mailfathom

openssl rand -base64 33 | tr -d '\n' > secrets/postgres-superuser-password
openssl rand -base64 33 | tr -d '\n' > secrets/mailfathom-database-password
chmod 444 secrets/postgres-superuser-password secrets/mailfathom-database-password

cp config/10-mailfathom.json.example config/10-mailfathom.json
$EDITOR config/10-mailfathom.json
```

**The directory restricts access and the files are readable — not the other way round.** MailFathom runs as an
unprivileged account inside the container that corresponds to no user on the host, and Compose bind-mounts a secret
with the host's own permissions: outside Swarm it ignores `mode`, `uid`, and `gid`. A `0600` file therefore presents
to MailFathom as a secret reference that cannot be resolved, and startup fails naming the setting. The `0700` directory
is what keeps other users on the host out; only root and you can reach through it at all. Every file you add under
`secrets/mailfathom/` needs the same treatment.

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
port, and the superuser password is already mounted inside its container, so the shortest route is to run psql there
and hand it the script on standard input — which also keeps the credential off a command line:

```bash
docker compose exec --no-TTY postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/postgres-superuser-password)" exec psql \
     --username postgres --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'
```

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
proxy the certificate.

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
  'PGPASSWORD="$(cat /run/secrets/postgres-superuser-password)" exec psql \
     --username postgres --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'                            # the version being upgraded to

docker compose pull                                        # or: docker compose build
docker compose up -d
```

Rolling back is the same sequence with the previous image. **A schema change is not rolled back by it.** A migration
only moves forward; returning to the earlier schema means restoring the database from the backup taken before the
migration, which is the reason applying one is a step you decide to take.
[Rolling back](database-schema.md#rolling-back) states when that is necessary and when rolling only the image back is
enough.

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

## Bounds

`.env.example` documents every knob: log rotation, CPU and memory limits for both services, the published bind address
and port, the database and role names, and the volume name. Each is the value the Compose file already applies, so an
unset variable and the documented value mean the same thing.

## Related

- [Applying the database schema](database-schema.md) — the release artifact, the privileges it needs, and the three
  startup failures it answers
- [The container image](container-image.md) — what is inside it, how it runs, and why it carries no schema tool
- [Kubernetes and Helm](deployment-kubernetes.md) — the same contract in the other shape
- [The platform TLS policy](platform-tls-policy.md) — for a mail server whose handshake the container's own OpenSSL
  refuses; the file has to be mounted into the container and named in the service's `environment:` block
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
