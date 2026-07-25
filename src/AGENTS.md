# Source Development Instructions

These instructions apply under `src/` in addition to the repository root instructions.

## API and application design

- Model one application use case per handler or service operation with explicit input and output contracts.
- Validate untrusted input at the outer boundary and enforce business invariants again in the domain object that owns them.
- Keep transport contracts, application contracts, domain models, and persistence models distinct. Map explicitly at boundaries.
- Do not return exceptions, stack traces, internal identifiers, inner-exception details, or provider responses through MCP or administrative endpoints.
- Use stable machine-readable error codes with safe human-readable messages for expected failures. Model domain invariant failures with domain-specific exceptions only for exceptional states, and translate them at MCP boundaries into safe serialized errors without leaking inner exceptions.
- Keep query result sizes bounded. Use keyset pagination and stable deterministic ordering.
- Make retryable commands idempotent and carry an idempotency identity where duplicate execution could cause an external side effect.
- Keep authorization close to the use case as well as at the transport boundary so alternate entrypoints cannot bypass it.

## Dependency injection and configuration

- Register dependencies in focused extension methods owned by the project that implements them; keep `Program.cs` as a readable composition root.
- Choose DI lifetimes deliberately. Never inject a scoped service into a singleton or capture scoped services in background workers.
- Background services create an explicit scope per independent work unit and honor host cancellation.
- Use typed options for related configuration. Apply `ValidateDataAnnotations`, custom validators where necessary, and `ValidateOnStart` for required settings.
- Keep secrets out of source control and ordinary configuration files. Load them from deployment secrets, systemd credentials, or an approved secret provider.
- Do not read environment variables throughout domain or application code. Bind configuration once at the host boundary.
