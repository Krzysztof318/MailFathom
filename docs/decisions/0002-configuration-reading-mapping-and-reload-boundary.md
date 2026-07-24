---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-07-24
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Use an application-owned configuration access layer for reading, mapping, and reloadable business settings

## Context and Problem Statement

MailMcp will need configuration for mail accounts, synchronization limits, security policy, storage adapters, AI providers, MCP endpoints, operational guardrails, and future governance controls. .NET configuration providers and the Options pattern are useful host-level mechanisms, but reading raw `IConfiguration` or injecting provider-shaped options directly into use cases would couple business behavior to transport keys, mutable provider state, binder constraints, and reload timing.

The decision question is: should MailMcp read configuration directly from `IConfiguration` or `IOptions*` throughout the codebase, or should it introduce an intermediate configuration access layer that maps raw/options-shaped configuration into application-owned business settings and publishes safe automatic reload updates? This ADR is intentionally limited to the first-stage read path: source validation, options binding, mapping, validation, and reload behavior. It also records the intended future direction for programmatic configuration modification so the read model does not block a later write model, but it does not yet decide how configuration is stored, including whether future sources are files, a database-backed provider, a cloud configuration store, or another managed configuration service.

## Decision Drivers

- Keep `Domain` independent from configuration frameworks, providers, file formats, environment variable conventions, cloud configuration services, and reload mechanics.
- Keep `Application` dependent on stable business contracts rather than raw string keys, nullable provider values, binder-friendly DTOs, or host-specific `IOptions*` lifetimes.
- Preserve startup validation and fail-fast behavior for unsafe or invalid source configuration while allowing selected runtime settings to reload without process restart.
- Separate source-configuration validation from business-settings validation so provider shape, allowed keys, precedence, and secret references are checked before use-case code sees the values.
- Leave a clear path for future programmatic configuration modification without making the first implementation responsible for writes, persistence, approval workflows, or history.
- Make reload behavior explicit, observable, thread-safe, and privacy-safe for long-running synchronization, retrieval, SMTP delivery, AI indexing, and MCP request handling.
- Avoid using reloadable configuration for values that require durable workflow state, audit evidence, migration, explicit approval, or per-tenant administration.
- Keep first implementation small and compatible with the existing clean-architecture modular monolith.

## Considered Options

- Read raw `IConfiguration` at every consumer.
- Inject framework options such as `IOptions<T>`, `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>` directly into application use cases and adapters.
- Introduce an application-owned configuration access layer backed by host options and reload notifications.
- Build a full configuration management subsystem with persistence, APIs, versioning, and audit workflow now.

## Decision Outcome

Chosen option: "Introduce an application-owned configuration access layer backed by host options and reload notifications", because it keeps .NET configuration infrastructure at the host/infrastructure boundary while giving application code stable, validated, domain-meaningful configuration objects with explicit reload semantics.

The decision is proposed, not accepted. The first implementation should cover only source validation, reading, mapping, and automatic reload for runtime settings that are safe to update in-process. Programmatic modification is a target capability for a later phase, but configuration storage itself remains undecided: files, a database-backed provider, a cloud configuration store, another managed configuration service, administrative editing, approval workflow, configuration history, and multi-tenant configuration lifecycle are deferred decisions.

### Consequences

- Good, because use cases consume business settings such as synchronization limits, retrieval safety policy, or provider capability flags rather than raw keys or binder DTOs.
- Good, because framework-specific details such as provider precedence, `reloadOnChange`, options validation, options caches, and change tokens stay behind adapter code.
- Good, because source validation and reload handling can centralize allowed-key checks, precedence expectations, last-known-good behavior, structured audit events, and privacy-safe logging instead of duplicating those concerns in every service.
- Neutral, because configuration DTOs and business settings will both exist and require explicit mapping tests.
- Bad, because the layer adds code and design discipline before MailMcp has many configurable behaviors.
- Bad, because reloadable or later writable configuration can create operational surprises unless every setting is classified by source, mutability, reload scope, and consistency requirements.

## Decision Details

### Boundary placement

Host composition code owns provider setup and raw binding. It may use `IConfiguration`, `AddOptions<TOptions>()`, `Bind`, `ValidateDataAnnotations`, custom validators, `ValidateOnStart`, `IOptionsMonitor<TOptions>`, and provider-specific reload features.

Application-facing configuration contracts belong in `Application` when use cases need them. They must use business names and value objects, not provider section names or serialization constraints. `Domain` should receive configuration values only as method parameters or constructor arguments on domain services/value objects when those values are part of a domain invariant; it must not depend on Microsoft configuration or options abstractions.

Infrastructure or Host adapters map provider/options DTOs to application-owned immutable settings. The mapping adapter is also the boundary where invalid raw values become safe startup failures or expected application errors.

### Contract style

Prefer focused reader interfaces over a generic configuration service. Example shapes are `ISynchronizationSettingsReader`, `IRetrievalSafetyPolicyReader`, `IAiProviderSettingsReader`, and `IMcpEndpointSettingsReader` when those settings become real use-case needs.

A reader should return immutable business settings or a snapshot reference. It should document whether values are captured per operation, refreshed between operations, or observed during long-running work. Avoid returning `IConfigurationSection`, options DTOs, mutable collections, raw dictionaries, or provider-specific objects.

Configuration DTOs used for binding may remain mutable and binder-friendly. Business settings should be immutable records or value objects with validation that expresses business meaning, such as maximum page sizes, explicit TLS requirements, bounded concurrency limits, timeout ranges, and whether a setting may contain sensitive data.

### Source configuration validation

The source configuration model is a separate contract from business settings. It describes the shape accepted from files, environment variables, command-line arguments, secret references, deployment systems, and future provider adapters. Source validation must run before mapping and should reject configuration that is structurally unsafe even if a later business default could hide the mistake.

Source validation should include these checks when a setting group is introduced:

- Required sections and keys are present for the selected feature mode.
- Unknown keys are rejected for security-sensitive sections unless a documented compatibility reason allows them.
- Raw values convert cleanly to the expected primitive types, units, enum names, URI forms, durations, sizes, and limits.
- Provider precedence is documented where the same key can come from files, environment variables, command-line arguments, or future managed providers.
- Secret-bearing values are represented as approved secret references or host-bound secret values, never as source-controlled plaintext.
- Cross-field constraints that belong to source shape are validated before mapping, such as requiring an endpoint when a provider mode is enabled.

A source-validation failure during startup must fail the host before workers, MCP endpoints, or background pipelines begin. A source-validation failure during reload must reject the candidate source snapshot and keep the active business snapshot unchanged unless that setting group's contract explicitly defines a safer shutdown or degraded-mode behavior.

Source validators should report stable machine-readable codes, setting paths, and safe messages. Diagnostics must avoid raw values for credentials, tokens, certificate material, email addresses, message content, raw MIME, embedding text, and provider responses.

### Reload policy

Reload is opt-in per setting group. Each group must be classified before implementation:

- Restart-required: values that affect dependency graph shape, database/provider selection, credentials, certificate trust anchors, schema assumptions, or security posture in ways that cannot be safely swapped.
- Reloadable for new operations: values that can be applied when a new synchronization pass, MCP request, indexing batch, or SMTP delivery attempt starts.
- Reloadable during running operations: values that can be safely observed mid-operation without violating consistency, cancellation, retry, or audit guarantees.

The initial default should be restart-required unless a setting has a concrete use case for automatic reload. For reloadable settings, the adapter should validate the candidate source configuration, bind provider/options DTOs, map them to business settings, publish an atomic snapshot, and retain the last known good snapshot if source validation, binding, or mapping fails. Failed reloads must be logged with safe setting names and error codes, never with credentials, message content, raw MIME, tokens, or provider responses.

### Validation and failure behavior

Startup validation remains mandatory for required configuration. Invalid required configuration should fail host startup before workers or endpoints run.

Reload validation must not crash the process by default. A rejected reload should leave the previous valid business settings active and emit privacy-safe operational evidence. Settings whose invalid reload must stop processing, such as an unsafe TLS downgrade or removed required endpoint policy, need an explicit safety behavior in that setting group's contract.

Validation should happen in layers:

1. Source validation checks the accepted external shape: sections, known keys, provider precedence expectations, secret references, primitive conversions, and feature-mode requirements.
2. Provider/options validation checks binder DTO requirements and obvious missing values with `ValidateDataAnnotations`, custom `IValidateOptions<TOptions>`, `ValidateOnStart`, and strict binder options where appropriate.
3. Mapping validation translates raw/options values into business value objects and rejects unsafe combinations.
4. Use-case validation enforces operation-specific invariants at execution time.

### Future programmatic modification

MailMcp should eventually support controlled configuration modification by program code, but that capability is deliberately not part of the first read-focused implementation. The storage question for those writes is also deliberately postponed; a later ADR must decide whether the writable source is file-based, database-backed, a dedicated configuration store, a cloud configuration service, or another managed provider. The read layer should therefore expose business settings through contracts that can later be backed by a validated writable source without changing application use cases.

The target write model should be command-oriented, not raw key mutation. Future APIs should accept intent-specific commands such as enabling a provider, changing synchronization limits, rotating a secret reference, or updating a tenant override. Each command should validate the proposed source configuration, map it to business settings, check authorization and policy, record audit evidence, and publish the change through the same reload path used by external provider changes.

Programmatic writes must not let arbitrary code mutate `IConfigurationRoot`, provider dictionaries, JSON files, environment variables, or options caches directly. They should go through an application-owned configuration writer port only after a separate ADR decides storage form, storage ownership, concurrency, rollback, versioning, approval, secret handling, tenant scoping, and audit retention. Until that ADR exists, production code should treat configuration as read-only.

### Scope exclusions

This ADR does not choose how configuration is stored. File-based configuration, database tables, a custom provider, a cloud or service-backed configuration store, a secret store, admin API, UI, tenant override model, approval workflow, configuration versioning model, and a concrete configuration writer API are all deferred to later decisions.

Secrets remain outside ordinary source-controlled configuration. The configuration access layer may reference secret identifiers or consume already-bound secret values at the host boundary, but it must not normalize broad secret access into application code or log secret material.

This ADR also does not permit adding new third-party packages. Any future provider package, hosted configuration service, or secret-store integration requires separate official documentation, license, service-terms, telemetry, and data-processing review, plus `LICENSES.md` updates when applicable.

## Validation

- Code review verifies that `Domain` never references `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`, provider SDK types, configuration section names, or raw setting keys.
- Code review verifies that `Application` use cases depend on focused configuration reader contracts or immutable settings, not raw `IConfiguration` or framework options types.
- Unit tests cover source validation, strict binding behavior where configured, mapping from options DTOs to business settings, startup validation failures, reload success, rejected reload with last-known-good preservation, and privacy-safe diagnostics for invalid reloads.
- Documentation for each setting group states its source shape, required keys, safe diagnostics, mutability classification, and whether it is restart-required, reloadable for new operations, or reloadable during running operations.
- Pull-request review rejects reloadable settings without an explicit consistency, security, privacy, and operational rationale, and rejects programmatic configuration mutation until the write-side ADR is accepted.

## Pros and Cons of the Options

### Read raw `IConfiguration` at every consumer

Consumers read configuration values by key from Microsoft configuration abstractions whenever they need a setting.

- Good, because it is simple for prototypes and avoids extra mapping classes.
- Good, because it can access provider precedence and current raw values directly.
- Neutral, because it can remain acceptable inside host composition code and narrowly scoped infrastructure adapters.
- Bad, because string keys, section layout, null/default handling, provider precedence, and reload behavior leak across the codebase.
- Bad, because it is hard to document which settings are sensitive, reloadable, bounded, or safe to change during an operation.
- Bad, because use cases can accidentally bypass source validation and interpret unknown, missing, malformed, or precedence-overridden values differently.

### Inject framework options directly into use cases and adapters

Consumers inject `IOptions<T>`, `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>` and use bound option DTOs as their configuration model.

- Good, because this uses standard .NET dependency injection and options validation mechanisms.
- Good, because `IOptionsMonitor<T>` supports singleton consumers and change notifications, while `IOptionsSnapshot<T>` can provide scoped recomputation.
- Neutral, because framework options are appropriate for host and adapter internals that already depend on Microsoft extensions.
- Bad, because options DTOs are often shaped for binding rather than business meaning, especially where the binder needs mutable properties or provider-friendly keys.
- Bad, because consumers must understand the semantic differences between singleton options, scoped snapshots, monitor current values, monitor caches, and change callbacks.
- Bad, because direct monitor use can let different parts of one operation observe different values unless the operation captures a business snapshot deliberately.
- Bad, because future programmatic writes would be tempted to manipulate option caches or provider-specific data structures instead of using audited intent-specific commands.

### Introduce an application-owned configuration access layer backed by host options and reload notifications

Host/infrastructure code binds and validates provider-shaped options, maps them into immutable business settings, and exposes focused application-facing readers or snapshots. Reloadable groups are updated through validated atomic snapshots.

- Good, because it preserves clean architecture and keeps Microsoft configuration mechanisms outside the domain model and ordinary use-case logic.
- Good, because mapping creates one place to translate provider strings, defaults, units, ranges, sensitive-value handling, source-validation results, and reload classification into business semantics.
- Good, because last-known-good reload behavior and safe operational diagnostics can be implemented once per setting group.
- Neutral, because it duplicates some shape between options DTOs and business settings.
- Bad, because incorrect classification of reloadability can still cause inconsistent runtime behavior.
- Bad, because this layer must stay narrow; a generic settings service or premature writer API would recreate raw configuration coupling under a different name.

### Build a full configuration management subsystem now

MailMcp would immediately add durable storage, administrative APIs, versioning, audit history, tenant overrides, approval workflows, and possibly an external configuration service.

- Good, because future enterprise governance, auditability, and multi-tenant operations will likely need some of these capabilities.
- Good, because explicit versioning and approvals can make configuration changes safer than ad hoc file edits.
- Neutral, because this may become necessary after core mail, MCP, AI, and operational requirements are better known.
- Bad, because it exceeds the current decision scope, which is validating, reading, and mapping configuration safely.
- Bad, because it would force premature choices about storage, tenancy, authorization, write concurrency, rollback, secret handling, audit retention, and service dependencies.
- Bad, because adding provider packages or hosted services now would trigger licensing, terms, telemetry, and data-processing review before there is a proven need.

## More Information

- Microsoft Learn, ".NET configuration," describes hierarchical keys, provider ordering where later providers override earlier providers, binder limitations for read-only collection interfaces, support for constructor binding, and notes that custom mapping can translate safe keys into desired business keys: <https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration>.
- Microsoft Learn, `BinderOptions.ErrorOnUnknownConfiguration`, documents strict binding behavior for unknown keys and conversion failures: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.binderoptions.erroronunknownconfiguration>.
- Microsoft Learn, "Configuration providers in .NET," documents provider setup and `reloadOnChange` for JSON/XML file providers: <https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers>.
- Microsoft Learn, "Options pattern in ASP.NET Core," documents `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`, named options, options validation, and that validation runs again when options are reloaded: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0>.
- Microsoft Learn, "Options pattern in .NET," documents options validation, `IValidateOptions<TOptions>`, and `AddOptionsWithValidateOnStart<TOptions>` / `ValidateOnStart` startup validation: <https://learn.microsoft.com/en-us/dotnet/core/extensions/options>.
- Microsoft Learn, "Detect changes with change tokens in ASP.NET Core," describes configuration reload change tokens and file-provider reload behavior: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens?view=aspnetcore-10.0>.
- Microsoft Learn, "Implement a custom configuration provider in .NET," documents custom providers backed by a database, which is relevant background for a future storage-backed read/write provider but is not adopted by this ADR: <https://learn.microsoft.com/en-us/dotnet/core/extensions/custom-configuration-provider>.
- This ADR refines the MailMcp architecture rule that `Host` owns configuration and dependency injection, while `Application` and `Domain` remain independent of infrastructure frameworks: `specs/2026-07-22-mail-mcp-architecture-draft.md`.
