# Telemetry and the Aspire dashboard

<!-- describes: src/Application/Observability/**, src/Common/Observability/**, src/Host/Observability/**, src/Host/ServiceDefaultsExtensions.cs, src/Host/Hosting/Workers/**, src/Infrastructure/Observability/**, src/Infrastructure/Mail/MailKit/MailKitImapClientFactory.cs, src/Infrastructure/HostApplicationBuilderExtensions.cs, src/Mcp/Observability/**, src/Cli/Diagnostics/**, src/AppHost/**, src/AI/ProviderAdapters/OpenAiCompatibleClientFactory.cs -->

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
instrumentation packages, and five meters that the libraries publishing them name themselves:

| Meter | What it reports | Subscribed by |
| --- | --- | --- |
| `Npgsql` | Connection-pool state, command durations, and command counts against PostgreSQL | The Aspire PostgreSQL enrichment |
| `Microsoft.EntityFrameworkCore` | Active contexts, queries, save operations, compiled-query cache hits and misses, execution-strategy failures, and optimistic-concurrency failures | The host |
| `Experimental.ModelContextProtocol` | MCP session duration, and per-operation duration broken down by protocol method and — for a tool call — tool name | The host |
| `Polly` | Every outbound-resilience pipeline's attempts, outcomes, timeouts, and circuit-breaker state transitions | The host |
| `Experimental.Microsoft.Extensions.AI` | One provider call's duration and the tokens it consumed, broken down by operation, provider, model, and token type | The host |

The split in the last column is where a meter is registered, not how important it is: the Aspire enrichment that gives
the EF Core context its health check and its database tracing subscribes `Npgsql` as part of the same call, and the
host subscribes the four it leaves out. Nothing is subscribed twice.
[Outbound resilience](../architecture/outbound-resilience.md#telemetry-and-privacy) records which tags the `Polly`
events carry and which they never do; the optimistic-concurrency counter is the aggregate view of the same conflicts
that surface individually as a persistence conflict failure.

**Traces** cover incoming requests, outbound HTTP, database commands, MCP protocol operations, and calls to an AI
provider, correlated end to end: the trace a request arrives with is the trace its log records and its failure
diagnostics carry, and a model call appears inside the MCP request that caused it rather than beside it. The MCP spans
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
three tool names, an outcome, and on the AI instruments the operation, the provider, the requested and answered model
names, the configured endpoint's address and port, and the token type — so none of them opens a time series per message
or per person. The MCP SDK does tag a metric with a resource URI, but only for the protocol's resource methods, and
MailFathom's server publishes tools alone: no resources and no prompts, so the tag never arises.

### What a call to a model emits, and what it never does

A chat model and an embedding model are reached through one client construction, and every client it builds is wrapped
in the telemetry decorators `Microsoft.Extensions.AI` publishes. That is a property of the construction rather than of
any call site: an adapter added later reaches a provider the same way and is spanned without anything else being
written. The decorator sits innermost, beneath the resilience pipeline and the spend ceiling, so a span measures one
attempt against the provider — a call retried three times is three spans rather than one slow one, which is what makes
a provider's own latency separable from the retrying around it. Spans and instruments both arrive under
`Experimental.Microsoft.Extensions.AI`, which is the library's name for them and follows OpenTelemetry's semantic
conventions for generative AI, still marked experimental upstream.

**The spans are complete; the instruments are not, under sustained load.** A client is built per provider call, so each
call constructs the decorator anew — and the decorator creates a meter of its own with it. Every construction therefore
allocates two fresh metric streams, and the OpenTelemetry SDK caps a provider at 1000 of them; the cap is reached after
roughly 250 provider calls within one export interval, and the streams are reclaimed at the next export. Measured
against the pinned SDK: 400 calls in one interval produce 400 spans and 250 measured calls. An ordinary interval's worth
of questions is far below that and is measured in full, and an embedding backfill running at full concurrency is the
shape that exceeds it — which shows up as an undercounted call rate and an undercounted token total for that interval,
never as a missing span or a wrong duration on the calls that were recorded. Reading a backfill's cost from the provider's
own accounting rather than from this meter is the remedy while that holds.

**A prompt and a completion are never recorded.** The library will capture both — the question somebody asked of their
mailbox, the answer a model gave, the passages an embedding request carried — and it turns that capture on from the
environment variable `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`. MailFathom sets the switch explicitly where
it builds the client, and an explicit value takes precedence, so **that variable has no effect on this process**. There
is no configuration key for it either: a collector holding every question and answer would be a second copy of the mail,
under a retention nobody chose and an access rule the mail store never granted, and this is not a trade an operator is
offered. What remains is metadata — which operation ran, against which model and endpoint, how long it took, how many
tokens it consumed, and whether it failed.

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

Every change MailFathom makes to a remote mailbox opens a span named after the mutation, and is counted by
`mailfathom.mailbox.mutations` and timed by `mailfathom.mailbox.mutation.duration`, both broken down by the mutation,
the account, the folder alias, and whether it succeeded. It is deliberately
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

Two spans carry that work rather than only counting it. **`embed_stored_email`** is opened around one message's turn
and **`backfill_email_embeddings`** around one bounded backfill pass, and both exist for the reason `run_job` does: the
work is caused by a queue or by an interval rather than by a request. What makes these two worth more than the counters
beside them is what sits underneath: a provider call, published by the AI decorators, and the commands that store the
vectors it produced. Without the span the most expensive thing this deployment does arrives in a trace store
attributable to nothing at all.

| Span | Tag | What it carries |
| --- | --- | --- |
| `embed_stored_email` | `mailfathom.embedding.outcome`, `…failure` | How the turn ended and what a provider refused, in the same words the instruments use |
| `embed_stored_email` | `mailfathom.embedding.passages` | How many passages of that message were given a vector |
| `backfill_email_embeddings` | `mailfathom.embedding.outcome`, `…failure` | The same two, for the sweep the pass performed |
| `backfill_email_embeddings` | `mailfathom.embedding.backfill.chunked`, `…messages`, `…passages` | What the pass cut, brought up to date, and gave vectors to |
| `backfill_email_embeddings` | `mailfathom.embedding.generation.switched` | Whether a generation being built became the one searches are answered from during this pass |

Of the endings that reach an outcome tag, a provider that refused is the one that marks either span as an error.
Everything else — no active profile, a declaration that disagrees with what was activated, a spend ceiling that bound, a
turn that spent its call budget — is an ordinary ending the deployment is meant to reach, and marking those as errors
would make a correctly idle instance read as a failing one.

A turn or a pass that reaches no outcome at all is the second way either span ends, and it is published with an error
status and no outcome tag — the same rule [`run_job`](#durable-background-work) is held to, and for the same reason:
every word in that dimension is a decision the work reached. Three things end a turn that way, and the worker isolates
all three so the messages behind it are still embedded: the host cancelling mid-turn on shutdown, a concurrency conflict
the retry policy could not resolve, and an unexpected failure. So an ordinary restart leaves the one turn and the one
pass that were in flight marked as errors, which is what an abandoned unit of work is.

Which message was embedded is nowhere on either span: a stored identity would open a series per message, so what is
published is what the work did and the message stays in the queue the worker took it from.

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

Every tool call is counted and timed. `mailfathom.mcp.tool.calls` and `mailfathom.mcp.tool.call.duration` carry
`mailfathom.mcp.tool` with the tool's name and `mailfathom.mcp.tool.outcome` with one of `succeeded`, `tool_error`,
`cancelled`, `protocol_error`, `refused`, or `failed`. They answer what a span cannot: how often a tool is called, how
its duration is distributed, and whether either is moving — which is how a regression in one tool arrives, long before
anybody correlates the individual complaints it produces. The figure is the same one the record of that call carries,
taken from the same measurement rather than from a second timing path.

The three failing outcomes are apart on purpose. `tool_error` is an answer the tool diagnosed and reported, `refused` is
a MailFathom error code the caller can act on, and `failed` is the generic code a call gets when nothing diagnosed it;
a deployment answering every call with a refusal and one that is broken would otherwise read the same. `cancelled` is
ordinary traffic on this surface — an impatient client, not a fault — and `protocol_error` is a JSON-RPC error the
transport reported.

The tool dimension is the one thing here a caller chooses, so it is bounded by construction: a name is used only when
this surface publishes a tool answering to it, and anything else is measured as `(unpublished)`. The log line beside it
keeps the name the caller sent whenever its shape is safe, because somebody diagnosing a client's mistake needs to read
it; a metric dimension cannot afford the same, since a client calling `list_email` in a loop would otherwise mint a time
series that never goes away. Nothing else about a call reaches either instrument — not an argument, not a filter value,
not a mailbox, not a result.

One counter belongs to no feature at all. `mailfathom.persistence.commits` counts every local write transaction this
process completed, tagged with `mailfathom.persistence.commit.outcome` as `committed` or `concurrency_conflict`. An
optimistic concurrency conflict is an expected branch rather than a failure — the retry policy commits again from a
fresh read — so a conflict it resolves leaves no other trace, and the only one that surfaces today is the conflict
nobody resolved, which arrives as a single exception after every allowed attempt was spent. The rate is what separates a
deployment where two writers race constantly from one where they never meet, and it is the reading that says a bound
wants raising before anybody sees that exception. Both outcomes are counted because a rate needs the writes it is a rate
of, and the denominator is MailFathom's own sessions rather than EF Core's count of every `SaveChanges` this process
issues. What was written is nowhere on it: a session covers whatever a use case staged, so any dimension naming it would
eventually name mail.

### What an authorization refusal records

A caller reaching something its grant does not carry is counted by `mailfathom.authorization.refusals`, tagged with
`mailfathom.authorization.surface` as `mail` or `administration`, `mailfathom.authorization.operation` with the tool or
the route that was refused, and `mailfathom.authorization.permission` with the permission that would have sufficed. It
is the signal worth alerting on rather than any single failure: one refusal is a client that was narrowed, and a
credential that starts producing them is one being used for something it was never provisioned for.

**On the MCP surface this record is the only place the boundary is visible at all.** A tool a caller may not reach is
absent from its listing, and a call naming one is answered exactly as a call naming a tool that does not exist, so a
client that stopped working is diagnosed from here rather than from anything it received —
[the MCP endpoint page](mcp-endpoint.md#what-a-credential-decides-and-what-it-does-not) records why. The administrative
surface names the permission to the caller as well, and is still counted here, because `mfctl` reporting one refusal to
one operator is not a rate anybody can watch.

Beside each measurement is a `Warning`, and it carries the one thing the counter deliberately does not: the credential
the caller was admitted as, which is what an operator repairs the grant of. That value is on the log alone because its
cardinality follows the credentials an operator wrote and, where a token admitted the caller, it is an issuer and that
authorization server's identifier for a person — which belongs in a record a deployment sets a level on rather than in
an exported series that never goes away. Work reached under no principal at all is recorded as `(none)`.

The operation dimension is bounded the same way the tool dimension above is, and for the same reason: a tool name is
used only where this surface publishes a tool answering to it and anything else is `(unpublished)`, while an
administrative route is named by the pattern this repository mapped it under rather than by the address the request
carried. A refusal that no grant would have satisfied is counted under `(none)`, and its log line names no permission
rather than naming one that would not have helped. Three arrangements produce one, and they are not equally
interesting: a call naming a tool this surface does not publish, which lands as `(unpublished)` under `(none)` and is
the ordinary mistake of a client on a stale or misspelled name; a route that published no decision; and a use case
refusing over the kind of principal that reached it rather than over a grant. The first is a client to repair and the
other two are defects in this repository, so read the operation before reading the remedy — and an alert on this
counter as *a credential asking for what it was never granted* is written against the operations a grant could have
reached rather than against `(unpublished)`.

**A listing that withholds a tool records nothing.** Nothing was refused there, every narrowed caller would produce one
on every listing it asked for, and the omission has no operation to partition by — so the reading this signal exists for
would sit under the steady state. Nor does a permitted call or a permitted route record anything here, so the ordinary
path costs what it did before. Nothing about the refused request reaches either channel beyond the four values above:
not an argument, not a route value, not a header, not the credential the caller presented, and nothing about the mail
the request was for.

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

Two more sit there for the same reason one level along, and both exist because the libraries span only the ends. A
provider call is spanned by the AI instrumentation and a query by the database one, so a read whose time went into
neither would otherwise be a duration with nothing under it.

| Span | Where it is opened | What it carries |
| --- | --- | --- |
| `rank_mailbox_search` | Inside `search_mailbox`, and inside an answering run's own retrieval | The same two tags the reads above carry, with the count being the candidates the ranking scored rather than the window returned |
| `scan_sensitive_content` | Wherever a read guards a payload before publishing it | `mailfathom.sensitive_content.egress_point`, `…texts` as how many texts the operation scanned, and `…outcome` as `succeeded`, `refused`, `cancelled`, or `failed` |

The ranking's count is deliberately the ranking's rather than the read's. A hybrid search asks each side for four times
the window so the fusion has agreement to observe, so a call returning ten matches having scored eighty candidates —
forty from each side — is the ordinary case, and the two numbers side by side are what separate a slow fusion from a
wide one. A lexical-only search ranks exactly the window it returns, so there the two numbers agree, which is itself
the answer to which of the two rankings ran.

**The scan is spanned per guarded operation and never per guarded value.** One `get_email_content` call scans a body
representation, a subject, and a display name for every email it names, so a span apiece would report each of those as
quick while the read they compose stayed slow — and the instruments under
[what guarding an egress point publishes](#what-guarding-an-egress-point-publishes) already answer at the level of a
value. The operation is the payload a use case is about to publish: one message's content, one page of a listing, one
window of results, one conversation's subjects. A deployment with both scanner switches off opens none of them, because
nothing is constructed on that path at all.

The four endings exist because every way a scan stops leaves the same way a scan that worked does. `refused` is not an
error status — a scanner that could not answer stopped the egress on purpose, and the read above it carries the failure
— and neither is `cancelled`, which is a client or a shutdown rather than anything the scanner did. `failed` is what is
left: an operation that neither finished nor was stopped, which is the scanner having faulted, and it is the one that
marks the span as an error. Reporting that as a success is what would rule the scanner out of an investigation it
belongs in.

Four instruments answer over all of those reads what that span answers about one, and cover the write beside it.
`mailfathom.mail.content.read.bytes` and `mailfathom.mail.content.write.bytes` are how large the messages moving through
the store are, and `mailfathom.mail.content.read.duration` and `mailfathom.mail.content.write.duration` are how long
each took, tagged with `mailfathom.mail.content.outcome` as `found`, `absent`, `stored`, `discarded`, or `failed`. All
four are distributions rather than totals, because what an operator acts on here is the tail: one enormous message and a
steady stream of ordinary ones cost the same in a sum and mean entirely different things. A read of an email whose
content was never stored is timed and not sized, since a zero there would pull the distribution towards a message that
never existed; a read or a write that threw is timed under `failed`, so a store that started failing does not read as one
nobody is using. The write is measured and deliberately not spanned — it happens once per stored message inside a folder
run that already has a span, so a span apiece would put one per synchronized email into a trace store to say what the
histogram says better.

A write is published under the ending of the transaction that staged it, which is what separates `stored` from
`discarded`. Raw MIME is staged inside an optimistic-concurrency attempt, and an attempt that loses the race is run
again from the beginning in a fresh transaction — so a write counted where it happens would report one message as two
stored ones, and would do it in exactly the deployment under contention that `mailfathom.persistence.commits` is being
read about. `stored` is therefore the count of messages this deployment holds, `discarded` is payload it carried and
threw away, and a `discarded` rate that is anything but negligible is the same finding as a conflict rate that is,
priced in bytes.

One more span here belongs to no request at all, and it is one of four — the others are
[`run_job`](#durable-background-work), [`embed_stored_email`, and `backfill_email_embeddings`](#what-mailfathom-publishes-under-its-own-name),
each described with the subsystem that opens it. The extraction backfill opens **`backfill_email_extraction`** once per
bounded pass, which is what tells work an interval caused apart from work a caller caused — without it the pass appears
as parentless database commands competing with the requests around them. It carries
`mailfathom.mail.extraction.backfill.extracted`, `…unreadable`, and `…missing_content` as the counts the pass reached,
`…remaining` as whether any stored email still awaits extraction, and `…outcome` as one of `succeeded`, `deferred`,
`failed`, or `interrupted`. `deferred` is a competing writer the pass could not resolve against and `interrupted` is
shutdown; neither is a failure, and the next interval resumes from the committed position in both cases.

That pass publishes a family under the same names, because the question an operator has about a backfill — *will this
finish* — needs a rate and a remaining amount side by side, over passes rather than within one.
`mailfathom.mail.extraction.backfill.extracted`, `…unreadable`, and `…missing_content` count what the passes moved, and
`…run.duration` times each pass under the same four outcomes the span carries, so its own count is how many passes there
were. Each counter is added to only when it moved, so an instance with nothing left to extract is not a stream of zeroes
indistinguishable from one working through a mailbox.

`mailfathom.mail.extraction.backfill.outstanding` is what those counters are working through: how many stored emails
still awaited extraction when the most recent pass ended. It is fed once per pass rather than measured when a collector
asks, because it is a count over every message the walk still owes work on and answering it inside the meter's callback
would put that scan on whatever interval a collector happened to be configured with. It is measured after the pass
rather than before it, so what a pass moved and what it left behind describe the same moment — and because the last pass
of a backfill is the one that finds nothing to do and then ends the worker, which would otherwise leave the figure it
started with published for the life of the process.

Two readings of it are worth knowing. A backlog that stops falling while the extracted counter keeps rising is a walk
finding new work as fast as it does old — a mailbox still synchronizing rather than a backfill that has stalled, and the
two are only separable because both figures are published. And a small figure that never reaches zero is not a stall
either: a message no reader can parse, and one whose raw MIME is no longer there, never gain an extraction, so they stay
in this count while the walk correctly reports nothing left to attempt. The counters beside it say how many of each the
passes stepped over.

Nothing on any of these spans is derived from a message. There is nowhere on them to put a query text, a filter value, a
cursor, a subject, an address, or a stored identity — the values are counts, sizes, and closed sets of MailFathom's own
words, which is a cardinality rule as much as a privacy one.

### Durable background work

Every attempt at a job opens **`run_job`**, and that span is what makes durable work readable as work at all. A job is
dispatched by an interval rather than by a request, so without it everything an attempt issues — its database commands
above all — reaches a trace store parentless, competing with whatever the process was serving at that moment. The span
is opened around the attempt rather than around the pass that dispatched it, so a pass running several jobs at once
produces one span each rather than one covering all of them.

| Tag | What it carries |
| --- | --- |
| `mailfathom.job.type` | The job type's own name, which is the same word the log line, the instruments, and the stored row use |
| `mailfathom.job.attempt` | Which attempt this was, counting from one, which is what separates a slow job from one that has been failing all day |
| `mailfathom.job.outcome` | How it ended, in the same six words the instruments below carry |
| `mailfathom.job.failure` | `transient` or `permanent`, on the attempt that dead-lettered the job and on no other |

**The attempt also carries a link to the trace that enqueued it, where one was recorded.** A durable queue is a break
in a trace rather than a tree: the folder run, the tool call, or the pass that asked for the work ends long before a
worker claims it, so making the attempt that work's child would ask a span store to hold one trace open for as long as
the queue is deep. What travels instead is the W3C context of whatever was running at the enqueue, written onto the job
row in `EnqueuedTraceParent` and `EnqueuedTraceState` and turned back into an `ActivityLink` when the attempt opens. The
attempt stays its own trace, and a mailbox change that never converged is one link away from the run that asked for it
rather than a search through logs.

Absence is ordinary and is never a failure. Every row written before those columns existed carries none, a job enqueued
while nothing was being traced carries none, and an attempt at either opens the same span with no link on it. Nothing
else about the enqueue is kept: a trace identifier is a random number this process minted, which is the whole reason it
is safe to store beside work that points at mail.

Three of those six endings mark the span as an error, and they are the three where the work itself failed:
`handler_failed`, `handler_missing`, and `timed_out`. An attempt the host released on shutdown and one whose lease had
already moved on carry their ending and no error, for the same reason the counters below treat them as ordinary — a
rolling deployment releases every attempt in flight, and marking those would put a wave of failed job traces in front of
an operator on every restart, indistinguishable from a handler that is genuinely broken.

Nothing else about the job reaches it. Not its identifier, not the account it belongs to, and above all not the
idempotency key, which is composed of folder aliases and message occurrences — the same rule the instruments below are
held to, for the same reason. An attempt that reported no outcome at all is published with an error status and no
outcome tag rather than an invented one: every word in that dimension is a decision the executor reached, and a
dispatch that never got that far reached none of them.

The queue of durable background work publishes six instruments, all broken down by `mailfathom.job.type` — the job
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

`mailfathom.jobs.schedule.dispatches` counts what a recurring dispatch decided about one schedule, tagged with
`mailfathom.job.outcome` as one of `seeded`, `not_due`, `dispatched`, `already_dispatched`, `previous_run_in_flight`, or
`refused_at_capacity`. It is one measurement per schedule per pass, which is at most once per `Jobs:PollInterval`, and
`seeded` appears once per schedule ever: the first pass that sees a declaration records where it starts counting from
rather than treating every occasion since the epoch as missed.

`mailfathom.jobs.schedule.skipped_occurrences` counts the scheduled occasions that were deliberately not run, under the
same outcome tag. A due time that passed while the process was down, while the queue was full, or while the schedule's
previous run was still going is skipped rather than replayed, so this is the only place those occasions are visible at
all — nothing else records a walk that did not happen. **A steady non-zero rate is worth alerting on**: under
`previous_run_in_flight` it says a schedule asks for a mailbox walk more often than the walk finishes, and under
`refused_at_capacity` it says `Jobs:MaxQueueDepthPerType` is turning scheduled work away. Neither carries the schedule's
own identity, which is composed of an account and a rule name and would grow with the configuration; which schedule is
behind is read from the log line the worker writes beside the measurement.

An instance with `Jobs:Enabled` switched off, or one with no registered handler, publishes none of the six: its worker
does not start, so it neither runs work, dispatches a schedule, nor measures the queue. The depth of a queue that
instance is not draining is somebody else's replica to report.

### What a synchronization cycle emits

No instrumentation package exists for the mail library, so without what follows the part of MailFathom that spends the
most wall-clock time would publish nothing of its own at all. Two spans and eight instruments answer the two questions
an operator opens a dashboard with: is this account still synchronizing, and if it is slow, which part of it is.

One account's cycle opens **`synchronize_account`**, and each folder it works opens **`synchronize_folder`** beneath it.
That nesting is the whole point of the pair — a cycle whose duration doubled is attributable to the folder it doubled
in rather than to the account as a whole — and the records the same cycle already logs carry the trace and span
identifiers of whichever of the two they were written inside, so a count in a log line and the span it belongs to are
one thing.

Beneath the folder run sit the stages it passes through, for the same reason one level down: a folder run fails and
slows for reasons that are remedied in different places, and one duration over all of them says only which folder was
slow. Each carries `mailfathom.mail.sync.outcome` as `succeeded`, `failed`, or `interrupted`, and nothing else — the
account and the folder alias are on the run above and would be repeated on every stage of every run to say what the
parent already says once.

| Span | The stage it reports |
| --- | --- |
| `resolve_mail_folder` | Turning the configured alias into the folder the server advertises for it, which opens a session of its own |
| `open_mailbox_session` | Connecting, negotiating transport security, authenticating, selecting the folder, and reading which incarnation of it the server is serving |
| `discover_mailbox_emails` | The forward walk: discovering mail, retrieving what it stores, deriving from it, and committing the checkpoint |
| `fetch_email_batch` | One batch of that walk's listing, opened once per batch and bounded by `MaxMetadataBatchesPerRun` |
| `reconcile_mailbox_folder` | The backward pass over the window, and the modification sequence it commits |
| `refill_deferred_content` | Retrieving the content of mail an earlier run recorded without it |

`fetch_email_batch` is the one that repeats, and it is what makes the story the pair was introduced for readable inside
the walk: a discovery whose duration doubled while its batches stayed quick is local work — a scanner, a derivation, a
database under contention — while a discovery that doubled along with them is a mail server. It repeats at most
`MailSynchronization:MaxMetadataBatchesPerRun` times, which is what keeps the bound on how many of these a run can publish.

There is deliberately no stage per message. A folder run stores as many emails as its batch bounds allow, so a span
apiece would put one per synchronized message into a trace store to say what
[`mailfathom.mail.sync.emails.stored`](#what-a-synchronization-cycle-emits) says better — the same reasoning that keeps
the content write measured and unspanned.

A run that ended early publishes the stages it reached and no others, which is how far it got: an alias the server
advertises no folder for publishes the resolution stage alone. `interrupted` is shutdown rather than a defect, exactly
as it is on the folder run above.

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

### Contact collection

An account that [collects contacts](../features/contacts.md#collecting-contacts-from-arriving-mail) writes personal data
about third parties without anybody asking it to, so an owner who switched it on is owed a way to see what it is doing.
`mailfathom.contacts.collection.decisions` is that way: one measurement per address considered, tagged with
`mailfathom.contacts.collection.outcome`, which carries one of six words — `recorded`, `already_held`,
`below_threshold`, `excluded`, `not_correspondence`, and `run_bound_reached`.

The six are what make the readings distinguishable. `recorded` rising is the book filling; a deployment where it is
almost all of the traffic is one whose threshold is too low. `already_held` becoming almost everything is the ordinary
state of a book that has filled. `excluded` at nearly the whole volume is a policy excluding everybody, which usually
means one pattern is wider than its author meant. `run_bound_reached` appearing repeatedly says runs are stopping at
`MaxContactsPerRun` rather than at the end of the mail, which is expected during a first synchronization and worth
looking at afterwards. `below_threshold` and `not_correspondence` are the two that mean collection is working as
configured and writing nothing.

The one tag is MailFathom's own closed set. No address, name, display name, folder, or message identity reaches an
instrument from collection: the outcome is a decision about a person and never the person, and nothing here logs at all.

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
one of them — read the count beside the duration before sizing the feature or alerting on a percentile. That sum is
also a span: `scan_sensitive_content` is opened once per guarded operation, carries how many texts it scanned, and sits
beneath the read that asked for the payload, so one call's guarding is readable without adding the values up.

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

## What the administration command emits

Nothing. `mfctl` runs on the operator's own machine and holds no exporter, no collector address, and no telemetry
configuration of any kind, so a span or a measurement it produced would be built and dropped — and every request it
issues is answered by a deployment that is already instrumented. So the trace of an administrative act is the one the
endpoint opens when the command reaches it, and it is a root because nothing upstream of it is collected.

What that costs is worth stating rather than working around: a command that signs in, performs an action, and reads a
status back is three requests, and they arrive as three traces with nothing saying they belonged to one invocation.
Grouping them would mean the command sending `traceparent`, which means a span, which means an activity source and a
listener in a binary published trimmed and self-contained for the sake of holding nothing it does not need. Three
traces an operator correlates by time is the cheaper answer, and the deployment's own spans are the ones that carry
what the act actually did.

What the command writes instead is its own output, to the terminal the operator ran it in, and one line per invocation
to a file beside its credential store. Neither is a telemetry signal and neither is collected anywhere: the first is the
answer to the command, read by the person who typed it, and the second is the only durable record that the command ran
at all, which is what an operator has left once the scrollback is gone. [What the command records about
itself](admin-endpoint.md#what-the-command-records-about-itself) states the path, the fields, and the two ways to turn
it off.

That file is held to the half of the rule below that is about disclosure and not to the half that is about a collector,
and the difference is worth being exact about. No credential and no mail reaches it, exactly as here. A deployment's own
address does — through the operator's name for a profile, and through a failure line quoting what they typed — which
this page forbids in every signal above. What makes that sound there and not here is the boundary each one crosses: an
exported signal leaves the machine for a store somebody else may read, and this file stays in the operator's own
directory beside a credential store that already records every profile's endpoint in clear.

## What no signal carries, and what holds every signal to it

Everything above is one rule, and it is a cardinality rule as much as a privacy one. **Counts, sizes, durations,
outcomes, error codes, and MailFathom's own configured account, folder, and provider-endpoint aliases are permitted.
Mail content, an address, a subject, a remote folder path, a message identifier, a UID, a search term, a credential, and
model prompt or completion text are not** — every one of them would open a time series per message or per person, quite
apart from putting personal data in a span store. It governs a name as much as a value: a span, an instrument, and a
dimension are all named after the operation or the quantity they report, never after anything the operation saw.

The rule is the same one that governs the log lines, and the aliases are the one place a value an operator wrote
reaches an exporter. That is deliberate: an alias is MailFathom's own word for an account, a folder, or an endpoint, it
is bounded by the size of somebody's configuration, and a dashboard without it cannot say which account is behind.

What makes it a contract rather than a convention is that it is asserted over the emitted surface as a whole rather
than per feature. A rule checked once per publisher is a rule the next publisher is not covered by, and a telemetry
surface grows one publisher at a time — so the unit-test suite drives **every** publisher the two boundaries that own
one hold, through a listener over MailFathom's activity source and meter, and holds what came out against four claims:

- No span name, instrument name, or dimension key is named after a word from the refused list, matched whole segment by
  whole segment. That is what separates `token`, which names a credential, from `tokens`, which is how many a model
  consumed.
- Every instrument name and dimension key sits under the single `mailfathom.` namespace in lower case, which is what
  keeps a dimension from being minted out of a value at run time.
- Every span is named as one lower-case phrase, so a span name is an operation rather than anything it read.
- **Every string the drive hands a publisher is a sentinel**, one per class of input — a configured alias, text a caller
  sent, text read out of a message — and the assertion is where each was allowed to surface. A caller's text and
  anything mail-derived may reach nothing at all; an alias may reach the dimensions named for it and no others.

Two things keep that from decaying quietly. Within each of those two boundaries a publisher is found by what it holds —
an instrument field, or a declared span name — rather than by where it lives, so a publisher nobody added to the drive
fails the suite instead of going unasserted. And the names every assembly of this deployment *declares*, the host's own
included, are read and held against the same vocabulary, which reaches what no drive does: the span the extraction
backfill worker opens, and a dimension only a failure path sets.

## How much of a trace is recorded

Sampling is a decision rather than a default to inherit, and MailFathom's is **parent-based always-on**: every trace
this process starts is recorded, and a trace it did not start keeps the decision the caller already made.

The always-on half is what a mailbox-sized workload is worth. The volume is bounded by one deployment's accounts and one
assistant's tool calls rather than by public traffic; a folder run whose duration doubled is attributable only if the
run before it was recorded too; and export is off unless an endpoint is configured, so the default costs an
unconfigured deployment nothing at all. The parent-based half keeps a head decision made upstream from being overturned
here — the MCP surface continues a caller's trace, and a caller that dropped it is not asking for a fragment of it back.

A deployment paying a collector per span is the case that wants the other answer, and it is the operator's to give:

| Variable | What it does |
| --- | --- |
| `OTEL_TRACES_SAMPLER` | `always_on`, `always_off`, `traceidratio`, `parentbased_always_on`, `parentbased_always_off`, or `parentbased_traceidratio`. Unset is the default above |
| `OTEL_TRACES_SAMPLER_ARG` | The ratio the two `traceidratio` samplers use, between `0` and `1` |

Both are read by the OpenTelemetry SDK itself, and MailFathom sets its own sampler **only when the first is unset** —
the SDK ignores its own configuration when a sampler was set programmatically and reports the fact to an event source
nobody is listening to, so setting one unconditionally would answer an operator's variable with silence.

Three things follow from the SDK reading these itself. `OTEL_TRACES_SAMPLER_ARG` on its own does nothing, because no
sampler is a ratio one until the first variable names it. A value the SDK does not recognize — a hyphen where the name
takes an underscore — is reported to that same event source and falls back to `parentbased_always_on`, which is what
this host would have set anyway, so a misspelling reads as the variable having been ignored rather than as an error.
And both have to be **environment variables**, not configuration keys, for the reason the exporter switch below gives —
a start that writes any `OTEL_*` name into a file or an argument fails naming it.

Health and liveness probes are excluded from tracing before any sampler sees them. A probe arrives every few seconds
for the life of the process and says the same thing every time, so on a deployment exporting to a collector it pays for,
the polling would otherwise be most of the bill.

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
