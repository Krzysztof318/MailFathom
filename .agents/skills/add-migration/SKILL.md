---
name: add-migration
description: Use when an EF Core model change needs a migration, or when a migration has to be reviewed as SQL before it is committed.
---

# Add Migration

Appends one EF Core migration for the current model, reviews it as SQL, and applies it to the orchestrated database.

**Every migration in this repository is permanent.** Nothing regenerates, renames, reorders, or deletes one, and no
command here does either. A migration identifier is written into `__EFMigrationsHistory` on every database that applies
it, so a regenerated baseline leaves that database recording an identifier no later migration can reach — it can then
only be recreated, which means destroying whatever mail it held. The workflow is additive for every database that
exists, which includes the persistent local one, not only the ones a release will create.

Clearing local data is a different operation with a different command, and it touches no file:

```bash
aspire resource mailfathom-migrations ef-database-reset --apphost src/AppHost/AppHost.csproj --non-interactive
```

## Preconditions

- The model change is saved. Docker is required only from step 3 onward, not to generate a migration.
- A deployment that configures a non-default `Persistence:TextSearchConfiguration` exports it first, because the value
  is compiled into the search vector's stored generated column and a migration is generated for exactly one
  configuration:

  ```bash
  export Persistence__TextSearchConfiguration=english
  ```

  The host compares the configured value against the live schema at startup and refuses to run on a mismatch, so
  skipping this produces a startup failure rather than a silently wrong index.

## Workflow

### 1. Generate

```bash
scripts/add-migration.sh <MigrationName>
```

The name describes what the migration does to the schema, in PascalCase — `AddEmailRetentionPolicy`, not `Update2`. It
becomes part of a permanent identity, so it is chosen once. The script refuses `Initial`, refuses a name already in
use, builds the startup project so the tool reads the current model, generates the migration, and writes the copyright
and license header into it.

Nothing here starts the orchestration or touches a database. Generating a migration compares the model against the
committed model snapshot and writes files, which has the same answer whether or not PostgreSQL is running, so the
script points the design-time factory at a port nothing listens on — if generation ever starts requiring a database, it
fails rather than silently reaching one.

Three files change under `src/Infrastructure/Persistence/Migrations/`: the new migration, its designer file, and the
model snapshot. The snapshot is the only existing file the workflow rewrites, and EF owns it — never hand-edit it. The
`.editorconfig` beside them is hand-written and relaxes the two diagnostics EF's generator trips.

**An empty migration means the model did not change.** EF generates one with empty `Up` and `Down` methods rather than
failing, so delete it and find out why the change did not reach the model — a configuration not applied, a property on
a type the context does not map, a build that did not run. An intentionally SQL-only migration is the one exception: it
is written deliberately, says in a comment why it carries no model change, and is reviewed as SQL like any other.

### 2. Review it as SQL, not only as C#

This is the step the whole workflow exists for, and it is the one no script performs: the generated C# hides what
PostgreSQL was actually asked to do.

```bash
scripts/script-migration.sh
```

With no arguments this scripts the newest migration from the one before it, which is the range a review has to cover.
`scripts/script-migration.sh 0` produces the whole schema from an empty database instead.

Read it for: destructive operations, and in particular a rename EF inferred as a drop followed by an add, which loses
every value in the column; the PostgreSQL types, nullability, and defaults each column ends up with; the indexes and
constraints the documented query shapes need; `ON DELETE` behavior on each foreign key; the generated `tsvector` column
and its text search configuration; and the lock and duration a statement implies on a table that already holds mail.

Fix the model and regenerate rather than hand-editing the generated migration — but once a migration has been applied
anywhere but your own machine, it is fixed forward by another migration instead.

### 3. Apply it to the orchestrated database

```bash
aspire resource mailfathom-migrations ef-database-update --apphost src/AppHost/AppHost.csproj --non-interactive
```

This is where a database is required and therefore where Aspire is: the command runs against the server the
orchestration issues a connection string for. Apply it to a database that already holds data rather than an empty one,
because applying only what the history says is missing, without losing what is there, is the behavior a released
installation gets and the only part of this workflow a clean database cannot demonstrate.

```bash
scripts/dump-local-schema.sh
```

Read the dump to confirm the schema PostgreSQL now holds is the one the migration described.

### 4. Prove the host accepts the schema

```bash
aspire stop  --apphost src/AppHost/AppHost.csproj --non-interactive
aspire start --apphost src/AppHost/AppHost.csproj --non-interactive
aspire describe --apphost src/AppHost/AppHost.csproj --non-interactive
```

`mailfathom-host` must reach `Running` and `Healthy`. The host verifies the migration history and the lexical index's
text search configuration at startup and refuses to run against a schema it does not recognize, so a healthy host is
the evidence that the migration and the model agree. A host stuck in `Waiting` with `mailfathom-migrations` in
`FailedToStart` means the migration did not apply; read `aspire logs mailfathom-migrations`.

### 5. Commit it with the model change

Update `docs/architecture/stored-email-schema.md` when the change is visible to a reader of the schema, and commit the
migration together with the model change that caused it. A migration committed on its own cannot be reviewed, because
the reason for it is in the other commit.

CI runs `dotnet ef migrations has-pending-model-changes` on any pull request touching `src/`, so a model change that
reaches `main` without its migration fails before merge rather than at a host's startup.

## When something is stuck

- **`Changes have been made to the model since the last migration` in CI, but nothing looks changed.** The model
  snapshot is stale rather than the model. Configuration that produces no SQL — a constraint name, an index filter —
  still moves the snapshot, so the fix is a migration whose `Up` is empty by design or a regenerated snapshot as part
  of the next real migration.

- **`postgres` stays `Unhealthy` with `password authentication failed`.** The data volume was initialized with a
  different generated password. Find this checkout's volume, stop the orchestration, remove it, and start again:

  ```bash
  docker volume ls --filter name=-postgres-data
  aspire stop --apphost src/AppHost/AppHost.csproj --non-interactive
  docker rm -f $(docker ps -aq --filter volume=<volume>)
  docker volume rm <volume>
  ```

  Aspire names the volume after the AppHost project's path, so each worktree owns its own and the name is read rather
  than assumed. Removing another worktree's volume destroys a database this repair was not about.

- **`dotnet build` hangs at "Determining projects to restore".** A previous orchestration is still holding the build
  output. `aspire stop` does not always reap it; `pkill -9 -f '/home/krzysiek/.aspire/versions'` and
  `pkill -9 -f aspire.hosting.orchestration` clear it. Orphaned `aspire-managed` helpers from a failed start do the
  same by starving the machine — `pkill -9 -f aspire-managed`.

- **`Another command is already running on this resource`.** The startup migration run has not finished. Wait for
  `mailfathom-migrations` to reach `Finished` in `aspire describe`.

- **`error IDE0073` on a generated migration.** The formatter did not reach it. `--include` has to name the files
  rather than their directory, and the scope has to be the solution: these files belong to `Infrastructure`, and an
  `--include` filter evaluated against another project's workspace silently matches nothing and still exits `0`.
