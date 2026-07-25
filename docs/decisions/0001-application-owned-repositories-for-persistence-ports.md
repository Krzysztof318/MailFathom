---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-07-25
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Use application-owned repository ports with explicit Unit of Work sessions and keep EF Core behind infrastructure adapters

## Context and Problem Statement

MailMcp follows clean architecture: `Application` owns use cases and ports, while `Infrastructure` owns EF Core, PostgreSQL, SQL details, and migrations. The initial architecture also requires fast isolated unit tests, future PostgreSQL-backed integration tests, explicit privacy/governance seams, and no leakage of EF Core or provider-specific types into `Application` or `Domain`.

The decision question is: should application use cases depend directly on EF Core objects in unit tests and production code, or should MailMcp introduce a repository-style persistence port; if repositories are used, should they be simple use-case/aggregate-specific contracts, capability-segregated CRUD interfaces, or a generic repository plus specification model? A second question is how writes that touch multiple repositories should share a transaction boundary: by passing an explicit session object, by exposing repositories as properties on a Unit of Work object, or by relying on an ambient transient session such as an `AsyncLocal` context. A third question is whether concurrent writes should normally use optimistic version checks or pessimistic database locks, how PostgreSQL should generate the concurrency token, and where conflict translation and bounded retries belong.

## Decision Drivers

- Keep `Application` and `Domain` independent from EF Core, Npgsql, PostgreSQL, and future object-storage/search implementation details.
- Preserve true unit tests for application behavior without making EF Core in-memory, SQLite in-memory, or mocked `DbSet` behavior stand in for PostgreSQL query semantics.
- Keep first-release scope small and reviewable while preserving seams for auditability, privacy, retention, erasure, synchronization idempotency, and future storage migrations.
- Avoid a repository abstraction that becomes either a leaky `IQueryable` façade or a generic CRUD layer that hides important domain and consistency rules.
- Support provider-specific query verification later through integration tests against PostgreSQL rather than pretending a fake provider proves SQL behavior.
- Keep query result sizes bounded, deterministic, and shaped as application read models rather than returning mutable EF Core entity graphs.
- Make write transaction boundaries explicit enough for application review, audit evidence, cancellation, idempotency, retry safety, and future PostgreSQL integration tests.
- Avoid hidden persistence state that can cross asynchronous flows, background work, retries, or nested use cases without a clearly owned lifetime.
- Prevent lost updates without holding database locks while application or external work is in progress.
- Prefer a low-overhead, server-generated concurrency token that integrates with EF Core and is not mistaken for an audit timestamp or domain version.
- Make retries explicit, bounded, observable, cancellation-aware, and safe only for operations that can be repeated from a fresh read.
- Reserve pessimistic locking for measured contention or atomic work-claiming requirements that optimistic retries cannot handle efficiently or correctly.

## Considered Options

- Direct EF Core usage in application code and unit tests.
- Simple application-owned repository/query ports implemented with EF Core in `Infrastructure`.
- Generic repository with broad CRUD interfaces such as `ICanCreate<T>`, `ICanDelete<T>`, `ICanUpdate<T>`, and `ICanQuery<T>`.
- Generic repository with specification/query objects for complex query composition.

For Unit of Work coordination, this ADR considers these patterns:

- Explicit application-owned Unit of Work session passed to repository methods that must share one transaction.
- Unit of Work object that exposes participating repositories as properties and owns commit/rollback.
- Ambient transient Unit of Work session resolved through an `AsyncLocal`-backed context.

For concurrent writes, this ADR considers these patterns:

- Optimistic concurrency using PostgreSQL's server-generated `xmin` system column as an EF Core row-version token.
- Optimistic concurrency using an ordinary application column, updated by application code or a PostgreSQL trigger.
- Pessimistic concurrency using explicit row locks such as `SELECT ... FOR UPDATE`.

## Decision Outcome

Chosen option: "Simple application-owned repository/query ports implemented with EF Core in `Infrastructure`, coordinated by explicit application-owned Unit of Work sessions for multi-repository writes", because it gives MailMcp reliable application-layer unit-test seams without adopting a broad generic repository framework, exposing EF Core query composition, or hiding transaction lifetime in ambient state.

Optimistic concurrency is the default for mutable EF Core-managed PostgreSQL records. Infrastructure persistence models use a `uint` property named `ConcurrencyVersion`, configured with EF Core's `IsRowVersion()`, which the Npgsql provider maps to PostgreSQL's implicit, server-generated `xmin` system column. Pessimistic row locking is an explicit exception that requires a concrete consistency or contention reason, a short transaction, and provider-level integration tests.

The decision is proposed, not accepted. The first implementation should introduce the smallest repository/query contracts required by active use cases. Single-store operations may remain atomic inside one repository method. Multi-repository writes should use an explicit Unit of Work session contract only when a concrete use case needs one transaction across ports, then revisit the abstraction shape after the first persistence-backed slices show real transaction and query pressure.

### Consequences

- Good, because application unit tests can stub repository outputs directly and focus on use-case behavior, authorization, pagination, idempotency, and privacy rules.
- Good, because EF Core LINQ, SQL translation, migrations, concurrency tokens, indexes, PostgreSQL full-text search, pgvector behavior, and raw MIME `bytea` access remain implementation concerns verified by future integration tests.
- Good, because persistence contracts can express domain language and bounded result contracts, such as mailbox timelines, synchronization checkpoints, message occurrence lookups, outbox leasing, and derived-index cleanup.
- Good, because `xmin` changes automatically when PostgreSQL creates a new row version and requires no application-managed timestamp, trigger, or additional user-defined column.
- Neutral, because every testable query must be represented by an application-owned method or query object rather than composed ad hoc from `IQueryable` in use cases.
- Neutral, because `ConcurrencyVersion` is a PostgreSQL-specific persistence detail and cannot serve as a business revision, audit time, external ETag, or durable version identifier.
- Bad, because the repository layer adds maintenance cost and can drift into an anemic CRUD wrapper if reviews do not enforce use-case-specific contracts.
- Bad, because explicit Unit of Work sessions add parameters and lifetime rules that must be documented and tested.
- Bad, because optimistic conflicts require explicit application behavior and may repeat work when multiple writers frequently contend for the same row.

## Decision Details

### Repository placement and dependency direction

Repository contracts are application ports. They belong in `src/Application` near the use case or subdomain that owns the persistence need. Implementations belong in `src/Infrastructure/Persistence/PostgreSql` and may use EF Core, Npgsql, provider-specific SQL, compiled queries, tracking, concurrency tokens, and transaction APIs internally.

`Domain` must not contain persistence repositories for ordinary data access. A domain interface is allowed only for pure domain policies that do not imply I/O, clocks, configuration, logging, or infrastructure.

### Contract style

Use explicit, behavior-oriented repository or query-port names. Prefer contracts such as `IEmailTimelineReader`, `ISynchronizationCheckpointStore`, `IEmailOccurrenceRepository`, `IEmailContentStore`, or `IOutboxLeaseStore` over `IRepository<T>`.

Repositories must not expose EF Core types, provider types, entity framework attributes, `DbContext`, `DbSet`, `EntityEntry`, `IQueryable<T>`, unbounded `IEnumerable<T>` query composition, or raw SQL fragments. Return domain objects, application read models, result types, bounded collections, or `IAsyncEnumerable<T>` only when streaming is part of the contract and cancellation/backpressure behavior is documented.

Repository methods should represent persistence operations required by a use case or aggregate lifecycle. They may include reads and writes when that mirrors a transactional consistency boundary. Split read-oriented query ports from write-oriented stores when it improves clarity, avoids unnecessary mutation capabilities, or supports different performance/read-model implementations.

### CRUD capability interfaces

Do not introduce cross-cutting CRUD capability interfaces such as `ICanCreate<T>`, `ICanDelete<T>`, `ICanUpdate<T>`, or `ICanList<T>` as a default pattern. They are too broad for MailMcp's persistence rules because delete, retention, erasure, synchronization writes, raw MIME storage, embedding cleanup, and SMTP outbox leasing all have different invariants, audit expectations, and idempotency requirements.

A narrowly scoped capability interface may be introduced only when at least two real application ports need the same behavior and the shared contract can state meaningful domain constraints. Until then, prefer explicit methods on explicit ports.

### Specifications and query objects

Do not adopt a generic specification framework at this stage. Specifications can be useful when clients need declarative query criteria submitted to a repository, but in MailMcp they also risk reintroducing query-language leakage, unbounded composition, and hard-to-review performance/privacy behavior.

Use simple query criteria records owned by `Application` when a use case genuinely needs variable filters, for example `MessageTimelineQuery`, `SearchEmailsQuery`, or `MessagesReadyForEmbeddingQuery`. These criteria are data contracts, not executable EF Core expressions. The infrastructure adapter translates them to EF Core or SQL and owns all provider-specific optimization.

Reconsider a specification pattern later only if all of the following become true:

- Many queries share stable, reusable predicates across multiple use cases.
- The specification model can remain provider-neutral and cannot expose `IQueryable`, `Expression<Func<TEntity, bool>>`, EF Core includes, ordering delegates, or provider-specific functions to `Application`.
- Reviewers can still reason about authorization, privacy filters, pagination, ordering, and query cost at the use-case boundary.
- PostgreSQL integration tests verify every translated specification shape that matters operationally.

### Unit-test and integration-test boundary

Application and domain unit tests must not instantiate production `DbContext`, EF Core in-memory providers, SQLite in-memory databases, or mocked `DbSet` query fakes to prove use-case behavior. Use NSubstitute or simple in-memory hand-written fakes for application-owned repository ports instead.

Infrastructure unit tests may cover pure mapping, option validation, SQL-shape helpers, and non-network adapter logic without a real database when behavior is deterministic. Tests that verify EF Core mappings, migrations, LINQ translation, raw SQL, transactions, concurrency, PostgreSQL full-text search, pgvector, `bytea`, or database constraints are integration tests and should be added only when the repository's integration-test phase begins.

### Transactions and Unit of Work

Do not introduce a generic Unit of Work abstraction only because EF Core has `SaveChangesAsync`. EF Core already treats one `SaveChanges` call as transactional when the provider supports transactions, and infrastructure adapters may use explicit EF Core transactions internally when one repository method owns the consistency boundary. Application contracts should introduce Unit of Work only when a use case must coordinate multiple application-owned repositories in one atomic write.

The preferred application-facing shape is an explicit Unit of Work session. A use case starts a session through a focused port such as `IPersistenceSessionFactory`, `IMailStoreUnitOfWorkFactory`, or a narrower use-case-specific transaction coordinator when that name better communicates intent. The returned session is passed explicitly to repository methods that must participate in the same transaction, and commit is an explicit asynchronous operation on the session or coordinator. Repository calls that are not part of that transaction do not receive the session.

MailMcp implements this as `IPersistenceSession` and `IPersistenceSessionFactory` in `MailMcp.Application.Persistence`. The names deliberately avoid the bare word `Session`, which in this system also means an open IMAP mailbox session (`IMailboxSession`); an unqualified `ISession` in a signature does not tell a reader which of the two it is.

### The session must be the write handle, not a marker parameter

A session parameter that the implementation ignores provides no guarantee at all. If a repository injects its own `DbContext` and merely accepts an `IPersistenceSession` it never reads, the write happens to join the caller's transaction only because dependency injection handed both objects the same scoped context. A session obtained from a different scope would be accepted silently and the write would land outside the caller's transaction.

Therefore a write repository must obtain its persistence context *through* the supplied session, and must fail loudly when handed a session it cannot write through. In MailMcp, `EfCorePersistenceSessionAccessor.DbContextOf` performs that resolution and write repositories consequently do not inject `DbContext` at all.

The guarantee this buys is that the write is issued on the session's own context, so it cannot land outside the transaction the caller opened. It is not a scope check: a session created by the same factory in a different scope is accepted, and writing through its context is the correct outcome. The rejection path, an `ArgumentException`, covers only a session backed by a different persistence provider, which cannot supply an EF Core context at all.

The corresponding rule for reads is the inverse: a read joins no transaction, so a read method uses the scoped context directly and does not take a session. A store that exposes both, such as `ISynchronizationCheckpointStore`, may hold an injected context used only by its read path.

### Change-tracker lookups

Entities added earlier in the same uncommitted session exist only in the change tracker. EF Core does not flush pending changes before a query and a LINQ query always reaches the database, so such an entity is invisible to a query and a change-tracker pass is genuinely required before falling back to the database.

That justification is narrow, and the pattern must not be copied by default:

- When the lookup is by primary key, use `FindAsync`. It already resolves from the change tracker without a database round-trip and needs no hand-written first pass.
- When the lookup is by an alternate key, use one shared helper that takes a single predicate expression and runs both passes, so the predicate is never written twice. `TrackedEntityLookup.SinglePendingOrPersistedAsync` is that helper.
- Do not use `FindAsync` when materializing the row is itself the cost being avoided, such as an `email_message_contents` row whose `bytea` payload must not enter memory or the change tracker. Take only the change-tracker pass and fall through to a set-based `ExecuteUpdate`.

Every remaining hand-written change-tracker pass carries a comment stating which of these cases applies.

### Persistence model types

EF Core entities model persistence, not the domain, so they use primitive column types and their own surrogate keys rather than domain value objects with value converters. `MailFolderEntity.Id` has no domain meaning at all, and converters reduce the transparency of migrations, generated SQL, and set-based operations such as `ExecuteUpdate`. Domain value objects are unwrapped at the adapter boundary, in one place per store.

This is a deliberate trade-off, not an omission: it accepts weaker type safety inside the adapter in exchange for a persistence model that stays readable against the database. Revisit it if adapter-level identifier mix-ups actually occur.

A Unit of Work session contract must remain application-owned and provider-neutral. It must not expose `DbContext`, EF Core transactions, connection objects, `IQueryable`, provider entities, or raw `SaveChangesAsync` as a generic persistence primitive. It may expose a commit method whose name reflects the application contract, such as `CommitAsync`, only when callers are responsible for completing a grouped write. The session must be asynchronously disposable, cancellation-aware, short-lived, non-shareable across concurrent operations, and documented as invalid after commit, rollback, or disposal.

Avoid a Unit of Work object that exposes all repositories as properties by default. That style can be acceptable for a narrow module-specific coordinator when the repository set is stable and all properties participate in the same consistency boundary, but it risks becoming a service locator with broad mutation authority. Prefer injecting the repositories a use case needs and passing the explicit session only to calls that join the transaction.

Do not use an `AsyncLocal` ambient Unit of Work as an application-facing contract. It is attractive because it keeps method signatures small and can let repositories discover the current transient session automatically, but it hides transaction participation from use-case code and tests. It also creates hazards around nested operations, retries, background tasks, asynchronous continuations, parallel fan-out, and cleanup after failures. Infrastructure may use an ambient implementation detail behind an explicit session object only if the public application contract still makes session lifetime and transaction participation visible.

If a transaction spans external I/O such as IMAP, SMTP, object storage, AI providers, or MCP tool calls, the design is probably wrong. Keep database transactions short and do not hold them open across network calls. Use durable state transitions, idempotency keys, outbox/inbox patterns, leases, or compensating operations instead of broad cross-resource transactions.

### Concurrency control

Use optimistic concurrency by default for mutable records written through tracked EF Core operations. Each applicable infrastructure persistence model has a `uint ConcurrencyVersion` property configured with `IsRowVersion()`. Npgsql maps that property to PostgreSQL's implicit `xmin` system column, so the database creates the value and changes it whenever an update produces a new row version. The mapping belongs only in `Infrastructure`; `Application` and `Domain` must not reference `xmin`, PostgreSQL transaction IDs, Npgsql metadata, or the provider-specific CLR representation.

`ConcurrencyVersion` is deliberately not named `Timestamp`, `UpdatedAt`, or `RowVersion`. It is not a wall-clock value, audit fact, domain event sequence, or portable SQL `rowversion` column. If a use case needs a last-modified timestamp, durable business revision, external ETag, or version that survives storage migration, backup/restore, or provider replacement, model that requirement separately rather than reusing `xmin`.

Apply the token to mutable EF Core-managed tables unless one of these exceptions is documented in the mapping:

- The table is append-only or immutable after insertion, so there is no update race to detect.
- The operation already uses one atomic conditional SQL statement and treats its affected-row count as the concurrency result, such as a lease acquisition or compare-and-set state transition.
- The data is disposable derived state whose update conflicts have no correctness impact and measurements show that conflict checking is materially harmful.
- The adapter does not use tracked EF Core writes, in which case it must implement an equivalent explicit concurrency predicate when lost-update protection is required.

Do not add a separate `timestamptz` column only to implement optimistic concurrency. PostgreSQL does not automatically update ordinary timestamp columns on every row change, so doing this consistently would require application-managed values or triggers. A time value also mixes conflict detection with audit semantics. An ordinary explicit version column may replace `xmin` for a specific table only when that table needs a durable or externally visible version identity; the migration, server-side generation mechanism, and performance cost must then be justified and tested.

Pessimistic concurrency is not the repository default. It may be used for a narrow operation when correctness requires exclusive access before a decision is made, when atomic work claiming benefits from `FOR UPDATE SKIP LOCKED`, or when measured conflict rates make optimistic retries more expensive than short blocking. Such code remains inside the PostgreSQL adapter, acquires the weakest sufficient lock, uses deterministic lock ordering, applies a lock timeout, never spans external I/O, and documents deadlock and cancellation behavior.

### Optimistic concurrency helper draft

EF Core reports a failed optimistic update or delete as `DbUpdateConcurrencyException`. Infrastructure must catch that exception only at the repository or Unit of Work commit boundary and translate it into an application-owned `ConcurrencyConflict` result. Provider exception details, tracked entries, SQL, and database values must not cross into `Application`, `Domain`, MCP responses, or administrative endpoints.

An internal application helper named `OptimisticConcurrencyRetryPolicy` may coordinate retries when the first concrete use case needs them. Its contract should execute an entire attempt supplied as a cancellation-aware delegate and should have these semantics:

- Retry is opt-in for an operation that is explicitly safe and idempotent to repeat.
- Each attempt creates a fresh persistence session, rereads current state, reevaluates authorization and domain invariants, reapplies the intended change, and commits once.
- The attempt count is bounded and supplied through validated options; exhaustion returns `ConcurrencyConflict` rather than converting the operation to last-writer-wins.
- Cancellation, shutdown, or a non-concurrency failure stops the loop immediately.
- Conflict, retry, exhaustion, and duration metrics contain operation and entity categories but no message content, addresses, tokens, tracked values, or other personal data.

The helper must not retry `SaveChangesAsync` on the same `DbContext`, mutate EF Core original values to force a write, automatically merge fields, or hide a retry inside a repository method after the application has made a decision from stale state. Interactive or security-sensitive changes should normally return the conflict to the caller for an explicit reread and new decision. Automated synchronization, indexing, retention, and outbox operations may opt into bounded retries only after their idempotency and external side effects are reviewed.

## Validation

- Code review verifies that `Application` and `Domain` do not reference EF Core, Npgsql, PostgreSQL, `DbContext`, `DbSet`, `IQueryable`, migrations, provider-specific AI types, or persistence entities.
- Unit tests for application use cases stub application repository/query ports instead of using EF Core in-memory, SQLite in-memory, or mocked `DbSet` query behavior.
- Repository contracts document bounds, ordering, cancellation behavior, authorization assumptions, side effects, transaction participation requirements, and privacy-sensitive data returned or persisted.
- Unit of Work session contracts document ownership, disposal, commit behavior, cancellation, concurrency restrictions, nested-session behavior, retry safety, and whether a repository call requires a session.
- Code review rejects a write repository that accepts a session parameter without writing through it, and rejects a hand-written change-tracker pass where `FindAsync` would do or where no comment states why the pattern is required.
- Future PostgreSQL integration tests verify EF Core mappings, migrations, query translation, constraints, concurrency, full-text search, pgvector, raw MIME content storage, and any provider-specific SQL.
- PostgreSQL integration tests verify that every mapped `ConcurrencyVersion` uses `xmin`, successful writes receive a new token, stale updates and deletes produce `ConcurrencyConflict`, and exempt write paths preserve their documented atomicity.
- Unit tests for an operation using `OptimisticConcurrencyRetryPolicy` verify fresh-state reevaluation, bounded attempts, cancellation, conflict exhaustion, no retry for unrelated failures, and no duplicate external side effects.
- Pull-request review requires a written justification for mutable EF Core-managed tables without a concurrency token and for every pessimistic lock, explicit version column, automatic merge, or retryable non-idempotent operation.
- Pull-request review rejects generic CRUD repositories, broad capability interfaces, hidden ambient transaction contracts, or specification abstractions unless the change includes concrete repeated use cases and validation evidence.

## Pros and Cons of the Options

### Direct EF Core usage in application code and unit tests

Use production EF Core entities, `DbContext`, `DbSet`, or fake EF Core providers directly from application services and tests.

- Good, because it has the least initial abstraction and lets developers write LINQ close to the use case.
- Good, because EF Core already provides identity tracking, change tracking, transactions, and query composition capabilities.
- Neutral, because simple prototypes can move quickly, but the dependency direction conflicts with the clean-architecture boundary once use cases grow.
- Bad, because EF Core and provider behavior would leak into `Application`, violating the architecture rule that persistence frameworks stay in `Infrastructure`.
- Bad, because EF Core in-memory and mocked `DbSet` query tests execute LINQ differently from PostgreSQL and cannot prove transaction, raw SQL, or provider-specific behavior.
- Bad, because application tests that instantiate EF Core infrastructure become integration-like tests while still failing to provide production-database confidence.

### Simple application-owned repository/query ports implemented with EF Core in `Infrastructure`

Define narrow ports in `Application`; implement them with EF Core in `Infrastructure`; stub ports in application unit tests; verify EF Core behavior later in PostgreSQL integration tests.

- Good, because it aligns with the existing MailMcp dependency rule and keeps persistence details behind adapters.
- Good, because tests can directly provide repository outputs and focus on use-case behavior rather than fake database semantics.
- Good, because contracts can preserve bounded queries, deterministic ordering, authorization assumptions, privacy constraints, cancellation, and idempotency in domain language.
- Neutral, because it requires a method or query criteria contract for each persisted behavior that must be tested without a database.
- Bad, because it adds an architectural layer and extra mapping code that must be maintained.
- Bad, because careless implementation can still become a one-method-per-LINQ-query catalog unless ports are organized around use cases and consistency boundaries.

### Generic repository with broad CRUD capability interfaces

Create reusable generic interfaces such as `IRepository<T>`, `ICanCreate<T>`, `ICanDelete<T>`, `ICanUpdate<T>`, and compose capabilities for each entity.

- Good, because capability segregation can make mutation permissions visible at the type level.
- Good, because common CRUD mechanics can be reused for simple entities.
- Neutral, because capability interfaces may be acceptable for low-risk administrative lookup data if real duplication appears.
- Bad, because generic CRUD names hide MailMcp-specific invariants for message occurrence identity, synchronization checkpoints, raw MIME content, embeddings, retention, deletion, and outbox leases.
- Bad, because broad capabilities are easy to over-inject, accidentally granting deletes or updates where only a narrow read or append operation is safe.
- Bad, because CRUD interfaces encourage entity-centric persistence instead of use-case-centric contracts and can make audit/privacy behavior less explicit.

### Generic repository with specification/query objects

Expose a generic repository that accepts reusable specifications or query objects, avoiding many repository methods while enabling composable filters.

- Good, because specifications can reduce method explosion when many queries share reusable criteria.
- Good, because declarative criteria can separate query intent from infrastructure translation when carefully constrained.
- Neutral, because MailMcp may eventually need richer query criteria for search, synchronization backlogs, retention, and indexing workflows.
- Bad, because a specification model often leaks expression trees, includes, ordering, pagination, or provider assumptions back into the application layer.
- Bad, because unbounded composition makes privacy filters, authorization predicates, deterministic ordering, and query cost harder to review.
- Bad, because it introduces a framework-level abstraction before MailMcp has enough real queries to prove the need.

## Pros and Cons of Unit of Work Coordination Options

### Explicit application-owned Unit of Work session passed to repository methods

A use case creates a short-lived session and passes it to only the repository methods that must share the transaction. Commit or rollback belongs to the session/coordinator contract.

- Good, because transaction participation is visible in method signatures, code review, tests, and documentation.
- Good, because each repository remains focused and can still be injected independently without granting broad repository access through one object.
- Good, because session lifetime, cancellation, disposal, retry behavior, and concurrency restrictions can be modeled as an application contract without exposing EF Core.
- Neutral, because method signatures gain an extra parameter for transactional writes.
- Bad, because callers must pass the session consistently; missing or mixed sessions can create subtle transactional bugs unless tests cover the use case.
- Bad, because the pattern can become noisy if applied to simple one-repository operations that already have an internal atomic boundary.

### Unit of Work object with repositories as properties

A use case receives or creates a Unit of Work object, accesses repositories through properties, then commits the Unit of Work.

- Good, because all repositories participating in one transaction are discoverable from one object.
- Good, because it can simplify small modules where every operation naturally uses the same stable repository set.
- Neutral, because it resembles common repository/UoW examples and may be familiar to contributors.
- Bad, because it tends to become a service locator that grants more persistence capabilities than a use case needs.
- Bad, because repository property collections can grow into an anemic persistence façade organized around infrastructure rather than use-case boundaries.
- Bad, because tests may accidentally exercise repository acquisition mechanics instead of the use-case contract and transaction semantics.

### Ambient transient Unit of Work session backed by `AsyncLocal`

A use case opens an ambient transaction scope, and repositories look up the current session from an async-flow context instead of receiving it explicitly.

- Good, because use-case and repository method signatures stay small.
- Good, because it can reduce repetitive plumbing when many repository calls participate in one transaction.
- Good, because it can be a useful infrastructure implementation detail behind an explicit application session.
- Neutral, because it relies on .NET async-flow behavior that is powerful but easy to misuse.
- Bad, because transaction participation becomes hidden, making code review, authorization review, privacy review, and unit tests less direct.
- Bad, because nested sessions, retries, background work, parallel fan-out, asynchronous continuations, and failure cleanup can accidentally reuse or lose the ambient session.
- Bad, because hidden ambient state conflicts with MailMcp's preference for explicit dependencies and small application contracts.

## Pros and Cons of Concurrency Options

### Optimistic concurrency using PostgreSQL `xmin`

Map an infrastructure `uint ConcurrencyVersion` property through `IsRowVersion()` so Npgsql uses PostgreSQL's implicit `xmin` system column.

- Good, because PostgreSQL generates and changes the token for every row version without a trigger or user-defined column.
- Good, because EF Core automatically includes the original token in update and delete predicates and reports zero affected rows as a concurrency conflict.
- Neutral, because every PostgreSQL table already has `xmin`, but each selected and tracked token still adds a small amount of query, change-tracking, and comparison work.
- Bad, because `xmin` is provider-specific and unsuitable as an audit timestamp, durable business revision, or public contract.
- Bad, because the whole row shares one token, so unrelated changes to the same record can cause a conflict.

### Optimistic concurrency using an ordinary version or timestamp column

Add a user-defined column and ensure every writer changes it through application logic or a PostgreSQL trigger.

- Good, because an explicit version can have durable, externally visible, or provider-neutral semantics when the use case requires them.
- Good, because application-managed generation can choose which property changes invalidate the token.
- Neutral, because a trigger centralizes generation for all writers but adds schema behavior that must be migrated, reviewed, and tested.
- Bad, because PostgreSQL has no ordinary auto-updating timestamp equivalent to SQL Server `rowversion`; a missed writer or trigger creates silent lost-update risk.
- Bad, because using wall-clock time as a version conflates audit and concurrency semantics and adds a column without improving the default internal EF Core case.

### Pessimistic concurrency using explicit row locks

Select and lock records before deciding and writing, using the weakest suitable PostgreSQL row-lock mode.

- Good, because exclusive access can simplify a narrow invariant or prevent duplicate work under heavy contention.
- Good, because `SKIP LOCKED` can support multiple workers claiming independent rows without waiting for one another.
- Neutral, because PostgreSQL already locks rows during updates; the decision here is whether to acquire a lock earlier and hold it while application logic runs.
- Bad, because blocking, deadlocks, lock timeouts, and longer transactions reduce throughput and increase operational complexity.
- Bad, because a lock cannot safely remain held across user interaction or external IMAP, SMTP, storage, AI, or MCP calls.

## More Information

- Microsoft Learn, "Handling Concurrency Conflicts," documents EF Core optimistic concurrency tokens, `DbUpdateConcurrencyException`, conflict resolution, retries, and the trade-offs of higher isolation levels: <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>.
- Npgsql, "Concurrency Tokens," documents mapping a `uint` property configured with `IsRowVersion()` to PostgreSQL's automatically changing `xmin` system column: <https://www.npgsql.org/efcore/modeling/concurrency.html>.
- PostgreSQL, "System Columns," defines `xmin` as the identity of the inserting transaction for the current row version: <https://www.postgresql.org/docs/current/ddl-system-columns.html>.
- PostgreSQL, "Explicit Locking," documents row-lock modes, blocking, deadlocks, and `SELECT ... FOR UPDATE`: <https://www.postgresql.org/docs/current/explicit-locking.html>.
- Microsoft Learn, "Testing EF Core Applications," states that EF Core in-memory has important behavioral differences from real databases, discourages mocked `DbSet` query testing, and notes that repository layers enable tests without EF Core at the cost of architectural and maintenance overhead: <https://learn.microsoft.com/en-us/ef/core/testing/>.
- Microsoft Learn, "Choosing a testing strategy," recommends real-database testing for production confidence, discourages the in-memory provider for test doubles, and states that a repository layer is the reliable way to stub database outputs without evaluating production LINQ against a fake provider: <https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy>.
- Microsoft Learn, "Testing without your production database system," shows repository interfaces returning `IAsyncEnumerable<T>` or `IEnumerable<T>` rather than `IQueryable<T>` so EF Core query translation does not leak into tests: <https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database>.
- Microsoft Learn, "Using Transactions," documents that one `SaveChanges` call is transactional by default when the provider supports transactions and describes explicit EF Core transaction control for multiple operations: <https://learn.microsoft.com/en-us/ef/core/saving/transactions>.
- Martin Fowler's Repository catalog entry describes Repository as a mediator between domain and data-mapping layers and notes that clients may submit declarative query specifications to repositories: <https://martinfowler.com/eaaCatalog/repository.html>.
- This ADR refines the MailMcp architecture draft's boundary rule that `Application` owns ports and `Infrastructure` owns persistence, EF Core, PostgreSQL, `bytea`, pgvector, and provider-specific mapping details: `specs/2026-07-22-mail-mcp-architecture-draft.md`.
