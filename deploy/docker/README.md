<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

**A brain for your mail — self-hosted, AI-native, and yours alone.**

![A chat client asked to show the latest mail, answered with a table of the ten most recent messages, their receipt times, and the moment the local copy was last synchronized](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/list-recent-emails.png)

*One question, answered from the local copy in an ordinary chat client. The `***` were blacked out by hand before the file entered a public repository — MailFathom redacts nothing on its way to a client.*

This page describes the container image. **[github.com/Krzysztof318/MailFathom](https://github.com/Krzysztof318/MailFathom) is the project**, and [the documentation site](https://krzysztof318.github.io/MailFathom/) is where everything below is stated in full.

MailFathom synchronizes your IMAP accounts into a PostgreSQL database you run, indexes that copy, and serves it to AI agents as read-only tools over the [Model Context Protocol](https://modelcontextprotocol.io/). Nothing depends on somebody else's service: the copy is yours, the database is yours, and the deployment is yours.

Two properties hold everywhere:

- **Reading is local.** A tool call answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it cannot change anything remotely.
- **Synchronization never writes to your mailbox.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client.

An agent gets three tools, and they are the whole surface:

| Tool | What it answers |
| --- | --- |
| `list_emails` | A page of the timeline, newest first, filtered by account, folder, sender, recipient, subject, date range, seen state, or attachment presence |
| `search_emails` | Ranked matches for a text query across subjects, participants, and body text, each with short extracts around what matched |
| `get_email_content` | Up to ten messages in full: normalized headers, plain-text body, optionally sanitized HTML, and attachment names, types, and sizes — never attachment bytes |

There is no write tool to enable. An agent cannot send, delete, move, or mark anything.

## Tags

| Tag | What it is |
| --- | --- |
| `<major>.<minor>.<patch>` | A release. It never moves. |
| `latest` | The newest release, and never a nightly |
| `nightly` | Built from `main`. **Not a release** — its schema can be ahead of any published migration, it has no upgrade path in either direction, and it is deleted once newer ones accumulate. [What a nightly build risks](https://krzysztof318.github.io/MailFathom/operations/container-image.html#what-a-nightly-build-risks) states the whole of it. |

The same manifest list is published to **`ghcr.io/krzysztof318/mailfathom`** and **`docker.io/krzysztof318/mailfathom`** under the same digest, so the registry you pull from is not part of what you have to trust. GHCR is the canonical reference; this one is the convenience mirror.

```bash
docker pull krzysztof318/mailfathom:latest
```

## What you need before it starts

- **PostgreSQL** with the [`vector`](https://github.com/pgvector/pgvector) extension available, and a role that owns its own database.
- **The schema, applied explicitly.** MailFathom never migrates a database while starting — it verifies the schema and refuses to serve against one it does not recognize. Each [release](https://github.com/Krzysztof318/MailFathom/releases) attaches an idempotent `mailfathom-schema-<version>.sql` for exactly this step.
- **A configuration file and your credentials.** Every credential is a *reference* to a file rather than a value in the configuration, so a configuration file is safe to review, diff, and back up.

## The supported Compose deployment

[`deploy/compose/`](https://github.com/Krzysztof318/MailFathom/tree/main/deploy/compose) in the repository is the shape to use for self-hosting on one machine: MailFathom, PostgreSQL, and a one-shot schema step that only ever runs when you ask for it.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom/deploy/compose

cp .env.example .env

mkdir -p secrets/mailfathom
chmod 700 secrets                # not mounted anywhere; this is what keeps other host users out
chmod 711 secrets/mailfathom     # bind-mounted, so uid 1654 needs to traverse it
openssl rand -base64 33 | tr -d '\n' > secrets/postgres-superuser-password
openssl rand -base64 33 | tr -d '\n' > secrets/mailfathom-database-password
chmod 444 secrets/postgres-superuser-password secrets/mailfathom-database-password

cp config/10-mailfathom.json.example config/10-mailfathom.json
$EDITOR config/10-mailfathom.json
chmod 644 config/10-mailfathom.json          # after the editor, which may rewrite it under your umask

docker compose up -d postgres    # creates the role, the database, and the vector extension
# apply mailfathom-schema-<version>.sql — the guide below has the command
docker compose up -d
```

[Deploying with Docker Compose](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html) is the full guide, including the schema command, the network boundary, upgrading, and backup. [Deploying on Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html) covers the Helm chart at `oci://ghcr.io/krzysztof318/charts/mailfathom`, which meets the Restricted Pod Security Standard.

## How the image runs

| Property | Value |
| --- | --- |
| Base | `mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra`, pinned to an exact patch version |
| Platforms | `linux/amd64` and `linux/arm64` |
| User | `1654`, the unprivileged `app` account — never root |
| Ports | `8080` for `/mcp`, which `McpEndpoint:Port` moves; `8081` for the probes, on a listener of its own |
| Writable paths | `/tmp` only, which a deployment supplies as a tmpfs or an `emptyDir` |
| Entrypoint | `dotnet /app/MailFathom.Host.dll` |
| Health check | None in the image. Startup, readiness, and liveness probes answer on `8081`. |

**Chiseled: there is no shell, no package manager, and no HTTP client**, and no tool that could apply a migration. The image carries the published application, `/app/LICENSE`, and `/app/NOTICE` — no SDK, no source tree, no build cache, no credential, and no certificate. `DOTNET_EnableDiagnostics=0` is set, so no diagnostic IPC socket is created. Both supported deployments run it on a read-only root filesystem with every Linux capability dropped.

**The container speaks plain HTTP and terminates no TLS.** A certificate belongs to the reverse proxy or the ingress in front of it. An MCP endpoint reached over plain HTTP hands its API key and every message it serves to anything on the network path — and the public scheme and host are read from any peer until you name your proxy in `ReverseProxy:TrustedProxies`. [Behind a TLS-terminating reverse proxy](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#behind-a-tls-terminating-reverse-proxy) states what that costs and how to close it.

## Verification

Every published image carries a signed build provenance statement tying the digest to the commit and the workflow that produced it:

```bash
gh attestation verify oci://ghcr.io/krzysztof318/mailfathom:latest --owner Krzysztof318
```

Before a release is pushed it is built, unit-tested, format-checked, proven against its migrations, run through the integration suite, started and required to report the version and revision its labels claim, and scanned by Trivy — which refuses to publish a release carrying a fixable `HIGH` or `CRITICAL` finding. [Verification](https://krzysztof318.github.io/MailFathom/operations/container-image.html#verification) records the whole order.

## Where to go next

| | |
| --- | --- |
| [Installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html) | Which deployment shape fits, and what each one needs |
| [Getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) | From an installed instance to a first successful tool call |
| [Using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html) | What the three tools do, what they bound, and how to read a failure |
| [Configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html) | Every user-settable option, its default, and whether changing it needs a restart |
| [The MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html) | Authentication, TLS, browser origins, client certificates, rate limits |
| [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html) | This page's subject, in full |
| [Changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html) | What each release promises across the four public surfaces |

## Security and license

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. The image carries `LICENSE` and `NOTICE` beside the binaries and declares `org.opencontainers.image.licenses`. Every third-party component it ships beside is registered in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). The software is provided without warranty and without contributor liability, under sections 7 and 8 of that license.
