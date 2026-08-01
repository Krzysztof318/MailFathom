# MailFathom documentation

This documentation set explains the durable design and operating model for MailFathom.

## For users

[The user guide](users/README.md) is the guided path for people who install, configure, and use MailFathom rather
than develop it: [choosing an installation](users/installation.md), [getting started](users/getting-started.md) from
first mailbox to first tool call, and [using the tools](users/usage.md). It links into the sections below for depth
instead of duplicating them.

## Sections

- [Architecture](architecture/solution-structure.md) describes the clean-architecture boundaries and project layout.
- [Stored email schema](architecture/stored-email-schema.md) documents the `stored_emails` table, its indexes, and the timeline ordering contract keyset pagination depends on.
- [Features](features/initial-scope.md) summarizes the first scaffolded capability scope.
- [Operations](operations/local-development.md) covers local .NET and Aspire development commands.
- [Configuration sources](operations/configuration-sources.md) covers the source precedence, the deployment-provisioned JSON directory and file, and the Kubernetes ConfigMap and Secret mapping.
- [Configuration reference](operations/configuration-reference.md) lists every user-settable option in one place, with its type, default, constraints, and whether changing it needs a restart.
- [Telemetry](operations/telemetry.md) records what the host emits over OpenTelemetry, the one environment variable that decides whether it is exported, the Aspire dashboard as the local destination, and why deployments export nothing by default.
- [Secret provisioning](operations/secret-provisioning.md) covers secret references, the systemd and container provisioning paths, and in-memory exposure.
- [Host startup telemetry](operations/host-startup-telemetry.md) describes the bootstrap logging pipeline that reports process start, startup failure, and shutdown.
- [MCP endpoint](operations/mcp-endpoint.md) covers enabling the protocol surface, the API keys and explicit unauthenticated mode that guard it, the origins it answers, and the domains and certificates it terminates TLS for.
- [Health endpoints](operations/health-endpoints.md) covers the startup, readiness, and liveness probes, the dedicated listener they are served on, the transports it supports, and why the surface carries no credential and no rate limit.
- [The container image](operations/container-image.md) documents the image `deploy/docker/Dockerfile` produces, how the service runs, its health endpoints, and why it carries no schema tool.
- [Deploying with Docker Compose](operations/deployment-compose.md) is the supported single-machine deployment, including secret and configuration provisioning, the explicit schema step, backup, and what survives removal.
- [Deploying to Kubernetes](operations/deployment-kubernetes.md) documents the Helm chart, its required inputs, what the pod serves by default, TLS at the ingress, and the Restricted Pod Security Standard defaults.
- [The release procedure](operations/release-procedure.md) documents where the version number comes from, where it is observable at run time and on disk, and the order the two pull requests and the tag have to land in.
- [Agent workflow](operations/agent-workflow.md) documents the shared Codex and Claude Code workflow.
- [Decisions](decisions/README.md) describes the ADR workflow and templates.

- [IMAP synchronization](features/imap-synchronization.md)
- [Mailbox queries](features/mailbox-queries.md) documents the `ListEmails` request contract, cursor semantics, freshness reporting, and attachment-presence rule.
- [Lexical email search](features/lexical-email-search.md) documents the `SearchEmails` query contract, the snippet bounds, the bounded-window rationale, and what the index does not cover.
- [Email content](features/email-content.md) documents the `GetEmailContent` representations, the HTML sanitization policy, the truncation contract, and what happens when a local copy is unusable.
- [MCP tools](features/mcp-tools.md) documents the tool descriptor conventions, the `list_emails` tool that publishes that use case, and the stable error codes.

## Planned work

This documentation set describes behavior that exists. The architecture draft and the PR-sized specifications that decompose the gap between it and the code live in [`specs/`](../specs/README.md). A specification is a statement of intent; a page here is a statement of fact.
