# MailFathom Development Instructions

These instructions apply to the entire repository.

The product and solution name is `MailFathom`. The solution file is `MailFathom.slnx`; project directory and file names use short boundary names such as `Domain`, `Application`, and `Host`, while `Directory.Build.props` applies the `MailFathom.*` prefix to assembly names and root namespaces.

## Project status

MailFathom is under continuous development and has not had a first release. Nothing is published, no deployment runs it, and no consumer outside this repository depends on any of its contracts. Builds are stamped with the version `Directory.Build.props` declares, but no tag has ever been pushed, so every build is a preview of a release that has not happened. The first release is milestone `0.1.0 — first public release`.

This is a working constraint, not a disclaimer. A breaking change to a configuration key, a database schema, an MCP tool contract, or a public API is taken now, in full, while it costs one edit and nothing has to be migrated — deferring it behind a compatibility shim trades a free change today for a permanent one later. It cuts the other way too: do not write versioning machinery, migration paths, deprecation shims, or fallbacks for a released version that does not exist. Say plainly in the change that a contract moved, and correct every caller in the same change set.

None of this relaxes the rest of these instructions. Pre-release is a reason to change a contract cleanly, never a reason to skip tests, documentation, verification, or the privacy and licensing obligations below.

## Development environment

- Development runs locally. The repository does not provision agent environments, so install the SDK pinned in `global.json` and the Aspire CLI on the developer machine. `dotnet-ef` is pinned in `.config/dotnet-tools.json` and arrives with `dotnet tool restore`, so it is not something to install by hand or to match a version of. `docs/operations/local-development.md` lists the commands that must work.
- An EF Core command that reaches a database goes through the AppHost's `mailfathom-migrations` resource — `aspire resource mailfathom-migrations <command>` — never through `dotnet ef` directly. Applying, resetting, and reporting status all depend on connecting to the server the orchestration provisions and the connection string it issues, so running them by hand either fails or, worse, reaches a database that differs from every real one. Aspire 13 has no `aspire exec` command; the migration resource replaces it.
- An EF Core command that reads only the checkout may call `dotnet ef` directly, because there is no database for it to see wrongly. Generating a migration, scripting one to SQL, and `has-pending-model-changes` compare the compiled model against the committed model snapshot and produce identical output against a database that does not exist; `scripts/add-migration.sh` and `scripts/script-migration.sh` are those commands, and both point the design-time factory at a port nothing listens on so that a future version requiring a connection fails loudly instead of silently reading one. Routing them through the orchestration would mean starting PostgreSQL to answer a question about two files.
- Use `$add-migration` for any model change that needs a migration. Every migration is permanent: nothing regenerates, renames, reorders, or deletes one, because a migration identifier recorded in a database's `__EFMigrationsHistory` can never be reached again once it is regenerated. A model change appends a migration with a descriptive name, and reviewing it as the SQL it produces is part of that workflow rather than an optional extra. Clearing local data is `ef-database-reset`, which drops the database and replays the migrations without touching a file.
- The host never applies migrations, in any environment. It verifies the migration history at startup and fails fast on a pending migration. Applying is the `mailfathom-migrations` resource locally and an explicit deployment step elsewhere; do not add a second mechanism that applies them.
- CI fails a pull request whose model has no migration. The `Pending model changes` job runs `has-pending-model-changes` whenever a change touches `src/`, so the failure arrives before merge rather than at a host's startup. Configuration that produces no SQL still moves the model snapshot, so the job can fail on a change that alters no schema; the fix is a regenerated snapshot, never a hand-edited one.

## Critical repository rules

- Never add `Co-authored-by:` or any other co-author trailer to commits or pull requests.
- Never commit directly on `main` or `master`. Create a branch named `agent/<short-description>` before committing.
- Always create pull requests as drafts. Mark a pull request as ready for review only when the owner explicitly requests it.
- Preserve unrelated user changes. Stage only files that belong to the current task.
- Make architectural decisions before implementation. Keep changes small, reviewable, and aligned with the architecture draft in `specs/`.
- Treat ADRs under `docs/decisions/` as required architectural context for AI agents. Before changing architecture, boundaries, configuration, persistence, provider integration, governance, security-sensitive behavior, or cross-cutting infrastructure, read the relevant ADRs and keep the change consistent with their current status and rationale.
- Treat MailFathom as an enterprise-grade system even during early scaffolding: preserve seams for governance, auditability, privacy controls, operational hardening, compliance evidence, and future Agent Governance Toolkit (AGT) adoption without prematurely adding runtime dependencies.

## The project's own license

MailFathom is licensed under the Apache License, Version 2.0. That decision is recorded in five places and each one has a single job, so a change that touches licensing keeps them consistent rather than picking whichever is nearest.

- The root `LICENSE` is the unmodified official Apache-2.0 text. Never edit it, never append attribution, third-party terms, or commentary to it: GitHub detects the license by matching that file against the known text, and an edit turns a detected `Apache-2.0` into `NOASSERTION`.
- The root `NOTICE` names Krzysztof Kasprowicz as the original author, which is what Apache-2.0 section 4(d) asks a derivative distribution to preserve. It stays informational — it adds no use restriction, changes no license term, and claims nothing about contributions by other copyright holders. Third-party notice text may join the distributed bundle beside it, never inside it.
- `Directory.Build.props` carries the machine-readable form: `PackageLicenseExpression`, the copyright, and the SPDX assembly metadata that ships in the assemblies. `src/Host` copies `LICENSE` and `NOTICE` into its publish output and fails the publish when either is missing, so a native artifact cannot ship without them.
- `.editorconfig` holds the two-line `file_header_template` that IDE0073 enforces on every source file. Changing it rewrites every file in the repository, so treat it as a repository-wide change rather than a formatting tweak.
- The deployment assets state the same identifier where their own ecosystems read it: `org.opencontainers.image.licenses` in `deploy/docker/Dockerfile` and `artifacthub.io/license` in the chart's `Chart.yaml`. Those are claims about terms rather than the terms themselves, so a change touching either checks that both still name `Apache-2.0` and that the build context still admits `LICENSE` and `NOTICE` — a label that outlived the files it names is the failure worth catching. The release pipeline in #156 is what will assert it mechanically; until it exists this is read rather than run.

`README.md` and `CONTRIBUTING.md` carry none of those jobs and are still written to match them. Each explains the decision to a reader rather than recording it — that contributions arrive under the license by section 5 and need nothing signed, that contributors keep their copyright, and that the header the `.editorconfig` template applies is one project's mark rather than a claim about who wrote a line. A change to the five above therefore reads both, because prose that contradicts the record is what a contributor will act on.

`THIRD_PARTY_LICENSES.md` is none of the above. It reviews what MailFathom consumes; the rules below govern it.

## Third-party licensing

- MailFathom must remain compatible with both commercial closed-source distribution and open-source publication.
- Before adding, upgrading, replacing, bundling, or distributing a third-party component, verify the current upstream license from official project, package, or vendor sources.
- Only use third-party components whose licenses permit commercial use and do not require MailFathom itself to be relicensed or distributed as source code. Prefer permissive licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, and PostgreSQL License.
- Do not introduce GPL, AGPL, SSPL, BUSL, Commons Clause, PolyForm Noncommercial, source-available-only, field-of-use-restricted, or otherwise non-permissive dependencies without explicit owner approval.
- Treat hosted services, AI models, provider APIs, container images, generated assets, and copied code samples as separately reviewable from SDK licenses. A permissive SDK license does not approve the service terms, model terms, data-use policy, trademark terms, or redistribution conditions.
- Keep the root `THIRD_PARTY_LICENSES.md` third-party license register current. Add or update its entries in the same change set as any dependency, service, protocol SDK, container image, generated asset, or externally sourced code sample change.
- The register describes software the repository actually uses, never software it plans to use. A component that is proposed, evaluated, rejected, or merely named in a specification gets no row and no "planned" section, because a row asserts a completed review, and a review of something not yet adopted has to be redone at adoption anyway. Keep that reasoning in the specification, ADR, or issue that tracks the future work, and add the row in the change that introduces the dependency.
- The register is not MailFathom's own license and not a notice bundle. It records which third-party components are used and under which terms; MailFathom's own grant is the root `LICENSE` above, and the third-party notices that must travel with a distributed artifact are a third document that does not exist yet. #191 owns generating it from each artifact's resolved dependency graph; what it must satisfy is what the register already records.
- Record a component in the register section that matches its exposure. What ships carries redistribution and notice obligations, and what only builds, tests, or runs on a developer machine does not; a register that states exposure per row rather than by structure lets the two blur.
- When a dependency is pinned in `Directory.Packages.props`, record the exact package name, version, license expression, upstream URL, and any required attribution or NOTICE handling in `THIRD_PARTY_LICENSES.md`. Record the version the artifact's own graph resolves when nearest-wins resolution raises a pin that is only a floor.

## Documentation and test obligations

- Before using or changing a library, framework, protocol, CLI, or external API, consult its latest official documentation. Prefer Microsoft Learn, official project documentation, specifications, and upstream repositories.
- Confirm .NET 10 compatibility and pin package versions centrally in `Directory.Packages.props`. Do not use floating versions.
- Regenerate the lock files in the same change that moves a pin. `Directory.Packages.props` fixes the direct versions and the committed `packages.lock.json` files fix the transitive closure those versions resolve to, so the two are one decision recorded in two places. `AppHost` and `IntegrationTests` deliberately carry none, because the Aspire SDK picks part of their graph from the host platform's runtime identifier; do not add one back. Restore runs in locked mode everywhere it is gated, which fails with `NU1004` rather than quietly rewriting the closure; `dotnet restore MailFathom.slnx --force-evaluate` is what updates it. Review the resulting transitive diff, because that is the part central pinning never showed.
- Unit tests are part of every behavior change, feature, and bug fix. Read `tests/AGENTS.md` before adding or changing tests.
- Develop and verify tests and production code before documenting the implemented behavior. Update affected durable documentation in the same reviewable change set; stale guidance is a defect.
- Read `docs/AGENTS.md` before changing documentation. Create or modify ADRs only with explicit owner approval.
- Never edit `CHANGELOG.md` during ordinary work. It is a statement about a release rather than about a change, so it is written by the release pull request alone — the one `$prepare-release` opens, whose merge commit is tagged and published to the container registries. Composing a release's entries from the work merged since the previous tag is that skill's job; adding a line while implementing a task is not diligence, it is an edit to what a release will claim it shipped. The file is a protected path for the same reason, and `$check-docs-licenses` reports `n/a` for it on every ordinary change.
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

Some of the rules below are enforced by the build rather than by a reader. Those are listed here so nobody re-checks by hand what the compiler already rejects, and so a new rule lands in the mechanism that can enforce it instead of becoming another paragraph:

| Enforced by | Covers |
|---|---|
| `.editorconfig` diagnostic severities, with `TreatWarningsAsErrors` | Formatting, unnecessary usings, accessibility modifiers, file-scoped namespaces, sealing internal types, disposal (`CA2000`), and the rest of the configured `CA`/`IDE` set |
| `.config/BannedSymbols.txt`, through `Microsoft.CodeAnalysis.BannedApiAnalyzers` (`RS0030`) | Ambient clocks (`DateTime.Now`, `DateTimeOffset.UtcNow`, and siblings), `Thread.Sleep`, and the `System.Net.Mail` types |
| `Microsoft.VisualStudio.Threading.Analyzers` | Blocking on tasks and other async hazards |
| `Roslynator.*` and `xunit.analyzers` | General C# quality and xUnit usage |

Add a mechanically checkable rule to the mechanism, not to this file: a severity in `.editorconfig` when an analyzer already covers it, a line in `.config/BannedSymbols.txt` when the rule is "never call this". Prose here is for what a tool cannot decide — architecture, naming, and the reasoning behind a constraint. When a rule appears in both places, the tool is authoritative and this file explains why the rule exists.

- Target .NET 10 and use idiomatic modern C# supported by the pinned SDK.
- Keep the SDK version in `global.json`. Shared compiler and build settings belong in `Directory.Build.props`; shared package versions belong in `Directory.Packages.props`.
- Enable nullable reference types, implicit usings, deterministic builds, .NET analyzers, and code-style enforcement during builds.
- Treat compiler and analyzer warnings as errors in repository code. Suppress a diagnostic only at the narrowest scope and document the concrete reason.
- Maintain one repository `.editorconfig` and let automated formatting define whitespace and layout. Do not hand-format against configured rules.
- Give every enum member an explicit, unique integral value starting at `0` and increasing contiguously in declaration order. Never reorder, renumber, or reuse an existing value; append new members with the next value. Apply this to every enum, including private and currently non-persisted types, so a future numeric persistence representation cannot silently change meaning after refactoring.
- A `[Flags]` enum is exempt from contiguity and from nothing else. Its members are powers of two starting at `1`, with `None = 0` for the empty set, and each value is still explicit, never reordered, and never reused — which is what the contiguity rule was protecting. Declare one only where the values genuinely compose: a set an operator writes as one configuration value beats a collection whose elements a binder can drop one at a time, and `McpTransportAuthenticationMethods` is the worked example. A set of alternatives that never combine stays an ordinary enum.
- Keep a plain `enum` for a set whose members are only names the process reads. When a member has to carry data, expose behavior, or publish an identity that must survive a rename — a SASL name, a five-digit error code — the enum is the wrong shape, and the value becomes a closed enumeration instead: a `readonly record struct` with a private constructor, one static member per value, and its own serialization. Use `$closed-enumeration` for the required shape and the reasoning; `MailAuthenticationMechanism` and `MailFathomErrorCode` are the worked examples.
- Prefer immutable records or value objects for data that represents values; use entities only when identity and lifecycle matter.
- Use domain-correct, descriptive names for types, methods, parameters, variables, fields, and files. Avoid abbreviations except established terms such as IMAP, SMTP, MIME, MCP, UID, TLS, and RAG.
- Prefer a longer name that communicates intent, constraints, or result over a short ambiguous name. Long method names are acceptable when every word adds useful domain meaning.
- Keep names proportionate and avoid redundant context already supplied by the containing type or namespace. Do not produce sentence-like names when a smaller precise name communicates the same contract.
- Name methods after observable behavior or the result they produce. Avoid vague verbs such as `Handle`, `Process`, `Manage`, `Do`, or `Execute` unless the surrounding application pattern gives them a precise established meaning.
- Rename unclear identifiers as part of the code change that exposes them. Do not rely on comments to compensate for misleading or abbreviated names.
- Use `Email` for the mail artifact throughout `Domain`, `Application`, and `Infrastructure`: `EmailOccurrenceId`, `RemoteEmailMetadata`, `IEmailContentStore`, `StoredEmailEntity`. Do not name a mail type `Message` or `MailMessage`; the first is ambiguous once AI conversations exist and the second shadows `System.Net.Mail.MailMessage`. Name an AI conversation turn `ChatMessage` or `AgentMessage` after the domain concept, never after the layer.
- Do not rely on a namespace to disambiguate a type whose name is ambiguous on its own. A reader sees the name at the point of use, not the namespace. `Session` in particular must always be qualified: `IMailboxSession` for an open IMAP folder, `IPersistenceSession` for a local write transaction.
- Reach every type, enum, attribute, and static class through a `using` directive and write it by its simple name. The import list then states which boundaries the file depends on, in one place a reader can scan, instead of that information being spread across the call sites that happen to mention a namespace. `IDE0001` removes qualification that an existing `using` makes redundant, so the shape this rule addresses is the one the analyzer cannot see: a qualified name written because nobody added the import.
- Qualify a name only to resolve a collision, where two namespaces the file needs publish the same simple name: `MailKit.Security` and `System.Security.Authentication` both publish `AuthenticationException`, and `TransientFailureClassifier` sorts both. Qualify every side of such a collision rather than importing one of them, because an import leaves one of the two written as a bare name that a reader will read as the other. Keep the file's other types from those same namespaces qualified as well, so a namespace is not half imported and half spelled out.
- Do not introduce a `using` or `global using` alias. An alias gives a type a second name that exists in one file, so a search for the real name never reaches the code that uses it and a search for the alias finds nothing else. When the type's own name is wrong, rename the type; when it collides, qualify it at the point of use.
- Keep public APIs small and predictable. Default types and members to `internal`; use `public` only for an intentional cross-project contract or when a framework demonstrably requires public visibility.
- Declare `InternalsVisibleTo` as an MSBuild `<InternalsVisibleTo Include="..." />` item in the project file that grants the access. Do not add hand-written assembly-attribute source files for it, so the granted friend assemblies stay visible where the project's build contract is defined.
- Prefer one primary type per file and align namespaces with folders. File names match their primary type.
- Default concrete implementation classes that are not designed for inheritance to `internal sealed`. DI registration, configuration binding, EF Core mapping, and unit-test access do not by themselves justify a public type. Prefer composition over inheritance and do not use inheritance only to share implementation.
- Prefer guard clauses over deep nesting. Validate public boundary arguments with the appropriate BCL guard methods or explicit domain validation.
- Express work over a sequence as a LINQ pipeline rather than as a loop that rebuilds one. `Where`, `Select`, `SelectMany`, `GroupBy`, `OrderBy`, `Any`, `All`, `FirstOrDefault`, `Distinct`, `ToDictionary`, and `Sum` name the operation in the code itself, while a loop around an accumulator forces the reader to recover that name from a mutable variable and a set of branches.
- Replace a nested loop that exists only to reach an inner sequence with `SelectMany`. Two `foreach` statements wrapped around one body are a flattening or a pairing written by hand; naming it once leaves a single loop whose body is the work itself.
- Replace a search loop with `FirstOrDefault`, `SingleOrDefault`, `Any`, or `All`, and a loop that copies matching elements into a new collection with a filtered projection materialized through a collection expression, for example `[.. source.Where(predicate).Select(selector)]`.
- Replace an `if` inside a loop with the operator that names its role: a skipped element is `Where`, a per-element choice is a `switch` expression inside `Select`, and a split into two outcomes is two filtered sequences or a `GroupBy`. An `if`/`continue` pair states a filter without calling it one.
- Keep the loop when the body does something a query cannot express: mutating an existing collection, awaiting per element where ordering or cancellation matters, carrying a `TryParse`-style `out` parameter across iterations, working with `Span<T>` or `ref` locals, or producing several unrelated results in one pass. A LINQ pipeline describes a result and must never be the place a side effect happens.
- Stop chaining where the pipeline stops reading as one sentence. Name the intermediate sequence in a local, or move a multi-statement lambda into a named private method, rather than nesting operators until the shape has to be traced.
- Enumerate a sequence once. Materialize with `ToArray` or a collection expression before a result is filtered, counted, and read again, and never hand a lazily evaluated query to a caller that will iterate it more than once.
- Materialize at every level of a pipeline whose operators read or write state that outlives one element, such as a recursive walk carrying a visited set. Deferred execution makes the result depend on when the caller enumerates it rather than on the input alone, and a second enumeration observes the state the first one left behind.
- Do not use `null` to encode several states. Model optionality and failure explicitly when absence has domain meaning.
- Expose read-only collection abstractions when callers must not mutate state. Avoid returning mutable internal collections.
- For byte-oriented data, do not model payloads as `byte[]`, `List<byte>`, `IReadOnlyList<byte>`, or other general-purpose byte collections at application/domain boundaries. Prefer `Span<byte>` or `ReadOnlySpan<byte>` for synchronous stack-only operations, and `Memory<byte>` or `ReadOnlyMemory<byte>` when data must cross async, object, or DI boundaries. Keep `byte[]` only where a framework/provider contract requires it, such as EF Core `bytea` persistence models, and convert at that adapter boundary.
- Prefer pattern matching, switch expressions, collection expressions, and other modern syntax only when they make the intent clearer.
- Avoid reflection, `dynamic`, source-code generation, and unsafe code unless a measured requirement justifies them. This restricts authoring custom generators and generator-driven designs, not first-party framework generators such as `[LoggerMessage]`, `[GeneratedRegex]`, or `System.Text.Json` source generation, which are the recommended shape of those APIs.
- Use constructor injection. Avoid service locators, global mutable state, and static dependencies that hide collaborators.
- When a method, constructor, or primary-constructor parameter list has three or more parameters, put each parameter on its own line. If all involved type and parameter names are genuinely short, this may be deferred until four parameters. Keep the closing parenthesis and base/initializer on their own readable line when wrapping.
- Make I/O asynchronous end-to-end. Never block on tasks with `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`; the threading analyzers reject these.
- Suffix task-returning methods with `Async`, except framework-defined signatures where the ecosystem convention differs.
- Async methods that perform I/O accept and propagate `CancellationToken`. Put it last and do not replace it with `CancellationToken.None` inside a call chain.
- Use `await using` and `IAsyncDisposable` for asynchronously released resources. Dispose owned resources; never dispose dependencies owned by the DI container.
- When a type implements disposable ownership, implement the appropriate disposable contract explicitly: use `IDisposable` for synchronous resources, `IAsyncDisposable` for asynchronous resources, and implement both when the type owns both synchronous and asynchronous cleanup paths or can be consumed by both sync and async owners. Document ownership and disposal expectations.
- Use `Task` by default. Choose `ValueTask` only after measurement shows a meaningful benefit and its consumption constraints are acceptable.
- Avoid unbounded concurrency. Put explicit limits and backpressure around mailbox synchronization, MIME processing, embedding generation, and SMTP delivery.
- Do not use blanket `ConfigureAwait(false)` in ASP.NET Core application code. Use it only where a reusable library boundary has a documented reason.
- Use `DateTimeOffset` for timestamps and inject `TimeProvider` wherever current time affects behavior. `.config/BannedSymbols.txt` bans the ambient clock properties outright, so a test never depends on the wall clock and a delay is always cancellable.
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
- Before adding AGT or any governance/compliance package, verify the current official documentation, .NET 10 compatibility, license, service terms, telemetry behavior, and data-processing implications; update `THIRD_PARTY_LICENSES.md` for any dependency or externally sourced component.

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
- Take every package from the sources the repository's own `NuGet.config` declares. It clears the inherited source list so a feed configured on a developer machine cannot supply a dependency the license register never reviewed, and its package source mapping means a second source restores nothing until its packages are named explicitly. Adding a source is a licensing and supply-chain decision, so review it as one and record it in `THIRD_PARTY_LICENSES.md`.
- Do not expose EF Core entities, MailKit objects, MCP SDK types, or provider-specific AI types across application boundaries.
- Access raw RFC 822 content only through the application-owned `IEmailContentStore` port. Its initial implementation uses a dedicated PostgreSQL table, separate from email metadata.
- Keep PostgreSQL, Npgsql, and `bytea` details inside the initial content-store adapter so a future MinIO/S3 implementation does not change application use cases or domain types.
- Do not load raw MIME in ordinary mailbox queries or track large `bytea` values in EF Core unnecessarily.
- Apply database migrations explicitly. Do not run destructive or automatic production migrations during ordinary host startup.
- Use keyset pagination for email timelines and bounded result sizes for all public queries.
- Treat email content, OAuth tokens, credentials, certificate material, and embeddings as sensitive data.

## Issue tracking and the roadmap board

Work is tracked as GitHub issues on the `MailFathom roadmap` project board (project number `4`, owner `Krzysztof318`), which is the owner's view of progress. The board reflects the repository; it never becomes a second source of truth. `specs/` remains authoritative for what a change must do, and an issue links to its specification instead of restating it.

This repository is worked by agents. Issues are opened, filled in, labeled, placed on the board, and closed by an agent rather than by a person, so the conventions below are the whole mechanism rather than a description of one. Nothing here is tidied up afterwards by hand: an issue that arrives without its label and its board fields simply stops being visible in the views the owner reads. Apply every rule in this section as part of opening the issue, decide the values from the rules given, and state in the task brief what was set. Ask the owner only where a rule below says the choice is theirs.

Every rule below is therefore written from the position that an agent opened the issue. A public repository also receives issues and pull requests nobody here opened, which none of those rules reached; **Issues and pull requests from outside the project** governs those.

### Four questions, four mechanisms

Each question has exactly one owner, and no mechanism answers a question another one already answers. Adding a second mechanism for a question that already has one is the failure mode this structure exists to prevent.

| Question | Mechanism | Decided by |
|---|---|---|
| What kind of work is this? | A `type:*` label | the rules under **Labels** |
| Which release does it ship in? | The milestone | the rules under **Milestones** |
| Where is it in its lifecycle? | The board's `Status` field | the built-in board workflows, never by hand |
| What is being worked next, and what is deliberately not being worked? | The board's `Queue` field | the rules under **Board fields**; the owner chooses `Next`, and `$finish-change` also writes it when a pull request opens |

An open pull request moves both of the bottom two rows, and that is not the duplication this table forbids. `Status: In Progress` is the lifecycle fact and `Queue: Next` is what puts the item in front of the owner; they stay separate answers because a project view filters fields with `AND` and can therefore read only one of them. Asking `Now` for what the owner queued *or* what is in flight is not expressible, so the two conditions have to meet in one field for either to be visible there at all.

### The issue that governs a change

- Every change starts from an issue. Identify it during `$start-task`, before editing files, and name it in the task brief. Read the governing specification and ADR context first, because an issue body is written from them.
- Each numbered specification under `specs/` has exactly one issue, titled `Spec NN — <specification title>`. Create the issue in the same change set that adds a new specification, so a specification never exists without a tracked unit of work.
- Work that is not a numbered specification — maintenance, an ADR consequence, a defect — also gets an issue. State in its body that no `specs/` file backs it and name the ADR or the reason instead.
- Do not open a second issue for work an existing issue already covers. Extend the existing issue when scope grows and record why.

### Issue content

- Write issues in English, matching `specs/` and the rest of the repository.
- Every issue body carries two or three user stories and a condensed acceptance list. A specification issue additionally opens with a header block naming the roadmap group, the draft delivery stage, a link to the specification file, the issues it depends on, and the estimated change size.
- Do not copy specification text into an issue. The specification is the contract, and a duplicated copy goes stale silently.
- Express dependencies as issue references so the board shows them as links. Specification dependencies always point backwards to lower-numbered specifications.
- Nothing on the board schedules work. The owner works alone at irregular times, so order is recorded and timing is not. The board carries a one-week `Week` field, kept deliberately informational: no rule reads it, no view filters on it, and an issue is complete without it. Never make it load-bearing, never add a deadline, day estimate, sprint, or capacity field beside it, and do not read `Size` as one — it estimates a diff, not a duration.
- Do not use sub-issues. A parent standing over the issues a release needs answers the question the milestone already answers, and thematic grouping belongs to the `Track` field, so both shapes create a second hierarchy next to the roadmap. An issue that needs several others finished first says so through the dependency references its body already carries.

### Labels

Every issue carries exactly one `type:*` label and nothing else is required. The type names what the work produces, which is a property of the work itself, so it is chosen when the issue is opened and then left alone; it does not track progress and it never changes because circumstances did.

Several changes match more than one description — a defect in database wiring, a documentation-only change to this contract — so the table is a precedence list, not a menu. Read it top to bottom and take the first row that fits.

| Label | Use it for |
|---|---|
| `type:spec` | Work backed by a numbered specification under `specs/` |
| `type:decision` | Work whose deliverable *is* a decision: an ADR, a policy, or a measurement that settles a question |
| `type:defect` | Something already implemented behaves incorrectly, whatever part of the system it lives in |
| `type:docs` | Documentation only, under `docs/`, `README`, or `specs/` prose. `AGENTS.md` and `.agents/skills/` are the workflow contract, not documentation, and belong to the next row |
| `type:workflow` | Repository tooling, CI, verification scripts, the release process, and this workflow contract |
| `type:infra` | Orchestration, database wiring, telemetry, build and packaging plumbing |
| `type:feature` | Any remaining production-code change: a feature, a refactor, or hardening |

`type:decision` marks work only the owner can settle, and the `Decisions` view is read as a queue of that debt. It says what the issue *produces*, so it belongs on the issue that decides, not on the issues waiting for the answer — those keep the type of what they will eventually build and say they are waiting through `Queue: Needs decision`. Encoding one state in both places would leave the type stale the moment the decision landed.

The remaining labels are flags, applied only when they are true: `blocked` when an issue waits on something outside itself, `security` when a change needs a security review before it merges, and `good first issue` or `help wanted` once the repository is public. `shipped` is historical, marking the six issues written retrospectively for work that predates the roadmap; never apply it to new work.

### Milestones

A milestone answers which release an issue ships in, and nothing else. An issue with no milestone is deliberately outside the current release rather than merely unsorted, which is what makes the absence of one meaningful.

`0.1.0 — first public release` is the work of reaching a first release and the read side that makes the product usable: mailbox query read models, the email content read model, lexical search, the three MCP tools, and one baseline migration on a settled schema. Assign it to a new issue when the release as described cannot ship without that issue, and leave it empty otherwise; both are decisions the rule already makes, so neither needs asking. Widening what `0.1.0` means is the owner's call, so raise it rather than assigning a milestone that stretches the definition. Do not open a further milestone in advance; the next one is created when the current release closes.

### Board fields

The board carries three single-select fields beyond `Status`, plus an informational one. Set `Track` on every issue. Set `Queue` on every open issue. Leave `Size` empty until the work is planned, and leave `Week` alone entirely.

- **`Track`** groups every item by the area of the system it belongs to, including work no specification backs. `A` through `E` are the roadmap groups from `specs/README.md`. `Release` is release-process and distribution work — licensing, versioning policy, branching, contributor entry points, packaging, publication — and it says nothing about which release ships it, because that is the milestone's question. `Platform` is repository tooling and cross-cutting concerns no roadmap group owns. `Future capabilities` is beyond the current roadmap segment.
- **`Queue`** is the ordering signal, and a new issue takes one of its three lower values without asking. `Later` is the default: accepted scope not yet started. `Needs decision` says this issue waits on an answer rather than on effort; name the `type:decision` issue that produces the answer, or state that none exists yet. `Parked` records a review outcome or a side question that carries no commitment to act — something the project decided about and may return to, which is why it never stands in for work the project has declined or for an issue nobody has read yet. `Next` means in the owner's field of view now, and it has two writers. The owner sets it to mean ready to start, and at most five **open** issues hold it that way; the cap is what keeps the value a decision rather than a copy of everything already accepted. `$finish-change` sets it as well, on the issue its pull request closes, so that work already in flight is legible in the view the owner reads instead of only in the pull request list. Those sit outside the cap: an agent opening a pull request is not choosing what to start next and must never spend one of the five slots that decision uses. A closed issue keeps whatever `Queue` value it had and stops counting, which is why neither kind has to be cleared on merge and why every view that reads `Queue` filters `is:open`.
- **`Size`** measures the pull request in changed lines, additions plus deletions, including tests and documentation. The ranges are contiguous and leave no gap: `S` under 1000, `M` from 1000 to 2499, `L` from 2500 to 4999, `XL` from 5000 up and to be split before it starts. Read a specification's own line estimate through a factor of five, because that is what the nine merged specification pull requests measured — a median of 5.0 against the estimate, ranging from 2.6 to 7.3, never below. A specification that says 600 lines is an `L`. `L` is the normal size of a specification here, so an `XL` is a genuine warning rather than a large-sounding label.
- **`Week`** is informational and unused. It exists because a one-week grid is occasionally worth glancing at, not because anything depends on it. Do not set it, do not filter on it, and do not let a rule come to rest on it.

The built-in workflows set `Status` and nothing else, so a newly opened issue reaches the board with no `Track` and no `Queue`. Setting both is part of opening the issue:

```bash
gh project field-list 4 --owner Krzysztof318 --format json   # field ids and option ids
gh project item-list  4 --owner Krzysztof318 --format json   # item id for the issue
gh project item-edit --project-id <project-id> --id <item-id> \
  --field-id <field-id> --single-select-option-id <option-id>
```

Each field is a separate call, so one can land while another fails. A project view filters fields with `AND` and cannot ask for a missing `Track` *or* a missing `Queue` in one expression, which is why the `Triage` view catches only the untouched case. Audit both after placing an issue, and whenever the board is worth trusting:

```bash
gh project item-list 4 --owner Krzysztof318 --format json --limit 200 \
  | jq -r '.items[] | select(.status != "Done") | select(.track == null or .queue == null)
           | "\(.content.number) track=\(.track) queue=\(.queue)"'
```

A missing `Track` is also visible without running anything: the `Roadmap` view groups by `Track`, so an unplaced item sits in its own group at the end of the board.

### Views

`Now` is open issues with `Queue: Next`, grouped by `Status`, and it is the view the owner works from. That grouping is what separates the field's two writers without a second field: what the owner queued waits in `Todo`, and what a pull request carried in sits in `In Progress`, because the same event that set `Queue` also moved `Status` there. `Roadmap` is everything open grouped by `Track`. `Release 0.1.0` is the milestone. `Decisions` is the open `type:decision` issues — the answers the owner owes, not the work waiting on them. `Triage` lists open items with no `Queue` value. While the repository is private it is expected to be empty, because an agent sets `Queue` as part of opening an issue and an item sitting there means one did not. Publication turns the same view into the inbox for issues the project did not open, and **Issues and pull requests from outside the project** is what empties it. Every view that reads `Queue` filters `is:open`, so no `Next` value outlives its issue and a closed one never occupies one of the owner's five slots.

### Issues and pull requests from outside the project

An issue the project did not open arrives with no `type:*` label, no `Track`, no `Queue`, and no milestone, because none of the rules above reached its author. That is the expected shape of an arrival rather than a defect in it, and it is not corrected by inventing values at a glance.

The absence of a `type:*` label is what marks an issue untriaged, because an agent always sets one. Triage is therefore a state a reader can see without a field, a label, or a board column existing to announce it, which is why none was added: the four questions still have four mechanisms, and *has anyone read this* is answered by whether the first of them was ever asked.

Triage is one pass over the issue and it is not implementation. Read it, then either place it or end it:

- **Place it.** Assign exactly one `type:*` label, a `Track`, a `Queue`, and a milestone, by the same rules that govern an issue the project opened. `Later` is the value a placed arrival takes, and triage never assigns `Next`: that choice stays the owner's whoever opened the issue, and the other way into it is a pull request that does not exist yet. What the reporter asked for does not decide the label: a report that names a defect is `type:defect` even when it was written as a feature request.
- **End it.** Close it as `not planned` and state the reason on the issue. `Parked` is not that, for the reason the `Queue` rules give.

A question is not a unit of work and does not become one by arriving as an issue. Move it to Discussions and close the issue with a link, rather than giving it a `type:*` label so the board has somewhere to put it. Discussions carries `Q&A` for questions, `Ideas` for proposals that are not yet scope, and `Announcements` for what the project says; a discussion that turns out to be work is converted to an issue and then triaged like any other.

A pull request the project did not open is read in a fixed order, so a change is refused for the cheapest reason first: the required checks, then `Protected paths`, which refuses a change from anyone but the owner to `.github/`, `.config/`, `.agents/`, or `.claude/`, to an `.editorconfig`, `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, or to the repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, or `global.json` — and which names the paths it found either way, so an allowed change says which of them it moved. Only then comes the code-owner review the `main` ruleset requires. Nothing precedes those, and in particular no acknowledgement gate does: section 5 of Apache-2.0 puts a contribution under the project's license by the act of submitting it, so a check asking a contributor to state that it does adds a step to every first contribution and establishes nothing the license did not already establish. `CONTRIBUTING.md` says so where a contributor reads it. `Fathom review` runs on a fork only when a maintainer applies the `fathom-review` label — a fork's own pushes never start one — so a contributor waiting on that verdict is waiting on a decision rather than on a queue. A pull request whose author has stopped answering is closed with a comment saying so, and the issue it addressed keeps its own `Queue` value. Nothing does that automatically: at this project's volume, machinery that closes a contribution nobody read would cost more than the stale pull requests it removes.

### Linking a pull request to its issue

- Every pull request body contains `Closes #<issue>` for the issue it completes, so merging closes the issue and the board moves the item to `Done`.
- Add the reference when the pull request is created. `$finish-change` treats a pull request without an issue reference as an incomplete gate.
- `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit. Patch a pull request body through the REST API instead:

  ```bash
  gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body="$(cat body.md)"
  ```

- Once the pull request exists, set `Queue: Next` on the issue it closes, through the same `gh project item-edit` call that placed the issue. Do this for every pull request, whether the issue was opened for this task or had been sitting in `Later` for weeks, and treat a value that did not land as an incomplete gate rather than as a detail to fix later. Nothing else writes the field afterwards: the issue keeps `Next` until the merge closes it out of every view that reads `Queue`.

Writing it from the skill is not a shortcut past the automation; it is the only place the write can happen. The board's built-in workflows set `Status` and nothing else, so no project automation reaches a custom single-select field. A GitHub Actions workflow could, but this board belongs to a user rather than to an organization, and nothing scoped can write to a user's Projects v2 — a GitHub App has no permission that covers one, and a fine-grained token has no `Projects` scope at the account level. Only a classic token with the `project` scope can, and that scope is account-wide: storing one as a repository secret would give every workflow run write access to all of the owner's projects, to save a step in a skill that already holds such a token and already talks to this board. The cost is that a pull request opened outside `$finish-change` moves nothing, which for a repository whose pull requests are all opened by agents is a smaller gap than the credential would be.

### Status transitions

- The board's `Status` field has `Todo`, `In Progress`, and `Done`.
- The board's built-in workflows own every transition: `Auto-add to project` places a newly opened issue on the board, `Item added to project` puts it in `Todo`, `Pull request linked to issue` moves it to `In Progress`, and `Pull request merged`, `Auto-close issue`, and `Item closed` carry it to `Done`. Do not set those statuses by hand; a manual status that contradicts the automation hides the real state.
- `Status` records what has happened and `Queue` records what is intended, which is why neither substitutes for the other. Work that stalls keeps whatever `Status` the automation gave it and moves to `Later` or `Parked` in `Queue`.
- Automation does not add an issue that is already closed when it is created. Add a retrospective `shipped` issue to the board explicitly and set it to `Done`.
- When work stops without merging, say so on the issue and leave the status to the automation rather than moving the card.

## Agent workflow and verification

- For file-changing tasks, start with `$start-task`.
- Before final verification, use `$review-change`.
- To finish, use `$finish-change`; it requires `$check-docs-licenses`, full verification, focused staging, and a draft pull request.
- Use `scripts/inspect-workspace.sh` for a read-only workspace preflight.
- Read the version with `scripts/read-declared-version.sh` rather than retyping it. `<VersionPrefix>` in `Directory.Build.props` is the only file in the repository that carries an application version number, every project derives its four version properties from it centrally, and it names the *next* release rather than the last one. Everything that has to put that number somewhere gets it there at build or package time — the image's tags and labels through build arguments, the chart's `appVersion` through `helm package --app-version` — so no second copy exists to drift. The chart's own `version` is not an exception: it counts edits to the chart directory and never follows the application. `docs/operations/release-procedure.md` records the whole scheme.
- Never cut a release as part of a task. `$prepare-release` is manual-invocation only — its frontmatter sets `disable-model-invocation`, so an agent cannot reach it — because when a version becomes real is the owner's decision rather than something to infer from work that looks release-shaped. When the owner invokes it, it reads the version rather than asking, refuses the states that must not be released, opens the changelog and version-bump pull requests as drafts, and prints the order the two merges and the tag land in. It pushes no tag either: tagging stays the owner's act as well.
- Use `scripts/verify-fast.sh` during implementation. It restores, builds, tests, and then formats the C# files the branch changed, so a style diagnostic surfaces in the implementation loop instead of after the full gate has already restored tools and collected coverage. Formatting runs twice over that scope: a repairing pass rewrites everything with a code fix, and a `--verify-no-changes` pass reports by file and line whatever has none. Review the rewrite; the loop changes working-tree files by design.
- Never invoke `dotnet format` by hand. Both of its modes already run where they belong: the fast loop repairs the changed files and then names by file and line whatever has no code fix, and the final gate verifies the whole solution without touching it. A hand-run pass repeats about 30 seconds of workspace loading and analysis to produce what the loop just reported, and a hand-run repair on the whole solution costs 70 seconds and can rewrite files the change never touched. Fix what the loop reported, then run `scripts/verify-fast.sh` again.
- Rename a harness-created `worktree-<id>` branch to `agent/<short-description>` before editing, but only inside the linked worktree that owns it. `$start-task` treats that as its first corrective step rather than as a reason to stop; a branch in the primary checkout is never renamed.
- Stage the task files before running `scripts/verify-full.sh`; the gate rejects remaining untracked files so newly added files cannot bypass diff validation.
- Use `scripts/verify-full.sh` before committing. It fetches `origin main` and rejects a branch that does not contain that freshly fetched base, then runs the workflow contract suite, restores tools and packages, builds, runs the complete unit-test and coverage gate, verifies formatting, and checks the diff.
- Rebase onto the fetched `origin/main` when the base check fails, and treat an unreachable remote as a blocked gate. Verification against a stale base proves nothing about the branch that will actually merge.
- Review a change under `deploy/` by reading it. The repository runs no deployment against a local Docker daemon or a local Kubernetes cluster, and it holds no script that does: testing, building, and publishing the deployment assets belong to the release pipeline #156 owns, in one place, rather than to shell scripts a developer has to remember and a machine has to be equipped for. `helm lint` and `helm template` against `deploy/helm/mailfathom/ci/*-values.yaml` are the useful local reading; nothing gates on them.
- The `Container image` workflow builds the image for `linux/amd64` and `linux/arm64` and does nothing else. It is manual dispatch only, like the integration suite, and it publishes nothing — no registry credential reaches it.
- Use `scripts/run-integration-tests.sh` only when the owner asks for it. The integration suite starts a PostgreSQL container and applies the baseline migration to it, so it is deliberately absent from both verification scripts and from every pull-request workflow; its GitHub workflow is manual dispatch only. `tests/AGENTS.md` states what belongs in it.
- The `Fathom review` workflow reviews a published pull request on its own and spends Claude subscription usage doing it. It reports no status check and gates nothing, so it is never something to trigger as part of a task: `$review-change` is the review an agent performs, and a comment line beginning with `fathom-review` or `@fathom-review` is the owner's way of asking for another. The phrase only triggers a run when it leads a line, so writing it mid-sentence in prose about the workflow is safe and is how to refer to it. A published pull request is reviewed again on every push, so the verdict describes the head that will merge, and merging or closing it cancels a review still in flight rather than paying for a verdict on a change that has landed; a draft is skipped by design, and a comment is what reviews one anyway. A re-review waits for the pull request's conversation to go quiet before it reads anything, so replying to the previous pass's threads and pushing the fix in the same minute is safe in either order — the run collects the answers rather than reporting the finding again as unanswered. Those automatic re-reviews stop at ten on one pull request, past which a label or a comment is what asks for another. What it posts arrives as the `Fathom reviewer` GitHub App rather than as `github-actions`, and a run that found nothing approves the pull request with `APPROVED` and its summary as the body — so an approval from that App is a clean pass rather than a merge decision. A run that found something opens its body with `NEEDS CHANGES` instead, which is a heading and not a review state: it never satisfies the `main` ruleset, which requires an approving review from a code owner, and it never requests changes. `docs/operations/agent-workflow.md` describes what the run may touch, why it never checks out the branch it reviews, why its `pull_request_target` trigger is the one granted exception to the rule that forbids it everywhere else, what bounds the subscription usage it spends, and how the App is provisioned.
- Both verification scripts refuse to run on `main` or `master`, before the fetch and before any `dotnet` invocation, because a gate that passes on the integration branch describes code no change is about to touch and the base check cannot catch it: `origin/main` is trivially its own ancestor. Check out the branch that carries the change instead of working around the refusal.
- Inspect the final diff for accidental secrets, unrelated edits, generated files, and dependency-boundary violations.
