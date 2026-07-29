# Aspire Integration Test Foundation

**Roadmap group:** E — schema consolidation and infrastructure verification
**Draft delivery stage:** deferred integration phase, draft section 6.2
**Depends on:** 19
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Introduce the integration-test suite the repository has been deferring, using Aspire orchestration in test mode to run the real host against real PostgreSQL, and pay off the verification debt that ADR 0001 and specifications 07, 08, 13, and 15 explicitly deferred.

## Approach

Integration tests drive the existing `AppHost` through `DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>` from the `Aspire.Hosting.Testing` package, then build and start the distributed application and wait on resource health before asserting. This reuses the orchestration the repository already defines — the pgvector-backed PostgreSQL resource and the host project — instead of maintaining a second, parallel container definition. `Aspire.Hosting.Testing` is pinned to the Aspire version already used, and recorded in `LICENSES.md` in the same change.

The test project targets xUnit v3 on Microsoft Testing Platform v2, matching the rest of the repository rather than the xUnit v2 examples in the upstream Aspire documentation. It lives at `tests/IntegrationTests/`, named for what it is rather than after a production boundary, because it verifies how the boundaries behave together. It is excluded from the unit-test conventions that forbid network, container, and database access, since those conventions govern unit tests specifically.

The app model gains a second topology rather than a second app host. Started with the argument `IntegrationTesting=true`, it names its container and volume with a shared `mailmcp-integrationtests` prefix instead of taking Aspire's random postfix and the path-derived volume name, so that a run killed rather than shut down leaves resources one filtered command removes; and it marks the MailMcp host project `WithExplicitStart`, so the resource stays in the model — the migration resource is defined on it and the connection string is issued to it — without a second MailMcp synchronizing mail underneath the data a test is asserting on.

The suite runs on request and nowhere else. `IsTestingPlatformApplication` is `false` for the project, which is what a solution-wide `dotnet test` uses to discover test projects, so neither the fast loop nor the coverage gate finds it and neither can absorb its coverage; the project stays in `MailMcp.slnx`, so build, analyzers, and formatting still cover it. `scripts/run-integration-tests.sh` is what starts it, and the GitHub workflow is manual dispatch only.

## Delivery

The foundation and the verifications it enables are separate units of work. This specification's foundation — the app model's test topology, the project, the shared orchestration fixture, the run script, the manual workflow, the documentation, and one test proving the harness reaches a real migrated database through the production registration path — is issue #54. The verifications listed under *Approved scope* are written against that fixture afterwards and are tracked separately, together with the integration coverage owed by every class currently marked `[RequiresIntegrationCoverage]`.

## Approved scope

The suite verifies what unit tests structurally cannot:

- The baseline migration from specification 19 applies cleanly to an empty database and produces the expected tables, constraints, and indexes.
- The unique constraint on account, folder, UIDVALIDITY, and UID rejects a duplicate occurrence, which is the PostgreSQL-side idempotency guarantee ADR 0001 left unverified.
- Raw MIME round-trips through the `bytea` content store with its recorded length and hash intact, including a value large enough to be stored out of line.
- Keyset pagination from specification 13 visits every row exactly once across pages, including equal and null timestamps, against real data volume.
- The full-text query from specification 15 uses the GIN index rather than a sequential scan, asserted from the query plan.
- Search query text containing SQL metacharacters and full-text operators is treated as data end to end, confirming against real PostgreSQL what specification 15 asserts at the infrastructure level.
- The migration policy from specification 19 behaves correctly against a real schema.

Whether the composed host itself is started under test is deliberately left open. The foundation keeps it unstarted, because a running MailMcp synchronizes mail underneath whatever a test is asserting on, and the host-level assertions above are reachable through the classes the host composes. Turning it on is a later decision with a stated reason, not a default.

The lifetime of the distributed application is shared across the suite rather than per test, with each test isolating itself through its own data rather than its own container, because starting the application per test would make the suite unusably slow.

`tests/AGENTS.md` currently forbids integration-test projects outright. This specification updates that rule to reflect the phase change, replacing the prohibition with the boundary that now applies: unit tests stay free of network, container, database, and file-system dependencies, and infrastructure verification belongs here.

## Safety and privacy

Test fixtures use synthetic mail data only. No real mailbox, real credential, or real personal data enters the repository. The suite provisions its own database resource through the app model and never points at a developer's or an operator's database.

## Testing

The suite is the test. Its own acceptance is that it fails when the schema or an index is wrong, and that it does not run as part of the unit-test gate, so the fast inner loop stays fast. The GitHub workflow is a separate, manually dispatched one rather than a job on the pull-request build.

It reports coverage of its own, over the classes marked `[RequiresIntegrationCoverage]` and nothing else, so the number reads as progress through the debt this suite exists to pay off. That report enforces no threshold and never merges into the aggregate the 85% gate reads: unit tests stay the only source of an enforced metric, so an expensive suite never has to run to know whether the gate passes and integration coverage can never mask missing unit coverage. The filter is derived from the marker rather than kept as a second list, and a class that gains real coverage loses its marker in the same change, leaving this report and rejoining the unit denominator.

## Out of scope

IMAP protocol verification, which specification 21 owns. SMTP verification with smtp4dev, which draft section 21.3 defers to the SMTP stage: MailMcp has no SMTP delivery path yet, so adding the image now would pin, license-review, and orchestrate a dependency with nothing to send to it. Load and performance testing.

Polly's chaos strategies, raised as a follow-up to specification 03 on issue #54, are also out. They inject failure into a resilience pipeline that already has unit coverage of its composition; what they would add is proof that an adapter survives a misbehaving dependency, which is worth doing once the adapters are under integration coverage at all. They belong to that later work, not to the foundation.

## Definition of done

- The suite runs against Aspire-orchestrated PostgreSQL with pgvector.
- Every deferred ADR 0001 and specification-level verification listed above is covered.
- `tests/AGENTS.md` reflects the new integration-test boundary.
- `LICENSES.md` records `Aspire.Hosting.Testing`.
- `docs/operations/` documents how to run the suite locally and what it leaves behind.
