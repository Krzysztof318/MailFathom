---
name: add-migration
description: Use when an EF Core model change needs a migration, or when the baseline migration has to be regenerated against the current model.
---

# Add Migration

Regenerates the single baseline migration from the current EF Core model, through the Aspire orchestration.

**This workflow is development-only and destroys local data.** While MailMcp is pre-release the repository keeps exactly
one migration, `Initial`. A model change does not add a second migration; it replaces the first one. That keeps the
schema reviewable as one artifact against a model that is still moving, instead of accumulating incremental migrations
that partially revert each other. The first release replaces this workflow with an additive one — see the issue linked
from `docs/operations/local-development.md`.

## Preconditions

- Docker is running, and the model change is already saved and compiles.
- A deployment that configures a non-default `Persistence:TextSearchConfiguration` exports it before step 5, because
  the value is compiled into the search vector's stored generated column and the migration is generated for exactly
  one configuration:

  ```bash
  export Persistence__TextSearchConfiguration=english
  ```

  The host compares the configured value against the live schema at startup and refuses to run on a mismatch, so
  skipping this produces a startup failure rather than a silently wrong index.
- Never run `dotnet ef` by hand. Every command below goes through the orchestration, so it uses the connection string
  the AppHost issues rather than one written by hand.
- Work from the repository root.

## Workflow

1. **Confirm the model builds.**

   ```bash
   dotnet build src/Host/Host.csproj --nologo
   ```

   Always build `Host`, never `Infrastructure` on its own. `Host` is the startup project the migration resource runs
   `dotnet-ef` against, and the tool loads `MailMcp.Infrastructure.dll` from `Host`'s output directory. Building only
   `Infrastructure` leaves a stale copy there, and the tool then reads the model and the migration list that the
   previous build produced. Building `Host` rebuilds `Infrastructure` as a dependency and refreshes that copy.

   A migration generated from a project that does not compile fails with a message about the tool rather than about the
   model, which is the slowest way to learn that a mapping is wrong.

2. **Start the orchestration** and wait until `postgres` and `mailmcp` report `Healthy`.

   ```bash
   aspire start --apphost src/AppHost/AppHost.csproj --non-interactive
   aspire describe --apphost src/AppHost/AppHost.csproj --non-interactive
   ```

   `mailmcp-migrations` applies whatever migration currently exists when the AppHost starts, so the database matches the
   old model at this point. That is expected.

   **Never drop the database as a separate step.** `Drop Database` leaves the `mailmcp` resource unhealthy, and every
   later command on the migration resource waits for that resource to become healthy before it runs — so the command
   that would recreate the database waits forever on the database it is about to create. Step 7 uses `Reset Database`,
   which drops and recreates in one command while the resource is still healthy.

3. **Delete the existing migration**, if there is one.

   ```bash
   rm -rf src/Infrastructure/Persistence/Migrations
   ```

   Deleting the directory is deliberate rather than running `Remove Migration`: removing a migration that is already
   applied to the local database fails, and the whole point here is that the old one is being discarded rather than
   unwound. `MailMcpDbContextModelSnapshot.cs` goes with it, which is what makes the regenerated migration describe the
   whole schema instead of a difference from the model it replaced.

4. **Rebuild** so the tool sees an assembly with no migrations in it.

   ```bash
   dotnet build src/Host/Host.csproj --nologo
   ```

   Skipping this fails the next step with `The name 'Initial' is used by an existing migration`, because the deleted
   migration is still compiled into the assembly the tool loads.

5. **Generate the baseline migration.** The name is always `Initial`.

   ```bash
   aspire resource mailmcp-migrations ef-migrations-add --apphost src/AppHost/AppHost.csproj --non-interactive -- --name Initial
   ```

   Three files appear under `src/Infrastructure/Persistence/Migrations/`: the migration, its designer file, and the
   model snapshot. All three are committed.

6. **Rebuild**, for the reason given in step 4: the next command applies what is in the assembly, not what is on disk.

   ```bash
   dotnet build src/Host/Host.csproj --nologo
   ```

7. **Reset the database**, which drops it and recreates it with the regenerated migration applied.

   ```bash
   aspire resource mailmcp-migrations ef-database-reset --apphost src/AppHost/AppHost.csproj --non-interactive
   ```

   The data volume outlives the container, so the schema the replaced migration created survives a restart and would
   make a plain `Update Database` fail on objects that already exist. This is the step that makes the recreation clean,
   and it is why the workflow destroys local mail data every time it runs.

8. **Review the result as SQL, not only as C#.** This is the step the whole workflow exists for, and it is not
   optional: the generated C# hides what PostgreSQL was actually asked to do. Dump the schema the migration produced:

   ```bash
   scripts/dump-local-schema.sh
   ```

   Read the dump for: every table, column type, and nullability the model intended; the unique constraints and indexes
   each documented query shape needs; the generated `tsvector` column and its text search configuration; `ON DELETE`
   behavior on each foreign key; and any object the model did not intend. Fix the model and start again from step 3
   rather than editing the generated migration, which the next regeneration discards.

   The dump is a review artifact, not a repository file. Do not commit it.

9. **Prove the host accepts the schema.** Restart the orchestration and confirm `mailmcp-host` reaches
   `Running` and `Healthy`.

   ```bash
   aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
   aspire start --apphost src/AppHost/AppHost.csproj --non-interactive
   aspire describe --apphost src/AppHost/AppHost.csproj --non-interactive
   ```

   The host verifies the migration history at startup and refuses to run against a schema it does not recognize, so a
   healthy host is the evidence that the migration and the model agree. A host stuck in `Waiting` with
   `mailmcp-migrations` in `FailedToStart` means the migration did not apply; read its log with
   `aspire logs mailmcp-migrations`.

10. **Update the schema documentation** in `docs/architecture/stored-email-schema.md` when the change is visible to a
    reader of the schema, and commit the migration with the model change that caused it. A migration committed on its
    own cannot be reviewed, because the reason for it is in the other commit.

## When something is stuck

- **`postgres` stays `Unhealthy` with `password authentication failed`.** The data volume was initialized with a
  different generated password. Stop the orchestration, remove the volume, and start again:

  ```bash
  aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
  docker rm -f $(docker ps -aq --filter volume=mailmcp.apphost-9beaf2538a-postgres-data)
  docker volume rm mailmcp.apphost-9beaf2538a-postgres-data
  ```

- **`aspire start` times out while building.** Orphaned helper processes from an earlier failed start starve the
  build. `pkill -9 -f aspire-managed`, then start again.
