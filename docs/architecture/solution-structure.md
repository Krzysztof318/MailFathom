# Solution structure

MailMcp uses a clean-architecture modular monolith. Dependencies point inward from adapters and hosts toward application and domain contracts.

## Runtime projects

- `Domain` contains pure business concepts and invariants.
- `Application` contains use-case contracts and ports and references only `Domain`.
- `Infrastructure` implements persistence, IMAP/SMTP, message-content storage, security, and observability adapters.
- `AI` owns chunking, embeddings, retrieval, and agent-framework composition.
- `Mcp` maps MCP protocol requests and responses to application use cases.
- `Host` is the ASP.NET Core composition root.
- `AppHost` is the Aspire local-development orchestration host.

## Keeping compiled source inside its project

No project compiles source from outside its own directory. The Aspire service-defaults template scaffolds its extensions into a repository-root `shared/` directory and links them into each executable service with a `Compile Include="..\..\shared\..."` item, which pays for itself only when several services consume the same file. MailMcp has one such consumer, so the scaffold lives with it as `src/Host/ServiceDefaultsExtensions.cs` in the `MailMcp.Host` namespace.

That linked-source arrangement also cost visibility, because both CI workflows filter on `src/**` and `tests/**`: a file outside those paths changed a production assembly without triggering the build, unit-test, coverage, or formatting gates. Keeping every compiled file under `src/**` is what makes those path filters trustworthy.

If a second executable service ever needs these defaults, the answer is a project that both reference, not a source file linked into each.

`src/shared/` is the one deliberate exception in production code, and it holds one file: `RequiresIntegrationCoverageAttribute.cs`, linked into `Infrastructure` with a `Compile Include` item. The coverage collector recognizes the marker by attribute name, not by declaring assembly, so a shared project would buy nothing a shared file does not already give and would put a build-tooling reference into every boundary that marks a class — including `Domain`, whose reference set is the point of the architecture. The file sits under `src/**`, so the CI path filters that made the Aspire scaffold a problem still cover it, and the exception stays limited to markers that carry no behavior: anything with executable logic gets a project.

`tests/shared/` is the same exception on the test side, and holds `RecordingLoggerProvider.cs` together with `FakeHttpMessageHandler.cs` and the `RecordedHttpRequest` snapshot it records into. A test that asserts what a component logged — and what it kept out of the log — needs the same recorder whichever boundary it exercises, and the same holds for the handler that answers an HTTP call without a network. A test-only helper project would be a build artifact whose only consumers are test projects that already compile source together, and it would carry a second cost here: its assembly name would not end in `.UnitTests`, so the coverage filters would pull test-only code into the measured denominator, and the exclusion needed to keep it out would be indistinguishable from one added to reach the threshold. Linking leaves every consumer's assembly already excluded. The files sit under `tests/**`, so the CI path filters cover them, and the same limit applies: a helper is shared as source, anything with production behavior gets a project. Everything here uses the assembly-neutral `MailMcp.TestSupport` namespace.

## Naming an adapter after its library

A type inside an adapter carries the library's name only when its own members traffic in that library's types. `IMailKitImapClient` and `MailKitTransportSecurityMapping` take or return `SecureSocketOptions` and `IMailFolder`, so their names are accurate. `ImapAccountSettings` and `IImapAccountSettingsProvider` describe a host, a port, and credentials in plain IMAP vocabulary and would survive replacing the client library unchanged, so they live directly under `Infrastructure/Mail/` and name no vendor. The test is mechanical: if swapping the library would not change a single member, the name must not say the library.

## Test projects

Unit tests live under `tests/` and are split by production boundary. They use xUnit.net v3 on Microsoft Testing Platform v2 and NSubstitute for architectural-boundary doubles.

One project is named after a shared directory rather than a boundary, because the linked-source directories otherwise have no project that owns their contract: `SharedSources.UnitTests` compiles both `src/shared/` and `tests/shared/` the same way a consumer does. It carries the longer name because CA1716 rejects `Shared` as a namespace, which is a reserved word in another .NET language.

The coverage marker is asserted against a sample type declared beside its tests, carrying the marker on the type, a property, and a method. Pointing the assertion at a boundary that happens to apply the marker today would tie the marker's own contract to that boundary's contents, and it would also walk into the trap this arrangement sets: every consumer compiles its own copy of the marker from `src/shared`, so a copy compiled in one assembly and a copy applied in another are distinct types to the runtime, and `IsDefined(typeof(RequiresIntegrationCoverageAttribute))` across that boundary matches nothing while the assertion still passes. A local sample keeps the types identical, and the test reads the marker by name anyway, which is how the coverage collector recognizes it.

`FakeHttpMessageHandler` is the suite's only HTTP double, and it is hand-written because NSubstitute cannot produce one: `HttpMessageHandler.SendAsync` is protected, so no substitute can override it, and adding a third-party HTTP mocking package would introduce a second test-double mechanism without removing that constraint. It records each request as an immutable snapshot taken at send time, because the caller keeps ownership of the request message and may dispose, mutate, or reuse it before an assertion reads it, and answers either from a factory that builds a fresh response per request or from a script consumed one response per request. Two details exist to keep the double from behaving unlike a real transport: responses carry the request that produced them, which `HttpClient` populates only for its own stack, and concurrent requests are recorded in the order they reached the handler rather than in the order their bodies finished being read.

`Host.UnitTests` is the one project whose subject is excluded from the coverage denominator. `Host` stays excluded because a composition root's wiring would dilute the measurement of the boundaries that hold the logic, but exclusion from a metric is not exemption from testing: options validation rules, startup fail-fast behavior, and the worker's per-folder failure isolation are decisions the host owns and they are asserted like any other. Anything larger belongs in `Application` or `Infrastructure`, where it counts.
