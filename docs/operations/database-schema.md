# Applying the database schema

<!-- describes: backend/src/Infrastructure/Persistence/Migrations/**, backend/src/AppHost/**, scripts/build-schema-artifact.sh -->

MailFathom never applies a schema change while starting, in any environment. It verifies the migration history and
refuses to serve against a schema it does not recognize, so bringing a new version up *tells* you a migration is
outstanding rather than silently applying one — which is what leaves a point at which to take a backup, and what stops
two replicas racing to alter the same tables.

The artifact that answers that refusal is one file per release:

| | |
| --- | --- |
| `mailfathom-schema-<version>.sql` | Every migration the release defines, as the SQL PostgreSQL runs |
| `mailfathom-schema-<version>.sql.sha256` | The checksum that identifies the file |

Both are attached to the GitHub release. Nothing runs them for you.

A **nightly** carries its own pair, named after the nightly identifier, and they live on the `Nightly` workflow run that
built the image, under `schema-artifact`. That run is the only place they exist — a nightly is not a release and has
nothing to attach an asset to — so a nightly whose run has aged out is answered by generating the file from the revision
the image labels, as [what a release records](#what-a-release-records) describes.

## What the script is

It is an **idempotent** script: every migration is wrapped in a check against the `__EFMigrationsHistory` table, so a
database that already carries some of them takes only what it is missing. You do not have to know which migrations a
given installation holds in order to know which file to apply — there is one file, and applying it twice is applying it
once.

It writes one row as well as creating tables. The chain provisions the **owner** every mailbox is bound to — one
record, with the mail accounts this deployment already holds carried onto it — because a mailbox belongs to somebody
from the moment its row exists. It is written on the apply that introduces it and left alone by every apply after that,
so applying the file twice still provisions one owner.

Some migrations in the chain carry existing data onto a new shape as well, and one of them reads a table rather than
only rewriting a column: the per-owner stored-content counter is seeded from what the message payloads already hold, so
that apply scans the content table once. It reads the recorded lengths rather than the payloads beside them, so the cost
is a sequential scan rather than a detoast, but on a mailbox of hundreds of thousands of messages it is the part of the
apply that takes noticeable time.

It is also **only forward**. The script carries no reverse migrations, so it cannot undo anything, and nothing in
MailFathom can. [Rolling back](#rolling-back) is what that leaves.

And it is **UTF-8 with no byte-order mark**. `psql` does not skip one, so a marked file fails on its first statement
with a syntax error naming a character nothing displays, which is a confusing way to be told the file is fine and the
encoding is not. What EF Core generates does carry the mark; `scripts/build-schema-artifact.sh` removes it and refuses
to publish a file that still has one, so an artifact attached to a release is one `psql -f` accepts.

Read it before you run it. That is the whole reason the artifact is a SQL file rather than something that runs itself:

```bash
sha256sum --check 'mailfathom-schema-<version>.sql.sha256'
less 'mailfathom-schema-<version>.sql'
```

**`<version>` throughout this page is the release you are applying** — substitute the file you downloaded. Every
command quotes the name, so a line pasted without that substitution fails with a missing file rather than with a shell
redirection, and no page here has to be rewritten when a release is cut.

## The role that applies it

**Two facts decide which role runs the SQL, and both outlive whatever does.**

**The `vector` extension.** The schema installs it, and PostgreSQL does not permit an ordinary role to create an
extension. Either install it out of band, while a superuser is connected — which is what the Compose deployment's
initialization script does, so its `CREATE EXTENSION IF NOT EXISTS vector` then finds it already present — or run the
schema step as a role that may.

**Ownership follows whoever runs the DDL.** PostgreSQL makes the role that created a table, sequence, or index its
owner, and ownership grants nothing to anybody else. A schema applied by any role but the one MailFathom connects as
therefore leaves it failing on permission errors against a schema that plainly exists — the superuser included, which
is the easiest version of this mistake to make.

That leaves two arrangements, and a deployment is one or the other.

**One role applies and serves.** The extension is installed out of band, the serving role owns everything the script
created, and no grant is needed. This is the Docker Compose deployment:
`postgres/10-create-mailfathom-database.sh` creates the database owned by `mailfathom` and installs `vector` while a
superuser is still connected, precisely so that the schema step afterwards is an ordinary role's work. [Applying it
there](#docker-compose) is the command, and it connects as `mailfathom`.

A Helm deployment that lets the chart run its own PostgreSQL is the same arrangement for the same reason: the chart's
initialization script does what Compose's does, on the same terms, so its schema step is also an ordinary role's work.
A deployment pointing the chart at a server it operates chooses between the two arrangements like any other.

**A separate migrator applies it.** `mailfathom_migrator` owns what it created and `mailfathom` serves, which is the
shape to reach for wherever the privilege to alter the schema and the privilege to serve requests are meant to differ.
It costs the grants below: grant the service's role the privileges it needs, and set default privileges so the next
migration's objects are covered too.

```sql
GRANT USAGE ON SCHEMA public TO mailfathom;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO mailfathom;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO mailfathom;

ALTER DEFAULT PRIVILEGES FOR ROLE mailfathom_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO mailfathom;
ALTER DEFAULT PRIVILEGES FOR ROLE mailfathom_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO mailfathom;
```

Grant rather than transfer: handing the tables to the service's role would leave the migrator unable to alter them next
time, and would give the role that serves requests the privilege to drop what it serves.

### Every index is in the script

MailFathom issues no DDL at runtime. It creates no index, drops none, and alters no table, so every object the database
holds was created by whichever role applied the script, and the grants above are the whole of what the serving role
needs. No table is owned by the serving role — `email_embeddings` included, which is the one a deployment might expect
an exception for.

Vector search is exact rather than approximate, and that is what leaves a profile's activation with nothing to build.
[What a semantic search costs](../architecture/semantic-ranking-cost.md) holds the measurement behind it, and
[stored email schema](../architecture/stored-email-schema.md#the-vector-index-that-is-not-there) states what it means
for the table.

## Applying it

Take a backup first, and take it *before* the script rather than after a failure. The script needs nothing from
MailFathom — no EF Core, no migration tool, no MailFathom image — so anything that can run a SQL file against
PostgreSQL will do, including a managed provider's own query console.

`psql` is the shortest path. `ON_ERROR_STOP` is not optional: without it psql reports a failure and carries on to the
next statement.

```bash
psql "postgresql://mailfathom_migrator@db.internal:5432/mailfathom" \
  --set ON_ERROR_STOP=on \
  --file 'mailfathom-schema-<version>.sql'
```

Do not add `--single-transaction`. The script issues its own transaction statements, and psql's would nest around them.

### Docker Compose

The database publishes no port, and both database credentials are already mounted inside its container, so the
shortest route is to run psql there and hand it the script on standard input. Nothing puts the credential on a command
line that another process could read:

```bash
cd deploy/compose

docker compose exec --no-TTY postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
     --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'
```

**As `mailfathom`, never as `postgres`.** This is the single-role arrangement above, so the role that applies the
schema is the role that serves requests, and it is the one the objects have to end up owned by. Applying the script as
the superuser instead leaves MailFathom refusing to start with `42501: permission denied for table
__EFMigrationsHistory` — the schema is there and unreadable to the only role that needs it.

[Deploying with Docker Compose](deployment-compose.md#starting) is where that step sits in the sequence.

### Podman Quadlet

The same command against the same container arrangement, reached through Podman rather than through Compose:
`podman exec --interactive mailfathom-postgres sh -c '…'`, with the identical `sh -c` body and redirection. Everything
above holds unchanged, including the role the script must be applied as.
[Deploying with Podman Quadlet](deployment-quadlet.md#starting) is where that step sits in the sequence.

### Kubernetes

Run it from wherever the database is already reachable — a bastion, a maintenance pod, or a port-forward from your own
machine:

```bash
kubectl --namespace databases port-forward service/postgres 5432:5432 &

psql "postgresql://mailfathom_migrator@127.0.0.1:5432/mailfathom" \
  --set ON_ERROR_STOP=on \
  --file 'mailfathom-schema-<version>.sql'
```

Where the chart deployed the database, it is already reachable through its own pod, and the role that connects there is
the one that serves:

```bash
kubectl --namespace mailfathom exec -i statefulset/<release>-postgres -- \
  psql --username mailfathom --dbname mailfathom \
    --set ON_ERROR_STOP=on < 'mailfathom-schema-<version>.sql'
```

The chart renders no Job and no `initContainer` for this, deliberately. [Why the artifact is a SQL
script](#why-the-artifact-is-a-sql-script) states what each of those would cost.

### Locks and timeouts

Each migration runs inside its own transaction, so a failure rolls that migration back rather than leaving the schema
half changed — PostgreSQL runs DDL transactionally, which is what makes that true. A chain that fails partway keeps
whatever completed before it, and re-running the script resumes from there.

DDL takes strong locks. `ALTER TABLE` takes `ACCESS EXCLUSIVE` on the table it changes, and a non-concurrent
`CREATE INDEX` blocks writes to the table it indexes. Two consequences are worth planning for on a database that is
already serving:

- **A long-running transaction elsewhere blocks the migration**, and everything arriving behind the migration then
  queues behind the lock it is waiting for. Give the session a `lock_timeout` so the script fails fast instead of
  stalling the database:

  ```bash
  PGOPTIONS='-c lock_timeout=5s' psql "postgresql://mailfathom_migrator@db.internal:5432/mailfathom" \
    --set ON_ERROR_STOP=on --file 'mailfathom-schema-<version>.sql'
  ```

- **Index creation on a large table takes time proportional to the table.** Stop MailFathom, or accept that its writes
  wait, for the duration.

- **A `CHECK` constraint added to a table that already holds rows is validated by scanning it.**
  `AddContentStorageBackendAndObjectLocator` adds one to each of the four tables that hold raw MIME, and
  `IndexObjectBackedContentAndRequireItsPayloadEmpty` replaces all four with a stricter form, so each table is scanned
  under `ACCESS EXCLUSIVE` twice across the two. What a scan costs follows the row count rather than the mail volume:
  the predicate asks which backend a row names and whether its payload column is null, and neither question
  dereferences a payload PostgreSQL stored out of line. Nothing else in the pair is proportional to the table — the
  columns are added without a rewrite, since a column default is recorded in the catalog rather than written into every
  row, which is what makes `ADD COLUMN "Backend" … NOT NULL DEFAULT 'Database'` fast on a table of any size and what
  leaves every row written before that column existed reading as the thing it is; and the four indexes the second
  migration creates are partial, filtered to a backend no row on an upgrading deployment names, so each is built empty.
  `IndexContentObjectLocators` then drops those four and creates four in their place, keyed on the locator rather than
  on the backend and unique; the same filter makes each of the new ones empty on a deployment that stored nothing in an
  endpoint, and proportional to the object-backed rows rather than to the table on one that did. Uniqueness is what a
  deployment that already wrote such rows is held to at that moment, and a duplicate locator would fail the migration
  rather than be created — no writer this schema has ever carried could produce one, since every placement mints its
  own key. `RetainDatabaseCopyUntilReleased` replaces those four `CHECK` constraints once more, relaxing them so that an
  object-backed row may hold the payload the move left beside its object, and adds a nullable
  `ObjectVerifiedAt` column to each table. Each table is therefore scanned under `ACCESS EXCLUSIVE` once more, on the
  same terms as before — the predicate compares the backend name and null-tests the locator, the payload, and the new
  column, none of which dereferences a payload PostgreSQL stored out of line — and the column is added without a
  rewrite, since a nullable column with no default is a catalog change.

The first release's script creates a schema from nothing, so none of these applies to an empty database.

## Ordering a deployment

1. **Back up the database.** The backup point is before the script, because a migration only moves forward.
2. **Apply `mailfathom-schema-<version>.sql`.**
3. **Roll out the new image.**

That order is what the startup gate requires: the new build refuses to start until the migrations it defines are
present. It is also safe in the middle, while the old build is still running against the new schema — a database
carrying *more* migrations than a build defines has no pending migration for that build, so an instance of the previous
version keeps serving. What it does not do is use the new columns, which is why the window is a rollout rather than a
resting state.

**Two migrations narrow that window rather than closing it.** `AddOwnerAccounts` makes the owner of a mail account a
required column, and a build older than the release carrying it does not know the column exists — so against this
schema such a build serves the mail already stored and still fails the moment it has to bind a folder for an account it
has never synchronized, because the row it writes states no owner. `AddContactOwner` does the same for the contact
book: an older build reads and amends the contacts already stored and fails the moment it records a new person or adds
an address, because the row it writes states no owner either. Keep the middle of the rollout short on these releases,
and do not treat a previous image as something that can be left running against them.

## When the host refuses to start

Three failures are about the schema, and each names a different problem. They are distinguishable from an ordinary
startup failure by the code: everything in the `32xxx` range is persistence.

| Code | Failure | What it means | What to do |
| --- | --- | --- | --- |
| `32001` | `DatabaseSchemaOutOfDateException` | The database has not applied migrations this build defines, and the message names them | Apply the schema artifact for this version, then start the host again |
| `32002` | `DatabaseSchemaStateUnreadableException` | The schema state could not be established at all — an unreachable server, a database that was never created, or a role without rights on the migration history | Fix the connection, the database, or the grant. The message names the reason class only; the provider's own text is the inner exception, because it can carry a host, user, and database name |
| `32003` | `DatabaseSchemaTextSearchConfigurationMismatchException` | The lexical index was built with one PostgreSQL text search configuration and this host is configured for another, so searching would stem queries one way and read lexemes built another | Set `Persistence:TextSearchConfiguration` to the value the message reports the schema holds, or rebuild the index under the configured one |

`32001` is the expected state of a first install and of an upgrade whose schema step has not run yet. It is not a
defect, and the log line names exactly which migrations are missing.

Anything else that stops the host is not a schema problem: a secret reference that did not resolve, a configuration
value the options validation refused, or a port already taken all fail before or beside this gate and report their own
codes.

## Rolling back

**A migration only moves forward, and rolling the image back does not roll the schema back.** `helm rollback` and
re-pointing a Compose deployment at the previous tag both return the workload and neither returns the database.

That leaves two answers, and which one applies is decided before the upgrade rather than after it:

- **Restore from the backup taken at step 1.** This is the only way back to the previous schema, and it discards
  everything written since the backup. Synchronized mail is re-fetchable from the server; what is not is anything the
  release wrote that the previous build cannot read.
- **Fix forward.** The previous version's build starts against a schema ahead of it, as [ordering a
  deployment](#ordering-a-deployment) describes, so a defect in the new version can be answered by rolling the image
  back while leaving the schema where it is, and shipping the correction in the next release. This is the cheaper
  answer whenever the migration itself was not the problem. **It is not an answer for the release that carries
  `AddOwnerAccounts`**: a build older than that release cannot bind a folder for an account this deployment has never
  synchronized against a schema whose mail accounts require an owner, so rolling the image back leaves a deployment
  that serves the mail it holds and takes on no new mailbox. Restoring from the backup is the way back there.

  **Nor for the release that carries `AddContactOwner`**, for the same reason one table over: a build older than that
  release reads and amends the contacts already stored and fails the moment it records a new person or adds an address,
  because the row it writes states no owner. Rolling the image back leaves a deployment whose contact book can be read
  and not written — including by the collection pass, which writes a contact per correspondent it recognizes — so
  restoring from the backup is the way back there too.

  **`AddContentStorageBackendAndObjectLocator` narrows it conditionally**, and which way depends on what the deployment
  did rather than on the migration. A build older than that release reads a payload column it expects to be filled, and
  a row written to the object backend leaves that column empty — so rolling the image back is a complete answer for a
  deployment that configured no object endpoint and never wrote such a row, and is no answer at all for one that did.
  The schema alone does not say which: `SELECT count(*) FROM email_message_contents WHERE "Backend" <> 'Database'`, and
  the same over `outgoing_email_contents`, `mail_draft_contents`, and `recurring_send_drafts`, is the question to ask
  before choosing. The running deployment answers it too — the `object-backed-content` readiness check reports the same
  fact, as [health endpoints](health-endpoints.md) describes.

  **`RetainDatabaseCopyUntilReleased` costs a rollback nothing**, and is recorded here because it looks as though it
  should. A build older than that release reads an object-backed row from its object exactly as the newer one does, so a
  row still carrying the copy the move left beside it is served correctly; what such a build does not have is the
  fallback, the release, and the column, so it empties that copy at the next repoint rather than retaining it. Nothing
  is lost either way — the object is the authoritative copy under both builds.

## What a release records

Each release's notes carry the artifact's name, its SHA-256, and the migration identifiers it contains, so which schema
a version expects is answerable long after the fact and from the release alone. The reasoning behind the apply path
lives here rather than being restated per release.

Generating the artifact from a checkout produces the same file:

```bash
scripts/build-schema-artifact.sh                 # artifacts/schema/mailfathom-schema-<version>.sql
```

An image states the commit it was built from, so a build whose script you no longer have is answered by checking that
commit out and generating it. This is the path back for a nightly whose run has aged out, and the check that a file you
were handed is the one that build expects:

```bash
docker image inspect <reference> --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'
git checkout <that revision>
scripts/build-schema-artifact.sh
```

It comes from `aspire publish`, which reads the `PublishAsMigrationScript` declaration in the app model
(`backend/src/AppHost/Program.cs`) rather than from a second `dotnet ef` invocation written beside it — so the release path and
a developer's path state which context, which migrations project, and which options exactly once. The script reaches no
database: it reads the migration assembly, and produces identical output against a server that does not exist.

## Why the artifact is a SQL script

Three other shapes were available, and each was refused for a reason that still holds.

**A command inside the published service image.** `deploy/docker/Dockerfile` keeps the image free of any migration
tool, SQL, or credential that could apply one, which is what makes "the host never applies migrations" a property of
the artifact rather than a rule somebody has to remember. It is also the wrong role: the credentials the service runs
with are not the ones that may create the `vector` extension or run DDL, and a second entry point in an image that
otherwise stands and listens would put them in the same place. A turnkey path is a **separate** artifact the operator
invokes, whose credentials exist for one run;
[issue #259](https://github.com/Krzysztof318/MailFathom/issues/259) owns it, and the manual path above stays supported
whatever it produces.

**A migration bundle.** `PublishAsMigrationBundle` produces a self-contained executable, and an executable cannot be
read. Everything above asks the operator to read the SQL and take a backup against what it will do; a bundle would
leave that instruction with nothing to point at.

**A Helm hook Job, or an `initContainer` on the service Deployment.** A hook Job is the automatic migration this whole
arrangement exists to prevent — it runs because a deployment happened rather than because somebody decided to. An
`initContainer` is worse: one runs *per replica*, so `replicas: 3` means three concurrent applies serialized behind EF
Core's advisory lock, and a pod that fails for any reason retries the migration as part of its restart.

What a SQL file gives instead is the three things the others cannot: it can be read, a backup can be taken against it,
and running it is a decision.

## Related

- [Deploying with Docker Compose](deployment-compose.md), [Deploying with Podman Quadlet](deployment-quadlet.md), and
  [Deploying to Kubernetes](deployment-kubernetes.md) — where the schema step sits in each deployment
- [The container image](container-image.md#the-schema) — why the image carries no schema tool
- [The release procedure](release-procedure.md) — where the version in the artifact's name comes from
- [Local development](local-development.md#ef-core-design-time-commands) — the `mailfathom-migrations` resource, which
  is how a schema reaches a developer's own database and is not this
