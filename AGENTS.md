# Mail MCP Development Instructions

These instructions apply to the entire repository.

The product and solution name is `MailMcp`. The solution file is `MailMcp.slnx`; project directory and file names use short boundary names such as `Domain`, `Application`, and `Host`, while `Directory.Build.props` applies the `MailMcp.*` prefix to assembly names and root namespaces.

## Development environment

- Development runs locally. The repository does not provision agent environments, so install the SDK pinned in `global.json` and any command-line tooling such as `dotnet-ef` or the Aspire CLI on the developer machine. `docs/operations/local-development.md` lists the commands that must work.

## Critical repository rules

- Never add `Co-authored-by:` or any other co-author trailer to commits or pull requests.
- Never commit directly on `main` or `master`. Create a branch named `agent/<short-description>` before committing.
- Always create pull requests as drafts. Mark a pull request as ready for review only when the owner explicitly requests it.
- Preserve unrelated user changes. Stage only files that belong to the current task.
- Make architectural decisions before implementation. Keep changes small, reviewable, and aligned with the architecture draft in `specs/`.
- Treat ADRs under `docs/decisions/` as required architectural context for AI agents. Before changing architecture, boundaries, configuration, persistence, provider integration, governance, security-sensitive behavior, or cross-cutting infrastructure, read the relevant ADRs and keep the change consistent with their current status and rationale.
- Treat MailMcp as an enterprise-grade system even during early scaffolding: preserve seams for governance, auditability, privacy controls, operational hardening, compliance evidence, and future Agent Governance Toolkit (AGT) adoption without prematurely adding runtime dependencies.

## Third-party licensing

- MailMcp must remain compatible with both commercial closed-source distribution and open-source publication.
- Before adding, upgrading, replacing, bundling, or distributing a third-party component, verify the current upstream license from official project, package, or vendor sources.
- Only use third-party components whose licenses permit commercial use and do not require MailMcp itself to be relicensed or distributed as source code. Prefer permissive licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, and PostgreSQL License.
- Do not introduce GPL, AGPL, SSPL, BUSL, Commons Clause, PolyForm Noncommercial, source-available-only, field-of-use-restricted, or otherwise non-permissive dependencies without explicit owner approval.
- Treat hosted services, AI models, provider APIs, container images, generated assets, and copied code samples as separately reviewable from SDK licenses. A permissive SDK license does not approve the service terms, model terms, data-use policy, trademark terms, or redistribution conditions.
- Keep the root `LICENSES.md` third-party license register current. Add or update its entries in the same change set as any dependency, service, protocol SDK, container image, generated asset, or externally sourced code sample change.
- When a dependency is pinned in `Directory.Packages.props`, record the exact package name, version, license expression, upstream URL, and any required attribution or NOTICE handling in `LICENSES.md`.

## Documentation and test obligations

- Before using or changing a library, framework, protocol, CLI, or external API, consult its latest official documentation. Prefer Microsoft Learn, official project documentation, specifications, and upstream repositories.
- Confirm .NET 10 compatibility and pin package versions centrally in `Directory.Packages.props`. Do not use floating versions.
- Unit tests are part of every behavior change, feature, and bug fix. Read `tests/AGENTS.md` before adding or changing tests.
- Develop and verify tests and production code before documenting the implemented behavior. Update affected durable documentation in the same reviewable change set; stale guidance is a defect.
- Read `docs/AGENTS.md` before changing documentation. Create or modify ADRs only with explicit owner approval.
- `$check-docs-licenses` is a mandatory completion gate, including when its verdict is `n/a`.

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
- Give every enum member an explicit, unique integral value starting at `0` and increasing contiguously in declaration order. Never reorder, renumber, or reuse an existing value; append new members with the next value. Apply this to every enum, including private and currently non-persisted types, so a future numeric persistence representation cannot silently change meaning after refactoring.
- Prefer immutable records or value objects for data that represents values; use entities only when identity and lifecycle matter.
- Use domain-correct, descriptive names for types, methods, parameters, variables, fields, and files. Avoid abbreviations except established terms such as IMAP, SMTP, MIME, MCP, UID, TLS, and RAG.
- Prefer a longer name that communicates intent, constraints, or result over a short ambiguous name. Long method names are acceptable when every word adds useful domain meaning.
- Keep names proportionate and avoid redundant context already supplied by the containing type or namespace. Do not produce sentence-like names when a smaller precise name communicates the same contract.
- Name methods after observable behavior or the result they produce. Avoid vague verbs such as `Handle`, `Process`, `Manage`, `Do`, or `Execute` unless the surrounding application pattern gives them a precise established meaning.
- Rename unclear identifiers as part of the code change that exposes them. Do not rely on comments to compensate for misleading or abbreviated names.
- Use `Email` for the mail artifact throughout `Domain`, `Application`, and `Infrastructure`: `EmailOccurrenceId`, `RemoteEmailMetadata`, `IEmailContentStore`, `StoredEmailEntity`. Do not name a mail type `Message` or `MailMessage`; the first is ambiguous once AI conversations exist and the second shadows `System.Net.Mail.MailMessage`. Name an AI conversation turn `ChatMessage` or `AgentMessage` after the domain concept, never after the layer.
- Do not rely on a namespace to disambiguate a type whose name is ambiguous on its own. A reader sees the name at the point of use, not the namespace. `Session` in particular must always be qualified: `IMailboxSession` for an open IMAP folder, `IPersistenceSession` for a local write transaction.
- Keep public APIs small and predictable. Default types and members to `internal`; use `public` only for an intentional cross-project contract or when a framework demonstrably requires public visibility.
- Declare `InternalsVisibleTo` as an MSBuild `<InternalsVisibleTo Include="..." />` item in the project file that grants the access. Do not add hand-written assembly-attribute source files for it, so the granted friend assemblies stay visible where the project's build contract is defined.
- Prefer one primary type per file and align namespaces with folders. File names match their primary type.
- Default concrete implementation classes that are not designed for inheritance to `internal sealed`. DI registration, configuration binding, EF Core mapping, and unit-test access do not by themselves justify a public type. Prefer composition over inheritance and do not use inheritance only to share implementation.
- Prefer guard clauses over deep nesting. Validate public boundary arguments with the appropriate BCL guard methods or explicit domain validation.
- Do not use `null` to encode several states. Model optionality and failure explicitly when absence has domain meaning.
- Expose read-only collection abstractions when callers must not mutate state. Avoid returning mutable internal collections.
- For byte-oriented data, do not model payloads as `byte[]`, `List<byte>`, `IReadOnlyList<byte>`, or other general-purpose byte collections at application/domain boundaries. Prefer `Span<byte>` or `ReadOnlySpan<byte>` for synchronous stack-only operations, and `Memory<byte>` or `ReadOnlyMemory<byte>` when data must cross async, object, or DI boundaries. Keep `byte[]` only where a framework/provider contract requires it, such as EF Core `bytea` persistence models, and convert at that adapter boundary.
- Prefer pattern matching, switch expressions, collection expressions, and other modern syntax only when they make the intent clearer.
- Avoid reflection, `dynamic`, source-code generation, and unsafe code unless a measured requirement justifies them. This restricts authoring custom generators and generator-driven designs, not first-party framework generators such as `[LoggerMessage]`, `[GeneratedRegex]`, or `System.Text.Json` source generation, which are the recommended shape of those APIs.
- Use constructor injection. Avoid service locators, global mutable state, and static dependencies that hide collaborators.
- When a method, constructor, or primary-constructor parameter list has three or more parameters, put each parameter on its own line. If all involved type and parameter names are genuinely short, this may be deferred until four parameters. Keep the closing parenthesis and base/initializer on their own readable line when wrapping.
- Make I/O asynchronous end-to-end. Never block on tasks with `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.
- Suffix task-returning methods with `Async`, except framework-defined signatures where the ecosystem convention differs.
- Async methods that perform I/O accept and propagate `CancellationToken`. Put it last and do not replace it with `CancellationToken.None` inside a call chain.
- Use `await using` and `IAsyncDisposable` for asynchronously released resources. Dispose owned resources; never dispose dependencies owned by the DI container.
- When a type implements disposable ownership, implement the appropriate disposable contract explicitly: use `IDisposable` for synchronous resources, `IAsyncDisposable` for asynchronous resources, and implement both when the type owns both synchronous and asynchronous cleanup paths or can be consumed by both sync and async owners. Document ownership and disposal expectations.
- Use `Task` by default. Choose `ValueTask` only after measurement shows a meaningful benefit and its consumption constraints are acceptable.
- Avoid unbounded concurrency. Put explicit limits and backpressure around mailbox synchronization, MIME processing, embedding generation, and SMTP delivery.
- Do not use blanket `ConfigureAwait(false)` in ASP.NET Core application code. Use it only where a reusable library boundary has a documented reason.
- Use `DateTimeOffset` for timestamps and inject `TimeProvider` wherever current time affects behavior.
- Validate options at startup. Fail fast on invalid or unsafe configuration.
- Use structured logging with named properties. Never log credentials, tokens, message bodies, attachment content, or raw MIME.
- Do not wrap ordinary log calls in `ILogger.IsEnabled(...)`. The logging infrastructure already skips disabled levels, so the guard only adds noise. Use it exclusively when producing the log arguments themselves is expensive, for example serialization, formatting, LINQ materialization, large allocations, or an extra query. Prefer removing the cost or using compile-time `LoggerMessage` source-generated methods over adding a guard.
- Catch exceptions only when adding useful context, translating at a boundary, applying a defined retry policy, or completing cleanup. Preserve the original exception as `InnerException`.
- Use explicit result types for expected application failures; reserve exceptions for exceptional or infrastructure failures.
- Keep methods focused and classes cohesive. Prefer readable control flow over clever expressions or premature generic abstractions.
- Keep methods small enough to read as a single sequence of decisions. When a method mixes several responsibilities, nests loops around multi-step work, or needs a comment to announce its next stage, extract a named private method instead. Prefer extraction over longer bodies, and prefer a well-named method over an explanatory comment.
- Use blank lines to separate the logical blocks inside a method body so structure is visible before the code is read. Separate at minimum: the argument-guard block, each distinct step or stage, a block that builds state from the block that consumes it, and the final `return` from the work that produced it. Do not separate lines that belong to one step, and do not leave a blank line as the first or last line of a block.
- Use English XML documentation comments to make code contracts useful to developers, IDE tooling, and future agents.
- Generate XML documentation files for production projects and keep missing public API documentation visible through compiler or analyzer diagnostics.
- Document every public type and public member. Also document internal interfaces, extension points, domain invariants, protocol boundaries, concurrency rules, security-sensitive behavior, and non-obvious lifecycle or ownership requirements.
- Use `<summary>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`, and `<example>` where they add concrete contract information. Describe cancellation behavior and side effects for asynchronous or state-changing operations.
- Keep XML documentation accurate when signatures or behavior change. Missing or stale documentation is part of the implementation and must be fixed in the same change.
- Add inline comments for non-obvious reasoning, protocol hazards, algorithms, workarounds, security constraints, or decisions that cannot be expressed through naming and types.
- Explain why the code must behave a certain way; do not narrate what an immediately readable statement already does. Prefer better naming or extraction over explanatory comments for ordinary control flow.

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

## Cross-boundary email invariants

- Treat `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity.
- Keep MCP reads local; an MCP request must not trigger a synchronous IMAP fetch.
- Make synchronization, object writes, indexing, and SMTP outbox processing idempotent.

## Dependency and implementation discipline

- Keep third-party types inside their owning adapter wherever practical.
- Prefer platform capabilities before adding packages. Every new package must have a clear owner and purpose.
- Do not expose EF Core entities, MailKit objects, MCP SDK types, or provider-specific AI types across application boundaries.
- Access raw RFC 822 content only through the application-owned `IEmailContentStore` port. Its initial implementation uses a dedicated PostgreSQL table, separate from email metadata.
- Keep PostgreSQL, Npgsql, and `bytea` details inside the initial content-store adapter so a future MinIO/S3 implementation does not change application use cases or domain types.
- Do not load raw MIME in ordinary mailbox queries or track large `bytea` values in EF Core unnecessarily.
- Apply database migrations explicitly. Do not run destructive or automatic production migrations during ordinary host startup.
- Use keyset pagination for email timelines and bounded result sizes for all public queries.
- Treat email content, OAuth tokens, credentials, certificate material, and embeddings as sensitive data.

## Agent workflow and verification

- For file-changing tasks, start with `$start-task`.
- Before final verification, use `$review-change`.
- To finish, use `$finish-change`; it requires `$check-docs-licenses`, full verification, focused staging, and a draft pull request.
- Use `scripts/inspect-workspace.sh` for a read-only workspace preflight.
- Use `scripts/verify-fast.sh` during implementation.
- Stage the task files before running `scripts/verify-full.sh`; the gate rejects remaining untracked files so newly added files cannot bypass diff validation.
- Use `scripts/verify-full.sh` before committing. It runs the workflow contract suite, restores tools and packages, builds, runs the complete unit-test and coverage gate, verifies formatting, and checks the diff.
- Inspect the final diff for accidental secrets, unrelated edits, generated files, and dependency-boundary violations.
