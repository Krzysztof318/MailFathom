# Service Stack Development Instructions

These instructions apply under `backend/` in addition to the repository root instructions. They hold across `backend/src/`, `backend/tests/`, and `backend/tools/` alike, which is why they sit here rather than in either of the two files below: the root is loaded into every session in the repository, including the client sessions that have no `.cs` file to apply them to, and a rule stated in one of the two would be silently absent from the other.

`backend/src/AGENTS.md` adds what holds for the service's production code, `backend/tests/AGENTS.md` what holds for its suites, and `backend/src/Infrastructure/AGENTS.md` and `backend/src/Mcp/AGENTS.md` what holds for those two boundaries. Nothing here is restated in any of them.

## Documentation and test obligations

The root file states the half both stacks owe: consulting a library's current official documentation before using it, updating affected durable documentation in the same reviewable change set, and tests being part of every behavior change. What follows is how this stack discharges it.

- Confirm .NET 10 compatibility and pin package versions centrally in `backend/Directory.Packages.props`. Do not use floating versions.
- Regenerate the lock files in the same change that moves a pin. `backend/Directory.Packages.props` fixes the direct versions and the committed `packages.lock.json` files fix the transitive closure those versions resolve to, so the two are one decision recorded in two places. `AppHost` and `IntegrationTests` deliberately carry none, because the Aspire SDK picks part of their graph from the host platform's runtime identifier; do not add one back. Restore runs in locked mode everywhere it is gated, which fails with `NU1004` rather than quietly rewriting the closure; `dotnet restore backend/MailFathom.slnx --force-evaluate` is what updates it. Review the resulting transitive diff, because that is the part central pinning never showed.
- Read `backend/tests/AGENTS.md` before adding or changing a service test. It states the exceptions this suite has earned from the isolation rule the root file holds.
- The suites are xUnit.net v3 on Microsoft Testing Platform. Name a test `Member_Scenario_ExpectedBehavior`.

## .NET and C# conventions

These govern every `.cs` file under `backend/`, production code and test code alike. They sit here rather than in `backend/src/AGENTS.md` because the suites and the tooling projects are compiled by the same build with the same analyzers, so a convention stated for the service's production code alone would govern two thirds of the C# in the repository.

Some of the rules below are enforced by the build rather than by a reader. Those are listed here so nobody re-checks by hand what the compiler already rejects, and so a new rule lands in the mechanism that can enforce it instead of becoming another paragraph:

| Enforced by | Covers |
|---|---|
| `.editorconfig` diagnostic severities, with `TreatWarningsAsErrors` | Formatting, unnecessary usings, accessibility modifiers, file-scoped namespaces, sealing internal types, disposal (`CA2000`), and the rest of the configured `CA`/`IDE` set |
| `.config/BannedSymbols.txt`, through `Microsoft.CodeAnalysis.BannedApiAnalyzers` (`RS0030`), which `backend/Directory.Build.props` includes as an additional file for every project here | Ambient clocks (`DateTime.Now`, `DateTimeOffset.UtcNow`, and siblings), `Thread.Sleep`, the `System.Net.Mail` types, and the `HttpContent.ReadFromJsonAsync` overloads that deserialize through reflection |
| `Microsoft.VisualStudio.Threading.Analyzers` | Blocking on tasks and other async hazards |
| `Roslynator.*` and `xunit.analyzers` | General C# quality and xUnit usage |

Add a mechanically checkable rule to the mechanism, not to this file: a severity in `.editorconfig` when an analyzer already covers it, a line in `.config/BannedSymbols.txt` when the rule is "never call this". Prose here is for what a tool cannot decide — architecture, naming, and the reasoning behind a constraint. When a rule appears in both places, the tool is authoritative and this file explains why the rule exists.

- Target .NET 10 and use idiomatic modern C# supported by the pinned SDK.
- Enable nullable reference types, implicit usings, deterministic builds, .NET analyzers, and code-style enforcement during builds.
- Treat compiler and analyzer warnings as errors in repository code. Suppress a diagnostic only at the narrowest scope and document the concrete reason.
- Maintain one repository `.editorconfig` and let automated formatting define whitespace and layout. Do not hand-format against configured rules. A nested `.editorconfig` exists only to change a diagnostic's severity for one directory, never to restate layout.
- Give every enum member an explicit, unique integral value starting at `0` and increasing contiguously in declaration order. Never reorder, renumber, or reuse an existing value; append new members with the next value. Apply this to every enum, including private and currently non-persisted types, so a future numeric persistence representation cannot silently change meaning after refactoring.
- A `[Flags]` enum is exempt from contiguity and from nothing else. Its members are powers of two starting at `1`, with `None = 0` for the empty set, and each value is still explicit, never reordered, and never reused — which is what the contiguity rule was protecting. Declare one only where the values genuinely compose: a set an operator writes as one configuration value beats a collection whose elements a binder can drop one at a time, and `McpTransportAuthenticationMethods` is the worked example. A set of alternatives that never combine stays an ordinary enum.
- Keep a plain `enum` for a set whose members are only names the process reads. When a member has to carry data, expose behavior, or publish an identity that must survive a rename — a SASL name, a five-digit error code — the enum is the wrong shape, and the value becomes a closed enumeration instead: a `readonly record struct` with a private constructor, one static member per value, and its own serialization. Use `$closed-enumeration` for the required shape and the reasoning; `MailAuthenticationMechanism` and `MailFathomErrorCode` are the worked examples.
- Prefer immutable records or value objects for data that represents values; use entities only when identity and lifecycle matter.
- Use domain-correct, descriptive names for types, methods, parameters, variables, fields, and files. Avoid abbreviations except established terms such as IMAP, SMTP, MIME, MCP, UID, TLS, and RAG.
- Prefer a longer name that communicates intent, constraints, or result over a short ambiguous name. Long method names are acceptable when every word adds useful domain meaning.
- Keep names proportionate and avoid redundant context already supplied by the containing type or namespace. Do not produce sentence-like names when a smaller precise name communicates the same contract.
- Name methods after observable behavior or the result they produce. Avoid vague verbs such as `Handle`, `Process`, `Manage`, `Do`, or `Execute` unless the surrounding application pattern gives them a precise established meaning.
- Rename unclear identifiers as part of the code change that exposes them.
- Do not rely on a namespace to disambiguate a type whose name is ambiguous on its own. A reader sees the name at the point of use, not the namespace.
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
- Use `DateTimeOffset` for timestamps and inject `TimeProvider` wherever current time affects behavior. `.config/BannedSymbols.txt` bans the ambient clock properties outright, so a test never depends on the wall clock and a delay is always cancellable.
- Use structured logging with named properties. Never log credentials, tokens, message bodies, attachment content, or raw MIME.
- Do not wrap ordinary log calls in `ILogger.IsEnabled(...)`. The logging infrastructure already skips disabled levels, so the guard only adds noise. Use it exclusively when producing the log arguments themselves is expensive, for example serialization, formatting, LINQ materialization, large allocations, or an extra query. Prefer removing the cost or using compile-time `LoggerMessage` source-generated methods over adding a guard.
- Catch exceptions only when adding useful context, translating at a boundary, applying a defined retry policy, or completing cleanup. Preserve the original exception as `InnerException`.
- Use explicit result types for expected application failures; reserve exceptions for exceptional or infrastructure failures.
- Keep methods focused and classes cohesive. Prefer readable control flow over clever expressions or premature generic abstractions.
- Keep methods small enough to read as a single sequence of decisions. When a method mixes several responsibilities, nests loops around multi-step work, or needs a comment to announce its next stage, extract a named private method instead. Prefer extraction over longer bodies.
- Use blank lines to separate the logical blocks inside a method body so structure is visible before the code is read. Separate at minimum: the argument-guard block, each distinct step or stage, a block that builds state from the block that consumes it, and the final `return` from the work that produced it. Do not separate lines that belong to one step, and do not leave a blank line as the first or last line of a block.

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
- Do not introduce `ValueTask` at an application port, a domain contract, or a protocol boundary. Signatures there are consumed by code paths chosen later and must stay safe to compose; only an adapter with a measured hot path is a candidate.
- Consume a `ValueTask` exactly once: await it directly, or call `AsTask()` on it, and then treat the instance as spent. Awaiting twice, awaiting concurrently, calling `AsTask()` twice, mixing consumption techniques, or reading `.Result` or `GetAwaiter().GetResult()` before completion is undefined behavior, not a slow path, and can corrupt a pooled backing source.
- When a caller genuinely needs to hold or re-observe the result, convert once with `AsTask()`, or use `Preserve()` when the value must stay a `ValueTask`. Never store a raw `ValueTask` in a field, collection, or captured local.
- Never let `ValueTask` reach `Task.WhenAll`, `Task.WhenAny`, or any other combinator without converting. Needing that conversion is evidence the method should have returned `Task`.
- Write asynchronous methods so cancellation, timeouts, and exceptions behave identically whichever type is returned. Switching from `Task` to `ValueTask` is an allocation decision only; it must never change observable semantics.

## Comments and XML documentation in C#

This is the one place comment discipline is stated, and it governs every `.cs` file under `backend/` — production code, test code, and the tooling projects alike. It sits here rather than beside one of them because a comment is answerable to what a reader needs rather than to which project the file belongs to, and because the same rule would otherwise be written three times and drift in two of them. No file below restates any of it.

- A comment earns its place by carrying what the code cannot. Write one for the *why*: a non-obvious constraint, a workaround and what it works around, protocol behavior, an algorithm that is not evident from its steps, a concurrency assumption, a security implication, a compatibility requirement, a performance decision taken after measurement, or a choice a later reader would otherwise quietly undo. Do not write one for the *what*, which the statement beneath it already says.
- Do not narrate an implementation step by step. `// Create the request`, `// Get the user`, `// Return the result`, `// Check if null`, and `// Loop through items` above the lines that do exactly that are a second copy of the code, and the copy is the one that goes stale. The repair is never a better comment: it is a name that says what the value is, a small private method that says what the block does, and structure a reader can follow. Rename an unclear identifier in the change that exposed it rather than explaining it in passing, and never let a comment compensate for a misleading or abbreviated name.
- A test method is the exception: it carries explicit `// Arrange`, `// Act`, and `// Assert` markers. They label the phases of a test rather than narrate its statements, and a reader scanning an unfamiliar suite finds the boundary between setup and assertion by them. Nothing else in a test is exempt.
- Never add comments to make generated code look documented. Comment density is not evidence of care, and an agent that writes more of them than a person would has made the file harder to read rather than better documented.
- Write `TODO` or `FIXME` only for a concrete unresolved issue, and with enough context to act on it: what is wrong, and what would resolve it. A marker naming nothing in particular is noise, and anything worth returning to is worth the issue `docs/operations/issue-tracking.md` describes.
- Whether a public member carries XML documentation is not a judgement, because the build already took it: `backend/Directory.Build.props` sets `GenerateDocumentationFile`, only test projects suppress `CS1591` and `CS1573`, and `TreatWarningsAsErrors` turns each of them into an error — so an undocumented public member in production code fails the build, and so does a documented one whose parameters are not. What is left to judge is what the documentation says. Write the contract the signature does not carry — behavior, constraints, side effects, exceptions, cancellation semantics, ownership, and lifetime — through `<summary>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`, and `<example>`, each where it adds concrete information. A `<summary>` restating the member name, or a `<param>` restating the parameter's name and type, satisfies the compiler and tells a reader nothing; it is the narrating comment above in a documented-looking form, and a member that genuinely says everything in its signature is answered by a sentence naming its purpose or its constraint rather than by an echo. Never answer either diagnostic by silencing it: neither is added to a `NoWarn`, and a `#pragma warning disable` around an undocumented member is a defect rather than a shortcut. Nothing in this section reduces how much of the public surface is documented — it decides what that documentation is worth reading for.
- Below that public surface nothing compels documentation, so do not add it mechanically to every class and member. Document an internal interface, an extension point, a domain invariant, a protocol boundary, a concurrency rule, security-sensitive behavior, or a non-obvious lifecycle or ownership requirement; leave the rest to naming.
- A link inside a documentation comment is absolute. A relative path resolves to nothing from a source file in an editor and to nothing in the generated reference, so a `<see href>` naming an ADR, a deployment asset, or anything else outside that reference carries its full `https://github.com/Krzysztof318/MailFathom` URL. `docs/AGENTS.md` holds the rule for the pages themselves, where a link between published pages stays relative instead.
- Keep a comment and a documentation block true when the change makes them false. Stale documentation is a defect in the change that stranded it, and fixing it belongs to that change. What stays out is unrelated cleanup: do not rewrite or delete comments the task did not touch, so what a reviewer reads is the change and nothing else.


## Architecture

The root file states which stack owns which build contract and how the two meet. What follows is the service's own shape.

- Build a clean-architecture modular monolith with clear `Domain`, `Application`, `Infrastructure`, `AI`, `Mcp`, `Host`, and `Cli` boundaries.
- `Domain` contains business concepts and invariants and has no dependency on infrastructure frameworks.
- `Application` contains use cases and ports and depends only on `Domain`.
- `Infrastructure` implements persistence, IMAP/SMTP, message-content storage, security, and observability ports.
- `AI` owns retrieval, chunking, embeddings, and agent-framework composition without leaking provider-specific types into `Application` or `Domain`.
- `Mcp` maps protocol inputs and outputs to application use cases. It contains no persistence or email-protocol logic.
- `Host` is a composition root only: configuration, dependency injection, middleware, endpoints, workers, and process lifetime.
- Keep email retrieval read-only. Synchronization and content retrieval must never set the remote IMAP `\Seen` flag. A change the mailbox owner authored may move it, and reaches the server only through the write session ADR 0007 defines, which no read path can obtain — a rule, the spam verdict, or an MCP caller.

## Reliability and performance

The root file holds what both stacks owe on timeouts, retries, untrusted input, secure randomness, least privilege, and measuring before optimizing. What follows needs the service's workers, its protocol handling, or its database.

- Make worker leases and state transitions durable where a process crash could otherwise duplicate or lose work.
- Prefer bounded channels or queues for background pipelines. Record queue depth, processing duration, failures, and retry counts without recording message content.
- Prefer database projections before low-level micro-optimizations, and stream large MIME content and attachments rather than buffering them repeatedly.

## Cross-boundary email invariants

- Treat `(account, folder, UIDVALIDITY, UID)` as the stable remote occurrence identity.
- Keep MCP reads local; an MCP request must not trigger a synchronous IMAP fetch.
- Make synchronization, object writes, indexing, and SMTP outbox processing idempotent.

## Dependency and implementation discipline

The root file holds the two rules that reach both stacks: keeping a third-party type inside its owning adapter, and preferring a platform capability before adding a package with a clear owner and purpose. What follows is this stack's.

- Take every package from the sources the repository's own `NuGet.config` declares. It clears the inherited source list so a feed configured on a developer machine cannot supply a dependency the license register never reviewed, and its package source mapping means a second source restores nothing until its packages are named explicitly.
- Do not expose EF Core entities, MailKit objects, MCP SDK types, or provider-specific AI types across application boundaries.
- Access raw RFC 822 content only through the application-owned `IEmailContentStore` port. Its initial implementation uses a dedicated PostgreSQL table, separate from email metadata.
- Keep PostgreSQL, Npgsql, and `bytea` details inside the initial content-store adapter so a future MinIO/S3 implementation does not change application use cases or domain types.
- Do not load raw MIME in ordinary mailbox queries or track large `bytea` values in EF Core unnecessarily.
- Apply database migrations explicitly. Do not run destructive or automatic production migrations during ordinary host startup.

## Verification

The root file names the entry points and the rules that hold before one is reached. Two of them are about this solution and nothing else.

- Never invoke `dotnet format` by hand. Both of its modes already run where they belong — the fast loop repairs the files the branch changed and the verifying mode runs in the full gate, both scoped to this solution's changed files. Neither has to *report* most style defects, because the Release build in front of it already does: `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` turn `IDE0005`, `IDE0073`, and `IDE0055` into errors naming their file and line. `IDE0060` is the exception on both counts — no code fix for the repairing pass to apply, and no build failure despite `.editorconfig` setting it to `warning` — so it reaches a reader only through a verifying pass. What the repairing pass adds is the part no build reports and only a rewrite fixes: the ordering of using directives and a missing final newline. Fix what the build named, then run `scripts/verify-fast.sh` again.
- Use `scripts/run-integration-tests.sh` only when the owner asks for it. The suite starts a PostgreSQL container, so it is deliberately absent from both verification scripts and from every pull-request workflow, and its GitHub workflow is manual dispatch only. `backend/tests/AGENTS.md` states what belongs in it.
