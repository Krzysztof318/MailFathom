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

It can write two rows as well as creating tables, and each is written once and left alone afterwards. Where it upgrades
a database that already holds mail, or anything recorded about it, from a release before users were recorded, the chain
provisions the **user** that mail is bound to — one record, labelled `user`, with the mail accounts this deployment already
holds carried onto it — because a mailbox belongs to somebody from the moment its row exists. A fresh database gets no
user row: its first user is the one an administrator records with
[`mfctl user add`](admin-endpoint.md#users-and-their-records). The label is what an administrator tells users
apart by rather than anything that resolves one, so a deployment is free to rename it. It also provisions the singleton row of
`settings_root`, the deployment's **persisted configuration** document, as an empty document at version 1, because the
host reads that row before it opens any endpoint and a deployment that has configured nothing still has to start. Both
inserts are guarded against a row already being there, so applying the file twice still provisions at most one user and
one configuration document, and neither apply writes over what a running deployment has since put in them.

Some migrations in the chain carry existing data onto a new shape as well, and one of them reads a table rather than
only rewriting a column: the per-account stored-content counter is seeded from what the message payloads already hold, so
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
user, and ownership grants nothing to anybody else. A schema applied by any role but the one MailFathom connects as
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

- **`AddAttachmentTextAndAttachmentChunks` rebuilds one index over the passage table and builds one over the largest
  table in the schema.** It drops `ix_email_chunks_email_ordinal` first, then builds five indexes in this order:
  `ix_stored_emails_awaiting_attachment_text`, which scans `stored_emails` in full under a lock that blocks writes to
  it; the three that replace the dropped one, `ix_email_chunks_email`, `ix_email_chunks_email_attachment_ordinal` and
  `ix_email_chunks_email_ordinal`, each of which reads `email_chunks` once — proportional to the passages a deployment
  has cut, and under a lock that blocks writes to that table for the duration; and the GIN index over the new table
  last. The passage table is therefore read three times rather than twice, the two indexes over the pair having gained
  filters that leave neither covering a statement naming the message alone. Unlike the other partial indexes above, the `stored_emails` one is not built empty, because
  `AttachmentTextDerivedAt` is null on every row an upgrading deployment already holds, so it is written holding every
  message that carries an attachment. Budget a window against the message table rather than against the passage table
  alone. Nothing else in the migration is proportional to anything already stored: the two
  added columns are nullable with no default, which is a catalog change rather than a rewrite, and the table and GIN
  index it creates are both empty until an attachment is read.

- **`FileHeldAccountDraftsAndSentCopiesLocally` reads `stored_emails` once to build an index that holds nothing.**
  `ix_stored_emails_filed_sent_copy` is filtered to a sent copy filed locally that no server has returned yet, which no
  earlier version wrote, so it is built empty — but building it still scans the message table under a lock that blocks
  writes to it. The two columns the migration adds to `mail_drafts` are nullable with no default and rewrite nothing.

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
  `MakeStoredEmailOccurrenceOptional` adds `ck_stored_emails_occurrence_complete` to `stored_emails`, so the message
  table is scanned under `ACCESS EXCLUSIVE` once; dropping `NOT NULL` from `uid_validity` and `uid` beside it is a
  catalog change. `SplitContactBooksByHolder` adds `ck_contacts_book_holder` to `contacts` and rebuilds both contact
  indexes, so each of those two tables is scanned under `ACCESS EXCLUSIVE` — both are proportional to how many people
  the books hold rather than to how much mail is stored, which is the smallest thing on this page.

- **A foreign key added to a table that already holds rows is validated by scanning it.** `AddLocalMailFolders` adds
  `FK_stored_emails_local_mail_folders_LocalMailFolderId` to `stored_emails`, so the message table is read once under a
  lock that blocks writes to it; the column it constrains is added in the same migration and is null on every row, so
  the scan finds nothing to look up. It also builds `IX_stored_emails_LocalMailFolderId`, a partial index over that
  column, which scans `stored_emails` in full under a lock that blocks writes to it even though it is written empty,
  because PostgreSQL evaluates the filter against every row — so budget two passes over the message table. Nothing
  else in it is proportional to what is stored: the column is nullable with no default, the two columns
  `mailbox_accounts` gains carry constant defaults recorded in the catalog, and the `local_mail_folders` table and its
  indexes are empty until an account is held.

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

**Four migrations narrow that window rather than closing it.** `AddUserAccounts` makes the user of a mail account a
required column, and a build older than the release carrying it does not know the column exists — so against this
schema such a build serves the mail already stored and still fails the moment it has to bind a folder for an account it
has never synchronized, because the row it writes states no user. `AddContactUser` does the same for the contact
book: an older build reads and amends the contacts already stored and fails the moment it records a new person or adds
an address, because the row it writes states no user either. `KeyMailAccountByUserAndIdentifier` moves the user into
seven primary keys, and the one an older build writes through by name is the sealed OAuth refresh token: its upsert
names the account identifier as the conflict target, no unique constraint matches that column alone any more, and the
statement is refused — so a rotation an older build receives against this schema is logged as a failure to store rather
than stored. `SplitContactBooksByHolder` is `AddContactUser`'s shape a second time: which book holds a contact becomes a
required column that a check constraint governs, and an older build states neither side of it — so such a build reads and
amends the contacts already stored and fails the moment it records a new person or an address, and the contact collection
of every mailbox it synchronizes fails with it. Keep the middle of the rollout short on these releases, and do not treat
a previous image as something that can be left running against them.

**`SplitContactBooksByHolder` is the one migration that deletes rows it cannot carry over.** A contact book stops being
one user's and becomes either a user's own or a mail account's, and every row has to say which. A contact somebody wrote
down carries over unchanged, filed under the user it was already filed under. A collected one has no mailbox recorded
against it — nothing in the old schema recorded which one picked the address up — so it carries over only where the
deployment answers that on its own: where the user it was filed under is assigned exactly one mailbox and that mailbox
is assigned exactly that one user, the mailbox it arrived on is the only one it can have arrived on, and the row moves
into that account's book. **Everything else collected is deleted** rather than attributed to a mailbox the migration
would have to guess, because a wrong guess would publish one user's correspondents to everybody else assigned a shared
mailbox. So a deployment where each person has their own mailbox loses nothing, and one that already shares a mailbox,
or assigns somebody several, loses what those accounts had collected. **Nothing an operator or a user wrote down is
touched either way**, and collection rebuilds what was deleted from the mail that arrives next, exactly as it does after
`mfctl contact delete-collected`. There is no way back to them afterwards, so an operator who wants them kept reads
them **before** the upgrade — and which reading is available depends on the deployment being upgraded from, because on
the release being upgraded from, every `mfctl contact` command still reaches the book of the one user that deployment
serves. A deployment serving one user reads them with `mfctl contact list --origin Collected` and exports each with
`mfctl contact export`. A deployment serving several — which is exactly the shape that loses rows — cannot, so it reads
them from the database instead, before the migration runs:

```sql
SELECT c."Id", c."UserId", c."DisplayName", c."Note", a."Address"
FROM contacts c
JOIN contact_addresses a ON a."ContactId" = c."Id"
WHERE c."Origin" = 'Collected'
ORDER BY c."UserId", c."DisplayName";
```

**The release carrying `MakeStoredEmailOccurrenceOptional` leaves queued classifications alone.** Arriving mail is
now classified under a job type of its own, `classify-stored-email-spam`, which names the stored email; a replica of
the previous build does not declare that type and so never claims one. `classify-email-spam` keeps its contract and a
handler, so the jobs a previous build queued — and still queues in the middle of the rollout — are claimed and run by
either build. Nothing in the queue is rewritten and nothing waits on the rollout finishing.

**`AddMailboxMutationWithdrawalHold` shortens a way back rather than failing anything.** It adds `HeldUntil` to
`mailbox_mutations`, nullable with no default, so it is a catalog change on a table of any size. A build older than the
release carrying it does not read the column, so a replica of that build still running in the middle of the rollout
issues a deletion from the trash as soon as it finds one, before the hold a client asked for has run out — and pressing
*Undo* on that deletion is then refused as already under way, which is the answer a deletion somebody else's replica
has taken in hand always gets. Nothing is deleted that was not asked for; what the window costs is the moment to change
one's mind.

**`FlagOnlyAuthoredDeletes` expunges a flag-only delete for as long as an older replica is running.** It adds three
nullable columns and an empty table, and writes `Expunge` onto the delete rows `mailbox_mutations` already holds, so it
is quick on a table of any size. A build older than the release carrying it knows only expunging: a delete recorded
under `FlagDeleted` that such a replica takes in hand is expunged rather than flagged, and a kept row a newer replica
marked flagged is served by the older one's queries. Nothing is lost that the delete did not ask to remove from the
server. An account that relies on `FlagDeleted` finishes the rollout before its user deletes anything.

**`AddMailboxMutationLocalChange` asks a held account's rollout to be short.** It adds `LocalChange` to
`mailbox_mutations`, not null with a `None` default, so it is a catalog change on a table of any size and every row
already written arrives as the ordinary change it was. What the column records is what each record has to do with
MailFathom's own copy — nothing, the erasure a second delete opens, or a change a restoring account has already made
and is only carrying to the source — and a build older than the release carrying it decides the first of those from the
account's phase instead: on a held account it erases every outstanding delete. So a delete opened while an account was
being restored to its source — a change the source is still owed, whose own disposition asked for the local copy to be
kept — is erased rather than left alone if an older replica takes it in hand while the account is held again. The same
older replica also lets such a record be withdrawn, which strands the local change it was carrying. Only an account
that is held or being restored is exposed to either, and only for as long as two builds are running. Finish the rollout
before restoring a mailbox, or leave the account held until it has finished.

**It backfills nothing, and that is the right answer rather than an omission.** Every row written by a release older
than this one is a change a server is owed, which is what the `None` default says. The local commit that opens an
erasure record has never shipped — it arrived after the last release and no deployed build writes one — and neither has
the one that opens a record beside a change already committed, which arrived with it. So there is no row a backfill
would be correcting, and the only predicate one could use for the first of them — every outstanding delete of a held
account, which is what the previous build erased — would mark the deletes an account inherited from before its hold as
erasures and destroy exactly the local copies their own `LocalDisposition` asked to keep. That is the defect the column
exists to remove, so the migration leaves the default alone.

**`AddMailboxRestoreAppends` asks a restoring account's rollout to be short.** It creates an empty table and adds two
columns to `mailbox_accounts` — `RestoreStatePosition`, nullable with no default, and `RestoreGeneration`, an integer
defaulting to zero — so it is a catalog change on a table of any size and an older build reads none of the three. What
such a build does write is the custody phase, and it writes it without either column. Both omissions cost the same
thing and cost it silently. A switch out of `Restoring` performed by a replica of the previous build leaves the walk
position where the restore left it, so a later restore of that account resumes from it and never revisits the mail
below it; and a switch *into* `Restoring` performed by one leaves the generation where it was, so that restore asks for
its records under a name an earlier restore of the same account already used and finds the earlier one's completed
records standing in place of its own. So a mailbox restored to its source is restored by a deployment whose replicas
are all on this release. Nothing is lost by the columns being there, and no mail is at risk: what goes unwritten is the
read, the star, the labels, and the move somebody gave a message while the account was held, which MailFathom still
holds and which a support request can put back.

**`KeyMailAccountByUserAndIdentifier` also asks one thing of you after the rollout: authorize every OAuth mailbox
again.** A sealed refresh token is bound to the account it was stored for, and the account was then the user and the
identifier together rather than the identifier alone — so a token sealed by an earlier release **does not open**. The
account's next token request fails with a cryptographic error rather than with `invalid_grant`, and nothing falls back
to the configured reference, which is why [mailbox OAuth](mailbox-oauth.md#troubleshooting) carries a row of its own
for that symptom. The repair is the ordinary one and needs no database access: `mfctl mailbox authorize --account <id>`
for each of them.

**`KeyTheMailGraphByTheAccountIdentifier` asks for that same authorization once more, and for the same reason.** It
takes the user out of every mail key, so a mail row, a derived row, a job payload, a lease scope, and a settings
lookup all name the generated identifier and nothing else. The binding a refresh token is sealed under moves with
them — from the user and the identifier together to the identifier alone — so a token sealed by the release before
this one **does not open** either, and `mfctl mailbox authorize --account <id>` is the whole repair again.

**No mail is discarded by it.** An account was served to one user before this release, so taking the user out of a key
can collide with nothing: every row keeps its account, its folder, and its message, and nothing is rewritten or
refetched. What the release permits rather than performs is discarding stored mail and resynchronizing, which stays an
operator's choice on any release. The one table that loses its meaning is `stored_content_claims`, whose rows gain an
account column with an empty default: a claim lives five minutes and is swept by the next one, so a claim outstanding
across the upgrade reserves nothing and blocks nothing.

**What it costs while it runs is locks rather than a rewrite, again.** Eighteen tables drop a `UserId` column,
fourteen have a primary key rebuilt, and fifty-six indexes are rebuilt around the account. Dropping a column is a
catalog change on a table of any size in PostgreSQL and an index build changes no row, so nothing here is proportional
to the bytes a row holds — but the two corpus-proportional tables, `email_thread_identifiers` and `stored_emails`, are
what to size the window from, and everything runs in one transaction so a deployment that cannot take a lock retries
rather than resuming a half-applied migration.

**What the migration costs while it runs is locks rather than a rewrite.** No column is added, dropped, or filled and
no table is rewritten, so nothing is proportional to the bytes a row holds. It locks **ten** tables inside one
transaction:

- Seven have a primary key rebuilt — `mailbox_accounts`, `email_thread_identifiers`, `mailbox_refresh_tokens`,
  `mail_rederivation_positions`, `mail_rederivation_runs`, `mail_rule_evaluation_runs`, and
  `spam_classification_runs`. `ALTER TABLE … ADD PRIMARY KEY` builds the new index and changes no row. Only
  `email_thread_identifiers` is proportional to the size of the mail corpus; the other six hold roughly one row per
  account, or per account and folder.
- Three have their foreign key onto `mailbox_accounts` dropped and re-added as the pair — `email_threads`, `jobs`, and
  `mail_folders` — and `jobs` takes the new `ck_jobs_account_user` beside it. A foreign key and a check are both
  validated by a full scan of the table they are added to, so each of those three is scanned once and `jobs` twice.
  `email_threads` holds one row per conversation and grows with the mail corpus, `jobs` grows with the queue's
  history, and `mail_folders` holds one row per bound folder.

So size the window from two corpus-proportional tables, `email_thread_identifiers` and `email_threads`, rather than
from one. Everything runs in one transaction, so a deployment that cannot take a lock retries the migration rather
than resuming a half-applied one.

## When the host refuses to start

Three failures are raised by the schema gate itself, and each names a different problem. They are distinguishable from
an ordinary startup failure by the code: everything in the `32xxx` range is persistence. A fourth, `12003`, is raised
before the gate runs at all and is described below the table.

| Code | Failure | What it means | What to do |
| --- | --- | --- | --- |
| `32001` | `DatabaseSchemaOutOfDateException` | The database has not applied migrations this build defines, and the message names them | Apply the schema artifact for this version, then start the host again |
| `32002` | `DatabaseSchemaStateUnreadableException` | The schema state could not be established at all — an unreachable server, a database that was never created, or a role without rights on the migration history | Fix the connection, the database, or the grant. The message names the reason class only; the provider's own text is the inner exception, because it can carry a host, user, and database name. Two of those three reach an operator as `12003` first, from the read the subsection below describes, which names the uncreated database and the missing grant apart rather than as one class |
| `32003` | `DatabaseSchemaTextSearchConfigurationMismatchException` | The lexical index was built with one PostgreSQL text search configuration and this host is configured for another, so searching would stem queries one way and read lexemes built another | Set `Persistence:TextSearchConfiguration` to the value the message reports the schema holds, or rebuild the index under the configured one |

### The schema is read once before this gate, and reports its own code

`12003` reaches an operator first on a database this release's migrations have not been applied to, and it is a schema
problem despite not being a `32xxx` one. MailFathom composes its settings over
[the persisted configuration layer](configuration-sources.md#the-persisted-layer) before it builds the host at all, so
a server carrying no database of the configured name, a missing `settings_root` table, a role holding no privilege on
it, a refused credential, and a server whose authorization rules admit no such connection are all met by that read
rather than by the gate below — under `RootSettingsUnreadableException`, whose message names which of them it was. The
correction is the same one this page gives for `32001` and `32002`: create the database, apply this version's schema
artifact, or fix the grant.

So on a first install `12003` is the expected refusal and `32001` is what an operator would have seen had the layer
already existed. Both name the same missing schema step, and each log line names exactly what it found missing.

Anything else that stops the host is not a schema problem: a secret reference that did not resolve, a configuration
value the options validation refused, a persisted configuration document carrying a setting MailFathom reads before
that layer exists (`12004`) or one it persists in a store of its own (`12005`), or a port already taken all fail before
or beside this gate and report their own codes.

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
  `AddUserAccounts`**: a build older than that release cannot bind a folder for an account this deployment has never
  synchronized against a schema whose mail accounts require a user, so rolling the image back leaves a deployment
  that serves the mail it holds and takes on no new mailbox. Restoring from the backup is the way back there.

  **Nor for the release that carries `AddContactUser`**, for the same reason one table over: a build older than that
  release reads and amends the contacts already stored and fails the moment it records a new person or adds an address,
  because the row it writes states no user. Rolling the image back leaves a deployment whose contact book can be read
  and not written — including by the collection pass, which writes a contact per correspondent it recognizes — so
  restoring from the backup is the way back there too.

  **Nor for the release that carries `SplitContactBooksByHolder`.** A build older than that release writes a contact row
  stating only a user, which the check constraint on `contacts` now refuses, so its contact book can be read and not
  written — and the collection pass, which writes a contact per correspondent it recognizes, fails with it. The rows
  that migration deleted are gone whichever image is running, so restoring from the backup is the only way back to them
  as well as to a writable book.

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
