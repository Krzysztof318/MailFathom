# Storage, keys, jobs, and logging

<!-- describes: backend/src/Host/Configuration/Provisioning/**, backend/src/Host/Configuration/Persistence/**, backend/src/Host/Configuration/DataEncryption/**, backend/src/Host/Configuration/Jobs/**, backend/src/Host/Configuration/DeploymentOptions.cs, backend/src/Infrastructure/Secrets/Resolution/SecretResolutionOptions.cs, backend/src/Infrastructure/Resilience/OutboundDependencyResilienceOptions.cs -->

Every key about the deployment itself rather than about the mail passing through it: where its configuration is read
from and how a secret-bearing value is interpreted, the database it writes to, the key ring that seals what it stores,
the address it publishes as its own, the background queue that runs its work, what it does when an outbound dependency
fails, and what it writes to its log. The tables read as
[the configuration reference](configuration-reference.md#how-to-read-the-tables) says they do, and that page is the map
to the rest of the sections.

## `ConfigurationSources`

Names JSON configuration provisioned outside the application — a mounted ConfigMap, a systemd drop-in.
[Configuration sources](configuration-sources.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConfigurationSources:Directory` | string | unset | Must exist when named | restart |
| `ConfigurationSources:File` | string | unset | Must exist when named | restart |

The *content* of files that existed at startup reloads; adding or removing a file is a restart.

## `Secrets`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Secrets:Interpretation` | enum | `ReferenceOnly` | `ReferenceOnly`, `ReferenceOrInline`, `InlineOnly` | restart |

Under the default, a plain-text value where a reference belongs fails startup instead of authenticating.
[Interpretation modes](secret-provisioning.md#interpretation-modes) records when the other two are appropriate;
development keeps `ReferenceOrInline` so `plaintext:` references stay convenient.

## `Persistence` and the connection string

Where the local copy lives. The connection settings travel through the validated snapshot, so repointing them reaches
the next physical connection without a restart; the remaining settings are read while the host composes itself.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConnectionStrings:mailfathom` | string | `Host=localhost;Database=mailfathom;Username=mailfathom` | Carries no password | reload (new connections) |
| `Persistence:ConnectionString` | secret block | unset | Replaces `ConnectionStrings:mailfathom` entirely when set | reload (new connections) |
| `Persistence:Password` | secret block | unset | A present block must carry a reference | reload (new connections); material per connection |
| `Persistence:MaximumConcurrencyCommitAttempts` | int | `2` | 1 – 10; counts the first attempt | restart |
| `Persistence:CommandTimeoutSeconds` | int | `30` | 1 – 600; bounds one command, not one unit of work | restart |
| `Persistence:TextSearchConfiguration` | string | `simple` | A stock PostgreSQL text search configuration (`simple`, `english`, `german`, …) | restart — **and it is part of the schema**: the value is compiled into the index, startup fails with `32003` on a mismatch, and changing it means regenerating the migration and rebuilding the search documents |

Repointing a reference or editing the connection string reloads; changing *which* setting supplies the credential —
moving a password out of the connection string into `Persistence:Password`, or back — is refused on reload and needs a
restart, because the connection pool attaches its password provider once.

## `ContentStorage`

Where the raw MIME of the messages this deployment stores next is written. A configuration root of its own rather than a
section of `Persistence`, because what it selects is whether message payloads are in the database at all: an instance
writing them into a bucket still runs every metadata row, every index, and every job through PostgreSQL.
[ADR 0017](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md)
records the whole decision.

An absent section is `Database`, which is what a deployment that has never heard of this setting is already running.
Selecting `ObjectStorage` composes the client, establishes its transport and its trust, has the readiness probe ask the
bucket on every scrape whether it is reachable, readable, and writable, and writes every payload from then on to that
bucket rather than into PostgreSQL.

**It decides only where the next write goes.** Every stored payload's own row names the store holding it, so turning the
setting on moves nothing already stored and turning it back off re-encodes nothing: mail written to the database stays
readable from the database, and mail written to a bucket stays readable from that bucket. The one thing an operator owes
that arrangement is the endpoint itself — a deployment holding mail in a bucket it no longer names reports unready until
the block comes back, which [health endpoints](health-endpoints.md) describes and [email
content](../features/email-content.md#where-a-payload-is-kept) reads off the schema. Carrying what is already stored
into the bucket is an operator's act rather than a setting; [moving stored content into the
bucket](moving-stored-content.md) is the operation, and `ContentStorage:Move` below is what bounds its cost.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ContentStorage:Backend` | enum | `Database` | `Database`, `ObjectStorage` | restart |

Selecting `ObjectStorage` makes the block below required, and startup then refuses a declaration missing an address, a
bucket, or either half of a credential. That refusal is the point rather than a formality: the S3 client's own
credential chain reaches environment variables, a shared credentials file, and an instance metadata service, so a
deployment that configured none must fail instead of quietly signing as whatever identity the host carries.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ContentStorage:ObjectStorage:Endpoint` | string | unset | Required; an absolute `https` address. A plain `http` address is refused, because a request carries a signature and, on a write, the message itself | restart |
| `ContentStorage:ObjectStorage:Bucket` | string | unset | Required | restart |
| `ContentStorage:ObjectStorage:KeyPrefix` | string | empty | Empty is a bucket MailFathom has to itself; whitespace is refused. Two deployments sharing one bucket need disjoint prefixes, and nothing here can check that | restart |
| `ContentStorage:ObjectStorage:Region` | string | `us-east-1` | The region a request is signed under. SigV4 carries one whether the endpoint has a notion of a region or not | restart |
| `ContentStorage:ObjectStorage:UsePathStyleAddressing` | bool | `true` | Off only for an endpoint that genuinely serves virtual-hosted buckets, which needs a wildcard DNS name and a certificate to match it | restart |
| `ContentStorage:ObjectStorage:ConnectTimeout` | TimeSpan | `00:00:10` | 1 s – 1 min | restart |
| `ContentStorage:ObjectStorage:RequestTimeout` | TimeSpan | `00:01:40` | 5 s – 10 min, and longer than `ConnectTimeout`. The transport's backstop rather than the operation's budget, which is `Resilience:ObjectStorageInvocation` | restart |
| `ContentStorage:ObjectStorage:AccessKeyId` | secret block | unset | Required; a present block must carry a reference | restart; material per request |
| `ContentStorage:ObjectStorage:SecretAccessKey` | secret block | unset | Required; a present block must carry a reference | restart; material per request |
| `ContentStorage:ObjectStorage:TrustAnchor` | secret block | unset | The certificate authority that signed the endpoint's certificate, for an endpoint the operator runs themselves. Absent for one the platform already trusts | restart |

**The access key identifier is a secret block like the secret beside it**, rather than a plain string. It names an
identity at the endpoint, it is one half of what an attacker needs, and every provider that issues one issues it
together with its secret from the same place. Both are resolved before every request, so a key rotated behind an
unchanged reference takes effect on the next call with nothing to invalidate.

The trust anchor is the one exception to that, and it is why the row says restart. The decision is a synchronous
callback inside a pooled TLS handler, so the authority is loaded once while the host starts; there is no setting
anywhere that turns validation off. [Platform TLS policy](platform-tls-policy.md) is the page.

**Reclamation is where mail whose record is gone stops being mail**, and both settings below are privacy-relevant
rather than housekeeping. Deleting a stored email, an outgoing record, a recurring declaration, or a draft removes the
object immediately after the transaction that removed its row commits, so in the ordinary case neither of these decides
anything. What they decide is the other case — a write that never committed, a superseded draft revision, an endpoint
that refused the removal — where the bytes go within one reclamation interval instead, and these two numbers are what
that bound is composed of. [An object nothing points at is
reclaimed](../features/email-content.md#an-object-nothing-points-at-is-reclaimed) states the whole promise, and
[telemetry](telemetry.md#reclaiming-content-objects) is where an operator sees whether it is being kept.

The sweep lists beneath `KeyPrefix` and nowhere else, so a bucket MailFathom shares with anything else is a bucket
whose other contents it cannot reach.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ContentStorage:ObjectStorage:Reclamation:Schedule` | string | `Every 06:00:00` | The occasions a sweep is dispatched on: `Every <hh:mm:ss>`, `Every <d.hh:mm:ss>`, or `Daily at <HH:mm> [<zone>]`, the same syntax a scheduled [mail rule](../features/mail-rules.md#running-a-rule-on-a-schedule) is written in. Longer intervals lengthen how long an orphaned payload lives | restart |
| `ContentStorage:ObjectStorage:Reclamation:MinimumObjectAge` | TimeSpan | `24:00:00` | 1 h – 30 d. Below the floor a sweep could meet an object whose unit of work has not committed yet, which is mail lost rather than reclaimed; above the ceiling the floor would be a retention decision written in the wrong place | restart |
| `ContentStorage:ObjectStorage:Reclamation:MaximumObjectsPerRun` | int | `100000` | At least 1000, the number of keys one listing request answers with. A run that reaches the ceiling hands its listing position to the run after it rather than starting over | restart |

### Moving stored content into the object backend

Selecting the backend leaves everything already stored where it is, so carrying it across is an operator's act through
the administrative endpoint rather than a setting. What these three settle is what that move costs the deployment
*while it runs*: one bounded pass per interval, ending on whichever of the two ceilings it reaches first, so most of
every interval is left for synchronization, delivery, and the reads a caller is waiting on.
[Moving stored content into the bucket](moving-stored-content.md) is the operation.

They are judged only where `ContentStorage:Backend` is `ObjectStorage`, because a deployment holding its content in the
database has nowhere to carry it to and runs no pass — a bound it declared for a backend it did not select must not be
what stops it from starting.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ContentStorage:Move:Interval` | TimeSpan | `00:00:10` | 1 s – 1 h. Below a second the move stops being background work and becomes a second workload beside the deployment's own | restart |
| `ContentStorage:Move:PayloadsPerPass` | int | `20` | Positive. A pass that carries no payload would leave the move running forever without moving anything | restart |
| `ContentStorage:Move:MaxBytesPerPass` | long | `67108864` | Positive, in bytes. A pass ends on whichever ceiling it reaches first, so a ceiling of nothing would end every pass before its first payload | restart |

A single payload larger than `MailSynchronization:MaxInFlightRawMimeBytes` is refused rather than carried, because the
move reads under the same process-wide budget synchronization reads under. It is counted, reported, and stepped past;
raising that ceiling and asking for another move is what reaches it.

## `DataEncryption`

The key ring every value MailFathom seals at rest is sealed under. A configuration root of its own rather than a
section of `Persistence`, because the database is the first thing sealed under it and there is no reason it is the
last. [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) records the whole decision, and
[secret provisioning](secret-provisioning.md) states how the material is generated and referenced.

An absent section is a valid deployment that seals nothing. Configuring the section makes every rule below apply.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `DataEncryption:ActiveKeyId` | string | unset | Must name one of `Keys`; required once any key is configured, and refused when none is | reload |
| `DataEncryption:Keys:<n>:KeyId` | string | — | Up to 64 letters, digits, dots, dashes, and underscores, beginning with a letter or a digit; unique within the ring | reload |
| `DataEncryption:Keys:<n>:Material` | secret block | — | Base64 decoding to exactly 32 bytes, generated with `openssl rand -base64 32` | reload; material per operation |

`KeyId` is stored beside every value the key seals, so it is chosen once and never edited — renaming it orphans every
value already carrying the previous spelling. The operator's own label for a key is its material's `Name`, which every
secret block requires; there is no second name on the entry.

The ring holds several keys so that rotation needs no downtime: move `ActiveKeyId` to the new key, leave the previous
key configured, and every value still carrying it keeps opening under it. Removing a key the database still references
makes those values unopenable, and the failure appears at the next read rather than at the edit.

## `Deployment`

What this installation is, rather than what any one surface it serves does. A root of its own for that reason: the
address clients reach this deployment at is not a property of the feature that first needed it, so an operator answers
it once and whatever else has to hand back an absolute address later reads the same key.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Deployment:PublicBaseAddress` | url | — | Absolute, `https` unless the host is loopback, no path, no query, no fragment | restart |
| `Deployment:ReadOnly` | bool | `false` | — | reload |

**It has no default on purpose.** Only an operator knows which name a client reaches this process by, and a guess would
produce addresses that resolve to nothing or, worse, to somebody else. Nothing derives it from a request either: an
address composed from a `Host` header would let whoever called a tool decide where the URL it receives points.

It carries no path because this process serves its routes at its root, and clear text is refused off this machine
because what is composed beneath it may be a capability — a secret in transit. Today the one consumer is the
[attachment download link](../features/email-content.md#what-a-download-link-is-and-what-bounds-it); a deployment that
declares no address issues none, which is a supported posture rather than a misconfiguration.

**`ReadOnly` is what a deployment holds rather than what it is currently configured to do.** In it MailFathom sends no
mail from any account, whatever an account's own
[`Delivery:Enabled`](configuration-mail.md#submission-endpoint--delivery) says and whoever asked — a tool call, a rule,
a command. The refusal happens where the outgoing record would be written, which is the one place every author passes
through, so nothing is queued and nothing waits for the mode to be turned off;
[mail delivery](../features/mail-delivery.md#what-a-deployment-must-turn-on-before-it-can-send) states what a caller is
told. What it reaches is sending, which is what leaves this installation for somebody else's mailbox; changes to a
mailbox this deployment reads are governed by the account's own
[rule action permissions](configuration-mail.md#one-account--mailsynchronizationaccountsn) and by the grant a caller holds.


## `Jobs`

The queue of durable background work, and the worker that runs it. A root of its own rather than a block inside any
feature, because the queue is a mechanism every consumer shares: what a job does belongs to the feature that enqueues
it, and how much of the instance the queue may take belongs here. Nothing here names a job type, and an instance whose
build registers no handler runs no pass at all — the worker says so once at startup and stops, which is what leaves work
an older replica cannot run for a newer one.

`MaxConcurrentJobs` decides how much of the instance background work may take, and it is stated here rather than left to
emerge from the database connection pool. A limit nobody wrote down moves whenever anything else in the process opens a
connection, and it arrives as a query waiting on a pool rather than as a job waiting for its turn. `BatchSize` is a
different number — what one claim takes — so a claimed job waits for a slot like any other, and raising the batch buys
fewer round trips rather than more work in flight.

`MaxConcurrentJobsPerType` bounds one kind of work on its own, and startup refuses a value above `MaxConcurrentJobs`,
which already caps it. A job waiting on the per-type ceiling holds none of the instance-wide one, so a bulk
re-evaluation of one kind of work is never the reason another kind never runs.

`MaxQueueDepthPerType` bounds what may be waiting rather than what is running. An enqueue against a queue already
holding that many jobs of a type is refused and says so, and the caller slows down, asks again later, or stops
producing — the work is neither queued nor lost, and a request whose work is already queued is answered with that job
rather than turned away. It is the one setting here that still applies with `Enabled` switched off, because it bounds
enqueuing rather than running. Two callers meeting the bound together can both pass it, so a queue may overshoot by as
many enqueuers as raced; this is backpressure rather than an invariant, and what it exists to stop is a backlog growing
without limit.

`ExecutionTimeout` must be shorter than `LeaseDuration`, and startup refuses a pair that inverts them. That ordering is
what keeps two workers off one job: an attempt is cancelled before its lease can expire underneath it. The lease is
renewed at half its duration while a handler works, so a job that legitimately takes longer than one lease is not
reclaimed while it runs.

A failed attempt is classified before the attempt budget is consulted, and only a failure that could clear on its own is
attempted again. A permanent one — a credential the dependency refused, a request it rejected, anything whose meaning is
unknown — ends the job on its first attempt rather than spending `MaxAttempts` to reach an answer it already had. What
runs out of attempts and what could never succeed both become dead letters: terminal rows nothing claims again, which
hold up no other job and keep the classification and the reason they ended on. A shutdown is neither, and spends no
attempt: the job goes straight back to the queue with the attempt it was claimed for given back.

No key decides what becomes of a dead letter, because an operator does. [`mfctl
jobs`](../users/administering.md#background-work-that-stopped) reads what has stopped and either returns one to the
queue or writes it off, and [durable background work](telemetry.md#durable-background-work) is what says one is there.

`RetryMaxDelay` must be at least `RetryBaseDelay`, and startup refuses a pair that inverts them. A retry delay doubles
per attempt from `RetryBaseDelay`, is capped at `RetryMaxDelay`, and is drawn from a range rather than computed exactly
— jobs that failed together failed on the same dependency, and an exact delay would return all of them to it in the same
instant.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Jobs:Enabled` | bool | `true` | turning it off leaves enqueued work where it is, for a replica that runs it | restart |
| `Jobs:BatchSize` | int | `5` | 1 – 100; how many jobs one pass claims. Each of them waits for a concurrency slot, so this bounds what one claim takes rather than what runs at once | restart |
| `Jobs:MaxConcurrentJobs` | int | `4` | 1 – 32; how many jobs this instance runs at once, across every type together. Kept well below the connection pool a stock connection string provides, so the pool is never what expresses the limit | restart |
| `Jobs:MaxConcurrentJobsPerType` | int | `2` | 1 – 32, and at most `Jobs:MaxConcurrentJobs`; how many jobs of one type run at once. A job waiting on this holds none of the instance-wide ceiling | restart |
| `Jobs:MaxQueueDepthPerType` | int | `10000` | 1 – 1000000; how many jobs of one type may be waiting before enqueuing is refused as backpressure. Applies whether or not `Jobs:Enabled` is on | restart |
| `Jobs:LeaseDuration` | TimeSpan | `00:05:00` | 2 s – 1 h; how long work stays held after the process running it stops existing, which is the delay before a crash is recovered from | restart |
| `Jobs:ExecutionTimeout` | TimeSpan | `00:02:00` | 1 s – 1 h, and strictly shorter than `Jobs:LeaseDuration`; exceeding it cancels the job, which counts as a transient failure and is attempted again. Raise it where this kind of work legitimately takes longer | restart |
| `Jobs:MaxAttempts` | int | `5` | 1 – 20; how many attempts one job may be handed out for before a transient failure dead-letters it. `1` leaves no retry at all. A permanent failure ends the job whatever this says | restart |
| `Jobs:RetryBaseDelay` | TimeSpan | `00:00:30` | 1 s – 1 h; the delay the first retry is drawn around, doubling per attempt | restart |
| `Jobs:RetryMaxDelay` | TimeSpan | `00:30:00` | 1 s – 24 h, and at least `Jobs:RetryBaseDelay`; the ceiling a grown retry delay never exceeds | restart |
| `Jobs:PollInterval` | TimeSpan | `00:00:10` | 1 s – 10 min; how long an idle worker waits before looking again, and how often at most it measures the queue depth it publishes and asks whether a rule's schedule has come due. A schedule is therefore noticed within one interval of its occasion rather than at it. A pass that filled its batch looks again at once | restart |


## `Resilience`

Retry, timeout, circuit-breaker, and concurrency budgets for the non-HTTP outbound dependencies, one subsection per
dependency class: `MailboxSessionEstablishment`, `MailboxDataRetrieval`, `MailAuthorizationServerInvocation`,
`EmailDelivery`, `DatabaseCommandExecution`, `AiProviderInvocation`, `ObjectStorageInvocation`. A subsection naming no
class fails startup. Every setting is **restart** by construction, and
[outbound resilience](../architecture/outbound-resilience.md#configuration) explains each strategy and the
per-class reasoning.

Settings, per class:

| Key | Type | Constraint |
| --- | --- | --- |
| `Resilience:<Class>:MaxAttempts` | int | 1 – 10; counts the first call, so `1` disables retry |
| `Resilience:<Class>:BaseDelay` / `MaxDelay` | TimeSpan | Jittered exponential backoff between attempts |
| `Resilience:<Class>:AttemptTimeout` / `TotalTimeout` | TimeSpan | One attempt / the whole operation |
| `Resilience:<Class>:CircuitBreakerFailureRatio` | double | 0.01 – 1.0 |
| `Resilience:<Class>:CircuitBreakerMinimumThroughput` | int | 2 – 1000 |
| `Resilience:<Class>:CircuitBreakerSamplingDuration` / `CircuitBreakerBreakDuration` | TimeSpan | — |
| `Resilience:<Class>:ConcurrencyLimit` | int | 1 – 1000 |

Defaults, per class:

| Class | Attempts | Base/max delay | Attempt/total timeout | Breaker ratio · min · sampling · break | Concurrency |
| --- | --- | --- | --- | --- | --- |
| `MailboxSessionEstablishment` | 3 | 2 s / 30 s | 30 s / 2 min | 0.5 · 5 · 60 s · 30 s | 4 |
| `MailboxDataRetrieval` | 3 | 1 s / 15 s | 60 s / 3 min | 0.5 · 10 · 30 s · 15 s | 8 |
| `MailAuthorizationServerInvocation` | 3 | 500 ms / 5 s | 10 s / 30 s | 0.5 · 10 · 60 s · 30 s | 8 |
| `EmailDelivery` | 2 | 5 s / 60 s | 60 s / 3 min | 0.5 · 5 · 60 s · 60 s | 4 |
| `DatabaseCommandExecution` | 3 | 200 ms / 2 s | 15 s / 30 s | 0.5 · 20 · 30 s · 5 s | 32 |
| `AiProviderInvocation` | 3 | 2 s / 30 s | 120 s / 5 min | 0.5 · 5 · 60 s · 30 s | 4 |
| `ObjectStorageInvocation` | 3 | 500 ms / 10 s | 30 s / 2 min | 0.5 · 10 · 60 s · 15 s | 16 |

## `Logging`

The standard .NET `Logging` section applies unchanged, and the host clears no provider: `Console`, `Debug`, and
`EventSource` stay attached beside the OpenTelemetry provider that the service defaults add. `Debug` writes only under
an attached debugger, and `EventSource` writes to the `Microsoft-Extensions-Logging` event source, which produces
nothing until something collects it — a `dotnet-trace` session, typically. So on a deployment the console is the
provider that produces output, and until `OTEL_EXPORTER_OTLP_ENDPOINT` names a collector it is where logs go at all,
which [telemetry](telemetry.md) records is the shipped default for both the Compose deployment and the chart. Log
lines are structured and never carry credentials, message content, or raw MIME, whatever the level or the format.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Logging:LogLevel:<category>` | enum | `Information`, and `Warning` for `Microsoft.AspNetCore` | A `LogLevel` name. `Default` is the catch-all; any other segment is a log-category prefix | reload |
| `Logging:Console:LogLevel:<category>` | enum | the `Logging:LogLevel` value | Filters the console alone, leaving what the OTLP exporter sends untouched | reload |
| `Logging:Console:FormatterName` | string | `simple` | `simple`, `systemd`, or `json` | reload |
| `Logging:Console:FormatterOptions:<name>` | mixed | — | `IncludeScopes`, `TimestampFormat`, and `UseUtcTimestamp` under any formatter; `SingleLine` and `ColorBehavior` under `simple` alone; `JsonWriterOptions` under `json` alone | reload |

An option the selected formatter does not define is accepted and does nothing — `SingleLine` under `json` is the one
worth naming, because it reads like it would fold a record onto one line and the JSON formatter already writes one
line per record without it.

`reload` here is the logging framework's own rather than a classification ADR 0002 made: a changed value is observed
by the next record written, without a restart and without reloading anything else. It is also why this section is
among the framework-shaped entries exempt from the strict binding
[every other section is bound with](configuration-reference.md#how-to-read-the-tables) — a key this table does not name
is the framework's to accept or to ignore, so a misspelling here leaves a default in force instead of failing startup
with the path.

**Executed SQL is a `Debug` record.** EF Core reports every command it runs through
`Microsoft.EntityFrameworkCore.Database.Command`, at `Information` in the library's own configuration. MailFathom logs
that one event at `Debug` instead, because a synchronization run, a backfill sweep, and every MCP read reach the
database repeatedly, and one record per round trip would leave the stream mostly SQL. What is lowered is the level of
the event rather than a filter over the category, so the records come back by asking for them — set
`Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Debug` for the commands alone, or `Default`
where a whole run is being read. A command that *fails* is untouched and stays in the default stream: only the
executed-command event is lowered, and every other EF Core event keeps the level the library gives it.

Select `json` where something parses the stream rather than reads it, and `systemd` where `journalctl` should read
the level rather than print it as text. Both are worth setting deliberately: `simple` is the default because it is
what a person reading `docker compose logs` wants, and it is the wrong shape for everything downstream of that.

**The startup records ignore every key above.** The host writes those four through a pipeline composed before
configuration exists, which attaches a console of its own at a fixed `Information` level, so a deployment that selects
`json` gets a stream whose `MailFathom.Host.Startup` records are still `simple` text — the `Critical` one explaining a
failed start included. Give a log shipper a path for those lines rather than assuming the stream is uniform;
[host startup telemetry](host-startup-telemetry.md) records why that pipeline cannot read this section.
