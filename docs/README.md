# MailFathom documentation

This documentation set explains the durable design and operating model for MailFathom.

**These pages are also published as a site**, at <https://krzysztof318.github.io/MailFathom/>, with search, an API
reference generated from the source, and one version per release. This file is the index for reading them here in the
repository; [the documentation site](operations/documentation-site.md) records what the site carries, which versions
it publishes, and what a new page owes it.

## For users

[The user guide](users/README.md) is the guided path for people who install, configure, and use MailFathom rather
than develop it: [choosing an installation](users/installation.md), [getting started](users/getting-started.md) from
first mailbox to first tool call, [configuring a mailbox at your provider](users/mailbox-providers.md),
[using the tools](users/usage.md), and
[administering a running deployment](users/administering.md) with the `mfctl` command. It links into the sections
below for depth instead of duplicating them.

## Sections

- [Architecture](architecture/solution-structure.md) describes the clean-architecture boundaries and project layout.
- [Stored email schema](architecture/stored-email-schema.md) documents the `stored_emails` table, its indexes, and the timeline ordering contract keyset pagination depends on.
- [Features](features/initial-scope.md) summarizes the first scaffolded capability scope.
- [Operations](operations/local-development.md) covers local .NET and Aspire development commands.
- [Configuration sources](operations/configuration-sources.md) covers the source precedence, the deployment-provisioned JSON directory and file, and the Kubernetes ConfigMap and Secret mapping.
- [Configuration reference](operations/configuration-reference.md) is the map to the four pages that list every user-settable option with its type, default, constraints, and whether changing it needs a restart, and it holds the settings read from the environment alone.
- [Permissions](operations/permissions.md) states what a credential may do: the names MailFathom publishes, what each one reaches, how a grant is written on an authentication entry, and what a refused caller is told.
- [Telemetry](operations/telemetry.md) records what the host emits over OpenTelemetry, the one environment variable that decides whether it is exported, the Aspire dashboard as the local destination, and why deployments export nothing by default.
- [The platform TLS policy](operations/platform-tls-policy.md) explains why a legacy mail server's handshake can be refused before any MailFathom setting applies, the one supported way to relax it, and what relaxing it costs the whole process.
- [Secret provisioning](operations/secret-provisioning.md) covers secret references, the systemd and container provisioning paths, and in-memory exposure.
- [Mailbox OAuth](operations/mailbox-oauth.md) covers authenticating a mailbox that no longer accepts a password: what each provider requires, how the `mfctl` command obtains a refresh token, and how the running service uses it.
- [Host startup telemetry](operations/host-startup-telemetry.md) describes the bootstrap logging pipeline that reports process start, startup failure, and shutdown.
- [MCP endpoint](operations/mcp-endpoint.md) covers enabling the protocol surface, the API keys and explicit unauthenticated mode that guard it, the origins it answers, and the domains and certificates it terminates TLS for.
- [MCP client OAuth](operations/mcp-client-oauth.md) is the end-to-end sequence for signing a client in through your own identity provider — the provider-side half of an OAuth connection: which steps are once per deployment and which repeat per client, the three client-registration shapes, one worked provider, a verification recipe to run before touching a client, and what each failed sign-in means. Which dialog each client offers is [connecting the chat client you already use](users/mcp-clients.md).
- [The administrative endpoint](operations/admin-endpoint.md) covers the surface the `mfctl` command reaches, why it is a listener and a credential separate from the MCP endpoint's, how a sign-in is stored on the operator's machine, and what each failure message means.
- [Health endpoints](operations/health-endpoints.md) covers the startup, readiness, and liveness probes, the dedicated listener they are served on, the transports it supports, and why the surface carries no credential and no rate limit.
- [The container image](operations/container-image.md) documents the image `deploy/docker/Dockerfile` produces, how the service runs, its health endpoints, and why it carries no schema tool.
- [Deploying with Docker Compose](operations/deployment-compose.md) is the supported single-machine deployment, including secret and configuration provisioning, the explicit schema step, backup, and what survives removal.
- [Deploying with Podman Quadlet](operations/deployment-quadlet.md) is the same single-machine stack run as rootless systemd units, so that encrypted systemd credentials reach a container, and documents what that path requires and what SELinux costs it.
- [Deploying to Kubernetes](operations/deployment-kubernetes.md) documents the Helm chart, its required inputs, what the pod serves by default, TLS at the ingress, and the Restricted Pod Security Standard defaults.
- [Applying the database schema](operations/database-schema.md) documents the idempotent SQL artifact each release ships, the privileges and ownership a schema step needs, the locks it takes, the deployment ordering it assumes, the three startup failures it answers, and why the artifact is a script rather than something that runs itself.
- [The release procedure](operations/release-procedure.md) documents where the version number comes from, where it is observable at run time and on disk, and the order the two pull requests and the tag have to land in.
- [Agent workflow](operations/agent-workflow.md) documents the shared Codex and Claude Code workflow.
- [Issue tracking and the roadmap board](operations/issue-tracking.md) documents which work needs an issue, what its body carries, the one `type:*` label, the stack label, the milestone, the board's fields and views, and how an arrival from outside the project is triaged.
- [The documentation site](operations/documentation-site.md) documents how these pages are published, which versions the site carries and which one it opens on, where the navigation is written, and which links have to be absolute.
- [Decisions](decisions/README.md) describes the ADR workflow and templates.

- [IMAP synchronization](features/imap-synchronization.md)
- [Mailbox queries](features/mailbox-queries.md) documents the `ListEmails` request contract, cursor semantics, freshness reporting, and attachment-presence rule.
- [Email search](features/email-search.md) documents the `SearchEmails` query contract, when retrieval is hybrid and what the fusion does, the snippet bounds, the bounded-window rationale, and what the index does not cover.
- [Email content](features/email-content.md) documents the `GetEmailContent` representations, the HTML sanitization policy, the truncation contract, and what happens when a local copy is unusable.
- [MCP tools](features/mcp-tools.md) documents the tool descriptor conventions, the `list_emails` tool that publishes that use case, and the stable error codes.

## Planned work

This documentation set describes behavior that exists. What MailFathom is still being built into is decomposed into [issues](https://github.com/Krzysztof318/MailFathom/issues), and where a decision has been taken ahead of the code that answers it, the record under [`docs/decisions/`](https://github.com/Krzysztof318/MailFathom/tree/main/docs/decisions) is where it is stated. Intent lives in those two places; a page here is a statement of fact.
