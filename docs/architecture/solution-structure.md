# Solution structure

<!-- describes: MailFathom.slnx, Directory.Build.props, src/*/*.csproj, tests/*/*.csproj, src/Host/ServiceDefaultsExtensions.cs -->

MailFathom uses a clean-architecture modular monolith. Dependencies point inward from adapters and hosts toward application and domain contracts.

## Runtime projects

- `Domain` contains pure business concepts and invariants.
- `Application` contains use-case contracts and ports and references only `Domain`.
- `Infrastructure` implements persistence, IMAP/SMTP, message-content storage, security, and observability adapters.
- `AI` owns chunking, embeddings, retrieval, and agent-framework composition.
- `Mcp` maps MCP protocol requests and responses to application use cases.
- `Host` is the ASP.NET Core composition root.
- `Cli` is the administration command, and it references `Common` alone. It reaches a deployment over HTTP through the
  administrative endpoint and holds no other capability, which is what lets it publish as a trimmed self-contained
  binary per platform: a reference to `Infrastructure` would put EF Core, Npgsql, and MailKit into an artifact that
  calls none of them, and would make trimming it impossible. Its assembly is named `mfctl` rather than after its
  boundary, because the published file is something an operator types repeatedly; the project, its directory, and its
  namespace stay `Cli`.
- `Common` is cross-cutting code that belongs to no boundary and depends on nothing but the base class library. It is
  not a layer and has no place in the dependency ordering above: `Cli` and, in time, `Infrastructure` reach it, and it
  reaches nothing. Admission is narrow on purpose — code arrives when a second boundary genuinely needs it, and lives
  with its one consumer until then — because a project defined by what it is not becomes the drawer everything ends up
  in.
- `AppHost` is the Aspire local-development orchestration host.

`Cli` also authorizes a mailbox, because one thing a headless service structurally cannot do is ask a person to sign
in, and a mailbox at a provider that has withdrawn password authentication needs exactly that once. Keeping it in the
operator's command is what lets the host serve no consent page, own no redirect endpoint, and hold no
authorization-server credential it has no run-time use for; [mailbox OAuth](../operations/mailbox-oauth.md) describes
the exchange it performs.

## What the published artifact carries

`Host` is the project that gets published, so it is also where the project's licensing travels with the binaries. Its publish output includes the repository-root `LICENSE` and `NOTICE` beside the assemblies, and a `VerifyPublishedLicenseAndNotice` target fails the publish when either is missing from the output directory. The check inspects the artifact rather than the source tree, because the failure worth preventing is an artifact that ships without its license — a missing source file would otherwise resolve to an empty item and publish quietly.

The identity in those files is declared once, in `Directory.Build.props`: the product name, the author, the copyright, the repository URL, `PackageLicenseExpression`, and an `SPDX-License-Identifier` assembly metadata entry that puts the identifier into every assembly, since nothing here is packed as a NuGet package. Each source file repeats the grant, and names the repository the file came from, in the three-line header that `.editorconfig` defines and `IDE0073` enforces. The same three lines are carried by hand outside the solution, where that analyzer does not reach — the workflows, the shell scripts, the Helm chart, and the skills — and `scripts/test-agent-workflow.sh` compares each of them against the one template.

## Keeping compiled source inside its project

No project compiles source from outside its own directory. The Aspire service-defaults template scaffolds its extensions into a repository-root `shared/` directory and links them into each executable service with a `Compile Include="..\..\shared\..."` item, which pays for itself only when several services consume the same file. MailFathom has one such consumer, so the scaffold lives with it as `src/Host/ServiceDefaultsExtensions.cs` in the `MailFathom.Host` namespace.

That linked-source arrangement also cost visibility, because the `CI` workflow decides in its `Detect changes` job whether the build and formatting jobs have anything to do, and both of its filters rest on `src/**` and `tests/**`: a file outside those paths changed a production assembly without triggering the build, unit-test, coverage, or formatting gates. Keeping every compiled file under `src/**` is what makes those filters trustworthy.

If a second executable service ever needs these defaults, the answer is a project that both reference, not a source file linked into each.

`src/shared/` is the one deliberate exception in production code. `RequiresIntegrationCoverageAttribute.cs` is why it exists: the coverage collector recognizes the marker by attribute name, not by declaring assembly, so a shared project would buy nothing a shared file does not already give and would put a build-tooling reference into every boundary that marks a class — including `Domain`, whose reference set is the point of the architecture. `StampedAssemblyVersion.cs` sits beside it and reads the version attributes an SDK stamps into whichever assembly it is handed, which every consumer needs about itself. Both sit under `src/**`, so the change filters that made the Aspire scaffold a problem still cover them, and the exception stays narrow: anything with executable logic that a caller reaches for as a capability gets a project, which is what `Common` is.

`tests/shared/` is the same exception on the test side, and holds `RecordingLoggerProvider.cs` together with `FakeHttpMessageHandler.cs` and the `RecordedHttpRequest` snapshot it records into. A test that asserts what a component logged — and what it kept out of the log — needs the same recorder whichever boundary it exercises, and the same holds for the handler that answers an HTTP call without a network. A test-only helper project would be a build artifact whose only consumers are test projects that already compile source together, and it would carry a second cost here: its assembly name would not end in `.UnitTests`, so the coverage filters would pull test-only code into the measured denominator, and the exclusion needed to keep it out would be indistinguishable from one added to reach the threshold. Linking leaves every consumer's assembly already excluded. The files sit under `tests/**`, so the change filters cover them, and the same limit applies: a helper is shared as source, anything with production behavior gets a project. Everything here uses the assembly-neutral `MailFathom.TestSupport` namespace.

## Naming an adapter after its library

A type inside an adapter carries the library's name only when its own members traffic in that library's types. `MailKitImapConnection`, `MailKitRemoteFolderCatalog`, and `MailKitTransportSecurityMapping` take or return `IImapClient`, `IMailFolder`, and `SecureSocketOptions`, so their names are accurate. `ImapAccountSettings` and `IImapAccountSettingsProvider` describe a host, a port, and credentials in plain IMAP vocabulary and would survive replacing the client library unchanged, so they live directly under `Infrastructure/Mail/` and name no vendor. The test is mechanical: if swapping the library would not change a single member, the name must not say the library.

The adapter consumes MailKit's own `IImapClient` rather than a port restating it. An interface this repository declares has to earn its existence — a seam the library leaves open, behavior of its own, or a surface deliberately narrowed for a stated safety reason. A member-for-member copy of a published interface is none of those: it buys no replaceability, because every one of its members would change with the library anyway, and it goes stale the moment the library moves. Replaceability lives one layer up, at `IMailboxSession`, `IMailboxSessionFactory`, and `IRemoteFolderCatalog`, which traffic in domain types and keep mail-library types out of `Application`.

## Interfaces this repository declares

Every interface declared under `src/` was checked against that rule once the MailKit copy was removed, and none of the survivors restates a published contract. Each one now names the reason it exists in its own documentation, so the next interface added is judged by the rule rather than by resembling the ones already here.

The ports in `Application` exist because the alternative is a library type in a use-case signature. `IMailboxSession`, `IMailboxSessionFactory`, and `IRemoteFolderCatalog` traffic in domain types and keep the mail library out; `IEmailMetadataRepository`, `IEmailContentStore`, `ISynchronizationCheckpointStore`, `IMailFolderResolutionStore`, `IPersistenceSession`, and `IPersistenceSessionFactory` do the same for EF Core, which publishes nothing at this altitude beyond a transaction handle and a concrete `DbContext`. Several also narrow deliberately: the mailbox session and the folder catalog expose no operation capable of setting `\Seen`, `IMailTransportSecurityPolicyReader` hands an adapter a validated policy it can only obey, `IMailFolderMappingChangeAuditor` is the only way to write a remote folder path outside the database, and `ITransientFailureClassifier` lets a supervisor ask the question the retry pipeline asks itself without depending on the resilience library that owns it.

The contracts declared in `Infrastructure` carry behavior no library offers. `ISecretReferenceResolver` and `ISecretSchemeResolver` are the extension point that keeps a managed secret store a registration rather than a refactor, and they are deliberately unreachable from `Application` so no use case gains the ability to ask for any secret by name. `IDatabaseConnectionSettingsValidator` asks what resolving a candidate's references does not answer — whether the setting that supplies the credential still has the shape the pool was composed around, and whether the resolved material parses into a connection string that carries a password when that secret is what must supply one — because material that resolves but does not parse would replace the last known good settings and fail every connection opened afterwards, and `IImapAccountSettingsProvider` resolves an account's secrets at the moment a connection is made so no settings object holds a live password between operations.

The rest sit close enough to a published type that the verdict is worth recording against it. `ISettingsSnapshot<TSettings>` is `IOptionsMonitor` reduced to one member and given a publication rule the framework has no equivalent for: a candidate becomes current only after every secret reference in it resolves. `IEfCorePersistenceSessionResources` covers what `IDbContextTransaction` does not — saving tracked changes, classifying a provider update failure as an optimistic conflict, clearing tracked state — and exists because the type holding the rest is a concrete class no fake provider may stand in for, while `IEfCorePersistenceSession` is the one-member seam that lets a write repository take its context from the caller's session instead of injecting its own. `ISecretFileReader` and `IEnvironmentVariableReader` wrap BCL statics that publish no interface at all, which is the plainest form of a seam the library leaves open. `IDatabaseSchemaInspector` exists so the composition root can refuse to start against a schema the build does not recognize without taking on EF Core itself: whether a pending migration is fatal is a host decision, and reading the migration history is a persistence one. It reads and never writes, because applying a migration is a reviewable deployment step rather than something an adapter may decide to do.

## Test projects

Unit tests live under `tests/` and are split by production boundary. They use xUnit.net v3 on Microsoft Testing Platform v2 and NSubstitute for architectural-boundary doubles.

One project is named after a shared directory rather than a boundary, because the linked-source directories otherwise have no project that owns their contract: `SharedSources.UnitTests` compiles both `src/shared/` and `tests/shared/` the same way a consumer does. It carries the longer name because CA1716 rejects `Shared` as a namespace, which is a reserved word in another .NET language.

The coverage marker is asserted against a sample type declared beside its tests, carrying the marker on the type, a property, and a method. Pointing the assertion at a boundary that happens to apply the marker today would tie the marker's own contract to that boundary's contents, and it would also walk into the trap this arrangement sets: every consumer compiles its own copy of the marker from `src/shared`, so a copy compiled in one assembly and a copy applied in another are distinct types to the runtime, and `IsDefined(typeof(RequiresIntegrationCoverageAttribute))` across that boundary matches nothing while the assertion still passes. A local sample keeps the types identical, and the test reads the marker by name anyway, which is how the coverage collector recognizes it.

`FakeHttpMessageHandler` is the suite's only HTTP double, and it is hand-written because NSubstitute cannot produce one: `HttpMessageHandler.SendAsync` is protected, so no substitute can override it, and adding a third-party HTTP mocking package would introduce a second test-double mechanism without removing that constraint. It records each request as an immutable snapshot taken at send time, because the caller keeps ownership of the request message and may dispose, mutate, or reuse it before an assertion reads it, and answers either from a factory that builds a fresh response per request or from a script consumed one response per request. Two details exist to keep the double from behaving unlike a real transport: responses carry the request that produced them, which `HttpClient` populates only for its own stack, and concurrent requests are recorded in the order they reached the handler rather than in the order their bodies finished being read.

`Host.UnitTests` is the one project whose subject is excluded from the coverage denominator. `Host` stays excluded because a composition root's wiring would dilute the measurement of the boundaries that hold the logic, but exclusion from a metric is not exemption from testing: options validation rules, startup fail-fast behavior, and the worker's per-folder failure isolation are decisions the host owns and they are asserted like any other. Anything larger belongs in `Application` or `Infrastructure`, where it counts.
