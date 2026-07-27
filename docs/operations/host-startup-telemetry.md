# Host startup telemetry

The host reports its own process lifetime through a bootstrap logging pipeline that `src/Host/Program.cs` owns directly. It is built before host composition begins and released when the process leaves.

## Why the host needs a second pipeline

`AddServiceDefaults` registers OpenTelemetry logging into the dependency-injection container, so that pipeline exists only after `builder.Build()` has returned. Two gaps follow from that, and both are worst exactly when an operator needs the record most:

- Everything before `builder.Build()` — configuration loading, options binding, secret-resolution registration — runs with no logger in scope at all.
- A failure during startup, such as an options binding rejected by `ErrorOnUnknownConfiguration` or an unresolvable secret reference rejected by `SecretConfigurationStartupValidator`, escapes `app.RunAsync()` as an unhandled exception. Nothing disposes the application on that path, the OTLP exporter the container owns batches records for its default five seconds, and the runtime does not guarantee that `finally` blocks run for an unhandled exception. The process dies with the explanation still in a queue.

The bootstrap pipeline closes both gaps: it exists before the container and it exports each record synchronously, so delivery does not depend on process teardown.

## What it emits

Three records, all under the category `MailMcp.Host.Startup`:

| Record | Level | Named properties |
| --- | --- | --- |
| Host is starting | `Information` | `ServiceName`, `EnvironmentName`, `ServiceVersion` |
| Host ended with an unhandled exception | `Critical` | `ServiceName`, plus the exception |
| Host stopped | `Information` | `ServiceName` |

Those properties are the whole payload the host composes. Nothing reads a configuration value, a connection string, an account, or secret material into a startup record, and the failure record carries the exception as structured exception data rather than interpolated into its message text.

The exception itself is the one part not under this pipeline's control. A component that puts a configuration value into its own exception message — a connection string a driver echoes back, a path a file reader names — publishes it wherever that exception is reported, here as much as through the container pipeline. Treat startup telemetry as carrying whatever the host's failure modes put into their exception messages, and keep sensitive values out of exception text at the point they are thrown.

The failure record is written from a `catch` that rethrows the exception unchanged, so the runtime still writes its own stack trace to standard error and the process exit code is the one an unhandled failure produces. A failed start is therefore reported twice, once by the host's own logger through the container pipeline — which may not survive the crash — and once here, which will. The duplicate is deliberate: a repeated record costs a line, a missing one costs a diagnosis.

## Destinations and configuration

The pipeline reads no `Logging` configuration section. Configuration that fails to load or bind is one of the failures it exists to report, so it cannot depend on it; the minimum level is fixed at `Information`.

- **Console** is always attached, so a native process under systemd and a container under a log driver both show the records without a collector.
- **OTLP** is attached only when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, the same rule `ServiceDefaultsExtensions` applies to the container pipeline. The exporter reads `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL`, and `OTEL_EXPORTER_OTLP_TIMEOUT` itself, so an orchestrator such as Aspire needs no additional configuration for the startup records to arrive alongside the rest.

The resource reports `service.name` from `OTEL_SERVICE_NAME` when it is set and from `IHostEnvironment.ApplicationName` otherwise. Preferring the configured name keeps bootstrap records and container-pipeline records under one service; taking the assembly name instead would split a single process into two services in the collector. `service.version` is the host assembly's informational version with source-control build metadata removed.

Records go through a simple export processor rather than the default batching one. Startup emits three records per process, so the throughput the batch processor exists to protect is irrelevant here, while its scheduled delay is precisely the failure mode being avoided.
