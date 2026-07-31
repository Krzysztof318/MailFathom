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

- Docker is running, and the model change is saved.
- A deployment that configures a non-default `Persistence:TextSearchConfiguration` exports it first, because the value
  is compiled into the search vector's stored generated column and a migration is generated for exactly one
  configuration:

  ```bash
  export Persistence__TextSearchConfiguration=english
  ```

  The host compares the configured value against the live schema at startup and refuses to run on a mismatch, so
  skipping this produces a startup failure rather than a silently wrong index.

## Workflow

### 1. Regenerate

```bash
scripts/regenerate-migration.sh
```

The script discards the existing migration, starts the orchestration if it is not already running, waits for PostgreSQL
and for the startup migration run to settle, regenerates `Initial`, writes the copyright and license header into it,
and drops and recreates the database with it applied. It reuses a running orchestration, which is what keeps a second
run cheap: starting one rebuilds the AppHost and is the single largest cost here.

Three files appear under `src/Infrastructure/Persistence/Migrations/`: the migration, its designer file, and the model
snapshot. All three are committed. The `.editorconfig` beside them is hand-written and is not regenerated — the script
deletes the `.cs` files rather than the directory for exactly that reason. It relaxes the two diagnostics EF's
generator trips and deliberately leaves `IDE0073` enforced, which is why the script runs the formatter over the
generated migration: the copyright and license header is a property of every file here, and the verification loop
formats only after a successful build that a missing header would fail.

### 2. Review the result as SQL, not only as C#

This is the step the whole workflow exists for, and it is the one the script deliberately does not do: the generated C#
hides what PostgreSQL was actually asked to do.

```bash
scripts/dump-local-schema.sh
```

Read the dump for: every table, column type, and nullability the model intended; the unique constraints and indexes
each documented query shape needs; the generated `tsvector` column and its text search configuration; `ON DELETE`
behavior on each foreign key; the extensions the schema creates; and any object the model did not intend. Fix the model
and run the script again rather than editing the generated migration, which the next regeneration discards.

The dump is a review artifact, not a repository file. Do not commit it.

### 3. Prove the host accepts the schema

```bash
aspire stop  --apphost src/AppHost/AppHost.csproj --non-interactive
aspire start --apphost src/AppHost/AppHost.csproj --non-interactive
aspire describe --apphost src/AppHost/AppHost.csproj --non-interactive
```

`mailmcp-host` must reach `Running` and `Healthy`. The host verifies the migration history and the lexical index's text
search configuration at startup and refuses to run against a schema it does not recognize, so a healthy host is the
evidence that the migration and the model agree. A host stuck in `Waiting` with `mailmcp-migrations` in `FailedToStart`
means the migration did not apply; read `aspire logs mailmcp-migrations`.

### 4. Commit it with the model change

Update `docs/architecture/stored-email-schema.md` when the change is visible to a reader of the schema, and commit the
migration together with the model change that caused it. A migration committed on its own cannot be reviewed, because
the reason for it is in the other commit.

## When something is stuck

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

- **`Another command is already running on this resource`.** The startup migration run has not finished. The script
  waits for it; a hand-run command has to wait too.

- **`The name 'Initial' is used by an existing migration`.** The startup project was not rebuilt after the migration
  files were deleted. `dotnet-ef` loads `MailMcp.Infrastructure.dll` from `Host`'s output directory, so `Host` is what
  has to be rebuilt — never `Infrastructure` on its own. The script does this; a hand-run command has to as well.

- **`error IDE0073` on a generated migration.** The formatter did not reach it. `--include` has to name the files
  rather than their directory, and the scope has to be the solution: these files belong to `Infrastructure`, and an
  `--include` filter evaluated against another project's workspace silently matches nothing and still exits `0`.
