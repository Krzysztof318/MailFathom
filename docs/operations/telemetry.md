# Telemetry and the Aspire dashboard

<!-- describes: src/Common/Observability/**, src/Host/Observability/**, src/Host/ServiceDefaultsExtensions.cs, src/Infrastructure/Observability/**, src/AppHost/** -->

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

**Metrics** cover the request pipeline (ASP.NET Core), outbound HTTP (`HttpClient`), the .NET runtime, PostgreSQL
through the Npgsql instrumentation, and the `Polly` meter, which is where every outbound-resilience pipeline reports
its attempts, outcomes, timeouts, and circuit-breaker state transitions.
[Outbound resilience](../architecture/outbound-resilience.md#telemetry-and-privacy) records which tags those events
carry and which they never do.

**Traces** cover incoming requests, outbound HTTP, and database commands, correlated end to end: the trace a request
arrives with is the trace its log records and its failure diagnostics carry. One filter is deliberate: requests to the
health-probe paths are not traced at all, because a probe arrives every few seconds for the life of the process and
says the same thing every time — tracing it would fill a trace store with polling instead of work.

## What MailFathom publishes under its own name

Everything above arrives from a library. MailFathom also owns a set of names of its own, one per subsystem, and the
same name serves as both an activity source and a meter — the two are separate registries to OpenTelemetry and cannot
collide, so a subsystem that publishes spans and instruments does so under one string rather than two that could drift
apart:

| Name | The subsystem it describes |
| --- | --- |
| `MailFathom.Mail` | Mailbox work: IMAP sessions, folder reconciliation, synchronization runs, and mutations |
| `MailFathom.Mcp` | The MCP surface: tool calls and the protocol boundary that serves them |
| `MailFathom.Persistence` | Local storage: the email content store and the write sessions around it |
| `MailFathom.Extraction` | Mail text extraction and the backfill that reprocesses what earlier runs left |

The name is decided by the subsystem, never by the feature that happens to emit first and never by the assembly the
code sits in — that is what an operator filters a dashboard on, and what survives a type moving between projects. The
shared `MailFathom.` prefix is what lets one filter select everything this process owns and nothing a library emits.
All four are subscribed by the host together, from one declaration, so a name that exists without being collected is a
failing test rather than a silently empty stream. Instruments that are collected in aggregate come from the meter
factory the owning service is given, rather than from a process-wide instance, which is what keeps them observable in a
test.

The names are the contract; what publishes to each one is documented with the subsystem that does it. The first-party
spans and instruments are being added subsystem by subsystem, so a dashboard filtered to `MailFathom.` shows only what
has been instrumented so far, and three of the four names are still quiet.

`MailFathom.Mail` is the one that carries something today: every change MailFathom makes to a remote mailbox opens a
span named after the mutation, and is counted along with how long it took, broken down by the mutation, the account,
the folder alias, and whether it succeeded. It is deliberately **not** broken down by which IMAP commands carried the
change — a relocation is one operation whether the server offered RFC 6851 `MOVE` or the copy-flag-expunge sequence was
used instead, and a dimension telling the two apart is exactly what would make a missing server extension look like a
different operation on a dashboard. Which path ran is in the debug log.

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
