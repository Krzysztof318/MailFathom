# Deploying with Docker Compose

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
build defines: 20260730152610_Initial.
```

> **The schema artifact does not exist yet.** The reviewed, idempotent artifact a released installation applies — and
> the command that applies it — are tracked by [issue #126](https://github.com/Krzysztof318/MailFathom/issues/126). Until
> it ships, establishing the schema is your own step against the `mailfathom` database: publish the database port
> temporarily, or attach a `psql` container to the `backend` network, and apply the migrations this build defines.
> Whatever you use, apply it once, read it before applying it, and take a backup first. That is the same discipline the
> shipped artifact will enforce; what is missing is the artifact, not the rule.

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
curl -fsS http://127.0.0.1:8080/health               # readiness, including the database
curl -fsS http://127.0.0.1:8080/alive                # liveness, the process alone
docker compose logs -f mailfathom
```

The MailFathom container declares no Docker health check. Its image carries no shell and no HTTP client for one to run
in, so the two endpoints above are asked from outside the container instead;
[issue #179](https://github.com/Krzysztof318/MailFathom/issues/179) is what turns the probe surface into something a
deployment configures.

The MCP endpoint answers at `/mcp` and is off until the configuration enables it. Read
[the MCP endpoint](mcp-endpoint.md) before you do; an enabled endpoint must state how it is authenticated, and there is
no default.

## The network boundary

The port is published on **loopback** by default. MailFathom speaks plain HTTP and terminates no TLS, so publishing it on
another interface exposes synchronized mail without transport protection. Change `MAILFATHOM_HTTP_BIND` only once a
reverse proxy on the `frontend` network is what listens publicly, and give that proxy the certificate.

PostgreSQL publishes no port at all. It sits on `backend`, which is declared `internal`, so it is reachable from
MailFathom and from whatever else you attach to that network — a schema step or a backup container — and from nothing
else.

## Upgrading

```bash
docker compose pull                                        # or: docker compose build
docker compose up -d
# if the host reports a pending migration, apply the schema and bring it up again:
docker compose up -d
```

Rolling back is the same sequence with the previous image. **A schema change is not rolled back by it.** A migration
only moves forward; going back to an image that expects an earlier schema means restoring the database from a backup
taken before the migration, which is the reason applying one is a step you decide to take.

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
checkout and nothing is ever pulled by accident. **There is no published MailFathom release yet.** Point it at an
immutable tag once one exists — never a moving tag.

### Nightly builds

GHCR nightly builds are unsupported development output: whatever `main` happened to be when someone dispatched a build,
carrying no release promise and possibly expecting a schema no published migration produces. Nothing in `compose.yaml`
reads GHCR, so a deployment that does not name the overlay cannot reach one however it is configured.

Using one is deliberately awkward:

```bash
MAILFATHOM_NIGHTLY_ACKNOWLEDGED=i-understand-this-is-unsupported \
MAILFATHOM_NIGHTLY_TAG=<the nightly identifier> \
docker compose -f compose.yaml -f compose.nightly.yaml up -d
```

Leaving either variable out fails immediately with the reason. Containers started this way carry
`io.mailfathom.release-channel=ghcr-nightly-unsupported`, so one started months ago still says what it is.

Compose can require that the acknowledgement *has* a value and nothing more — it has no equality operator — so the
phrase above is the one to use rather than one that is checked. The Helm chart does compare it exactly, because a
template function can. Neither is a security control: what both buy is that nobody reaches a nightly image without
reading a sentence saying it is not a release.

## Bounds

`.env.example` documents every knob: log rotation, CPU and memory limits for both services, the published bind address
and port, the database and role names, and the volume name. Each is the value the Compose file already applies, so an
unset variable and the documented value mean the same thing.

## Related

- [The container image](container-image.md) — what is inside it, how it runs, and the schema script
- [Kubernetes and Helm](deployment-kubernetes.md) — the same contract in the other shape
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
