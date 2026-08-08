---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-08
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep the job store in MailFathom's own schema, claim a row with `FOR UPDATE SKIP LOCKED`, and let the enqueuer compose the one key that identifies an execution

<!-- describes: none -->

## Context and Problem Statement

Issue 405 asks for durable background work: a job enqueued against committed local state, claimed by one worker, retried only where retrying is safe, bounded in what it may consume, and visible afterwards. Issue 468 is its gate, and it asks two questions that have to be answered before any of it is written. Whether the store is MailFathom's or a library's, because that decides which schema the job record lives in and under which licence. And what identifies one execution, because the store's uniqueness constraint is built on that identity and the schema here is append-only, so a later correction is a migration over data somebody is already running.

The architecture draft's section 21.5 refuses a generic workflow engine, a message broker, and a separate scheduler process until PostgreSQL-backed jobs demonstrate a concrete limitation. A library that persists and leases jobs in the PostgreSQL database MailFathom already runs is none of those three, so the refusal does not reach it and the question is genuinely open.

## Decision Drivers

- **There is already one schema, one apply mechanism, and one upgrade story, and they are a contract rather than a habit.** Migrations are append-only, a `Pending model changes` job compares the compiled model against the committed snapshot on every pull request, a release is deployable over the previous release's data, and the schema artifact beside the `mfctl` binaries is how an operator brings a database to a version. A second schema with its own DDL and its own installer is a second answer to every one of those.
- **No transaction may stay open across IMAP, SMTP, or an AI call.** That rule already exists and it decides the shape of a lease before any library is compared: whatever claims a job has to commit the claim and let go, because the work itself talks to a mail server or a model provider.
- **A licence obligation is inherited by everyone who runs MailFathom, not only by the person who added the package.** The product is Apache-2.0, self-hosted, and redistributed as an image operators copy into their own registries. A dependency whose terms are copyleft or commercial does not stop at this repository.
- **The mechanism ships with no consumer, and its two known consumers have nothing in common.** Mail automation from issue 251 identifies work by a rule; classification from issue 76 does not. Anything the shared table hard-codes about one of them is a column the other leaves null.
- **The job row is personal data by classification even when it holds no mail.** It points at a message occurrence, so it is linkable, and erasure, retention, and per-account bounds have to be able to reach it by query rather than by search.
- **MailFathom already runs a durable, retried, converging record it wrote itself.** `MailboxMutationEntity` carries a stage, an attempt count, a last failure code, `xmin` optimistic concurrency, and a denormalized account for the query that lists what is unfinished. Owning the job store is generalizing something in production, not acquiring a capability.

## Considered Options

1. **A job table in MailFathom's own schema**, claimed with `SELECT … FOR UPDATE SKIP LOCKED` and a lease that outlives the claiming transaction.
2. **Quartz.NET**, Apache-2.0, with its `AdoJobStore` against PostgreSQL.
3. **Hangfire**, LGPL v3 or commercial, with the `Hangfire.PostgreSql` storage provider.
4. **Wolverine**, MIT, with its durable PostgreSQL inbox and outbox.
5. **MassTransit**, whose v9 is commercially licensed.

## Decision Outcome

Chosen option: **a job table in MailFathom's own schema**, because every library considered brings a second database schema with a lifecycle this repository does not control, and none of them is shaped around the thing that actually has to be decided here — what identifies one execution — which stays MailFathom's to define whoever stores the row.

Two of the four carry a licence cost as well, and it is worth being exact about what each one is rather than treating "not permissive" as one verdict. Hangfire's LGPL v3 is satisfiable here, because the image publishes framework-dependent and its assembly sits replaceable beside MailFathom's own; what it costs is that a permissive register gains its first copyleft entry and every operator who redistributes the image inherits an obligation somebody now has to honour. MassTransit v9 is commercially licensed and source-available, and a per-organization subscription is not something an Apache-2.0 product can pass to the people who self-host it — a free tier below a revenue threshold does not change that, because MailFathom would be requiring each operator to qualify for another vendor's programme in order to run it. Quartz.NET is Apache-2.0 and Wolverine is MIT, and its commercial half is a separate monitoring product; both are refused on design alone.

### The store is a table in the migration chain that already exists

The job record, its lease, and its outcome are ordinary EF Core entities in `MailFathomDbContext`, mapped and migrated exactly as the mutation record is. There is one schema, one `__EFMigrationsHistory`, one snapshot the pull-request gate compares against, and one artifact that brings a database to a release. Nothing installs or upgrades a schema during host startup, which is a rule the repository already holds and which `Hangfire.PostgreSql` defaults to breaking.

A claim is one statement: the oldest due row whose type this process has a handler for, `FOR UPDATE SKIP LOCKED`, stamped with a lease owner and an expiry and committed immediately. A partial index over the claimable states carries the job type and the due time, because that predicate is the only query the queue runs at any volume. Filtering the claim on the handlers the process holds is what makes a rolling deployment safe: a job whose type a running version does not know is left where it is rather than dead-lettered, because the absence of a handler is a fact about the deployment and not about the work.

### The lease is a stamped row, and the execution timeout is what keeps it honest

The claiming transaction ends with the claim. The work then runs outside any transaction, which it must, because it reaches a mail server or a provider. What survives a crash is the stamp: an expired lease is claimable again, so work in flight when a process dies is picked up without an operator doing anything.

Two things make that safe rather than merely likely.

Completion is conditional on the lease owner still matching. A worker that lost its lease, finished late, and tried to write its result finds the row already owned by the attempt that replaced it, and writes nothing. Without that compare-and-set, an expiry would let a slow attempt overwrite a newer one's outcome.

And the per-execution timeout is strictly shorter than the lease duration, by a margin, validated at startup. That ordering is what makes two workers running one job concurrently structurally impossible rather than a race that is rare: an attempt is cancelled before its lease can expire underneath it. A configuration that inverts the two is a startup failure, not a warning.

What this buys is at-least-once execution and nothing stronger. A handler is therefore registered on the promise that running it twice with the same payload is the same as running it once — the store's uniqueness stops the same work being *enqueued* twice, and only the handler can stop a re-run after a crash from having a second effect. The two guarantees are different and both are needed; saying so here is what keeps a later reader from assuming the first delivered the second.

### One execution is identified by one key the enqueuer composes

A job carries an idempotency key: bounded text, unique together with the job type across the whole table, opaque to the store, and derivable from the trigger alone without reading the table first. Enqueue inserts on that key and reports whether it created the job or found it, so a caller that retries its own enqueue is answered rather than refused.

The draft derives an execution's identity from the message occurrence, the rule version, the trigger generation, and the action. That composition is right and it stays exactly where the draft put it — in the rule engine, which is the only thing that knows what a rule version is. It does not become four columns of the shared table. Classification has no rule and no action, so a table shaped around automation's identity would hand the second consumer three null columns and a unique index that no longer means anything, and the third consumer would arrive with a fourth shape. One opaque key composed by whoever knows the work is the only form that is equally true of all of them.

Two consequences of the key follow from where it is stored rather than from what it holds.

**Uniqueness spans the whole table, terminal rows included.** A row that succeeded is what stops the same trigger enqueuing the same work again, so a job keeps its key in every terminal state — including dead-lettered, which is also why a dead letter is a state on the row rather than a move to another table. Moving it would free the key and let permanently failing work be re-enqueued forever.

That makes retention a correctness setting rather than housekeeping: pruning terminal rows is what ends the deduplication, so the retention floor is the longest window in which the same trigger can legitimately fire again. No child of issue 405 owns pruning today, and whichever change adds it inherits that floor rather than choosing a period freely.

**The key is read by an operator, so it is composed of MailFathom's own names and identifiers** — an account alias, a folder alias, an occurrence, a rule identity and version — never a subject, an address, or anything from the message. A digest would be shorter and would tell somebody reading the dead-letter list nothing about what is stuck.

The account the job belongs to is a column of its own, denormalized onto the row for the same reason the mutation record denormalizes it: erasure, retention, and any per-account bound must be a query on an indexed column and not a search inside a document. A job that belongs to no account leaves it null.

### The job type is a closed enumeration, and it is what makes the payload readable

`JobType` is a closed enumeration in the shape `MailboxMutation` already uses: a `readonly record struct` with a private constructor, one static member per value, and its own serialization, stored as its name. The name is the identity — it is the word in a log line, the name of a span, and the dimension a counter is broken down by — and an operator reading any of the three sees the same word.

An open string was the alternative, and it is refused because nothing here is extensible by anyone outside this repository: every enqueuer is in-tree, so an open set would be modelling a plugin surface that does not exist. It would also make the job type an unbounded metric dimension, and it would leave a name read back from the database with no defined meaning, where a closed set parses an unknown name as unknown and leaves the row alone.

The payload is a record declared per job type, serialized through a source-generated `System.Text.Json` context and stored in one `jsonb` column. The closed enumeration is what makes that work: the type names exactly one payload contract, so a stored document is always read back as the shape it was written as, with no discriminator invented for the purpose and no reflection-based serializer touching a stored document. Nothing queries into the payload — the key, the type, the account, and the due time are all columns — so the column is a document and not a schema.

A payload holds references. It names a message occurrence, an account, a folder, a rule identity; it never copies a subject, a body, an address, or extracted text. Job state must not become a second uncontrolled copy of personal data with retention obligations of its own, and the bound on it is a size limit at the enqueue boundary plus the review of each payload record when its job type is added. That last part is a rule a person applies and no analyzer can, which is why it is written down here rather than assumed.

### Where the line falls

`Application` owns `JobType`, the payload contract for each type, the enqueue port and its created-or-found answer, the lease as the application sees it, the handler registration, and the execution result. `Infrastructure` owns the table, the claim statement, the `jsonb` encoding, the lease clock, and the bounds. `Host` registers the workers and the validated settings and holds nothing else. No library type appears in an application signature, which for `Application` and `Domain` is already structural rather than reviewed: neither assembly may reference anything but the other.

### Consequences

- Good, because there is one schema, one migration chain, one snapshot gate, and one upgrade artifact, and the job tables are inside all four rather than beside them.
- Good, because no new licence obligation reaches an operator who runs MailFathom, and `THIRD_PARTY_LICENSES.md` gains no entry for this feature.
- Good, because the identity of an execution is defined where the work is understood, so the second consumer needs no schema change and the third needs none either.
- Good, because a claim is one indexed statement against a table this repository can read the plan of, and every bound — lease, timeout, batch, retention — is expressed in MailFathom's own configuration and validated with the rest of it.
- Neutral, because at-least-once is what the design delivers. It is the same guarantee every option here delivers, and the obligation it places on a handler is stated rather than discovered.
- Neutral, because ordering is best-effort. `SKIP LOCKED` hands a worker the next *available* row, so work that must happen in a fixed order relative to other work cannot express that through the queue; nothing planned needs it.
- Bad, because leasing, retry, dead-lettering, the worker loop, the observability, and the pruning are all MailFathom's to write and to test. Issues 469 to 473 are most of that and pruning is not yet any of them, and a library would have supplied a version of the lot on day one.
- Bad, because there is no dashboard. What an operator gets is what issue 473 builds out of counts, durations, states, and the dead-letter list, and it will be smaller than Hangfire's for a long time.
- Bad, because the concurrency bound is per process. A deployment running several replicas bounds in-flight work at the claim batch times the replica count, which is legible but is not a deployment-wide limit; providing one would need a counted claim or an advisory lock, and nothing has asked for it yet.

## Validation

- The `Pending model changes` job and the append-only migration rule keep the job tables inside the one schema; there is no second DDL for anything to drift from.
- `ApplicationDependencyBoundaryTests` already fails if `Application` or `Domain` grows a reference to anything but the other, so a framework type reaching an application contract is caught by a test that exists.
- Unit tests over the enqueue port require a repeated enqueue of the same type and key to be reported as found rather than created.
- A host started with a member of the closed job-type set that no registered handler claims fails at startup, so a build cannot enqueue work it is unable to run. The claim filter is for the other case — an older replica meeting a type a newer one introduced — and a unit test over the claim requires such a row to be left where it is rather than dead-lettered.
- Unit tests over the worker require the execution timeout to be rejected at startup when it is not shorter than the lease, and require a completion whose lease owner no longer matches to write nothing.
- The integration suite is where the rest is provable at all: concurrent claims against a real PostgreSQL taking disjoint rows, an expired lease being reclaimed, the unique index refusing a duplicate key, and a dead-lettered row keeping the key that stops it being enqueued again.
- Issue 405 lists what the feature is done when, and every item on that list is a claim about this store; the children implement against this record and are reviewed against it.

## Pros and Cons of the Options

### A job table in MailFathom's own schema

- Good, because it reuses the database, the migration chain, the apply procedure, the options validation, the resilience pipelines, the telemetry registries, and the failure-code scheme that already exist.
- Good, because the schema says what MailFathom means: an idempotency key with the uniqueness this product needs, an account column erasure can reach, and a payload contract per job type.
- Good, because it adds no dependency, so it adds no licence obligation, no supply-chain surface, and no second release cadence to track.
- Neutral, because `FOR UPDATE SKIP LOCKED` is the ordinary way to drain a queue table in PostgreSQL rather than anything inventive, and the mechanism is a documented part of the database rather than a trick.
- Bad, because everything a library would have supplied is written and tested here, and the first version will be less capable than any of them.
- Bad, because the operational surface starts empty. A dashboard, a management API, and the ability to requeue a dead letter are all things MailFathom now owes its operators itself.

### Quartz.NET

- Good, because it is Apache-2.0, mature, and maintained, so nothing about its licence reaches an operator.
- Good, because clustering, misfire handling, and calendar scheduling are solved, and a scheduled scan over a bounded local query — one of the draft's four triggers — is exactly what it is for.
- Neutral, because its persistent store is well understood; it is simply a different store from this one.
- Bad, because `AdoJobStore` is installed from DDL scripts distributed with the project and applied outside EF Core. That is a second schema with a second lifecycle, a second upgrade step in the release procedure, and a second thing the schema artifact does not cover.
- Bad, because it is a scheduler before it is a queue. Durable one-shot work with a domain idempotency identity is expressed as a one-shot trigger whose deduplication key is a scheduler name and group, which is not the identity this decision exists to fix.
- Bad, because `JobDataMap` is the payload, and its own documentation warns that objects placed in it are serialized and become prone to class-versioning problems, recommending a restricted mode holding only primitives and strings. A typed payload record with source-generated serialization is the opposite of that.
- Bad, because its clustering serializes work through a row lock on a `LOCKS` table, which is a coarser mechanism than the per-row `SKIP LOCKED` claim the same database offers directly.
- Bad, because its vocabulary is the Java scheduler's, which it shares its name and its model with — `IJobDetail`, a string-keyed `JobDataMap`, `SCHED_NAME`-keyed tables, a trigger-and-calendar core — and adopting it means carrying that into a codebase whose contracts are typed, closed, and named after this domain.

### Hangfire

- Good, because durable background jobs, bounded retries, and a dashboard arrive working, and the dashboard in particular is real operational value this decision now owes issue 473 instead.
- Neutral, because LGPL v3 is satisfiable here. A framework-dependent image keeps its assembly separately replaceable, so the relinking condition is met without a special build.
- Bad, because satisfiable is not free. `THIRD_PARTY_LICENSES.md` gains its first copyleft entry, and every operator who redistributes the image inherits an obligation that is currently nobody's to honour.
- Bad, because `Hangfire.PostgreSql` is LGPL v3 as well and installs or upgrades its own schema on application startup by default. Turning that off is possible and leaves the schema to be applied some other way — which is a second migration chain outside the one the release artifact carries, and the default is a direct conflict with the rule against schema changes during ordinary startup.
- Bad, because the storage schema and the job identity are Hangfire's. The idempotency identity this decision fixes would have to be layered on top of a store that has its own idea of what a job is.
- Bad, because the dashboard is a second authenticated HTTP surface with its own authorization model, next to the MCP endpoint and the administrative endpoint that already exist.

### Wolverine

- Good, because it is MIT, and the commercial part of the Critter Stack is a separate monitoring product rather than a licence on the library.
- Good, because a durable inbox and outbox on PostgreSQL solves transactional enqueue, which is one of the two boundaries issue 405 wants to be structural.
- Neutral, because it would be a reasonable choice for a system that wanted a message bus; MailFathom wants one queue.
- Bad, because it is a messaging and mediator framework, and taking it for a job queue means adopting its handler discovery and its conventions across the application rather than behind an adapter.
- Bad, because it manages its own PostgreSQL schema and applies it on start, which is the same conflict Hangfire's storage provider has with the rule about startup.
- Bad, because it is by far the largest surface of the five for a table with one query, and the draft's refusal of a generic workflow engine and a broker is aimed at exactly this kind of adoption.

### MassTransit

- Good, because v8 is Apache-2.0 and it is the most widely deployed .NET messaging library.
- Bad, because v9, released under a commercial licence, is where the project now is. Taking v8 is taking a version whose successor is not available on the same terms, which is a dead end chosen knowingly.
- Bad, because a per-organization subscription cannot be passed to the people who self-host an Apache-2.0 product. A revenue-based free tier does not fix it: MailFathom would still be requiring each operator to qualify for another vendor's programme in order to run it.
- Bad, because it is a message-broker abstraction, which is one of the three things the architecture draft refuses outright until PostgreSQL-backed jobs demonstrate a concrete limitation.

## More Information

- Issue 468 asks the question and carries the option table this record decides between; issue 405 is the feature, and issues 469 to 473 implement the store, the worker, the failure policy, the capacity bounds, and the operability against this contract.
- Issues 251 and 76 are the two consumers, and each names the absence of this model as its first blocker. Neither adds a scheduler of its own afterwards.
- Section 21.5 of the architecture draft describes the subsystem, derives the idempotency identity this record places in the enqueuer, and refuses the workflow engine, the broker, and the separate scheduler process.
- [ADR 0001](0001-application-owned-repositories-for-persistence-ports.md) is why the store is reached through an application-owned port with EF Core behind it, and [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) is where a job-store failure gets its code. `MailboxMutationEntity` is the working precedent for a durable, retried, converging record with a denormalized account and a stored failure code.
- The `describes:` marker names nothing because none of this code exists yet. It gains its paths when issue 469 lands the record and its lease, which is one of the two edits an accepted ADR is permitted.
- Revisit when a deployment-wide concurrency bound is actually asked for, when the volume makes a single claim query a measured bottleneck rather than a suspected one, or when what an operator needs to see outgrows what MailFathom is willing to build — a dashboard is the one thing a library here would have given away, and wanting it back is a legitimate reason to reopen this.
