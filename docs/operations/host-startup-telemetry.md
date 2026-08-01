# Host startup telemetry

The host reports its own process lifetime through a bootstrap logging pipeline that `src/Host/Program.cs` owns directly. It is built before `WebApplication.CreateBuilder` runs and released when the process leaves.

## Why the host needs a second pipeline

`AddServiceDefaults` registers OpenTelemetry logging into the dependency-injection container, so that pipeline exists only after `builder.Build()` has returned. Two gaps follow from that, and both are worst exactly when an operator needs the record most:

- Everything before `builder.Build()` — configuration loading, options binding, secret-resolution registration — runs with no logger in scope at all. `CreateBuilder` is where a malformed `appsettings.json` or a failing configuration provider throws, and it is the very first statement of the program.
- A failure during startup, such as an options binding rejected by `ErrorOnUnknownConfiguration` or an unresolvable secret reference rejected by `SecretConfigurationStartupValidator`, escapes `app.RunAsync()` as an unhandled exception. Nothing disposes the application on that path, the OTLP exporter the container owns batches records for its default five seconds, and the runtime does not guarantee that `finally` blocks run for an unhandled exception. The process dies with the explanation still in a queue.

The bootstrap pipeline closes both gaps: it is composed before `CreateBuilder`, so configuration loading is inside the window it reports on, and it exports each record synchronously, so delivery does not depend on process teardown.

## What it emits

Four records, all under the category `MailFathom.Host.Startup`:

| Record | Level | Named properties |
| --- | --- | --- |
| Host is starting | `Information` | `ServiceName`, `EnvironmentName`, `ServiceVersion`, `ServiceRevision` |
| Host layered provisioned configuration files | `Information` | `ServiceName`, `FileCount` |
| Host ended with an unhandled exception | `Critical` | `ServiceName`, plus the exception |
| Host stopped | `Information` | `ServiceName` |

The configuration record is a count and never a path, and it is written here rather than through the container pipeline for the same reason the others are: it describes what the host read before a container existed to log it. A `0` against a deployment that mounts a ConfigMap is how an empty or misplaced mount becomes visible; [configuration sources](configuration-sources.md) states what is counted.

`ServiceVersion` and `ServiceRevision` are read from the host assembly's own build-time metadata and are not configurable, so a deployment cannot make the process claim a build it is not running. They answer different questions and are therefore reported apart: the version is the compatibility statement [ADR 0004](../decisions/0004-versioning-and-release-policy.md) defines over MailFathom's four public surfaces, and the revision is the commit the assemblies were built from, which is what makes a report from a deployment the reader did not build reproducible. A build inside a Git worktree resolves that revision on its own; one with no repository beside it, such as the container build, carries whatever its caller supplied and reports `unknown` otherwise, which is a legitimate state rather than a fault. The same version, without the revision, is what the MCP surface reports to a client during `initialize`.

Those properties are the whole payload the host composes. Nothing reads a configuration value, a connection string, an account, or secret material into a startup record, and the failure record carries the exception as structured exception data rather than interpolated into its message text.

The exception itself is the one part not under this pipeline's control. A component that puts a configuration value into its own exception message — a connection string a driver echoes back, a path a file reader names — publishes it wherever that exception is reported, here as much as through the container pipeline. Treat startup telemetry as carrying whatever the host's failure modes put into their exception messages, and keep sensitive values out of exception text at the point they are thrown.

The failure record is written from a `catch` that rethrows the exception unchanged, so the runtime still writes its own stack trace to standard error and the process exit code is the one an unhandled failure produces. A failed start is therefore reported twice, once by the host's own logger through the container pipeline — which may not survive the crash — and once here, which will. The duplicate is deliberate: a repeated record costs a line, a missing one costs a diagnosis.

## Destinations and configuration

Every setting comes from an environment variable. The pipeline reads no `Logging` section and no `IConfiguration` at all, because configuration does not exist yet when it is built and its failure to load is one of the things it exists to report. The minimum level is fixed at `Information`.

That is a real narrowing, not a technicality: an endpoint set **only** in `appsettings.json` or on the command line configures the container pipeline but not this one, so startup records reach the console and nothing else. The environment is also the only source the standalone OTLP exporter reads, so binding the decision to it keeps the choice to export and the endpoint being exported to from ever disagreeing. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in the environment — which is what Aspire, systemd units, and container runtimes do anyway — to have startup records follow the same path as the rest.

- **Console** is always attached, so a native process under systemd and a container under a log driver both show the records without a collector.
- **OTLP** is attached only when the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is set, the same condition `ServiceDefaultsExtensions` applies to the container pipeline. The exporter reads `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL`, and `OTEL_EXPORTER_OTLP_TIMEOUT` itself, so an orchestrator such as Aspire needs no additional configuration for the startup records to arrive alongside the rest.

The resource is whatever the OpenTelemetry SDK resolves on its own — `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` when they are set, and the SDK's `unknown_service:{processName}` fallback when they are not. No `AddService` call overrides it, which is the only arrangement under which bootstrap records and container-pipeline records cannot disagree: `ServiceDefaultsExtensions` does not name the service either, so naming it here would agree with the rest of the process only while `OTEL_SERVICE_NAME` happened to be set, and would otherwise report one process under two identities.

The application name and version are therefore properties of the startup records rather than resource attributes. That is where they are useful in any case, including on a console with no resource attached at all.

Records go through a simple export processor rather than the default batching one. Startup emits a handful of records per process, so the throughput the batch processor exists to protect is irrelevant here, while its scheduled delay is precisely the failure mode being avoided.
