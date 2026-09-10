---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-10
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Exclude a replica from work that must not run twice with a lease row keyed by the scope that already records the work's progress, promise one writer rather than one runner, and move a rate ceiling into the database while an in-flight ceiling stays a process's and says so

<!-- describes: backend/src/Host/Hosting/Workers/AccountPushNotificationWatch.cs, backend/src/Host/Hosting/Workers/AccountSynchronizationSupervisor.cs, backend/src/Host/Hosting/Workers/MailSynchronizationCoordinator.cs, backend/src/Host/Hosting/Workers/MailEmbeddingBackfillWorker.cs, backend/src/Host/Hosting/Workers/MailExtractionBackfillWorker.cs, backend/src/Host/Hosting/Workers/StoredContentMoveWorker.cs, backend/src/Application/AiProviders/ProviderRequestPacer.cs, backend/src/Application/EmailContent/Storage/StoredContentCeiling.cs, backend/src/Application/Emails/Embeddings/Limits/IEmbeddingSpendLedger.cs, backend/src/Infrastructure/Observability/MailAnsweringSpendTracker.cs, backend/src/Host/Security/ClientAssertions/ClientAssertionReplayStore.cs, backend/src/Host/Security/ClientAssertions/ClientAssertionAuthenticator.cs, backend/src/Host/Security/ClientAssertions/UserClientAssertionAuthenticator.cs -->

## Context and Problem Statement

The Helm chart exposes `replicaCount` and refuses no number above `1`, and part of the service is ready for one. The job queue and the outbox claim a row under `FOR UPDATE SKIP LOCKED` and hold a lease a crash releases, which [ADR 0009](0009-durable-job-store-and-execution-identity.md) records; two replicas reaching one scheduled occasion compose the same idempotency key and the queue resolves the duplicate; the refresh-token store lets PostgreSQL settle a concurrent rotation; and the MCP transport is stateless.

The rest is not, and issue 1287 is the feature that changes it. A second replica starts a second supervisor for every configured account, holding its own IMAP sessions and its own push watch against a provider's per-account connection limit. It starts a second embedding sweep advancing the one deployment-wide position row, a second extraction sweep, and a second stored-content move carrying passes over the same run row. Nothing anywhere decides who runs any of it.

Three things have to be settled before any of that is written, and issue 1288 is the gate that settles them. **What the unit of exclusion is**, because it becomes a key in an append-only schema. **Whether exclusion is a row in MailFathom's own schema or a PostgreSQL advisory lock**, because the two fail differently and only one of them can be read by an operator. And **what a replica is promised**, because every worker beneath this is written against that sentence, and a worker whose author assumed a stronger one orders its cancellation and its lease wrongly.

This record produces no code. Its children implement it.

## Decision Drivers

- **There is already one mechanism of exactly this shape, and it is understood.** ADR 0009's lease is a stamped row with an owner and an expiry, every write against it conditional on the owner still matching, and the attempt's timeout strictly shorter than the lease so an attempt is cancelled before its lease can expire underneath it. A second coordination mechanism beside that one is two failure modes, two vocabularies, and two things an operator has to learn before they can read what their deployment is doing.
- **No transaction stays open across IMAP, SMTP, or a provider call.** That rule already exists and it decides the shape of exclusion before any option is compared: whatever holds a scope has to survive the transaction that took it ending, because the work itself reaches a mail server or a model.
- **The connection pool is sized for request work.** Anything that pins a connection for the lifetime of a held scope scales with the number of accounts and sweeps rather than with the number of requests, and a deployment with thirty accounts would hold thirty connections doing nothing but proving a fact.
- **An operator has to be able to read who holds what.** The administrative surface answers out of MailFathom's own tables, in MailFathom's own names. A mechanism the database describes as a pair of integers in `pg_locks` cannot be joined to an account alias, a run, or a replica.
- **Every kind of singleton work already writes down where it got to, and every one of those rows already has a scope.** The exclusion does not need a key invented for it, and inventing one would create a second answer to *what is one unit of this work* that could disagree with the first.
- **Raising `replicaCount` must not quietly weaken a security property.** A ceiling counted per process is a bound an operator can be told about. An anti-replay store that is per process is not a bound at all above one replica, and the difference matters because one is a number to adjust and the other is a guarantee that stops holding.
- **A ceiling worded as the deployment's was set by somebody who read that wording.** Multiplying one by the replica count is a defect against that person rather than a scaling detail, and the two ceilings that cost money are exactly the ones nobody notices multiplying until the provider's invoice arrives.

## Considered Options

1. **A lease row in MailFathom's own schema**, keyed by the scope, stamped with a holder and an expiry, renewed by the holder while it works.
2. **A session-level PostgreSQL advisory lock** (`pg_advisory_lock`) per scope, held for the life of the connection that took it.
3. **A transaction-level PostgreSQL advisory lock** (`pg_advisory_xact_lock`) taken around each pass.
4. **Electing one leader replica** that runs every singleton and leaves the others serving requests.
5. **Expressing each singleton as a job in the queue ADR 0009 built**, and letting the existing lease carry it.

## Decision Outcome

Chosen option: **a lease row in MailFathom's own schema**, because it is the only option that survives the transaction ending without holding a connection, expires on its own so a dead replica parks nothing, and is readable in the deployment's own vocabulary — and because the repository already runs one of them, so this is generalizing a mechanism in production rather than acquiring a second one.

### The unit of exclusion is the scope of the row that already records the work's progress

Every kind of work here already commits how far it has come, and every one of those rows is keyed by something. That key is the unit of exclusion, and the rule is worth more than the list because whatever is added later inherits it:

| Work | Unit of exclusion | The row that already carries it |
|---|---|---|
| One account's synchronization | The mail account identity — the user and the account identifier together | The supervisor is already one per account, and `SynchronizationCheckpointEntity` is keyed beneath it |
| A deployment-wide derived-data sweep | The named walk | `BackfillPositionEntity`, keyed by name — `stored-email-extraction`, `stored-email-embedding` |
| An operator-asked re-derivation | The scope the operator named — an account, or one folder of it | `MailRederivationPositionEntity`, keyed by account and folder |
| The stored-content move | The deployment | `ContentMoveRunEntity`, under its one fixed key |

**So exclusion is keyed exactly as progress is keyed.** Where progress is one row for the deployment, exclusion is deployment-wide; where it is one row per account, exclusion is per account; where an operator names the scope, they name the lease with it. Nothing has to decide a unit separately from deciding where the cursor lives, which is what stops the two from ever disagreeing about what one unit of the work is.

The account is the right unit for synchronization from both directions. Coarser — one lease over the coordinator — puts every account on one replica and gives up the reason for running three. Finer — a lease per folder — cuts inside one IMAP session, and the limits this feature exists to respect are the provider's per-account connection limit and `mail_max_userip_connections`, neither of which is per folder. It also has to cover the push watch rather than only the run, because a replica that holds no lease but still opens an IDLE connection is precisely the second connection issue 1287 opens with; the watch belongs to the supervisor, so it belongs inside the supervisor's lease.

[ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is why the unit is the account rather than the user: ownership hangs on the mail account, a user may hold several, and two of their accounts have no reason to be synchronized by the same replica.

### The lease is a row of its own, and it is not the job queue

The claim is one statement against the key. It takes the row when no holder is recorded or the recorded lease has expired, stamps a holder and an expiry, and commits immediately — the claiming transaction ends with the claim, because what follows reaches a mail server or a provider. The holder identifies one *hold* rather than one process, for the reason [`JobLeaseOwner`](0009-durable-job-store-and-execution-identity.md) identifies one attempt rather than one process: a replica whose lease was reclaimed and that then finished late must find the row owned by whoever replaced it and write nothing. Every write against a leased scope — progress, counts, release — is conditional on the holder still matching.

What the losing option costs is worth stating in one place rather than only in the comparison below. A session-level advisory lock is bound to the connection that took it, so the take, every check, and the release have to happen on that one physical connection — which a pool exists not to promise. Holding a scope therefore means holding a connection out of the pool for as long as the work runs, and a deployment supervising thirty accounts holds thirty of them. A lease row is written on whichever connection the pool hands over and keeps nothing, because the row is what persists.

It is a second table rather than a job, and the distinction is what a job is. A job is an *occasion*: something asked for it, it carries an idempotency key that stops the same trigger enqueuing it twice, it has attempts, a failure policy, and a dead letter. A supervisor is a *role*, held for as long as the replica is up and the account is configured. Expressing it as a job means either a job that never completes — a lease held by the queue while every other thing the queue does means nothing — or a re-enqueue interval that races the push notification and the authored-change signal the supervisor exists to wake on. The queue keeps exactly what it has; this is a table beside it with the same invariants and none of its machinery.

### The holder renews, and a failed renewal is what stops the work — not the expiry

ADR 0009 makes two workers running one job structurally impossible rather than rare by ordering two settings: the per-attempt timeout is strictly shorter than the lease, validated at startup, so an attempt is cancelled before its lease can expire underneath it. That rule stands unchanged wherever the work is one bounded pass. A supervisor is not one bounded pass, so it needs the same rule in the form an open-ended loop takes:

- **The renewal interval is strictly shorter than the lease duration, by a margin, and startup refuses a configuration that inverts them** — exactly as it already refuses `Jobs:ExecutionTimeout` at or above `Jobs:LeaseDuration`.
- **The holder cancels its own work on the first renewal it fails to complete, not when the lease's clock says it expired.** A failed renewal happens strictly before the expiry, and the difference is the margin — which is what leaves the cancellation time to tear an IMAP session down rather than only to stop a loop.

A replica that cannot take a lease neither fails nor parks. It waits and asks again on the interval it would have run on, because the work is not its to do and there is nothing to report: what holds what is a question the lease table answers about the deployment, rather than one the replica that happens to be asked answers about itself.

A holder releases its lease on graceful shutdown. That is what makes a rolling upgrade move work rather than wait it out, and it is an optimization of the ordinary case only — the expiry is what covers the crash, and nothing may be built on the release having happened.

### What a replica is promised, and where the promise stops

ADR 0009 states its guarantee as at-least-once execution, with idempotence owed by the handler. This record states its own in the same terms, and the difference between the two halves is the whole of what a worker's author has to hold on to:

**One scope has at most one holder, and therefore at most one writer.** The compare-and-set on every write is what makes that true rather than likely, and it is true through a partition, a stalled process, and a clock that drifted.

**It is not at-most-once execution.** A replica cut off from the database can still be running work whose lease has moved on. What is excluded is that it *commits*; what is bounded is how long it can go on running, by the renewal margin above.

**And the promise stops at the process boundary.** A compare-and-set takes back a write. It does not take back an IMAP connection, an IDLE session, an SMTP send, or a provider call already in flight. So MailFathom's own state has exactly one writer, while a mail server may briefly see two connections for one account and a provider may briefly see two callers. "Briefly" is the renewal margin, which is why that margin is a setting rather than a constant and why cancellation has to reach the session rather than only the loop.

The sentence every child is written against is therefore: **a worker may assume it is the only one writing, and never that it is the only one running.**

### Which ceilings become the deployment's, and which stay a process's and say so

Issue 1287 states that bounds worded as the deployment's are counted per process and multiply by the replica count. Reading each of them shows that to be true of three and wrong about two, and the correction is part of this decision rather than a note beside it:

| Bound | How it is counted today | Above one replica | What issue 1293 owes it |
|---|---|---|---|
| `MailSynchronization:MaxStoredContentBytes` | A level held in the process, seeded and refreshed from the database's own catalogue measurement of the content table | The measurement is shared, the reservation held between measuring and writing is not — so the ceiling is approached correctly and overshot by whatever the other replicas reserved since each last measured | Shorten the gap between measuring and claiming, or make the claim itself the shared figure |
| `MailSynchronization:MaxStoredContentBytesPerUser` | The same shape over `user_stored_content`, a row moved inside the storing transaction | The same: a shared figure with per-process reservations above it | The same |
| `Embeddings:MaxInputCharactersPerPeriod` and its per-user form | `embedding_spend_periods`, an increment issued as an upsert per period and user | **Already the deployment's.** Nothing multiplies | Assert it, and change nothing |
| `Embeddings:MaxRequestsPerMinute`, and the image-description rate beside it | `ProviderRequestPacer`, a slot marker under an in-process lock | **Multiplies.** Three replicas send three times the rate the deployment declared, at a provider that answers a per-minute quota with a refusal | The marker becomes one row per paced workload, moved forward by a single statement that hands the caller its own slot instant |
| `MailAnswering:MaxRunsPerPeriod` and `MailAnswering:MaxTokensPerPeriod` | Three numbers in `MailAnsweringSpendTracker`, process-local by an explicit decision recorded in its own remarks | **Multiplies** | Becomes a ledger row per period, charged once per admitted run, in the shape `embedding_spend_periods` already has |
| The `AiProviderInvocation` resilience budget's `ConcurrencyLimit` | The resilience pipeline, per process | **Multiplies** | Stays per process, and every page an operator reads says so |
| `Jobs:MaxConcurrentJobs` and `Jobs:MaxConcurrentJobsPerType` | `JobConcurrencyGate`, per process, which ADR 0009 already records as per process | **Multiplies** | Stays per process, and says so where the first one does |

Two of them cost money and were already right, which is worth stating plainly: the spend ledgers were built as tables precisely so a crash-restart loop could not begin every period again from zero, and that same choice makes them replica-proof for free.

**A rate becomes the deployment's and an in-flight count does not**, and the asymmetry is deliberate rather than an inconsistency to tidy later. A provider quota is stated per minute, which is the quantity the pacer exists to respect; moving its marker into a row costs one round trip on a path that is about to wait seconds for an inference call, and it preserves the reservation semantics exactly. A deployment-wide in-flight count needs a permit taken and released around every provider call, with a lease of its own so a crashed replica does not leak one — a coordination mechanism per call, to bound a quantity no provider states. So in-flight stays a process's, and the configuration reference reads `× replicaCount` where an operator meets it.

`MailAnswering`'s tracker argues against durability on the grounds that a question opens no write of its own. That reasoning holds against a write per provider call and not against a write per admitted run: a run is already about to spend a provider's tokens, and one row touched at admission is not measurable beside that.

This is also the answer to ADR 0009's own revisit trigger, which named a deployment-wide concurrency bound as the thing to reopen on. It has been asked for. The answer is **yes for spend and for rate, and no for in-flight concurrency**, and that record is extended rather than contradicted.

### The replay window is a security property the replica count removes, and routing does not put it back

`ClientAssertionReplayStore` is in memory and per process, and it is consulted from the authentication handler on every request that presents a client assertion. Its own remarks record the choice and its reasoning, and both are right at one replica.

Above one replica the property stops holding. An identifier is spendable once *per replica*, because the replica that refuses a replay is not the replica the next presentation reaches. That is not a ceiling being approached differently; it is a guarantee that exists at `replicaCount: 1` and does not exist at `replicaCount: 2`.

The store's text names the trade a deployment behind a load balancer could take — binding a client to one instance. **This record refuses that answer.** Session affinity is honoured by the *client*: it is a cookie the caller returns, or a hash of something the caller controls. The caller here is whoever captured the assertion and is replaying it, and they simply do not present it. Affinity on the source address fails for the reason it always does, and neither is a constraint the server enforces. Routing cannot close a window against an adversary who chooses the route.

**So the answer is a change to the store.** The spend becomes an insert of the verifying credential and the identifier under a unique constraint, where whether a row was inserted is the answer, beside a sweep of entries past the permitted assertion lifetime. It is one insert on a path that has already verified a signature and is about to serve a request that reaches the database anyway, and the table's size is bounded by the same two facts the in-memory store already rests on: only a verified assertion is remembered, and an entry outlives no assertion.

**It gates the chart.** Issue 1287's acceptance says `replicaCount` is safe to raise; while this stands, raising it silently weakens authentication, so this is a precondition of that claim. No child of issue 1287 covers it, and it needs an issue of its own before that acceptance can be met.

### ADR 0009 is extended, not superseded

Nothing in ADR 0009 becomes wrong. The store stays a table in the one migration chain, the idempotency key stays the enqueuer's to compose, the job type stays a closed enumeration, and job concurrency stays per process — this record reaches that last one and leaves it where it is. What is added is a second mechanism of the same shape for work nothing enqueued, the lease invariants restated in the form an open-ended loop needs, and an answer to the revisit trigger that record left open. The two are linked in both directions.

### Consequences

- Good, because there is one shape of exclusion in this repository rather than two. A reader who has understood the job lease has understood this one, and an operator reads holders out of a table in the deployment's own names.
- Good, because the unit of exclusion is derived rather than chosen: a sweep added later inherits the key its position row already has, and no session can decide the two differently.
- Good, because nothing is pinned. No connection is held for the life of a scope, no deployment is required to configure affinity, and nothing joins the deployment that was not already there.
- Good, because a dead replica parks nothing: an expired lease is claimable, so work moves without an operator being told a process died.
- Neutral, because the promise is one writer rather than one runner. It is the same class of guarantee ADR 0009 makes, and it is stated here so a worker's author meets it before writing cancellation rather than after.
- Neutral, because two of the five ceilings issue 1287 named were already deployment-wide, which makes issue 1293 smaller than the parent assumed and its acceptance a check on two of them rather than a change to all five.
- Bad, because an expiry is a window. A holder whose renewal fails has to stop inside the margin, and that is a rule each worker's author honours rather than something the compiler enforces — which is why the ordering is validated at startup and why the margin is configurable.
- Bad, because a rolling upgrade that loses a replica rather than stopping it leaves an account unsupervised for up to one lease duration. Releasing on graceful shutdown removes that for the ordinary case and never for a crash, and shortening the lease to shrink it costs renewal traffic against the same database.
- Bad, because the paced workloads gain a contended row. Every replica's provider calls serialize on it, which is what pacing is, but it is a hot row and a database briefly unavailable now pauses work that would previously have gone ahead unpaced.
- Bad, because this record opens a change to an authentication path rather than closing one. The chart's `replicaCount` cannot honestly be called safe until the replay store is shared, and that work has no issue yet.

## Validation

- Startup refuses a renewal interval that is not strictly shorter than the lease duration, and a unit test over the settings requires that refusal — the same assertion `JobQueueOptions` already carries for its own two.
- Unit tests over the lease port require a claim on a held and unexpired scope to be refused, a claim on an expired one to succeed, and a write conditional on a holder that has moved on to write nothing.
- Unit tests over each worker require a failed renewal to cancel the run's own token, rather than to let the loop reach its next pass.
- Unit tests over the ceilings require the answering ledger and the pacer to read their state from the store rather than from a field, and require the two bounds that stay per process to be named as per process in the configuration reference.
- The integration suite is where the rest is provable at all, and issue 1287's acceptance already asks for it: two hosts against one database, with each kind of singleton work observed to happen once.
- Every child of issue 1287 is reviewed against this record, and the sentence about what a replica is promised is what a cancellation ordering is read against.

## Pros and Cons of the Options

### A lease row in MailFathom's own schema

- Good, because it survives the claiming transaction ending, which the rule against holding a transaction across IMAP or a provider call makes mandatory rather than convenient.
- Good, because it holds no connection: a replica supervising thirty accounts holds thirty rows and no more connections than its work needs.
- Good, because it expires, so a replica that stopped answering releases everything it held without anything having to notice that it did.
- Good, because the holder, the expiry, the scope, and the renewal are columns, so the administrative surface answers who holds what in MailFathom's own names.
- Neutral, because it is the mechanism ADR 0009 already runs, so it is neither new nor inventive — which is the argument for it.
- Bad, because an expiry is a window that a lock does not have: between a holder stalling and its lease expiring, two replicas can be running, and only the compare-and-set stops both from writing.
- Bad, because renewal is traffic. Every held scope writes to the database on its renewal interval whether or not the work did anything.

### A session-level advisory lock

- Good, because it is released the instant the connection ends, so a crashed replica frees everything it held with no expiry to wait out and no window at all.
- Good, because PostgreSQL enforces it, so no compare-and-set is needed to make it true.
- Neutral, because the lock keys are the deployment's to compose, exactly as a lease key would be.
- Bad, because it is bound to the session that took it. Taking, checking, and releasing one all have to run on that same physical connection, and a pool promises the opposite — it hands out whichever connection is free. So the connection has to be pulled out of the pool and kept, which means the lock's affinity is not an implementation detail of the adapter but a second connection lifetime the host has to own.
- Bad, because that connection is then held for the life of the scope, so a held scope pins one for hours. The pool is sized for request work, and thirty supervised accounts would hold thirty connections proving a fact. A lease row holds none: it is written on whatever connection the pool hands over, and the row is the thing that persists.
- Bad, because it has no expiry either. A replica partitioned from the database but whose TCP session has not yet died holds its locks until the operating system's keepalive notices, which is a timeout nothing in MailFathom configures.
- Bad, because `pg_locks` describes a lock as a pair of integers. Nothing joins that to an account alias, a run, or a replica, so the administrative surface cannot answer out of it and an operator debugging a stuck scope is reading the database's vocabulary rather than the product's.
- Bad, because it would be the second coordination mechanism in the repository beside the job lease, with a different failure mode, for work of the same kind.

### A transaction-level advisory lock

- Good, because it cannot be leaked: the transaction ending releases it whatever happened.
- Good, because it is the right answer for a short database-only critical section, and remains available for one.
- Neutral, because it costs nothing to take.
- Bad, because it cannot outlive its transaction, and every unit of work here reaches a mail server, a provider, or an object store — so holding it for the work would mean holding a transaction across exactly what the repository forbids.
- Bad, because taking it per pass excludes nothing between passes, and the thing being excluded is a supervisor's whole tenure rather than one write.

### One elected leader replica

- Good, because it is the least machinery: one fact to decide, and every singleton follows it.
- Neutral, because the election itself would be a lease of the same shape as option 1, over one key.
- Bad, because it puts every account's synchronization on one replica, so the work does not divide at all and the other replicas serve requests only. That is a different feature from the one issue 1287 asks for.
- Bad, because a lost leader parks every kind of singleton work at once, and a rolling upgrade moves all of it together rather than an account at a time.

### Each singleton as a job in the existing queue

- Good, because it adds no table, no port, and no adapter, and inherits leasing, retries, dead-lettering, and observability that already exist and are already tested.
- Neutral, because the queue's claim filter on registered handler types would give the rolling-upgrade safety this needs, in the form it already has.
- Bad, because a job is an occasion and a supervisor is a role. Expressing a tenure as a job means a job that never completes, at which point the retry policy, the attempt count, the dead letter, and the idempotency key all describe nothing.
- Bad, because the alternative — a job re-enqueued on an interval — races the push notification and the authored-change signal that end a supervisor's wait early, and those are the two things that make synchronization feel immediate.
- Bad, because it would make the queue's uniqueness answer two different questions: *this trigger has already been enqueued* and *this scope already has a holder*. Pruning terminal rows already ends the first, and it would silently end the second with it.

## More Information

- Issue 1288 asks the question and issue 1287 is the feature; that parent's `## Delivery order` table names the children this record gates. Issue 1289 builds the lease as a table, a port, and an adapter; issues 1290, 1291, and 1292 take it for synchronization, the derived-data sweeps, and the stored-content move; issue 1293 is the ceilings above; issues 1294 and 1295 are the administrative surface and the chart.
- [ADR 0009](0009-durable-job-store-and-execution-identity.md) is what this extends: the lease shape, the compare-and-set, and the ordering rule are its, and its revisit trigger about a deployment-wide concurrency bound is answered above.
- [ADR 0001](0001-application-owned-repositories-for-persistence-ports.md) is why the lease is reached through an application-owned port with EF Core behind it, and [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) is where a lease failure takes its code. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is why synchronization's unit is the account rather than the user.
- **The replay store belongs to no issue yet.** It is a consequence of the replica count rather than of anything issue 1287's children build, it is a change to an authentication path, and it is a precondition of calling `replicaCount` safe. It needs an issue before that acceptance can be met.
- The `describes:` marker names the code this decision is *about* rather than the code that implements it, because none of the latter exists: the workers that will take a lease, the pacer whose marker moves, the two ceilings that were already shared, the answering tracker that becomes one, and the replay store with the two authenticators that spend into it. It names those files one by one rather than the directories holding them, because `Host/Hosting/Workers/` also holds the job and outbox workers that run under ADR 0009's own lease and are not this decision's subject at all. It gains the lease's own paths when issue 1289 lands them, which is one of the two edits an accepted record is permitted.
- Revisit when a deployment-wide in-flight bound on provider calls is asked for by something concrete rather than by symmetry with the rate, when the renewal traffic of a deployment with many accounts is measured rather than assumed to be negligible, or when a second database is on the table — every option here rests on there being exactly one, and that assumption is the one that would make all of them wrong at once.
