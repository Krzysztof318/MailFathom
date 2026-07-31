# EF Core Migration Baseline and Apply Policy

**Roadmap group:** E — schema consolidation and infrastructure verification
**Draft delivery stage:** 1, deliberately rescheduled to the end of this segment
**Depends on:** 07, 08, 10, 12
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Consolidate the schema that specifications 07 through 12 grew in the EF Core model into one reviewed baseline migration, replace the Development-only bootstrap with an explicit migration workflow, and define how migrations are applied in each environment.

## Why this is last

Draft section 22 lists migrations in stage 1. Scheduling them there would have produced a dozen incremental migrations against a schema that was still changing shape, each needing review, and several partially reverting each other. Deferring produces one migration reviewed once against the settled schema. The cost is that nothing before this specification can run against a migrated database, which the Development-only bootstrap in specification 07 covers and which specification 20 replaces with real verification.

## Approved scope

The `Infrastructure` project gains a design-time `DbContext` factory so `dotnet ef` can build the model without starting the host. One baseline migration is generated for the full schema — accounts, folders, stored emails, message contents, checkpoints, extracted text, the generated `tsvector` column, and every index and constraint from specifications 07 through 12 — and reviewed as SQL, not only as generated C#.

The pgvector extension is enabled by this migration, which this specification originally deferred to the RAG stage on the grounds that no vector column exists yet. The image ships the extension but does not install it, so the deferral would have left the first vector column failing on a type PostgreSQL does not know, and the RAG stage needing a migration whose only content is one `CREATE EXTENSION`. Enabling it costs an empty database one catalogue entry.

Migrations are generated through Aspire so the command runs with the connection information from the app model rather than a hand-copied connection string.

**This section was written against `aspire exec`, which Aspire 13 does not have.** Earlier Aspire versions offered that command; the pinned CLI 13.4.6 does not, and its documentation is gone. The replacement is the `Aspire.Hosting.EntityFrameworkCore` package, which declares a migration resource in the app model. The AppHost adds it against the host project, points it at `src/Infrastructure` for the migrations, and calls `RunDatabaseUpdateOnStart`, so a local run applies pending migrations before the host starts. Commands are run against the resource:

```bash
aspire resource mailfathom-migrations ef-migrations-add --apphost src/AppHost/AppHost.csproj --non-interactive -- --name Initial
aspire resource mailfathom-migrations ef-database-update --apphost src/AppHost/AppHost.csproj --non-interactive
```

The package ships no stable build, so it is the repository's only prerelease pin. It is referenced by `AppHost` alone, so nothing it carries reaches a deployed assembly.

Apply policy is one rule rather than a per-environment pair, which is stricter than this specification originally described. **The host never applies migrations, in any environment, including Development.** It verifies at startup that the database carries every migration the running build defines and fails fast when any are pending, so an instance either serves traffic against a known schema or does not serve traffic at all. The Development branch this specification once allowed is unnecessary now that the orchestration applies migrations before the host starts, and it would have made two mechanisms own one concern, so that a given local schema could not be attributed to either. Outside Development, applying is an explicit deployment step, or the future `mcpmail` CLI when it exists.

While MailFathom is pre-release the baseline is regenerated rather than extended: a model change deletes `Initial` and recreates it, which destroys local data by design. The `add-migration` skill is that workflow. Making it additive is first-release work and is tracked separately.

Draft section 17 currently states that the service applies pending migrations automatically at first-release startup, calling that an arbitrary initial policy. That conflicts with the repository rule forbidding automatic production migrations during ordinary host startup, and the repository rule wins: an application instance that mutates schema while starting can race a second instance, can apply a destructive change no one reviewed at deploy time, and gives the operator no point at which to take a backup. This specification updates the draft accordingly rather than implementing the weaker policy.

The Development-only bootstrap from specification 07 is deleted in this change.

## Safety and privacy

A schema mismatch is a fail-fast startup error, never a warning, because running against an unknown schema risks writing mail data into a shape the deletion and retention paths do not reach. Migration logs record the migration identifiers applied and nothing about the data.

## Testing

Unit tests cover the startup gate: a matching schema starting normally, pending migrations failing startup while naming them, an unreadable migration history failing rather than defaulting to either outcome, and cancellation reaching the inspector. There is no environment branch to cover, because the host applies nothing anywhere.

Verification that the baseline migration actually produces the expected PostgreSQL schema, constraints, and indexes is the responsibility of specification 20, which runs immediately after this one and whose checks are the real acceptance evidence. Until then the evidence is a reviewed schema dump from the orchestrated database, produced by `scripts/dump-local-schema.sh`.

## Out of scope

Zero-downtime migration strategy, migration squashing after release, and moving long-running migrations into the future `mcpmail` CLI.

## Definition of done

- One reviewed baseline migration reproduces the full schema on an empty database.
- The Development-only bootstrap no longer exists.
- The `mailfathom-migrations` workflow for adding and applying migrations is documented and is the only documented workflow.
- No configuration setting exists that lets any host apply migrations at startup; pending migrations fail startup instead.
- Draft section 17 is updated to match this policy.
- `docs/operations/` documents the workflow and the per-environment policy.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
