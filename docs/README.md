# MailMcp documentation

This documentation set explains the durable design and operating model for MailMcp.

## Sections

- [Architecture](architecture/solution-structure.md) describes the clean-architecture boundaries and project layout.
- [Stored email schema](architecture/stored-email-schema.md) documents the `stored_emails` table, its indexes, and the timeline ordering contract keyset pagination depends on.
- [Features](features/initial-scope.md) summarizes the first scaffolded capability scope.
- [Operations](operations/local-development.md) covers local .NET and Aspire development commands.
- [Secret provisioning](operations/secret-provisioning.md) covers secret references, the systemd and container provisioning paths, and in-memory exposure.
- [Host startup telemetry](operations/host-startup-telemetry.md) describes the bootstrap logging pipeline that reports process start, startup failure, and shutdown.
- [MCP endpoint](operations/mcp-endpoint.md) covers enabling the protocol surface and the interim posture of an endpoint with no transport authentication.
- [Agent workflow](operations/agent-workflow.md) documents the shared Codex and Claude Code workflow.
- [Decisions](decisions/README.md) describes the ADR workflow and templates.

- [IMAP synchronization](features/imap-synchronization.md)
- [Mailbox queries](features/mailbox-queries.md) documents the `ListEmails` request contract, cursor semantics, freshness reporting, and attachment-presence rule.
- [MCP tools](features/mcp-tools.md) documents the tool descriptor conventions, the `list_emails` tool that publishes that use case, and the stable error codes.

## Planned work

This documentation set describes behavior that exists. The architecture draft and the PR-sized specifications that decompose the gap between it and the code live in [`specs/`](../specs/README.md). A specification is a statement of intent; a page here is a statement of fact.
