# Mail MCP Development Instructions

These instructions apply to the entire repository.

The product and solution name is `MailMcp`. The solution file is `MailMcp.slnx`; project names, assembly names, and root namespaces use the `MailMcp.*` prefix.

## Critical repository rules

- Never add `Co-authored-by:` or any other co-author trailer to commits or pull requests.
- Never commit directly on `main` or `master`. Create a branch named `agent/<short-description>` before committing.
- Preserve unrelated user changes. Stage only files that belong to the current task.
- Make architectural decisions before implementation. Keep changes small, reviewable, and aligned with the architecture draft in `specs/`.
- Treat MailMcp as an enterprise-grade system even during early scaffolding: preserve seams for governance, auditability, privacy controls, operational hardening, compliance evidence, and future Agent Governance Toolkit (AGT) adoption without prematurely adding runtime dependencies.

## Third-party licensing

- MailMcp must remain compatible with both commercial closed-source distribution and open-source publication.
- Before adding, upgrading, replacing, bundling, or distributing a third-party component, verify the current upstream license from official project, package, or vendor sources.
- Only use third-party components whose licenses permit commercial use and do not require MailMcp itself to be relicensed or distributed as source code. Prefer permissive licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, and PostgreSQL License.
- Do not introduce GPL, AGPL, SSPL, BUSL, Commons Clause, PolyForm Noncommercial, source-available-only, field-of-use-restricted, or otherwise non-permissive dependencies without explicit owner approval.
- Treat hosted services, AI models, provider APIs, container images, generated assets, and copied code samples as separately reviewable from SDK licenses. A permissive SDK license does not approve the service terms, model terms, data-use policy, trademark terms, or redistribution conditions.
- Keep the root `LICENSES.md` third-party license register current. Add or update its entries in the same change set as any dependency, service, protocol SDK, container image, generated asset, or externally sourced code sample change.
- When a dependency is pinned in `Directory.Packages.props`, record the exact package name, version, license expression, upstream URL, and any required attribution or NOTICE handling in `LICENSES.md`.

## Documentation workflow

- Before using or changing a library, framework, protocol, CLI, or external API, consult its latest official documentation.
- Prefer Microsoft Learn, official project documentation, specifications, and upstream repositories over blog posts or secondary examples.
- Confirm package compatibility with .NET 10 before adding or updating a dependency.
- Pin package versions centrally in `Directory.Packages.props`. Do not use floating versions.
- Write repository documentation in English and keep durable documentation under `docs/`.
- Develop and verify tests and production code before writing the corresponding repository documentation. Documentation must describe the behavior that actually exists, not an intended implementation.
- After the code is implemented and verified, create or update the relevant `docs/` page before completing the task. Code and its documentation normally belong in the same commit or reviewable change set.
- Document architecture, feature behavior, configuration, security assumptions, operational procedures, failure modes, and important implementation trade-offs when they are introduced or changed.
- Keep a discoverable documentation structure such as `docs/architecture/`, `docs/features/`, `docs/operations/`, and `docs/decisions/`. Add an index when more than a few pages exist.
- Update examples, configuration snippets, command names, and diagrams whenever the corresponding code changes. Stale documentation is a defect.
- Do not create documentation that merely repeats type names or folder structure. Explain purpose, contracts, invariants, data flow, operational impact, and the reason behind important decisions.

## Architecture

- Build a clean-architecture modular monolith with clear `Domain`, `Application`, `Infrastructure`, `AI`, `Mcp`, `Host`, and `Cli` boundaries.
- `Domain` contains business concepts and invariants and has no dependency on infrastructure frameworks.
- `Application` contains use cases and ports and depends only on `Domain`.
- `Infrastructure` implements persistence, IMAP/SMTP, message-content storage, security, and observability ports.
- `AI` owns retrieval, chunking, embeddings, and agent-framework composition without leaking provider-specific types into `Application` or `Domain`.
- `Mcp` maps protocol inputs and outputs to application use cases. It contains no persistence or email-protocol logic.
- `Host` is a composition root only: configuration, dependency injection, middleware, endpoints, workers, and process lifetime.
- Prefer direct, explicit dependencies and small interfaces at architectural boundaries. Do not introduce abstractions without a current testing, protocol, or replacement need.
- Keep email retrieval read-only. Synchronization and content retrieval must never set the remote IMAP `\Seen` flag.

## .NET and C# conventions

- Target .NET 10 and use idiomatic modern C# supported by the pinned SDK.
- Keep the SDK version in `global.json`. Shared compiler and build settings belong in `Directory.Build.props`; shared package versions belong in `Directory.Packages.props`.
- Enable nullable reference types, implicit usings, deterministic builds, .NET analyzers, and code-style enforcement during builds.
- Treat compiler and analyzer warnings as errors in repository code. Suppress a diagnostic only at the narrowest scope and document the concrete reason.
- Maintain one repository `.editorconfig` and let automated formatting define whitespace and layout. Do not hand-format against configured rules.
- Prefer immutable records or value objects for data that represents values; use entities only when identity and lifecycle matter.
- Use domain-correct, descriptive names for types, methods, parameters, variables, fields, and files. Avoid abbreviations except established terms such as IMAP, SMTP, MIME, MCP, UID, TLS, and RAG.
- Prefer a longer name that communicates intent, constraints, or result over a short ambiguous name. Long method names are acceptable when every word adds useful domain meaning.
- Keep names proportionate and avoid redundant context already supplied by the containing type or namespace. Do not produce sentence-like names when a smaller precise name communicates the same contract.
- Name methods after observable behavior or the result they produce. Avoid vague verbs such as `Handle`, `Process`, `Manage`, `Do`, or `Execute` unless the surrounding application pattern gives them a precise established meaning.
- Rename unclear identifiers as part of the code change that exposes them. Do not rely on comments to compensate for misleading or abbreviated names.
- Keep public APIs small and predictable. Make types and members `internal` unless they are intentionally part of a cross-project contract.
- Prefer one primary type per file and align namespaces with folders. File names match their primary type.
- Use `sealed` for concrete classes not designed for inheritance. Prefer composition over inheritance and do not use inheritance only to share implementation.
- Prefer guard clauses over deep nesting. Validate public boundary arguments with the appropriate BCL guard methods or explicit domain validation.
- Do not use `null` to encode several states. Model optionality and failure explicitly when absence has domain meaning.
- Expose read-only collection abstractions when callers must not mutate state. Avoid returning mutable internal collections.
- Prefer pattern matching, switch expressions, collection expressions, and other modern syntax only when they make the intent clearer.
- Avoid reflection, `dynamic`, source-code generation, and unsafe code unless a measured requirement justifies them.
- Use constructor injection. Avoid service locators, global mutable state, and static dependencies that hide collaborators.
- Make I/O asynchronous end-to-end. Never block on tasks with `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.
- Suffix task-returning methods with `Async`, except framework-defined signatures where the ecosystem convention differs.
- Async methods that perform I/O accept and propagate `CancellationToken`. Put it last and do not replace it with `CancellationToken.None` inside a call chain.
- Use `await using` and `IAsyncDisposable` for asynchronously released resources. Dispose owned resources; never dispose dependencies owned by the DI container.
- Use `Task` by default. Choose `ValueTask` only after measurement shows a meaningful benefit and its consumption constraints are acceptable.
- Avoid unbounded concurrency. Put explicit limits and backpressure around mailbox synchronization, MIME processing, embedding generation, and SMTP delivery.
- Do not use blanket `ConfigureAwait(false)` in ASP.NET Core application code. Use it only where a reusable library boundary has a documented reason.
- Use `DateTimeOffset` for timestamps and inject `TimeProvider` wherever current time affects behavior.
- Validate options at startup. Fail fast on invalid or unsafe configuration.
- Use structured logging with named properties. Never log credentials, tokens, message bodies, attachment content, or raw MIME.
- Catch exceptions only when adding useful context, translating at a boundary, applying a defined retry policy, or completing cleanup. Preserve the original exception as `InnerException`.
- Use explicit result types for expected application failures; reserve exceptions for exceptional or infrastructure failures.
- Keep methods focused and classes cohesive. Prefer readable control flow over clever expressions or premature generic abstractions.
- Use English XML documentation comments to make code contracts useful to developers, IDE tooling, and future agents.
- Generate XML documentation files for production projects and keep missing public API documentation visible through compiler or analyzer diagnostics.
- Document every public type and public member. Also document internal interfaces, extension points, domain invariants, protocol boundaries, concurrency rules, security-sensitive behavior, and non-obvious lifecycle or ownership requirements.
- Use `<summary>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`, and `<example>` where they add concrete contract information. Describe cancellation behavior and side effects for asynchronous or state-changing operations.
- Keep XML documentation accurate when signatures or behavior change. Missing or stale documentation is part of the implementation and must be fixed in the same change.
- Add inline comments for non-obvious reasoning, protocol hazards, algorithms, workarounds, security constraints, or decisions that cannot be expressed through naming and types.
- Explain why the code must behave a certain way; do not narrate what an immediately readable statement already does. Prefer better naming or extraction over explanatory comments for ordinary control flow.

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

## Persistence and EF Core

- Keep `DbContext` scoped and short-lived. It is not thread-safe and must never be shared across concurrent operations.
- Use asynchronous EF Core APIs and propagate cancellation tokens.
- Project queries directly into application read models. Do not load full entity graphs when a bounded projection is sufficient.
- Use `AsNoTracking` for read-only queries unless identity resolution or change tracking is explicitly required.
- Avoid lazy loading and hidden N+1 queries. Make related data loading explicit.
- Express uniqueness, concurrency, and idempotency guarantees in PostgreSQL constraints as well as in application logic.
- Keep transactions short and define their boundary in the application operation. Do not hold a database transaction open across IMAP, SMTP, or AI network calls.
- Review generated migrations and SQL. Add indexes from demonstrated query shapes and inspect query plans for performance-critical paths.
- Use provider-supported parameterization. Never construct SQL from untrusted strings; any dynamic identifier must come from validated application-owned metadata.

## Enterprise governance, privacy, and GDPR readiness

- Design every feature with GDPR-aligned privacy by design and by default: data minimization, purpose limitation, storage limitation, confidentiality, integrity, availability, and accountable processing must be visible in the architecture, tests, and documentation.
- Email content, metadata, embeddings, retrieval snippets, audit events, and model/tool traces can contain personal data. Classify them as sensitive by default and avoid broad access, unnecessary copying, long retention, or unredacted logging.
- Keep explicit seams for future data-subject workflows such as access, export, rectification support, erasure, restriction of processing, retention holds, and audit evidence, even when those workflows are not implemented in the first release.
- Do not treat embeddings or derived indexes as anonymous. They inherit the classification, retention, access-control, deletion, and export constraints of the source mail content unless a reviewed privacy design proves otherwise.
- Future AGT adoption must remain an adapter-level governance concern. AGT policy decisions, audit records, and tool-call controls must not leak provider-specific or governance-framework types into `Domain` or `Application`.
- Before adding AGT or any governance/compliance package, verify the current official documentation, .NET 10 compatibility, license, service terms, telemetry behavior, and data-processing implications; update `LICENSES.md` for any dependency or externally sourced component.

## Reliability, security, and performance

- Apply timeouts to external I/O and distinguish caller cancellation, service shutdown, timeout, authentication failure, and transient transport failure.
- Retry only operations known to be transient and safe to repeat. Use bounded attempts with jittered backoff; never create nested retry storms.
- Make worker leases and state transitions durable where a process crash could otherwise duplicate or lose work.
- Prefer bounded channels or queues for background pipelines. Record queue depth, processing duration, failures, and retry counts without recording message content.
- Use cryptographically secure random generation for tokens and security-sensitive identifiers. Compare secrets using appropriate constant-time APIs where relevant.
- Encode and validate data for its destination context. Treat email HTML, headers, filenames, URLs, tool arguments, and model output as untrusted input.
- Use least privilege for PostgreSQL, OAuth scopes, filesystem access, certificates, and operating-system service accounts.
- Optimize only after measurement. Prefer appropriate algorithms, bounded allocations, streaming, and database projections before low-level micro-optimizations.
- Stream large MIME content and attachments rather than buffering them repeatedly. Set explicit size and count limits at every public or remote boundary.

## Email protocol safety

- Treat `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity.
- Fetch message bodies with mechanisms that preserve the remote `\Seen` state. Add a regression test for every code path that fetches content.
- Keep MCP reads local; an MCP request must not trigger a synchronous IMAP fetch.
- Make synchronization, object writes, indexing, and SMTP outbox processing idempotent.
- Require explicit opt-in for unencrypted IMAP/SMTP transport and clear-text authentication over an unencrypted connection.
- Do not disable TLS certificate validation. Support private servers through explicit trusted CA configuration.

## Unit testing policy

- Unit tests are part of every behavior change, feature, and bug fix. Write or update the failing test before production code when practical.
- Use xUnit.net v3 on Microsoft Testing Platform v2 as the test framework and NSubstitute for test doubles.
- Keep unit tests in separate projects under `tests/`, named after the production boundary they cover:
  - `MailMcp.Domain.UnitTests`
  - `MailMcp.Application.UnitTests`
  - `MailMcp.Infrastructure.UnitTests`
  - `MailMcp.AI.UnitTests`
  - `MailMcp.Mcp.UnitTests`
- Follow Arrange, Act, Assert. Add explicit `// Arrange`, `// Act`, and `// Assert` comments in unit tests so test phases are visually consistent across the repository.
- Name tests `Member_Scenario_ExpectedBehavior`. Use `[Fact]` for one scenario and `[Theory]` for the same behavior over multiple inputs.
- Test observable behavior and domain invariants, not private implementation details. One test should describe one behavior even if several assertions are needed to prove it.
- Tests must be fast, isolated, repeatable, order-independent, and safe to run in parallel. Do not use real clocks, random nondeterministic values, shared mutable fixtures, sleeps, network calls, databases, containers, or the filesystem in unit tests.
- Prefer real domain values and simple in-memory fakes for state. Use NSubstitute at external or architectural boundaries where interaction is part of the contract.
- Do not substitute concrete MailKit clients. Define narrow application-facing session or transport ports and use NSubstitute to model IMAP/SMTP server capabilities, responses, disconnects, and failures.
- Use `Received()` and `DidNotReceive()` only when the interaction itself is a required side effect or safety invariant. Prefer state or result assertions otherwise.
- Use argument matchers only while configuring substitutes or verifying received calls.
- Every IMAP content-fetch path must prove that no operation capable of setting `\Seen` was requested.
- Cover cancellation, retry boundaries, idempotency, duplicate events, UIDVALIDITY changes, partial failures, and unsafe TLS/authentication configuration where relevant.
- Run the complete unit test suite with `dotnet test` before committing.

## Integration tests

- Integration tests are planned for a later phase and are documented only in the architecture draft for now.
- Do not add integration-test projects, Testcontainers, Docker-based fixtures, real PostgreSQL dependencies, or real/mock network mail servers yet.
- Cases that require validating MailKit wire behavior against an actual IMAP/SMTP server belong to the future integration suite. Do not mislabel them as unit tests.

## Dependency and implementation discipline

- Keep third-party types inside their owning adapter wherever practical.
- Prefer platform capabilities before adding packages. Every new package must have a clear owner and purpose.
- Do not expose EF Core entities, MailKit objects, MCP SDK types, or provider-specific AI types across application boundaries.
- Access raw RFC 822 content only through the application-owned `IMessageContentStore` port. Its initial implementation uses a dedicated PostgreSQL table, separate from message metadata.
- Keep PostgreSQL, Npgsql, and `bytea` details inside the initial content-store adapter so a future MinIO/S3 implementation does not change application use cases or domain types.
- Do not load raw MIME in ordinary mailbox queries or track large `bytea` values in EF Core unnecessarily.
- Apply database migrations explicitly. Do not run destructive or automatic production migrations during ordinary host startup.
- Use keyset pagination for email timelines and bounded result sizes for all public queries.
- Treat email content, OAuth tokens, credentials, certificate material, and embeddings as sensitive data.

## Verification

Run checks appropriate to the change before reporting completion:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

Also inspect the final diff for accidental secrets, unrelated edits, generated files, and dependency-boundary violations.
