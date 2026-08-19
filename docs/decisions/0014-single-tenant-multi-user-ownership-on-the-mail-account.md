---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-19
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Serve several users from one deployment, hang ownership on the mail account rather than on the mail, and answer multi-tenancy with a second instance

<!-- describes: src/Application/Access/**, src/Application/Accounts/**, src/Application/Emails/Mailboxes/**, src/Application/Emails/GetEmailContent/**, src/Application/Emails/DownloadAttachment/**, src/Application/Mail/Delivery/Authoring/**, src/Application/Mail/Mutations/Authoring/**, src/Infrastructure/Persistence/Emails/StoredEmailSelectionPredicate.cs, src/Infrastructure/Persistence/Entities/**, src/Infrastructure/Persistence/MailFathomDbContext.cs -->

## Context and Problem Statement

MailFathom serves one owner's mail. Accounts are declared in read-only configuration, no persisted entity carries a discriminator for whose data a row is, and ADR 0012 names *the deployment serves one owner's mail* as a decision driver — which is why issue 880 cut the account-restriction axis out of that record and left issue 588 waiting on a decision nobody had made. Issue 889 is that decision, and this record is its answer.

Two target shapes were on the table and they are layers rather than alternatives: one tenant with several users, each owning a set of mail accounts; and several tenants, with the option of a PostgreSQL database per tenant. The second does not remove the need for the first — if a tenant holds several users, the axis of *whose data is this* has to exist in the rows regardless, and a database per tenant only adds routing above it. So the question is not only which shape to build but whether the second is worth building at all, and the answer decides how much of the first has to anticipate it.

The decision was taken after reading the persistence layer, the read paths, the background workers, and the indexes rather than from the shape of the problem. Four findings from that reading changed what the answer had to say, and each is stated where it bears below:

**`mailbox_accounts` is a one-column table.** It holds an identifier and nothing else, created lazily by whichever synchronization run first binds one of the account's folders. `mail_folders` cascades from it, `stored_emails` cascades from the folder, and every table derived from a message cascades from the message — so an owner hung on that row reaches all of the mail without a column on the table that will hold it. Sixteen entities carry `MailboxAccountId`, but only three key onto the account, which is what separates what the owner *reaches* from what a deletion *discharges*; the erasure section below is where that gap is stated rather than smoothed over.

**Nothing in the mail read path uses raw SQL.** `IgnoreQueryFilters` appears nowhere in the repository, and every use of `ExecuteSql`, `ExecuteSqlRaw`, `SqlQuery`, and `SqlQueryRaw` lives in one of eleven files, none of which returns mail: the job store and its schedule and dead-letter companions, the outgoing-mail claim, embedding generation, the spend ledger, the refresh-token store, the repair-request store, the content table's own size, schema inspection, and the vector index's DDL. The premise that made PostgreSQL row-level security the only mechanism below the ORM is therefore absent here.

**The approximate vector index already exists, and the ranking query cannot use it.** `EmbeddingProfileVectorIndex` builds a partial HNSW index per embedding profile at activation, and `EmailVectorSearchIndexReader` ranks emails by a correlated minimum over each message's chunks with the structural filters joined — a shape that index cannot serve, as that class says in its own remarks. So *what a filtered vector search does once an ANN index exists* is not a future question; it has an answer today, and ownership neither causes it nor changes it.

**Two ceilings, a queue, and a cursor are deployment-wide with no owner in them.** The embedding spend ledger is keyed by period alone; the stored-content ceiling is `pg_total_relation_size` over one table; the job claim orders by `AvailableAt` and nothing else; and each named backfill keeps one cursor for the whole deployment.

## Decision Drivers

- **A deployment is already cheap to run twice.** One container, one database, one configuration file, and a Helm chart or a Quadlet unit that installs it. Whatever multi-tenancy would buy has to be worth more than the instance it replaces, and for the shape this project has it is not.
- **The owner axis is irreversible and the routing above it is not.** Where ownership lands in the data model is a migration over data somebody is running; a database per tenant added later rewrites no entity. So the order is fixed whatever is decided about the second layer, and the second layer's absence must not be allowed to shape the first badly.
- **Ownership is not a permission, and ADR 0012 must not be reopened to say so.** A permission names *what may be done* and scoping names *on what*. Adding `mailfathom.mail.read.account.<id>` would reopen exactly what that record closed, and the published set would stop being closed.
- **Isolation has to be a property of the system rather than of the care taken in each repository method.** An operator running one deployment for several people needs one person's mail unreachable from another's credential even where a query is written wrong.
- **One schema, one migration history, one snapshot gate, and one upgrade artifact is a contract rather than a habit.** ADR 0009 states all four, the `Pending model changes` job enforces the third, and the schema artifact beside the `mfctl` binaries is how an operator brings a database to a release. Multiplying databases multiplies every one of them.
- **A first deployment has to work before it has a user model.** This is somebody's own mail server as often as it is an organization's, and the person standing one up configures a mailbox and a credential. A design whose first step is a user-provisioning decision is one most of its users meet as an obstacle.
- **Erasure has to be dischargeable and provable.** Dropping a database discharges a deletion request completely; a shared database has to reach mail, embeddings, and the content store, and then reach every table that records an account without a foreign key onto one, and then show that nothing was left.
- **A break here is affordable and still has to be written down.** ADR 0004 permits a `0.y.z` minor to break the configuration schema, the MCP tool contract, the database schema, and the deployment contract, so what a change decides is which shape is right, never whether the break may be taken.

## Considered Options

- **One tenant, several users, ownership on the mail account.**
- **Several tenants, a PostgreSQL database per tenant, with a role per tenant.**
- **Several tenants inside one database, separated by a tenant discriminator and row-level security.**
- **Stay single-user and refuse the whole question.**

## Decision Outcome

Chosen option: **one tenant, several users, ownership on the mail account**, because it is the only one of the four that puts the irreversible half of the change where the data model already reaches, and because the two multi-tenant options both spend their cost on operations to buy a separation a second instance of this deployment already gives.

**Multi-tenancy is refused rather than deferred.** An operator who needs two tenants runs two instances. That is not a smaller version of the tenanted shape: it is stronger separation than a database per tenant with a role per tenant, because there is no routing to get wrong and no process holding both, and it costs a container and a database — which this project already asks an operator to run once.

The rest of this section settles each question issue 889 lists. Every implementation issue this record authorizes is written against these answers, and one that departs from an answer says so on its own issue rather than deciding it again.

### A principal is a user, a user owns accounts, and the bound belongs to the user

A **user** is who mail belongs to. It is the unit ownership hangs on: a mail account has exactly one user, and the mail beneath that account is that user's.

A **credential** belongs to a user and reaches that user's accounts. The bound is therefore a property of the user rather than of the credential, which settles the question issue 588 was left holding: two mailboxes an operator wants separated are two users, not one user with two narrowed credentials.

`AuthorizedPrincipal` gains the user it is acting for and carries nothing else new. Its permissions stay ADR 0012's closed set, unchanged in vocabulary and in meaning. The process identity acquires no user, because work no caller requested is not acting for one; a use case reachable by a caller refuses it exactly as it does today.

**Narrowing one credential within its user's own accounts is a second axis, and this record does not take it.** It is what issue 588's first story asks for once the two-mailbox case is answered by two users — an owner who wants an agent confined to part of their *own* mail — and it is a bound on a credential rather than a statement about who owns what. It is permitted by everything here and required by nothing here; issue 588 is where it stays.

### Ownership is a column on the account and on nothing else

`mailbox_accounts` gains the user that owns it. No other table gains an owner column.

That is the whole of the discriminator, and it works because of the shape the model already has: `mail_folders` carries `MailboxAccountId` and cascades from the account, `stored_emails` carries it too and cascades from the folder, and every derived table — contents, search documents, chunks, embeddings, classifications, threads, mutations, executions, audits, outgoing mail — either carries the account or reaches it through a foreign key. An owner on the account row is an owner on all of it.

**The owner never reaches a mail query as a column.** It reaches the *resolution* that runs before the query. `MailboxScopeResolver` already replaces an unnamed account list with the accounts the deployment serves and refuses one it does not serve; under this record it resolves against the accounts the *caller's user* owns instead. What the predicate then carries is `MailboxAccountId = ANY(...)` over a list — which is exactly the predicate `StoredEmailSelectionPredicate` composes today, on exactly the indexes that exist today. Ownership adds no term to any mail query.

**It does change which plan is the common one, and that is the cost this decision actually carries.** The account timeline index leads with `MailboxAccountId`, so it serves one account as an ordered walk and a keyset page costs the page. Across several accounts the ordering is not something an array scan key preserves — PostgreSQL derives an ordered path from partitioning or from appended paths, not from `= ANY` — so the read is planned as a scan of everything that matched followed by a top-N sort, and the keyset walk stops being the bounded thing it was designed to be. Today that plan is rare, because a deployment serving one owner usually names one account; under this record a user owning several accounts makes *all of my mail* the ordinary request, and the rare plan becomes the default one.

So the timeline is read as a merged per-account walk rather than as one predicate over a list — a lateral or appended keyset per account, merged on the same ordering — and that is delivered with the resolution rather than after it. It is written here rather than left to an implementation because it is the one place where the cheap ownership axis is not free, and because the shape that fixes it has to agree with `EmailTimelinePosition.NewestFirst` column for column or the continuation cursor stops being contiguous.

Two things follow for every other read. The lexical ranking already computes `ts_rank` over everything that matched before taking a window, and the vector ranking is already exact, so neither acquires a new bound from ownership and neither is relieved of the one it has. And the measurement that settles any of this is a corpus large enough for the account predicate to stop being selective on its own — the existing index test seeds six hundred rows, which is enough to prove an index is chosen and not enough to prove anything about a mailbox somebody actually has.

This is still what makes the option cheap where the alternatives are not: the irreversible migration is one column on a table whose row count is the number of mailboxes an operator configured, rather than a backfill over every stored message.

### Ownership is not part of the permission vocabulary, and here is where the check runs

No permission names an account. ADR 0012's set is untouched and stays closed. The scoping check runs in three places and each is named, because between them they are every path that returns mail:

**The resolution, for every scoped read.** `IMailAccountCatalog` splits in two. A caller-scoped catalog answers with the accounts the request's user owns and is what `MailboxScopeResolver` reads; a process-wide catalog answers with every served account and is what the synchronization coordinator and the workers read. The split is the mechanism rather than a convention: a read model that asked the wrong one would be asking a differently-named service, and the scoped one is the only thing registered where a use case can reach it.

**The administrative surface reads whichever the operation is, and that is decided per operation rather than per surface.** It is the one place a user and a deployment administrator share routes: `MailboxMaintenanceEndpoints`, `MailFolderErasureEndpoint`, `MailAnsweringAuditEndpoint`, `MailboxMutationAuditEndpoint`, `MailRuleEndpoints`, and `SpamClassificationEndpoints` resolve an account straight from `IMailAccountCatalog.ServedAccounts` rather than through `MailboxScopeResolver`, and the answering audit returns mail a run cited. An endpoint cannot read both catalogs, so the rule is on the operation: one reached with an administrative permission reads the process-wide catalog, and one a user reaches about their own accounts reads the caller-scoped one. An operation that is both is two operations.

**The identifier reads, for the four paths that reach an email without building a scope.** `EmailContentReader`, `EmailAttachmentDownloadReader`, `StoredEmailResponseAuthoring`, and `MailFlagChangeRecorder` each look an email up by its stored identifier and then ask `MailboxScopeResolver.IsReadableByTools` whether the account and folder it turned out to be in are readable. That question is answered from configuration today and becomes a question about the caller's user as well. It is one method, asked in four places, and it stays one method for the reason it already is one: a fifth path added later asks the same question or does not read mail.

**Three of those four run for a caller. The attachment download does not, and it is the only path that streams an attachment's octets.** `EmailAttachmentDownloadReader` calls `RequireSignedCapability`, and `EmailAttachmentDownloadEndpoint` assumes `AuthorizedPrincipal.SignedCapability` on a route with no authenticated caller — so there is no caller's user for it to ask about, and the amended `IsReadableByTools` cannot be what bounds it. **Ownership reaches it through the ticket instead:** a signed capability records the user it was minted for, and redemption checks that the email the ticket names is owned by that user, so a ticket minted before ownership existed does not redeem and one minted for one user does not serve another who holds the URL. That is a property of the capability rather than of the read, which is why it is stated here rather than folded into the sentence above.

A request naming an account the caller's user does not own is answered exactly as one naming an account this deployment does not serve — the same failure, with nothing in it that separates *not yours* from *not here*, so a refusal cannot enumerate what exists.

### Row-level security is refused, with the condition that would reopen it

Issue 889 reasoned that EF Core global query filters cover the common route and miss raw SQL, `SqlQuery`, `IgnoreQueryFilters`, and lookups by identifier, leaving PostgreSQL row-level security as the only mechanism below the ORM. **Two of those three premises do not hold here, and the third is answered above.**

`IgnoreQueryFilters` appears nowhere in the repository. Raw SQL is confined to the eleven files named above and not one of them reads mail. The identifier lookups are the four paths named above too, and they are gated by a method rather than by a filter, so a query filter was never what protected them.

Refusing row-level security also avoids two costs that are real rather than theoretical. Under Npgsql a session setting made without `SET LOCAL` survives into the next request on the same physical connection, so a per-request identity carried that way is a defect waiting for a pooled connection to be reused — the mechanism would have to be designed around a trap rather than merely adopted. And the posture row-level security requires is one this deployment cannot hold: the policy is decoration unless the application role neither owns the tables nor holds `BYPASSRLS`, while `EmbeddingProfileVectorIndex` creates and drops an index on `email_embeddings` at profile activation, which PostgreSQL permits only to the table's owner and cannot be granted separately. Adopting row-level security would mean either giving up the approximate index's lifecycle or giving up the property that makes the policy worth having.

What stands in its place is that the predicate is written once. `StoredEmailSelectionPredicate.Matching` is the only place a mailbox read is narrowed, all three read models compose it — the timeline, the lexical ranking, and the vector ranking — and the tombstone exclusion already leads it precisely so no caller can opt out. The owner narrowing joins it there.

**The condition that reopens this: a mail-reading path that takes raw SQL.** If one is ever written — a ranking the query provider cannot express, a statement composed for a plan — then the premise this refusal rests on is gone and row-level security becomes required rather than optional. That is a rule an implementation can be checked against rather than a hope.

### Accounts, users, and credentials leave configuration together

Users arrive and depart while the process runs, so the three move into the database as one change: the user, the mail account, and the credential that admits a caller acting for that user.

Moving fewer than three is worse than moving none. Accounts in the database with credentials still in a file means a restart for every user added; users in the database with accounts still in a file means an account nobody owns.

What stays configuration is what belongs to the deployment rather than to a person: the synchronization cadence and its bounds, the shape and default of every ceiling, folder mapping policy, the embedding profile declaration, rules authored under ADR 0010, endpoints and their transport authentication, and the data-encryption key ring under ADR 0005.

**This costs ADR 0002 nothing**, which was checked rather than assumed. That record closes the write side — configuration is read-only to the process, and administrative editing of it, an approval workflow, a configuration history, and *a multi-tenant configuration lifecycle* are refused rather than deferred — and it then says that every question about state a program modifies belongs to the record introducing that state and is answered in PostgreSQL. Accounts, users, and credentials become exactly that: state the program modifies, answered in PostgreSQL, introduced by this record. The refusal of a multi-tenant configuration lifecycle is agreed with rather than strained, because this record refuses multi-tenancy outright.

### An account identifier becomes opaque and its display name becomes the user's own

`MailboxAccountEntity.Id` is an operator-chosen string today, unique across the deployment, and it leaves through `list_accounts`. Under several users that is not merely enumerable — it is a shared namespace, so two users who both call an account `work` collide.

**What a caller is given and what the rows are keyed by become two different values, and that separation is the decision.** The account gains a published identifier that is opaque and generated, and a display name unique **within a user**; `MailAccountSelector` already resolves an account by its identifier *or* by the name it is published under, so the human-facing half survives unaltered. The storage key — `MailboxAccountEntity.Id`, the value carried as `MailboxAccountId` on the mail, the folders, the threads, the jobs, the mutations, and the outgoing mail — is **not** rewritten. It stops being operator-chosen for an account provisioned after this record, and an account that already exists keeps the value its rows are keyed by.

Conflating the two would be the most expensive change in this whole feature and the only one that touches every stored message: rewriting the storage key means updating `MailboxAccountId` across `stored_emails`, where it leads two of the fourteen indexes, and across every table that references an account — a table rewrite and an index rebuild, to change a value no caller was ever supposed to depend on. The shared-namespace problem is solved by the published identifier being generated and by the display name being scoped to its user, neither of which needs the key underneath to move.

Per ADR 0004 the movement is named against every surface it reaches, and there are three rather than two.

**MCP tool contract** — breaking. `list_accounts` publishes the opaque identifier rather than the configured string, and `MailboxScopeArguments` accepts it, so a client holding a display name keeps working while one holding the old string does not. The operator's action is to re-point any client that stored an identifier rather than a name.

**Configuration schema** — breaking. The account is no longer configured, so its keys are removed rather than renamed, and validation that used to accept them fails startup. The operator's action is to provision what they had configured, or to let the migration import it.

**Database schema** — a change that has to be classified rather than assumed, and the implementation decides which. `mailbox_accounts` gains a required owner and a published identifier, and a user table appears beside it. Because the storage key is not rewritten, the migration can fill both for the accounts already there — one migrated owner, a generated published identifier each — and import the configured accounts as rows. **Where it does, this is ADR 0004's minor row**: a migration that applies forward without operator action. Where it cannot, it is the major row, and the release is not deployable over the previous release's data. Either way the release states it in the terms ADR 0004 requires an upgrade to be planned in: whether a migration must be applied, whether it can be applied while the previous version is still running, and whether the release can be deployed over the previous release's data at all.

### The deployment-wide mechanisms, and which of them stay global

**Job leases and fairness.** `jobs` already carries `MailboxAccountId`, and the claim orders by `AvailableAt` and the identifier — pure FIFO, so one user's backlog delays every other user's due work for as long as it lasts. The claim becomes fair across owners. This record fixes the property rather than the algorithm: no owner's backlog may postpone another owner's due work indefinitely, and the claim stays one statement with `FOR UPDATE SKIP LOCKED` in it, because that is what ADR 0009 decided and this changes the order rather than the mechanism.

**Embedding spend ceilings.** The ledger is keyed by the period alone. It gains an owner, and a ceiling is enforced per owner *and* per deployment, over the same epoch-anchored windows. A refusal names which of the two it reached.

**The stored-content ceiling.** Issue 889 does not list this one and it is the same fault: the ceiling is `pg_total_relation_size` over `email_message_contents`, so one user's mailbox fills it and every other user's mail is recorded as `AwaitingStorageHeadroom` with no content. It gains a per-owner bound beside the deployment-wide one, measured per owner rather than from the catalog, since the catalog answers for the table.

**The embedding profile stays deployment-wide, and that is a decision rather than an omission.** One registered profile serves everyone, one generation is read at a time, and one backfill fills it. ADR 0006 makes a profile the geometry of a vector space; a profile per user would mean an HNSW index per user, a backfill per user, and an activation spend per user, to let two people search their own mail in different spaces — which nothing asks for, since no query ever spans two users. The operator chooses the model for the deployment, as they choose the database.

**The backfill cursors stay global too.** Each named backfill keeps one position, ordered by the stored-email identifier. That order interleaves owners by construction, so the walk is already fair, and a cursor per owner would multiply the cheapest part of the system. This is written down so it is not later mistaken for something the owner axis forgot.

### Vector search, and what partitioning is deferred with

The ranking is exact and does not use the approximate index that exists. That is `EmailVectorSearchIndexReader`'s own decision, argued in its remarks: the structural filters join the ranking rather than trailing it, so post-filtering cannot measure the query against mail the caller may not see, and an HNSW scan orders the whole table and cannot carry a filter on a joined table.

**Ownership does not change this and must not be blamed for it.** The owner narrowing enters as a longer account list in a predicate the query already carries. Whether the ranking should become approximate is a retrieval question about corpus size, it is open today at one user, and it is opened separately rather than folded in here.

**Partitioning by owner is deferred, and the cost of deferring is named.** It is not decided now because the predicate that would benefit from it is already an account predicate, and no measurement exists at a corpus size that would justify the change. What deferral costs is that partitioning `stored_emails` or `email_embeddings` afterwards, under an append-only migration rule, is a table rewrite rather than a migration — so this is a decision to revisit against a measurement, not a step that can be taken absent-mindedly later.

### Migrations, pooling, and schema versions

Refusing multi-tenancy answers all three, and it is worth stating what it preserves rather than only what it avoids. There is one database, so there is one schema, one `__EFMigrationsHistory`, one snapshot the pull-request gate compares against, and one artifact that brings a database to a release — the four things ADR 0009 names as a contract. The application never has to tolerate two schema versions at once. One connection pool serves one database, so no transaction-mode pooler is required, and Npgsql's automatic statement preparation and the safety of transaction-scoped session settings both remain assumptions this deployment may make.

### Erasure

A shared database, so erasure is a cascade rather than a `DROP DATABASE` — but the cascade reaches less than the model's use of `MailboxAccountId` suggests, and that gap is the substance of this section rather than a caveat on it.

**Exactly three foreign keys point at `mailbox_accounts`**: `MailFolderEntity`, `EmailThreadEntity`, and `JobEntity`. So deleting a user's accounts takes their folders, and the folders take their stored mail, and the mail takes its content, its search documents, its chunks, its chunks' embeddings, its classifications and their signals, and its repair requests; the threads and the jobs go with the account directly. That much is the foreign keys' own work and needs nothing remembered.

**Every other table naming an account carries `MailboxAccountId` as a plain bounded string with no key onto one**, and none of them is erased by deleting the account: the mailbox mutations and their audit entries, the rule evaluation runs and executions, the answering audit entries and the audited emails recording what a run cited, the spam classification runs, the rederivation positions and runs, the outgoing mail with its recipients, contents, and filings, and the sealed `MailboxRefreshTokenEntity` — whose own mapping says it, that removing an account has to remove the row deliberately rather than by cascade, and that this is the erasure seam's job rather than the schema's.

So the honest statement is the narrower one: **ownership on the account makes the deletion one statement plus a named list, rather than one statement alone.** What it still buys over a discriminator repeated on every table is that the list is derived from one column and is finite and enumerable, instead of every repository being asked whether it kept a copy. Naming that list, and deciding whether the erasure seam discharges it or the missing foreign keys are added, is work this record authorizes rather than something it assumes; the contact book, which today is deployment-wide, and any ledger row recording spend a user incurred — retained as a cost record rather than erased with the vectors it paid for — stand outside it for reasons of their own.

### The administrative surface gains a deployment administrator

An administrative entry that writes no grant reaches every administrative operation. That is the default rather than the whole posture: ADR 0012 shipped, every route under `src/Host/Api/` chains a `RequirePermission`, and an entry that narrows its grant is already bounded — so the published permissions distinguish operations, and what they do not distinguish is principals. Several users require an administrator distinct from a user — issue 889 calls it a tenant administrator, and since this record refuses tenants it is a **deployment administrator**.

This is what the record adds beside ADR 0012 rather than an edit to it. That record is not reopened, reworded, or corrected: it publishes a closed set of named capabilities, and a deployment administrator is a principal holding administrative ones while an ordinary user holds none of them. A user's own maintenance operations — reindexing their mail, reading their own histories, disposing of their own folders — are scoped to their accounts by the same resolution every other read uses.

### The order the work is delivered in

1. **The owner on the account**, with the user it names. This is the irreversible one and it goes first.
2. **The principal carries the user, and the account catalog splits** into a caller-scoped and a process-wide answer.
3. **The four identifier reads** ask about the caller's user as well as about configuration.
4. **The contact book gains an owner**, which is the one schema change that is genuinely awkward and is described on its own issue.
5. **The ceilings and the job claim** gain the owner axis.
6. **Accounts, users, and credentials leave configuration** into the database and an administrative surface.
7. **The account identifier becomes opaque** and the display name becomes the user's own.

Steps 1 through 3 are what make isolation true. Steps 4 through 7 are what make a second user usable. Nothing here is delivered by a release this record names, because the work is opened without a milestone.

### Consequences

- Good, because the irreversible half of the change is one column on a table holding one row per configured mailbox, rather than a discriminator backfilled across every stored message.
- Good, because ownership adds no term to any mail query: the resolution narrows the account list and the predicate, the indexes, and the query plans are the ones that exist today.
- Good, because one schema, one migration chain, one snapshot gate, and one upgrade artifact all survive, and ADR 0009's contract is preserved rather than renegotiated.
- Good, because erasing a user is one delete against `mailbox_accounts` plus a list derived from one column, rather than a question asked of every repository — though only three foreign keys point at the account, so that list is real work and the erasure section names it.
- Good, because an operator who genuinely needs tenant separation gets a stronger answer than a database per tenant would have given them, with no routing to get wrong.
- Neutral, because isolation rests on query predicates and on one method asked in four places, with no cryptographic separation between users; the data-encryption key ring under ADR 0005 stays deployment-wide and covers refresh tokens rather than mail.
- Neutral, because the embedding profile, the backfill cursors, and the model choice stay deployment-wide, so users share retrieval geometry and an operator's model decision reaches everyone.
- Bad, because the contact book is deployment-wide with an address unique across the whole table, so making it per-user is a schema change on the one structure whose entire meaning rests on that uniqueness.
- Bad, because accounts, users, and credentials leaving configuration is a larger change than the ownership axis itself and reaches the surface ADR 0002 draws, even though it costs that record nothing.
- Bad, because the change reaches three of ADR 0004's four public surfaces: the MCP tool contract and the configuration schema break outright, and the database schema changes in a way whose classification depends on the migration importing the configured accounts — so an operator has to provision what they had configured and re-point any client holding a stored account identifier.
- Bad, because a deployment serving several users reaches the synchronization coordinator's `MaxConcurrentAccounts` sooner than one serving one, and that bound is read once at startup.

## Validation

- The caller-scoped and process-wide account catalogs are separate registrations, so a read model reaching the wrong one is a compilation-visible mistake rather than a review finding.
- A unit test asserts that all three mailbox read models — the timeline, the lexical ranking, and the vector ranking — compose `StoredEmailSelectionPredicate.Matching`, which is what makes the owner narrowing a property of the system rather than of each query.
- A unit test per identifier read asserts that an email belonging to another user's account is answered as absent, in the same shape as one that does not exist.
- A test asserts that no mail-reading path composes raw SQL, which is the condition under which this record's refusal of row-level security stops holding.
- `Fathom review` reads this record's `describes:` marker, so a change under the paths it names is told which decision it is being read against.
- The breaks named against the MCP tool contract, the configuration schema, and the database schema are carried into `CHANGELOG.md` by the release pull request, per ADR 0004, each with the operator's action.

## Pros and Cons of the Options

### One tenant, several users, ownership on the mail account

The user owns accounts, the account carries the owner, and the resolution that already narrows a mailbox read narrows it to the caller's accounts.

- Good, because the data model reaches every mail table from the account already, so the discriminator lands in one place.
- Good, because the scoped reads share one predicate and the identifier reads share one method, so the number of places ownership has to be got right is five and they are all named.
- Good, because it leaves one database, which keeps the schema contract, the pooling assumptions, and the upgrade artifact exactly as they are.
- Neutral, because it does not answer narrowing one credential within its user's accounts, which stays open on issue 588.
- Bad, because isolation is enforced by the application rather than by the database, so a mail-reading path written outside the shared predicate would bypass it.

### Several tenants, a PostgreSQL database per tenant, with a role per tenant

Each tenant's data in its own database, reached by a role of its own, with routing above.

- Good, because it is the strongest separation short of separate processes, and it concentrates the attack surface into one auditable place — the resolution of which database.
- Good, because erasure is a `DROP DATABASE` and discharges a deletion request completely.
- Good, because query performance improves: smaller indexes and no filtered approximate search.
- Neutral, because it needs the owner axis underneath it anyway once a tenant holds more than one user, so it is additional work rather than alternative work.
- Bad, because migrations multiply by the tenant count against an explicitly non-automatic migration step, so either the application runs against schema *N* and *N-1* at once or every release needs a window per tenant.
- Bad, because connection pooling multiplies too, and a modest tenant count times a minimum pool exceeds a default `max_connections`; the way out is a transaction-mode pooler, which withdraws Npgsql's automatic preparation and invalidates transaction-scoped session settings as a safe assumption.
- Bad, because ADR 0009's job store assumes one database, so the scheduler would acquire one store per tenant to rotate over.
- Bad, because everything it buys is already bought by running the deployment twice, which costs a container and a database.

### Several tenants inside one database, separated by a tenant discriminator and row-level security

One database, a tenant column on every table, and PostgreSQL policies below the ORM.

- Good, because it needs no routing and keeps one schema and one pool.
- Good, because the policy holds below any query the application writes, including one written wrong.
- Neutral, because it needs the same owner axis underneath, so the user layer is unchanged by it.
- Bad, because a per-request identity carried in a session setting without `SET LOCAL` survives into the next request on the same pooled Npgsql connection, so the mechanism has to be designed around a trap.
- Bad, because the policy is decoration unless the application role neither owns the tables nor holds `BYPASSRLS`, and this deployment's role must own `email_embeddings` to create and drop the approximate index at profile activation.
- Bad, because it buys a second line of defence against a class of defect — raw SQL reading mail — that does not exist in this repository.

### Stay single-user and refuse the whole question

Leave accounts in configuration and the deployment serving one owner's mail.

- Good, because it is free and nothing is at risk.
- Neutral, because it is what every release so far has shipped and nothing is broken by it.
- Bad, because it leaves issue 588 waiting on a decision indefinitely, and every issue presupposing more than one principal fixes the answer in its own implementation diff.
- Bad, because the ownership axis only gets more expensive as the corpus grows, and refusing to decide is itself a decision to pay more later.

## More Information

- Issue 889 asks the question this record answers; issue 588 waits on it and is unblocked by it, narrowed to the one axis this record deliberately leaves open.
- ADR 0002 closes the configuration write side and sends state a program modifies to PostgreSQL, which is the authority under which accounts, users, and credentials move.
- ADR 0004 permits every break named here and requires each to be recorded against its surface.
- ADR 0005 keeps the data-encryption key ring deployment-wide; nothing here divides it per user.
- ADR 0006 makes an embedding profile the geometry of a vector space, which is why the profile stays deployment-wide.
- ADR 0009 states the one-schema contract this record preserves and owns the job claim whose ordering it changes.
- ADR 0012 publishes the closed permission set this record adds an ownership axis beside, without reopening it, and gains a deployment administrator distinct from a user.
- Revisit this decision if an operator case appears that a second instance genuinely cannot serve — shared infrastructure a tenant may not have its own of, or a tenant count high enough that per-instance overhead dominates. Revisit the partitioning deferral against a measurement of a corpus large enough for the account predicate to stop being selective enough on its own.
