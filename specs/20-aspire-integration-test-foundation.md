# Aspire Integration Test Foundation

**Roadmap group:** E — schema consolidation and infrastructure verification
**Draft delivery stage:** deferred integration phase, draft section 6.2
**Depends on:** 19
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Introduce the integration-test harness the repository has been deferring, using Aspire orchestration in test mode to run against real PostgreSQL, so that the verification debt ADR 0001 and specifications 07, 08, 13, and 15 explicitly deferred finally has somewhere to be written.

Paying that debt off is what this specification enables, not what it defines. The debt is not one specification's: ADR 0001 deferred it across the persistence layer, and the classes marked `[RequiresIntegrationCoverage]` are its inventory. It is therefore tracked outside this specification, under the *What this enables* section below, so that the harness can be finished, reviewed, and closed as the self-contained piece of work it is.

## Approach

Integration tests drive the existing `AppHost` through `DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>` from the `Aspire.Hosting.Testing` package, then build and start the distributed application and wait on resource health before asserting. This reuses the orchestration the repository already defines — the pgvector-backed PostgreSQL resource and the host project — instead of maintaining a second, parallel container definition. `Aspire.Hosting.Testing` is pinned to the Aspire version already used, and recorded in `THIRD_PARTY_LICENSES.md` in the same change.

The test project targets xUnit v3 on Microsoft Testing Platform v2, matching the rest of the repository rather than the xUnit v2 examples in the upstream Aspire documentation. It lives at `tests/IntegrationTests/`, named for what it is rather than after a production boundary, because it verifies how the boundaries behave together. It is excluded from the unit-test conventions that forbid network, container, and database access, since those conventions govern unit tests specifically.

The app model gains a second topology rather than a second app host. Started with the argument `IntegrationTesting=true`, it names its container and volume with a shared `mailfathom-integrationtests` prefix instead of taking Aspire's random postfix and the path-derived volume name, so that a run killed rather than shut down leaves resources one filtered command removes; and it marks the MailFathom host project `WithExplicitStart`, so the resource stays in the model — the migration resource is defined on it and the connection string is issued to it — without a second MailFathom synchronizing mail underneath the data a test is asserting on.

The suite runs on request and nowhere else. `IsTestingPlatformApplication` is `false` for the project, which is what a solution-wide `dotnet test` uses to discover test projects, so neither the fast loop nor the coverage gate finds it and neither can absorb its coverage; the project stays in `MailFathom.slnx`, so build, analyzers, and formatting still cover it. `scripts/run-integration-tests.sh` is what starts it, and the GitHub workflow is manual dispatch only.

## Approved scope

The harness and nothing written with it:

- The app model's second topology, selected by argument, with ephemeral prefixed container and volume names and the MailFathom host project left unstarted.
- The `tests/IntegrationTests` project and the assembly-scoped orchestration fixture every test draws its database from.
- The exclusion from the unit-test gate, the run script, and the manually dispatched workflow.
- The suite's own coverage report over the classes marked `[RequiresIntegrationCoverage]`, enforcing nothing.
- One test proving the harness reaches a real migrated database through the production registration path, which is what makes the fixture's contract real rather than asserted.

Whether the composed host itself is started under test is deliberately left open. The foundation keeps it unstarted, because a running MailFathom synchronizes mail underneath whatever a test is asserting on, and the host-level assertions above are reachable through the classes the host composes. Turning it on is a later decision with a stated reason, not a default.

The lifetime of the distributed application is shared across the suite rather than per test, with each test isolating itself through its own data rather than its own container, because starting the application per test would make the suite unusably slow.

`tests/AGENTS.md` currently forbids integration-test projects outright. This specification updates that rule to reflect the phase change, replacing the prohibition with the boundary that now applies: unit tests stay free of network, container, database, and file-system dependencies, and infrastructure verification belongs here.

## Safety and privacy

Test fixtures use synthetic mail data only. No real mailbox, real credential, or real personal data enters the repository. The suite provisions its own database resource through the app model and never points at a developer's or an operator's database.

## Testing

The suite is the test. Its own acceptance is that it fails when the schema or an index is wrong, and that it does not run as part of the unit-test gate, so the fast inner loop stays fast. The GitHub workflow is a separate, manually dispatched one rather than a job on the pull-request build.

It reports coverage of its own, over the classes marked `[RequiresIntegrationCoverage]` and nothing else, so the number reads as progress through the debt this suite exists to pay off. That report enforces no threshold and never merges into the aggregate the 85% gate reads: unit tests stay the only source of an enforced metric, so an expensive suite never has to run to know whether the gate passes and integration coverage can never mask missing unit coverage. The filter is derived from the marker rather than kept as a second list. A covered class keeps its marker, because the marker records where a class's verification lives rather than whether it has been written: removing it would move structurally unreachable code into the enforced denominator at nearly zero, so writing an integration test would lower the aggregate and hide the coverage it just produced.

## What this enables

The verifications a unit test structurally cannot perform are written against this fixture afterwards, and are tracked outside this specification because they are not this specification's scope. ADR 0001 deferred them across the persistence layer and the classes marked `[RequiresIntegrationCoverage]` are their inventory, which is broader than anything one specification defines. They include:

- The baseline migration from specification 19 applying cleanly to an empty database and producing the expected tables, constraints, and indexes, and its apply policy behaving correctly against a real schema.
- The unique constraint on account, folder, UIDVALIDITY, and UID rejecting a duplicate occurrence, which is the PostgreSQL-side idempotency guarantee ADR 0001 left unverified.
- Raw MIME round-tripping through the `bytea` content store with its recorded length and hash intact, including a value large enough to be stored out of line.
- Keyset pagination from specification 13 visiting every row exactly once across pages, including equal and null timestamps, against real data volume.
- The full-text query from specification 15 using the GIN index rather than a sequential scan, asserted from the query plan, and query text containing SQL metacharacters and full-text operators treated as data end to end.

None of these is a condition of this specification being done. The harness is what this specification owes; the coverage report is how the work they belong to is measured.

## Out of scope

IMAP protocol verification, which specification 21 owns. SMTP verification with smtp4dev, which draft section 21.3 defers to the SMTP stage: MailFathom has no SMTP delivery path yet, so adding the image now would pin, license-review, and orchestrate a dependency with nothing to send to it. Load and performance testing.

Polly's chaos strategies, raised as a follow-up to specification 03 on issue #54, are also out. They inject failure into a resilience pipeline that already has unit coverage of its composition; what they would add is proof that an adapter survives a misbehaving dependency, which is worth doing once the adapters are under integration coverage at all. They belong to that later work, not to the foundation.

## Definition of done

- The suite runs against Aspire-orchestrated PostgreSQL with pgvector, and one test proves it reaches the migrated database through the production registration path.
- The suite is absent from the fast loop, the enforced coverage gate, and every pull-request workflow, and leaves no container or volume behind.
- The coverage report exists, is scoped to the marked classes, and enforces nothing.
- `tests/AGENTS.md` reflects the new integration-test boundary.
- `THIRD_PARTY_LICENSES.md` records `Aspire.Hosting.Testing`.
- `docs/operations/` documents how to run the suite locally and what it leaves behind.
