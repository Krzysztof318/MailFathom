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

Apply policy is explicit per environment. Draft section 17 accepts automatic startup migration for a single-owner first release; this specification implements it as an opt-in setting that is on by default only in Development, requires a deliberate opt-in elsewhere, and fails startup rather than serving traffic against an unknown or partially migrated schema. The repository rule that destructive or long-running migrations are not run automatically at production startup stands, so the policy check refuses to apply automatically when pending migrations are flagged as destructive.

The Development-only bootstrap from specification 07 is deleted in this change.

## Safety and privacy

A schema mismatch is a fail-fast startup error, never a warning, because running against an unknown schema risks writing mail data into a shape the deletion and retention paths do not reach. Migration logs record the migration identifiers applied and nothing about the data.

## Testing

Unit tests cover the apply-policy decision table: development default, non-development opt-in required, pending destructive migration refused, mismatch failing startup. Verification that the baseline migration actually produces the expected PostgreSQL schema, constraints, and indexes is the responsibility of specification 20, which runs immediately after this one and whose checks are the real acceptance evidence.

## Out of scope

Zero-downtime migration strategy, migration squashing after release, and moving long-running migrations into the future `mcpmail` CLI.

## Definition of done

- One reviewed baseline migration reproduces the full schema on an empty database.
- The Development-only bootstrap no longer exists.
- The `aspire exec` workflow for adding and applying migrations is documented and is the only documented workflow.
- Automatic apply is opt-in outside Development and refuses destructive pending migrations.
- `docs/operations/` documents the workflow and the per-environment policy.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
