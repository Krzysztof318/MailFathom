# Infrastructure Development Instructions

These instructions apply under `src/Infrastructure/` in addition to parent instructions.

## Persistence and EF Core

- Keep `DbContext` scoped and short-lived. It is not thread-safe and must never be shared across concurrent operations.
- Use asynchronous EF Core APIs and propagate cancellation tokens.
- Project queries directly into application read models. Do not load full entity graphs when a bounded projection is sufficient.
- Use `AsNoTracking` for read-only queries unless identity resolution or change tracking is explicitly required.
- Avoid lazy loading and hidden N+1 queries. Make related data loading explicit.
- Express uniqueness, concurrency, and idempotency guarantees in PostgreSQL constraints as well as in application logic.
- Keep transactions short and define their boundary in the application operation. Do not hold a database transaction open across IMAP, SMTP, or AI network calls.
- A write repository obtains its `DbContext` from the `IPersistenceSession` it is given and does not inject one. A read method joins no transaction, so it takes no session and uses the scoped context. Never accept a contract parameter the implementation ignores: a session that is not written through guarantees nothing and only appears to.
- Reach for the change tracker only when a pending insert must be visible before commit, and say so in a comment. Prefer `FindAsync` for primary-key lookups, which already resolves from the tracker. For alternate-key lookups use the shared two-pass helper with a single predicate expression rather than repeating the predicate for the in-memory and database passes.
- Review generated migrations and SQL. Add indexes from demonstrated query shapes and inspect query plans for performance-critical paths.
- Use provider-supported parameterization. Never construct SQL from untrusted strings; any dynamic identifier must come from validated application-owned metadata.

## Email protocol safety

- Treat `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity.
- Fetch message bodies with mechanisms that preserve the remote `\Seen` state. Add a regression test for every code path that fetches content.
- Keep MCP reads local; an MCP request must not trigger a synchronous IMAP fetch.
- Make synchronization, object writes, indexing, and SMTP outbox processing idempotent.
- Require explicit opt-in for unencrypted IMAP/SMTP transport and clear-text authentication over an unencrypted connection.
- Do not disable TLS certificate validation. Support private servers through explicit trusted CA configuration.
