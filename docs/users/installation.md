# Installing MailFathom

<!-- describes: deploy/** -->

MailFathom runs in four shapes, and each has one authoritative guide. This page is the decision: what each shape
assumes, what it is good for, and what every shape shares. Follow the linked guide for the commands; the guides do not
repeat each other and neither does this page.

**A release publishes an image, the chart, the schema script, and the administrative command.** The image is
`ghcr.io/krzysztof318/mailfathom:<version>` and `docker.io/krzysztof318/mailfathom:<version>` — one manifest list under
one digest, so the registry to pull from is whichever your environment already reaches — with `latest` on the newest
release's digest in both. The chart is `oci://ghcr.io/krzysztof318/charts/mailfathom` at the same version. Each release
also attaches `mailfathom-schema-<version>.sql` and its checksum, which is the schema step below, and one `mfctl`
binary per platform with a checksum file covering all of them —
[getting the command](../operations/admin-endpoint.md#getting-the-command) is where that one is picked up, including
the [install script](../operations/admin-endpoint.md#on-linux-with-the-install-script) that does it in one line on
Linux. Both packages are public, so pulling one needs no login.

**`<version>` is the release you are installing**, and
[the releases page](https://github.com/Krzysztof318/MailFathom/releases) is where the current one is named. Pin it
rather than tracking `latest`: an immutable tag is what makes a deployment reproducible and an upgrade a decision,
which matters here because a new release can require a schema step before it will serve.

**There is no binary artifact for the service itself**, so the native shape below is published from a checkout, and so
are the Compose deployment and the Quadlet units, whose files live here and are versioned with the code that reads
them. `mfctl` is the exception, and it is a client rather than the service: it runs on the machine you administer
*from*.

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

**To try MailFathom before choosing any of this, `scripts/quick-start-compose.sh` prepares the Compose shape for you** —
it asks where the mailbox lives and performs every step
[deploying with Docker Compose](../operations/deployment-compose.md#trying-it-first-with-one-command) otherwise asks you
to type. What it produces is a deployment to evaluate with rather than one to depend on: it serves the machine it runs
on over plain HTTP, keeps its credentials in files under the checkout, and backs nothing up. The choice below is the one
it does not make for you, and it stays yours to make afterwards.

## Choosing a shape

| Shape | Choose it when | Guide |
| --- | --- | --- |
| **Docker Compose** | You self-host on one machine and want the database, the network boundary, and the secret mounts arranged for you | [Deploying with Docker Compose](../operations/deployment-compose.md) |
| **Podman Quadlet** | You self-host on one machine, run Podman rootless, and want the container's secrets to be encrypted systemd credentials rather than plaintext files | [Deploying with Podman Quadlet](../operations/deployment-quadlet.md) |
| **Kubernetes with Helm** | You operate a cluster and bring your own Secret management | [Deploying to Kubernetes](../operations/deployment-kubernetes.md) |
| **Native process** | You run services under systemd without a container runtime, and want secrets delivered as systemd credentials | [Below](#native-process), then [secret provisioning](../operations/secret-provisioning.md#native-systemd-service) |

Docker Compose is the recommended first installation. It provisions PostgreSQL for you — `compose.yaml` creates the
role, the database, and the `vector` extension on first start — and its defaults publish both ports on loopback, so
nothing is reachable from another machine until you decide it should be.

The Podman Quadlet is that same stack expressed as systemd units, and it provisions PostgreSQL the same way. What it
buys is the one thing no Compose file can reach: a `.container` file is a systemd unit source, so the deployment's
secrets are `LoadCredentialEncrypted=` credentials — ciphertext at rest, bound to the machine, decrypted only as the
unit starts. What it asks in return is Podman rather than Docker, a rootless user, systemd 258 or later, and a decision
about SELinux that its guide states before the first command.

The Helm chart provisions one too, as a StatefulSet on a persistent claim, and turns it off for a deployment that has a
server of its own. It creates no Secret, deliberately, so it needs an image reference and a Secret carrying the
credentials; [what you supply](../operations/deployment-kubernetes.md#what-you-supply) covers both, and the trade-off
between the deployed database and one you operate, before the install command.

The native process is the shape that brings no database at all.

**Every shape can serve MailFathom's own client, and none of them does until you say so.** The bundle travels inside
the container image, so serving it is a setting rather than anything to install and no shape gains a second process for
it: `client.enabled` in the Helm chart, `MAILFATHOM_CLIENT=true` in Compose, or the two adjacent `Environment=` lines in
the Quadlet unit. Each needs the client endpoint on beside it, because the page is served on that surface's listeners,
and each is refused over a clear-text socket until the deployment states that something in front of it terminates TLS.
[Serving the client from the deployment](../operations/client-endpoint.md#serving-the-client-from-the-deployment) is
the page.

What every shape *does* serve is the client **surface** under `/api/client`, which is an endpoint of its own and is
what the next client will call. Signing in to it needs a credential no shape provisions on its own: there is no
self-service and no default, so a username and password are written over
[the administrative endpoint](../operations/admin-endpoint.md#owner-credentials) or they do not exist.

## What every shape needs

- **Linux.** It is the only platform this project officially supports, and everything below assumes it: the image is
  built for `linux/amd64` and `linux/arm64`, the native shape is a systemd service with systemd credentials, and TLS
  goes through the system OpenSSL. **MailFathom may well run on Windows — it is ordinary .NET — but expect problems
  and a setup of your own**: credential provisioning, TLS parameters, and file-permission expectations all differ
  there, nothing in this repository is verified against it, and a defect that reproduces only on Windows is not one
  this project can act on today.
- **PostgreSQL with the `vector` extension.** The synchronized mail, its indexes, and — unless you choose otherwise —
  the raw message content all live there. The Compose deployment, the Quadlet units, and the Helm chart bring their own
  (`pgvector/pgvector`, PostgreSQL 18); a native process expects yours, and the chart uses yours when you ask it to.
- **An S3-compatible bucket, only if you want the message payloads out of the database.** It is off, and a deployment
  that never mentions it is the ordinary one. What it changes is where the raw MIME of each message is written; the
  metadata, the indexes, the embeddings, and every job still run through PostgreSQL either way. MailFathom's own process
  runs no object store, and the endpoint needs an `https` address and a credential of its own — an access key identifier
  and its secret, both provisioned as [secret references](../operations/secret-provisioning.md) like every other
  credential. The endpoint can be one you rent, one you already run, or one the deployment starts beside MailFathom:
  each of the three shapes can run a single-node store for you, off unless you ask. Its bucket exists before the first
  start either way; nothing here creates one.
  [Choosing where message content lives](#choosing-where-message-content-lives) is the rest of it.
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
  shape, and [`DataEncryption`](../operations/configuration-runtime.md#dataencryption) the section that
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
dotnet publish backend/src/Host/Host.csproj --configuration Release --output /opt/mailfathom
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

  **`systemd-creds encrypt` seals that `.cred` file against this machine** — by default against its TPM2 chip and its
  `/var/lib/systemd/` together — so the file opens here and on no other host, and it is not a backup of what it holds.
  Run the command on the machine that will read it, back the key's own base64 up with the database, and encrypt it
  again on a replacement machine rather than copying the sealed file across.
  [What an encrypted credential is bound to](../operations/secret-provisioning.md#what-an-encrypted-credential-is-bound-to)
  states the whole binding, including which flag makes the chip a requirement rather than a preference and why a
  firmware update does not invalidate the credential.
- [Where each surface is served](../operations/configuration-endpoints.md#where-each-surface-is-served) is stated by
  each surface's own section. `McpEndpoint:BindAddress` and `McpEndpoint:Port` bind the protocol surface, `0.0.0.0:8080`
  by default, in clear text unless you configure otherwise. `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, and
  `Kestrel:Endpoints` are refused at startup, so an address you state is never one the process quietly ignores. The
  health probes answer on their own port, `8081` by default;
  [health endpoints](../operations/health-endpoints.md) records how to move or disable that listener.
- PostgreSQL, the `vector` extension, and the schema step are yours, exactly as they are under Kubernetes.
- A mail server whose TLS parameters the machine's own OpenSSL refuses is reached by naming an OpenSSL configuration
  file in the service's environment, which is a pre-start concern no MailFathom setting can replace.
  [The platform TLS policy](../operations/platform-tls-policy.md) has the sample file and the unit fragment.

## Choosing where message content lives

Two stores can hold the raw MIME of your mail, and which one is a decision you take once per deployment rather than per
message. Configuring nothing keeps every payload in PostgreSQL beside the metadata, which is the shape everything above
describes. Setting `ContentStorage:Backend` to `ObjectStorage` writes new payloads into an S3-compatible bucket instead
— useful when the mail is larger than the database you want to operate, or when object storage is what your platform
already backs up well. Everything else stays where it was.

Each shape carries the setting in its own idiom: `contentStorage` in the Helm chart's values,
`MAILFATHOM_CONTENT_STORAGE` and the variables beside it in Compose's `.env`, and the `ContentStorage` lines in the
Quadlet unit.
[`ContentStorage`](../operations/configuration-runtime.md#contentstorage) is the reference for every key and its bounds.

**And each shape can run the store as well as point at one.** An operator who wants payload bytes out of PostgreSQL and
does not already run object storage would otherwise have nothing to name, so every shape carries a single-node
[Silo](https://github.com/pgsty/silo) beside the product — a switch in the chart's values, a Compose profile, an extra
Quadlet unit — off in every default, and one node on one volume rather than anything replicated. It answers over TLS
with a certificate you supply, its administrative console is not served, and the bucket and the access key MailFathom
presents are created once by you rather than by any of it.
[Kubernetes](../operations/deployment-kubernetes.md#running-the-object-store-beside-mailfathom),
[Compose](../operations/deployment-compose.md#running-an-object-store-beside-mailfathom), and
[Quadlet](../operations/deployment-quadlet.md#running-an-object-store-beside-mailfathom) each state what that takes.

Two things about it are worth knowing before you choose rather than after.

**Switching is a move, not a setting.** The value decides only where the *next* payload is written. Every stored message
records which store holds its own content, so turning the bucket on leaves everything already in the database exactly
where it is and readable, and turning it back off re-encodes nothing. Carrying the mail you already have into the bucket
is something you run, with its own controls and its own progress — [moving stored content into the
bucket](../operations/moving-stored-content.md) is that operation. Until it has finished, the deployment needs both
stores, and pointing it away from one it still has mail in leaves it unready rather than leaving that mail unreadable.

**A database backup stops being a whole backup.** Once payloads are in the bucket, the rows point at objects there, so
your backup is the database and the bucket together — and a restore brings the database back first and the bucket
second, because the other order can let the reclamation sweep delete objects the database has not caught up to yet. Each
deployment page states the order for its own shape:
[Kubernetes](../operations/deployment-kubernetes.md#what-you-now-back-up-and-in-which-order),
[Compose](../operations/deployment-compose.md#with-content-in-a-bucket-the-dump-above-is-only-half-of-it), and
[Quadlet](../operations/deployment-quadlet.md#backup-and-what-survives-removal).

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
