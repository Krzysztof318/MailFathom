---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-07-27
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Use an application-owned configuration access layer for reading, mapping, and reloadable business settings

<!-- describes: backend/src/Host/Configuration/** -->

## Context and Problem Statement

MailFathom will need configuration for mail accounts, synchronization limits, security policy, storage adapters, AI providers, MCP endpoints, operational guardrails, and future governance controls. .NET configuration providers and the Options pattern are useful host-level mechanisms, but reading raw `IConfiguration` or injecting provider-shaped options directly into use cases would couple business behavior to transport keys, mutable provider state, binder constraints, and reload timing.

The decision question is: should MailFathom read configuration directly from `IConfiguration` or `IOptions*` throughout the codebase, or should it introduce an intermediate configuration access layer that maps raw/options-shaped configuration into application-owned business settings and publishes safe automatic reload updates? This ADR covers the read path — source validation, options binding, mapping, validation, and reload behavior — and it settles that there is no write path: configuration is read-only to the running process, and state a program modifies is persisted in PostgreSQL instead. What it leaves open is which read-only sources a deployment composes, including whether a later one is a file, a mounted object, a cloud configuration store, or another managed configuration service.

## Decision Drivers

- Keep `Domain` independent from configuration frameworks, providers, file formats, environment variable conventions, cloud configuration services, and reload mechanics.
- Keep `Application` dependent on stable business contracts rather than raw string keys, nullable provider values, binder-friendly DTOs, or host-specific `IOptions*` lifetimes.
- Preserve startup validation and fail-fast behavior for unsafe or invalid source configuration while allowing selected runtime settings to reload without process restart.
- Separate source-configuration validation from business-settings validation so provider shape, allowed keys, precedence, and secret references are checked before use-case code sees the values.
- Keep configuration read-only to the process, so that the file a deployment provisioned is the one in force, and give state a program modifies a home in PostgreSQL, which already carries migrations, transactions, retention, and history.
- Make reload behavior explicit, observable, thread-safe, and privacy-safe for long-running synchronization, retrieval, SMTP delivery, AI indexing, and MCP request handling.
- Avoid using reloadable configuration for values that require durable workflow state, audit evidence, migration, explicit approval, or per-tenant administration.
- Keep first implementation small and compatible with the existing clean-architecture modular monolith.

## Considered Options

- Read raw `IConfiguration` at every consumer.
- Inject framework options such as `IOptions<T>`, `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>` directly into application use cases and adapters.
- Introduce an application-owned configuration access layer backed by host options and reload notifications.
- Build a full configuration management subsystem with persistence, APIs, versioning, and audit workflow.

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

- Restart-required: values that affect dependency graph shape, database/provider selection, schema assumptions, or security posture in ways that cannot be safely swapped. A credential or certificate trust anchor that is *bound as a value* belongs here, because swapping such a value in place has no validation step before it takes effect. A credential or trust anchor reached through a validated secret reference does not; see [Amendment 1](#amendment-1-referenced-secrets-are-reloadable-for-new-operations).
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

### Configuration is read-only, and the database is where a program writes

**Configuration is read-only to the running process, and no configuration writer port is coming.** Nothing in MailFathom mutates `IConfigurationRoot`, a provider dictionary, a JSON file, an environment variable, or an options cache, and nothing acquires an application-owned port that would do it on a caller's behalf. State a program has to modify is persisted in PostgreSQL instead, which is the whole of the write channel: a design that appears to need a mutable setting is a design deciding which table holds it, never one deciding how to write the operator's file.

Read-only is not the same as static, and the distinction is the reason this is not a restriction on anything above. Everything the read path already promises stays: a validated snapshot is published on reload, a rejected candidate leaves the last known good one in force, and material behind a secret reference is resolved per use, so a rotated password or a re-provisioned certificate reaches the next operation with no restart and no reload. What is refused is one direction of travel — the process writing back to where its own settings came from — rather than change reaching a running process.

The reasoning is what the two stores are respectively good at, and neither answer improves by being given the other's job. Configuration is provisioned from a chart, a unit file, or a mounted object; it is diffable, reviewable before it takes effect, reproducible from a repository, and it makes an instance fully described by what was deployed. Every one of those properties is destroyed the moment the process edits it, because the file on disk stops being the file in force and nothing in the deployment says so. Persistence, meanwhile, already carries what mutable state needs and what a writable configuration source would have had to grow from nothing: a migration chain, transactions, concurrency, retention, erasure, encryption for anything sensitive, and a place a history can live. A configuration writer port would have had to acquire all of it, under an approval and audit model of its own, so that a value could be changed by a call instead of by an edit.

One precedent already reads this way and is the worked example rather than an exception to it. A mailbox refresh token — the first credential the service itself writes — is persisted in a sealed database column under the key ring rather than written back into the secret reference it arrived through, which is the decision [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) records. The shape it shows is the one this rule produces generally: the authored text an operator provisioned stays theirs, and what the program owns lives somewhere the program may write.

Runtime rule authoring is where that shape is asked for next, and this record settles only its own half of it. Issue 771 proposes rules authored while the deployment runs, in a table beside the configured ones and explicitly without rewriting the configuration section they sit next to; issue 761 is its gate and is the owner's decision, over [ADR 0010](0010-rule-authoring-in-configuration-and-ncalc-conditions.md)'s storage half rather than over this one. Whatever it decides, it decides in the database: this section is why the alternative — a program editing the rules file — is not among its options.

Reversing this is a new ADR superseding this one, not a feature added under it.

### Scope exclusions

This ADR does not choose which read-only sources a deployment composes its configuration from. A file, a mounted object, a custom read-only provider, a cloud or service-backed configuration store, and a secret store are all open questions, and `docs/operations/configuration-sources.md` records what the sources are today. A configuration writer API is not among them: the section above settles that one rather than deferring it, and an administrative surface, a UI, a tenant override model, an approval workflow, or a configuration versioning model built over configuration is refused with it. What such a surface may administer is state in the database, which is a decision for whichever record introduces that state.

Secrets remain outside ordinary source-controlled configuration. The configuration access layer may reference secret identifiers or consume already-bound secret values at the host boundary, but it must not normalize broad secret access into application code or log secret material.

This ADR also does not permit adding new third-party packages. Any future provider package, hosted configuration service, or secret-store integration requires separate official documentation, license, service-terms, telemetry, and data-processing review, plus `THIRD_PARTY_LICENSES.md` updates when applicable.

## Amendments

### Amendment 1: referenced secrets are reloadable for new operations

*Approved by the owner on 2026-07-27, for `specs/02b-certificate-material-and-secret-rotation.md`.*

The original reload policy classified credentials and certificate trust anchors as restart-required without qualification. That guidance was written before a secret-reference indirection existed. With one, reload no longer means mutating a bound secret value in place; it means re-resolving a reference whose validity is proven before the snapshot carrying it is published. The two are different operations with different risks, and the original text could only describe the first.

A credential or trust anchor reached through a secret reference is therefore classified **reloadable for new operations**, subject to all of the following:

- A candidate snapshot is validated by resolving every reference in it, and by loading the material behind any reference whose consumer requires a typed artifact, before it is published. A candidate that fails is rejected and the last known good snapshot stays active.
- Material is applied at operation boundaries only: a synchronization run that has authenticated continues with the credential it authenticated with, and a long-lived authenticated session is recycled at its next safe point rather than having its credentials swapped underneath it. "Reloadable during running operations" is deliberately not chosen, because swapping a credential or a trust anchor mid-operation has no coherent meaning.
- Resolution moves from once-at-startup to per use, so material rotated behind an unchanged reference is observed without any configuration reload at all. A network-backed provider that cannot afford per-use retrieval caches inside its own adapter with its own expiry, which keeps caching policy an adapter concern rather than a contract concern.
- Validation never runs on the thread that reported the reload, never terminates the process on a resolution failure, and never lets an older candidate publish after a newer one.
- A rejected reload is logged with the configuration path and a stable failure identity, and never with material.

A secret that is *not* reached through a reference — a password written into a connection string in ordinary configuration — keeps the original classification, because nothing re-reads it and no validation step precedes its use.

## Validation

- Code review verifies that `Domain` never references `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`, provider SDK types, configuration section names, or raw setting keys.
- Code review verifies that `Application` use cases depend on focused configuration reader contracts or immutable settings, not raw `IConfiguration` or framework options types.
- Unit tests cover source validation, strict binding behavior where configured, mapping from options DTOs to business settings, startup validation failures, reload success, rejected reload with last-known-good preservation, and privacy-safe diagnostics for invalid reloads.
- Documentation for each setting group states its source shape, required keys, safe diagnostics, mutability classification, and whether it is restart-required, reloadable for new operations, or reloadable during running operations.
- Pull-request review rejects reloadable settings without an explicit consistency, security, privacy, and operational rationale.
- Code review rejects any code that writes a configuration source or a bound options instance — a JSON file the host reads, a provider dictionary, an environment variable, an options cache — and rejects a port, service, endpoint, or command offering to do it. A feature needing mutable state is reviewed against the table that holds it.

## More Information

- Microsoft Learn, ".NET configuration," describes hierarchical keys, provider ordering where later providers override earlier providers, binder limitations for read-only collection interfaces, support for constructor binding, and notes that custom mapping can translate safe keys into desired business keys: <https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration>.
- Microsoft Learn, `BinderOptions.ErrorOnUnknownConfiguration`, documents strict binding behavior for unknown keys and conversion failures: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.binderoptions.erroronunknownconfiguration>.
- Microsoft Learn, "Configuration providers in .NET," documents provider setup and `reloadOnChange` for JSON/XML file providers: <https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers>.
- Microsoft Learn, "Options pattern in ASP.NET Core," documents `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`, named options, options validation, and that validation runs again when options are reloaded: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0>.
- Microsoft Learn, "Options pattern in .NET," documents options validation, `IValidateOptions<TOptions>`, and `AddOptionsWithValidateOnStart<TOptions>` / `ValidateOnStart` startup validation: <https://learn.microsoft.com/en-us/dotnet/core/extensions/options>.
- Microsoft Learn, "Detect changes with change tokens in ASP.NET Core," describes configuration reload change tokens and file-provider reload behavior: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens?view=aspnetcore-10.0>.
- Microsoft Learn, "Implement a custom configuration provider in .NET," documents custom providers backed by a database, which is background for a read-only source of that shape and is not adopted by this ADR; the writable half of what that page describes is refused above: <https://learn.microsoft.com/en-us/dotnet/core/extensions/custom-configuration-provider>.
- This ADR refines the MailFathom architecture rule that `Host` owns configuration and dependency injection, while `Application` and `Domain` remain independent of infrastructure frameworks: `specs/2026-07-22-mail-fathom-architecture-draft.md`.

## Decision Outcome

Chosen option: "Introduce an application-owned configuration access layer backed by host options and reload notifications", because it keeps .NET configuration infrastructure at the host/infrastructure boundary while giving application code stable, validated, domain-meaningful configuration objects with explicit reload semantics.

The decision is proposed, not accepted. It covers source validation, reading, mapping, and automatic reload for runtime settings that are safe to update in-process, and it closes the write side: configuration is read-only to the process, and administrative editing of it, an approval workflow over it, a configuration history, and a multi-tenant configuration lifecycle are refused with the writer port rather than deferred. Where a deployment's read-only sources come from stays open — a file, a mounted object, a cloud configuration store, another managed configuration service — and so does every question about state a program modifies, which each belongs to the record introducing that state and is answered in PostgreSQL.

### Pros and Cons of the Selected Option

#### Introduce an application-owned configuration access layer backed by host options and reload notifications

Host/infrastructure code binds and validates provider-shaped options, maps them into immutable business settings, and exposes focused application-facing readers or snapshots. Reloadable groups are updated through validated atomic snapshots.

- Good, because it preserves clean architecture and keeps Microsoft configuration mechanisms outside the domain model and ordinary use-case logic.
- Good, because use cases consume business settings such as synchronization limits, retrieval safety policy, or provider capability flags rather than raw keys or binder DTOs.
- Good, because mapping creates one place to translate provider strings, defaults, units, ranges, sensitive-value handling, source-validation results, and reload classification into business semantics.
- Good, because last-known-good reload behavior and safe operational diagnostics can be implemented once per setting group.
- Neutral, because configuration DTOs and business settings will both exist and require explicit mapping tests.
- Bad, because the layer adds code and design discipline before MailFathom has many configurable behaviors.
- Bad, because incorrect classification of reloadability can still cause inconsistent runtime behavior.
- Bad, because this layer must stay narrow; a generic settings service would recreate raw configuration coupling under a different name, and a writer API would do it while also breaking the read-only rule above.
