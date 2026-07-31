# Initial scaffold scope

The initial scaffold creates the project boundaries needed for the first release without implementing mail, persistence, retrieval, or MCP behavior yet.

The ASP.NET Core host exposes a root readiness response. In development, `Host`'s own service defaults also expose `/health` and `/alive` endpoints while wiring OpenTelemetry, HTTP resilience, and service discovery; `docs/architecture/solution-structure.md` records why that source belongs to `Host`. The Aspire AppHost wires the host to a PostgreSQL resource for future persistence work.


## IMAP synchronization status

The first implemented slice covers periodic read-only reconciliation, application-owned IMAP/persistence abstractions, EF Core PostgreSQL mappings, bounded raw MIME content storage, synchronization checkpoints, typed connection validation, and a disabled-by-default hosted worker with per-folder failure isolation. IDLE, NOTIFY, deployment-specific secret binding, RAG indexing, and SMTP outbox processing remain pending. The MCP protocol surface has since landed as an endpoint that is disabled by default, together with the `list_emails`, `get_email_content`, and `search_emails` tools, their descriptor conventions, and the stable error codes every later tool reuses; [MCP tools](mcp-tools.md) and [MCP endpoint](../operations/mcp-endpoint.md) record them. The baseline migration and its apply policy have since landed, and so has the integration-test foundation; the EF Core mappings, constraints, indexes, and transaction behavior this slice introduced are now verified against real PostgreSQL by `tests/IntegrationTests`, which is what the `[RequiresIntegrationCoverage]` markers record the location of.

## Read side status

Three read use cases have landed, all answered entirely from the local copy and none reaching a mail server.

`ListEmails` answers a mailbox listing with structured filters, a bounded page size, keyset pagination, and per-folder synchronization freshness; [Mailbox queries](mailbox-queries.md) documents its request contract, cursor semantics, and privacy bounds. The `list_emails` MCP tool serves it over the protocol.

`GetEmailContent` answers one email with normalized headers, a bounded plain-text body, optionally a sanitized HTML representation, and per-attachment metadata re-derived from the stored MIME without any bytes. Truncation is always explicit, an encrypted body is a state of its own rather than an empty one, and a missing or damaged local copy produces a stable failure and a durable repair request instead of an IMAP fetch; [Email content](email-content.md) documents the representations, the sanitization policy, and the consistency behavior. The `get_email_content` MCP tool serves it over the protocol.

`SearchEmails` answers a bounded, ranked window of emails matching free text, with highlighted snippets cut by PostgreSQL and the same structured filters a listing takes; [Lexical email search](lexical-email-search.md) documents its query contract, snippet bounds, and why it publishes a window rather than a cursor. The `search_emails` MCP tool serves it over the protocol, adding a retrieval-mode field that reports `lexical` so the later hybrid work widens an enumeration rather than reshaping a response.

`ask_mail` and RAG retrieval are still pending; the three read-only tools of the first release are complete.
