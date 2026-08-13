# Telemetry and the Aspire dashboard

<!-- describes: src/Application/Observability/**, src/Common/Observability/**, src/Host/Observability/**, src/Host/ServiceDefaultsExtensions.cs, src/Host/Hosting/Workers/MailExtractionBackfillWorker.cs, src/Infrastructure/Observability/**, src/Infrastructure/Mail/MailKit/MailKitImapClientFactory.cs, src/Infrastructure/HostApplicationBuilderExtensions.cs, src/AppHost/** -->

The host instruments itself with OpenTelemetry throughout — logs, metrics, and traces — and exports none of it unless
the environment names a destination. Today exactly one environment does that out of the box: a local run under the
Aspire orchestration, whose dashboard is the destination. This page records what is emitted, the one switch that
decides whether it leaves the process, and why the deployments deliberately ship with that switch off.

## What the process emits

**Logs** go through the OpenTelemetry logging provider with formatted messages, beside the console output that is
always on. Scopes are left out of what it exports, which is a redaction decision rather than a preference: the only
scope anything opens here is the one ASP.NET Core puts around every request, it carries the path exactly as that
request arrived, and one route on this host serves a path that is itself a credential. Correlation does not depend on
it — every record carries the trace and span identifiers of the request that produced it, and the span carries the path
with the capability already removed. Log lines are structured with named properties, and by contract they never carry
credentials, tokens, message bodies, attachment content, or raw MIME; a tool call is recorded as a name, an outcome,
and a duration, never as what was searched for. [What the endpoint records](mcp-endpoint.md#what-the-endpoint-records)
states that boundary precisely.

**Metrics** cover the request pipeline (ASP.NET Core), outbound HTTP (`HttpClient`), and the .NET runtime through their
instrumentation packages, and four meters that the libraries publishing them name themselves:

| Meter | What it reports | Subscribed by |
| --- | --- | --- |
| `Npgsql` | Connection-pool state, command durations, and command counts against PostgreSQL | The Aspire PostgreSQL enrichment |
| `Microsoft.EntityFrameworkCore` | Active contexts, queries, save operations, compiled-query cache hits and misses, execution-strategy failures, and optimistic-concurrency failures | The host |
| `Experimental.ModelContextProtocol` | MCP session duration, and per-operation duration broken down by protocol method and — for a tool call — tool name | The host |
| `Polly` | Every outbound-resilience pipeline's attempts, outcomes, timeouts, and circuit-breaker state transitions | The host |

The split in the last column is where a meter is registered, not how important it is: the Aspire enrichment that gives
the EF Core context its health check and its database tracing subscribes `Npgsql` as part of the same call, and the
host subscribes the three it leaves out. Nothing is subscribed twice.
[Outbound resilience](../architecture/outbound-resilience.md#telemetry-and-privacy) records which tags the `Polly`
events carry and which they never do; the optimistic-concurrency counter is the aggregate view of the same conflicts
that surface individually as a persistence conflict failure.

**Traces** cover incoming requests, outbound HTTP, database commands, and MCP protocol operations, correlated end to
end: the trace a request arrives with is the trace its log records and its failure diagnostics carry. The MCP spans
come from the SDK's own `Experimental.ModelContextProtocol` activity source and carry the protocol method, the
negotiated protocol version, the transport, the session identifier, the JSON-RPC request identifier, and the tool name
for a tool call — which is what makes a slow call attributable to a tool before anything inside the tool is
instrumented. Database commands are spanned by the `Npgsql` source rather than by EF Core, which reports through
`DiagnosticSource` and would need a bridging package to span the same commands a second time. Between those two ends
sit MailFathom's own spans, which say which use case ran; [what a request-path trace contains](#what-a-request-path-trace-contains)
is where they are named.

One filter is deliberate: requests to the health-probe paths are not traced at all, because a probe arrives every few
seconds for the life of the process and says the same thing every time — tracing it would fill a trace store with
polling instead of work.

One attribute is deliberately rewritten. An [attachment download](mcp-endpoint.md#the-one-route-on-this-surface-that-admits-no-credential)
carries a signed capability in its path, and whoever holds it can fetch that file until it expires, so the span records
the route template `/attachments/{capability}` in place of the path the request arrived with. The span itself is kept,
because a download is real traffic an operator has to be able to see; what is removed is the one segment that is a
secret. Nothing else in the pipeline writes it down: no log line here mentions a download, the exported log records
carry no request scope, and the framework's own request logging is off at the shipped `Microsoft.AspNetCore` level of
`Warning`. Two settings put the path back — that log level, and `Logging:Console:FormatterOptions:IncludeScopes`, which
turns scopes on for the console output rather than for the exporter — and
[the MCP endpoint](mcp-endpoint.md#the-one-route-on-this-surface-that-admits-no-credential) states what each one costs.

Every tag on the metrics above is a bounded set — a protocol method, a transport kind, a negotiated version, one of the
three tool names, an outcome — so none of them opens a time series per message or per person. The MCP SDK does tag a
metric with a resource URI, but only for the protocol's resource methods, and MailFathom's server publishes tools
alone: no resources and no prompts, so the tag never arises.

## The build every record names

Every record the process exports carries the build on its resource — a log record, a metric point, and a span alike —
read from the host assembly's own build-time metadata. That is what makes a deployment serving two versions at once
readable: a regression is grouped by the build it appeared in rather than inferred from the moment it started, and a
collector's history stays attributable to a build long after the rollout it belonged to.

It is two attributes, because the build answers two questions:

| Attribute | What it says |
| --- | --- |
| `service.version` | The semantic version, which is the compatibility statement an operator groups deployments by |
| `vcs.ref.head.revision` | The commit the assemblies were built from, which is what makes a report reproducible |

`service.version` carries the prerelease identifier where the build has one and never the revision, which the stamp
keeps after SemVer's plus sign. So a release reports the plain three-part version and a nightly reports
`<version>-nightly.<run number>-<short revision>`, the identifier that image's tag carries, because the two are stamped
differently at build time rather than told apart at run time. The process holds no notion of the channel it was
published on, and needs none.

`vcs.ref.head.revision` is the name OpenTelemetry's attribute registry publishes for a head revision. The `service.*`
namespace has none for build provenance, and an attribute invented here would be one no backend recognizes. It is
abbreviated to the same seven characters the identifiers above carry, so a value can be pasted into `git show` as it
stands; the whole object name is on the image's `org.opencontainers.image.revision` label for anything that needs it. It
reads `unknown` where the build stamped no revision, which is what a build with no repository beside it produces — an
image built outside the publishing pipeline is the case that occurs — and is a legitimate state rather than a fault.

[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
holds the whole scheme, including which inputs each build shape supplies.

Both values are the assembly's stamp rather than configured ones, so a deployment cannot make its telemetry claim a
build the process is not running — and neither attribute written into `OTEL_RESOURCE_ATTRIBUTES` overrides the stamped
one. That precedence is deliberate rather than incidental: the build is a fact about the running process, and the
variable is the one thing that could make it wrong. They are the same two values the startup records report as
properties, and the version is the one the MCP surface reports to a client during `initialize`, from the same source, so
no two of them can disagree.

The rest of the resource is left to the OpenTelemetry SDK. `service.name` comes from `OTEL_SERVICE_NAME`, or from the
SDK's `unknown_service:{processName}` fallback where that is unset, and nothing names the service a second time;
[host startup telemetry](host-startup-telemetry.md) records why one process reporting under two identities is the
failure that arrangement avoids.

## What MailFathom publishes under its own name

Everything above arrives from a library. MailFathom publishes under a name of its own, and there is exactly one of
them: **`MailFathom`**. It serves as both an activity source and a meter — the two are separate registries to
OpenTelemetry and cannot collide, so spans and instruments go under one string rather than two that could drift apart.

One name is what an operator filters a dashboard on to see everything this process owns and nothing a library emits. No
subsystem has a name of its own, and none gets one until there is something a name is the right way to tell apart:
which subsystem a signal came from is already carried by the span or instrument name and by its tags, and a distinction
added there costs an operator nothing, while a second registration is one more thing to subscribe to before anything is
collected.

There is also one instance of each registry, held for the lifetime of the process, and a subsystem starts its spans and
creates its instruments on those rather than constructing its own. That is what makes a name invented for a feature
impossible rather than merely discouraged: there is no second source to give a different name to. Neither is disposed,
because disposing a shared source would silence every other publisher, so a type that reports through them implements
no disposal on their account.

What publishes to that name is documented with the subsystem that does it.

Every change MailFathom makes to a remote mailbox opens a span named after the mutation, and is counted along with how
long it took, broken down by the mutation, the account, the folder alias, and whether it succeeded. It is deliberately
**not** broken down by which IMAP commands carried the change — a relocation is one operation whether the server
offered RFC 6851 `MOVE` or the copy-flag-expunge sequence was used instead, and a dimension telling the two apart is
exactly what would make a missing server extension look like a different operation on a dashboard. Which path ran is in
the debug log.

That counter answers what happened; two gauges beside it answer what has not happened yet, which is the question an
operator opens a dashboard with. `mailfathom.mailbox.mutations.outstanding` reports how many changes an account has
asked a mail server for and not seen finished, and `mailfathom.mailbox.mutations.oldest_outstanding_age` reports in
seconds how long the oldest of them has been waiting. Both carry the account, the mutation, and a lifecycle of
`pending`, `converging`, or `dead-lettered`; a completed change is not on them, because it is already the counter's
success outcome and an age is meaningless for it. Each account's values are republished by its own convergence pass and
replaced whole, so a lifecycle that empties stops being reported instead of reporting its last non-zero value forever.
A `dead-lettered` count that stops falling is the reading worth alerting on: those are changes nothing will attempt
again, waiting for somebody to look.

One counter beside them answers a question about the record rather than about the change.
`mailfathom.mailbox.mutation.audit.refused_appends` counts the audit entries a finished mutation owed and the trail
could not be given, by mutation and account, alongside a warning naming the record. It is zero on a deployment that
keeps no trail, because nothing is owed there. On one that keeps a trail it is the reading worth alerting on outright:
the change was made and the history of it is missing, which is exactly the gap an audit cannot recover afterwards. It
exists because writing an entry may never fail the mutation that produced it — a trail that could roll back somebody's
mailbox would be worse than a reported hole — so swallowing the failure is only defensible while it is counted.

Synchronization publishes the byte volume it moves, which is what storage is sized from — counting messages says
nothing about it, since one message is anywhere between a kilobyte and the configured size limit.
`mailfathom.mail.content.fetched` and `mailfathom.mail.content.stored` count the raw MIME bytes each folder run read
from its mail server and wrote to local storage, by account and folder alias, and are read as a rate: how much a mailbox
costs per interval. `mailfathom.mail.content.stored_total` is the level that rate is filling — how much storage the
stored content occupies, as the most recent run measured it. It carries no account or folder dimension on purpose,
because content storage is one store every account writes into and a per-account copy of the same number would invite a
dashboard to sum it.

`mailfathom.mail.content.limits_reached` counts the folder runs that ended against one of the two byte limits, tagged
with which: `run_budget` for a run that spent what it may fetch, and `storage_ceiling` for one that had to record
messages without their content. Both are counted rather than only logged because both are conditions that persist — a
run that stopped for its budget will stop again next interval, and a deployment at its ceiling stays there until
somebody acts — so a rising count says it has been running that way rather than that it did once.
[Bounding how much mail a run brings in](../features/imap-synchronization.md#bounding-how-much-mail-a-run-brings-in)
states what each limit does when it is reached and how the gap a ceiling leaves is closed.

Embedding publishes the depth of its backlog, how many messages the bound turned away, and how many messages and
passages it embedded and how long that took, broken down by outcome and by the classification of a provider failure.
[Automatic embedding](../features/automatic-embedding.md#what-an-operator-can-see) names each instrument and what it
answers; the depth is the one an instance falling behind shows up in first.

Three counters beside those answer what embedding is costing rather than how it is going.
`mailfathom.embedding.budget.consumed` is the characters sent to a provider and charged against the spend ceiling,
which summed over a period is what that period spent and summed over any other window is a question a ceiling cannot
answer; `mailfathom.embedding.input.truncated` and `mailfathom.embedding.input.omitted` are how many messages the
per-message ceiling cut short and how much text it left out. The consumed figure is a counter rather than a gauge of
what is left, because a remaining figure would have to be read from the database inside a callback a meter invokes on
its own schedule. Two instruments carry the truncation rather than one, because one enormous message and a thousand
slightly oversized ones need different answers from an operator: how often the ceiling binds, and what raising it would
cost. [Embedding generation](../features/embedding-generation.md#what-an-instance-is-willing-to-spend) records what each
ceiling bounds.

The backfill over mail stored before a profile existed publishes its own family beside that one, under
`mailfathom.embedding.backfill.*`: how many messages awaited embedding when the current sweep began, how each bounded
run ended, and how many messages it cut into passages, brought up to date, and gave vectors to. The instruments are
separate and the tag keys are shared, because a rate an instance settles at and a finite amount of work an operator
started are different questions about one provider bill.
[Embedding backfill](../features/embedding-backfill.md#what-an-operator-can-see) names each of them, and says why the
outstanding figure is a sweep old rather than live.

A model change publishes two more, `mailfathom.embedding.generation.switches` and
`mailfathom.embedding.generation.removed`: the moment a generation being built became the one searches are answered
from, and the vectors of the one it replaced going away in bounded batches. Both are counters rather than a gauge over
which generation is current, because an identifier is a dimension of unbounded cardinality for a value the switch's own
log line already carries. [Changing the embedding model](embedding-profiles.md) is what those two are read against.

Spam classification publishes two counters, and they exist because what they describe leaves no other trace. Where
classification is switched on, a message it has not decided about yet is not chunked, not embedded, and not offered to
the rule set — so a deployment withholding everything and a deployment with nothing to do publish exactly the same
absence of embedding and rule activity. `mailfathom.spam.derived_work.admissions` is what separates them: one
measurement per decision, tagged with `mailfathom.spam.admission` as one of `admitted`, `withheld_as_junk`,
`awaiting_classification`, `released_as_unclassifiable`, or `released_after_waiting`. The last is the reading worth
alerting on, because a deployment releasing mail on the wait rather than on a verdict is one whose classification is not
running. `mailfathom.spam.derived_work.discarded` counts the passages removed when a junk verdict arrived after they had
already been cut, and publishes nothing where a verdict removed none. Both are counts, the tag is a closed set of
MailFathom's own names, and nothing about a message reaches either. [Junk is kept out of what a deployment derives from
mail](../features/spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail) holds what each answer
means and what the wait is.

Each outbound AI provider publishes what its last call established about it, as `mailfathom.ai.provider.health` tagged
with `mailfathom.ai.provider.role` — `embedding` or `chat`. The two roles carry one measurement each rather than one
combined figure, because an instance may hold a working embedding provider and a failing chat one and the two ask
different things of an operator. The value is the state's own number rather than its name, since an instrument's value
has to be one: `0` nothing called yet, `1` serving, `2` unavailable and worth waiting out, `3` failing for a reason a
credential or a declaration has to change. A role nothing has called publishes no measurement at all, so a flat line
always means a provider that is being watched rather than one nobody configured. [Chat
generation](../features/chat-generation.md#provider-health-is-tracked-per-provider) records what each state means and
why nothing probes a provider to produce one.

A change between two of those states is also written to the log — at `Warning` when a provider stops answering and at
`Information` when it answers again — carrying the role and the two states and nothing else. The gauge says what is true
now and the record says when it stopped being true, which is the question the state itself cannot answer because it
carries no age. Only a change is written: every provider call records a state, so a record per call would scale the log
with the mailbox instead of with what an operator would act on. A first call that succeeded is the one change that is
not written, because it restored nothing; a first call that failed is.

### What a request-path trace contains

A tool call arrives with a span from the request pipeline and leaves a set of database spans behind it, and neither of
those says which use case ran in between. Every read the MCP surface serves therefore opens a span of its own, named
after the use case rather than after the tool — the tool's own name is already on the SDK's span above it, and a second
entrypoint over the same use case is work of the same kind:

| Span | The read it reports |
| --- | --- |
| `read_account_directory` | Which accounts this deployment serves, and how current the local copy of each is |
| `list_mailbox_timeline` | One bounded page of the stored email timeline |
| `search_mailbox` | One window of a ranking over the stored emails |
| `read_email_content` | The stored content of the emails one call named |
| `answer_mail_question` | One answering run, described in full [below](#what-a-run-records) |

The nesting is what they are for. Each is started inside the protocol span, so it is that span's child, and the database
commands and content-store reads it issues are its children — which is what separates a tool call that was slow in the
use case from one that was slow in a query, and both from one that spent its time waiting on a scanner. A search made
inside an answering run is reported by that run's span instead of opening a second `search_mailbox` beside it, so the
same work is never counted twice.

| Tag | What it carries |
| --- | --- |
| `mailfathom.mailbox.read.results` | How many accounts, summaries, matches, or emails the read returned |
| `mailfathom.mailbox.read.outcome` | How it ended: `succeeded`, `cancelled`, or `failed` |

`cancelled` is deliberately not `failed`. A client that disconnects mid-read is ordinary traffic on this surface, and
counting it as a failure would make an impatient assistant read as a broken deployment. A read that returned nothing is
`succeeded` with a count of zero: matching nothing is a normal answer everywhere here, and the outcome describes the
read rather than what the mailbox held.

For `read_email_content` the count is the emails that were **served** rather than the emails that were named. The gap
between the two is the whole of what that number can say — a call naming ten identifiers and answering for one is a
caller working from a listing that has moved on.

Beneath a read, one span answers a question the database span cannot. **`read_stored_email_content`** is opened
wherever raw MIME is read out of local storage, and carries `mailfathom.mail.content.bytes` with the size of what came
back and `mailfathom.mail.content.found` with whether anything was there. Every other query this deployment issues
returns columns sized like a row; this one returns a whole message, so a command duration on its own cannot tell a
forty-megabyte message from a two-kilobyte one. An email whose content was never stored reports `found` as false and no
size, which is an answer rather than a failure.

One more span belongs to no request at all. The extraction backfill opens **`backfill_email_extraction`** once per
bounded pass, which is what tells work an interval caused apart from work a caller caused — without it the pass appears
as parentless database commands competing with the requests around them. It carries
`mailfathom.mail.extraction.backfill.extracted`, `…unreadable`, and `…missing_content` as the counts the pass reached,
`…remaining` as whether any stored email still awaits extraction, and `…outcome` as one of `succeeded`, `deferred`,
`failed`, or `interrupted`. `deferred` is a competing writer the pass could not resolve against and `interrupted` is
shutdown; neither is a failure, and the next interval resumes from the committed position in both cases.

Nothing on any of these spans is derived from a message. There is nowhere on them to put a query text, a filter value, a
cursor, a subject, an address, or a stored identity — the values are counts, sizes, and closed sets of MailFathom's own
words, which is a cardinality rule as much as a privacy one.

### Durable background work

The queue of durable background work publishes four instruments, all broken down by `mailfathom.job.type` — the job
type's own name, which is the same word the log line, the span, and the stored row use. Nothing else about a job reaches
any of them: not its identifier, not the idempotency key it was enqueued under, not the account it belongs to, and not
the reason recorded against a failure. The key is the one that would carry mail, because it is composed of folder
aliases and message occurrences; the reason is unbounded in the way a metric dimension may not be. Both are read from
the queue itself, through the commands [administering a deployment](../users/administering.md#background-work-that-stopped)
describes.

`mailfathom.jobs.attempts` counts every attempt at a job and `mailfathom.jobs.attempt.duration` records how long each
one took, both tagged with `mailfathom.job.outcome` as one of `succeeded`, `handler_failed`, `handler_missing`,
`timed_out`, `released_for_shutdown`, or `lease_lost`. The last two are counted like any other attempt on purpose: both
occupied a worker and neither is a failure of the work, so a rolling deployment reads as released attempts rather than
as an unexplained gap in the count.

`mailfathom.jobs.retries` counts the failed attempts the queue scheduled again, tagged with the outcome that failed. It
is a separate instrument rather than a tag on the attempts, because it answers the question that separates an instance
that is busy from one that is failing and trying again — a retry rate rising while the attempt rate holds steady is work
being repeated rather than work arriving.

`mailfathom.jobs.dead_letters` counts the jobs nothing will attempt again, tagged with the outcome that ended each one
and with `mailfathom.job.failure` as `transient` or `permanent`. **This is the one worth alerting on outright**: a dead
letter is claimed by nobody and delays nothing, so it is invisible everywhere else and stays where it is until an
operator acts. The classification says which act is right — `permanent` names something to fix before a retry could do
anything, and `transient` names a dependency that stayed broken for longer than the queue was willing to wait. It
carries that dimension and the other three instruments do not, which is why it is an instrument of its own rather than a
tag on the attempts.

`mailfathom.jobs.queue.depth` reports how many jobs of each type are waiting to be claimed, as the worker last measured
it. Waiting is the pending state alone — a job a worker holds is running, and what bounds that is `Jobs:MaxConcurrentJobs`
rather than the queue's depth — which is the same reading `Jobs:MaxQueueDepthPerType` is applied against, so the depth an
operator watches and the depth an enqueue is refused at are one number. Two things follow from that. It **saturates** at
`Jobs:MaxQueueDepthPerType`, because the count stops there so that measuring costs the same on a queue of a thousand and
on a queue of a million; a reading sitting at the bound is a queue already refusing work as backpressure. And it is
measured at most once per `Jobs:PollInterval` by the worker rather than read live, so an instance draining a backlog
pass after pass pays for it no more often than an idle one. A type whose queue emptied reports zero rather than its last
non-zero depth; a type nothing has ever measured is absent from the gauge entirely, so a flat zero always means measured
and empty.

An instance with `Jobs:Enabled` switched off, or one with no registered handler, publishes none of the four: its worker
does not start, so it neither runs work nor measures the queue. The depth of a queue that instance is not draining is
somebody else's replica to report.

### What a synchronization cycle emits

No instrumentation package exists for the mail library, so without what follows the part of MailFathom that spends the
most wall-clock time would publish nothing of its own at all. Two spans and eight instruments answer the two questions
an operator opens a dashboard with: is this account still synchronizing, and if it is slow, which part of it is.

One account's cycle opens **`synchronize_account`**, and each folder it works opens **`synchronize_folder`** beneath it.
That nesting is the whole point of the pair — a cycle whose duration doubled is attributable to the folder it doubled
in rather than to the account as a whole — and the records the same cycle already logs carry the trace and span
identifiers of whichever of the two they were written inside, so a count in a log line and the span it belongs to are
one thing.

| Tag | Where | What it carries |
| --- | --- | --- |
| `mailfathom.mail.account` | Both | MailFathom's own configured alias for the account |
| `mailfathom.mail.folder` | Folder | MailFathom's own configured alias for the folder |
| `mailfathom.mail.sync.outcome` | Both | How it ended: `succeeded`, `failed`, `interrupted`, and for a folder also `alias_unresolved` or `alias_ambiguous` |
| `mailfathom.mail.sync.failure` | Folder | What stopped it: `concurrency_conflict`, `mail_server_unavailable`, or `unexpected` |
| `mailfathom.mail.sync.folders`, `…folders.failed` | Cycle | How many folders the cycle scheduled, and how many did not complete |
| `mailfathom.mail.sync.stored`, `…skipped` | Folder | What the folder stored with its content, and what it recorded from the envelope alone |

`interrupted` is deliberately not `failed`. Shutdown is what produces it, and an account backed off for every restart
would be an account approached less often for having been stopped. An alias that named no single advertised folder is
kept apart for the same reason: it is a configuration mistake an edit remedies, so it is an outcome rather than a
failure, and it counts against nothing.

| Instrument | What it answers |
| --- | --- |
| `mailfathom.mail.sync.run.duration` | How long one account's cycle took, by account and outcome |
| `mailfathom.mail.sync.emails.stored` | Messages a folder run stored with their content, by account and folder |
| `mailfathom.mail.sync.emails.skipped` | Messages it recorded from their envelope alone because they exceeded the size limit |
| `mailfathom.mail.sync.failures` | Folder runs that did not complete, by what stopped them |
| `mailfathom.mail.sync.backoff` | How long each account waits before its next run, which is its configured interval until a run fails |
| `mailfathom.mail.sync.consecutive_failures` | How many of that account's runs failed in a row |
| `mailfathom.mail.sync.runs.queued` | Account cycles waiting for one of the slots that bound how many accounts run at once |
| `mailfathom.mail.sync.runs.active` | Account cycles holding one of those slots |

The counts are separate from the byte volume above rather than a second view of it: one message is anywhere between a
kilobyte and the size limit, so how much a mailbox costs and how much of it has arrived are different questions.

The wait and the failure count are published together because neither is readable alone — a wait says nothing about
health without the interval it is being compared against, and a failure count says nothing about when the server will
next be approached. Both are republished on every pass rather than only while an account is backing off, so an account
that recovered stops reporting the wait it was deferred by instead of holding it, and an account nothing supervises any
more stops reporting altogether. A rising `consecutive_failures` against a flat `runs.active` is the reading worth
alerting on: an account being deferred rather than run.

The queue depth is the wait for a slot, which is why the cycle's span opens only once the account holds one — a
duration that included the wait would report the accounts in front of it as this account's own work.
`runs.queued` standing at the account count while `runs.active` sits at the configured bound is a pipeline saturated by
that bound rather than an idle one, which is a distinction nothing else here makes.

Nothing published by any of this is derived from a message. The dimensions are the two configured aliases and closed
sets of MailFathom's own words, and the values are counts and durations — no UID, no message identifier, no address, no
subject, and no remote folder path, each of which would open a time series per message or per person. The mail library
itself reaches none of it either: every IMAP client this deployment opens is constructed without a protocol logger, so
the commands, responses, and payloads of a session are written nowhere for a log level or a setting to expose, and no
configuration key exists that could attach one.

### Answering spend

Answering a question sends mail to a model provider on demand, so what it costs is published while it is being spent
rather than at the end of a billing period. `mailfathom.answering.runs` counts the questions this deployment was asked
to answer, tagged with `mailfathom.answering.outcome` — `admitted` or `refused` — so a ceiling that is met constantly
reads as a ceiling to raise or a client to look at rather than as an absence of questions. `mailfathom.answering.tokens`
counts what the provider reported those runs consuming.

Beside them, `mailfathom.answering.period.runs` and `mailfathom.answering.period.tokens` report how much of the current
period is already spent. The counter and the gauges answer opposite questions — how often the ceiling was reached, and
how close the deployment is to reaching it now — and neither is visible from the other.

An endpoint that reports no usage advances neither token figure, which is why the run and period ceilings exist in a
call-count form as well. [Mail answering § What one question may
spend](../features/mail-answering.md#what-one-question-may-spend) holds the ceilings these are read against, and
[`MailAnswering`](configuration-reference.md#mailanswering) the keys.

### What a run records

Those instruments describe what answering costs across a deployment. One span describes a single run, and it is the only
place a slow or degraded question is attributable to itself. Every run opens `answer_mail_question` on the `MailFathom`
activity source, inside the MCP tool call it happened in — so the SDK's own span for the call is its parent, the
provider calls the run makes are its children, and its duration is the run's own.

| Tag | What it carries |
| --- | --- |
| `mailfathom.answering.endpoint` | The alias of the chat endpoint the run was conducted through |
| `mailfathom.answering.instructions_version` | The version of the instruction the run was conducted under |
| `mailfathom.answering.candidates` | How many candidates the run's lookups ranked, before any relevance filtering |
| `mailfathom.answering.candidates.relevant` | How many of those survived being judged, which equals the figure above where nothing judged them |
| `mailfathom.answering.passages` | How many passages reached the model |
| `mailfathom.answering.outcome` | How the run ended: `Answered`, `AnswerEmpty`, `ProviderFailed`, `RunBudgetExhausted`, `Cancelled`, or `Failed` |
| `mailfathom.answering.degradation` | `None`, `RetrievalCeilingReached`, `RelevanceFilterFellBack`, or both |

Three counts rather than one, because they narrow for three different reasons and only the gaps between them say
anything: what the queries resembled, what the second pass decided actually answered, and what the run's own ceiling on
retrieved mail then allowed to leave the process. A dashboard showing only the last cannot tell a question that found
little from one that was stopped from sending much.

The degradation is one bounded tag rather than a log line, which is what lets a degraded run be counted and alerted on
rather than read about. It is a set because the two genuinely compose, and it is separate from the outcome because
failing is an ending while degrading is what a run that reached an ending did on the way.

A run that failed publishes exactly as one that answered. That is deliberate: the run that ended badly is the one an
operator most needs attributed, and a report built only from a successful answer would be silent about it.

Nothing here republishes consumed budget; the instruments above own that, and a second one for it would be duplication.
Nothing here carries a question, an answer, a query the model wrote, a retrieved passage, or a message identifier
either — which is a cardinality rule as much as a privacy one, and is why *which* messages a run read is kept in a
durable record instead. [Mail answering § An account can keep a record of what a question
read](../features/mail-answering.md#an-account-can-keep-a-record-of-what-a-question-read-and-none-does-by-default)
describes that record and why the split falls where it does.

One counter beside the span answers a question about the record rather than about the run.
`mailfathom.answering.audit.refused_appends` counts the entries a finished run owed and the record could not be given,
by endpoint alias, alongside a warning naming the run. It is zero on a deployment where no account keeps a record,
because nothing is owed there. On one that does it is the reading worth alerting on outright: the question was answered
and the history of what it read is missing, which is exactly the gap an audit cannot recover afterwards. It exists
because writing an entry may never fail the answer it describes, so swallowing the failure is only defensible while it
is counted.

### What guarding an egress point publishes

Sensitive-content scanning publishes five instruments, all of them tagged with
`mailfathom.sensitive_content.egress_point` — `chat_prompt`, `hosted_embedding_input`, `mcp_snippet`, or
`mcp_email_content`. The egress point is on every one of them because it is what an operator acts on: "something was
redacted" says nothing, while a scanner finding credentials in retrieved extracts and nothing in subjects, or adding two
seconds to a listing and nothing to an embedding call, is where a category list or a bound gets changed. It is also how
the cost of scanning a whole message is read: `mcp_email_content` is the point a reader waits on, kept apart from the
snippet scanning that would otherwise average it away.

**Every one of these series counts a guarded value rather than a guarded operation**, because a value is what a scan
runs over. One `get_email_content` call scans each body representation, the subject, and each display name it publishes,
for every email it names, so what the caller waits for is the sum of the durations that call recorded rather than any
one of them — read the count beside the duration before sizing the feature or alerting on a percentile.

| Instrument | What it answers |
| --- | --- |
| `mailfathom.sensitive_content.guarded` | How many texts were scanned before they crossed out of the deployment |
| `mailfathom.sensitive_content.findings` | How many detections were replaced, split by `mailfathom.sensitive_content.category` |
| `mailfathom.sensitive_content.omitted` | How many characters the analyzed ceiling dropped rather than hand on unscanned |
| `mailfathom.sensitive_content.refusals` | How many operations a scanner that could not answer refused, by `mailfathom.sensitive_content.scanner` |
| `mailfathom.sensitive_content.scan.duration` | What scanning added to one guarded operation |

The findings are split by category rather than totalled because which kind of material a mailbox is producing is what
decides whether a category list is right, and a total says only that the feature is switched on. The omitted count is
recorded only when the ceiling actually cut something: a zero on every guarded text would make the series say the
ceiling is in play on ordinary mail, which is the one question that instrument exists to answer. All five read zero on a
deployment with both switches off, because nothing is constructed there.

Nothing published here is mail or derived from it. The three tags are MailFathom's own closed sets, and the values are
counts and durations — never a rule's match, a position, a message identity, or any part of what was found, each of
which would put the credential in the telemetry written to prove it never left.
[Sensitive-content scanning](../features/sensitive-content-scanning.md#the-guarded-egress-points) names the points
themselves and what a refusal does to each.

### What redacting a derived write publishes

The derived path publishes five instruments of its own, under `mailfathom.sensitive_content.derivation.*`. They carry
no egress point, because a derived write crosses nothing — the text is redacted on its way into this deployment's own
store — so what separates these series from the guarded ones is their names.

| Instrument | What it answers |
| --- | --- |
| `mailfathom.sensitive_content.derivation.redacted` | How many texts were scanned before they were stored, chunked, and embedded |
| `mailfathom.sensitive_content.derivation.findings` | How many detections were replaced in them, split by `mailfathom.sensitive_content.category` |
| `mailfathom.sensitive_content.derivation.omitted` | How many characters the analyzed ceiling dropped rather than store unscanned |
| `mailfathom.sensitive_content.derivation.refusals` | How many extractions a scanner that could not answer refused, by `mailfathom.sensitive_content.scanner` |
| `mailfathom.sensitive_content.derivation.duration` | What scanning added to deriving one message |

The duration is the measurement the feature is judged on: it is paid once per message rather than once per request, and
it lands on synchronization and on the extraction backfill, which is where a mailbox being indexed for the first time or
[re-derived after a switch](../features/sensitive-content-scanning.md#what-a-late-switch-does-and-what-it-costs-to-fix)
either keeps up or does not. The refusals are how much of a mailbox is being left underived, and every refused message
is retried on a later run rather than lost. All five read zero on a deployment with both switches off, which constructs
no detector on this path at all.

What such a signal may carry is bounded by the same rule that governs the log lines, and it is a cardinality rule as
much as a privacy one. Counts, sizes, durations, outcomes, error codes, and MailFathom's own configured account and
folder aliases are permitted. Mail content, an address, a subject, a remote folder path, a message identifier, a UID, a
search term, a credential, and model prompt or completion text are not — every one of them would open a time series per
message or per person, quite apart from putting personal data in a span store.

## The one switch: `OTEL_EXPORTER_OTLP_ENDPOINT`

The OTLP exporter is attached only when the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is non-empty. Unset,
the instruments still exist and nothing collects them: no telemetry leaves the process, and the console remains the
only place logs go. There is no MailFathom-specific telemetry key — the exporter reads the standard OpenTelemetry
variables itself:

| Variable | What it does |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | The OTLP destination; setting it is what attaches the exporter |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (the default) or `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers sent with every export, which is where a collector's credential travels |
| `OTEL_EXPORTER_OTLP_TIMEOUT` | The per-export timeout |
| `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | The resource identity the records carry, except the version and the revision [above](#the-build-every-record-names) |

The variable has to be an **environment variable**, not a configuration key. That is deliberate, and
[host startup telemetry](host-startup-telemetry.md) records why: the bootstrap pipeline that reports startup failures
is built before configuration exists, reads the same variable, and exports each record synchronously — so the decision
to export and the destination being exported to can never disagree between the two pipelines, and a start that fails
while configuration is loading is still reported to the same place as everything else.

Writing any `OTEL_*` name into `appsettings.json`, a provisioned configuration file, or a command-line argument fails
startup naming it, rather than leaving a deployment exporting to nowhere while its own file says otherwise;
[environment-only settings](configuration-reference.md#environment-only-settings) states that rule and the two other
families it covers.

## Local development: the Aspire dashboard

The AppHost orchestration is where the switch is flipped for you. When `src/AppHost` starts a project resource, Aspire
injects `OTEL_EXPORTER_OTLP_ENDPOINT` — together with the authentication header its dashboard expects — pointing at
the dashboard's own OTLP ingestion endpoint. The host needs no telemetry configuration at all:

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

The AppHost prints the dashboard address, including a one-time login link, as it starts. The dashboard then shows, per
resource: the console output, the structured logs with their named properties, the traces — an MCP request, the
database commands it issued, the outbound calls beside them — and every metric above, including the `Polly` meter's
resilience events and the Npgsql instruments.

Two properties keep this arrangement honest:

- **It is per run and in memory.** The dashboard retains nothing across restarts. Telemetry produced from a developer's
  own synchronized mail never lands in a store that outlives the session.
- **It is local.** The OTLP endpoint Aspire injects is a loopback address with a per-run key, so nothing is exported
  off the machine.

The startup records from the bootstrap pipeline arrive in the same dashboard, because Aspire sets the same variable
that pipeline reads — a host that fails while binding its options is therefore diagnosable from the dashboard's
structured logs, not only from the console.

[Running locally with Aspire](local-development.md#running-locally-with-aspire) covers the rest of the orchestration:
the resource start order, the PostgreSQL data volume, and the migration resource that applies the schema before the
host starts.

## Deployments export nothing by default

Neither the Compose deployment nor the Helm chart sets any `OTEL_*` variable. That is a privacy default, not a gap:
MailFathom's telemetry describes activity around personal mail — account aliases, folder aliases, tool-call rates,
failure codes — and even without content, that stream identifies people and habits. Where it flows is therefore a
decision the operator takes explicitly, never one a deployment asset takes for them.

To export from a deployment, set the standard variables on the MailFathom container or service — an OTLP collector
address, its credential in `OTEL_EXPORTER_OTLP_HEADERS` — and treat the destination as part of the deployment's trust
boundary: it stores what the log contract permits, which still includes MailFathom's own names for accounts and
folders, durations, and error codes. Content never enters telemetry, so a collector never holds mail — but a
collector outside your control is still the wrong place for a mailbox's activity pattern.

The console log needs none of this. A container's log driver, `journalctl` for a native service, and
`docker compose logs` all read the stream that is always written, and the startup records land there
[synchronously](host-startup-telemetry.md) whether or not any exporter is attached.

What that stream looks like is configuration rather than a fixed shape. `Logging:Console` selects the formatter —
`json` where something parses the lines, `systemd` where `journalctl` should read the level rather than print it as
text — and carries a level filter of its own, so a noisy container log can be quietened without changing what the
exporter sends to a collector. [The `Logging` section](configuration-reference.md#logging) states those keys, together
with the one asymmetry worth planning for: the startup records are written before that configuration exists and keep
the default format whatever it selects.
