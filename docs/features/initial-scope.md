# Initial scaffold scope

The initial scaffold creates the project boundaries needed for the first release without implementing mail, persistence, retrieval, or MCP behavior yet.

The ASP.NET Core host exposes a root readiness response. In development, `Host`'s own service defaults also expose `/health` and `/alive` endpoints while wiring OpenTelemetry, HTTP resilience, and service discovery; `docs/architecture/solution-structure.md` records why that source belongs to `Host`. The Aspire AppHost wires the host to a PostgreSQL resource for future persistence work.


## IMAP synchronization status

The first implemented slice covers periodic read-only reconciliation, application-owned IMAP/persistence abstractions, EF Core PostgreSQL mappings, bounded raw MIME content storage, synchronization checkpoints, typed connection validation, and a disabled-by-default hosted worker with per-folder failure isolation. IDLE, NOTIFY, deployment-specific secret binding, MCP read tools, RAG indexing, and SMTP outbox processing remain pending. The baseline migration and its apply policy have since landed, and so has the integration-test foundation; the EF Core mappings, constraints, indexes, and transaction behavior this slice introduced are now verified against real PostgreSQL by `tests/IntegrationTests`, which is what the `[RequiresIntegrationCoverage]` markers record the location of.

## Read side status

The first read use case has landed. `ListEmails` answers a mailbox listing from the local copy with structured filters, a bounded page size, keyset pagination, and per-folder synchronization freshness, and it reaches no mail server; [Mailbox queries](mailbox-queries.md) documents its request contract, cursor semantics, and privacy bounds. It is an application use case only — the `list_emails` MCP tool, the email content read model, and lexical search are still pending, as are the remaining MCP tools and RAG retrieval.
