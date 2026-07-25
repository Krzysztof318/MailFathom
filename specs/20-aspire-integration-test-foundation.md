# Aspire Integration Test Foundation

**Roadmap group:** E — schema consolidation and infrastructure verification
**Draft delivery stage:** deferred integration phase, draft section 6.2
**Depends on:** 19
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Introduce the integration-test suite the repository has been deferring, using Aspire orchestration in test mode to run the real host against real PostgreSQL, and pay off the verification debt that ADR 0001 and specifications 07, 08, 13, and 15 explicitly deferred.

## Approach

Integration tests drive the existing `AppHost` through `DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>` from the `Aspire.Hosting.Testing` package, then build and start the distributed application and wait on resource health before asserting. This reuses the orchestration the repository already defines — the pgvector-backed PostgreSQL resource and the host project — instead of maintaining a second, parallel container definition. `Aspire.Hosting.Testing` is pinned to the Aspire version already used, and recorded in `LICENSES.md` in the same change.

The test project targets xUnit v3 on Microsoft Testing Platform v2, matching the rest of the repository rather than the xUnit v2 examples in the upstream Aspire documentation. It lives at `tests/Integration.Tests/` and is excluded from the unit-test conventions that forbid network, container, and database access, since those conventions govern unit tests specifically.

## Approved scope

The suite verifies what unit tests structurally cannot:

- The baseline migration from specification 19 applies cleanly to an empty database and produces the expected tables, constraints, and indexes.
- The unique constraint on account, folder, UIDVALIDITY, and UID rejects a duplicate occurrence, which is the PostgreSQL-side idempotency guarantee ADR 0001 left unverified.
- Raw MIME round-trips through the `bytea` content store with its recorded length and hash intact, including a value large enough to be stored out of line.
- Keyset pagination from specification 13 visits every row exactly once across pages, including equal and null timestamps, against real data volume.
- The full-text query from specification 15 uses the GIN index rather than a sequential scan, asserted from the query plan.
- The host starts, reports healthy, and applies the migration policy from specification 19 correctly.

The lifetime of the distributed application is shared across the suite rather than per test, with each test isolating itself through its own data rather than its own container, because starting the application per test would make the suite unusably slow.

`AGENTS.md` currently forbids integration-test projects outright. This specification updates that rule to reflect the phase change, replacing the prohibition with the boundary that now applies: unit tests stay free of network, container, database, and file-system dependencies, and infrastructure verification belongs here.

## Safety and privacy

Test fixtures use synthetic mail data only. No real mailbox, real credential, or real personal data enters the repository. The suite provisions its own database resource through the app model and never points at a developer's or an operator's database.

## Testing

The suite is the test. Its own acceptance is that it runs in CI, fails when the schema or an index is wrong, and does not run as part of `dotnet test` for unit tests, so the fast inner loop stays fast. The CI workflow gains a separate job, and the coverage gate scope is reviewed so integration coverage does not mask missing unit coverage.

## Out of scope

IMAP protocol verification, which specification 21 owns. SMTP verification with smtp4dev, which draft section 21.3 defers to the SMTP stage. Load and performance testing.

## Definition of done

- The suite runs against Aspire-orchestrated PostgreSQL with pgvector and passes in CI.
- Every deferred ADR 0001 and specification-level verification listed above is covered.
- `AGENTS.md` reflects the new integration-test boundary.
- `LICENSES.md` records `Aspire.Hosting.Testing`.
- `docs/operations/` documents how to run the suite locally.
