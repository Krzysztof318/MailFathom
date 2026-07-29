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

## Failures

These rules implement [ADR 0003](../docs/decisions/0003-first-party-exception-hierarchy-and-stable-error-codes.md), which records why they exist.

- Derive every exception this system publishes from `MailMcpException` and give it a `MailMcpErrorCode`. A concrete public exception outside that hierarchy carries no identity a boundary can report, and the per-assembly hierarchy tests fail on one.
- Keep an exception internal only when it is a control-flow signal between one implementation and its own caller and reaches no boundary. It then carries no code, because a code names something a boundary publishes, and it documents why it does not escape in the `CA1064` suppression that making it internal already requires. `MimeStructureLimitReachedException` is the worked example.
- Allocate the code as five digits, `C S NNN`: category, subcategory, then the failure's number within that subcategory. Categories are 1 configuration and transport security, 2 mail protocol, 3 persistence, 4 outbound resilience, 5 the MCP boundary. Allocate a number once and never reuse or renumber it, the same way an enum member's value is never reordered.
- Write the message for an operator to read. It must never carry a credential, a token, a certificate, a host name, a remote folder path, the mechanisms a server advertised, message content, or any other personal data. An account alias, a folder alias, a rule identity, a size, and a limit are permitted, because they are MailMcp's own configured names for things.
- Declare only the constructors callers use, and keep every payload non-nullable. `CA1032` and `RCS1194` are both disabled, so nothing mandates a parameterless or message-only overload, and one added anyway would leave the payload degenerate. A property is nullable only where absence has its own domain meaning, which the remarks then state.
- Prefer a result type when the immediate caller acts on the failure and continues, and raise an exception when the fact must travel through code that cannot decide what it means. `RemoteEmailContentFetchResult` and `PersistenceConcurrencyConflictException` are the two worked examples; a new failure states which one it follows and why.

## Asynchronous return types

- Return `Task` or `Task<TResult>` from every asynchronous method unless a rule below applies. A reference-typed task is a single field, composes directly with `Task.WhenAll` and `Task.WhenAny`, and can be awaited, stored, and awaited again without care.
- Complete synchronously through `Task.CompletedTask` and `Task.FromResult`, or a cached completed task for a hot repeated result, rather than reaching for `ValueTask` to avoid an allocation.
- The default covers methods that return a task. An async iterator returns `IAsyncEnumerable<T>` instead, and `IAsyncEnumerable<T>.GetAsyncEnumerator` returns `IAsyncEnumerator<T>` synchronously; neither is subject to it. Choose a stream when a caller consumes results incrementally, and keep the sequence bounded like any other query result.
- Return `ValueTask` or `ValueTask<TResult>` when a framework contract requires it: `IAsyncDisposable.DisposeAsync`, and `IAsyncEnumerator<T>.MoveNextAsync` should the repository gain an async stream. Only `DisposeAsync` occurs today. A mandated signature is not precedent for choosing `ValueTask` elsewhere.
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
