# MailFathom user guide

This is the documentation for people who install, configure, operate, and use MailFathom. It is a guided path rather
than a second copy of the reference material: each step links into the operations and feature pages where the full
contract lives, so nothing here goes stale on its own. Contributor and agent documentation is separate, under the
repository root and [`docs/`](../README.md) generally.

## What MailFathom is

MailFathom is a self-hosted service that synchronizes mail from your IMAP accounts into a local PostgreSQL copy,
indexes it for search, and serves it to AI agents as read-only tools over the
[Model Context Protocol](https://modelcontextprotocol.io/). An agent connected to it can list, read, and search your
mail; it cannot send, delete, move, or mark anything, because no such tool exists on the surface.

Two properties hold everywhere and are worth knowing before anything is installed:

- **Reading is local.** A tool call answers from the local copy and never contacts a mail server, so it is fast, works
  while the server is down, and cannot change anything remotely. Every result states how fresh the local copy is.
- **Synchronization is read-only.** Fetching mail never sets the remote `\Seen` flag, so mail MailFathom has copied
  still shows as unread in your mail client until you read it there.

## The state of the release

MailFathom has not had a first release yet. No container image is published, no versioned artifact exists, and the
schema-application artifact a released installation will use is still open. Every installation therefore starts from a
checkout of this repository, and these pages say so where it matters instead of describing a release that does not
exist. The first release is milestone `0.1.0`.

## The path

1. **[Choose and perform an installation](installation.md)** — which deployment shape fits, what each needs, and where
   its full guide is.
2. **[Getting started](getting-started.md)** — from an installed instance to a synchronized mailbox and a first
   successful tool call, including secrets, the schema step, health verification, and connecting an MCP client.
3. **[Using the tools](usage.md)** — what `list_emails`, `search_emails`, and `get_email_content` do, what they
   deliberately bound, and how to read a failure.
4. **[Configuration reference](../operations/configuration-reference.md)** — every user-settable option in one place,
   with its type, default, constraints, and whether changing it needs a restart.

## Once it is running

| Question | Page |
| --- | --- |
| Is it healthy, and how do I probe it? | [Health endpoints](../operations/health-endpoints.md) |
| How do I provision and rotate credentials? | [Secret provisioning](../operations/secret-provisioning.md), [secret rotation](../operations/secret-rotation.md) |
| How do I protect the MCP endpoint — keys, OAuth, TLS, client certificates, rate limits? | [The MCP endpoint](../operations/mcp-endpoint.md) |
| How do I upgrade, back up, restore, or remove it? | [Docker Compose](../operations/deployment-compose.md), [Kubernetes](../operations/deployment-kubernetes.md) |
| Where does configuration come from, and what reloads without a restart? | [Configuration sources](../operations/configuration-sources.md), [configuration reference](../operations/configuration-reference.md) |
| What does it record about itself, and where do the records go? | [Telemetry](../operations/telemetry.md), [host startup telemetry](../operations/host-startup-telemetry.md) |
| What exactly does synchronization store and reconcile? | [IMAP synchronization](../features/imap-synchronization.md) |
