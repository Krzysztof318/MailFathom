# Installing MailFathom

<!-- describes: deploy/** -->

MailFathom runs in three shapes, and each has one authoritative guide. This page is the decision: what each shape
assumes, what it is good for, and what every shape shares. Follow the linked guide for the commands; the guides do not
repeat each other and neither does this page.

**A release publishes an image, the chart, the schema script, and the administrative command.** The image is
`ghcr.io/krzysztof318/mailfathom:<version>` and `docker.io/krzysztof318/mailfathom:<version>` — one manifest list under
one digest, so the registry to pull from is whichever your environment already reaches — with `latest` on the newest
release's digest in both. The chart is `oci://ghcr.io/krzysztof318/charts/mailfathom` at the same version. Each release
also attaches `mailfathom-schema-<version>.sql` and its checksum, which is the schema step below, and one `mfctl`
binary per platform with a checksum file covering all of them —
[getting the command](../operations/admin-endpoint.md#getting-the-command) is where that one is picked up, including
the winget package the Windows binaries are also offered as. Both packages are public, so pulling one needs no login.

**`<version>` is the release you are installing**, and
[the releases page](https://github.com/Krzysztof318/MailFathom/releases) is where the current one is named. Pin it
rather than tracking `latest`: an immutable tag is what makes a deployment reproducible and an upgrade a decision,
which matters here because a new release can require a schema step before it will serve.

**There is no binary artifact for the service itself**, so the native shape below is published from a checkout, and so
is the Compose deployment, whose `compose.yaml` lives here and is versioned with the code that reads it. `mfctl` is the
exception, and it is a client rather than the service: it runs on the machine you administer *from*.

Beside the release runs the nightly channel: `ghcr.io/krzysztof318/mailfathom:nightly` — or
`docker.io/krzysztof318/mailfathom:nightly`, which is the same digest in the other registry — and the
`-nightly.<n>-<short revision>` tag of each night's build, published from `main` when it has moved. **A nightly is not a release and is a poor place to keep data
you care about** — its schema can be ahead of any migration, it has no upgrade path in either direction, and it is
deleted once thirty newer ones exist. [What a nightly build risks](../operations/container-image.md#what-a-nightly-build-risks)
states the whole of it before you choose one.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom
```

## Choosing a shape

| Shape | Choose it when | Guide |
| --- | --- | --- |
| **Docker Compose** | You self-host on one machine and want the database, the network boundary, and the secret mounts arranged for you | [Deploying with Docker Compose](../operations/deployment-compose.md) |
| **Kubernetes with Helm** | You operate a cluster and bring your own Secret management | [Deploying to Kubernetes](../operations/deployment-kubernetes.md) |
| **Native process** | You run services under systemd without a container runtime, and want secrets delivered as systemd credentials | [Below](#native-process), then [secret provisioning](../operations/secret-provisioning.md#native-systemd-service) |

Docker Compose is the recommended first installation. It provisions PostgreSQL for you — `compose.yaml` creates the
role, the database, and the `vector` extension on first start — and its defaults publish both ports on loopback, so
nothing is reachable from another machine until you decide it should be.

The Helm chart provisions one too, as a StatefulSet on a persistent claim, and turns it off for a deployment that has a
server of its own. It creates no Secret, deliberately, so it needs an image reference and a Secret carrying the
credentials; [what you supply](../operations/deployment-kubernetes.md#what-you-supply) covers both, and the trade-off
between the deployed database and one you operate, before the install command.

The native process is the shape that brings no database at all.

## What every shape needs

- **Linux.** It is the only platform this project officially supports, and everything below assumes it: the image is
  built for `linux/amd64` and `linux/arm64`, the native shape is a systemd service with systemd credentials, and TLS
  goes through the system OpenSSL. **MailFathom may well run on Windows — it is ordinary .NET — but expect problems
  and a setup of your own**: credential provisioning, TLS parameters, and file-permission expectations all differ
  there, nothing in this repository is verified against it, and a defect that reproduces only on Windows is not one
  this project can act on today.
- **PostgreSQL with the `vector` extension.** The synchronized mail, its indexes, and the raw message content all live
  there. The Compose deployment and the Helm chart bring their own (`pgvector/pgvector`, PostgreSQL 18); a native
  process expects yours, and the chart uses yours when you ask it to.
- **An IMAP account to synchronize** and its password or app password, provisioned as a
  [secret reference](../operations/secret-provisioning.md) rather than written into configuration.
- **A data-encryption key, if any mailbox authenticates with OAuth.** MailFathom seals the refresh tokens it stores
  under one key the whole deployment shares, so generate it once before the first start and provision it like any other
  secret:

  ```bash
  openssl rand -base64 32
  ```

  It is `-base64 32`, not the `-base64 33` beside it for the database passwords: the value has to decode to exactly 32
  bytes and a longer one is refused at startup. **Back it up with the database and never regenerate it** — the key is
  not in the database, and losing it means re-authorizing every mailbox.
  [Secret provisioning](../operations/secret-provisioning.md#the-data-encryption-key) covers where it goes in each
  shape, and [the configuration reference](../operations/configuration-reference.md#dataencryption) the section that
  points at it. A deployment whose mailboxes all authenticate with a password needs none, and starts without one.
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
  fragment, including the encrypted-at-rest variant and the core-dump limit worth setting alongside it. The
  data-encryption key is one of them, provisioned no differently from a mailbox password:

  ```ini
  [Service]
  LoadCredentialEncrypted=mailfathom-data-key:/etc/mailfathom/mailfathom-data-key.cred
  ```

  ```json
  {
    "DataEncryption": {
      "ActiveKeyId": "2026-08",
      "Keys": [
        {
          "KeyId": "2026-08",
          "Material": {
            "Name": "mailfathom-data-key",
            "SecretReference": "systemd-credential:mailfathom-data-key"
          }
        }
      ]
    }
  }
  ```

  `KeyId` is stored beside every value the key seals, so it is chosen once and never edited afterwards. Leave both out
  when no account authenticates with OAuth.
- [Where each surface is served](../operations/configuration-reference.md#where-each-surface-is-served) is stated by
  each surface's own section. `McpEndpoint:BindAddress` and `McpEndpoint:Port` bind the protocol surface, `0.0.0.0:8080`
  by default, in clear text unless you configure otherwise. `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, and
  `Kestrel:Endpoints` are refused at startup, so an address you state is never one the process quietly ignores. The
  health probes answer on their own port, `8081` by default;
  [health endpoints](../operations/health-endpoints.md) records how to move or disable that listener.
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
