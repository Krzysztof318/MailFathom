# Applying the database schema

<!-- describes: src/Infrastructure/Persistence/Migrations/**, src/Infrastructure/Persistence/Embeddings/EmbeddingProfileVectorIndex.cs, src/AppHost/**, scripts/build-schema-artifact.sh -->

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

It is also **only forward**. The script carries no reverse migrations, so it cannot undo anything, and nothing in
MailFathom can. [Rolling back](#rolling-back) is what that leaves.

Read it before you run it. That is the whole reason the artifact is a SQL file rather than something that runs itself:

```bash
sha256sum --check 'mailfathom-schema-<version>.sql.sha256'
less 'mailfathom-schema-<version>.sql'
```

**`<version>` throughout this page is the release you are applying** — substitute the file you downloaded. Every
command quotes the name, so a line pasted without that substitution fails with a missing file rather than with a shell
redirection, and no page here has to be rewritten when a release is cut.

## The role that applies it

**It needs privileges the service's role does not, and it should not be the service's role.** Two facts drive that,
and both outlive whatever runs the SQL.

**The `vector` extension.** The schema installs it, and PostgreSQL does not permit an ordinary role to create an
extension. Either install it out of band, while a superuser is connected — which is what the Compose deployment's
initialization script does, so its `CREATE EXTENSION IF NOT EXISTS vector` then finds it already present — or run the
schema step as a role that may.

**Ownership follows whoever runs the DDL.** PostgreSQL makes the role that created a table, sequence, or index its
owner, and ownership grants nothing to anybody else. A schema applied by `mailfathom_migrator` therefore leaves
MailFathom failing on permission errors against a schema that plainly exists. Grant the service's role the privileges
it needs, and set default privileges so the next migration's objects are covered too:

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

### The one index MailFathom creates itself

Every index in the script is the migrating role's, with one exception that is not in the script at all. The approximate
index over stored vectors covers one embedding profile's width, and no migration can know a width that a later
activation chooses — so MailFathom issues that `CREATE INDEX` itself, under the role it connects as. [Stored email
schema](../architecture/stored-email-schema.md#the-index-no-migration-creates) describes the index and why it has to be
per profile.

**Creating an index requires owning the table.** PostgreSQL treats the right to modify an object as inherent in being
its owner and offers no privilege that grants it separately, so the two roles above need one more arrangement before
that index can be built:

```sql
GRANT mailfathom TO mailfathom_migrator;
ALTER TABLE email_embeddings OWNER TO mailfathom;
```

The grant comes first because a role may only hand a table to one it is a member of, and it is what keeps the migrator
able to alter that table in a later migration — it acts there as a member of the role that now owns it. This is the one
table the serving role owns, and it is the price of an index a migration cannot contain. A deployment where a single
role both applies the schema and serves requests — which the Docker Compose deployment is — already satisfies this and
needs neither statement.

**How long it takes depends on when it is built.** The index covers only the rows its predicate selects, so a profile
indexed before its generation holds any vectors is built in an instant, and every vector written afterwards enters the
index as it is stored. Building one over a generation that is already embedded takes time proportional to the vectors
in it, and a non-concurrent `CREATE INDEX` blocks writes to `email_embeddings` for that time — the same caution
[Locks and timeouts](#locks-and-timeouts) states for the script.

**A refusal costs performance and nothing else.** Where the privilege is missing, or the build fails for any other
reason, MailFathom reports which profile and why, and the vectors are untouched. Vector search over that profile stays
exact until an index exists: correct, and linear in the number of vectors it reads.

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

The database publishes no port, and the superuser password is already mounted inside its container, so the shortest
route is to run psql there and hand it the script on standard input. Nothing puts the credential on a command line
that another process could read:

```bash
cd deploy/compose

docker compose exec --no-TTY postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/postgres-superuser-password)" exec psql \
     --username postgres --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'
```

[Deploying with Docker Compose](deployment-compose.md#starting) is where that step sits in the sequence.

### Kubernetes

Run it from wherever the database is already reachable — a bastion, a maintenance pod, or a port-forward from your own
machine:

```bash
kubectl --namespace databases port-forward service/postgres 5432:5432 &

psql "postgresql://mailfathom_migrator@127.0.0.1:5432/mailfathom" \
  --set ON_ERROR_STOP=on \
  --file 'mailfathom-schema-<version>.sql'
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

The first release's script creates a schema from nothing, so neither applies to an empty database.

## Ordering a deployment

1. **Back up the database.** The backup point is before the script, because a migration only moves forward.
2. **Apply `mailfathom-schema-<version>.sql`.**
3. **Roll out the new image.**

That order is what the startup gate requires: the new build refuses to start until the migrations it defines are
present. It is also safe in the middle, while the old build is still running against the new schema — a database
carrying *more* migrations than a build defines has no pending migration for that build, so an instance of the previous
version keeps serving. What it does not do is use the new columns, which is why the window is a rollout rather than a
resting state.

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
  answer whenever the migration itself was not the problem.

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
(`src/AppHost/Program.cs`) rather than from a second `dotnet ef` invocation written beside it — so the release path and
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

- [Deploying with Docker Compose](deployment-compose.md) and [Deploying to Kubernetes](deployment-kubernetes.md) —
  where the schema step sits in each deployment
- [The container image](container-image.md#the-schema) — why the image carries no schema tool
- [The release procedure](release-procedure.md) — where the version in the artifact's name comes from
- [Local development](local-development.md#ef-core-design-time-commands) — the `mailfathom-migrations` resource, which
  is how a schema reaches a developer's own database and is not this
