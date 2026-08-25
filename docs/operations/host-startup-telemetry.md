# Host startup telemetry

<!-- describes: backend/src/Host/Program.cs, backend/src/Host/HostComposition.cs, backend/src/Host/Observability/** -->

The host reports its own process lifetime through a bootstrap logging pipeline that `backend/src/Host/Program.cs` owns directly. It is built before `WebApplication.CreateBuilder` runs and released when the process leaves.

## Why the host needs a second pipeline

`AddServiceDefaults` registers OpenTelemetry logging into the dependency-injection container, so that pipeline exists only after `builder.Build()` has returned. Two gaps follow from that, and both are worst exactly when an operator needs the record most:

- Everything before `builder.Build()` runs with no logger in scope at all: configuration loading, which `Program.cs` does itself before it composes anything, and then the whole of `HostComposition.Compose`, where options binding and secret-resolution registration happen. `CreateBuilder` is the first statement that can throw from configuration — a malformed `appsettings.json` or a failing configuration provider fails there — and the only statements ahead of it are the two that compose this pipeline.
- A failure during startup, such as an options binding rejected by `ErrorOnUnknownConfiguration` or an unresolvable secret reference rejected by `SecretConfigurationStartupValidator`, escapes `app.RunAsync()` as an unhandled exception. Nothing disposes the application on that path, the OTLP exporter the container owns batches records for its default five seconds, and the runtime does not guarantee that `finally` blocks run for an unhandled exception. The process dies with the explanation still in a queue.

The bootstrap pipeline closes both gaps: it is composed before `CreateBuilder`, so configuration loading is inside the window it reports on, and it exports each record synchronously, so delivery does not depend on process teardown.

## What it emits

Five records, all under the category `MailFathom.Host.Startup`:

| Record | Level | Named properties |
| --- | --- | --- |
| Host is starting | `Information` | `ServiceName`, `EnvironmentName`, `ServiceVersion`, `ServiceRevision` |
| Host layered provisioned configuration files | `Information` | `ServiceName`, `FileCount` |
| Host composed its settings over a persisted configuration version | `Information` | `ServiceName`, `Version` |
| Host ended with an unhandled exception | `Critical` | `ServiceName`, plus the exception |
| Host stopped | `Information` | `ServiceName` |

The persisted-configuration record is a version number and never a key or a value. It is the only record of which document the process actually composed itself over: the files are in the repository and the environment is in the manifest, and what the database held at that moment is otherwise unrecoverable from the running process. [Configuration sources](configuration-sources.md) states what the layer is.

The provisioned-file record is a count and never a path, and it is written here rather than through the container pipeline for the same reason the others are: it describes what the host read before a container existed to log it. A `0` against a deployment that mounts a ConfigMap is how an empty or misplaced mount becomes visible; [configuration sources](configuration-sources.md) states what is counted.

`ServiceVersion` and `ServiceRevision` are read from the host assembly's own build-time metadata and are not configurable, so a deployment cannot make the process claim a build it is not running. They answer different questions and are therefore reported apart: the version is the compatibility statement [ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md) defines over MailFathom's four public surfaces, and the revision is the commit the assemblies were built from, which is what makes a report from a deployment the reader did not build reproducible. A build inside a Git worktree resolves that revision on its own; one with no repository beside it, such as the container build, carries whatever its caller supplied and reports `unknown` otherwise, which is a legitimate state rather than a fault. `ServiceRevision` is reported as the first seven characters of the object name, the same abbreviation the nightly identifier carries, and the whole name stays on the image's `org.opencontainers.image.revision` label. The same version, without the revision, is what the MCP surface reports to a client during `initialize`, and both properties are what every exported record carries on its resource as `service.version` and `vcs.ref.head.revision`.

Those properties are the whole payload the host composes. Nothing reads a configuration value, a connection string, an account, or secret material into a startup record, and the failure record carries the exception as structured exception data rather than interpolated into its message text.

The exception itself is the one part not under this pipeline's control. A component that puts a configuration value into its own exception message — a connection string a driver echoes back, a path a file reader names — publishes it wherever that exception is reported, here as much as through the container pipeline. Treat startup telemetry as carrying whatever the host's failure modes put into their exception messages, and keep sensitive values out of exception text at the point they are thrown.

The failure record is written from a `catch` that rethrows the exception unchanged, so the runtime still writes its own stack trace to standard error and the process exit code is the one an unhandled failure produces. A failed start is therefore reported twice, once by the host's own logger through the container pipeline — which may not survive the crash — and once here, which will. The duplicate is deliberate: a repeated record costs a line, a missing one costs a diagnosis.

## Destinations and configuration

Every setting comes from an environment variable. The pipeline reads no `Logging` section and no `IConfiguration` at all, because configuration does not exist yet when it is built and its failure to load is one of the things it exists to report. The minimum level is fixed at `Information`.

That is a real narrowing, not a technicality: an endpoint set **only** in `appsettings.json` or on the command line would configure the container pipeline but not this one. Rather than leave that divergence to be discovered through missing records, the host refuses it — an `OTEL_*` value that did not come from the process environment fails startup naming the variable, which [environment-only settings](configuration-reference.md#environment-only-settings) states in full. The environment is also the only source the standalone OTLP exporter reads, so binding the decision to it keeps the choice to export and the endpoint being exported to from ever disagreeing. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in the environment — which is what Aspire, systemd units, and container runtimes do anyway — to have startup records follow the same path as the rest.

That refusal is reported by this pipeline, and reaches the console alone by construction: the pipeline was composed from the environment several steps before the configuration carrying the misplaced value existed, so a deployment whose only mistake was writing the endpoint into a file has no collector attached to hear about it. Read the `Critical` record on the container's or the unit's own output.

- **Console** is always attached, so a native process under systemd and a container under a log driver both show the records without a collector. It is this pipeline's own console rather than the one the container pipeline composes, which is where the narrowing above stops being an internal detail: a deployment that selects `json` under `Logging:Console` so that a log shipper can parse the stream still receives these four records in the default format, the `Critical` one explaining a failed start among them. [The `Logging` section](configuration-runtime.md#logging) states the keys; what they cannot reach is this pipeline.
- **OTLP** is attached only when the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is set, the same condition `ServiceDefaultsExtensions` applies to the container pipeline. The exporter reads `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL`, and `OTEL_EXPORTER_OTLP_TIMEOUT` itself, so an orchestrator such as Aspire needs no additional configuration for the startup records to arrive alongside the rest.

The service identity on the resource is whatever the OpenTelemetry SDK resolves on its own — `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` when they are set, and the SDK's `unknown_service:{processName}` fallback when they are not. No `AddService` call overrides it, which is the only arrangement under which bootstrap records and container-pipeline records cannot disagree: `ServiceDefaultsExtensions` does not name the service either, so naming it here would agree with the rest of the process only while `OTEL_SERVICE_NAME` happened to be set, and would otherwise report one process under two identities.

Two attributes are added on top of what the SDK resolved, and for exactly the same reason: `service.version` and `vcs.ref.head.revision`, read from the assembly's own stamp, which is what the container pipeline puts on the resource of everything it exports. Both pipelines therefore name one build, and a startup record and a span from the same process cannot claim different versions or different commits. [The build every record names](telemetry.md#the-build-every-record-names) holds that rule in full, including why either attribute supplied through `OTEL_RESOURCE_ATTRIBUTES` does not override the stamped one.

The application name stays a property of the startup records rather than a resource attribute, which is where it is useful in any case, including on a console with no resource attached at all. The version and the revision are both: properties of these four records and attributes on the resource they are exported with.

Records go through a simple export processor rather than the default batching one. Startup emits a handful of records per process, so the throughput the batch processor exists to protect is irrelevant here, while its scheduled delay is precisely the failure mode being avoided.
