# MailFathom user guide

This is the documentation for people who install, configure, operate, and use MailFathom. It is a guided path rather
than a second copy of the reference material: each step links into the operations and feature pages where the full
contract lives, so nothing here goes stale on its own. Contributor and agent documentation is separate, under the
repository root and [`docs/`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/README.md) generally.

## What MailFathom is

MailFathom is a self-hosted service that synchronizes mail from your IMAP accounts into a local PostgreSQL copy,
indexes it for search, and serves it to AI agents as read-only tools over the
[Model Context Protocol](https://modelcontextprotocol.io/). An agent connected to it can list, read, and search your
mail; it cannot send, delete, move, or mark anything, because no such tool exists on the surface.

Two properties hold everywhere and are worth knowing before anything is installed:

- **Reading is local.** A tool call answers from the local copy and never contacts a mail server, so it is fast, works
  while the server is down, and cannot change anything remotely. Every result states how fresh the local copy is.
- **Retrieval is read-only.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied still
  shows as unread in your mail client until you read it there. What can write to your mailbox is what you configured to:
  a mail rule whose action moves, copies, deletes, or marks a message read, and the spam actions that file junk and mark
  it read. Both are off until you turn them on.

## The state of the release

`0.6.0` is the current release. The container image is published to both registries, the Helm chart is published, and
the schema file you apply and the `mfctl` binaries are attached to the GitHub release — so an installation starts from
a versioned artifact rather than from a checkout. Where a page describes something that arrives later than `0.6.0`, it
says so and names the release, rather than describing it as though you could already download it.

## The path

1. **[Choose and perform an installation](installation.md)** — which deployment shape fits, what each needs, and where
   its full guide is.
2. **[Getting started](getting-started.md)** — from an installed instance to a synchronized mailbox and a first
   successful tool call, including secrets, the schema step, health verification, and connecting an MCP client.
3. **[Configuring a mailbox at your provider](mailbox-providers.md)** — the address, port, and credential kind each
   popular mail service publishes, and what each one does differently once synchronization runs.
4. **[Connecting the chat client you already use](mcp-clients.md)** — where the dialog is in each popular client, which
   address kind it needs, and which of them cannot present an API key at all.
5. **[Using the tools](usage.md)** — what `list_emails`, `search_emails`, `get_email_content`, and `ask_mail` do, what
   they deliberately bound, and how to read a failure.
6. **[Administering your deployment](administering.md)** — the `mfctl` command: what it is for, signing in to a
   deployment from your own machine, and what it cannot do yet.
7. **[Configuration reference](../operations/configuration-reference.md)** — every user-settable option in one place,
   with its type, default, constraints, and whether changing it needs a restart.

## Handing this guide to your own agent

If you are working with an AI assistant beside you, give it the documentation rather than a search over it. The
published site carries three artifacts written for exactly that, under whichever version you are running:

- **`llms.txt`** at the version's root maps every published page with one line saying what it answers, and links each
  page's Markdown source. An agent loads it in full and then fetches only the page that owns your question.
- **`llms-operator.txt`** is this guide's operator path — choosing an installation, getting started, configuring a
  mailbox at your provider, and administering the deployment — in one file, for when the question is the path rather
  than a page of it.
- **`llms-mailbox-user.txt`** is the other path in one file: connecting the chat client you already use, and what each
  tool returns and bounds.

The version-agnostic address `https://krzysztof318.github.io/MailFathom/llms.txt` is the map of the release the site
opens on; the same file sits under each version's own directory. [The documentation
site](../operations/documentation-site.md#what-an-agent-reads) records how they are produced and what they
deliberately leave out.

## Once it is running

| Question | Page |
| --- | --- |
| Is it healthy, and how do I probe it? | [Health endpoints](../operations/health-endpoints.md) |
| Which port does it serve `/mcp` on, and is it HTTP or HTTPS? | [Where each surface is served](../operations/configuration-reference.md#where-each-surface-is-served) |
| How do I reach a running deployment from my own machine? | [Administering your deployment](administering.md), [the administrative endpoint](../operations/admin-endpoint.md) |
| How do I provision and rotate credentials? | [Secret provisioning](../operations/secret-provisioning.md), [secret rotation](../operations/secret-rotation.md) |
| How do I protect the MCP endpoint — keys, OAuth, TLS, client certificates, rate limits? | [The MCP endpoint](../operations/mcp-endpoint.md) |
| How do I add MailFathom to the chat client I use? | [Connecting the chat client you already use](mcp-clients.md) |
| How do I upgrade, back up, restore, or remove it? | [Docker Compose](../operations/deployment-compose.md), [Podman Quadlet](../operations/deployment-quadlet.md), [Kubernetes](../operations/deployment-kubernetes.md) |
| It refuses to start, saying a migration is pending. What now? | [Applying the database schema](../operations/database-schema.md) |
| Where does configuration come from, and what reloads without a restart? | [Configuration sources](../operations/configuration-sources.md), [configuration reference](../operations/configuration-reference.md) |
| What does it record about itself, and where do the records go? | [Telemetry](../operations/telemetry.md), [host startup telemetry](../operations/host-startup-telemetry.md) |
| What exactly does synchronization store and reconcile? | [IMAP synchronization](../features/imap-synchronization.md) |
| What address and credential does my mail service want? | [Configuring a mailbox at your provider](mailbox-providers.md) |
