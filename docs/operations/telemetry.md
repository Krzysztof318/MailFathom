# Telemetry and the Aspire dashboard

<!-- describes: backend/src/Application/Observability/**, backend/src/Common/Observability/**, backend/src/Host/Observability/**, backend/src/Host/ServiceDefaultsExtensions.cs, backend/src/Host/Api/ClientTelemetryEndpoint.cs, backend/src/Host/Hosting/Workers/**, backend/src/Infrastructure/Observability/**, backend/src/Infrastructure/Mail/MailServerConnectionBudget.cs, backend/src/Infrastructure/Mail/MailKit/MailKitImapClientFactory.cs, backend/src/Infrastructure/HostApplicationBuilderExtensions.cs, backend/src/Mcp/Observability/**, backend/src/Cli/Diagnostics/**, backend/src/AppHost/**, backend/src/AI/ProviderAdapters/OpenAiCompatibleClientFactory.cs, frontend/src/Client.App/src/telemetry/**, frontend/src/Client.Backend/src/telemetry.ts, frontend/src/Client.Backend/src/mailAttachment.ts, frontend/src/Client.Backend/src/ownDisplayName.ts, frontend/src/Client.Backend/src/ownPortrait.ts -->

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

Two filters are deliberate. Requests to the health-probe paths are not traced at all, because a probe arrives every few
seconds for the life of the process and says the same thing every time — tracing it would fill a trace store with
polling instead of work. Neither are the client's OTLP routes, because
[exporting must not feed itself](#exporting-is-never-itself-exported).

A trace that began in the client covers both stacks. The client sends W3C trace context on every request it makes, so
the server span above is that client span's child; [which surfaces continue an incoming
trace](#which-surfaces-continue-an-incoming-trace) is the decision, per surface, and [what a client-originated trace
contains](#what-a-client-originated-trace-contains) is what one holds.

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

Every dimension MailFathom names for itself is written in `snake_case`, and an outcome is written as a past
participle — `succeeded`, `failed`, `cancelled`, `lease_lost`, `outcome_unknown`. That is what lets a panel written
against one subsystem be reused against the next, so a query grouping the job queue by `lease_lost` groups delivery by
the same word. A value MailFathom carries in rather than names — a mutation's own name such as `set-seen`, a lifecycle
such as `dead-lettered` — is published exactly as the contract that owns it spells it, because renaming one for a
dashboard would put a second spelling of it into the world rather than remove one.

What publishes to that name is documented with the subsystem that does it.

Every change MailFathom makes to a remote mailbox opens a span named after the mutation, and is counted by
`mailfathom.mailbox.mutations` and timed by `mailfathom.mailbox.mutation.duration`, both broken down by the mutation,
the account, the folder, and how it ended. Two of those names are not mutations —
`file-outgoing-copy` and `withdraw-outgoing-copy`, which put a copy of this deployment's own outgoing message into a
folder and take it back out — and they report here regardless, because an operator asking what MailFathom changed about
a mailbox wants one answer rather than two dashboards. It is deliberately
**not** broken down by which IMAP commands carried the change — a relocation is one operation whether the server
offered RFC 6851 `MOVE` or the copy-flag-expunge sequence was used instead, and a dimension telling the two apart is
exactly what would make a missing server extension look like a different operation on a dashboard. Which path ran is in
the debug log.

The folder is carried on `mailfathom.mail.folder`, the same key every other mail family publishes it under, so a
mutation panel and a synchronization panel answer for the same folder somebody asked about. The ending is carried on
`mailfathom.mailbox.mutation.outcome`, whose values are `succeeded`, `failed`, and `cancelled`. The third one is
shutdown rather than a defect: a change the host stopped waiting for counts as cancelled and leaves its span's status
unset, for the reason `interrupted` is kept apart from `failed` on the synchronization counter below — a rolling
restart that reads as a burst of failed writes is a dashboard reporting the deployment's own restart as a mail-server
problem. What such a change left behind is not folded into success either: the write may have reached the server, and
which it was is settled from the change's own record, which the two gauges below report the backlog of.

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

`mailfathom.mail.content.limits_reached` counts the folder runs that ended against one of the byte limits, tagged with
which: `run_budget` for a run that spent what it may fetch, `storage_ceiling` for one that had to record messages
without their content, and `owner_storage_ceiling` for one whose owner was at their own share while the deployment
still had room. The last two are separate values rather than one because they ask an operator for different things —
more disk or a higher instance ceiling against the first, a larger share for one person or a wait against the second —
and a run that left messages for both reasons reports both, one measurement each. One message is deferred by one of
them rather than by both, because the instance's room is claimed first and an owner is never charged for a payload the
instance had no room for. All are counted rather than only logged
because each is a condition that persists — a run that stopped for its budget will stop again next interval, and a
deployment or an owner at a ceiling stays there until somebody acts — so a rising count says it has been running that
way rather than that it did once.
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
run ended, how many messages it cut into passages, brought up to date, and gave vectors to, and how many it stepped
past because the owner they belong to had spent their share. That last one has an instrument rather than a tag because
an owner's ceiling ends nothing: the run carries on, so there is no ending for a tag to describe. The instruments are
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
process completed, tagged with `mailfathom.persistence.commit.outcome` as `committed`, `concurrency_conflict`,
`transient_failure`, or `outcome_unknown`. Neither of the middle two is a failure in itself — the retry policy replays
the whole unit of work from a fresh read, after a lost race and after a database failure that can clear on its own
alike — so an ending it resolves leaves no other trace, and the only ones that surface today are the ones nobody
resolved, which arrive as a single exception after every allowed attempt was spent. `outcome_unknown` is the one no
retry resolves: the connection went away during the `COMMIT` round trip, so the server may have made the write durable
and the client will never hear which. It reaches its caller immediately rather than being replayed, because staging an
accumulating write again would apply it twice, and a rate of it that is anything but zero is a network or a database to
look at rather than a bound to raise. The rates are what separate a deployment where two writers race constantly from
one where they never meet, and a deployment that is losing connections from one that is not; they are the reading that
says a bound wants raising, or that the database is unwell, before anybody sees that exception. Every
outcome is counted because a rate needs the writes it is a rate of, and the denominator is MailFathom's own sessions
rather than EF Core's count of every `SaveChanges` this process issues. What was written is nowhere on it: a session
covers whatever a use case staged, so any dimension naming it would eventually name mail.

### What an authorization refusal records

A caller reaching something its grant does not carry — [what a credential may do](permissions.md) is the vocabulary
those grants are written in — is counted by `mailfathom.authorization.refusals`, tagged with
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
| `read_account_directory` | Which accounts the caller's owner owns, and how current the local copy of each is |
| `list_mailbox_timeline` | One bounded page of the stored email timeline |
| `search_mailbox` | One window of a ranking over the stored emails |
| `read_email_content` | The stored content of the emails one call named |
| `read_email_thread` | One bounded page of the conversation one call named |
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
| `scan_sensitive_content` | Wherever a read guards a payload before publishing it | `mailfathom.sensitive_content.egress_point`, `…texts` as how many texts the operation scanned, `…outcome` as `succeeded`, `refused`, `cancelled`, or `failed`, and `mailfathom.owner` as whose mail was being published |

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

### Reaching the object-storage endpoint

A deployment that selected the object-storage content backend reaches a second remote party — for the readiness probe,
and for every payload it stores and reads back — and three instruments answer the three questions an operator has about
one operation against it. They are published only where that backend is selected; an instance storing content in the
database opens no transport and publishes none of this.
[`ContentStorage`](configuration-runtime.md#contentstorage) is where the backend is selected.

`mailfathom.object_storage.operations` counts how much of it is happening,
`mailfathom.object_storage.operation.duration` how long one operation took in seconds, and
`mailfathom.object_storage.bytes` how many payload bytes it carried. The first two carry
`mailfathom.object_storage.operation` — `list`, `put`, `get`, or `delete` — and `mailfathom.object_storage.outcome` as
`succeeded` or `failed`; the size carries the operation alone, and an operation that carried no payload records nothing
there rather than a zero that would read as a write that moved nothing. Both distributions are histograms rather than
totals, for the reason the content store's are: what is acted on is the tail.

**Why it is failing is a dimension rather than an instrument.** A failed operation additionally carries
`mailfathom.object_storage.failure`, one of `caller_cancelled`, `host_shutting_down`, `timed_out`,
`authentication_failed`, `transient_transport_failure`, or `unrecognized` — a refused credential and an unreachable
endpoint are the same operation ending differently, and an operator wants them in one series they can split. Each of
those words is also the classification a boundary reports the failure's error code under, so a dashboard query and an
alert match the same value; [outbound resilience](../architecture/outbound-resilience.md#classifying-a-failure) states
how each is decided and which are worth repeating.

Nothing here carries an object key, a bucket, an endpoint address, or any part of a payload. A key names the row that
owns it and therefore a message, and the payload is mail; the operation and the classification are MailFathom's own
words and are the whole of what is published. The endpoint's own answers are the one place a response could carry a
key, and the client is constructed with response and metric logging off so neither reaches a log at any level.

The readiness probe publishes these on a deployment that stores nothing, one `list`, one `put`, and one `delete` per
scrape — a zero-length object under a key of the deployment's own, written and removed, so nothing about a message
reaches the bucket to establish that the bucket works. [Health endpoints](health-endpoints.md) records what an unready
instance means. Everything else on these instruments is mail: a `put` per payload stored, a `get` per payload read
back, and the `list` and `delete` operations reclamation issues, which the section below reports separately.

### Reclaiming content objects

Mail whose record is gone stops being mail when its payload leaves the endpoint, and two mechanisms take it there: the
deletion that follows a committed erasure, and the bounded sweep that reclaims objects no row points at.
[An object nothing points at is reclaimed](../features/email-content.md#an-object-nothing-points-at-is-reclaimed) is
what each of them does; this is what an operator reads.

They report through one set of instruments split by `mailfathom.content_object_reclamation.trigger`, which is
`erasure` or `sweep`, rather than through an instrument each. The split is the interesting part: a deployment whose
sweep reclaims almost everything is one whose post-commit deletion is failing, and that reading is only available if
both are in one series.

`mailfathom.content_object_reclamation.reclaimed` counts objects the endpoint removed,
`mailfathom.content_object_reclamation.bytes` how many bytes those objects held, and
`mailfathom.content_object_reclamation.failed` counts the ones it did not — each of which is left where it is for a
later sweep rather than retried in place. A shutdown that stops either mechanism part-way counts what it had not
reached as well, rather than only what the endpoint refused, and how much of the remainder that is differs by
mechanism: an erasure was handed every locator it was to remove and counts all of the ones it never attempted, while a
sweep counts the orphans it had not attempted in the page it was on, the pages beyond it having never been listed and
being reached instead by the segment that resumes the walk. Only the sweep contributes to the byte total, because only
a listing states a size; a deletion that follows an erasure knows the key and not the length. Each counter is added to
only when it moved, so an interval that reclaimed nothing publishes nothing rather than a stream of zeroes.

**`mailfathom.content_object_reclamation.oldest_orphan.age` is the number that says whether reclamation is keeping
up.** It is a gauge in seconds, read from the most recent sweep that reached the end of its listing — a run that
stopped part-way saw part of the bucket, and the oldest orphan in part of one says nothing about the whole. A bucket
too large for one run is swept by a chain of them, and each hands what it met on to the next alongside its listing
position, so the figure covers the whole sweep rather than its last segment. A value that stays near the configured age
floor is a bucket in step with the database; a value that grows across intervals is a backlog, and the interval or the
object ceiling is what to move.

Nothing here carries an object key, a bucket, or any part of a payload. A key names one message, so what is published
is counts, volumes, and MailFathom's own two words for the two mechanisms.

### The move of stored content

Carrying the content already in the database into that bucket is an operator's act rather than a setting, and what it
publishes answers a question asked across passes rather than within one: how much of the mailbox is in the bucket now,
what that came to in bytes, and how much the move would not touch. The run's own row answers that for one move; these
answer it for a deployment, across the restarts and the pauses a move of a large mailbox lives through.
[Moving stored content into the bucket](moving-stored-content.md) is the operation.

`mailfathom.mail.content.move.moved` counts the payloads copied, verified, and repointed at their object, and
`mailfathom.mail.content.move.moved.bytes` how much raw MIME they carried between them. Both move per payload rather
than per pass, because a pass a restart stopped has still moved everything it repointed — a counter that only advanced
when a pass finished cleanly would report a deployment restarting under load as one doing nothing.

`mailfathom.mail.content.move.refused` counts the payloads left in the database, and carries
`mailfathom.mail.content.move.failure` as `source_mismatch`, `object_mismatch`, `object_absent`, or `oversized`. The
reason is a dimension rather than four instruments because it is what an operator acts on and the acts differ: stored
bytes that disagree with their own row are a mailbox to re-synchronize, an object that came back wrong is an endpoint to
look at, and a payload too large to hold is a bound to raise.

`mailfathom.mail.content.move.pass.duration` is how long one bounded pass took, in seconds, and one pass is also a span
of its own — `move_stored_content` — so the endpoint and database work a pass causes is attributable to the pass rather
than arriving as parentless spans among the requests around them. The pass that walked past the last payload publishes
`reached_end_of_content` as an event on that span, which is what says when the backlog ran out.

**There is no dimension for the payload kind**, deliberately: a kind names which table a row is in, an operator does
nothing differently for one, and it would put the shape of the schema into every series the move publishes. Nothing here
carries an object key, a payload identifier, or any part of a message.

### While both stores hold the same payload

The move copies and never removes, so a payload it has carried is held twice until an operator releases the database's
copy. Two instrument families answer what happens in that window, and they are what the decision to release rests on.

`mailfathom.mail.content.fallback` counts the reads a moved payload's object could not answer and the retained copy
did, carrying `mailfathom.mail.content.fallback.reason` as `object_absent` or `object_mismatch`. **A flat counter is
what a release waits for**: it says the deployment has been reading from its bucket and the bucket has answered every
time, which is the only evidence there is that the copies are safe to free. A counter that moves at all is an endpoint
to look at rather than a release to ask for, and the two reasons are two different faults — an object nobody wrote, and
an object that is not what the row records.

`mailfathom.mail.content.release.released` counts the retained copies an operator's releases have freed and
`mailfathom.mail.content.release.released.bytes` how much raw MIME they were holding. They are counters rather than an
answer per request because releasing a large mailbox is a hundred bounded requests: what one batch freed says nothing,
and what the deployment has freed since it started is what an operator weighs against the backlog they began with. The
volume is the point rather than a decoration, since this is the one step of the move that actually takes weight off a
database. Neither carries a dimension of any kind — which payloads were freed is a list of mail.

Both move as each payload kind is freed rather than once per request, so a request cancelled part way through still
counts what it had already disposed of. Read the volume as what the releases covered rather than as an exact ledger:
two releases running at the same moment agree on the count, and each attributes to itself the bytes of a copy the other
freed first.

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

Three further gauges report the process-wide connection budget per IMAP server host. Each carries only
`mailfathom.mail.server`, whose `server-…` value is a keyed process-local pseudonym rather than the configured host:
`mailfathom.mail.server.connections.limit` is the configured ceiling, `…connections.active` is the connections and
attempts holding it, and `…connections.queued` is the attempts waiting for it. They aggregate every owner and account
without sharing a protocol session between any two of them or exporting the server's name.

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
means one pattern is wider than its author meant; it also carries the rare address collection could derive no name
from, which is why the reading is *this address was never a candidate* rather than *the owner's list caught it*. `run_bound_reached` appearing repeatedly says runs are stopping at
`MaxContactsPerRun` rather than at the end of the mail, which is expected during a first synchronization and worth
looking at afterwards. `below_threshold` and `not_correspondence` are the two that mean collection is working as
configured and writing nothing.

The one tag is MailFathom's own closed set. No address, name, display name, folder, or message identity reaches an
instrument from collection: the outcome is a decision about a person and never the person, and nothing here logs at all.

### What delivering the outbox emits

Sending is the other direction of the same absence: the mail library publishes nothing of its own here either, and a
message that fails to leave is invisible in a way an unsynchronized mailbox is not — nobody refreshes an outbox waiting
for it. Five questions are worth answering, and each has one signal.

**Is anything piling up?** `mailfathom.mail.outbox.depth` reports how many messages stand at each stage a send can still
move from, tagged with the account alias and `mailfathom.mail.delivery.stage`, whose values are `recorded` and
`transmission_begun`. It is a gauge fed by the delivery pass rather than a query the collector runs on its own
interval, exactly as [the job queue's depth](#durable-background-work) is: an outbox is read per account by the worker
that already reads it, and asking the database again on a schedule would count the same rows a second time for nothing.
A level that rises and does not come back down is mail that is not leaving; a `transmission_begun` level that does not
return to zero is the one an operator acts on, because nothing moves those messages without a person. The gauge covers
the two unfinished stages alone — what has been sent, refused, or withdrawn is history rather than backlog, and
[`mfctl outbox status`](admin-endpoint.md#reading-what-is-in-the-outbox-and-deciding-about-one-message) is where the
whole of it is counted.

**Is mail leaving?** `mailfathom.mail.delivery.attempts` counts every attempt that ended, tagged with the account alias
and `mailfathom.mail.delivery.outcome`, whose values are `sent`, `refused`, `deferred`, `outcome_unknown`, `released`,
`lease_lost`, `not_recorded`, and `missed_due_time`. One measurement per send rather than per pass, because a pass routinely ends in
several of them. A send a stopped process left mid-transmission is counted too, under `outcome_unknown`, at the pass
that finds it: the attempt was made by a process that never lived to report it, and it is stamped once, so counting it
where it becomes knowable is the only place it can be counted at all.

`outcome_unknown` is a dimension of that counter rather than a counter of its own, deliberately: a send whose server
never answered is neither a success nor a failure, and giving it an instrument would let a dashboard summing successes
and failures report a total that quietly omits it. **It is the one value worth alerting on at any rate above zero** —
each measurement is a message that may or may not have reached somebody and that nothing will attempt again until a
person decides. `refused` is a terminal failure and worth watching as a rate; `deferred` is the ordinary answer to a
provider that is briefly busy and is only interesting when it stops turning into `sent`. `released` is shutdown and
`lease_lost` is an attempt that had already been taken over, so neither counts against a deployment's health.
`not_recorded` is the store refusing to take an attempt's answer rather than a server refusing the message: the record
stands where the failed write left it and its lease is what frees it, so a rate above zero is a database to look at and
not an outbox to act on. `missed_due_time` is a message written to leave at a named time that a pass reached later than
`MailDelivery:AllowedSendLateness` allows — nothing was running when the moment came, or the queue was full — and it is
the value that says a held send ended without being transmitted. It stands in the outbox like any other refusal, so a
measurement above zero is a message somebody has to decide about rather than a provider to investigate;
[the mail configuration](configuration-mail.md#maildelivery) states the bound and its default.

`mailfathom.mail.delivery.retries` counts the same measurements again, narrowed to the attempts that were not a
message's first, under the same two dimensions. It is a counter of its own rather than a dimension of the one above,
because the question it answers is about the ratio between them: attempts rising while retries stay flat is a busier
deployment, and retries rising while attempts do not is the same mail being offered over and over. A first attempt is
never counted here, so the two series are read together rather than one being a subset a dashboard has to subtract.

**Can the owner see what they sent?** `mailfathom.mail.filing.attempts` counts every attempt to put a copy of an
outgoing message into one of this account's own folders, tagged with the account alias, `mailfathom.mail.filing.place`
— `draft`, `held`, `sent`, or `undetermined` where a failure ended before any place was chosen — and
`mailfathom.mail.filing.outcome`, whose values are `filed`, `already_filed`,
`not_requested`, `destination_unavailable`, `outcome_unknown`, `failed`, and `withdrawn`. Most of them are ordinary:
`not_requested` is an account that files no copy, `already_filed` is a settlement asked for twice, and `withdrawn` is a
mirror going away because its message left. Two are worth a dashboard. `destination_unavailable` is a deployment whose
`Sent` folder mapping resolves to nothing, so every message it sends goes unrecorded in the mailbox — a configuration
answer rather than a server one. And `outcome_unknown` means the same here as it does above and for the same reason:
the append may or may not have reached the folder, nothing will attempt it again, and repeating it would put a second
copy of somebody's own message in front of them.

**Is the drafts folder showing what is held?** `mailfathom.mail.draft.attempts` counts every attempt to bring a
mailbox's drafts folder into step with a draft this deployment holds, tagged with the account alias and
`mailfathom.mail.draft.outcome`, whose values are `filed`, `replaced`, `discarded`, `already_settled`,
`destination_unavailable`, `diverged`, `outcome_unknown`, and `failed`. It is a counter of its own beside the filing
one above, because a draft was never offered to a submission server: nothing about it is a delivery, and summing the
two would report an outbox busier than the mail actually leaving it. The first four are ordinary — a draft written, a
draft edited, a draft given up or sent, and a pass finding nothing owed. `destination_unavailable` is a deployment
whose drafts-role mapping resolves to nothing, so what an owner writes here is never in front of them in their own mail
client. `diverged` is the one that names the owner rather than the system: the tracked copy is no longer provably the
one this deployment appended — the role resolves elsewhere, the folder was recreated, the server named no placement —
so the message is left exactly where it is and a person decides. `outcome_unknown` means here what it means above and
for the same reason: the append may or may not have reached the folder, nothing will attempt it again, and repeating it
would put a second draft in front of somebody.

**Is one submission the slow part?** Each opens **`submit_outgoing_email`** as a client span, tagged with the account
alias and with `mailfathom.mail.delivery.record` naming the outbox record it is submitting, and
`mailfathom.mail.delivery.submission.duration` records how long it took. The record identifier is what joins the trace
to the row an operator then reads with
[`mfctl outbox show`](admin-endpoint.md#reading-what-is-in-the-outbox-and-deciding-about-one-message): a slow or failed
submission is otherwise a duration with nothing to act on. It is MailFathom's own identifier for a queued message and
names neither the message nor anybody it is addressed to. Both cover the exchange with the
server and nothing else: the claim, the record movements, and the backoff are local work, and including them would blur
the one thing the span exists to attribute. A span that ends without the server having answered is marked as an error,
which is what makes the trace and the `outcome_unknown` measurement the same event read two ways. A caller that stopped
waiting and a host that is shutting down are the exception and leave the span unmarked, because neither says anything
about the provider and marking them would make a rolling restart read as an outage on the rate an operator alerts on.

There is deliberately no span or instrument per recipient. A message is offered per address, so one apiece would open a
time series per person this deployment writes to; what each recipient was told is on the send's own record, where it is
reached by identity rather than exported.

Nothing here is derived from a message. Every instrument's dimensions are the account's configured alias and a closed
set of MailFathom's own words — no address, no subject, no identifier, no recipient count, and no line of what a
submission server wrote, since a server's refusal text routinely repeats the address it is refusing. The one identifier
anything carries is the outbox record on the span, which is a row number this deployment issued rather than anything
read out of the message, and it is on a span rather than on a metric precisely because a per-message dimension would
open a time series apiece. As with reading,
every SMTP client this deployment opens is constructed without a protocol logger, so the commands and responses of a
submission are written nowhere that a log level could expose.

### Re-reading stored mail

A re-derivation is a walk of a whole mailbox that nobody is watching, so what it publishes has to answer *is it still
moving* from a dashboard rather than from a terminal. Two spans and four instruments do that, and everything they carry
is a count, a duration, or one of the two configured aliases.

One segment of a run opens **`rederive_stored_mail`**, and each bounded pass beneath it opens
**`rederive_stored_mail_pass`**. The nesting is what the pair is for: a walk that got slower is attributable to a pass
rather than to an attempt whose length is decided by the execution timeout. Both carry `mailfathom.mail.account` and
`mailfathom.mail.folder`, and a run over the whole account reports `(every folder)` there rather than leaving the
dimension out, because a series missing a dimension and one carrying a folder are two shapes a dashboard sums
differently. It is not an alias and no folder can collide with it, since an alias is validated non-blank.

**How a segment ends is its status.** The one that reached the end of the scope ends `Ok`. One that handed the rest of
the walk to a segment of its own ends unset and publishes a `handed_on` event carrying
`mailfathom.mail.rederivation.queued`, which says whether the queue took the remainder. A hand-on the queue refused
ends the span in **error**, and that is the reading worth alerting on: the run stays outstanding, nothing is carrying
it, and no dead letter records it either, so the next request for the same scope is what puts it back in motion.

`mailfathom.mail.rederivation.rederived` counts the messages a pass re-read and wrote metadata for.
`mailfathom.mail.rederivation.unreadable` counts those whose stored MIME no reader could parse, which keep whatever an
earlier release read from them, and `mailfathom.mail.rederivation.missing_content` those whose raw MIME is no longer
stored, which only a fetch could reach. The two rejections are separate instruments because they ask an operator
different questions, and each is added to only when it moved: a stream of zeroes would make a mailbox that reads
cleanly indistinguishable from one nobody is walking.

`mailfathom.mail.rederivation.pass.duration` records how long one pass took, in seconds, under the same two dimensions.
A pass an attempt stopped part way through records nothing at all — what it committed is durable but is not a pass
comparable to another, and a truncated duration would read as a mailbox that had got faster.

The counters are what the run is watched by across segments, since a span covers one attempt and a mailbox takes many.
What an operator reads as a total is [`mfctl mailbox
rederive-status`](admin-endpoint.md#bringing-stored-mail-up-to-a-later-release), which reads the run's own record
rather than a metric.

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
[`MailAnswering`](configuration-ai.md#mailanswering) the keys.

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

Sensitive-content scanning publishes six instruments, all of them tagged with
`mailfathom.sensitive_content.egress_point` — `chat_prompt`, `hosted_embedding_input`, `mcp_snippet`,
`mcp_email_content`, `outgoing_mail`, `client_mail_listing`, or `client_mail_search`. The egress point is on every one
of them because it is
what an operator acts
on: "something was redacted" says nothing, while a scanner finding credentials in retrieved extracts and nothing in
subjects, or adding two seconds to a listing and nothing to an embedding call, is where a category list or a bound gets
changed. It is also how
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
| `mailfathom.sensitive_content.guarded` | How many texts were scanned, whatever followed. At `outgoing_mail` the text that stopped the act is counted here too, so this is not a count of what left |
| `mailfathom.sensitive_content.findings` | How many detections were made, split by `mailfathom.sensitive_content.category`. What followed depends on the point: replaced at the four redacting ones, and at `outgoing_mail` either counted and let through or, where the deployment screens for that category, the reason the act was stopped |
| `mailfathom.sensitive_content.omitted` | How many characters the analyzed ceiling dropped rather than trust unscanned — dropped from what was published at the four redacting points, and at `outgoing_mail` the reason the act was stopped |
| `mailfathom.sensitive_content.refusals` | How many operations a scanner that could not answer refused, by `mailfathom.sensitive_content.scanner` |
| `mailfathom.sensitive_content.stopped` | How many acts were cancelled because the material found is material this deployment will not let leave, by `mailfathom.sensitive_content.scanner` and `mailfathom.sensitive_content.category` |
| `mailfathom.sensitive_content.scan.duration` | What scanning added to one guarded operation |

**Three of these rows mean something slightly different at `outgoing_mail`**, because that point rewrites nothing:
the redaction runs there only to reach its findings and the redacted text is discarded. So a detection counted at
`outgoing_mail` is a detection *made* rather than one removed from something a reader received, and a message that was
stopped never left at all. Reading `findings{egress_point="outgoing_mail"}` as credentials taken out of transmitted
mail would be reading it exactly backwards — nothing was transmitted, and nothing was rewritten.

**The stopped count is the one series about an act that did not happen**, and today `outgoing_mail` is the only point
that produces it: everywhere else a finding is redacted and the operation continues. Both of its tags are written
whatever stopped the act, and an act stopped because the analyzed ceiling cut the text reads `not_scanned` in each —
a value rather than an absent tag, because a series missing one dimension is a second series and a query summing this
counter by scanner would silently drop every length refusal. Which findings stop an act is the operator's floor and the
owner's to tighten: [`SensitiveContent:ScreenOutgoingMailFor`](configuration-ai.md#sensitivecontent) is where the floor
is written, and [each owner's own posture](../features/sensitive-content-scanning.md#each-owners-own-posture) is what
may add to it.

The findings are split by category rather than totalled because which kind of material a mailbox is producing is what
decides whether a category list is right, and a total says only that the feature is switched on. The omitted count is
recorded only when the ceiling actually cut something: a zero on every guarded text would make the series say the
ceiling is in play on ordinary mail, which is the one question that instrument exists to answer. All six read zero on a
deployment where nothing is switched on for anybody, because nothing is constructed there.

**Whose mail an operation published is on the span alone**, as `mailfathom.owner`. Postures differ between the people
one deployment serves, so a scan that cannot be attributed to one of them cannot be read against what that person asked
for — and the same identifier on a counter incremented once per guarded text would be an unbounded dimension, which is
what every closed tag here exists to avoid. The attribute is absent rather than zero where no owner was resolved.

Nothing published here is mail or derived from it. The three tags are MailFathom's own closed sets, and the values are
counts and durations — never a rule's match, a position, a message identity, or any part of what was found, each of
which would put the credential in the telemetry written to prove it never left. The owner identifier on the span is the
deployment's own configured value and names a person no more than a mail account alias does.
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

### What the client telemetry proxy emits

The [client endpoint](client-endpoint.md#the-telemetry-routes) receives OTLP from the signed-in client and forwards it
to the same destination this section's own signals leave by. That path is deliberately silent — accepting and
forwarding writes no log record at any level, because a client open on somebody's desk exports every few seconds — so
five counters are the whole of what an operator reads it by.

| Instrument | Unit | What it counts |
| --- | --- | --- |
| `mailfathom.client_telemetry.accepted` | `{batch}` | Batches read, bounded, and attributed |
| `mailfathom.client_telemetry.records` | `{record}` | Records those batches carried, which is what their volume is read against |
| `mailfathom.client_telemetry.refused` | `{batch}` | Batches refused before anything was forwarded |
| `mailfathom.client_telemetry.forwarded` | `{batch}` | Batches the destination accepted |
| `mailfathom.client_telemetry.failed` | `{batch}` | Batches that did not reach the destination |

All five carry `mailfathom.client_telemetry.signal`, whose values are `traces`, `metrics`, and `logs`. A refusal
carries `mailfathom.client_telemetry.refusal` as well — `unsupported_media_type`, `rate_limited`, `too_large`,
`too_many_records`, or `malformed`, which is this endpoint's own closed vocabulary for the bounds it enforces. A
failure carries `mailfathom.client_telemetry.condition`, written as a past participle like every other outcome here:
`refused` for a destination that will never take the batch, and `throttled`, `unavailable`, `timed_out`, `unreachable`,
or `cancelled` for one that might.

`unauthorized` is the eighth and the one worth alerting on, because it is the only forwarding condition an operator
repairs rather than waits out: the collector would not take **this deployment's own** credential, which is what
`OTEL_EXPORTER_OTLP_HEADERS` carries. It is kept apart from `unavailable` for exactly that reason, and the client is
still told to hold — the batch was never what was wrong, and telling a browser to drop telemetry over a credential
nobody there can repair would lose what a corrected header would have carried.

**None of the five names a person.** A batch is attributed to its owner in the payload that leaves for the collector,
where it is what makes one client's traces separable from another's; the instruments here are the deployment's own
reading of whether the relay works, and an owner dimension on them would publish how much each person's client is
doing to whoever reads a dashboard. `refused` rising is a client sending something this deployment will not take, and
`failed` rising is the collector rather than the client — which is the distinction the two counters exist to make,
because the clients hold what they could not export and nothing is queued here.

The one thing that does write a line is a forwarding condition that holds: one record per condition, repeated at most
every five minutes, naming the condition and how many batches it has cost since it was last reported. That is a count a
rate cannot give an operator reading the log after the fact, and it carries no part of a payload.

### What the client publishes about itself

Everything above is the deployment's own reading of the relay. What travels through it is the client's, and it arrives
at the collector under the same name every other section here publishes under — **`MailFathom`**, as both a tracer and
a meter, for the reason [that section](#what-mailfathom-publishes-under-its-own-name) gives about one name: a second
registration would be one more thing to subscribe to, and being on the second stack is not a reason to take one.

**The resource identifies a client and never a person.** Three attributes are the client's own, and the receiver writes
the owner attributes itself, from the credential the export presented, replacing whatever a page put in their place.

| Attribute | What it says |
| --- | --- |
| `service.name` | `mailfathom-client`, which is what separates the client's records from the deployment's own in one collector |
| `service.version` | The same `<VersionPrefix>` the deployment reports, substituted into the bundle at build time |
| `mailfathom.client.head` | `web` or `desktop` — which head produced the record, not which operating system it ran on |

**Whoever is reading decides whether any of it is recorded, and the switch is on the client's settings screen.** It is
held by the deployment rather than by the browser profile or the desktop install it was moved in — it is
[one of the four client preferences](client-endpoint.md#the-preferences-routes), so declining once holds on every
machine that person signs in from — and the client keeps the last answer it was given on the device as well, so a
restart honours a decision before the first read comes back rather than recording for the second it takes. That cache
is not a second opinion: nothing writes it but the deployment's answer and the switch itself, and a client holding that
person's own answer never reads it again for the rest of their session. A sign-out, or somebody else signing in on the
same tab, reads it again — and reads it **under that person's own name**, because the cache is kept per person rather
than per machine: two people sharing a machine hold two answers, and one of them being a refusal is exactly why a
single name for both would be a defect rather than a saving.

**Off means nothing is recorded, not that records are dropped on the way out.** The switch stops the writing, so a
person who declined pays nothing to run the client instead of paying for everything but the upload. What was recorded
before the answer arrived is thrown away rather than flushed: a client that has never been told stands on the unset
answer and records into the buffer described below, so an answer of off empties that buffer without ever addressing
it. Turning it back on begins at that moment and reaches back over nothing.

**A deployment that forwards no telemetry offers no switch.** It answers
[`"telemetry": false` on the session route](client-endpoint.md#the-session-route); the client stops recording the moment
it reads that and throws away what it had held, and the settings screen says so in place of a control that would decide
nothing. Until a deployment has said either way the client records, because a deployment nobody has reached yet is
every cold start and every failed sign-in — which is what the buffer below exists to keep.

**The client exports nothing until somebody has signed in, and records from the moment it opens.** The exporter reaches
[the telemetry routes](client-endpoint.md#the-telemetry-routes) on the deployment the client is pointed at and
authenticates there with the session's own credential, so there is no destination and nothing to present before that.
What the client records meanwhile is held rather than dropped, because starting up, resolving which deployment the
client belongs to, and a sign-in that did not succeed are the failures a person cannot describe and the deployment
never saw. A deployment that named no collector answers those routes `404`, which is what a client sending to a
deployment that exports nothing meets.

**What is held is bounded, in memory, and never written to the device.** A buffer holds at most 512 records or 128 kB
of them per signal, whichever it reaches first, and a full one drops the oldest — bounded on size rather than on
elapsed time, so a sign-in screen left open for an afternoon keeps the newest records instead of throwing everything
away. Measurements are held by their own instruments rather than in a buffer, the temporality being cumulative, so the
first export after a sign-in carries every total recorded since the client opened. A client closed without ever signing
in keeps none of it: nothing reaches storage, and a restart begins empty.

**Signing in flushes the whole of it in one export, attributed to that session.** Signing out, and being pointed at
another deployment, each flush what the session recorded under that session's own credential and return the client to
holding — so a client that signs in again exports whatever accumulated meanwhile, and nothing is ever left queued for a
deployment somebody has left.

| Instrument | Unit | What it reports |
| --- | --- | --- |
| `mailfathom.client.telemetry.dropped` | `{record}` | Records the client recorded and could not deliver |

It carries `mailfathom.client.signal`, whose values here are `traces` and `logs`, and
`mailfathom.client.telemetry.condition`, which is `overflowed` where a full buffer dropped the oldest and
`export_failed` where an export did not arrive past the exporter's own retry bounds. Neither condition is written as a
log record: both describe a burst by definition, so a line per dropped record would be the loudest thing in a
deployment's log at the moment the client is least able to send anything. There is no `metrics` value on either — a
refused metric export loses nothing, the next one carrying the same cumulative totals again. The counter is itself a
measurement, so it reaches a deployment on the first export after a sign-in like everything else.

**One span and two measurements per request**, opened where the client asks and closed where it decides what the answer
was — which is later than the response, because a body the client refused as unreadable is a failure a screen acts on
and the status alone does not say so.

| Instrument | Unit | What it reports |
| --- | --- | --- |
| `mailfathom.client.requests` | `{request}` | Requests the client made to the client surface |
| `mailfathom.client.request.duration` | `s` | How long each of them took, answer read and all |

Both carry `mailfathom.client.request`, which is the method and the **route template** the client asked for —
`GET /folders`, `GET /messages/{storedEmailId}/body` — never a composed path, so a message identifier is not a
dimension value. The span takes the same string as its name. Beside it sits `mailfathom.client.outcome`, whose values
are the client's own contract rather than words invented here — `read` and `failed` — and, on a failure,
`mailfathom.client.failure`, carrying which of `unauthenticated`, `unauthorized`, `unavailable`, or `unreadable` the
client mapped the answer to. A failed request's span status is an error carrying no message: what failed is already the
dimension beside it.

**Every request the client makes is one of them, including the four it does not put on the wire itself.** A file a
message carries, and the picture the signed-in person is drawn by, arrive as octets rather than as a document, so those
four requests are composed by the half of the client that owns the wire and sent by the half that may name a browser
API. Where the span begins and ends is the composing half's decision either way: it opens around the composition and
the send together and closes on what the sending half made of the answer, so the record is the same shape as every
other request's and the request carries the trace context exactly as one this client sent itself.

**The outcome says whether an answer arrived, not whether the answer was yes.** A route that deliberately refuses — a
name this deployment will not record, a person it holds no portrait for — has answered something a screen acts on, so
it is `read`; so is a download the person waiting on it stopped, that being their own act rather than the deployment
failing to answer. What `failed` names is the four an operator can act on, and a file larger than the message described
is `unreadable` among them, the body having been refused rather than absent.

**Moving between screens is the client's alone to report.** Every screen after the first is rendered rather than
fetched, so the wait a person actually has is invisible from the deployment.

| Instrument | Unit | What it reports |
| --- | --- | --- |
| `mailfathom.client.navigations` | `{navigation}` | Moves to another space |
| `mailfathom.client.navigation.duration` | `s` | How long each move took, from the address changing to the space being on the screen |
| `mailfathom.client.arrival.duration` | `s` | How long the document itself took to arrive |

The first two carry `mailfathom.client.space`, whose values are the client's own space names, and each move opens a
span named `navigate <space>`. The space a run opens on is not a move and is reported by none of them. An address
naming anything else is reported as `other` rather than as itself, which is what keeps a route carrying a message
identifier out of a span name and out of a dimension however the client's addresses grow.

**`mailfathom.client.arrival.duration` is reported only by a client the deployment itself served**, and that is the
one place the two heads differ in what they can say. The browser times every document it loads, and that number means
a deployment answering only where the deployment is what answered: a desktop shell serves the same document out of the
bundle it packages, and a development server serves it beside a deployment it merely points at, so in both the same
measurement would be timing a disk and would put two unrelated quantities on one histogram. It is therefore absent
there rather than reported as a zero — which is why a panel reading it is read against `mailfathom.client.head` rather
than against the client population as a whole. Nothing in the client branches on the head to decide this: what is
asked is whether the document's own origin is the deployment the session is signed in to, which a shell serving the
bundle over `http://tauri.localhost` answers exactly as one serving from a scheme of its own does.

**Two log records, and no others.** `session_started` at `INFO` when a signed-in session begins, and
`credential_no_longer_accepted` at `WARN` when the deployment stops taking the credential a session held. Each carries
`mailfathom.client.event` naming which it is. There is no record per request and none per screen: a client open on
somebody's desk would make a deployment's log unreadable within a day, which is the same reasoning the relay above
applies to itself.

**Nothing the client sends carries what was on the screen.** No address, no subject, no correspondent, no search text,
no message identifier, no folder name, and no part of the credential reaches a span name, an attribute, a measurement,
or a log record. The route templates and the space names above are the whole of the vocabulary, and both are closed
sets. That is a test rather than a claim: the client's unit suite drives every operation the wire package can make with
mail in each argument that takes one, asserts that none of those values reaches a record, and asserts the stronger form
beside it — that every request the client names itself is a method and a route template whose segments are literals or
`{placeholder}` holes, which is what holds for a value nobody thought to forbid.

### What a client-originated trace contains

**A trace starts in the client, at the moment a screen asks for something.** The span the request wrapper above opens is
its root, and the deployment's own work hangs beneath it — which is what lets an operator read a screen that took four
seconds and see which of the four parts it was:

| Span | Where it comes from |
| --- | --- |
| `GET /messages/{storedEmailId}/body` | The client, opened where a screen asked and closed where it decided what the answer was |
| The server span for that request | The deployment's request pipeline, as the client span's child |
| `read_email_content` and its siblings | The use case the endpoint ran, named in [what a request-path trace contains](#what-a-request-path-trace-contains) |
| The database commands and content-store reads | Beneath the use case, as they are on every other surface |

The join is one request header. The client sends `traceparent` — the W3C trace context of the span it opened, and
nothing else: no `tracestate`, since nothing in the client writes a vendor entry, and no `baggage`, for the reason
[above](#which-surfaces-continue-an-incoming-trace). The client endpoint's CORS policy names that one header, so a
cross-origin page's preflight admits it and admits nothing beside it.

**A client that is not exporting sends no trace context**, because there is no span to name: the pipeline begins when
somebody signs in and ends when they sign out, and a request made outside it starts an ordinary root trace at the
deployment exactly as an MCP caller sending nothing does. **An export sends none either**, which is what keeps the
export path from feeding itself.

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

Two sets of paths are excluded from tracing before any sampler sees them, and neither is a sampling decision. Health and
liveness probes are the first: a probe arrives every few seconds for the life of the process and says the same thing
every time, so on a deployment exporting to a collector it pays for, the polling would otherwise be most of the bill.
The client's OTLP routes beneath `/api/client/telemetry` are the second, and they are excluded because
[exporting must not feed itself](#exporting-is-never-itself-exported).

## Which surfaces continue an incoming trace

Every traced surface continues one. A request arriving with W3C trace context is served under a span that is the
caller's child, so a caller that already had a trace sees this deployment's work inside it rather than beside it, and
that is what makes one trace cover a screen, its request, the use case behind it, and the database command. The two
untraced sets above never ask the question at all.

It is written down per surface rather than left as the framework's default, because what continuing buys is not the
same on each of them.

| Surface | An incoming trace context | Why |
| --- | --- | --- |
| Client endpoint | Continued | The caller is the signed-in client, and joining the two halves is the whole point: a screen that took four seconds and a query that took thirty milliseconds say nothing apart |
| MCP endpoint | Continued | An assistant's own trace reaches a tool call, which is what makes a slow answer attributable past this process's boundary |
| Administrative endpoint | Continued | `mfctl` holds no exporter and never sends one, so this is what the framework does rather than a capability anyone uses; a proxy or a scripted caller that does send one is treated like any other |
| Health probes | Never read | Untraced entirely, so there is no span to parent |
| The client's OTLP routes | Never read | Untraced entirely, for the reason [below](#exporting-is-never-itself-exported) |

**What a caller can therefore do is name a trace identifier, and nothing else.** It reads no trace, reaches no other
caller's spans, and changes nothing about what is recorded except the one thing the parent-based sampler already grants
it: a caller arriving with the sampled flag clear is not asking this process for a fragment of a trace it dropped, so
its own requests go unrecorded. On a surface that requires a credential — which is every posture the client endpoint
has, and the shipped default of the other two — that caller is authenticated, and a signed-in person choosing not to
have their own requests traced is the same choice as not sending the header at all. On an MCP surface an operator
deliberately opened without one, it is worth knowing that an anonymous caller can keep its own traffic out of the trace
store; the trace store is not where that deployment's access record lives, and the counters and log records the surface
publishes are unaffected.

Baggage is not propagated in either direction. It carries values rather than identifiers, and a value crossing a trust
boundary is a different decision from an identifier joining two spans.

### Exporting is never itself exported

A client that traced its own telemetry export would record the export, export that record, and have one more record to
export on the next batch — and the request carrying each batch would be spanned at the deployment on the way in. Three
things cut that, one per leg:

- The client's exporters do not go through the wrapper every request on the client surface goes through, so an export
  opens no span and takes no measurement.
- The deployment does not trace a request to `/api/client/telemetry`. The proxy is read by
  [its own five counters](#what-the-client-telemetry-proxy-emits) instead, which are aggregates rather than one record
  per batch and so cannot grow with what they measure.
- The proxy's forward to the collector runs with instrumentation suppressed, which is the outbound half of the same
  hop and the mechanism the OpenTelemetry SDK's own exporters use for exactly this.

## The one switch: `OTEL_EXPORTER_OTLP_ENDPOINT`

The OTLP exporter is attached only when the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is non-empty. Unset,
the instruments still exist and nothing collects them: no telemetry leaves the process, and the console remains the
only place logs go. There is no MailFathom-specific telemetry key — the exporter reads the standard OpenTelemetry
variables itself:

| Variable | What it does |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | The OTLP destination; setting it is what attaches the exporter, and what serves [the client endpoint's OTLP routes](client-endpoint.md#the-telemetry-routes) |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (the default) or `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers sent with every export, which is where a collector's credential travels |
| `OTEL_EXPORTER_OTLP_TIMEOUT` | The per-export timeout |
| `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | The resource identity the records carry, except the version and the revision [above](#the-build-every-record-names) |

The variable has to be an **environment variable**, not a configuration key. That is deliberate, and
[host startup telemetry](host-startup-telemetry.md) records why: the bootstrap pipeline that reports startup failures
is built before configuration exists, reads the same variable, and exports each record synchronously — so the decision
to export and the destination being exported to can never disagree between the two pipelines, and a start that fails
while configuration is loading is still reported to the same place as everything else.

Writing any `OTEL_*` name into `appsettings.json`, a provisioned configuration file, the persisted configuration
document, or a command-line argument fails
startup naming it, rather than leaving a deployment exporting to nowhere while its own file says otherwise;
[environment-only settings](configuration-reference.md#environment-only-settings) states that rule and the two other
families it covers.

## Local development: the Aspire dashboard

The AppHost orchestration is where the switch is flipped for you. When `backend/src/AppHost` starts a project resource, Aspire
injects `OTEL_EXPORTER_OTLP_ENDPOINT` — together with the authentication header its dashboard expects — pointing at
the dashboard's own OTLP ingestion endpoint. The host needs no telemetry configuration at all:

```bash
dotnet run --project backend/src/AppHost/AppHost.csproj
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

Neither the Compose deployment nor the Helm chart sets any `OTEL_*` variable, so neither exports anything and neither
serves [the client endpoint's OTLP routes](client-endpoint.md#the-telemetry-routes). That is a privacy default, not a gap:
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
exporter sends to a collector. [The `Logging` section](configuration-runtime.md#logging) states those keys, together
with the one asymmetry worth planning for: the startup records are written before that configuration exists and keep
the default format whatever it selects.
