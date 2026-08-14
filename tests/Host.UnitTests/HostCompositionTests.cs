// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using MailFathom.Host.Hosting;
using MailFathom.Host.Hosting.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Composes the real service graph, in each shape a deployment can be configured into.</summary>
/// <remarks>
/// <para>
/// A container resolves a factory-registered service the first time something asks for it, so a dependency nobody
/// registered is not a failure the composition reports: it is an exception out of whichever worker asked first, minutes
/// after the process reported itself healthy. <c>ValidateOnBuild</c> answers half of that and no more — it inspects the
/// constructors the container can see, and most of what the composition root registers is a factory it cannot see
/// through. The other half is why every registration this repository owns is resolved here rather than validated,
/// health checks included — those are appended to an options object rather than to the service collection, so
/// nothing else in this pass would reach the factory each one is built by.
/// </para>
/// <para>
/// Which registrations exist at all follows from configuration, so one composition proves one deployment. The shapes
/// below are the switches that decide it — an embedding chain, a chat endpoint, its relevance filter, each scanner, the
/// spam daemon, and which surfaces are served — because a section nobody turned on in a test is a section whose
/// registrations are first composed on somebody's server.
/// </para>
/// <para>
/// Nothing here starts a host. The graph is built from the service collection directly, so no listener is opened, no
/// migration runs, and no connection is made: a data source is constructed from a connection string without dialling
/// it, and a hosted service is constructed without being started.
/// </para>
/// </remarks>
public sealed class HostCompositionTests
{
    /// <summary>The one setting every shape carries, because a deployment reaching no database is not one worth composing.</summary>
    private static readonly KeyValuePair<string, string?>[] Database =
    [
        new("ConnectionStrings:mailfathom", "Host=localhost;Database=mailfathom;Username=mailfathom"),
    ];

    /// <summary>One declared embedding endpoint, which is what makes the deployment one that embeds.</summary>
    private static readonly KeyValuePair<string, string?>[] EmbeddingChain =
    [
        new("Embeddings:Endpoints:0:Alias", "embeddings"),
        new("Embeddings:Endpoints:0:Provider", "example"),
        new("Embeddings:Endpoints:0:Model", "text-embedding-3-small"),
        new("Embeddings:Endpoints:0:ModelVersion", "1"),
        new("Embeddings:Endpoints:0:Dimension", "1536"),
        new("Embeddings:Endpoints:0:Address", "http://models.example.test/v1/"),
        new("Embeddings:Endpoints:0:Unauthenticated", "true"),
    ];

    /// <summary>The declared chat endpoint, which is what makes the deployment one that answers questions.</summary>
    private static readonly KeyValuePair<string, string?>[] ChatEndpoint =
    [
        new("Chat:Alias", "chat"),
        new("Chat:Model", "a-model"),
        new("Chat:Address", "http://models.example.test/v1/"),
        new("Chat:Unauthenticated", "true"),
    ];

    /// <summary>Each deployment shape, as the configuration an operator would have written to reach it.</summary>
    /// <remarks>
    /// Written as configuration rather than as flags, because that is the input the composition actually reads: the
    /// sections that decide which services exist are read from <see cref="IConfiguration" /> while the services are
    /// being registered, before a container able to resolve an options snapshot exists.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string?>>> Shapes =
        new Dictionary<string, IReadOnlyList<KeyValuePair<string, string?>>>(StringComparer.Ordinal)
        {
            ["probes only"] = [],
            ["mcp and admin served"] =
            [
                new("McpEndpoint:Enabled", "true"),
                new("McpEndpoint:Authentication:0:ApiKey:Name", "workstation"),
                new("McpEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key"),
                new("AdminEndpoint:Enabled", "true"),
                new("AdminEndpoint:Port", "8082"),
                new("AdminEndpoint:Authentication:0:ApiKey:Name", "operator"),
                new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key-either"),
            ],
            ["mail synchronized"] =
            [
                new("MailSynchronization:Enabled", "true"),
                new("MailSynchronization:Accounts:0:AccountId", "personal"),
                new("MailSynchronization:Accounts:0:DisplayName", "Personal"),
                new("MailSynchronization:Accounts:0:Host", "imap.example.test"),
                new("MailSynchronization:Accounts:0:UserName", "someone@example.test"),
                new("MailSynchronization:Accounts:0:Secrets:Password:Name", "mailbox-password"),
                new("MailSynchronization:Accounts:0:Secrets:Password:SecretReference", "plaintext:not-a-real-password"),
            ],
            ["embedding chain declared"] = EmbeddingChain,
            ["chat declared"] = ChatEndpoint,
            ["chat judging its own retrieval"] =
            [
                .. EmbeddingChain,
                .. ChatEndpoint,
                new("Chat:RelevanceFilter:Enabled", "true"),
            ],
            ["secret scanning"] =
            [
                new("SensitiveContent:Secrets:Enabled", "true"),
            ],
            ["personal-data scanning"] =
            [
                new("SensitiveContent:Pii:Enabled", "true"),
                new("SensitiveContent:PersonalDataAnalyzer:Endpoint", "http://analyzer.example.test:5001"),
            ],
            ["spam classified by a scanner"] =
            [
                new("SpamClassification:Enabled", "true"),
                new("SpamClassification:UseScanner", "true"),
                new("SpamClassification:Scanner:Host", "spamd.example.test"),
            ],
            ["every capability at once"] =
            [
                new("McpEndpoint:Enabled", "true"),
                new("McpEndpoint:Authentication:0:ApiKey:Name", "workstation"),
                new("McpEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key"),
                new("AdminEndpoint:Enabled", "true"),
                new("AdminEndpoint:Port", "8082"),
                new("AdminEndpoint:Authentication:0:ApiKey:Name", "operator"),
                new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key-either"),
                new("MailSynchronization:Enabled", "true"),
                new("MailSynchronization:Accounts:0:AccountId", "personal"),
                new("MailSynchronization:Accounts:0:DisplayName", "Personal"),
                new("MailSynchronization:Accounts:0:Host", "imap.example.test"),
                new("MailSynchronization:Accounts:0:UserName", "someone@example.test"),
                new("MailSynchronization:Accounts:0:Secrets:Password:Name", "mailbox-password"),
                new("MailSynchronization:Accounts:0:Secrets:Password:SecretReference", "plaintext:not-a-real-password"),
                .. EmbeddingChain,
                .. ChatEndpoint,
                new("Chat:RelevanceFilter:Enabled", "true"),
                new("SensitiveContent:Secrets:Enabled", "true"),
                new("SensitiveContent:Pii:Enabled", "true"),
                new("SensitiveContent:PersonalDataAnalyzer:Endpoint", "http://analyzer.example.test:5001"),
                new("SpamClassification:Enabled", "true"),
                new("SpamClassification:UseScanner", "true"),
                new("SpamClassification:Scanner:Host", "spamd.example.test"),
            ],
        };

    /// <summary>Names the shapes, which is what xUnit renders when one of them fails.</summary>
    public static TheoryData<string> DeploymentShapes => [.. Shapes.Keys];

    /// <summary>
    /// The assertion this class exists for. Every service the composition registered is resolved, in a scope, out of a
    /// provider built with both validations on — so a dependency nobody registered, a scoped service a singleton
    /// captured, and a factory that cannot build what it promised are all reported here rather than at run time.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeploymentShapes))]
    public async Task Compose_ResolvesEveryServiceItRegistered(string shape)
    {
        // Arrange
        var services = ComposeServices(shape);

        // Act
        // Released asynchronously, because the graph holds connection pools that implement only IAsyncDisposable and
        // the container refuses to release one synchronously.
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        await using var scope = provider.CreateAsyncScope();

        // The phase the host runs before it starts anything, and the one the graph is written against: the composed
        // connection string, the proven secret references, and the published settings snapshots all arrive here, and a
        // service resolved ahead of it is told so rather than handed a half-composed dependency. Only configuration
        // work happens in it — a service reaching a database or a network in this phase would break this test loudly,
        // which is the right way for that to be noticed.
        foreach (var starting in scope.ServiceProvider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await starting.StartingAsync(TestContext.Current.CancellationToken);
        }

        string[] unbuildable =
        [
            .. services
                .Where(IsWorthResolving)
                .Where(static descriptor => !descriptor.ServiceType.IsGenericTypeDefinition)

                // One resolution per registration group rather than per descriptor, because resolving a service type
                // builds every implementation registered against it: a shape declaring a dozen hosted services would
                // otherwise build all of them a dozen times, and one broken constructor among them would be reported
                // once per sibling instead of once.
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ServiceKey))
                .Select(descriptor => ReportResolving(scope.ServiceProvider, descriptor))
                .OfType<string>(),

            // Health checks are the one thing the composition registers that the service collection does not carry.
            // Both overloads used here append a HealthCheckRegistration to an options object instead of adding a
            // descriptor, so the check is built by HealthCheckService rather than by the container: neither
            // ValidateOnBuild nor the pass above ever runs its factory, and a port it resolves that nobody registered
            // would first be missed by a probe answering in production.
            .. scope.ServiceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations
                .Select(registration => ReportBuilding(scope.ServiceProvider, registration))
                .OfType<string>(),
        ];

        // Assert
        // Reported together rather than through Assert.Empty, which abbreviates a collection: one missing registration
        // usually leaves several services unbuildable, and the reader needs the one that is actually absent.
        if (unbuildable.Length > 0)
        {
            Assert.Fail(
                $"The '{shape}' deployment registered services it cannot build:{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", unbuildable));
        }
    }

    /// <summary>
    /// What the pipeline is told is what the operator configured. Resolving the graph proves the registrations exist;
    /// this proves the decisions taken beside them are the ones handed on, which is the other half of what composition
    /// produces and the half a resolution cannot observe.
    /// </summary>
    [Fact]
    public void Compose_ReportsTheSurfacesTheDeploymentServes()
    {
        // Arrange
        var builder = ConfiguredBuilder("mcp and admin served");

        // Act
        var surfaces = HostComposition.Compose(builder);

        // Assert
        Assert.True(surfaces.Mcp.Enabled);
        Assert.True(surfaces.Admin.Enabled);
        Assert.True(surfaces.Health.Enabled);
        Assert.True(surfaces.IsRateLimited);
        Assert.Equal(3, surfaces.Listeners.Listeners.Count);
    }

    /// <summary>
    /// A process serving none of its surfaces would let Kestrel bind an address no section describes, so composition
    /// refuses it. Asserted here because the refusal happens while the services are being registered, which is the one
    /// place no options validator can reach.
    /// </summary>
    [Fact]
    public void Compose_WithNoSurfaceServed_RefusesTheDeployment()
    {
        // Arrange
        var builder = ConfiguredBuilder("probes only", [new("HealthEndpoints:Enabled", "false")]);

        // Act
        var refusal = Record.Exception(() => HostComposition.Compose(builder));

        // Assert
        Assert.NotNull(refusal);
        Assert.Contains("No network surface is enabled", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The analyzer is a sidecar with a lifetime of its own, so whether it answers is a readiness question rather than a
    /// startup one. Composition is where that is decided, and the only place both halves of the decision are observable
    /// together: that the startup probe turns healthy without it, and that the readiness probe asks about it.
    /// </summary>
    [Fact]
    public async Task Compose_WithThePersonalDataScannerOn_AsksTheAnalyzerOnReadinessRatherThanAtStartup()
    {
        // Arrange
        await using var provider = ComposeServices("personal-data scanning").BuildServiceProvider();

        var startupGates = provider.GetRequiredService<HostStartupGates>();

        // Act
        startupGates.MarkCompleted(HostStartupGate.SecretConfiguration);
        startupGates.MarkCompleted(HostStartupGate.DatabaseSchema);

        // Assert
        Assert.True(startupGates.Completed);

        var analyzer = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            registration => registration.Name == PersonalDataAnalyzerHealthCheck.Name);

        Assert.True(HealthProbe.Readiness.Selects(analyzer));
        Assert.False(HealthProbe.Startup.Selects(analyzer));
        Assert.False(HealthProbe.Liveness.Selects(analyzer));
    }

    /// <summary>An opt-in nobody took reaches no analyzer, so it must not report unready for one it never deployed.</summary>
    [Fact]
    public async Task Compose_WithThePersonalDataScannerOff_ReportsNothingAboutAnAnalyzer()
    {
        // Arrange
        await using var provider = ComposeServices("probes only").BuildServiceProvider();

        // Act
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // Assert
        Assert.DoesNotContain(
            registrations,
            registration => registration.Name == PersonalDataAnalyzerHealthCheck.Name);
    }

    /// <summary>Composes one shape and hands back what it registered.</summary>
    private static IServiceCollection ComposeServices(string shape)
    {
        var builder = ConfiguredBuilder(shape);

        HostComposition.Compose(builder);

        return builder.Services;
    }

    /// <summary>Builds the application builder the composition runs against, carrying one shape and nothing else.</summary>
    /// <remarks>
    /// The configuration sources the framework supplies are cleared rather than layered under, because they are this
    /// machine's: the host's own <c>appsettings.json</c> travels into the test output through the project reference, and
    /// an environment variable set for a developer's run would otherwise decide what a shape composes. What each shape
    /// states is then the whole of what the composition reads.
    /// </remarks>
    private static WebApplicationBuilder ConfiguredBuilder(
        string shape,
        IReadOnlyList<KeyValuePair<string, string?>>? beyondTheShape = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection([.. Database, .. Shapes[shape], .. beyondTheShape ?? []]);

        // The one service this test decides for itself, and it is the framework's rather than MailFathom's: data
        // protection is registered by the web host, nothing in this repository configures it, and resolving the hosted
        // service it registers makes its key manager settle on a key repository. Left alone, that is a directory under
        // the profile of whoever ran the suite — a unit test may not write to the file system, and creating one there
        // would also be the wrong answer on a build agent. What the composition registers is unchanged by this.
        builder.Services.Configure<KeyManagementOptions>(
            keyManagement => keyManagement.XmlRepository = new KeysHeldInMemory());

        return builder;
    }

    /// <summary>Resolves one registration, reporting what it could not build rather than ending the pass at the first one.</summary>
    /// <returns>The failure, or <see langword="null" /> where every implementation of the service resolved.</returns>
    /// <remarks>
    /// Every implementation is resolved rather than the last one registered, because several registrations of one
    /// service type is how the handlers, the hosted services, and the option validators are declared, and asking for
    /// one of them would leave the rest unbuilt.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A registration that cannot be built is what this pass reports, and a factory throws whatever it throws — narrowing the catch would end the pass on the first failure and hide every registration behind it, which is the opposite of what a report of a broken graph is for.")]
    private static string? ReportResolving(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        try
        {
            var resolved = descriptor.IsKeyedService
                ? provider.GetKeyedServices(descriptor.ServiceType, descriptor.ServiceKey)
                : provider.GetServices(descriptor.ServiceType);

            // Materialized rather than left as a query, because the resolution is what this asks for and a deferred
            // sequence would report every registration as sound without building one of them.
            _ = resolved.ToArray();

            return null;
        }
        catch (Exception failure)
        {
            return $"{descriptor.ServiceType.FullName} ({descriptor.Lifetime}): {failure.Message}";
        }
    }

    /// <summary>Builds one health check, reporting what its factory could not resolve.</summary>
    /// <returns>The failure, or <see langword="null" /> where the check was built.</returns>
    /// <remarks>Named by the check rather than by a service type, because that is the only identity a registration carries and the one the probe would report it under.</remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A check that cannot be built is what this pass reports, and a factory throws whatever it throws — the reasoning is the one ReportResolving carries.")]
    private static string? ReportBuilding(IServiceProvider provider, HealthCheckRegistration registration)
    {
        try
        {
            _ = registration.Factory(provider);

            return null;
        }
        catch (Exception failure)
        {
            return $"health check '{registration.Name}': {failure.Message}";
        }
    }

    /// <summary>Whether a registration is one this repository owns and therefore one this pass has to be able to build.</summary>
    /// <remarks>
    /// Hosted services are named explicitly because their service type belongs to the framework and most of them are
    /// registered through a factory, so nothing about the descriptor says whose they are — and they are the
    /// registrations whose failure costs the most, being what a running deployment consists of.
    /// </remarks>
    private static bool IsWorthResolving(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService)
        || IsOwned(descriptor.ServiceType)
        || (descriptor.ImplementationType is { } implementation && IsOwned(implementation));

    /// <summary>Whether a type is one of this repository's, including one named only as a generic argument.</summary>
    private static bool IsOwned(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("MailFathom.", StringComparison.Ordinal) is true
        || (type.IsGenericType && type.GetGenericArguments().Any(IsOwned));

    /// <summary>Where the framework's data protection keys go while a composition is being asserted.</summary>
    /// <remarks>Nothing here protects anything, so the repository is never read from and never written to; it exists so the key manager settles on something that is not a directory in somebody's home.</remarks>
    private sealed class KeysHeldInMemory : IXmlRepository
    {
        private readonly List<XElement> elements = [];

        /// <inheritdoc />
        public IReadOnlyCollection<XElement> GetAllElements() => this.elements.AsReadOnly();

        /// <inheritdoc />
        public void StoreElement(XElement element, string friendlyName) => this.elements.Add(element);
    }
}
