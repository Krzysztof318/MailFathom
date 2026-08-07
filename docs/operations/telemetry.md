# Telemetry and the Aspire dashboard

<!-- describes: src/Common/Observability/**, src/Host/Observability/**, src/Host/ServiceDefaultsExtensions.cs, src/Infrastructure/Observability/**, src/Infrastructure/HostApplicationBuilderExtensions.cs, src/AppHost/** -->

The host instruments itself with OpenTelemetry throughout — logs, metrics, and traces — and exports none of it unless
the environment names a destination. Today exactly one environment does that out of the box: a local run under the
Aspire orchestration, whose dashboard is the destination. This page records what is emitted, the one switch that
decides whether it leaves the process, and why the deployments deliberately ship with that switch off.

## What the process emits

**Logs** go through the OpenTelemetry logging provider with formatted messages and scopes included, beside the console
output that is always on. Log lines are structured with named properties, and by contract they never carry
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
`DiagnosticSource` and would need a bridging package to span the same commands a second time.

One filter is deliberate: requests to the health-probe paths are not traced at all, because a probe arrives every few
seconds for the life of the process and says the same thing every time — tracing it would fill a trace store with
polling instead of work.

Every tag on the metrics above is a bounded set — a protocol method, a transport kind, a negotiated version, one of the
three tool names, an outcome — so none of them opens a time series per message or per person. The MCP SDK does tag a
metric with a resource URI, but only for the protocol's resource methods, and MailFathom's server publishes tools
alone: no resources and no prompts, so the tag never arises.

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

What publishes to that name is documented with the subsystem that does it, and today two subsystems do.

Every change MailFathom makes to a remote mailbox opens a span named after the mutation, and is counted along with how
long it took, broken down by the mutation, the account, the folder alias, and whether it succeeded. It is deliberately
**not** broken down by which IMAP commands carried the change — a relocation is one operation whether the server
offered RFC 6851 `MOVE` or the copy-flag-expunge sequence was used instead, and a dimension telling the two apart is
exactly what would make a missing server extension look like a different operation on a dashboard. Which path ran is in
the debug log.

Embedding publishes the depth of its backlog, how many messages the bound turned away, and how many messages and
passages it embedded and how long that took, broken down by outcome and by the classification of a provider failure.
[Automatic embedding](../features/automatic-embedding.md#what-an-operator-can-see) names each instrument and what it
answers; the depth is the one an instance falling behind shows up in first.

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
| `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | The resource identity the records carry |

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
