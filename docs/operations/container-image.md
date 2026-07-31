# The container image

`Dockerfile` at the repository root is the only image definition MailMcp has. Both deployment shapes in `deploy/` build
from it, and nothing else produces an image, so what this page describes is what runs wherever MailMcp runs in a
container.

It produces **two** images, and the difference between them is the whole of MailMcp's migration model.

| Target | What it is | What it can do to a database |
| --- | --- | --- |
| `runtime` | The service | Read and write rows. It contains no migration tool and no SQL. |
| `migrations` | A one-shot schema step | Apply the schema, once, when an operator runs it. |

```bash
docker build --target runtime    --tag mailmcp:local .
docker build --target migrations --tag mailmcp-migrations:local .
```

Keeping the two apart is what makes "the host never applies a migration" a property of what was built rather than a
rule someone has to remember. `DatabaseSchemaStartupGate` refuses to start against a schema this build does not
recognize, in every environment; the answer to that refusal is the second image, run deliberately.

## What is inside, and what is not

The runtime image is built on `mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra` and is about 77 MB.
Chiseled means there is no shell, no package manager, and no HTTP client: a process that reaches the container finds
almost nothing to use. `-extra` carries ICU and tzdata, which the plain chiseled image does not — MailMcp decodes
internationalized headers, folds case for search, and formats instants for several time zones, and the invariant
globalization the smaller image forces would quietly change how mail from outside one alphabet is read.

It contains the published application and nothing else. No SDK, no source tree, no repository history, no test
artifacts, no build cache, no credential, and no certificate. The XML documentation files every project generates are
dropped at publish, because none is read at run time and shipping them would put the repository's commentary about its
own internal contracts into an artifact an operator can unpack. The portable symbol files stay, because they are what
turns a stack trace in a support report into file and line numbers.

`.dockerignore` is an allow-list rather than a deny-list: it excludes everything and then names what may reach the
build. The build context is the repository root, which is also where a developer's `.env`, a mounted secret, and a
certificate live, and a rule that only excluded what someone remembered would send all of them to the daemon.

Every base image is pinned to an explicit patch version. `scripts/verify-deployment-assets.sh` rejects a floating one.

## How it runs

| Property | Value |
| --- | --- |
| User | `1654`, the unprivileged `app` account the .NET base images define |
| Port | `8080`, plain HTTP |
| Writable paths | `/tmp` only, which a deployment supplies as a tmpfs or an `emptyDir` |
| Entrypoint | `dotnet /app/MailMcp.Host.dll` |
| Health check | `dotnet /app/MailMcp.Host.dll --health-probe` |

The application directory is owned by `root` and the process is not, so the service cannot rewrite its own code even
before the deployment imposes a read-only root filesystem on it. Both deployments do impose one, and both drop every
Linux capability.

**The container speaks plain HTTP and terminates no TLS.** A certificate belongs to the reverse proxy or the ingress in
front of it, which is also the only place one has to exist. An MCP endpoint reached over plain HTTP hands its API key
and every message it serves to anything on the network path.

`DOTNET_EnableDiagnostics=0` is set, so no diagnostic IPC socket is created. That socket can request a process dump,
and a dump is a way to read secret material out of managed memory — the residual exposure
[secret provisioning](secret-provisioning.md#secret-material-in-process-memory) documents and asks deployments to
close. Set it back to `1` deliberately, for one session, when a dump is genuinely needed.

### The health probe

Kubernetes probes `/health` and `/alive` over HTTP from the kubelet and needs nothing inside the container. Docker and
Podman do not work that way: `HEALTHCHECK` runs a command *inside* the container, and a chiseled image has no shell for
one to be written in. The image therefore uses the runtime it already ships — `--health-probe` asks the running host's
own readiness endpoint over loopback and reports the answer as an exit code.

`MAILMCP_HEALTH_PROBE_PATH` selects which endpoint it asks; the default is `/health`.

The two endpoints answer different questions and are wired to different probes on purpose:

- **`/health`** runs every registered check, the database among them. It is readiness: a process that cannot reach its
  database stops receiving requests it cannot fulfil.
- **`/alive`** runs only the checks tagged `live`, which report the process itself. It is liveness: a database outage
  must never become a restart loop that cannot fix what is actually broken.

Both are unauthenticated. Neither carries mailbox data — the body is one word — and a probe has no credential to
present. This is the same split [the MCP endpoint](mcp-endpoint.md) describes: the authorization requirement sits on
the MCP route rather than on the pipeline.

### Shutdown

`SIGTERM` starts a graceful stop. The host's own budget comes from `MailSynchronization:ShutdownDrainTimeout`, and a
deployment's grace period has to be longer than it or the process is killed with the drain still running. Both
deployments in `deploy/` allow 60 seconds against a 10-second default; raise them together.

### Labels

The image carries the OCI labels that let a pulled image be traced back to the commit it was built from —
`org.opencontainers.image.source`, `.revision`, `.version`, `.created`, `.licenses` — supplied as build arguments.
`IMAGE_VERSION` currently defaults to `0.0.0-unversioned`: MailMcp has published no release, and what a version means
here is still an open decision. The application does not yet report its own version at run time either.

## The schema script

The migration image applies an **idempotent SQL script**, generated during the build from the migrations the same build
compiled. It is not a checked-in file, so the script and the migrations can never describe two different schemas.

That shape was chosen over an EF Core migration bundle for three reasons. It is text, so it reads the same on every
architecture and can be reviewed as SQL — which is how this repository already reviews a migration. A bundle is a
native executable per runtime identifier, needing a runtime-identifier-specific restore that the committed lock files
reject. And a bundle takes its connection string on a command line, where every process on the host can read it, while
`psql` reads a password from a file.

Read it before applying it:

```bash
docker run --rm mailmcp-migrations:local --print
```

The script is idempotent: it consults `__EFMigrationsHistory` and applies only what is missing, so running it against an
up-to-date database is a no-op. It brings its own transaction around each migration and runs under `ON_ERROR_STOP`, so
a failure part-way rolls back rather than leaving a schema nothing describes.

**The role it connects as needs more privilege than the service's.** The schema installs the `vector` extension, which
PostgreSQL does not permit an ordinary role to create. That asymmetry is the point of a separate step: grant the
service a role that can read and write rows and nothing more, and give the migration a role that can do the rest. Both
deployments in `deploy/` show one way to arrange that.

The connection is resolved in this order, and a file is preferred at every step because a file never appears in another
process's environment block or in `ps`:

| Setting | Holds |
| --- | --- |
| `MAILMCP_MIGRATION_DSN_FILE` | A file holding a libpq connection string or URI |
| `MAILMCP_MIGRATION_DSN` | The same value, inline |
| `PGHOST` / `PGPORT` / `PGDATABASE` / `PGUSER` | The connection, with the password from `MAILMCP_MIGRATION_PASSWORD_FILE` |

## Verification

```bash
bash scripts/verify-deployment-assets.sh   # reads the files: pins, privileges, rendering, schema guards
bash scripts/smoke-deployment.sh compose   # starts the real thing and asserts what only a running one can answer
```

The second one proves, among other things, that the host refuses an unrecognized schema, that the migration is
idempotent, that the container runs unprivileged on a read-only root filesystem, and that shutting it down is a stop
rather than a kill. The `Deployment assets` job of the CI workflow runs both, plus the two-architecture build and the
same smoke run against an ephemeral Kubernetes cluster.

## Where the deployments are

- [Docker Compose](deployment-compose.md)
- [Kubernetes and Helm](deployment-kubernetes.md)
