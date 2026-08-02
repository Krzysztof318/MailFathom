# Initial scaffold scope

<!-- describes: src/Application/Emails/**, src/Application/EmailContent/**, src/Application/Synchronization/**, src/Mcp/Tools/** -->

The initial scaffold creates the project boundaries needed for the first release without implementing mail, persistence, retrieval, or MCP behavior yet.

The ASP.NET Core host exposes a root readiness response. It also serves three probes — `/started`, `/health`, and `/alive` — on a listener of their own, on port 8081 unless a deployment configures another, in every environment; `docs/operations/health-endpoints.md` states what each one consults, why they are kept off the port that serves `/` and `/mcp`, and how a deployment turns them off or puts TLS in front of them, and `docs/architecture/solution-structure.md` records why the service defaults belong to `Host`. Those defaults wire OpenTelemetry, HTTP resilience, and service discovery. The Aspire AppHost wires the host to a PostgreSQL resource for future persistence work.


## IMAP synchronization status

The first implemented slice covers periodic read-only reconciliation, application-owned IMAP/persistence abstractions, EF Core PostgreSQL mappings, bounded raw MIME content storage, synchronization checkpoints, typed connection validation, and disabled-by-default hosted synchronization that supervises each configured account on a schedule of its own, with per-account and per-folder failure isolation. Each run now also reconciles a bounded window of already-stored mail against the server, so a message deleted remotely stops being served locally — as a tombstone every query excludes or as an erased local copy, per account. IDLE, NOTIFY, deployment-specific secret binding, RAG indexing, and SMTP outbox processing remain pending. The MCP protocol surface has since landed as an endpoint that is disabled by default and, once enabled, requires an explicit choice between API key authentication and none at all, together with the `list_emails`, `get_email_content`, and `search_emails` tools, their descriptor conventions, and the stable error codes every later tool reuses; [MCP tools](mcp-tools.md) and [MCP endpoint](../operations/mcp-endpoint.md) record them. Client certificates are still pending. The baseline migration and its apply policy have since landed, and so has the integration-test foundation; the EF Core mappings, constraints, indexes, and transaction behavior this slice introduced are now verified against real PostgreSQL by `tests/IntegrationTests`, which is what the `[RequiresIntegrationCoverage]` markers record the location of.

## Read side status

Three read use cases have landed, all answered entirely from the local copy and none reaching a mail server.

`ListEmails` answers a mailbox listing with structured filters, a bounded page size, keyset pagination, and per-folder synchronization freshness; [Mailbox queries](mailbox-queries.md) documents its request contract, cursor semantics, and privacy bounds. The `list_emails` MCP tool serves it over the protocol.

`GetEmailContent` answers up to ten emails in one call, each with normalized headers, a bounded plain-text body, optionally a sanitized HTML representation, attachment counts, and — on request — per-attachment metadata re-derived from the stored MIME without any bytes. The count and a shared character budget bound what one call draws out of a mailbox, truncation is always explicit and names which of the two bounds cut it, an encrypted body is a state of its own rather than an empty one, and a missing or damaged local copy produces a stable per-email failure and a durable repair request instead of an IMAP fetch, leaving the emails beside it readable; [Email content](email-content.md) documents the representations, the bounds, the attachment default, the sanitization policy, and the consistency behavior. The `get_email_content` MCP tool serves it over the protocol.

`SearchEmails` answers a bounded, ranked window of emails matching free text, with highlighted snippets cut by PostgreSQL and the same structured filters a listing takes; [Lexical email search](lexical-email-search.md) documents its query contract, snippet bounds, and why it publishes a window rather than a cursor. The `search_emails` MCP tool serves it over the protocol, adding a retrieval-mode field that reports `lexical` so the later hybrid work widens an enumeration rather than reshaping a response.

`ask_mail` and RAG retrieval are still pending; the three read-only tools of the first release are complete.
