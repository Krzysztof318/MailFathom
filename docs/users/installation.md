# Installing MailFathom

<!-- describes: deploy/** -->

MailFathom runs in three shapes, and each has one authoritative guide. This page is the decision: what each shape
assumes, what it is good for, and what every shape shares. Follow the linked guide for the commands; the guides do not
repeat each other and neither does this page.

**There is no published release yet.** No release image and no binary artifact exist, so every path below starts from a
checkout of this repository. That is a statement about today, not about the design: the deployment assets are written
for released artifacts and will point at them once `0.1.0` ships. Until then, treat what you install as a development
build of an unreleased product.

What does exist is the nightly channel: `ghcr.io/krzysztof318/mailfathom:nightly` and the `-nightly.<n>-<short revision>` tag of each
night's build, published from `main` when it has moved. **A nightly is not a release and is a poor place to keep data
you care about** — its schema can be ahead of any migration, it has no upgrade path in either direction, and it is
deleted once thirty newer ones exist. [What a nightly build risks](../operations/container-image.md#what-a-nightly-build-risks)
states the whole of it before you choose one. The package is public, so pulling one needs no GHCR login.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
```

## Choosing a shape

| Shape | Choose it when | Guide |
| --- | --- | --- |
| **Docker Compose** | You self-host on one machine and want the database, the network boundary, and the secret mounts arranged for you | [Deploying with Docker Compose](../operations/deployment-compose.md) |
| **Kubernetes with Helm** | You operate a cluster and bring your own PostgreSQL and Secret management | [Deploying to Kubernetes](../operations/deployment-kubernetes.md) |
| **Native process** | You run services under systemd without a container runtime, and want secrets delivered as systemd credentials | [Below](#native-process), then [secret provisioning](../operations/secret-provisioning.md#native-systemd-service) |

Docker Compose is the recommended first installation. It is the only shape that provisions PostgreSQL for you —
`compose.yaml` creates the role, the database, and the `vector` extension on first start — and its defaults publish
both ports on loopback, so nothing is reachable from another machine until you decide it should be.

The Helm chart deliberately installs neither a database nor a Secret. It needs an image reference, a PostgreSQL server
with the `vector` extension, and a Secret carrying the credentials; [what you supply](../operations/deployment-kubernetes.md#what-you-supply)
lists all three before the install command.

## What every shape needs

- **Linux.** It is the only platform this project officially supports, and everything below assumes it: the image is
  built for `linux/amd64` and `linux/arm64`, the native shape is a systemd service with systemd credentials, and TLS
  goes through the system OpenSSL. **MailFathom may well run on Windows — it is ordinary .NET — but expect problems
  and a setup of your own**: credential provisioning, TLS parameters, and file-permission expectations all differ
  there, nothing in this repository is verified against it, and a defect that reproduces only on Windows is not one
  this project can act on today.
- **PostgreSQL with the `vector` extension.** The synchronized mail, its indexes, and the raw message content all live
  there. The Compose deployment brings its own (`pgvector/pgvector`, PostgreSQL 17); the other shapes expect yours.
- **An IMAP account to synchronize** and its password or app password, provisioned as a
  [secret reference](../operations/secret-provisioning.md) rather than written into configuration.
- **OpenSSL 3.0 or later**, because MailFathom connects to the mail server over TLS and .NET hands every handshake to
  the system library. **1.1.1 is the floor below which nothing runs at all**: .NET 10 requires it on Unix and
  [fails to start](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement)
  without it. **Between the two, MailFathom may work and may not.** 1.1.1 has been out of upstream support since
  September 2023, nothing here is verified against it, and a defect that reproduces only there is not one this project
  can act on. Every current distribution ships 3.x, so this is a constraint on old machines rather than on new ones.

  The library's *security policy* is part of the installation too, not a detail of it: an OpenSSL that considers a
  server's cipher suite or key size too weak ends the connection before any credential is sent, and reports it as an
  authentication failure. **An installation that configures nothing runs at that full-strength policy and negotiates
  the newest TLS both ends support**, which is what almost every mail server wants. One that does not clear the policy
  is reached by opting in to a relaxed one — an OpenSSL configuration file named in the environment, which
  [the platform TLS policy](../operations/platform-tls-policy.md) covers with a sample file. It is an exception you
  choose per deployment, never a default, and nothing in MailFathom's own configuration can substitute for it.
- **An explicit schema step.** MailFathom never applies database migrations while starting: it verifies the schema and
  refuses to serve against one it does not recognize, so bringing a new build up *tells* you a migration is
  outstanding rather than silently applying one. What you apply is one idempotent SQL file — a release attaches
  `mailfathom-schema-<version>.sql`, and a checkout produces the same file with `scripts/build-schema-artifact.sh`.
  Read it, back the database up, and run it with any PostgreSQL client;
  [applying the database schema](../operations/database-schema.md) states the privileges it needs and what each
  startup failure means.

## Native process

The publish output is self-contained in licensing terms — it carries `LICENSE` and `NOTICE` beside the binaries — but
there is no packaged unit file or installer yet, so a native installation is assembled by hand:

```bash
dotnet publish src/Host/Host.csproj --configuration Release --output /opt/mailfathom
```

Build with the SDK pinned in `global.json`. The process is then an ordinary ASP.NET Core service:

- Configuration arrives through `appsettings.json` beside the binaries, a deployment-provisioned JSON file or
  directory named by [`ConfigurationSources`](../operations/configuration-sources.md), command-line arguments, or
  environment variables.
- Credentials arrive as systemd credentials: `LoadCredential=` in the unit, `systemd-credential:` references in the
  configuration. [Secret provisioning](../operations/secret-provisioning.md#native-systemd-service) shows the unit
  fragment, including the encrypted-at-rest variant and the core-dump limit worth setting alongside it.
- The application listener binds the address `ASPNETCORE_URLS` or your Kestrel configuration names; the health probes
  answer on their own port, `8081` by default. [Health endpoints](../operations/health-endpoints.md) records how to
  move or disable that listener.
- PostgreSQL, the `vector` extension, and the schema step are yours, exactly as they are under Kubernetes.
- A mail server whose TLS parameters the machine's own OpenSSL refuses is reached by naming an OpenSSL configuration
  file in the service's environment, which is a pre-start concern no MailFathom setting can replace.
  [The platform TLS policy](../operations/platform-tls-policy.md) has the sample file and the unit fragment.

## Verifying any installation

An installation is done when the probes answer and the log shows a healthy start:

```bash
curl -fsS http://127.0.0.1:8081/started   # startup gates have passed
curl -fsS http://127.0.0.1:8081/health    # ready, including the database
curl -fsS http://127.0.0.1:8081/alive     # the process itself
```

A refusal at startup is designed to name the setting that caused it — a missing secret reference, a pending migration,
an unsafe transport combination — so the log is the first place to look, and the message quotes the configuration key
to fix.

Continue with [getting started](getting-started.md): provisioning the secrets, configuring the first account, and
connecting an MCP client.
