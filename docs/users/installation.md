# Installing MailFathom

MailFathom runs in three shapes, and each has one authoritative guide. This page is the decision: what each shape
assumes, what it is good for, and what every shape shares. Follow the linked guide for the commands; the guides do not
repeat each other and neither does this page.

**There is no published release yet.** No image is on any registry and no binary artifact is downloadable, so every
path below starts from a checkout of this repository. That is a statement about today, not about the design: the
deployment assets are written for released artifacts and will point at them once `0.1.0` ships. Until then, treat what
you install as a development build of an unreleased product.

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

- **PostgreSQL with the `vector` extension.** The synchronized mail, its indexes, and the raw message content all live
  there. The Compose deployment brings its own (`pgvector/pgvector`, PostgreSQL 17); the other shapes expect yours.
- **An IMAP account to synchronize** and its password or app password, provisioned as a
  [secret reference](../operations/secret-provisioning.md) rather than written into configuration.
- **An explicit schema step.** MailFathom never applies database migrations while starting: it verifies the schema and
  refuses to serve against one it does not recognize, so bringing a new build up *tells* you a migration is
  outstanding rather than silently applying one. The reviewed artifact a released installation will apply is still
  open — [issue #126](https://github.com/Krzysztof318/MailFathom/issues/126) tracks it — so today the step is your own,
  performed against the `mailfathom` database as the [Compose guide](../operations/deployment-compose.md#starting)
  describes.

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
