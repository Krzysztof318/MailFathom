<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

**A brain for your mail — self-hosted, AI-native, and yours alone.**

![A chat client asked to show the latest mail, answered with a table of the ten most recent messages, their receipt times, and the moment the local copy was last synchronized](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/list-recent-emails.png)

*One question, answered from the local copy in an ordinary chat client. The `***` were blacked out by hand before the file entered a public repository — until you turn `SensitiveContent` on, MailFathom redacts nothing on its way to a client.*

This page describes the container image. **[github.com/Krzysztof318/MailFathom](https://github.com/Krzysztof318/MailFathom) is the project**, and [the documentation site](https://krzysztof318.github.io/MailFathom/) is where everything below is stated in full.

MailFathom synchronizes your IMAP accounts into a PostgreSQL database you run, indexes that copy, and serves it to AI agents as tools over the [Model Context Protocol](https://modelcontextprotocol.io/). Nothing depends on somebody else's service: the copy is yours, the database is yours, and the deployment is yours.

Two properties hold everywhere:

- **Reading is local.** A read answers from your copy and never contacts a mail server, so it is fast, it works while the server is down, and it changes nothing remotely.
- **Synchronization never writes to your mailbox.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client. The tools that write do so behind grants of their own, and they are named below.

An agent gets twenty-one tools, and they are the whole surface. Five of them read your mail:

| Tool | What it answers |
| --- | --- |
| `list_accounts` | Which mailboxes this deployment serves, each with the readable name you gave it and how current its local copy is — the tool an agent calls first, so it knows what to narrow the others to |
| `list_emails` | A page of the timeline, newest first, filtered by account, folder, sender, recipient, subject, date range, seen state, or attachment presence |
| `search_emails` | Ranked matches for a text query across subjects, participants, and body text, each with short extracts around what matched — ranked lexically, and by embedding similarity beside it once an embedding model is configured |
| `get_email_content` | Up to ten messages in full: normalized headers, plain-text body, optionally sanitized HTML, and every attachment by name, type, and size — plus a short-lived signed link that fetches each file when a call asks for one, which is off by default and needs a public address to be declared |
| `ask_mail` | A question answered from the mail a chat model looks up while answering, citing the identifiers of every message it drew on |

The first four are always there. `ask_mail` needs a chat model and an embedding model you configure and point at, so a deployment with neither does not advertise it at all.

Eight tools reach a mail server. `set_mail_flags` marks one message read or unread, stars or unstars it, and adds, removes, or replaces its keywords; `send_email` sends one message, as an account you configured to send, to the addresses the call names; `reply_to_email` and `forward_email` answer one message this deployment already holds, taking the message's own identifier and the new words while the addressing, the subject, the threading, the quotation, and a forward's attachments are read from your stored copy; `save_draft`, `update_draft`, and `delete_draft` write a message into your own Drafts folder, replace it, and take it back out without sending anything; and `send_draft` sends the message one of those drafts holds. None of them waits for mail to be delivered — each writes what was asked for down and answers with a record identity, and the account's next run issues it. The three draft tools are the one place a call waits on your mail server, for the single round trip that puts the copy into your Drafts folder after the draft itself is durable. A flag change is reversible with the call that would have made it; a send is not reversible by anything, which is why the three sending tools announce themselves as destructive and why their answer says `queued` rather than `sent`. Nothing a caller sends decides who a message is from: it is sent from the address that account's own configuration declares, and an account with no delivery configuration cannot send at all. Each tool is offered only to a credential granted its own permission — `mailfathom.mail.flags.write`, `mailfathom.mail.drafts.write`, and `mailfathom.mail.send` — none of which reading mail carries and none of which implies another; a reply, a forward, and a draft that answers stored mail need `mailfathom.mail.read` beside the grant on the tool, because an answer is derived from the message it answers. The drafting grant reaches `save_draft`, `update_draft`, and `delete_draft` and cannot send: `send_draft` is admitted by the sending grant, so a credential granted drafting alone prepares mail for you to send and sends none of it.

Two more answer for a send rather than performing one, behind the same sending grant. `get_outgoing_email` reports what became of a message the caller queued — its state, its delivery attempts, what a mail server said about each recipient, and the code it stopped on — so an agent unsure whether a send went through reads the record instead of sending a second copy; `cancel_outgoing_email` stops one during the seconds before it leaves and refuses once transmission has begun. Neither enumerates, and each is confined to what the calling credential itself queued.

The other six are MailFathom's own contact book — `list_contacts`, `get_contact`, `create_contact`, `update_contact`, `delete_contact`, and `promote_contact` — which record the people you write down and the addresses each of them uses, and take on the ones this deployment collected from arriving mail. They are offered to a credential granted them, which every credential is until you narrow its entry, and both `update_contact` and `delete_contact` announce themselves as destructive — an amendment drops whatever the caller left out of the record, and an erasure cannot be undone.

`set_mail_flags`, the three sending tools, and the four draft tools are the whole of what a tool can ask a mail server for. An agent cannot delete or move mail, and the contact tools and the two over a queued send reach no mail server at all.

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

[`deploy/compose/`](https://github.com/Krzysztof318/MailFathom/tree/main/deploy/compose) in the repository is the shape to use for self-hosting on one machine: MailFathom and PostgreSQL, with the schema applied as a step you take yourself. Nothing in that deployment applies one, so bringing the stack up after a version change tells you a migration is outstanding rather than running it.

```bash
git clone https://github.com/Krzysztof318/MailFathom.git
cd MailFathom/deploy/compose

cp .env.example .env
# Then set both of these in .env, in one edit. Left at their defaults they name mailfathom:local and build the
# checkout, which is deliberate — nothing is ever pulled by accident — so this is the step that runs this image.
#   MAILFATHOM_IMAGE=docker.io/krzysztof318/mailfathom:<version>   # an immutable tag, never latest and never a nightly
#   MAILFATHOM_PULL_POLICY=missing

mkdir -p secrets/mailfathom
chmod 700 secrets                # not mounted anywhere; this is what keeps other host users out
chmod 711 secrets/mailfathom     # bind-mounted, so uid 1654 needs to traverse it
chmod 755 config                 # bind-mounted and listed, so it needs read too — a clone's umask decides this
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

[Deploying with Docker Compose](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html) is the full guide, including the schema command, the network boundary, upgrading, and backup. [Deploying on Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html) covers the Helm chart at `oci://ghcr.io/krzysztof318/charts/mailfathom`, which meets the Restricted Pod Security Standard. [Deploying with Podman Quadlet](https://krzysztof318.github.io/MailFathom/operations/deployment-quadlet.html) runs the same single-machine stack as rootless systemd units, which is what lets this image take its secrets as encrypted systemd credentials instead of as plaintext files.

## How the image runs

| Property | Value |
| --- | --- |
| Base | `mcr.microsoft.com/dotnet/aspnet:10.0.11-noble-chiseled-extra`, pinned to an exact patch version |
| Platforms | `linux/amd64` and `linux/arm64` |
| User | `1654`, the unprivileged `app` account — never root |
| Ports | `8080` for `/mcp`, which `McpEndpoint:Port` moves; `8081` for the probes, on a listener of its own, which `HealthEndpoints:Port` moves |
| Writable paths | `/tmp` only, which a deployment supplies as a tmpfs or an `emptyDir` |
| Entrypoint | `dotnet /app/MailFathom.Host.dll` |
| Health check | None in the image. Startup, readiness, and liveness probes answer on `8081`. |

**Chiseled: there is no shell, no package manager, and no HTTP client**, and no tool that could apply a migration. The image carries the published application, MailFathom's own client under `/app/wwwroot`, `/app/LICENSE`, and `/app/NOTICE` — no SDK, no source tree, no build cache, no credential, and no certificate. `DOTNET_EnableDiagnostics=0` is set, so no diagnostic IPC socket is created. Both supported deployments run it on a read-only root filesystem with every Linux capability dropped.

**The container speaks plain HTTP and terminates no TLS.** A certificate belongs to the reverse proxy or the ingress in front of it. An MCP endpoint reached over plain HTTP hands its API key and every message it serves to anything on the network path — and the public scheme and host are read from any peer until you name your proxy in `ReverseProxy:TrustedProxies`. [Behind a TLS-terminating reverse proxy](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#behind-a-tls-terminating-reverse-proxy) states what that costs and how to close it.

**MailFathom's own client is inside the image and is not served.** The bundle under `/app/wwwroot` is about 230 kB of the image's size and answers nothing until a deployment writes `ClientEndpoint__Application__Enabled=true`, which needs `ClientEndpoint__Enabled` beside it and serves the page from that surface's own listeners. Serving it changes no authorization — a browser is an untrusted client wherever it was served from, and whatever `ClientEndpoint:Authentication` requires is still required of it — and it is refused over a clear-text socket unless `ClientEndpoint__Application__AllowClearText=true` states that the proxy in front of this container terminates TLS. [Serving the client from the deployment](https://krzysztof318.github.io/MailFathom/operations/client-endpoint.html#serving-the-client-from-the-deployment) is the page.

## Verification

Every published image carries a signed build provenance statement tying the digest to the commit and the workflow that produced it:

```bash
gh attestation verify oci://ghcr.io/krzysztof318/mailfathom:latest --repo Krzysztof318/MailFathom
```

Before a release is pushed it is built, unit-tested, format-checked, proven against its migrations, run through the integration suite, started and required to report the version and revision its labels claim, and scanned by Trivy — which refuses to publish a release carrying a fixable `HIGH` or `CRITICAL` finding. [Verification](https://krzysztof318.github.io/MailFathom/operations/container-image.html#verification) records the whole order.

## Where to go next

| | |
| --- | --- |
| [Installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html) | Which deployment shape fits, and what each one needs |
| [Getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) | From an installed instance to a first successful tool call |
| [Using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html) | What each tool does, what they bound, and how to read a failure |
| [Configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html) | Every user-settable option, grouped by what it configures, with its default and whether changing it needs a restart |
| [The MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html) | Authentication, TLS, browser origins, client certificates, rate limits |
| [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html) | This page's subject, in full |
| [Changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html) | What each release promises across the four public surfaces |

## Security and license

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. The image carries `LICENSE` and `NOTICE` beside the binaries and declares `org.opencontainers.image.licenses`. Every third-party component it ships beside is registered in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). The software is provided without warranty and without contributor liability, under sections 7 and 8 of that license.
