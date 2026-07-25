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

The `Infrastructure` project gains a design-time `DbContext` factory so `dotnet ef` can build the model without starting the host. One baseline migration is generated for the full schema — accounts, folders, stored emails, message contents, checkpoints, extracted text, the generated `tsvector` column, and every index and constraint from specifications 07 through 12 — and reviewed as SQL, not only as generated C#. The pgvector extension is enabled by this migration if and only if a vector column exists by then; otherwise it is left to the RAG stage that introduces one.

Migrations are generated through Aspire so the command runs with the connection information from the app model rather than a hand-copied connection string:

```bash
aspire exec --resource mailmcp-host -- \
  dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Host
```

The equivalent `aspire exec … -- dotnet ef database update` applies migrations locally. The exact resource name, working directory, and any required `--start-resource` for the PostgreSQL dependency are verified against the Aspire CLI documentation and recorded in the operations documentation as the single supported workflow.

Apply policy differs by environment, and the difference is a hard boundary rather than a setting an operator can cross.

In Development the host may apply pending migrations at startup, which keeps the local loop convenient. Outside Development the host never applies migrations. It verifies at startup that the database schema matches the model's expected migration set and fails fast when migrations are pending, so an instance either serves traffic against a known schema or does not serve traffic at all. Applying them is an explicit deployment step run through `aspire exec … -- dotnet ef database update`, or through the future `mcpmail` CLI when it exists.

Draft section 17 currently states that the service applies pending migrations automatically at first-release startup, calling that an arbitrary initial policy. That conflicts with the repository rule forbidding automatic production migrations during ordinary host startup, and the repository rule wins: an application instance that mutates schema while starting can race a second instance, can apply a destructive change no one reviewed at deploy time, and gives the operator no point at which to take a backup. This specification updates the draft accordingly rather than implementing the weaker policy.

The Development-only bootstrap from specification 07 is deleted in this change.

## Safety and privacy

A schema mismatch is a fail-fast startup error, never a warning, because running against an unknown schema risks writing mail data into a shape the deletion and retention paths do not reach. Migration logs record the migration identifiers applied and nothing about the data.

## Testing

Unit tests cover the apply-policy decision table: Development applying pending migrations, every non-Development environment refusing to apply and failing startup when migrations are pending, a matching schema starting normally, and an unreadable or unknown migration history failing rather than defaulting to either branch. Verification that the baseline migration actually produces the expected PostgreSQL schema, constraints, and indexes is the responsibility of specification 20, which runs immediately after this one and whose checks are the real acceptance evidence.

## Out of scope

Zero-downtime migration strategy, migration squashing after release, and moving long-running migrations into the future `mcpmail` CLI.

## Definition of done

- One reviewed baseline migration reproduces the full schema on an empty database.
- The Development-only bootstrap no longer exists.
- The `aspire exec` workflow for adding and applying migrations is documented and is the only documented workflow.
- No configuration setting exists that lets a non-Development host apply migrations at startup; pending migrations there fail startup instead.
- Draft section 17 is updated to match this policy.
- `docs/operations/` documents the workflow and the per-environment policy.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
