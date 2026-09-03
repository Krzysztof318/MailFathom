# IMAP synchronization

<!-- describes: backend/src/Application/Synchronization/**, backend/src/Domain/Synchronization/**, backend/src/Domain/Folders/**, backend/src/Application/Folders/**, backend/src/Infrastructure/Mail/**, backend/src/Application/Mail/Mutations/**, backend/src/Application/Mail/Maintenance/**, backend/src/Domain/Mutations/**, backend/src/Host/Hosting/Workers/MailSynchronizationCoordinator.cs, backend/src/Host/Hosting/Workers/AccountSynchronizationSupervisor.cs, backend/src/Host/Hosting/Workers/AccountPushNotificationWatch.cs -->

MailFathom synchronizes mailboxes read-only, on a bounded schedule, and — for an account that asks for it — the moment the mail server says something changed. Both mechanisms run the same synchronization pass over the same read-only session; what differs is only what starts one.

## Implemented behavior

- `Domain` models stable IMAP email occurrence identity as `EmailOccurrenceId`, keyed by `(account, folder, UIDVALIDITY, UID)`. The folder component is a `MailFolderResolutionId` — an alias together with the generation it was bound under — rather than a folder name, for the reason [folder aliases and discovery](#folder-aliases-and-discovery) explains. `Email` is the repository-wide term for the mail artifact; `Message` is reserved so it stays unambiguous once AI conversation types exist.
- `Application` owns IMAP, metadata repository, content store, and checkpoint ports, the folder-discovery, binding-store, and mapping-audit ports in `MailFathom.Application.Folders`, plus the `IPersistenceSession` write-transaction port in `MailFathom.Application.Persistence`. The persistence session is named separately from `IMailboxSession` because both would otherwise be "the session" at a call site.
- `MailboxSynchronizer` resolves the configured alias against the folders the server currently advertises before it reads anything, then opens folders through a read-only session port and requests bounded metadata batches. It retains at most one fetched MIME payload at a time: each seen-preserving remote fetch finishes before a short local session atomically upserts that occurrence's metadata, uses the returned local stored-email identifier for its content, and commits and disposes before the next remote fetch starts. After the inspected batch finishes, a separate short session advances the checkpoint only when the mailbox adapter reports a non-speculative UID cursor known safe from the opened folder state.
- How far back a run reaches is bounded per account by an optional earliest date, which travels into the IMAP search itself rather than filtering what came back. An account that names one pays no `FETCH`, no MIME read, no `bytea` write, and no search-vector computation for the mail it excludes, and the folder checkpoint still advances across the excluded range so a run ends instead of rescanning it every interval. [Bounding how far back a run reaches](#bounding-how-far-back-a-run-reaches) states which date the bound compares against and what widening one later does not do.
- Batches are bounded by email count, not by UID-space width. The adapter searches the whole remaining assigned UID range — a UID SEARCH returns identifiers only — and then fetches envelopes for at most `MaxMetadataBatchSize` emails. A folder whose UIDs are sparse after deletions therefore still advances a full batch per iteration instead of crawling the UID space, which keeps an initial backfill practical.
- An email that exceeds `MaxRawMimeBytes` is never silently dropped. Its occurrence metadata is committed with `ContentAvailability = ExceededSizeLimit` before the checkpoint moves past it, so the gap stays queryable and auditable instead of existing only as a counter in a log line. The same applies when the advertised size understated the payload and the bounded stream read abandons it mid-fetch: the session reports that as a `RemoteEmailContentFetchResult` outcome rather than as a failure, because the caller records the occurrence and continues, exactly as it does for MIME the reader cannot parse.
- How much mail a run brings in is bounded in bytes as well as in messages, and how much of it may be kept is bounded too. A folder run fetches at most `MaxContentBytesPerRun` of raw MIME and then ends at a committed checkpoint; local content storage stops accepting payloads at `MaxStoredContentBytes`, and one owner's stops at `MaxStoredContentBytesPerOwner` while everybody else's keeps arriving, with the occurrences still recorded for a later run with room to fill in; and every folder work unit of the process shares `MaxInFlightRawMimeBytes` of buffer, so peak memory does not grow with the concurrency bounds. [Bounding how much mail a run brings in](#bounding-how-much-mail-a-run-brings-in) describes all four, what each one does when it is reached, and what an operator sees.
- Committing occurrences before the window checkpoint means a process failure may cause a later run to fetch an already stored occurrence again. Content and metadata writes use the stable remote occurrence identity and are idempotent, so this retry does not create duplicate stored emails.
- `Infrastructure` maps the pre-migration PostgreSQL model to `mailbox_accounts`, `mail_folders`, `stored_emails`, `email_message_contents`, and separate `synchronization_checkpoints`. A `mail_folders` row is one alias binding: it carries the alias, its resolution generation, the remote path, and the hierarchy delimiter the server advertised, and is unique on `(account, alias, generation)` rather than on `(account, alias)`. Each stored email has a local UUIDv7; its raw MIME row uses the same UUID as both primary key and foreign key and records byte length, SHA-256, and storage time. Each stored email also records a `ContentAvailability` value as text so a metadata-only occurrence is distinguishable from one whose raw MIME is present, alongside the normalized participants, thread identifiers, attachment summary, and remote flag snapshot that [Stored email schema](../architecture/stored-email-schema.md) describes in full. Persistence sessions clear tracked state after cleanup so one scoped context does not retain MIME arrays between per-email transactions, and re-synchronizing an occurrence that is already stored overwrites its payload with a set-based update rather than reading the existing `bytea` back into the change tracker.
- A write repository takes its EF Core context from the `IPersistenceSession` it is handed, and injects none of its own. The write is therefore always issued on that session's own context, whichever scope the session came from, so "this write joined the caller's transaction" is structurally true instead of being an effect of both objects happening to resolve from the same DI scope. A session backed by a different persistence provider cannot supply a context at all and is rejected outright. Read methods take no session and use the scoped context, because a read joins no transaction.
- Lookups that must see an insert still pending in the open session use the change tracker before the database, since EF Core never flushes pending changes before a query. Primary-key lookups rely on `FindAsync`, which already does this; alternate-key lookups go through one shared two-pass helper driven by a single predicate expression. The one hand-written exception is the raw MIME row, where materializing the existing `bytea` is precisely the cost being avoided.
- Mutable tracked email metadata and synchronization checkpoints carry an infrastructure-only `ConcurrencyVersion`. It is a `uint` row version, which is how Npgsql maps a property onto the PostgreSQL `xmin` system column, so the token is server-generated and no concurrency column exists in either table. A stale tracked update is translated from `DbUpdateConcurrencyException` into an application-owned commit result at the session boundary, which is the only place a conflict is an ordinary branch: its consumer is the retry policy's loop. Synchronization retries a complete idempotent metadata/content write in a fresh persistence session, never repeats the preceding IMAP fetch, and uses cancellation-aware exponential backoff with jitter between bounded attempts. Checkpoint writes are attempted once and only when their durable UIDVALIDITY and last-seen UID still equal the progress read at the start of the run; timestamp precision differences are ignored, while the later synchronization timestamp is retained. `xmin` detects a later race before commit, and three named unique violations are treated narrowly as the same conflict: a concurrent first checkpoint of a folder, a concurrent first binding of an alias, and the `mailbox_accounts` row that first binding creates on its way. The account is one of them because two runs binding an alias under an account nothing has stored yet collide on it before they ever reach the alias, so recognizing only the alias would leave the same race unhandled on an empty database. All three mean another run got there first rather than that the data is wrong; every other unique violation stays a failure.
- Once bounded attempts are spent, or a checkpoint moved under the run, the conflict leaves `SynchronizeAsync` as `PersistenceConcurrencyConflictException` instead of being restated as a result value by each layer it passes. Progress the run already committed stays durable. The account's supervisor catches it per folder, logs a deferral with the reason, and continues with the remaining folders; the next run rereads the last committed checkpoint. The attempt bound is one deployment-wide setting, not a synchronization option, because writers compete for shared rows rather than for anything a single service owns.
- The MailKit adapter resolves folders asynchronously, caps UID progress with the opened folder UIDNEXT value, normalizes email sent dates to UTC before persistence, and rejects occurrence identities that do not belong to the open account, alias binding, and UIDVALIDITY scope. A previous generation of the same alias is as foreign there as another account.
- A connection the adapter has declared unusable — a failed setup, or one being replaced after a transient failure — is closed rather than asked for a graceful logout. A logout is a command, and a server that stopped answering can hold it far past the attempt budget on a call no cancellation reaches, while the pipeline starts the next attempt against the same connection object. Its cleanup failure never replaces the failure being retried. Orderly session disposal still disconnects and disposes, and reports the first cleanup failure.
- A mailbox session survives a mail server that drops its connection. `MailKitImapConnection` owns the client, and no hand-written retry loop exists anywhere in the adapter: the [outbound resilience pipelines](../architecture/outbound-resilience.md) do the repeating, and the adapter only decides what is safe to repeat and how the session recovers. The applied pipelines and the failure classification are documented in the next section.
- `Domain` owns the mail transport security policy: the five connection-security modes, the ordered SASL allow-list, the two opt-ins that permit weakening transport protection, and the trust-anchor selection. The rules that reject an unsafe combination live in `MailTransportSecurityPolicy` rather than in a configuration validator, so a future command-line or MCP entry point cannot reach a transport adapter with a policy that host startup would have refused.
- A permitted mechanism is a domain value object rather than an enum, so its registered SASL name, its clear-text classification, and its JSON form travel with the value instead of living in a separate mapping table that could drift. It serializes as that SASL name, which is also the name configuration accepts and the name matched against a server's advertised set.
- The policy is an input to `IMailboxSessionFactory.OpenReadOnlyAsync`, not something the adapter resolves. `MailboxSynchronizer` reads it per run through `IMailTransportSecurityPolicyReader`, which is why an adapter can only narrow what it is handed.
- `Infrastructure` owns secret reference resolution. Every secret-bearing setting binds to a block carrying a `<scheme>:<target>` reference, four scheme adapters resolve it into pinned byte material that is erased when the operation that owns it ends, and `Host` fails startup when any reference is unresolvable. `Application` and `Domain` see none of it. [Secret provisioning](../operations/secret-provisioning.md) documents the grammar, the deployment shapes, the interpretation modes, and the residual in-memory exposures.
- `Infrastructure` also owns the one place that knows about X.509. `TrustAnchorLoader` turns the bytes a resolver produced into a certificate, so a future material kind arrives as another loader rather than as a change to how a secret is retrieved. It recognizes PEM, DER, and PKCS#12 from the material itself, imports every bundle with ephemeral key storage, and rejects an anchor that carries a private key.
- `MailServerCertificateValidator` decides trust for an account that names an additional authority, by rebuilding the chain against the configured anchor rather than by forgiving what the platform reported. Nothing anywhere can switch validation off.
- Secrets are re-resolved per operation rather than cached, and the configuration snapshot that names them is republished only after every reference in it has resolved. A rotated credential, trust anchor, or database password therefore reaches the next operation without a restart, and a reload that cannot resolve leaves the previous snapshot active. [Secret rotation](../operations/secret-rotation.md) is the operator procedure.
- Every message whose raw MIME was stored is also read for normalized metadata — participants by role, sent and received timestamps, subject, thread identifiers, and an attachment summary. The read happens on the payload the run already fetched, so enrichment costs no second IMAP round trip and cannot reach the remote `\Seen` flag. [MIME metadata extraction](#mime-metadata-extraction) describes what is extracted and how each part is classified, and [Stored email schema](../architecture/stored-email-schema.md) describes which of it the row keeps. A message whose MIME no reader could parse is still stored, carrying only what the server's envelope reported; the same holds for one whose payload was never fetched because it exceeded the size limit.
- The same read also derives the message's searchable text from its body and stores it, together with a bounded copy of the subject and of the normalized participant addresses, in a `email_search_documents` row whose PostgreSQL-generated `tsvector` column carries the GIN index lexical search will query. [Body text and the lexical index](#body-text-and-the-lexical-index) describes which part supplies the text, when a derivation is marked lossy, and what a message with no readable body records instead.
- Each run ends with a bounded backward pass over mail that is already stored, so a message deleted on the server stops being served locally and a flag changed elsewhere stops being stale. It reuses the run's own read-only session, asks for flags and the UID and nothing that could set `\Seen`, and treats a UID the server declines to answer for as a message that left the folder. What becomes of that message locally is the account's choice between a tombstone every query excludes and erasing the local copy outright — unless the message left because MailFathom itself moved or deleted it, which the bullet below covers and which reaches neither. A UIDVALIDITY change selects no window at all and can therefore never delete anything. [Reconciling against the server](#reconciling-against-the-server) describes the window, the ordering that advances it without a cursor, the two dispositions, and the audit lines.
- A bounded background backfill re-reads the raw MIME of messages stored before extraction existed, writes the classification markers and the text it finds, and records the position it reached so an interrupted run resumes rather than restarts. It reaches no mail server, and it ends itself once no stored message awaits extraction.
- `Host` provides typed `MailSynchronization` options, startup validation for enabled account connection settings and their transport security policy, secret resolution and trust anchor loading before any hosted service starts, a validated snapshot every consumer reads instead of the raw bound one, and one supervised synchronization schedule per configured account that isolates failures per account and per folder work unit. [Per-account supervision](#per-account-supervision) describes the coordinator, the two concurrency bounds, the backoff layering, and the shutdown drain. A second worker, configured under `MailExtractionBackfill`, runs the extraction backfill on the same scoped-work-unit terms.
- An account configured for push keeps its folders watched and starts its next pass as soon as the server reports a change, instead of waiting out the interval. How they are watched is the server's answer: one `NOTIFY` subscription covering the account's folders where the server supports it, one `IDLE` session per folder where it supports only that, and polling where it supports neither. The mode is chosen per folder against what the server advertises, degrades to polling when push keeps failing, and is reported whenever it changes. [Push synchronization](#push-synchronization) describes the fallback matrix, mode selection, renewal, degradation, the rotation boundary a long-lived connection creates, and what an operator can observe.
- A change MailFathom itself made to the mailbox comes back through an ordinary run, and is recognized rather than reacted to. A message it relocated is discovered in its new folder as the email that was already stored, joined to it by the `COPYUID` the server gave rather than by a guess at a header, and carried across instead of stored a second time; the source occurrence vanishing is that relocation or an authored delete completing rather than a deletion to propagate. A discovery or a disappearance that matches no record is treated exactly as before, so nothing changes for mail a person moved or deleted in their own client. [Changes MailFathom itself made](#changes-mailfathom-itself-made) describes the join, what happens when the server named no placement, and the order the two halves may arrive in.
- The backward pass asks a capable server only about what changed. Where the server reports modification sequences, the folder's checkpoint records the sequence a completed pass covered it through, and the next pass narrows its flag fetch to what changed since — establishing what still exists from the vanished report `QRESYNC` carries or, without it, from a UID search that returns identifiers and no message data. Every path reaches the same end state; a checkpoint written before sequences were tracked carries none and simply asks about its whole window. [Asking only about what changed](#asking-only-about-what-changed) states the matrix and why a partial pass records nothing.

## Per-account supervision

`MailSynchronizationCoordinator` is a hosted service that reaches no mail server and holds no scoped service. It
starts one `AccountSynchronizationSupervisor` per configured account and supervises those supervisors; everything a
run actually does belongs to the supervisor of the account it runs for.
[The arrival pipeline](../architecture/arrival-pipeline.md) draws what a run does after its folders have finished — the
classification pass, the rules, and the cut that produces a message's passages — and in what order. A run also drains
the account's outbox there, which is what makes sending correct without anything watching for it;
[mail delivery](mail-delivery.md#how-a-written-down-send-reaches-a-server) states why that step can never fail the run.

The account set is a published snapshot rather than a query the coordinator repeats. A validated configuration reload
or a committed owner document raises its change token, and that signal replaces every supervisor together. The account
set in the new snapshot then decides which accounts start, resume scheduling, or remain stopped; each replacement begins
with a new schedule and failure backoff. A rejected reload raises no token and leaves the last valid snapshot in force.
This costs no database query per account or per tick.

Replacing a supervisor cancels its scheduling token and not the work-unit token. A run already writing content and its
checkpoint therefore finishes against the immutable account snapshot it began with; only work still waiting to start
is skipped. The coordinator starts the replacement after that supervisor has drained, so old and new document versions
never run the same account concurrently.

Each supervisor owns its own schedule, its own consecutive-failure count, and its own backoff, and creates a scope per
folder work unit. That is what a server which stops answering can no longer reach: no other account inherits its
failure state, waits out its backoff, or has its own interval pushed back by the folders it is still working through.
The one thing supervisors do share is the slot count below, so an account can wait for a *slot* a slow run is holding
— bounded by that run rather than by the failing account's backoff, which is the isolation this design provides and
the limit of it.

### Two bounds, and what each one is for

**Both bounds count what is happening at one moment, not how much gets done.** Neither is a quota, a per-interval
budget, or a cap on how many accounts or folders are synchronized: every configured account is supervised and every
configured folder is reached. What the bounds decide is how many of them may be *in flight simultaneously*; the rest
wait their turn within the same run and the same interval.

| Bound | Setting | Default | How many may run at the same moment |
| --- | --- | --- | --- |
| Accounts | `MaxConcurrentAccounts` | 4 | Accounts inside a run; the others wait for a slot and then run |
| Folders, within one account | `MaxConcurrentFoldersPerAccount` | 1 | Folder work units of one account; its remaining folders follow in the same run |

Both are validated at startup and enforced at run time, and they multiply: the process never has more than
`MaxConcurrentAccounts × MaxConcurrentFoldersPerAccount` folder work units in flight at once. With ten accounts of
three folders each on the defaults, all ten accounts are synchronized and all thirty folders are reached every
interval — at most four accounts are working at any instant, and each of those is working one folder at a time.

The account bound is what keeps the length of an operator's account list from deciding how much database and network
work synchronization does at once. A supervisor waiting for a slot is delayed by the runs holding them, never by
another account's backoff.

The folder default of one is deliberate. A single IMAP connection per account is the conservative, server-friendly
choice, and every folder of an account already shares that account's session-establishment budget and its circuit
breaker, so raising the bound spends one account's resilience budget faster rather than buying independent capacity.
A deployment with a fast server and many folders raises it; nothing in the current design needs more than one.

Both bounds count folders that are *synchronizing*. An account in push mode additionally holds one waiting connection
per folder, which neither bound covers and [Push synchronization](#push-synchronization) explains.

### One connection budget per mail server host

`MaxConcurrentConnectionsPerHost` bounds every authenticated IMAP connection to the same host across all owners and
accounts: synchronization sessions, folder discovery, push waits, and the idle write connection all take one slot and
hold it until the socket closes. Host names are compared without regard to DNS casing. A protocol session still belongs
to exactly one mail account; the budget shares capacity, never a client or an authenticated session.

Push connections may occupy at most one less than the configured limit. They can remain open for the process lifetime,
so allowing them to take every slot would let a full set of watches prevent the synchronization run needed to make
progress. A further push attempt waits and can degrade to polling under the existing bounded establishment behavior,
while ordinary work retains one route to the server.

Three gauges make the bound readable, each tagged with the keyed process-local pseudonym
`mailfathom.mail.server` rather than the configured host name:

- `mailfathom.mail.server.connections.limit` — the configured ceiling;
- `mailfathom.mail.server.connections.active` — connections holding slots;
- `mailfathom.mail.server.connections.queued` — attempts waiting for a slot.

### Backoff is layered, and the layers never wrap each other

Two different decisions are made about a failure, at two levels, and each is made exactly once:

| Layer | Decides | Mechanism |
| --- | --- | --- |
| Operation | Whether one IMAP command is worth repeating | The [outbound resilience pipelines](../architecture/outbound-resilience.md), per account |
| Run | When this account's next whole run is worth starting | `SynchronizationRunBackoff`, per account |

A run that failed has already spent its pipeline budget, so the supervisor never repeats the operation the adapter
retried. It only defers: the delay before the next run grows from the configured `Interval`, doubling once per
consecutive failed run, capped at `MaxFailureBackoff`, and drawn from a jittered range so accounts that share a server
do not all return to it in the same instant. A run that succeeds resets the count, which returns the account to its
configured interval immediately.

The interval is measured between runs rather than on a fixed grid, so a run that outlives its interval delays the
account instead of overlapping itself. A run counts as failed when at least one of its folders failed — an unreachable
server, a refused folder, an unresolved persistence conflict. An alias that matched no advertised folder, or several,
does **not** count: it is a configuration mistake whose remedy is an edit rather than a wait, and backing the account
off for it would slow the folders that are working.

### Where a run has got to is readable without a metrics stack

Everything above is visible in telemetry and in the log, and neither reaches an operator running without a metrics
stack: a run that is failing, backing off, or making no headway on one folder arrives as mail that does not turn up.
[`mfctl mailbox status`](../operations/admin-endpoint.md#reading-what-synchronization-is-doing) answers it directly,
per account and per mapped folder.

What it composes is two halves that only settle the question together. The account half is this process's own state —
which phase a supervisor is in, the instant the current wait ends, the consecutive failure count that wait was grown
from, and how the last finished run ended. It is deliberately not durable: a restart resets the backoff it describes,
so a count carried across one would name a delay nothing is applying. The folder half is the durable checkpoint —
how far the forward pass has committed and when it last moved — which survives a restart because it is the row a run
advances.

Together they separate a folder with nothing left to fetch from one that has been repeating a batch it cannot get past.
Both show a checkpoint that has stopped moving; only the folder's last outcome says which is which, and a folder that
raises before committing leaves that outcome as the sole signal, since each run of it still reports itself as finished.

### Shutdown stops scheduling first and drains second

Host shutdown cancels scheduling for every supervisor at once, so no further run starts and no further folder of a run
already going is admitted — a folder still queued behind the folder bound is skipped rather than started, because the
drain exists to finish work already in flight and not to open a new mailbox session inside it. The work units already
under way are then given `ShutdownDrainTimeout` to finish, and only what outlasts the drain is cancelled. That is what
keeps a run from being torn down between persisting an email's content and advancing the folder checkpoint — and when
the drain does expire, the progress already committed is durable and idempotent, so the next start resumes from the
committed checkpoint rather than losing or duplicating anything.

The drain is only real while the host is still waiting for it, so the host's own shutdown budget is derived from it
rather than left on the framework's 30-second default: `HostOptions.ShutdownTimeout` is set to the configured drain
plus five seconds for the hosted services stopping beside it, and never below that 30-second default. Without that, a
drain configured above the default would be accepted and silently not honored — the host would stop waiting and the
process would exit with the work still running.

### The account set is re-read rather than fixed at startup

The coordinator re-reads the published snapshot on the configured interval and starts a supervisor for any configured
account that has none running. One mechanism therefore covers three things: an account a configuration reload adds
begins synchronizing without a restart, a supervisor that ended unexpectedly is started again instead of leaving one
account silently unsynchronized, and an account a reload removes ends its own supervision at the start of its next run.
Removing an account is not the same as disabling synchronization: the served-account catalog drops it, and the read side
resolves its mailbox scopes from that same catalog, so the mail already stored for a removed account stops being
readable as well as stopping being synchronized. Turning `Enabled` off stops the runs and leaves the stored copy
readable; removing the account does both.

Supervisor logs and run records carry the account identifier, the folder alias, counts, the run duration, the
consecutive failure count, and the current backoff, and never message-level data. The same facts are also spanned and
metered under MailFathom's own name: a cycle is one span with a span per folder beneath it, so a cycle that stalled is
attributable to the folder it stalled in, and the wait before an account's next run is a gauge, so a backing-off account
is visible without a log line being read at all. [What a synchronization cycle
emits](../operations/telemetry.md#what-a-synchronization-cycle-emits) names each signal and what it answers.

## Bounding how much mail a run brings in

Every other bound on synchronization counts **messages**. `MaxMetadataBatchSize` and `MaxMetadataBatchesPerRun` cap a
folder run at a thousand occurrences by default, and `MaxRawMimeBytes` caps each of those at 25 MiB — which is a legal
run of some 25 GB, every message of it inside every configured limit. Counting messages says nothing about volume,
because one message is anywhere between a kilobyte and the size limit.

Three settings bound the volume instead, and each answers a different question:

| Bound | Setting | Default | The question it answers | Scope |
| --- | --- | --- | --- | --- |
| Per run | `MaxContentBytesPerRun` | 1 GiB | How fast may storage fill? | One folder run |
| In total | `MaxStoredContentBytes` | *(none)* | How full may it get? | The whole process |
| Per owner | `MaxStoredContentBytesPerOwner` | *(none)* | How much of that may one person hold? | One owner, process-wide |
| At one moment | `MaxInFlightRawMimeBytes` | 128 MiB | How much may be in memory while it does? | The whole process |

Three of the four are process-wide, and that is the point of them rather than an implementation detail: each bounds a
resource every concurrent folder run draws on at once, so a per-run version of any of them would be no bound at all. The
per-owner one is process-wide in the same sense and is simply counted per person rather than once.

All four are validated at startup against `MaxRawMimeBytes`, and none may be below it. That is one rule stated three
times rather than three rules: a bound smaller than a single message would not make that message rare, it would make it
unfetchable — and the folder holding it would stop in front of it on every run, forever.

### The per-run budget ends a run; it never drops a message

A run stops fetching once it has read `MaxContentBytesPerRun` of raw MIME. What it does then is the part worth being
precise about, because the obvious alternatives are both wrong:

- It stops **between two messages**, never part-way through one. The budget is tested before an occurrence is touched,
  so nothing is half-stored and nothing already committed is discarded.
- It commits the checkpoint **through the message it actually stored**, not through the cursor the batch reported. The
  batch's cursor covers occurrences the run never reached, and committing it would step the folder past mail nothing
  fetched — silently, and with no later pass that would come back for it.
- It reports **why** it stopped, separately from reporting that more mail remains. Those ask the operator for different
  things: more mail to discover is ordinary, and a folder that keeps ending on its budget is a budget to raise.

A message above `MaxRawMimeBytes` is exempt from the budget, because it costs no fetch at all: it is recorded from its
envelope as it always was, and the run continues. So is a message the folder has stopped holding, which is [its own
case](#a-message-that-leaves-between-being-listed-and-being-fetched).

The next run resumes from the committed checkpoint and spends a fresh budget. An initial backfill of a large mailbox
therefore arrives over many runs at a rate the operator chose, instead of in one run whose size nobody predicted.

### The storage ceiling degrades ingestion rather than failing it

`MaxStoredContentBytes` is the point at which MailFathom stops writing payloads. Reaching it does **not** stop
synchronization: occurrences keep being discovered, their metadata keeps being committed, the envelope-only search
document is still written, and the checkpoint keeps advancing. What stops is the content, and the row says so —
`ContentAvailability = AwaitingStorageHeadroom`.

That value is deliberately distinct from `ExceededSizeLimit`, because the two have opposite futures. A message above the
size limit will exceed it on every later run and nothing is waiting for it. A message recorded here is one the mailbox
would have served, and it is fetched as soon as there is room.

**Closing the gap is a pass of its own, at the end of every run.** After the forward pass and after the backward pass, a
run asks which occurrences of the folder it just worked are recorded without their content, and fetches as many as its
remaining run budget and the ceiling allow — over the session it already has open, in UID order. The order is
deliberate: new mail comes first because discovering it is what keeps the timeline current, and the backward pass comes
before this one so a message that has left the folder is settled before anything asks the server for its body.

Each refill is the ordinary store of a discovery, onto the row that already names the occurrence, so the content write is
the same idempotent one and nothing is duplicated. Raising the ceiling, or freeing space, is all it takes; no
UIDVALIDITY has to be reset and no folder is re-walked. A deployment whose forward pass keeps spending the whole run
budget fills these in once discovery has caught up, which is the right order — a mailbox with unread mail arriving is
better served by finding it than by backfilling bodies.

There is deliberately **no default ceiling**. No number MailFathom could pick would describe an operator's disk, and one
guessed too low would stop a healthy deployment from storing mail. Unset means content storage is bounded by the disk,
which is what a deployment gets until it says otherwise.

**The ceiling is one budget for the process, not one per run.** Several folder work units write into the same content
store at the same moment, so a ceiling each of them evaluated against its own measurement would let every one of them
find the same room and take it — and the deployment would pass the configured limit by as much as those runs were
allowed to fetch between them. Room is therefore *claimed* from a single process-wide ceiling before a payload is
fetched, and kept only for what was actually stored: an abandoned fetch, a message that had left the folder, and a
rolled-back commit each give their claim back, so the level tracks what storage holds rather than what runs intended to
put there.

Each run still measures the store when it begins, and that measurement replaces the level rather than accumulating on
top of it, so space a vacuum reclaimed is noticed. Bytes claimed while the measurement was in flight are carried onto
the new reading instead of being overwritten by it, and a measurement older than one already adopted is discarded — two
runs measuring at once cannot make the newer reading lose to the slower query.

What the level is measured as is PostgreSQL's own accounting of what the content table occupies — its heap, its indexes,
and the out-of-line storage the payloads live in — read from the catalog in constant time rather than summed over the
rows. That is the quantity a disk fills with, and it is cheap enough to read once per folder run. Two consequences
follow from it and are intended: the number is somewhat above the sum of the message sizes, because storage overhead is
part of what fills a disk; and space a deletion freed counts as occupied until the database reclaims it.

### One owner's share stops their mail and nobody else's

`MaxStoredContentBytesPerOwner` asks the same question of one person that `MaxStoredContentBytes` asks of the instance,
and a payload is fetched only where both have room. It exists because the instance ceiling is otherwise the only thing
bounding storage: on a deployment serving several owners, one large mailbox fills it and every other owner's mail is
then recorded without content until somebody frees space. Reaching an owner's share defers that owner's messages exactly
as the instance ceiling defers everybody's — `ContentAvailability = AwaitingStorageHeadroom`, the checkpoint still
advancing, the refill pass fetching what was left as soon as there is room — and leaves every other owner's run storing
content whole.

The deferral is counted apart from the instance one and reported apart from it, because the two ask an operator for
different things: one for more disk or a higher instance ceiling, the other for a larger share for one person or for
that person to wait. A run that left messages for both reasons reports both, one measurement each, so neither remedy
is hidden by the other. One message is deferred by one of them rather than by both, because the instance's room is
claimed first and an owner is never charged for a payload the instance had no room for.

**The two ceilings are counted in different quantities, deliberately.** The instance's is what the disk fills with,
which only PostgreSQL's catalog can report; an owner's is what their payloads hold, because a catalog answers for a
table and never for a share of one. So an owner's figure excludes the indexes, the row overhead, and the space a
deletion freed that the database has not reclaimed, and the two are not expected to agree. That figure is maintained as
a counter moved inside the same transaction that stores or removes a payload, rather than summed over one person's whole
mailbox before every message; an owner with no counter yet — a deployment upgraded before their first message, or an
owner provisioned since — has it derived once and adopted.

There is deliberately **no default share**, and leaving it unset is right for a deployment serving one owner: the
instance ceiling already bounds that person. What leaving it unset exposes on a deployment serving several is the fault
above.

### The in-flight budget is the one bound that spans work units

A payload is buffered whole between the fetch that reads it and the commit that stores it. Peak memory is therefore one
payload per folder work unit in flight — and `MaxConcurrentAccounts × MaxConcurrentFoldersPerAccount` is how many of
those there are. Without a shared bound, raising either concurrency setting would raise the memory ceiling with it,
silently.

`MaxInFlightRawMimeBytes` is that shared bound, and it is a single process-wide budget rather than a per-run value. A
work unit reserves the size the server advertised before it fetches and releases it once the transaction that stored the
payload has ended; one that cannot reserve its share waits for one that finishes, so the effect is slower ingestion
rather than a refused message. Reservations are granted in request order, which is what keeps a large message from being
starved by a stream of small ones.

Two approximations are worth stating. A server that understated a message's size can exceed its reservation, by at most
`MaxRawMimeBytes` for that work unit; and a server that advertised no size at all is charged that limit outright, since
nothing short of the fetch says what the message costs. The budget bounds what MailFathom deliberately holds, not what
the runtime has allocated.

### A message that leaves between being listed and being fetched

A folder can stop holding a message between the moment a run learned of it and the moment the body is asked for — and a
run refilling deferred content is asking about a message it last saw runs ago. The session reports that as an outcome
rather than raising a failure: nothing is recorded, the checkpoint moves past the occurrence, and the run continues. The
alternative would fail the whole folder's run on a message that no longer exists, and fail it again on every later run
until the backward pass happened to reach it.

### What an operator can see

| Signal | Kind | When |
| --- | --- | --- |
| `mailfathom.mail.content.fetched` | Counter, bytes | Every run, by account and folder |
| `mailfathom.mail.content.stored` | Counter, bytes | Every run, by account and folder |
| `mailfathom.mail.content.stored_total` | Gauge, bytes | The level the most recent run measured, for the deployment |
| `mailfathom.mail.content.limits_reached` | Counter, runs | A run that ended on its budget or met the ceiling, tagged with which |
| `Folder …/… ended its run after fetching N bytes…` | Information | The run budget was spent |
| `Local content storage holds N bytes and has reached its configured ceiling…` | Warning | Messages were recorded without their content |
| `Fetched the content of N messages … that an earlier run had left without it` | Information | The refill pass closed gaps |

The counters are what a **rate** is read from — how much a mailbox costs per interval, which is what storage is sized
from — and the gauge is the level that rate is filling. Reaching a limit is counted as well as logged, because both are
conditions that persist: a run that stopped for its budget will stop again next interval, and a deployment at its
ceiling stays there until somebody acts, so a rising count says it has been running that way rather than that it did
once. The gauge carries no account or folder dimension, because content storage is one store every account writes into
and publishing it per account would invite a dashboard to sum it.

Everything here carries the account identifier, the folder alias, byte counts, and the name of the limit — MailFathom's
own configured words. No subject, address, remote folder path, or UID appears in any of it.

## Push synchronization

An account whose `Mode` is `Push` keeps its folders watched and starts its next synchronization pass the moment the mail
server reports a change. Everything else about the account is unchanged: the same supervisor, the same interval, the
same backoff, the same folder concurrency.

**Push changes what ends the wait between runs, and nothing about what a run does.** The pass a notification starts is
the ordinary one — it opens its own read-only session, walks the same bounded batches, advances the same checkpoint, and
runs the same backward pass. There is deliberately no second retrieval path: a fetch driven from the watching session
would be a second implementation of the correctness-critical work, and the one place the read-only `\Seen` invariant
could hold in one path and lapse in the other. A watching session issues `NOTIFY` and `IDLE` and nothing else, its
subscription asks the server for no message data, and a test asserts that it requests no body, no envelope, no flags,
and no read-write reselection.

### One connection, or one per folder

How the watching is done is the server's answer rather than a setting. Two IMAP extensions are involved and they are
asked for in order:

| Server advertises | How folders are watched | Cost against the connection limit |
| --- | --- | --- |
| `NOTIFY` and `IDLE` | One subscription per **account** names every folder up to `MaxSubscribedFolders`; the server reports which one changed | One connection per account |
| `IDLE` only | One session per **folder** | One connection per watched folder |
| neither | Nothing is watched; every folder synchronizes on the account's interval | None |

`NOTIFY` (RFC 5465) is what lets one connection be told about a folder it has not selected. The connection selects the
first folder of the set so that `IDLE` has a mailbox to run against, and the subscription covers the rest; a server
reports a change to a folder it has not selected as an unsolicited status response, which arrives as a moved message
count, a moved next UID, a moved unread count, or a moved modification sequence. None of those numbers is read. The only
thing carried out of a report is *which folder changed*, and the pass that follows re-reads the folder the way every
other pass does.

Both capabilities are required together, because they answer different halves of one question: `NOTIFY` is what makes a
report about another folder possible, and `IDLE` is what keeps the connection in a state where an unsolicited report can
reach it. A server offering one without the other is treated as offering neither, and the account falls back to the row
below it.

**Folders past `MaxSubscribedFolders` are synchronized on the account's interval**, in the order the run resolved them,
which is the order they are configured in. They are not given a connection each: a subscription is bounded because a
server is entitled to refuse one naming more mailboxes than it will track — as a whole, rather than mailbox by mailbox —
and answering that refusal with one connection per overflow folder would spend exactly what the subscription exists to
save. Which folders get push is therefore the operator's choice through configuration order, and the effective mode of
every folder is logged.

The capability is re-read rather than remembered, but not on every run: reading it costs a connection and an
authentication, so a server that has declined a subscription is asked again after `PushDegradationPeriod` while its
folders are watched one at a time in the meantime. That is a working push mode rather than a degradation, and it is
logged at information level.

A subscription that keeps **failing** — as opposed to being declined — leaves the whole account on its interval once
`MaxConsecutivePushFailures` is spent, and does not fall back to one connection per folder. Asking a server for several
more connections in the same moment it refused one is not a fallback; the account keeps synchronizing on its interval
and the subscription is attempted again after `PushDegradationPeriod`.

### A folder in push mode still keeps the account's interval

This is a decision rather than a leftover, and it is the one most likely to surprise: **`Interval` is not replaced by
push, it becomes the ceiling on the wait that a notification cuts short.** The wait a supervisor computes — the
interval, or the backoff a failing account is under — is still the longest a folder waits; a notification only ends it
early.

The reason is the backward pass. Reconciliation walks a window bounded by `MaxReconciledEmailsPerRun`, oldest
observation first, so a folder holding more mail than that window notices a remote deletion or a flag change over
several runs rather than in one. Those runs have to keep happening while **nothing is arriving**, because a message
deleted on the server produces no new mail to be notified about — and a folder whose backlog is a hundred thousand
messages needs two hundred passes at the default window to work through it once. A push mode that removed the interval
would tie the whole backward pass to inbound traffic: a quiet mailbox would stop reconciling entirely, and the longer it
stayed quiet the further behind it would fall, silently.

So push buys latency on new mail and gives up nothing. The cost of keeping the interval is one pass per interval on a
mailbox that had nothing to report, which is exactly what an account not in push mode already pays.

### Choosing the mode, per folder

The operator configures push per **account**, and the mode is settled per **folder**, because only the server can answer
whether it will serve one:

| Configured | Server watches the folder | Effective mode |
| --- | --- | --- |
| `Polling` | either | `Polling` — no session is opened at all |
| `Push` | yes, through a subscription or a session of its own | `Push` |
| `Push` | no, or the folder is past `MaxSubscribedFolders` | `Polling`, and the reason is logged |

The capability is read from the connection the session was just established on, not from anything cached, so a server
that gains or loses the mechanism across a restart or behind a load balancer is followed rather than remembered. A
folder the server declines is polled and retried after `PushDegradationPeriod`; leaving the connection open for a folder
that is going to be polled anyway would spend one of the account's connection slots on nothing.

The folders come from the run rather than from configuration, because an alias names a remote folder only after
discovery has matched it. That is also what keeps the two in step: an alias repointed to a different remote folder is
synchronized under its new binding and watched under it too, instead of leaving a connection idling on a folder nothing
reads any more. An alias that resolves to nothing is watched by nothing.

**Push is opt-in and defaults to off.** It holds a connection open for the lifetime of the process — one for the whole
account on a server that supports subscriptions, one per watched folder on a server that does not — which is a real cost
against a mail server's connection limit, and that is a choice to make rather than to inherit.

Those connections sit **outside** `MaxConcurrentFoldersPerAccount`, which bounds how many folders may be *synchronizing*
at once and says nothing about how many may be waiting. That is deliberate rather than an oversight: on a server without
subscriptions a folder cannot be watched by a connection another folder is holding, so bounding the watches there would
mean choosing which folders get push and leaving the rest silently on polling. What is bounded instead is the
subscription, by `MaxSubscribedFolders`, because that bound belongs to a command a server accepts or refuses as a whole.
An account with many folders on a server that offers `IDLE` alone and a tight connection limit is therefore a
configuration to reconsider, and the log says which folders lost push.

### The third connection: writing

An account can hold one more connection than the two kinds above, and it is the only one able to change the mailbox.
MailFathom opens it the first time something asks to relocate, delete, copy, or mark a message read or unread, keeps it
for `WriteConnectionIdlePeriod` after the last change it carried, and closes it when that elapses. There is **at most one
per account**, whatever is happening: a second caller waits for the first rather than opening a second connection, so a
burst of changes never becomes a burst of logins against a server that counts them.

| Bound | Setting | Default | What it decides |
| --- | --- | --- | --- |
| Write connections, per account | none — fixed at one | 1 | Never more than one, whichever folder is being changed and however many changes are in flight |
| How long one is kept | `WriteConnectionIdlePeriod` | 2 min | The idle time after the last change before the slot is given back |

Setting the period lower gives the slot back sooner and makes the next change pay for a fresh connection, a TLS
handshake, and an authentication. Setting it higher does the opposite. It is read once at startup, which is why the
[mail configuration](../operations/configuration-mail.md#mailsynchronization) marks it *restart* rather than
*reload*.

A write connection is pinned to the folder it selected, the way any IMAP selection is, so changing a message in a second
folder replaces the connection rather than adding one. Nothing on the read side can open, borrow, or reach it — the
guarantee that synchronization and content retrieval never mark mail read is a property of the types they hold, which
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
records in full — and the push session is never taken out of `IDLE` to carry a change.

A rule's action, a spam verdict's action, the `set_mail_flags` MCP tool, and somebody opening a message in MailFathom's
own client with `markReadOnOpen` on are what ask for a change today, so an account whose rules write nothing, whose spam
actions are off, whose mail no caller has changed, and whose reader has not opened a message here holds no such
connection and this setting costs nothing.

### Every change is written down before it is issued

A change to a mailbox is recorded in `mailbox_mutations` before the first IMAP command goes out, and the record is
advanced as the sequence proceeds. That ordering is the whole mechanism rather than bookkeeping beside it: a relocation
on a server without `MOVE` is a copy, a flag, and an expunge, and a process that dies between any two of them leaves a
mailbox nothing can interpret afterwards. A second run reads the record and continues from the stage it names, so the
message ends up filed once whichever command the stop landed between.

The record answers three questions with one row.

**Whether asking again is the same request.** Its identity is the email occurrence, the mutation, and who asked — a rule
together with the revision of it that matched, the identity of one invocation, or the profile a spam verdict was decided
under, which is the deciding stage's rule corpus together with the score the operator acts at. The three are told apart
on the record by their origin, so an operator reading a change knows whether a rule they wrote, something they did, or
[spam classification](spam-classification.md#what-an-operator-can-let-a-verdict-do) asked for it. Two callers asking for
the same change at the same moment both reach the database and the unique index refuses one of them, so the same request
twice performs one change rather than two. That is enforced by the constraint rather than by a check, because no check
made between reading and writing closes the window two concurrent writers fall through.

**Where to continue from.** A relocation whose copy is confirmed removes its source without copying again; a delete
whose flag landed reissues only the expunge; a `\Seen` change simply repeats, because the store is idempotent on the
wire. The one case that is never retried is a placement command whose answer was never read: `COPY` issued twice is a
second message, and nothing in the destination folder afterwards says whether the first attempt landed. Such a mutation
is reported as an unknown outcome and records that as the reason it is stuck; what becomes of it afterwards is
[the section below](#a-change-nobody-finished-finishes-by-itself).

What a half-finished relocation still owes is read from the record rather than asked of the server again. `MOVE`
removes the source itself and a copy does not, so the same stage means opposite things depending on which ran, and the
connection a retry lands on is not required to be the one that answered the first — a fallback relocation resumed
against a server that now advertises `MOVE` would otherwise be read as already finished, leaving the message in both
folders for good. Which path ran is still invisible above `Debug`: it changes what the next attempt does, never what
the operation is called.

**What became of it.** `MaxMutationAttempts` bounds how many attempts one change may spend, counted before each attempt
so one that kills the process still counts. A change that spends them stops being attempted and stays readable as stuck
rather than looking busy forever. Two refusals do not wait for that bound at all: a server that advertises no safe way
to carry the change, and one that answers that the destination folder is not there, have each already given the answer
every later attempt would receive, so those stop at the first refusal.

The record holds no mail. A folder path, a UID, a mutation name, a requester identity, and a five-digit failure code are
all it carries, and it is removed with the email it describes — including when the change it recorded was that email's
deletion. [Stored email schema](../architecture/stored-email-schema.md#recorded-mailbox-mutations) states the columns and
the stages in full.

### An account can keep a record of what was done to it, and none does by default

The record above exists to make a change correct, and its useful life ends when the change does. A durable answer to
"why is this message in this folder" months later is a different artifact with a different lifetime, so it is a second
table: `mailbox_mutation_audit_entries`, written once when a mutation reaches a terminal stage and read by nothing the
mechanism depends on.

**It is off by default and enabled per account.** An audit trail of mail movements is derived personal data — it says
where a person's mail has been, when, and at whose instruction — so a deployment that never asked for it never
accumulates one. An account turns it on and states how long it keeps entries:

```yaml
MailSynchronization:
  Accounts:
    - AccountId: work
      DisplayName: Work mail
      AuditTrail:
        Enabled: true
        Retention: 90.00:00:00
```

The answer is resolved when a change is written down and travels on its record, so switching the trail on or off while a
change is in flight decides nothing about a change already begun. Turning it off stops new entries and leaves the
existing ones to age out under the window that was configured for them.

**One entry per finished change, of every kind.** A relocation, a delete, a `\Seen` change, and a copy each leave one,
naming the change, the local email, the source folder path with its UIDVALIDITY and UID, the destination folder path and
the UID the server reported where there was one, the flag direction where the change was one, who asked — a rule with
its revision, or one invocation — when it was asked for, when it ended, and whether it was performed or given up on with
the failure code it was given up on for.

**It holds no mail content and it outlives the mail.** No subject, no address, no body fragment, no filename: folder
paths, identifiers, and MailFathom's own configured names are what an entry carries. It references the email by its
local identifier rather than hanging on it, so erasing that email leaves the entry standing — including when the change
recorded *was* the deletion, which is exactly the entry an audit of deletions exists to hold.

**Writing an entry never costs a change.** The append is a commit of its own, made after the terminal stage is already
durable, so it can neither roll back a mailbox somebody's mail server has already changed nor fail the operation that
changed it. An append that does not happen is reported as a warning naming the account and the mutation, and counted by
`mailfathom.mailbox.mutation.audit.refused_appends`, so a deployment that undertook to hold this history can see the
moment it stops holding it.

**Retention rides the account's own run.** Every account run erases the entries that have outlived the window the
account configured, which makes the window honored as often as the account comes round rather than to the minute. One
pass erases at most five thousand entries, oldest first, so an operator shortening a long window clears the backlog over
several runs instead of one delete that locks the trail against every append behind it. A failure there never puts the
account into backoff: retention is a storage-limitation obligation rather than a mail operation, and the next run erases
what this one did not.

The trail is read through the administrative endpoint, in bounded keyset-paginated pages filterable by account, by
mutation, and by time; [Administrative endpoint](../operations/admin-endpoint.md#reading-what-mailfathom-changed) states
the route, and it also states the erasure path a data-subject request is answered through.

### Marking mail read is an act, never a side effect of reading

**Reading mail through MailFathom still never marks it read, and that has not been traded away for this.** What changed
is the shape of the guarantee rather than its strength: it is scoped to reading rather than to the whole process. A
synchronization pass, a reconciliation pass, a content fetch, and every MCP tool hold a session type that has no
operation capable of writing a flag, so none of them can mark mail read whatever a later change does inside them.
Marking a message read or unread is not one of those paths at all: it is a change the mailbox owner authored, carried by
the write session above, which nothing on the read side can open, borrow, or reach.

A caller can now author one, through
[`set_mail_flags`](mcp-tools.md#set_mail_flags), and that does not weaken the sentence above. The tool holds neither
session type: it writes the same durable record a rule's action writes and returns, and the account's own run is what
opens the write session and issues the command. So the flag still moves because somebody asked for it in as many words,
never because something read the message — and the type separation stays a property of the code rather than a rule
somebody has to remember.

**MailFathom's own client authors one too, when somebody opens a message and its words reach the screen.** That is the
one place a person's act and a read of the local copy coincide, and it is still an authored change rather than a side
effect of the read: the client submits it to the same route `set_mail_flags` writes through, so it holds neither session
type either, and the account's own run is what tells the mail server. What decides whether it happens is the reader's
own setting, on unless they turn it off, and the grant their credential signed in under — a client whose credential may
not write a flag says so and marks nothing.
[ADR 0026](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md)
is the decision, including why it is the body having been drawn rather than the selection having moved.

Both directions are one mutation and one authored act, because both are the same statement about the same flag. Setting
it is what stops mail MailFathom has already handled from sitting unread in the client the owner actually opens;
clearing it is what lets automation put something back in front of them.

`\Flagged` and a message's keywords are written the same way and under the same rules — an authored change, carried by
the write session, recorded before it is issued, and never a side effect of reading anything. `\Answered` and `\Draft`
stay unwritten on every message the mailbox already holds, because each states that an act was performed rather than
describing the message, and permitting one of them is a decision to reopen
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
rather than a gap to read as permission. The one `\Draft` MailFathom writes is on a message it composed itself and put
in a folder — [the copy of a waiting message](mail-delivery.md#the-copy-in-the-accounts-own-folders) — where the flag
states exactly what happened.

**A keyword replacement reads before it writes, and reads nothing else.** Asking a server to set a message's keywords
outright would replace its entire flag set, clearing `\Seen`, `\Flagged`, `\Answered`, and `\Draft` as a side effect of
writing a label. So a replacement fetches the message's current flags, then removes only the keywords the rule did not
name and adds only the ones it did. The fetch requests flags alone and sets nothing, so the read-side guarantee holds
across it exactly as it does anywhere else.

**A folder that would not keep a keyword refuses it rather than accepting it.** A folder tells MailFathom which flags it
keeps permanently when it is opened, and one that keeps no arbitrary keyword makes an addition or a replacement fail
with `MailboxMutationUnsupported` (25001) naming the account and the folder alias. A removal is never refused for that
reason. The alternative — a command the server accepts and forgets — would leave the rule reading as one that never
fired.

**The stored value stays a mirror.** MailFathom does not write the local flag when it issues the command. The request is
recorded and sent, and `is_remotely_seen` changes only when the reconciliation pass next reads that folder and finds the
flag standing somewhere new — the same way it would change had the owner moved it in their own mail client. So a query
run between the command and that window still reports the last value the server was seen to hold, which is a short lag
rather than a disagreement: the column has exactly one writer, and there is never a local value to reconcile against a
command nobody can prove landed. `is_remotely_flagged` and `RemoteKeywords` are mirrors in exactly the same sense.
[Stored email schema](../architecture/stored-email-schema.md) states the columns.

A flag or keyword change also leaves the occurrence exactly where it was, which is what makes the
suppression below unavoidable rather than tidy — and what makes asking twice mean something specific. The idempotency
identity is the occurrence, the mutation, and who asked, so the same rule asking again about the same message is
answered from its own record and issues nothing. That is deliberate: an owner who reverted the change by hand is not
overruled by the rule that made it. A different rule asking about the same message is a different act and is carried.

### A change nobody finished finishes by itself

Every account run begins by taking that account's unfinished changes in hand, before its folders are touched. Nothing
has to be scheduled for it and nothing has to be asked for: a service restarted between a copy and an expunge, a change
recorded while the server was unreachable, and a command whose acknowledgement was lost on the way back all leave a
durable record in a non-final state, and the first run after the interruption reads it and carries it the rest of the
way. The mailbox ends in the state that was asked for however the process behaved in between.

A change has exactly three acceptable endings — it completes, it is given up on, or the person who asked for it takes it
back — and *pending forever* is deliberately not one of them, because that is the one state that looks like success from
every screen an operator reads.

| Where a change is left | What the next run does |
| --- | --- |
| Recorded, nothing issued | Issues it, from the beginning |
| A relocation whose copy the server confirmed | Removes the source; the copy is never repeated |
| A delete whose `\Deleted` flag landed | Reissues the expunge alone |
| A placement whose answer never arrived | Never reissues it — see below |
| Given up on | Nothing; it stays counted and readable |
| Withdrawn | Nothing; a run never sees it again |

A withdrawal is the third ending, and it is the person who asked for the change taking it back rather than anything
here deciding: a record still at *recorded* is moved to *cancelled*, and no run ever reads it. It is reachable from that
one stage alone, so nothing withdraws a command already issued. [The client
endpoint](../operations/client-endpoint.md#the-mutation-routes) is where a person does it, which is what makes a change
against an unreachable account something they can change their mind about rather than something they wait out.

An unacknowledged placement is the one case that cannot simply be resumed, and how it ends depends on which sequence
issued it. A relocation the server carried with `MOVE` removes the source as part of the same command, so once
synchronization has seen that source occurrence leave its folder the server has said the command ran, and the change is
completed from that observation. A copy and a relocation on a server without `MOVE` both leave the source where it was,
so nothing about it distinguishes a command that landed from one that never arrived — and finding out by searching the
destination folder for a message that looks right would replace a fact with a guess, which
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
refuses. Those wait `UnknownMutationOutcomeGrace` for an observation that may still settle them, and are then given up
on so they stand as dead-lettered rather than as changes still apparently happening.

| Bound | Setting | Default | What it decides |
| --- | --- | --- | --- |
| Changes per run | `MaxMutationsPerConvergencePass` | 50 | How much of a backlog one run takes on; the rest waits for the next run, oldest first |
| Unknown outcome | `UnknownMutationOutcomeGrace` | 6 h | How long an unacknowledged placement waits to be settled before it is given up on |

Nothing here retries on a schedule of its own. A change that fails fails the account's run, so the same jittered backoff
that defers a failing account's folders defers its changes, and one stuck account never delays another — the accounts
run under the same slot count they always did. What bounds a change that keeps failing is still `MaxMutationAttempts`,
counted across runs rather than within one.

What is outstanding is readable while it is outstanding: `mailfathom.mailbox.mutations.outstanding` counts the changes
an account is waiting on, and `mailfathom.mailbox.mutations.oldest_outstanding_age` says how long the oldest has waited.
Both are broken down by the change and by whether it is *pending*, *converging*, or *dead-lettered*, so a count that
stops falling is visible without reading a log. [Telemetry](../operations/telemetry.md) lists them with the rest.

### Renewal

**`PushRenewalInterval` is not a polling interval, despite its name.** It bounds the lifetime of a *single* `IDLE`
command, and it schedules nothing whatsoever: it does not decide when a pass runs, it does not shorten or lengthen the
wait, and lowering it makes MailFathom check nothing more often. Read it as *how long one command may sit before it has
to be re-issued*.

RFC 2177 requires a client to leave and re-enter `IDLE` at least every 29 minutes, and several servers drop a connection
that stays in it longer. A wait longer than this setting is therefore served by a sequence of `IDLE` commands over the
same connection rather than by one long one, and the default of twenty minutes keeps nine minutes below the mandate as
margin for a slow round trip. Renewal reconnects nothing and re-authenticates nothing; a change reported during any
command in the sequence ends the whole wait at once, whichever command of the sequence is in flight.

Lowering it therefore buys nothing but chatter, and raising it past the mandate is refused at startup. The setting an
operator changes to make a folder synchronize more often is `Interval`, which is the ceiling
[the section above](#a-folder-in-push-mode-still-keeps-the-accounts-interval) describes.

### Degradation, and why it expires

A push session fails for the same reasons any mailbox session does, and the [session resilience](#session-resilience)
pipelines have already spent their budget by the time a failure is counted here. What is counted is how long MailFathom
keeps asking a server for a mechanism it is not serving: after `MaxConsecutivePushFailures` consecutive failures the
folder is **degraded to polling**, which is logged at warning level with the account, the alias, the count, and how long
push stays off. The session is closed on every failure, so a retry always starts from a connection it established
itself rather than from one that may or may not still be carrying a protocol conversation.

**What clears the count is a wait that returned, not a connection that opened.** A server can accept the connection and
then serve nothing, and treating the successful connect as evidence would reset the count on every attempt: the account
would reconnect and fail a wait as fast as the server could answer, for as long as the process ran. A session proves
itself by holding a wait to its renewal or by reporting a change, and only then are the failures counted against it
forgotten.

The degradation expires after `PushDegradationPeriod` and push is attempted again. That is deliberate: a server stops
serving `IDLE` for reasons that end — a restart, a connection limit, a mailbox moving — and a folder left on polling
until the next process restart would leave the operator's configured mode wrong for as long as the process runs. The
folder synchronizes on its ordinary interval throughout, so the only thing the period delays is the retry.

### A long-lived connection is a rotation boundary

[Secret rotation](../operations/secret-rotation.md) works because every operation re-resolves its secrets: for polling
that is the next run's connect. A watching session has no next connect, so a rotated password, a replaced trust anchor,
or a withdrawn token would stay in use for the lifetime of the process — and a revoked credential would keep working,
which is the opposite of what rotation is for.

**The connection is therefore the operation boundary here.** Each watching session is opened inside a scope pinned to
the settings snapshot the run used, exactly as a folder work unit is, so the endpoint, the policy, and the credential it
holds all come from one reload. When a newer snapshot is published the session is closed and reopened between runs — at
a point where nothing is in flight — and the reconnection resolves the secrets again. A reload is not distinguishable
from a rotation within it, so every republished snapshot recycles the session rather than being reasoned about;
reconnecting costs one handshake, and the alternative is a revoked credential outliving the reload that replaced it. The
recycle is logged.

### What an operator can see

| Line | Level | When |
| --- | --- | --- |
| `Folder <account>/<alias> is now synchronized in <mode> mode` | Information | The **effective** mode changed — only on a change, so it is not repeated every interval |
| `Mail server reported a change in <account>/<alias>` | Information | A notification ended the wait and started a pass |
| `Account <account> watches N folders through one push subscription` | Information | A subscription was established, and how many folders it covers |
| `…advertises no NOTIFY capability` | Information | The server watches one folder per connection instead; states when a subscription is attempted again |
| `…advertises no IDLE capability` | Warning | Configured push, server declined; states when push is retried |
| `Push session … failed N times in a row, so the folder is synchronized by polling` | Warning | One folder's degradation |
| `Push subscription … failed N times in a row, so its folders are synchronized by polling` | Warning | The whole account's degradation |
| `Push session … was recycled…` / `Push subscription … was recycled…` | Information | A newly published snapshot superseded the one the session was opened under |

The effective-mode line is what answers "which mechanism is this folder actually using", which configuration alone
cannot: an account configured for push may be polling because the server declined, because its recent attempts kept
failing, or because the folder is past the subscription's bound, and those have different remedies. The subscription
line answers the question beside it — whether the account is spending one connection or one per folder. Like every other
line here, all of them carry the account identifier and, where it applies, the folder alias, and nothing derived from a
message.

## Folder aliases and discovery

Configuration never names a remote folder path unless the operator chooses to. It names an **alias** — the stable
operator-facing folder name MailFathom owns, which appears in configuration, in logs, and in future MCP filters, and
which keeps its meaning when the server renames or recreates the folder behind it. Aliases are trimmed and
upper-cased when they are read, so recasing one in configuration is not a second alias.

What the alias points at is discovered rather than configured. Before every run, `MailFolderResolver` lists the
account's folders through `IRemoteFolderCatalog` and matches the mapping against what the server advertised:

| Mapping | Matches |
| --- | --- |
| `RemotePath` | The advertised folder whose path is the configured text. |
| `SpecialUse`, alone | The advertised folder carrying that RFC 6154 role. `Outbox` is refused here, because no server advertises one. |
| `RemotePath` and `SpecialUse` together | The advertised folder whose path is the configured text; the role is what that folder *plays* rather than how it is found. |

A role several folders carry does **not** resolve to whichever the server listed first. `LIST` ordering is a response
order rather than an identity contract, so taking the first would let a reordered response repoint the alias, start a
generation, and resynchronize a different folder with no configuration having changed. The alias is reported ambiguous
instead, and the log names the remedy: configure its `RemotePath`.

One role is MailFathom's own and is therefore never matched against what a server advertised. RFC 6154 defines no
outbox attribute — the outbox a mail client shows is that client's own local queue — so `SpecialUse: Outbox` names a
folder only beside a `RemotePath`, and startup refuses the role written alone, naming the alias and the key it wants. A
provider folder merely *named* like an outbox plays no role either, because nothing here reads a folder's name. What
the role is for is [the copy of a waiting message](mail-delivery.md#the-copy-in-the-accounts-own-folders), which is
mirrored into that folder and withdrawn when the message leaves; an account that maps none mirrors nothing.

A `SpecialUse: Inbox` mapping additionally falls back to the folder named `INBOX` when the server advertises no
special-use attribute at all, because RFC 3501 mandates that name and makes it case-insensitive. That fallback exists
for the inbox alone: every other role exists only as an advertised attribute, and guessing a name for it would bind an
alias to a folder nobody named. An account whose server presents the inbox under a localized name therefore needs no
folder configuration, which is also why the post-binding default is the inbox *role* rather than the path `INBOX`.

An alias that resolves to no single folder ends that one folder's run and no other. It is reported as
`FolderAliasUnresolved` or `FolderAliasAmbiguous`, logged as a warning naming the alias, and the account's remaining
folders continue — a mistyped alias is a configuration mistake, not a mail-server failure, and the three are logged as
different things because each asks the operator for something different.

### What a role says, beside how a folder is found

`SpecialUse` answers two questions that used to be one. *Where is this folder* is answered by whichever of `RemotePath`
and `SpecialUse` the mapping names — the table above. *What is this folder for* is answered by `SpecialUse` alone, and
it goes on being answered when the path is what found the folder:

```json
{ "Alias": "spam", "RemotePath": "INBOX.Spam", "SpecialUse": "Junk" }
```

That mapping is found by its path, so no advertised attribute is needed, and it still answers the question *which
folder of this account is the junk folder*. A server that advertises nothing therefore loses none of what a role is
for, which is what makes a rule or a request written once work against every account you configure.

A role belongs to **at most one folder of an account**. Startup refuses a configuration that gives one role to two
folders of the same account, naming both aliases and the role, because *the* junk folder has to be one folder for the
question to have an answer. Two accounts naming the same role is ordinary: the question is asked per account. Roles are
optional, and a mapping that names only a `RemotePath` carries none.

The question is answered from configuration rather than from the server, so it costs no listing and does not depend on
a run having happened. It is answered the same way for a folder nothing mirrors: `Synchronize: false` withdraws a folder
from what MailFathom stores, not from what it is for, so the role still names it and a rule condition still reads
`folderRole` for it.

Such a folder is a **destination** like any other. Mapping the folder is the whole of what makes it reachable, so a rule
may file into one whether or not the account mirrors it, and startup refuses a destination only when no mapping of that
account answers to the name — by its alias or by its role alike. What the refusal asks for is a mapping, not
`Synchronize: true`.

The direction is what differs: **the source of a change has to be mirrored and its destination only has to be mapped.**
A folder nothing mirrors puts no mail in the local store, so no rule condition ever sees a message in it, no query lists
one, and nothing authors a change against one. Filing *into* it is the whole of what it takes part in.

Wherever something names a folder — a rule's destination, a rule condition's `folderRole` fact, an MCP tool's `folders`
argument — the role is written `role:<role>`, for example `role:Junk`. Anything without that prefix is an alias, so a
deployment whose alias happens to be spelled `Junk` keeps meaning that alias. The name is turned into the folder it
means in one place, so every caller gets the same answer and the same refusal: a role no folder of the account carries
is refused, naming the role, rather than quietly answered with nothing.

### A folder the mapping asked for is created

A mapping naming a `RemotePath` may also ask for that folder to be **created** when the server advertises none at it.
`CreateIfMissing` is the switch, it defaults to `false`, and it is the only thing that ever makes MailFathom change the
shape of a mailbox. Everything else about folder management stays refused: nothing here renames a folder, deletes one,
or unsubscribes from one, and no folder MailFathom did not create is ever subscribed to.

The default is the opposite way round from the three participation switches, and deliberately so. Those withdraw a
folder that already exists from something MailFathom does locally; this one authorizes an act against your mail server.
So a mapping that says nothing behaves exactly as it did before creation existed, and a mistyped `RemotePath` stays the
unresolved alias above rather than becoming a folder on your server named after the mistake.

Creation happens **where the alias is resolved**, which is before the run of a folder MailFathom mirrors and at the
moment a change first files into a folder it does not. Nothing is created by reading configuration, and nothing is
created for a mapping nothing ever resolves. After the first creation the server advertises the folder, so resolution
finds it and no further `CREATE` is issued.

`CreateIfMissing` therefore reaches a `Synchronize: false` mapping as well, on the same terms: nothing happens until
something files mail into that folder, and then the folder the mapping named comes into existence exactly as a mirrored
folder's would. A mapping nothing ever files into stays a mapping and creates nothing.

What the creation does with the awkward parts of IMAP is fixed rather than left to the server that was tested against:

- **A folder already at the path is success**, not a failure. A `CREATE` the server refuses is followed by one lookup of
  the path; a folder now advertised there means another client — or another MailFathom process — created it between the
  listing and the attempt.
- **The path is split with the delimiter the server reports** through `NAMESPACE`, never an assumed `/`. The configured
  text is the server's own path and is never rewritten; the delimiter only says where its levels are.
- **The ancestors the configured path names are created first**, in order, each skipped where it is already there. Every
  one of them is a name you wrote, so none of them is a folder nobody named.
- **A name the server already holds as a hierarchy container, or as a node holding no mail, is refused.** Discovery
  leaves both out of the catalog, so the alias resolves to nothing while the name is taken, and what that needs is a
  different path rather than an act MailFathom can take for you.
- **The created folder is subscribed to**, so it appears in a mail client that lists subscriptions and you can find mail
  a rule filed there. A server that refuses the subscription does not fail the creation — the folder exists, which is
  what was asked for — and the refusal is logged as a warning naming the alias.

A creation the server refuses fails as itself, under error code `26001` and with a message naming the alias alone. It is
deliberately distinguishable from the alias that resolves to nothing: a quota, a namespace that forbids the name, or a
name the server will not accept each ask you for something different from a path you mistyped.

`CreateIfMissing: true` on a mapping that names no `RemotePath` **fails startup**, naming the alias. A folder that does
not exist advertises no role, so creating one from a role alone would mean either an extension whose support is uneven
or MailFathom inventing a name in your own mailbox — and writing the path you wanted is one line of configuration. A
mapping naming a path *and* a role may ask for the creation: the path says what to create, and the role is what the
created folder plays.

The creation is issued over the account's **single write connection**, the same one the mutations run over, so it costs
no second login. It reaches that connection through a port of its own, `IRemoteFolderCreator`, rather than through the
write session: a component that can file a message into a folder cannot create one, and a component that can create one
cannot relocate, delete, flag, or copy a message. A created folder then binds, resolves, and appears in the
mapping-change audit exactly as a discovered one does.

### Why a binding carries a generation

The repository treats `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity, and that tuple
is only stable while its folder component identifies one specific remote folder. UIDVALIDITY is unique *inside* one
mailbox and says nothing across mailboxes, so two unrelated folders on the same server can advertise the same value.
An alias repointed from one to the other while keeping a single persistence identity would let the previous folder's
checkpoint apply to the new folder and skip every message below its last-seen UID — silently, permanently, and with
nothing failing to reveal it.

Each binding of an alias therefore runs under its own **resolution generation**. Resolving an alias to a different
remote path commits a new generation, which has its own `mail_folders` row and therefore no checkpoint, so the new
folder is synchronized from its first UID whatever UIDVALIDITY it reports. Occurrences stored under the previous
generation are kept and stay attributable to the folder they actually came from. A binding is committed before
anything is synchronized under it; the write paths require the row rather than creating one, so occurrences can never
be attached to a generation nothing recorded. A generation already held by a binding naming a *different* remote
folder is refused as a concurrency conflict rather than adopted, so two overlapping runs that resolved the same alias
elsewhere cannot end with one `(alias, generation)` naming two remote folders.

Every binding change is an auditable event carrying the alias, both remote paths, and the new generation.
`LoggedMailFolderMappingChangeAuditor` writes it to the structured log — a first binding at `Information`, a
repointing at `Warning` — and that record is the only place a remote folder path is written outside the database,
because a folder path can itself carry personal or organizational information. Every other log line names the alias.

The listing covers the personal, other-user, and shared namespaces the account can reach, so a delegated mailbox is a
folder an operator may name. It keeps the server's advertised path exactly, including surrounding whitespace, because
IMAP permits a quoted mailbox name that begins or ends with a space and trimming one would persist a path that selects
a different mailbox or none at all. Trimming padding belongs to configured paths alone. What one listing retains is
bounded at 10 000 folders, checked as each namespace is read; a server whose answer exceeds that fails discovery
naming the limit rather than growing without one.

Discovery is read-only by contract as well as by implementation. `IRemoteFolderCatalog` exposes no operation that
creates, renames, subscribes to, or deletes a folder, and the adapter issues an IMAP `LIST`, which selects no folder,
over a connection that pins none. Three kinds of entry are left out of the catalog: `\NonExistent` and `\NoSelect`
ones, because neither is a mailbox that can be opened and binding an alias to one would commit a generation every
later run then fails to select; and an entry that names no folder at all, such as a namespace root with an empty path.
Each exclusion costs that entry rather than the account's whole listing, which would take every usable folder with it.

### What a mapping decides beyond where the folder is

A mapping also says what MailFathom does with the folder once it has found it. Three switches on the same entry answer
that, and each one defaults to `true`, so a mapping that names none of them is a folder that is mirrored, embedded, and
readable by tools:

| Switch | With it `false` |
| --- | --- |
| `Synchronize` | No run schedules the folder, so no connection is opened for it and nothing further of it is stored. |
| `GenerateEmbeddings` | What is stored is never cut into passages and never reaches an embedding provider. |
| `VisibleToTools` | No MCP tool lists, searches, reads, or answers from the folder. |

One role decides something beyond where the folder is too. A mapping of `SpecialUse: Junk` names the folder that
`list_emails` and `search_emails` leave out unless a caller asks for it, and that `ask_mail` leaves out with no way to
ask — [spam classification](spam-classification.md#the-junk-folder-is-left-out-of-listing-and-search) records why. It is
read from the configured role rather than from what a server advertised for the same reason the alias is: the role is
what an operator decided, and a folder is withheld by their decision rather than by a `LIST` response. An account
mapping no junk folder withholds nothing, and every mailbox read behaves as it did before this existed.

None of them changes what an **unmapped** folder is, because an unmapped folder is not a folder with its switches turned
down. A folder no mapping names is not discovered into a binding, stores nothing, and has no alias anything can name it
by: it does not exist for this deployment. Every reader is expressed over the folders configuration maps — the tools and
the two reads that name an email by its identifier over the ones a mapping also leaves visible, chunking and the
embedding backfill over the ones a mapping also leaves embedded, and both rule passes over the ones a mapping mirrors —
so a folder outside that list is outside the answer, and an account mapping nothing has nothing any of them can read.
That is a different thing from `Synchronize: false`, where the mapping goes on naming the folder and the alias goes on
resolving.

**Rows already stored under an alias whose mapping was removed stay in the table, and nothing reaches them.** That is the
same answer removing the `Synchronize` switch gets, for the same reason: nothing here takes local mail away because a
configuration value changed, so the rows are kept and read by nothing — nothing lists, searches, reads, or answers from
them, nothing cuts them into passages or embeds them, no rule pass walks them, and no alias of theirs resolves as a
destination. Mapping the folder again is what makes them readable again, and a folder that was mirrored once keeps its
checkpoint, so mapping it back is the resumption the section below describes rather than a remirror. What it costs
meanwhile is storage, and [`mfctl folder
erase`](../operations/admin-endpoint.md#erasing-a-folder-you-have-stopped-mirroring) is how an operator who wants that
storage back asks for it — an alias no mapping names is exactly what that command accepts.

That distinction is the reason to switch synchronization off rather than delete the mapping. The alias still resolves,
by remote path or by special-use role, so the folder stays a **destination**: a relocation or a copy files a message
into it over the account's existing write session, through the same commands and the same
[recorded change](#every-change-is-written-down-before-it-is-issued) any other destination takes. Nothing new is opened,
no capability is added, and what
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
refuses is untouched — the destination is still never searched for the message afterwards.

**A mapped folder resolves when it is needed rather than when a run reaches it.** No run schedules a folder nothing
mirrors, so nothing would ever bind its alias; instead the alias is resolved the first time a change names it as a
destination, against the folders the server advertises, and the binding and the mapping-change audit record that a
mirrored folder produces are the same ones produced here. What is resolved is then reused rather than looked up per
message, and a server that has since renamed the folder is followed exactly as it is for a mirrored folder — the binding
is replaced by its next generation. A folder that was mirrored once keeps the checkpoint its own runs left, which is what
makes switching it back on a resumption; a folder that never was has none to keep.

Four things can go wrong, and each is reported against the one change rather than ending the pass. A name **no mapping
of the account declares** is refused, naming what was asked for. A mapping the server advertises **no folder for**, and a
role **two advertised folders carry**, are each refused with a classification of their own: nothing falls back to the
configured path and nothing picks one of two folders. A mirrored destination **nothing has bound yet** waits for that
folder's own next run. All four appear in the [rule run history](mail-rules.md) beside the folder the rule wrote.

What differs is what becomes of the local copy. A relocation into a mirrored folder carries the row into that folder and
decides nothing; a relocation into a folder nothing mirrors has taken the message out of the mirrored mailbox, so its
record carries the account's `AuthoredDeleteEmailDisposition` and reconciliation applies it exactly as it does for a
delete — the value resolved when the change was authored, not the one the account carries by the time the source
occurrence is observed gone. There is no setting of its own for this case, and the mutation record still reads as the
relocation it was rather than as a deletion. A **copy** into such a folder leaves the local store untouched, because the
message it duplicates stayed where it was and the duplicate is in a folder nothing mirrors.

**Configuration that asks for embedding or tool visibility on an unsynchronized folder is refused when it binds**, naming
the alias, because a folder that stores nothing has nothing to embed and nothing a tool could read. Leaving a switch out
is not asking for it, so `Synchronize: false` alone binds. The opposite combination — mirrored and embedded but withheld
from tools — binds as well, and it is worth naming what it costs: the passages and their vectors are produced and paid
for, and no reader reaches them today, since the tools are the only readers there are. It is a mapping to write when the
folder is expected to become visible, not one to leave in place indefinitely.

**Mail already stored for a folder whose synchronization is switched off is kept.** Nothing is removed, no pass runs
over the folder, and the checkpoint stays exactly where the last run left it. Rows nobody may read are not stale in a
way anybody observes: an unsynchronized folder is withdrawn from every reader there is, because the mapping derives the
other two switches from this one, so no tool lists, searches, reads, or answers from it, nothing of it is embedded, and
no rule pass reaches it. Erasing them would charge an operator a whole remirror for a switch they may flip back the
same week, so nothing in MailFathom takes local mail away because a configuration value changed.

**Getting the storage back is a command rather than a setting.** [`mfctl folder
erase`](../operations/admin-endpoint.md#erasing-a-folder-you-have-stopped-mirroring) is the one operation that removes
a folder's local copy, and it exists precisely so that no configuration value has to: it erases in bounded passes
through the deletion path an erasing disposition already uses, refuses a folder the account still mirrors, accepts an
alias no mapping names, and clears the folder's checkpoint in the pass that empties it. The binding itself stays, which
is what keeps the alias resolving as a destination.

**Switching the folder back on is an ordinary mirror**, whichever state the folder is in. A folder that never stored
anything — or whose erasure took its checkpoint with the mail — is scheduled, discovered, bound, and backfilled exactly
as one mapped mirrored from the start; a folder that stored something before resumes from its retained checkpoint, so
the run fetches what arrived while it was off and nothing else, and the backward pass converges the retained rows —
flags that changed, messages that left, and messages the server removed during the gap are observed exactly as they are
for a folder that simply went unread for a while. A
`UIDVALIDITY` the server changed meanwhile invalidates the checkpoint on that first run like any other, and
reconciliation resolves what the invalidation leaves behind. There is no branch anywhere that tells a re-enabled folder
from a newly mapped one.

The tool switch is applied in exactly one place. `MailboxScopeResolver` resolves the scope every read model is expressed
in, attaches to it the folders a mapping both names and leaves visible, and answers the same question for the two reads
that name an email by its identifier, so a tool added later inherits the whole rule — the withheld folder and the
unmapped one alike — instead of having to remember either. What it attaches is the list of what may be read rather than
a list of what may not, which is what makes an account with no mapped folder read as nothing rather than as everything.
[Mailbox queries](mailbox-queries.md#folders-withheld-from-tools) states what a caller sees.

## Session resilience

Two dependency classes cover an IMAP session, and each one is resolved for the account it belongs to, so one
unreachable server opens a circuit for its own account and leaves every other account reading.

| Class | Covers | Repeated? |
| --- | --- | --- |
| `MailboxSessionEstablishment` | Connecting, negotiating TLS, authenticating, and selecting the folder read-only | Only a transport-level failure |
| `MailboxDataRetrieval` | UID search, envelope batches, and the `BODY.PEEK` content fetch | Yes; both reads are repeatable by construction |

Failure classification is explicit rather than inherited from a library's guess:

- **Terminal.** A rejected credential, an unusable TLS handshake, an authentication mechanism the operator's allow-list
  refused, and any IMAP command the server refused. Repeating a rejected credential against a mail server is how an
  account gets locked, which is exactly why establishment is a separate class from retrieval. Anything unrecognized is
  terminal too.
- **Transient.** A dropped socket, a server-initiated disconnect, a desynchronized protocol stream, and an attempt the
  pipeline abandoned at its per-attempt timeout.
- **Neither.** The caller's own cancellation. It stays an `OperationCanceledException` so the supervisor can tell a host
  shutting down from a mail server that stopped answering; the latter reaches it as `MailboxUnavailableException` and
  is logged as a deferral naming the account and folder.

Two outcomes produce that `MailboxUnavailableException`: a limit the pipeline imposed, and a transient failure that
survived every attempt. The second needs saying because a retry strategy that runs out of attempts rethrows the last
failure rather than a rejection, so without the translation the most ordinary exhaustion — three dropped connections in
a row — would bypass the supervisor's deferral branch and put a mail-library exception through an application port.
Terminal failures still surface as themselves, because a rejected credential is the operator's to see.

A retried read re-establishes the session when the previous attempt lost the connection. It also re-establishes when
the previous attempt failed on anything worth repeating while the client still claimed to be connected: nothing in an
exception says the command stream is still synchronized, and `IsConnected` only reports a socket. Retrying on such a
connection would spend the whole budget on one unusable session. Every establishment — the
first one and every recovery — resolves the account's secret material again, connects a new client, and selects the
folder with `FolderAccess.ReadOnly`. There is no other selection path in the adapter, so recovery cannot become the
route by which a folder is opened read-write and a fetch sets the remote `\Seen` flag. A regression test drops the
connection during a content fetch and asserts that the recovered session reselected read-only, retrieved through the
peeking fetch, and requested no flag update on either folder.

A recovered connection re-selects the folder, and a server may answer with a new UIDVALIDITY. That folder is not the
one the run started on — its UIDs name different emails — so the session refuses it with
`MailboxFolderRecreatedException` instead of attaching the recovered folder's emails to the previous folder's
checkpoint. The supervisor logs the failure and the next run starts the folder over from an empty checkpoint.

Budgets are the operator settings described in [outbound resilience](../architecture/outbound-resilience.md) and are
bound from `Resilience:MailboxSessionEstablishment` and `Resilience:MailboxDataRetrieval`.

## MIME metadata extraction

`IEmailMimeReader` turns raw RFC 822 content into normalized metadata. The port is named for the mail artifact rather
than for MIME's own vocabulary, because `Message` is reserved repository-wide so it stays unambiguous once AI
conversation types exist. Its implementation is the only place MimeKit types appear; `Application` and `Domain` see
domain values.

The synchronizer calls it on the payload each occurrence was already fetched with, between the fetch and the local
write. Nothing is fetched twice, and no extraction path can select a folder or set a flag.

### What is extracted

| Field | Source | Notes |
| --- | --- | --- |
| Participants | `Sender`, `From`, `Reply-To`, `To`, `Cc`, `Bcc` | Each address carries the role of the header it was written in. Group syntax is flattened to its members. |
| Sent timestamp | `Date` | UTC. Absent or unparseable yields nothing rather than a guess. |
| Received timestamp | topmost `Received` | UTC, taken from the trace after the header's final semicolon. |
| Subject | `Subject` | Decoded; control characters removed so it cannot span lines it never spanned. |
| Thread identifiers | `Message-ID`, `In-Reply-To`, `References` | Angle brackets and the whitespace around them removed; `References` keeps header order and collapses duplicates. |
| Attachment summary | the message structure | Counts, total decoded size, inline-resource count, three markers, and one record per attachment. |
| Sender-authentication verdict | the trusted `Authentication-Results` header, or the message's own DKIM signatures | What was established about who sent the message, and which of the two readings established it. Only the header carrying the account's configured authserv-id is read, and only the topmost such header. Where no such header is found, extraction verifies the message's DKIM signatures itself against the keys their domains publish, which is the one step of a run that queries DNS; the verdict records which of the two readings produced it. [Sender authentication](sender-authentication.md) holds the whole rule. |
| Automation claim | `List-Id`, `List-Post`, `List-Unsubscribe`, `Auto-Submitted`, `Precedence` | What the message says about itself: a list posting, something submitted automatically, or bulk. It is read for [contact collection](contacts.md#collecting-contacts-from-arriving-mail), which refuses the whole message rather than one of its addresses, and it is deliberately not persisted — it is a claim one message makes rather than a property of the mail, and a reader wanting it has the headers. |

An address is normalized to an upper-cased comparison form and keeps what the message wrote alongside it, and that
comparison form is the whole of its identity: two addresses differing only in case, or only in the display name one
sender chose, are one participant in any `Distinct`, set, or dictionary. What the message wrote is never edited —
`"John Smith"@example.com` is a valid mailbox whose space belongs to it, and the domain is taken as whatever follows
the *last* at-sign, because a quoted local part is allowed to contain one. Unfolding is the mail parser's job and has
already happened before an address reaches the domain type.

Only what surrounds a message identifier is removed. What the identifier itself holds is kept, including interior
whitespace: the mail parser resolves header folding long before a value reaches the domain type, so a space that
survives that far is content rather than leftover folding, and `"a b"@example.test` is an identifier a message may
legitimately carry. Deleting its space would record an identifier nobody minted and merge the message into a
conversation it does not belong to. An identifier still carrying a control character after the surrounding transport
is stripped is refused rather than repaired, because no parser produces one and a repaired identifier would be a thread
key nobody wrote.

Case is *not* normalized in a message identifier, on either half. An identifier is an opaque token that the mail
ecosystem compares octet for octet, and a client places an ancestor in `References` by copying the identifier it
received rather than by rewriting it, so case-folding would not repair a difference that arises in practice while it
would merge two identifiers every other client keeps apart — and merging is the direction that joins unrelated
conversations.

A malformed address is dropped rather than repaired — it costs one participant of one message, and a repaired address
nobody wrote would end up in a filter someone trusts.

### What counts as an attachment

The rule is MailFathom's, not a library default. MimeKit's own `Attachments` keys off `Content-Disposition`, and inheriting
that would report an `smime.p7s` attachment on every signed message and would leave an embedded logo counting or not
counting depending on how the sender wrote one header. The classification runs in a fixed order, and the order is the
rule — several ordinary parts satisfy more than one of these at once:

1. **The cryptographic envelope.** A `multipart/encrypted` container marks the message encrypted and **no child of it is
   classified at all**. A `multipart/signed` container marks an unverified signature, classifies its signed content
   normally, and classifies the detached signature not at all. Both are recognized from the container, never from a
   child's media type: PGP/MIME ciphertext is usually typed `application/octet-stream`, so a child-driven rule would
   report a file that does not exist. Recognition requires the `protocol` parameter RFC 1847 mandates on both
   containers, because a container that names no protocol has stated nothing about its children: honoring a bare
   `multipart/encrypted` header would let anyone take a file out of the summary by writing one line. Such a container
   is classified as the ordinary multipart it is, and a real signature or ciphertext part inside it is still caught by
   the cryptographic leaf rule. RFC 1847 gives a signed container exactly two children; one carrying more is
   malformed, and its extra children are still classified rather than dropped, so a part smuggled past the signature
   cannot disappear from the summary and from every filter built on it.
2. **Cryptographic leaf parts** — `application/pkcs7-signature`, `application/pgp-signature`,
   `application/pkcs7-mime`, `application/pgp-encrypted`. This precedes disposition deliberately, because an
   `smime.p7s` part almost always declares itself an attachment. An opaque `application/pkcs7-mime` part *is* the
   message rather than a file beside it, so its `smime-type` parameter sets the matching marker — `enveloped-data` and
   `authEnveloped-data` mark the message encrypted, `signed-data` marks an unverified signature — instead of leaving it
   looking like a message with no parts and no explanation.
3. **The body branch**, resolved recursively rather than at the message root: in a `multipart/mixed` the body is its
   first child resolved again by these rules, in a `multipart/related` the part the `start` parameter names or the
   first child, and in a `multipart/alternative` every member. Resolving only at the root would classify the
   `text/plain` body of the most ordinary message there is — a body and one PDF — as an attachment.
4. **Inline resources** — a part with a `Content-ID` that an HTML body part references, whose disposition is `inline`
   or absent. The absent case carries the weight, because senders routinely omit the header on embedded images. An
   explicit `attachment` disposition overrides it, since there the sender has said what the part is. A `cid:` URL is
   percent-decoded before it is compared, because RFC 2392 escapes whatever cannot appear literally in a URL, so
   `<logo/dark@example.test>` is referenced as `cid:logo%2Fdark@example.test`. A reference counts only where a renderer
   would follow it — an attribute value, or the style sheet a `<style>` element carries — so the body is tokenized
   rather than scanned as text. Matching every occurrence in the source would let a crafted message hide a real file by
   naming its `Content-ID` in visible text, in a comment, or in script data, taking the part out of the summary and out
   of every filter built on it.
5. **Everything else is an attachment.**

Three cases follow from the ordering and are worth naming. A nested `message/rfc822` is one attachment and is not
recursed into, so a forwarded thread does not report the attachment count of every message inside it. A TNEF
`winmail.dat` part is one attachment marked unexpanded, because expanding it is a separate decision with its own
parsing surface. A `text/calendar` part is a body alternative when it sits in a `multipart/alternative`, which is how
Outlook sends an invitation, and an attachment when it is a separate part, which is how several other clients send one.

Attachment presence means the attachment count is greater than zero, so an inline-only or signature-only message does
not have attachments. The signature marker states **presence only** and its name says so: anyone can attach a
signature-typed part, nothing here verifies one, and a marker named after signing would be read downstream as an
authenticity result that was never established. Decryption and verification are out of scope and tracked separately.

Size is the **decoded** octet count, measured by streaming the part through a counter that discards the bytes. MIME
declares no per-part length, so this is measured rather than read, and the sum over a message's attachments does not
equal the message size IMAP reports. The parse itself is persistent: part content stays in the fetched raw MIME and is
read from there when a part is measured, rather than being copied into a second buffer the parser owns, so a message
near `MaxRawMimeBytes` is held once. A forwarded `message/rfc822` part that arrived under a transfer encoding is
decoded like any other part, so its own formatting is what gets measured; one that arrived unencoded is measured as the
parsed message writes itself, which matches its transmitted octets for the CRLF line endings mail transport requires.

### File names are untrusted input

A file name arrives RFC 2047 encoded-word or RFC 2231 continuation encoded, and after decoding can carry path
separators and traversal segments, control characters and line breaks, unbounded length, and bidirectional overrides
that make a name render as something other than what it is. `AttachmentFileName` decodes nothing itself — the adapter
hands it the decoded name — and then removes control and Unicode formatting characters, scalar by scalar rather than
code unit by code unit so that a formatting character written as a surrogate pair is removed like any other, keeps only
the segment after the last `/`, `\`, or `:` so a result can never be a path, trims, and bounds the length at 200
characters. The bound falls on a text-element boundary rather than on a count of UTF-16 code units, so a name ending in
an emoji or a combining sequence is never cut through the middle of a character and left as a string a JSON writer
would have to replace or reject. When any of that changed the name the record says so, so a caller can tell a plain name from a
repaired one. A part left with nothing usable is recorded as **unnamed** rather than given a synthetic name, which
would be indistinguishable from one the sender wrote.

### Structural limits and unreadable messages

`MaxRawMimeBytes` bounds the bytes, but a message far below it can declare tens of thousands of parts or nest
multiparts hundreds deep, which is an inexpensive way to consume disproportionate CPU and allocations.
`MaxMimePartCount` and `MaxMimeNestingDepth` bound the structure, and both are enforced **while the message is read**:
a forward-only `MimeReader` pass reports the structure through callbacks and abandons the message the moment a limit is
crossed, so the object tree the limit exists to prevent is never built. Counting the parts of an already parsed message
would concede exactly the allocations being refused.

A message that crosses a limit, and one whose bytes do not parse, produce a **result rather than an exception**. Badly
formed mail is expected: the occurrence is still stored with its content, the folder checkpoint still advances past it,
and the run reports how many stored messages carried MIME it could not read. Content that defeats the bounded scan for
embedded-resource references is reported the same way, because the alternative is worse than an imprecise label — an
exception there would leave the occurrence unstored and its checkpoint unmoved, so one crafted message would block its
folder on every later run. The supervisor logs that count alongside the
stored and oversized counts. An occurrence stored without content has no MIME to read and is counted as neither
enriched nor unreadable.

Logs record counts and the account and folder identity only. Addresses, subjects, file names, and body text are never
written to a log — a file name is both mail content and attacker-controlled, so logging one would leak and inject at
once. Extracted participant data is personal data by default, which constrains how it is persisted and
indexed.

## Body text and the lexical index

Every message whose MIME is read also yields the searchable text of its body, on the same parse that produced the participants and the attachment summary. Running one walk for both answers is what keeps them consistent: a second walk under slightly different rules could classify a part as an attachment in one place and as the body in the other.

On a deployment with a sensitive-content scanner switched on, that text is redacted before it is stored, so the placeholder is what the index, the chunks, and the vectors are built from; the row also records the configuration it was derived under. What is scanned, what a stamp covers, and what re-derives text written before a switch are in [derived data](sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped). With both scanners off — the default — nothing below changes in any way.

### Which part supplies the text

The text comes from the parts the attachment rules already resolved as the **body branch**, not from every textual part in the message. A `.txt` file attached to an HTML message is a `text/plain` part that is not the body, and indexing it would put a document's contents into the body text of the mail carrying it.

Within that branch:

1. A genuine `text/plain` part wins over every HTML alternative, because it is what the sender wrote rather than a reading of how it was displayed.
2. Only when the message offered no plain-text alternative is text derived from the HTML body, and the result is **marked lossy** so a later chunking or ranking design can decide how much to trust it instead of re-deriving from the message which path produced the words it is holding.
3. A message with neither records **no textual body**.

Derivation uses `MimeKit.Text.HtmlTokenizer`, which arrives with MailKit, so this adds no dependency. That matters beyond convenience: the content read model reaches for AngleSharp twice on a read that asks for both renderings — once inside the sanitizer and once for the document a reading pane draws, neither derived from the other's output — and keeping derivation on MimeKit leaves the text the index is built from off that stack entirely, so what search holds cannot move because a parser did. Nothing in the derivation resolves a URL, loads a style sheet, follows a `src`, or expands an external entity — an HTML body cannot make extraction reach the network or the filesystem however it is written. Script, style, and head content is machinery rather than words and never reaches the index; block elements become line breaks, character references are decoded to what a reader saw, and source indentation collapses.

### Encrypted, empty, and unread are different answers

A message whose **own body** arrived inside a cryptographic envelope records *no extractable text, because it is encrypted* rather than an empty body. A message that genuinely said nothing records *no textual body*. A message whose body nothing read — one that exceeded the size limit, or one whose stored MIME no reader could parse — records *not extracted*. Merging any of them would turn a known gap in search into a silent one; decrypting is out of scope and tracked separately, and the backfill re-evaluates these messages if it is ever enabled.

The encryption question extraction asks is narrower than the one the attachment summary answers. The summary's marker says the message carries encrypted content **somewhere**, which is what a mailbox filter asks; extraction asks whether *this message's own body* is unreadable. A readable message forwarding an encrypted one as an attachment satisfies the first and not the second, and reading the summary's marker there would discard a body its author wrote and can see.

### Trimming quoted history and signatures

Quoted history and signatures are removed from the **end** of the text and nowhere else. Trimming the end alone is the whole safety argument: a reply written above the message it answers keeps its own words, and a reply written inside or below a quoted block is untouched because the block does not reach the end.

Three conventions are recognized: a `-----Original Message-----` style separator, a trailing run of `>`-prefixed lines together with the `On … wrote:` attribution line directly above it, and the RFC 3676 `--` signature separator when at most twenty lines follow it. Where a forwarded chain carries several separators the **topmost** one is honored, because it is the outermost: cutting at the innermost would leave every message above it indexed as though its text belonged to this one. An attribution pattern is anchored on both ends and length-bounded, so a sentence that merely ends in "wrote:" is prose and nothing after it is removed. If trimming would leave nothing — a message that is entirely a forwarded block — nothing is trimmed, because a message whose whole content is a quote is a message whose whole content is that quote.

**The untrimmed text is retained beside the trimmed one.** Trimming is heuristic, and without the untrimmed reading an over-aggressive cut would be the only surviving one until somebody re-derived from the raw MIME. Only the trimmed form is indexed.

### Attachment payloads are never opened

Text comes from the message body only. A PDF, an office document, or an image contributes nothing, so a message whose information lives entirely in an attachment is found by its subject and participants alone. That is a limitation by design rather than an omission: attachment extraction is an unbounded-cost path, and document parsers are a far larger hostile-input surface than MIME parsing. It needs its own bounded-cost design and its own parser-hardening review.

### The index

The extracted text is bounded by `MaxExtractedTextCharacters` and cut on a text-element boundary, so it never ends in half a character. The bound is not about storage: a PostgreSQL `tsvector` cannot exceed one megabyte and the generated column is computed on every insert, so an unbounded body would not degrade search — it would make the row unwritable and stop the folder the message arrived in. What is cut is lost to search rather than lost outright, because the raw MIME stays stored beside it.

The bound is applied **while the text is accumulated**, not to a finished string, so a message far below `MaxRawMimeBytes` but far above the text bound costs the bound rather than the body: the HTML tokenizer stops at it, and the plain-text path stops appending at it. Because the tokenizer stops before a document's layout whitespace is collapsed, heavily formatted markup yields somewhat less than the bound. One decoded copy of each body part is still materialized by the MIME library before any of this sees it, which is what `MaxRawMimeBytes` bounds.

The setting's own ceiling is a value the arithmetic supports rather than a round number. A `tsvector` spends four bytes of entry header, the lexeme, and four bytes of position data per distinct word, so text of single-character words separated by single spaces — the shape that maximizes entries — costs about 4.5 bytes of vector per character. The subject and participant copies take about 101,000 of the 1,048,575 available bytes at their own ceilings, leaving roughly 210,000 characters of body, so the permitted maximum is 200,000.

**Every synchronized message gets a document**, including one whose body was never read. An oversized or unparseable message is indexed on the subject its envelope reported, with its text source recorded as not extracted; without that it would be findable by nothing at all. Such a document is only ever inserted, never written over an existing one — the remote message is immutable, so a run that could not read it is no reason to forget the body a run that could read it wrote earlier.

The search vector is a **stored generated column**, so no code path, migration, or ad-hoc update can leave a row whose vector describes text the row no longer holds. [Stored email schema](../architecture/stored-email-schema.md#the-derived-search-document) describes the table and why the subject and participant addresses are copied into it.

`Persistence:TextSearchConfiguration` names the PostgreSQL text search configuration the vector is built with. It is stated rather than inherited from the server's `default_text_search_config` for two reasons: it decides how every indexed word is stemmed and which are dropped as stop words, so it is part of the schema rather than of a query, and a value taken from a session setting could differ between the process that wrote a row and the one that queries it — a mismatch that shows up as missing results rather than as an error. PostgreSQL refuses the single-argument `to_tsvector` in a generated column for exactly that reason.

The default is `simple`, which neither stems nor drops stop words. That is the honest default for a mailbox: the language of a message is not known when it is indexed, and a language-specific configuration applied to mail written in another one stems words into forms no query produces. A deployment whose mail is reliably in one language configures that language and gains its stemming. Only a configuration a stock PostgreSQL server ships is accepted, matched case-sensitively as PostgreSQL folds an unquoted identifier; anything else **fails startup** with the supported names, because the value is compiled into the generated column and an unknown one would either fail schema creation far from the mistake or index the whole mailbox under the wrong language.

### Backfilling messages stored earlier

Extraction runs on the payload the current run fetched, so messages stored before it existed have raw MIME and no derived text — and, for anything stored before MIME metadata extraction, no classification markers either. A background walk closes that gap.

It selects stored messages that have raw MIME and no search document, re-reads each one, and writes the classification markers **before** deriving text from what that classification found. Reading a marker that was never written would leave every pre-existing encrypted message indistinguishable from an empty one, which is the exact gap the encrypted marker exists to close.

Each batch commits its extractions together with the position it reached, so a committed position always describes committed work and a crash between the two cannot skip a message. A batch also stops early once it is holding enough text, because the batch size bounds emails while the extraction bound bounds characters and the two multiply; the position committed is then the last message actually read, and the rest of that batch is simply the next batch's. The position is what makes the walk finite: selecting only messages without a search document would already be idempotent, but a message no reader can parse never gains one, so such a walk would return the same unreadable message forever and never reach the messages behind it. An unreadable message is counted, stepped over, and left with whatever the server's envelope reported.

The walk reaches no mail server — every byte it reads was fetched and stored by an earlier synchronization run — so it cannot touch a remote `\Seen` flag however long it runs. A run is bounded by `BatchSize` and `MaxBatchesPerRun` and then yields; the worker ends itself once a run finds nothing left, because every message synchronized from then on is extracted as it is written. A failed run keeps the worker alive: the database being briefly unavailable says nothing about whether work remains, and the committed position means the next interval resumes.

### Offering a stored message for embedding

The offer is made by the run's last local step rather than by the commit that stored the message. That step runs after
the classification and rule passes and after every folder has finished, and it names a message whose passages are
already durable in a transaction of their own — so what is offered is committed work, cut under the folder mapping the
message actually ended up in. [The arrival pipeline](../architecture/arrival-pipeline.md) draws that order and says why
the cut is not part of the storing commit.

The offer is deliberately outside a transaction: a provider call inside one would hold a database transaction open for
as long as a remote model takes to answer, and a provider outage would stall the run behind it. It also never waits — a
full backlog refuses the offer rather than making a mailbox as slow as the provider is.

Nothing about that changes what a run reports or how far it gets. A message the backlog turned away is stored with its
passages exactly as one it accepted, and an instance that has activated no embedding profile offers into a backlog
nothing drains. [Automatic embedding](automatic-embedding.md) is what happens on the other side of it.

Where spam classification is switched on over the folder, both halves of that are decided one step earlier: the message
is neither cut nor offered while no verdict exists for it, and junk is neither cut nor offered at all. The verdict is
usually already there, because the run asks for one as soon as it has committed the message and the work runs on the
durable queue rather than in the run. What releases a message the verdict was not there for is the account's own next
run — its rule pass and the cut behind it ask the gate again — rather than the
[embedding backfill](embedding-backfill.md) sweep, which is narrowed by the same admission and therefore never sees a
message no rule pass has stamped.
[Junk is kept out of what a deployment derives from mail](spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
holds the rule and the bound. With classification off, which is the default, the paragraphs above describe every message.

## Reconciling against the server

A synchronization run has two halves. The forward pass only ever moves past the checkpoint, so it discovers new mail and
can never notice that an old message is gone or that its flags changed. The backward pass walks a bounded window of what
is **already stored** and asks the server about it, over the same read-only session the forward pass opened.

### Choosing the window, and why there is no cursor

The window is bounded by `MaxReconciledEmailsPerRun` and ordered by `remote_flags_observed_at`, oldest first. Writing an
observation is what moves a row to the back of that queue, so the pass advances by doing its work rather than by
recording where it stopped. An interrupted run therefore resumes rather than restarting or skipping, and no second
piece of state can drift out of step with the rows it describes.

**Half the window is reserved for mail that has been observed before.** Without that reserve the two groups would not
compete fairly, because the run refills one of them itself: the forward pass can store a thousand new occurrences under
the default batch settings while the window holds five hundred, and every one of them arrives never-observed. Taking
the window in observation order alone would therefore spend all of it on mail that has just arrived, for as long as
mail keeps arriving, and a deletion or a flag change among the mail stored last month would never be noticed again.
Only as much of the reserve as there is mail to fill it is held back, so a mailbox being synchronized for the first
time still gives its whole window to new mail rather than leaving half of it idle.

That is why the window is read as two queries rather than one ordered scan. Both are served by an index scan with no
sort step: the previously observed group by `ix_stored_emails_reconciliation_queue`, which is
`(mail_folder_id, remote_flags_observed_at, uid)` filtered to the rows that are not tombstoned, and the never-observed
group by the occurrence index that already orders a folder by UID. Measured against a folder of 200,000 rows, each
query reads only the rows its limit asks for.

The window is selected under the UIDVALIDITY the open session reports. That is what makes a server-side renumbering
cost nothing locally: rows stored under the previous UIDVALIDITY name a UID space the server abandoned, so they fall
outside every window instead of being reported as missing. **A UIDVALIDITY change can never cause mass local deletion**,
and it is left to the existing invalidation rule.

### What the answer means

The pass issues one `UID FETCH <set> (FLAGS UID)`. IMAP requires a server to ignore a UID that names a message the
folder no longer holds rather than to fail the command, so the answer describes exactly the messages that still exist —
and **silence is the finding**. A UID the server answered for has its stored flag snapshot refreshed; a UID it said
nothing about is a message that left the folder.

One `FLAGS` answer carries both halves of the snapshot: the five system flags and the keywords a client or a server set
beside them, such as `$Junk` or a label. Both are refreshed together, so reading the keywords costs no wider request and
no second round trip, and the stored value stays a mirror of the last observation rather than becoming state of its own.
What one message keeps is bounded — at most 64 keywords of at most 64 characters each, folded to one case and
deduplicated — and a server reporting more has the excess discarded rather than failing the window, because a window
exists to record what the server said about mail that is already stored.
[Stored email schema](../architecture/stored-email-schema.md) describes the column and its index.

### Asking only about what changed

A server that supports modification sequences can be asked a narrower question, and the checkpoint records what it needs
to be asked. Which question it gets depends on what the connection advertises:

| Server advertises | What the pass issues | What establishes existence |
| --- | --- | --- |
| neither | `UID FETCH <window> (FLAGS UID)` | The fetch itself: silence is a deletion |
| `CONDSTORE` and `QRESYNC` | `UID FETCH <window> (FLAGS UID) (CHANGEDSINCE <modseq> VANISHED)` | The vanished report the same command carries |
| `CONDSTORE` only | `UID SEARCH UID <window>`, then the same narrowed fetch | The search, which returns identifiers and no message data |

**The three reach the same end state and differ only in the work.** A narrowed fetch alone cannot tell an unchanged
message from a deleted one — both are silence — so something else always has to say which of the rest still exist. That
is why `CONDSTORE` on its own buys a cheap identifier search rather than nothing, and why the two extensions together
answer both halves in one command. A message the server confirms without describing keeps the flags already stored and
only moves to the back of the reconciliation queue.

The sequence the pass narrows by is recorded on the folder's checkpoint, and **only a pass that emptied the folder's
queue records one**. Recording it after a partial pass would assert that everything older than it is accounted for,
including the occurrences that window never reached, which would then never be asked about again. It also belongs to one
UIDVALIDITY scope: a renumbered folder is a different UID space, so the sequence is dropped with the rest of the
progress rather than carried across.

A checkpoint written before MailFathom tracked sequences carries none, which reads as exactly what it means — a folder
nobody has reconciled by sequence — so the first pass after an upgrade asks about its whole window and records a
sequence at the end of it. Nothing is resynchronized and no mail is fetched twice.

Quick resynchronization is enabled on a connection immediately after authentication, before any folder is selected,
because RFC 7162 allows it nowhere else. It also changes how a server reports a removal — as a vanished report rather
than as an expunge — which is why every session that watches a folder for changes watches both events.

The `\Deleted` flag is deliberately not that signal. It marks a message the folder still holds and still serves, so it
is recorded as a flag and nothing more. Only disappearance from the folder is a deletion here.

An answer that names a UID **without** the flags the command asked for is refused rather than acted on, and the folder's
run ends. That is the one case where the pass fails instead of recording something: the protocol requires a server to
return every data item a `FETCH` named, and an incomplete answer cannot be allowed to degrade into the silence a deleted
message produces — an account configured to erase local copies would otherwise destroy mail on the strength of an answer
the server never gave. The next run asks again.

Everything in this pass is read-only. It asks for flags and for the UID, both of which a server answers out of folder
state, so nothing it issues can set the remote `\Seen` flag — and the requested item set is a named constant that a
test asserts against, because adding any body or header item to it would silently mark every message the pass inspects
as read. The pass holds no port that could write a flag back.

### Changes MailFathom itself made

Not every disappearance is somebody else's act, and not every discovery is new mail. MailFathom relocates and deletes
messages on the server too, and both come back through an ordinary run looking like something to react to: a message
appears in one folder and vanishes from another. Left alone that is a new message and a deleted one, the history forks,
and the mailbox holds a duplicate as far as MailFathom is concerned.

The [durable mutation record](https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/stored-email-schema.md#recorded-mailbox-mutations)
is what tells the two apart, and both halves are joined to it by a recorded fact rather than by a guess at a header:

- **A discovered occurrence** is matched against relocations and copies that named this folder as their destination and
  that the server answered with a `COPYUID` naming this UIDVALIDITY and this UID. A relocation carries the existing
  local email onto the new occurrence instead of storing a second one — no fetch, no MIME read, and no new row, because
  the message is the one already stored and its content, extracted metadata, search document, and passages are all
  keyed by the local identity being carried across. Only the occurrence identity moves, and the flags become unobserved
  so the destination folder's own window is what says what holds there now. A copy is stored like any other discovery,
  because the message it duplicates stayed where it was and a second live occurrence is a second local email; what the
  record settles for it is only whose act the arrival was, and
  [what a message MailFathom copied becomes locally](#what-a-message-mailfathom-copied-becomes-locally) carries the rest
  of that answer.
- **A disappearance** is matched against the source occurrence the record names, which was written down before the
  first IMAP command went out. A match is the relocation or the delete completing, so the disposition below is never
  reached for it: what happens locally is what the record itself decided. A relocation into a mirrored folder decided
  nothing, and the row keeps its place while only its position in the reconciliation queue moves; a delete, and a
  relocation whose destination nothing mirrors, carry the `AuthoredDeleteEmailDisposition` they were authored under and
  [that setting](#what-becomes-of-a-message-mailfathom-deleted-itself) is applied here.
- **A writable flag standing somewhere new** is matched against the stores issued against that occurrence, one value
  at a time. A `\Seen` or `\Flagged` flag is matched against the stores for that flag and the direction is compared as
  well, so a store that asked for the flag to be set answers for the flag becoming set and never for it becoming clear.
  Keywords are matched by computing the set the store would have left on the message that was last read — the earlier
  keywords plus the ones an addition named, minus the ones a removal named, or exactly the ones a replacement stated —
  and comparing that whole set, folded, with what the server now reports. Anything else is somebody else's, which is
  the direction this has to fail in: a message MailFathom labelled and the owner then labelled again reaches evaluation
  as their change, and so does a removal that would otherwise have been read as accounting for a label it never
  touched. The stored snapshot still follows the server in every case — what the match decides is whose act it was, not
  what is recorded.

A relocation or a copy whose server named no placement is joined to no discovery at all. Searching the destination
folder for something that looks like the message would replace a fact the server gave with a guess, and guessing by
`Message-ID` or a content hash is wrong in both directions — a message legitimately appears twice with one `Message-ID`,
and a provider may rewrite headers on copy. The record then stays visibly unjoined instead.

The order the two halves arrive in is not fixed, because the destination folder is not necessarily one MailFathom
synchronizes on the same schedule. A source disappearance seen first settles that half and leaves the row where it is
until the placement is discovered; a placement discovered first settles both, because carrying the row across is what
takes the email out of the source folder locally and no later window could observe the disappearance afterwards.

A discovery or a disappearance that matches no record is treated exactly as it is below. Nothing here changes what
happens to mail a person moved or deleted in their own client.

#### A change MailFathom made is not a change to react to

The join above is one reader of the record. The other is provenance: a change MailFathom performed must not come back
as something to act on. Rules write to a mailbox, which is what makes that a correctness rule rather than tidiness. A
rule matching on folder would file a message, meet it in its new folder, match again, and go round for as long as the
folder is watched — an IMAP command a lap — and two rules with overlapping conditions would do the same to each other.
`\Seen` is the case that makes it unavoidable: a flag change reaches synchronization as a changed modification
sequence, which is precisely the signal a person marking mail read in their own client produces, so a rule conditioned
on unread mail that marks mail read would re-evaluate everything it had just acted on.

A run therefore **withholds** every change a mutation record accounts for, and raises everything else. There is no
cycle counter, no depth limit, and no rate limit involved, and that is deliberate: a cycle limit stops a loop only
after it has already run several times, and a rate limit stops legitimate work at the same threshold. The record
answers the question exactly.

The suppression is scoped to the one change the record describes and expires with it. A relocation or a copy is written
down as observed the first time synchronization meets the occurrence it placed, and answers for no discovery afterwards.
A flag or keyword store moves no occurrence, so it expires against the message's own flag observation instead: it
accounts for a reading only while the last one predates the moment the store completed, and every window advances that
for every occurrence it asked about. So the mailbox owner setting by hand the same flag MailFathom set months earlier,
starring a message it had starred, or moving the message back themselves, reaches rule evaluation as the change it is —
including when they reverted the value before any window had seen MailFathom's own change at all.

The three values are matched independently, because a folder reports them in one `FLAGS` response and any of them may
have moved. One occurrence whose seen state, star, and keywords all changed is one read of the records and up to three
attributions, so a run can withhold the star MailFathom set while raising the label the owner attached in the same
window.

A withheld change is visible rather than silent, because a rule that appears not to have fired is otherwise
indistinguishable from a rule that never matched. Each folder's run reports how many changes it withheld at
`Information`, and names each one at `Debug` with its kind, the mutation, the local email, and the record that
accounted for it. Nothing derived from a message appears in either line. A folder MailFathom never writes to emits
neither.

### What becomes of a message the server no longer holds

This is what happens to a disappearance the previous section did **not** account for — mail that left the folder because
somebody else removed it. Each account chooses, through `RemotelyDeletedEmailDisposition`:

| Value | What happens locally |
|---|---|
| `RetainTombstone` (default) | The row stays and records `remote_expunge_observed_at`. Every mailbox query — the timeline, search, and the content read — excludes it from that moment, and so does every later reconciliation window. Its raw MIME and derived text stay in the database. |
| `EraseLocalCopy` | The row is removed as the disappearance is observed, and PostgreSQL removes the raw MIME, the search document, the chunks, the vectors, and any outstanding repair request with it. A payload stored in a bucket goes too, once the transaction has committed. Nothing of the message survives locally. |

The setting is per account because the accounts of one deployment are not interchangeable: a mailbox whose provider is
the system of record can be followed exactly, while a mailbox MailFathom is the durable copy of must not lose mail because
a server dropped it. The default is the reversible one, so a server that misreports a folder costs a hidden row rather
than a destroyed local copy.

**Changing the setting governs what is observed from then on, and touches nothing already recorded.** A message already
tombstoned under `RetainTombstone` is outside every later window — the server has nothing left to say about it — so
switching an account to `EraseLocalCopy` erases the disappearances observed after the change and leaves the existing
tombstones exactly as they are. Cleaning those up is deliberately not automatic; [#170](https://github.com/Krzysztof318/MailFathom/issues/170)
tracks the retention grace period and the bounded garbage collection that will own it, and that is also where a delay
between observing a disappearance and erasing the local copy will come from.

The whole window is applied as one set of writes against rows read in one query, so a pass costs one round trip rather
than one per email inside an open write transaction.

Every write is idempotent and none of them moves state backwards, which is what makes replaying a window after a commit
conflict safe. A tombstone keeps the timestamp of the run that first observed the disappearance rather than being
restamped, and a row already removed is not an error to remove again. An email whose stored observation is **newer** than
the window being applied is left alone entirely: another writer has since asked the same server, its answer supersedes
this one, and that includes the case where this window would have deleted the email.

### What becomes of a message MailFathom deleted itself

A deletion the mailbox owner authored is a different act from the one above, and it is answered by a different setting.
`AuthoredDeleteEmailDisposition` decides it, per account, and **takes precedence over `RemotelyDeletedEmailDisposition`
for every disappearance a mutation record accounts for** — which is to say the setting above is never consulted for one.
That separation is the point of having two: without it, an account configured to erase what its server loses would also
erase what MailFathom was just told to delete, and that is precisely where the owner is likeliest to have meant the
opposite. Deleting on the server frees quota; the local archive is usually the reason to do it.

| Value | What happens locally |
|---|---|
| `RetainLocalCopy` (default) | The row stays readable. It records `remote_expunge_observed_at`, because the server no longer holds the message and the reconciliation queue must stop asking about it, and it also records `is_retained_after_authored_delete`, which keeps it inside the timeline, search, and content read. Freeing space on the server is then not the same instruction as forgetting the mail. |
| `RetainTombstone` | Exactly the counterpart of the default above: the row stays and every mailbox query excludes it from that moment. The record that the email existed survives, so an authored delete is auditable rather than silent, and the mail itself stops being reachable. |
| `EraseLocalCopy` | The row is removed as the disappearance is observed, and PostgreSQL removes the raw MIME, the search document, the chunks, the vectors, and any outstanding repair request with it. A payload stored in a bucket goes too, once the transaction has committed. Nothing of the message survives locally. |

The default is the value that destroys nothing, for the reason the other setting's default is: a disposition nobody has
thought about must not be why mail stops being readable.

**The value is resolved when the delete is authored, not when it completes.** Those are different runs — the commands go
out now and the local copy is disposed of by the synchronization run that later sees the message gone — so the answer is
written onto the mutation record and read back from there. Changing the setting therefore governs the deletes authored
after the change and leaves one already in flight exactly as it was begun.

A deletion that never reaches the server changes nothing locally either. The disposition is applied where reconciliation
observes the message gone from its folder, so a delete that was refused, abandoned, or is still in flight has left the
local copy alone; and because all three values take the row out of the reconciliation queue, the disposition is applied
once per delete however many windows pass over the folder afterwards.

**One relocation is answered by this setting too.** A message moved into a folder MailFathom does not mirror has left the
mirrored mailbox rather than moved inside it, so its record carries this value and reconciliation applies it to the
source row exactly as it does above — under the same resolution rule, at the same moment, once. There is no setting of
its own for that case, and the record goes on reading as the relocation it was rather than as a deletion. A relocation
whose destination *is* mirrored reaches none of this, because its row is carried into the destination folder instead.
[What a mapping decides beyond where the folder is](#what-a-mapping-decides-beyond-where-the-folder-is) states which
destinations are which.

### What a message MailFathom copied becomes locally

A copy is the one mutation that ends with the message in two places at once, and the answer to *is that one local email
or two* is **two**. The copy is discovered by the destination folder's own forward pass and stored exactly as any other
arrival is: its own row, its own raw MIME, its own extracted text, search document, passages, and vectors. Nothing is
carried across, the source row keeps its occurrence and everything derived from it, and the mutation record settles only
whose act the arrival was.

That is a decision rather than the absence of one, and
[ADR 0008](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md)
records why. The short of it: a stored row means one occurrence the server holds, `UID COPY` really does create a second
message with its own UID, and a model that made the two one local email could only do so for the copies MailFathom
itself performed — a copy the owner made in their own mail client is joined to no record, and identifying it by
`Message-ID` or by a digest is the guess the join already refuses.

What it costs an operator is worth knowing before a rule starts filing mail:

- A search across folders returns the message **twice**, once per folder, each hit naming where it was found. A search
  scoped to one folder returns one.
- Everything is stored twice, including one vector per passage per active profile — which is a **paid** derivation.
  Copying is a storage decision as much as a filing one.
- Deleting one copy leaves the other, locally and on the server. That is what the mailbox holds; the two are not
  versions of one message.
- An export or an erasure request reaches both, because both are ordinary rows that any selection over sender,
  recipient, subject, or content reaches. Neither hides behind the other's identity.

One limit follows from where the placement comes from. The arrival is withheld from rule evaluation only where the
destination folder answered with `COPYUID`; on a server advertising no `UIDPLUS` the copy still happens and is still
never repeated, but the arrival reaches rule evaluation as an ordinary discovery, because nothing joins it to the record
that caused it and searching the folder for something that looks right is refused.

### What a run reports

The counts reach the log and nothing else does. A window that observed something logs how many snapshots it refreshed
and whether the folder has more to reconcile; a window that found messages gone logs that at information level, with the
account, the folder alias, the count, and the disposition that was applied. A run that recognized changes of
MailFathom's own logs them on a line of their own, at information level: how many discoveries carried an existing local
email across, and how many occurrences left the folder because MailFathom moved or deleted them. Each of the three
writable values a run found standing somewhere new is counted separately beside those — how many messages somebody
else marked read or unread, how many they starred or unstarred, and how many they relabelled — because they are three
different acts and an operator reading one number could not tell which of them happened. Without it an operator
reading the counts would see mail arriving in one folder and vanishing from another, which is exactly the conclusion the
join exists to stop the system itself from drawing. No subject, address, or fragment of a message takes part in deciding
whether a message still exists, so none of it is read to decide it and none of it can reach an audit line.

## Bringing stored mail up to a later release

A release adds properties to stored mail, and every message stored before it carries none of them. Nothing above fills
them in: the forward pass asks the server only about UIDs above the folder's checkpoint, and the backward pass
reconciles what disappeared rather than re-reading what stayed, so mail already mirrored keeps whatever shape it had on
the day it arrived. Two commands answer that, because the properties have two sources and very different costs.

**Which one a property needs is decided by where its value comes from.** A property the stored payload already carries —
the [sender-authentication
verdict](../architecture/stored-email-schema.md#the-sender-authentication-verdict) is the worked example, read out of
the headers and signatures the message itself carries — is re-derivable from the MIME on this deployment's own disk. A property only the mailbox knows
— the [remote flags and keywords](#reconciling-against-the-server), the internal date, anything a later release starts
recording from the envelope — exists nowhere locally, so nothing short of fetching the message again produces it.

**`mfctl mailbox rederive --account <id> [--folder <alias>]` asks for what is already stored to be re-read.** The walk
itself takes the scope's stored emails in the order of their local identity, reads each one's raw MIME back through
[`IEmailMimeReader`](#mime-metadata-extraction), and writes the row's own columns. It opens no mailbox session at all,
so it cannot touch a remote `\Seen` flag however long it runs, and it rewrites no stored content.

**The request records the run and returns; the deployment carries it.** The command writes one row in
`mail_rederivation_runs` — one per scope, holding the run's identity, when it was asked for, how much it has re-read,
and when it ended — and enqueues a `rederive-stored-mail` job against the account. The walk is therefore durable
background work under the [job store](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0009-durable-job-store-and-execution-identity.md), which is what
lets it outlive the operator's terminal and survive a restart of the deployment.

**One attempt of that job is several bounded passes with its lease renewed between them.** A pass is fifty messages a
batch, ten batches, and no more than sixty-four mebibytes of raw MIME read, whichever comes first, because a scope of
messages carrying attachments reaches the second ceiling long before the first. Each batch commits its position beside
its writes in one transaction, in `mail_rederivation_positions`, keyed by the scope; each pass adds what it re-read to
the run. An attempt that ends before the scope does — a shutdown, the execution timeout — leaves everything its passes
committed and enqueues the run's next segment, which resumes past the stored position rather than starting the scope
over. A walk that reaches the end of its scope removes its position row and ends the run, so asking again after the
next release starts at the beginning.

**Asking again while a run is outstanding is answered with that run.** The job's idempotency key carries the scope, the
run, and the segment it is on, so a second request finds the segment already queued rather than starting a second walk
over the same mail — and where the run was written down but its work never reached the queue, the same request is what
repairs it. `mfctl mailbox rederive-status` reads the run from the row.

A message no reader can parse keeps whatever an earlier release read from it and the walk moves past it; a row whose
raw MIME is no longer stored is counted apart, because only a fetch could bring that message back. Neither is a failure
and the run counts both. A segment that fails permanently dead-letters like any other job, which is where a run that
stopped moving is read and put back.

**The conversation assignment is the one write this pass makes outside the row's own columns, and it is deliberate.**
A [conversation](../architecture/stored-email-schema.md#the-conversation-a-message-belongs-to) cannot be a column: it is
decided from this row's message identifiers and recorded as a relation other rows share, so a mailbox stored before
threading existed becomes threaded only if re-derivation is allowed to write it. Everything the assignment touches is
still derived from stored MIME and from nothing a mail server would have to be asked for, which is what keeps this
command the cheap route rather than the expensive one. It is idempotent in the same sense the rest of the pass is:
re-deriving a scope that is already assembled reaches the same conversations, starts none, and changes nothing — the
identifiers a message names are bound to a conversation once, and a second pass finds the binding it wrote the first
time.

**`mfctl mailbox rewind --account <id> [--folder <alias>]` discards the durable progress instead.** It removes the
`synchronization_checkpoints` row of every binding in the scope — the UID the forward pass resumes from and the
`ReconciledThroughModSeq` beside it, which go together because they describe one UIDVALIDITY scope — so the next run
starts at the first UID inside the account's [synchronization window](#bounding-how-much-mail-a-run-brings-in) and
everything the server knows is read again. That is also its cost: the whole scope off the wire, back through MIME
extraction, and back into the content store. The command therefore reads how many stored emails the scope holds and
puts that figure in front of the operator before it discards anything, in the way `mfctl embedding activate` confirms a
figure it will spend; `--yes` states the agreement in the command for a scripted rewind.

**The figure informs the question and never answers it**, including when it is zero. What the count measures is the
mail this deployment stores, which is deliberately not what a run would fetch: mail that arrived since is fetched
without ever having been stored, and a folder whose local copies are all tombstoned counts nothing while its bindings
still hold the progress a rewind takes away. So every rewind is confirmed or carries `--yes`, and an invocation with
input redirected and neither is refused rather than reading an answer out of whatever was piped in.

**A rewind erases nothing and duplicates nothing.** What it removes is one row of progress per binding; the mail, its
raw MIME, its passages, and their vectors stay exactly where they are, and re-reading an occurrence upserts the local
email already stored at `(account, folder, UIDVALIDITY, UID)` rather than storing a second one. It records no second
placement observation either — an observation belongs to a mutation record naming that occurrence, and an ordinary
re-read names none — so [mutation reconciliation](#a-change-nobody-finished-finishes-by-itself) reads a rewound folder
as the folder it already knew rather than as a mailbox that filled up with copies.

**A run in flight loses the race rather than corrupting the rewind.** Such a run decided from progress the removal has
taken away, so its advance is refused by the checkpoint's compare-and-set contract instead of being written in front of
mail the rewind was about to have re-read; the folder is deferred and the account's next run picks it up from the start
of the window. The rewind's own answer names the folders whose bindings held progress, which is what says the removal
was the write that won.

**Neither command touches embeddings, and neither re-runs classification.** Passages and vectors are derived from text
that the same bytes read by the same reader produce unchanged, so re-deriving them would spend a re-cut and a provider
bill to arrive back where they already are — [ADR
0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
makes that a deliberately confirmed act rather than something a metadata refresh performs. A message re-read after a
rewind is chunked, classified, and evaluated only where it carries no passages, no verdict, and no rule stamp already,
which is the same gate every arriving message passes. Text written under a sensitive-content configuration this
deployment no longer runs is the one derived value a refresh does not correct, and the [extraction
backfill's rebuild](sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped) already owns it.

[Administering a deployment](../operations/admin-endpoint.md#bringing-stored-mail-up-to-a-later-release) is the
operator's reference for all three routes.

## Configuration

Synchronization is disabled by default:

```json
{
  "MailSynchronization": {
    "Enabled": false,
    "Interval": "00:05:00",
    "MaxFailureBackoff": "00:30:00",
    "MaxConcurrentAccounts": 4,
    "MaxConcurrentFoldersPerAccount": 1,
    "ShutdownDrainTimeout": "00:00:10",
    "MaxMetadataBatchSize": 100,
    "MaxRawMimeBytes": 26214400,
    "MaxMetadataBatchesPerRun": 10,
    "MaxReconciledEmailsPerRun": 500,
    "MaxMimePartCount": 1000,
    "MaxMimeNestingDepth": 30,
    "MaxExtractedTextCharacters": 100000,
    "PushRenewalInterval": "00:20:00",
    "MaxConsecutivePushFailures": 3,
    "PushDegradationPeriod": "00:15:00",
    "MaxSubscribedFolders": 20,
    "Accounts": [
      {
        "AccountId": "primary",
        "DisplayName": "Personal mail",
        "Host": "imap.example.test",
        "Port": 993,
        "UserName": "mailfathom@example.test",
        "Mode": "Push",
        "EarliestEmailReceivedDate": "2024-01-01",
        "RemotelyDeletedEmailDisposition": "RetainTombstone",
        "AuthoredDeleteEmailDisposition": "RetainLocalCopy",
        "Secrets": {
          "Password": {
            "Name": "imap-primary-password",
            "SecretReference": "systemd-credential:imap-primary-password"
          }
        },
        "TransportSecurity": {
          "ConnectionSecurity": "TlsOnConnect",
          "PermittedAuthenticationMechanisms": [ "SCRAM-SHA-256", "PLAIN" ],
          "AllowInsecureConnection": false,
          "AllowClearTextAuthenticationOverUnencryptedConnection": false,
          "CertificateTrust": "SystemTrustStore"
        },
        "Folders": [
          { "Alias": "inbox", "SpecialUse": "Inbox" },
          { "Alias": "archive", "RemotePath": "Archief/2026" }
        ]
      }
    ]
  }
}
```

Optimistic concurrency is configured once for the whole deployment, outside the synchronization section, because it bounds every local writer rather than this feature. The PostgreSQL password sits in the same section as an optional secret reference, so the connection string in configuration keeps host, database, and user name and never carries the credential:

```json
{
  "Persistence": {
    "MaximumConcurrencyCommitAttempts": 2,
    "TextSearchConfiguration": "simple",
    "Password": {
      "Name": "postgres-password",
      "SecretReference": "file:/run/secrets/postgres-password"
    }
  }
}
```

The extraction backfill has a section of its own rather than a block inside the synchronization settings, because it reaches no mail server: it reads raw MIME an earlier run already stored, so it needs no account and must not be disabled with synchronization. It shares that feature's extraction limits, which are what decide how a message is read. It is on by default, since a deployment that stored mail before extraction existed would otherwise keep that mail out of search silently and indefinitely; a deployment with nothing to backfill pays one query, because the first run finds no work and the worker stops.

```json
{
  "MailExtractionBackfill": {
    "Enabled": true,
    "Interval": "00:00:30",
    "BatchSize": 50,
    "MaxBatchesPerRun": 10
  }
}
```

`MaxReconciledEmailsPerRun` bounds the backward pass the way the batch settings bound the forward one, and `RemotelyDeletedEmailDisposition` is the per-account choice [Reconciling against the server](#reconciling-against-the-server) describes. It binds as one of the two names `RetainTombstone` and `EraseLocalCopy`, and a value that is neither **fails startup** rather than falling back to a default: the setting decides whether stored mail is destroyed, and a typo in it must never be the reason mail survives or does not. That check is explicit rather than left to the binder, because a bare number binds onto an enum whether or not any member carries it — strict binding rejects unknown keys and failed conversions, and this conversion succeeds.

`AuthoredDeleteEmailDisposition` answers the same question for the opposite act — a deletion MailFathom performed on the owner's instruction rather than one it observed — and [takes precedence over the setting above](#what-becomes-of-a-message-mailfathom-deleted-itself) for every such deletion. It binds as `RetainLocalCopy`, `RetainTombstone`, or `EraseLocalCopy`, is validated the same way, fails startup the same way, and defaults to keeping the local copy readable.

Every configured account carries a `DisplayName`, whether or not synchronization is enabled, because the stored copy stays readable after the switch is turned off and the name is what a caller reads the account back as. There is no fallback to `AccountId`: a name MailFathom invented would be published to callers as though an operator had chosen it. The two share one naming space — a request may name an account by either — so startup refuses a display name another account's identifier or display name already carries, compared without regard to case; one equal to the account's own identifier is accepted, since both spellings then reach the same mailbox.

When enabled, at least one account with a non-blank `AccountId`, host, and user name must be configured. The account password is not a configuration value at all: `Secrets.Password` carries a reference, and startup fails when it cannot be resolved. Each entry of `Folders` names an alias and at least one of `RemotePath` and `SpecialUse`; naming neither, or naming a role that does not exist, fails startup with a message identifying the alias. Naming both is how a folder found by its path still [plays a role](#what-a-role-says-beside-how-a-folder-is-found), and two folders of one account naming the same role fails startup with a message identifying both aliases and the role. Supported roles are `Inbox`, `Archive`, `Drafts`, `Sent`, `Junk`, `Trash`, `All`, `Flagged`, `Important`, and `Outbox`; `Outbox` is MailFathom's own rather than one RFC 6154 defines, so an entry naming it without a `RemotePath` fails startup naming the alias. An entry naming a `RemotePath` may additionally set `CreateIfMissing`, which defaults to `false` and is what [creates the folder](#a-folder-the-mapping-asked-for-is-created) when the server advertises none at that path; setting it on an entry that names no `RemotePath` fails startup naming the alias. If an account omits `Folders`, its supervisor applies the post-binding default of one alias `inbox` mapped to the inbox role; explicit folder lists replace that default.

### Bounding how far back a run reaches

`EarliestEmailReceivedDate` is optional and per account. Omitting it synchronizes every email the server still holds,
which stays the default and keeps existing configuration behaving exactly as before. Naming a date bounds the corpus: an
account whose archive predates the assistant's usefulness synchronizes the mail worth asking about instead of fifteen
years of backlog that would otherwise be searched, fetched, parsed, stored as raw MIME, and indexed before anything
recent arrived. It binds as a plain date — `2024-01-01` — and the date itself is inside the window.

**The bound compares against arrival, not against the sent date.** It becomes the IMAP `SINCE` key, which compares the
server-assigned `INTERNALDATE`, disregarding time and time zone. It deliberately does not compare the envelope `Date`
header that MailFathom stores as the email's sent timestamp, and the two disagree for imported and forwarded mail:

- An archive copied onto a new server carries the arrival date of the copy, so a migrated mailbox is *not* bounded by
  what its mail says it was sent on. A migration that has to be bounded needs the date the migration ran, not the date
  the mail was written.
- A message forwarded today carries the old header date of what it quotes. Bounding on the header would have made such a
  message permanently invisible, because a bound on the sent date keeps excluding newly arriving old-dated mail forever
  rather than only bounding a first run.

Arrival is also the property that grows with the UID sequence a run walks, and every server keeps it for every email,
while a `Date` header can be absent or unparseable.

The bound is pushed into the search rather than applied to its answer: the remaining UID range and the date condition go
to the server in one `UID SEARCH`, so an excluded UID is never fetched. The checkpoint still advances through everything
the search inspected, not merely through the last email it described, so a folder whose entire backlog is excluded
advances once to the highest assigned UID, reports no remaining work, and ends. Read-only behavior is unchanged: no path
introduced by the bound can set `\Seen`.

**Widening the bound later does not revisit mail a run already passed.** The folder checkpoint records how far the UID
sequence has been walked, and nothing about a changed date rewinds it, so moving the bound further back only affects UIDs
that have not been reached yet — mail below the checkpoint stays absent, and the absence shows up as a missing search
result rather than as an error. Recovering it means discarding that folder's progress with [`mfctl mailbox
rewind`](#bringing-stored-mail-up-to-a-later-release) so the next run walks the folder from its first UID again, which
is safe because metadata and content writes are idempotent on the remote occurrence identity, and expensive because
everything inside the widened window is fetched and indexed again.
Narrowing the bound removes nothing that is already stored; pruning stored mail is a separate concern.

A date later than the current UTC date fails startup, naming the account, because it would exclude every email in the
mailbox — indistinguishable from synchronization silently doing nothing. That rule needs the current date, which no data
annotation on bound options can reach, so it runs as the options framework's custom validator, at startup and then
whenever a reload materializes new options, on the same terms as this section's other bound-options rules. It is not gated on `Enabled`, for the same reason secret resolution is not: a date an
operator wrote is a date they intend to synchronize from. A value that is not a date at all fails startup while binding,
which is what the section's strict binding buys here — a collection item whose conversion fails is otherwise dropped, and
a typo in this one setting would remove the whole account from synchronization.

### Secrets

Every secret-bearing setting — the account password, the trust anchor, the database password — binds to a block whose `SecretReference` property holds a `<scheme>:<target>` reference rather than the credential, and which names itself through a required `Name` and states its own `Lifetime`. The host resolves every reference before any hosted service starts and reports all failures at once, each naming its configuration path and a stable failure identity and nothing else. Each actual connection attempt resolves again and erases the material when it finishes, so no long-lived copy exists and a rotated credential is observed without a restart.

Secret resolution is not gated on `Enabled`, unlike the transport security rules. Every configured account's password reference is resolved at startup even when synchronization is disabled, because a reference an operator wrote is a reference they intend to work, and discovering it broken at the moment synchronization is switched on is worse than discovering it now. An account that is configured but has no reachable password therefore fails startup; remove the account rather than disabling synchronization around it.

[Secret provisioning](../operations/secret-provisioning.md) is the operator reference: the four schemes, the systemd, Compose, and Kubernetes provisioning paths, the three interpretation modes and why `ReferenceOnly` is the default, and the in-memory exposures that need operational rather than code-level mitigation.

`MaxMimePartCount` must be between 1 and 100 000 and `MaxMimeNestingDepth` between 1 and 1 000. The defaults are far
above what real mail declares — a message with a thousand parts or thirty levels of nesting was constructed rather than
written — so lowering them refuses more and raising them costs work per message rather than buying anything.

Account identifiers and folder aliases must be unique after domain normalization, IMAP ports must be between 1 and 65535, and `MaximumConcurrencyCommitAttempts` must be between 1 and 10. The default of two attempts covers the single lost race that a rare conflict represents; a folder deferred after that is retried by the next run anyway.

### Supervision bounds

`MaxConcurrentAccounts` must be between 1 and 100 and `MaxConcurrentFoldersPerAccount` between 1 and 20;
[Per-account supervision](#per-account-supervision) states what each admits and why the folder default is one.

`MaxFailureBackoff` bounds how far the delay between an account's runs may grow while its runs keep failing, and it
must not be shorter than `Interval` — a shorter ceiling would ask backoff to run a failing account more often than a
healthy one, so it fails startup naming both values. A deployment that wants no backoff at all sets it equal to the
interval, which leaves every wait exactly one interval long.

### Push settings

`Mode` is per account and defaults to `Polling`; [Push synchronization](#push-synchronization) states what each value
costs and why the default is the one that opens no connection. It binds as one of the two names and a value that is
neither **fails startup** rather than falling back — an operator who asked for push and mistyped it would otherwise get
polling with nothing reporting the difference, for the same reason the disposition above is checked explicitly.

The four deployment-wide settings shape how a watched folder behaves and apply to every account that asked for push.
`PushRenewalInterval` accepts one to twenty-nine minutes, the ceiling being what RFC 2177 mandates, and defaults to
twenty; it is the lifetime of one `IDLE` command rather than a cycle, which [Renewal](#renewal) states in full because
the name reads the other way. `MaxConsecutivePushFailures` accepts 1 to 100 and defaults to three, and
`PushDegradationPeriod` accepts ten seconds to a day and defaults to fifteen minutes; together they decide when a folder
stops retrying push and when it starts again.

`MaxSubscribedFolders` accepts 1 to 100 and defaults to twenty. It bounds how many folders one subscription may name on
a server that supports them, and it exists because such a server refuses an oversized subscription as a whole rather
than mailbox by mailbox. Folders past it synchronize on the account's interval, in configuration order, and the setting
does nothing at all on a server that watches one folder per connection; [One connection, or one per
folder](#one-connection-or-one-per-folder) states the whole matrix.

**How often a push folder synchronizes is still `Interval`.** Push adds no schedule of its own: it ends that wait early
when the server reports a change, and [A folder in push mode still keeps the account's
interval](#a-folder-in-push-mode-still-keeps-the-accounts-interval) records why the interval stays.

`ShutdownDrainTimeout` is how long shutdown waits for the work units already under way after it has stopped scheduling
new ones. It accepts anything from zero to two minutes and defaults to ten seconds. The host's shutdown budget is
derived from whatever is configured, so every accepted value is honored rather than being cut short by the framework
default; changing it is restart-required, which a shutdown budget is by nature. Zero cancels in-flight work
immediately, and what a run had already committed stays durable either way.

### Transport security

Every setting below lives in the account's `TransportSecurity` section, which `MailAccountTransportSecurityOptions` in `Infrastructure` binds and validates. `ConnectionSecurity` selects one of five modes and defaults to `TlsOnConnect`:

| Mode | Behavior |
| --- | --- |
| `TlsOnConnect` | Encrypts immediately with implicit TLS. |
| `StartTlsRequired` | Requires STARTTLS and fails when the server does not advertise it. |
| `StartTlsWhenAvailable` | Uses STARTTLS when advertised and otherwise continues unencrypted. |
| `Auto` | Lets the client negotiate and continues unencrypted when the server offers no encryption. |
| `None` | Uses no encryption. |

Only the first two guarantee that nothing travels unencrypted. The other three require `AllowInsecureConnection: true`, including `Auto` and `StartTlsWhenAvailable`: an opportunistic mode completes the connection in clear text whenever the server declines encryption, which is the same exposure as `None`.

`PermittedAuthenticationMechanisms` is an **unordered** allow-list that defaults to `[ "PLAIN", "LOGIN" ]` when omitted, which is safe under the default `TlsOnConnect` and trips the clear-text rule on any mode that can stay unencrypted. The default is applied after binding rather than as a property initializer, because the configuration binder appends bound entries to an existing list and would otherwise keep `PLAIN` and `LOGIN` permitted alongside whatever the operator configured. Supported names are `PLAIN`, `LOGIN`, `CRAM-MD5`, `DIGEST-MD5`, `SCRAM-SHA-1`, `SCRAM-SHA-1-PLUS`, `SCRAM-SHA-256`, `SCRAM-SHA-256-PLUS`, `SCRAM-SHA-512`, `SCRAM-SHA-512-PLUS`, `NTLM`, `XOAUTH2`, and `OAUTHBEARER`; names are matched ignoring case and duplicates collapse while keeping the configured order. That order is presentation only: the adapter narrows the server's advertised set to the permitted names and lets MailKit pick the strongest survivor, deliberately rather than obeying the configured sequence, so a list that happens to put `PLAIN` before `SCRAM-SHA-256` still authenticates with SCRAM when the server offers it. Permitting `PLAIN` or `LOGIN` on a mode that can stay unencrypted additionally requires `AllowClearTextAuthenticationOverUnencryptedConnection: true` on top of `AllowInsecureConnection: true`, because those two mechanisms hand over the reusable password itself.

The MailKit adapter removes every non-permitted mechanism from the set the server advertised before it authenticates. It never restores a removed mechanism after a failed authentication, so a server cannot negotiate its way to a mechanism the operator refused.

When nothing permitted remains — because the server advertised no SASL mechanism at all, or only ones the allow-list refuses — what happens next depends on the allow-list itself. A server is not required to advertise `AUTH=` capabilities, and RFC 3501 leaves the `LOGIN` command as the client's last resort; MailKit issues exactly that when the advertised set is empty, and still refuses when the server advertises `LOGINDISABLED`. That command hands over the reusable password in clear text, which is the same exposure `PLAIN` and `LOGIN` carry, so the adapter permits it precisely when the allow-list already permits a clear-text mechanism — and, on a mode that can stay unencrypted, only after the separate `AllowClearTextAuthenticationOverUnencryptedConnection` opt-in the policy already requires. An allow-list of challenge-response mechanisms alone is a statement that the password must never travel in clear text, so it still ends the attempt with `MailAuthenticationMechanismUnavailableException`.

The two token-bearing names are what switch an account onto access-token authentication. `XOAUTH2` and `OAUTHBEARER` carry a bearer token rather than a password, so an account permitting only those configures no password at all and configures an `OAuth` block instead; startup settles which credential the account needs from the permitted mechanisms rather than from which blocks happen to be present, and refuses either shape without the other. Which of the two is used is read from the server's advertised set, preferring the registered `OAUTHBEARER` where both are offered. The channel rules above are unchanged by any of this: a bearer token is not a reason to relax connection security, and the clear-text `LOGIN` fallback stays a password path that no token can travel through. [Mailbox OAuth](../operations/mailbox-oauth.md) covers where the token comes from and what each provider requires.

These settings decide what MailFathom will accept, and they apply only to a handshake the platform is prepared to complete at all. The system OpenSSL refuses a cipher suite, key size, or protocol version below its own security policy before any of the above is consulted, and a server it refuses fails as an `AuthenticationException` wrapping an OpenSSL handshake error — which reads as a credential problem and is not one. [The platform TLS policy](../operations/platform-tls-policy.md) is where that failure and its one supported remedy are documented; nothing in this section can influence it.

Certificate validation is always enabled and no configuration path disables it. A private or self-signed server is supported by setting `CertificateTrust` to `AdditionalTrustedAuthority` and naming the deployment-provisioned material in the `TrustedCertificateAuthority` secret block. `SystemTrustStore` rejects a configured anchor, and `AdditionalTrustedAuthority` requires one. A block present with a blank `SecretReference` reads as no anchor at all, so `"TrustedCertificateAuthority": {}` fails the rule that requires one rather than passing it and failing later with a confusing missing-material error.

#### Trust anchor material

The block resolves like any other secret, and the bytes behind it are loaded as a certificate. Three encodings occur in deployment and all three load, recognized from the material rather than declared in configuration:

| Encoding | Where it comes from | Inline |
| --- | --- | --- |
| PEM | What a certificate authority hands an operator. | Yes |
| DER | What some tooling emits. Binary. | No |
| PKCS#12 / PFX | A bundle, optionally protected by a password. Binary. | No |

A protected bundle takes its password from the nested `Password` block, which is itself an ordinary secret block, so a bundle password is validated, resolved, and erased by exactly the machinery every other secret uses. An unprotected bundle is still a valid file an operator is entitled to use and loads without one.

```json
{
  "TrustedCertificateAuthority": {
    "Name": "primary-private-ca",
    "SecretReference": "systemd-credential:private-ca-bundle",
    "Password": {
      "Name": "primary-private-ca-bundle-password",
      "SecretReference": "systemd-credential:private-ca-bundle-password"
    }
  }
}
```

The configured value decides only whether an anchor is *present*; whether it is usable is the loader's question. A non-blank value is an anchor the operator supplied, whether or not it is a `<scheme>:<target>` reference, so the inline shape below passes the presence rule and then gets a named load failure if the material is wrong — rather than being reported as a missing anchor, which it is not. What crosses into the domain policy is never the raw value: a parsed reference crosses masked, and anything else as a fixed `***`.

Under `ReferenceOrInline` or `InlineOnly` the block may carry the PEM text directly, which is what makes an Azure App Configuration deployment work end to end: the store holds the certificate, the provider binds it, and MailFathom parses what it was given. A trust anchor is a public certificate, so writing one into configuration leaks nothing. Only PEM works that way — DER and PKCS#12 are binary and have no faithful representation in a configuration value, so an inline block carrying them fails startup with `InlineEncodingNotSupported`, naming the encoding rather than surfacing a parse error further down. PEM is multi-line, so a JSON document has to escape the newlines; a store-backed provider has no such problem, because the value is transported rather than authored in JSON.

Material is imported with ephemeral key storage, and an anchor that carries a private key is **rejected**. A trust anchor needs only a public certificate; a private key would sit outside the buffer whose lifetime the secret machinery controls, and with default key-storage flags the import could persist it to a key store on disk. Material that does not parse, or parses but is unusable, fails startup with a named failure and never with the material itself:

| Failure | Meaning |
| --- | --- |
| `MaterialMissing` | The block carries no reference at all. |
| `SecretNotResolvable` | The reference, or the bundle password's reference, produced no material. |
| `EncodingNotRecognized` | The material is neither PEM nor an ASN.1 certificate or bundle. |
| `InlineEncodingNotSupported` | Binary material was supplied as the configuration value itself. |
| `MaterialNotReadable` | The encoding is supported but the material does not parse. |
| `BundlePasswordMissing` | The bundle is protected and no nested `Password` block was configured. |
| `BundlePasswordIncorrect` | The bundle did not open with the configured password. |
| `BundleCarriesNoCertificate` | The bundle parsed but holds no certificate. |
| `TrustAnchorCarriesPrivateKey` | The certificate carries a private key. |

The platform reports a wrong bundle password, a missing one, and corrupt bundle contents identically, so the last two are told apart by what was configured rather than by what the platform said. It is a diagnostic refinement that points at the part an operator controls, not a claim about the material. A loaded anchor is logged by subject and thumbprint, which is public information and the detail that confirms MailFathom trusts the authority the operator provisioned.

#### How the anchor is used

Trust is decided by rebuilding the chain against the configured anchor, never by accepting the error the platform reported:

- A name mismatch and an unavailable certificate are rejected outright. Neither has anything to do with which authority signed the certificate, and forgiving them would turn the private-authority path into the validation bypass this design exists to avoid.
- Only a chain-trust failure is re-examined, by building a chain that trusts the configured anchor as its sole root and requiring a clean rebuild. The rebuild re-applies the requirement that the certificate be usable for TLS server authentication, because a chain error also covers a usage rejection and the same private authority commonly issues client certificates too. It also refuses a chain the platform reported as revoked or explicitly distrusted, since neither verdict is about which authority signed the certificate and the rebuild checks no revocation of its own.
- Certificate downloads are disabled for the rebuild. The handshake already supplied every intermediate it is meant to use, and leaving them enabled would let an incomplete, server-chosen chain send a synchronous validation callback to a URL of the server's choosing with no caller cancellation reaching it.
- The certificates the server sent are reused as path-building candidates. A private server whose certificate is signed by an intermediate rather than directly by the configured root is an ordinary deployment, and that intermediate is often reachable only from the handshake. It completes a path; it gains no trust of its own.

**Revocation trade-off.** The rebuild does not check revocation. A private authority typically publishes neither a CRL distribution point nor an OCSP responder, so an online check would fail every connection to the server this feature exists to support, and a status-unknown result would have to be either ignored — which is what skipping the check states plainly — or treated as fatal. Compromise of a deployment-provisioned anchor is therefore handled by replacing the provisioned material, which rotation now makes possible without a restart. An account left on `SystemTrustStore` is unaffected: it keeps the mail client's own validation, revocation checking included.

The whole `MailSynchronization` section binds strictly (`ErrorOnUnknownConfiguration`), so a misspelled key fails startup instead of being ignored. Without that, a singular `PermittedAuthenticationMechanism` would be dropped silently and the default allow-list would take its place, quietly permitting mechanisms the operator meant to exclude.

Every rule above is enforced twice: in the domain policy object and again during `ValidateOnStart` options validation. A connection-security mode or certificate-trust source bound from a raw number that names no member is reported as a violation rather than slipping past the rules it cannot be evaluated against. `Host` binds the section and turns each reported configuration error into a startup failure that names the account and the violated rule and never includes the user name, password, or the trust anchor reference.

Each reported error carries the domain's `MailTransportSecurityViolation` alongside its operator sentence, and the startup message appends that identity in brackets — for example `Account 'primary': An unencrypted connection requires AllowInsecureConnection. [UnencryptedConnectionRequiresExplicitOptIn]`. The bracketed name is the stable half: an operator or log query can match on it while the surrounding prose stays free to change. An unsupported SASL mechanism name carries no violation and is reported without brackets, because it is a parse failure rather than a violated rule.

Secret resolution is the one rule that cannot join `ValidateOnStart`, because options validation is synchronous while resolution is not — the contract is asynchronous so a network-backed secret store needs no breaking change later. It runs instead in the host's starting phase, which completes before any hosted service starts, so no run ever starts against an unresolvable secret.

An account may carry a `Delivery` block beside all of this, naming where its mail would be submitted. That is a second server rather than a second way of reaching this one, and it is judged by every rule above: the permitted mechanisms, both weakenings, and the certificate authority are this section's and are read for both endpoints, while the connection-security mode is the submission endpoint's own, because a provider serving implicit TLS for reading and STARTTLS for submission is the ordinary case. [Mail delivery](mail-delivery.md) states what the block configures and what a session opened against it establishes.

## Safety assumptions

The application layer exposes only `FetchEmailContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\Seen` flags are not changed. The MailKit adapter satisfies both halves — it selects the folder with `FolderAccess.ReadOnly` and retrieves content through `GetStreamAsync(uid)`, which issues `UID FETCH <uid> (BODY.PEEK[])`. Regression tests exercise both a successful fetch and a fetch retried after a dropped connection, and assert that neither `StoreAsync`, the only `IMailFolder` member able to change flags, nor a read-write reselection was requested on either path. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxMetadataBatchesPerRun`, empty unassigned UID ranges are not checkpointed speculatively, and raw MIME above `MaxRawMimeBytes` is recorded as metadata-only. Metadata extraction reads only the payload that fetch already produced and never materializes attachment content: each attachment's size is measured by decoding the part into a stream that counts what it is written and keeps none of it, which is the same measurement a read of mail performs. Body text extraction reads the same already-fetched payload and opens no attachment payload at all, and the backfill reads only raw MIME an earlier run stored, so neither can reach a mail server. Logs record counts and account/folder identifiers only; raw MIME, email bodies, extracted and indexed text, attachments, participant addresses, subjects, attachment file names, credentials, and tokens remain sensitive and must not be logged, and no error message carries a fragment of them.

### Reloading a rotated reference

Every consumer reads a published snapshot rather than the raw bound options. A configuration reload produces a candidate, and the candidate becomes the published snapshot only after every secret reference in it resolves and every configured trust anchor loads. A candidate that fails is discarded with a log line naming the configuration path and the failure identity, and the previous snapshot stays active — a mistyped credential name does not take a running deployment offline.

Validation never runs on the thread that reported the reload. It is handed to a single background reader through a channel that keeps only the newest candidate, so a burst of reloads costs one validation rather than a queue of stale ones, and an older candidate can never overwrite a newer one that already published. A reload that fails unexpectedly is logged and dropped; it never terminates the process.

Snapshots are read once per operation, and one operation means one snapshot end to end. A supervisor takes its account, that account's folders, and the bounds a run obeys when the run begins, and hands that same snapshot down to each folder's scope, so a folder scheduled from one account list can never connect with another's endpoint, policy, and credentials. Each work unit's scope therefore holds one snapshot — the transport security policy it validates against and the material it connects with therefore always come from the same reload, which two independent reads of the published snapshot could not guarantee. Whether synchronization runs at all, how often the account set is re-read, how many accounts may run at once, and how long shutdown drains are read once at start, because all four shape the coordinator loop itself rather than the work one run does.

The database secrets reload on the same terms. `Persistence:Password` and `Persistence:ConnectionString` are read from their own published snapshot each time a physical connection needs a credential, so repointing a reference takes effect without a restart, and a reload whose reference does not resolve is rejected with the previous one left active. Two further checks run before that snapshot publishes, because resolving a reference proves less for a connection string than for a password: the material must parse as a PostgreSQL connection string and, when it is what supplies the credential, still carry one. Changing *which* setting supplies the credential is refused outright — the pool attaches its password provider once, so that change is restart-required and is reported as such instead of being adopted with no effect.

`Persistence:TextSearchConfiguration` is refused on the same terms and for the same kind of reason. It is compiled into the search vector's generated column, so the schema already holds the value the model was built from and every indexed row was written under it. Publishing a different one would change nothing about the index and everything about what an operator believed it contained: queries would be stemmed one way and the stored lexemes another, which surfaces as missing results rather than as an error. Changing it is a schema change that rebuilds the search documents, so it is restart-required and reported as such.

That guard covers a reload, and only a reload. Across a **restart** the newly configured value is what the model is built from, while the generated column PostgreSQL already holds still carries the previous one. The startup gate is what catches that: it reads the configuration out of the column's stored definition in PostgreSQL's own catalogue and refuses to start when it differs from the configured one, naming both. Changing this setting therefore remains an operator action paired with recreating the search documents, but a deployment that skipped the second half fails at startup instead of returning fewer search results than it should. The integration suite proves the reading half against a real schema, composing a host with a different configuration from the one the baseline migration applied and asserting the applied one is what gets reported.

A rejected reload is logged with the configuration path and the failure identity. When a credential provider fails in a way no failure identity covers, only the exception's type is logged and its message and stack trace are deliberately withheld, because a provider exception routinely carries the target path, request URI, or credential identifier that the reload contract keeps out of diagnostics.

## Pending work

- Per-account discovery. Every folder currently resolves on its own short-lived connection, so a run costs one extra
  IMAP login per configured folder on top of its synchronization session. The listing is the same for every folder of
  an account, and the per-account supervisor a run now belongs to is where one listing can serve them all.
- Watching a folder the account does not synchronize. A subscription names the folders a run resolved, so a change in a
  folder nothing is configured for is not reported and would not start a pass if it were.
- A durable audit store for mapping changes. The log-backed sink cannot join the transaction that commits a binding,
  so a sink failure loses the record of a change that already happened.
- Adapters for external managed secret stores. Kubernetes and container deployments need none, because their secrets are files.
- Explicit EF Core migrations after schema review.
- A retention grace period for a message the server no longer holds, and the bounded garbage collection that would act
  on it. Erasing a local copy happens as the disappearance is observed, and a tombstone keeps its content
  indefinitely; [#170](https://github.com/Krzysztof318/MailFathom/issues/170) owns both, together with clearing the
  tombstones an account accumulated before it switched to erasing.
- Chaos-tested resilience pipelines. The composition has unit coverage; what is missing is proof that an adapter survives a
  dependency misbehaving, which belongs with the adapters now that they are under integration coverage.
- RAG indexing and SMTP outbox integration. The MCP protocol surface has landed, with `list_emails` and the conventions
  every later tool follows; [MCP tools](mcp-tools.md) describes it.
