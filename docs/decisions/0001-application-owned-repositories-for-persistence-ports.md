---
status: proposed
contact: MailMcp maintainers
date: 2026-07-24
deciders: MailMcp owner
consulted: Microsoft Learn EF Core testing guidance, Martin Fowler Repository pattern catalog, repository architecture research requested by the owner
informed: MailMcp contributors and future persistence implementers
---

# Use application-owned repository ports for persistence access and keep EF Core behind infrastructure adapters

## Context and Problem Statement

MailMcp follows clean architecture: `Application` owns use cases and ports, while `Infrastructure` owns EF Core, PostgreSQL, SQL details, and migrations. The initial architecture also requires fast isolated unit tests, future PostgreSQL-backed integration tests, explicit privacy/governance seams, and no leakage of EF Core or provider-specific types into `Application` or `Domain`.

The decision question is: should application use cases depend directly on EF Core objects in unit tests and production code, or should MailMcp introduce a repository-style persistence port; if repositories are used, should they be simple use-case/aggregate-specific contracts, capability-segregated CRUD interfaces, or a generic repository plus specification model?

## Decision Drivers

- Keep `Application` and `Domain` independent from EF Core, Npgsql, PostgreSQL, and future object-storage/search implementation details.
- Preserve true unit tests for application behavior without making EF Core in-memory, SQLite in-memory, or mocked `DbSet` behavior stand in for PostgreSQL query semantics.
- Keep first-release scope small and reviewable while preserving seams for auditability, privacy, retention, erasure, synchronization idempotency, and future storage migrations.
- Avoid a repository abstraction that becomes either a leaky `IQueryable` façade or a generic CRUD layer that hides important domain and consistency rules.
- Support provider-specific query verification later through integration tests against PostgreSQL rather than pretending a fake provider proves SQL behavior.
- Keep query result sizes bounded, deterministic, and shaped as application read models rather than returning mutable EF Core entity graphs.

## Considered Options

- Direct EF Core usage in application code and unit tests.
- Simple application-owned repository/query ports implemented with EF Core in `Infrastructure`.
- Generic repository with broad CRUD interfaces such as `ICanCreate<T>`, `ICanDelete<T>`, `ICanUpdate<T>`, and `ICanQuery<T>`.
- Generic repository with specification/query objects for complex query composition.

## Decision Outcome

Chosen option: "Simple application-owned repository/query ports implemented with EF Core in `Infrastructure`", because it gives MailMcp reliable application-layer unit-test seams without adopting a broad generic repository framework or exposing EF Core query composition outside the persistence adapter.

The decision is proposed, not accepted. The first implementation should introduce the smallest repository/query contracts required by active use cases, then revisit the abstraction shape after the first persistence-backed slices show real query pressure.

### Consequences

- Good, because application unit tests can stub repository outputs directly and focus on use-case behavior, authorization, pagination, idempotency, and privacy rules.
- Good, because EF Core LINQ, SQL translation, migrations, concurrency tokens, indexes, PostgreSQL full-text search, pgvector behavior, and raw MIME `bytea` access remain implementation concerns verified by future integration tests.
- Good, because persistence contracts can express domain language and bounded result contracts, such as mailbox timelines, synchronization checkpoints, message occurrence lookups, outbox leasing, and derived-index cleanup.
- Neutral, because every testable query must be represented by an application-owned method or query object rather than composed ad hoc from `IQueryable` in use cases.
- Bad, because the repository layer adds maintenance cost and can drift into an anemic CRUD wrapper if reviews do not enforce use-case-specific contracts.

## Decision Details

### Repository placement and dependency direction

Repository contracts are application ports. They belong in `src/Application` near the use case or subdomain that owns the persistence need. Implementations belong in `src/Infrastructure/Persistence/PostgreSql` and may use EF Core, Npgsql, provider-specific SQL, compiled queries, tracking, concurrency tokens, and transaction APIs internally.

`Domain` must not contain persistence repositories for ordinary data access. A domain interface is allowed only for pure domain policies that do not imply I/O, clocks, configuration, logging, or infrastructure.

### Contract style

Use explicit, behavior-oriented repository or query-port names. Prefer contracts such as `IMessageTimelineReader`, `ISynchronizationCheckpointStore`, `IMessageOccurrenceRepository`, `IMessageContentStore`, or `IOutboxLeaseStore` over `IRepository<T>`.

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

### Transactions and unit of work

Do not introduce a generic unit-of-work abstraction only because EF Core has `SaveChangesAsync`. Use-case handlers should define transaction boundaries conceptually, while the infrastructure adapter should implement them with EF Core transactions where a persistence operation requires atomicity.

If multiple repositories must participate in one transaction for a concrete use case, introduce an application-owned transaction coordinator port for that use case or refactor the operation behind a single store method that owns the consistency boundary. Avoid exposing `SaveChangesAsync` as a public application concern unless there is a clear batching contract.

## Validation

- Code review verifies that `Application` and `Domain` do not reference EF Core, Npgsql, PostgreSQL, `DbContext`, `DbSet`, `IQueryable`, migrations, provider-specific AI types, or persistence entities.
- Unit tests for application use cases stub application repository/query ports instead of using EF Core in-memory, SQLite in-memory, or mocked `DbSet` query behavior.
- Repository contracts document bounds, ordering, cancellation behavior, authorization assumptions, side effects, and privacy-sensitive data returned or persisted.
- Future PostgreSQL integration tests verify EF Core mappings, migrations, query translation, constraints, concurrency, full-text search, pgvector, raw MIME content storage, and any provider-specific SQL.
- Pull-request review rejects generic CRUD repositories, broad capability interfaces, or specification abstractions unless the change includes concrete repeated use cases and validation evidence.

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

## More Information

- Microsoft Learn, "Testing EF Core Applications," states that EF Core in-memory has important behavioral differences from real databases, discourages mocked `DbSet` query testing, and notes that repository layers enable tests without EF Core at the cost of architectural and maintenance overhead: <https://learn.microsoft.com/en-us/ef/core/testing/>.
- Microsoft Learn, "Choosing a testing strategy," recommends real-database testing for production confidence, discourages the in-memory provider for test doubles, and states that a repository layer is the reliable way to stub database outputs without evaluating production LINQ against a fake provider: <https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy>.
- Microsoft Learn, "Testing without your production database system," shows repository interfaces returning `IAsyncEnumerable<T>` or `IEnumerable<T>` rather than `IQueryable<T>` so EF Core query translation does not leak into tests: <https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database>.
- Martin Fowler's Repository catalog entry describes Repository as a mediator between domain and data-mapping layers and notes that clients may submit declarative query specifications to repositories: <https://martinfowler.com/eaaCatalog/repository.html>.
- This ADR refines the MailMcp architecture draft's boundary rule that `Application` owns ports and `Infrastructure` owns persistence, EF Core, PostgreSQL, `bytea`, pgvector, and provider-specific mapping details: `specs/2026-07-22-mail-mcp-architecture-draft.md`.
