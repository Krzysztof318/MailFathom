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

`src/shared/` is the one deliberate exception, and it holds one file: `RequiresIntegrationCoverageAttribute.cs`, linked into `Infrastructure` with a `Compile Include` item. The coverage collector recognizes the marker by attribute name, not by declaring assembly, so a shared project would buy nothing a shared file does not already give and would put a build-tooling reference into every boundary that marks a class — including `Domain`, whose reference set is the point of the architecture. The file sits under `src/**`, so the CI path filters that made the Aspire scaffold a problem still cover it, and the exception stays limited to markers that carry no behavior: anything with executable logic gets a project.

`tests/shared/` is the same exception on the test side, and holds `RecordingLoggerProvider.cs`. A test that asserts what a component logged — and what it kept out of the log — needs the same recorder whichever boundary it exercises, and a test-only helper project would be a build artifact whose only consumers are test projects that already compile source together. The file sits under `tests/**`, so the CI path filters cover it, and the same limit applies: a helper is shared as source, anything with production behavior gets a project.

## Naming an adapter after its library

A type inside an adapter carries the library's name only when its own members traffic in that library's types. `IMailKitImapClient` and `MailKitTransportSecurityMapping` take or return `SecureSocketOptions` and `IMailFolder`, so their names are accurate. `ImapAccountSettings` and `IImapAccountSettingsProvider` describe a host, a port, and credentials in plain IMAP vocabulary and would survive replacing the client library unchanged, so they live directly under `Infrastructure/Mail/` and name no vendor. The test is mechanical: if swapping the library would not change a single member, the name must not say the library.

## Test projects

Unit tests live under `tests/` and are split by production boundary. They use xUnit.net v3 on Microsoft Testing Platform v2 and NSubstitute for architectural-boundary doubles.

`Host.UnitTests` is the one project whose subject is excluded from the coverage denominator. `Host` stays excluded because a composition root's wiring would dilute the measurement of the boundaries that hold the logic, but exclusion from a metric is not exemption from testing: options validation rules, startup fail-fast behavior, and the worker's per-folder failure isolation are decisions the host owns and they are asserted like any other. Anything larger belongs in `Application` or `Infrastructure`, where it counts.
