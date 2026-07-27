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

## Asynchronous return types

- Return `Task` or `Task<TResult>` from every asynchronous method unless a rule below applies. A reference-typed task is a single field, composes directly with `Task.WhenAll` and `Task.WhenAny`, and can be awaited, stored, and awaited again without care.
- Complete synchronously through `Task.CompletedTask` and `Task.FromResult`, or a cached completed task for a hot repeated result, rather than reaching for `ValueTask` to avoid an allocation.
- Return `ValueTask` or `ValueTask<TResult>` when a framework contract requires it: `IAsyncDisposable.DisposeAsync`, and `IAsyncEnumerable<T>.GetAsyncEnumerator` with `IAsyncEnumerator<T>.MoveNextAsync` should the repository gain an async stream. Only `DisposeAsync` occurs today. A mandated signature is not precedent for choosing `ValueTask` elsewhere.
- A private or internal helper may return `ValueTask` when it exists to implement one of those mandated signatures, so the dispose path stays free of a wrapping conversion. Keep such a helper unpublished and awaited once by its caller.
- Choose `ValueTask` over `Task` only when every one of these holds, and record the measurement in the pull request:
  - the operation completes synchronously on its common path, for example a cache or buffer hit, or the implementation pools an `IValueTaskSource<TResult>` so an asynchronous completion is also allocation-free;
  - it is called often enough for one task allocation per call to be a measured cost, not a suspected one;
  - every caller awaits the result directly and none needs to store it, fan it out, or combine it;
  - a benchmark or profile over a realistic workload shows the improvement.
- Weigh the costs before deciding. A `ValueTask` holds several fields, so returning one copies more data and enlarges the state machine of every async method that awaits it. A caller forced to call `AsTask()` reintroduces the allocation the choice was meant to remove, and leaves the code harder to read than the `Task` it replaced.
- Do not introduce `ValueTask` at an application port, domain contract, or MCP boundary. Signatures there are consumed by code paths chosen later and must stay safe to compose; only an adapter with a measured hot path is a candidate.
- Consume a `ValueTask` exactly once: await it directly, or call `AsTask()` on it, and then treat the instance as spent. Awaiting twice, awaiting concurrently, calling `AsTask()` twice, mixing consumption techniques, or reading `.Result` or `GetAwaiter().GetResult()` before completion is undefined behavior, not a slow path, and can corrupt a pooled backing source.
- When a caller genuinely needs to hold or re-observe the result, convert once with `AsTask()`, or use `Preserve()` when the value must stay a `ValueTask`. Never store a raw `ValueTask` in a field, collection, or captured local.
- Never let `ValueTask` reach `Task.WhenAll`, `Task.WhenAny`, or any other combinator without converting. Needing that conversion is evidence the method should have returned `Task`.
- Write asynchronous methods so cancellation, timeouts, and exceptions behave identically whichever type is returned. Switching from `Task` to `ValueTask` is an allocation decision only; it must never change observable semantics.

## Dependency injection and configuration

- Register dependencies in focused extension methods owned by the project that implements them; keep `Program.cs` as a readable composition root.
- Choose DI lifetimes deliberately. Never inject a scoped service into a singleton or capture scoped services in background workers.
- Background services create an explicit scope per independent work unit and honor host cancellation.
- Use typed options for related configuration. Apply `ValidateDataAnnotations`, custom validators where necessary, and `ValidateOnStart` for required settings.
- Keep secrets out of source control and ordinary configuration files. Load them from deployment secrets, systemd credentials, or an approved secret provider.
- Do not read environment variables throughout domain or application code. Bind configuration once at the host boundary.
