# The container image

`deploy/docker/Dockerfile` is the only image definition MailFathom has. Both deployment shapes in `deploy/` build from it,
and nothing else produces an image, so what this page describes is what runs wherever MailFathom runs in a container.

The build context is the repository root, so the definition is named rather than found:

```bash
docker build --target runtime --file deploy/docker/Dockerfile --tag mailfathom:local .
```

It produces one image: the service. It carries no migration tool, no SQL, and no credential that could apply one, which
is what makes "the host never applies a migration" a property of what was built rather than a rule someone has to
remember. `DatabaseSchemaStartupGate` refuses to start against a schema this build does not recognize, in every
environment, and the reviewed artifact that answers that refusal belongs to issue #126 rather than to anything here.

## What is inside, and what is not

The runtime image is built on `mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra` and is about 77 MB.
Chiseled means there is no shell, no package manager, and no HTTP client: a process that reaches the container finds
almost nothing to use. `-extra` carries ICU and tzdata, which the plain chiseled image does not — MailFathom decodes
internationalized headers, folds case for search, and formats instants for several time zones, and the invariant
globalization the smaller image forces would quietly change how mail from outside one alphabet is read.

It contains the published application and nothing else — plus the two files that licensing requires travel with it,
`/app/LICENSE` and `/app/NOTICE`. No SDK, no source tree, no repository history, no test
artifacts, no build cache, no credential, and no certificate. The XML documentation files every project generates are
dropped at publish, because none is read at run time and shipping them would put the repository's commentary about its
own internal contracts into an artifact an operator can unpack. The portable symbol files stay, because they are what
turns a stack trace in a support report into file and line numbers.

`deploy/docker/Dockerfile.dockerignore` is an allow-list rather than a deny-list: it excludes everything and then names
what may reach the build. The build context is the repository root, which is also where a developer's `.env`, a mounted
secret, and a certificate live, and a rule that only excluded what someone remembered would send all of them to the
daemon. Docker looks for an ignore-file named after the Dockerfile before it looks for one at the context root, and
prefers it, so the file bounding the context travels with the definition that uses it.

Every base image is pinned to an explicit patch version. `scripts/verify-deployment-assets.sh` rejects a floating one.

## How it runs

| Property | Value |
| --- | --- |
| User | `1654`, the unprivileged `app` account the .NET base images define |
| Ports | `8080`, plain HTTP, for `/` and `/mcp`; `8081` for the probes, on a listener of its own |
| Writable paths | `/tmp` only, which a deployment supplies as a tmpfs or an `emptyDir` |
| Entrypoint | `dotnet /app/MailFathom.Host.dll` |
| Health check | None. See [the health endpoints](#the-health-endpoints) below. |

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

### The health endpoints

The host serves three probes on a listener of their own — port `8081` unless a deployment configures another — in every
environment. Kubernetes probes them over HTTP from the kubelet and needs nothing inside the container, which is what
the chart's probes use.

**The image declares no `HEALTHCHECK`.** Docker and Podman run one as a command *inside* the container, and a chiseled
image has no shell and no HTTP client for one to be written in. Adding either so the container could ask an endpoint
that is already reachable from outside would grow its attack surface for nothing; under Compose, the published probe
port is asked from the host instead.

The three answer different questions and are wired to different probes on purpose:

- **`/started`** reports whether the host's startup gates have completed: every secret reference resolved and the
  database schema verified. It is what an orchestrator's startup probe reads, so a slow first start extends the grace
  period rather than counting as a failure.
- **`/health`** consults the dependencies a request needs, the database among them. It is readiness: a process that
  cannot reach its database stops receiving requests it cannot fulfil.
- **`/alive`** consults only process-local state. It is liveness: a database outage must never become a restart loop
  that cannot fix what is actually broken.

All three are unauthenticated, and none of them is served on port 8080 — a probe path asked there is answered with
`404`, and `/mcp` asked on the probe port is too. The exposure control is which network the probe port is published on.
[The health endpoints](health-endpoints.md) records the full contract, including the TLS transports and the switch that
turns the surface off.

### Shutdown

`SIGTERM` starts a graceful stop. The host's own budget comes from `MailSynchronization:ShutdownDrainTimeout`, and a
deployment's grace period has to be longer than it or the process is killed with the drain still running. Both
deployments in `deploy/` allow 60 seconds against a 10-second default; raise them together.

### Labels

The image carries the OCI labels that let a pulled image be traced back to the commit it was built from —
`org.opencontainers.image.source`, `.revision`, `.version`, `.created` — supplied as build arguments.

`IMAGE_VERSION` has no useful default, and its `0.0.0-unversioned` placeholder says so. The version is declared once,
as `VersionPrefix` in `Directory.Build.props`, and every build path reads it with `scripts/read-declared-version.sh`
rather than restating it; `scripts/verify-deployment-assets.sh` fails a build path that stopped doing so, which is what
keeps a labelled version from drifting away from the stamped one. [ADR 0004](../decisions/0004-versioning-and-release-policy.md)
records why the number lives in one reviewed line.

`IMAGE_REVISION` is passed to the publish inside the build as `SourceRevisionId`, so the assemblies report the same
commit the label names rather than a second claim about it. The running process then reports its version and revision
in its [startup record](host-startup-telemetry.md), and its version to an MCP client during `initialize`. A published
artifact identifies itself both ways: from outside, through the labels, without being run; and from inside, once it is.

```bash
docker build --target runtime --file deploy/docker/Dockerfile \
  --build-arg "IMAGE_VERSION=$(bash scripts/read-declared-version.sh)" \
  --build-arg "IMAGE_REVISION=$(git rev-parse HEAD)" \
  --build-arg "IMAGE_CREATED=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --tag mailfathom:local .
```

`org.opencontainers.image.licenses` is fixed rather than passed in, at `Apache-2.0`, because it states MailFathom's own
license and a build must not be able to say otherwise. The label is only the claim a registry indexes; the terms
themselves are `/app/LICENSE` and `/app/NOTICE`, which arrive as part of the publish output the runtime stage copies.
`Host` fails its own publish when either is missing, so the image cannot be built without them. The third-party
notices that must accompany them are not in the image yet — see `THIRD_PARTY_LICENSES.md` and issue #191.

`io.artifacthub.package.logo-url` is the only label the image carries that OCI does not define. The specification has no
field for a project icon, so a listing reads a vendor label instead. It points at `assets/icon-1254.png` in the
repository, which is the asset the Helm chart's `icon` names as well, and it stays a URL because a label carries no
payload of its own.

## The schema

The image applies none and carries nothing that could — the verification script fails a Dockerfile that reintroduces a
schema tool. The reviewed artifact a released installation applies, and the step that runs it, belong to issue #126.
Until it ships, establishing the schema is an explicit operator action each deployment page describes.

The role that applies it needs more privilege than the service's: the schema installs the `vector` extension, which
PostgreSQL does not permit an ordinary role to create. That asymmetry is why the step is separate — grant the service a
role that can read and write rows and nothing more, and give the schema step a role that can do the rest. The Compose
deployment installs the extension during initialization, while a superuser is still connected, so neither of its roles
has to be one.

## Verification

```bash
bash scripts/verify-deployment-assets.sh   # reads the files: pins, privileges, rendering, schema and license guards
bash scripts/smoke-deployment.sh compose   # starts the real thing and asserts what only a running one can answer
```

The second one proves that the container runs unprivileged on a read-only root filesystem, reads its mounted
configuration, resolves its mounted secret, reaches the database, and then refuses an unrecognized schema — which is
where a deployment without a schema artifact stops. Neither script runs on a pull request: the `Deployment assets`
workflow that runs both, plus the two-architecture build and the same smoke against an ephemeral Kubernetes cluster, is
manual dispatch only.

## Where the deployments are

- [Docker Compose](deployment-compose.md)
- [Kubernetes and Helm](deployment-kubernetes.md)
