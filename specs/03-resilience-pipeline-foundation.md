# Resilience Pipeline Foundation

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** cross-cutting, consumed from stage 3 onward
**Depends on:** nothing
**Estimated change size:** ~650 lines including tests and documentation

## Goal

Give MailMcp one deliberate, configurable, and testable resilience model for outbound dependencies, instead of hand-written retry loops appearing independently in the IMAP adapter, the synchronization supervisor, the SMTP worker, and the AI provider adapters.

## Current state

`Microsoft.Extensions.Http.Resilience` 10.6.0 is already pinned and `src/Host/ServiceDefaultsExtensions.cs` calls `AddStandardResilienceHandler` for `HttpClient` defaults. That covers HTTP only. IMAP, SMTP, PostgreSQL, and future embedding and chat calls have no timeout, retry, or circuit-breaker policy at all. The architecture draft asks for bounded jittered backoff and isolated retry state but never names a mechanism.

## Approved scope

Adopt Polly v8 resilience pipelines through the `AddResiliencePipeline` registration on `IServiceCollection`, resolving pipelines by key with `ResiliencePipelineProvider<TKey>`. Verify and centrally pin `Polly.Core` and `Polly.Extensions`, and record them in `LICENSES.md` with their upstream license expression before use. Evaluate `Microsoft.Extensions.Resilience` for metering enrichment in the same change and either adopt and record it or state why it was not needed.

The pipeline key is a typed enumeration of dependency classes rather than a free string, so a typo cannot silently resolve an empty pipeline: mailbox session establishment, mailbox data retrieval, message delivery, database command execution, and AI provider invocation. Each class has typed options — attempt count, base delay, maximum delay, per-attempt timeout, total timeout, circuit-breaker failure ratio and sampling window, and concurrency limit — validated at startup with `ValidateOnStart`.

`Application` owns a `ITransientFailureClassifier` port so use-case and adapter code can ask whether a failure is worth retrying without referencing Polly types. `Infrastructure` implements the classifier per dependency class and owns all Polly registration; Polly types never appear in `Domain` or `Application` contracts.

Two composition rules are enforced by review and by test: a pipeline is applied at exactly one layer per logical operation, so an adapter-level retry is never wrapped by a supervisor-level retry for the same call; and EF Core's own `EnableRetryOnFailure` execution strategy is either used or replaced by the database pipeline, never both, because combining them breaks explicit transaction boundaries.

## Safety and privacy

Retry is restricted to operations that are safe to repeat. Authentication failures, permission failures, and malformed-request failures are classified as terminal and are never retried, because repeating them can lock a mailbox account. Resilience telemetry records dependency class, outcome, attempt number, and duration, and never records credentials, mailbox addresses, message identifiers, or provider payloads.

## Testing

`Infrastructure.UnitTests` use `FakeTimeProvider` to prove backoff growth, jitter bounds, attempt caps, total-timeout enforcement, circuit-breaker open and half-open transitions, and concurrency limiting without any real delay. Classifier tests cover the transient and terminal cases per dependency class. An options-validation test proves that an unbounded or contradictory configuration fails startup.

## Out of scope

Applying the pipelines to any specific adapter. Specification 04 wires IMAP; later specifications wire the database, SMTP, and AI providers. Distributed rate limiting and cross-process circuit state are not part of this design.

## Definition of done

- Polly packages are pinned centrally and recorded in `LICENSES.md`.
- Every dependency class resolves a configured pipeline, and an unknown key cannot be constructed.
- No Polly type is reachable from `Domain` or `Application`.
- `docs/architecture/` gains a page describing the pipeline model, the single-layer rule, and the EF Core execution-strategy interaction.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
