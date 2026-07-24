# Initial scaffold scope

The initial scaffold creates the project boundaries needed for the first release without implementing mail, persistence, retrieval, or MCP behavior yet.

The ASP.NET Core host exposes a root readiness response. In development, shared service defaults also expose `/health` and `/alive` endpoints while wiring OpenTelemetry, HTTP resilience, and service discovery. The Aspire AppHost wires the host to a PostgreSQL resource for future persistence work.


## IMAP synchronization status

The first implemented slice covers periodic read-only reconciliation, application-owned IMAP/persistence abstractions, EF Core PostgreSQL mappings, raw MIME content storage, synchronization checkpoints, and a disabled-by-default hosted worker. IDLE, NOTIFY, authenticated account connection settings, migrations, integration tests, MCP read tools, RAG indexing, and SMTP outbox processing remain pending.
