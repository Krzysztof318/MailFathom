// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.UnitTests;

/// <summary>Composes the real client graph and resolves it, without a window to launch it in.</summary>
/// <remarks>
/// <para>
/// A container resolves a factory-registered service the first time something asks for it, so a dependency nobody
/// registered is not a failure the composition reports: it is an exception out of whichever screen asked first, after
/// the application has already drawn itself. <c>ValidateOnBuild</c> answers half of that and no more — it inspects the
/// constructors the container can see — so every registration this application owns is resolved here rather than
/// validated, and every model a screen is drawn from is constructed beside them, because navigation builds a model
/// from the same provider and a model is the one consumer of most of what is registered.
/// </para>
/// <para>
/// What the tests supply around the composition is what the host builder supplies in a running head: the string table,
/// the navigator, the theme service, and the localization service all come from the <c>Use*</c> calls in
/// <see cref="App.OnLaunched" /> rather than from <see cref="ClientComposition" />, and they are stood in for here for
/// the same reason a unit host stands in for a window. The three readers of the platform's own settings store are
/// stood in for as well, because <c>ApplicationData.Current</c> belongs to a running application — which of them the
/// composition registered is asserted on its own instead. Everything else is the application's own.
/// </para>
/// <para>
/// Nothing here reaches a deployment. The graph is built from the service collection directly, so no transport is
/// created until a test asks for one and no request is ever made: a transport is constructed from an address without
/// dialling it.
/// </para>
/// </remarks>
public sealed class ClientCompositionTests
{
    /// <summary>Where the configuration the application ships is read from beside the test assembly.</summary>
    private static readonly string ShippedConfiguration =
        Path.Combine(AppContext.BaseDirectory, "Configuration", "appsettings.json");

    /// <summary>What an installation states, in the shape the application reads it in.</summary>
    private static readonly KeyValuePair<string, string?>[] StatedDeployment =
    [
        new($"{DeploymentSettings.SectionName}:{nameof(DeploymentSettings.Address)}", "https://mail.example/"),
    ];

    /// <summary>
    /// What every space shares for the length of a run, and would silently stop sharing if one of these were ever
    /// registered per model.
    /// </summary>
    /// <remarks>
    /// Stated as a list rather than asserted by resolving twice, because the defect this guards against is a lifetime
    /// written wrongly rather than a container behaving unexpectedly: a transient registration resolves perfectly and
    /// hands the next screen a tree that has forgotten where somebody was.
    /// </remarks>
    private static readonly Type[] SharedForTheRun =
    [
        typeof(IWorkspace),
        typeof(IMailboxTreeMemory),
        typeof(IMailboxTree),
        typeof(IMessageListMemory),
        typeof(IMessageList),
        typeof(IMailThread),
        typeof(IMailSearch),
        typeof(IClientSession),
        typeof(DeploymentChoice),
        typeof(DeploymentAddress),
        typeof(SignedInOwner),
        typeof(IOwnerCredentialStore),
        typeof(OwnerSignIn),
    ];

    /// <summary>The assertion this class exists for.</summary>
    [Fact]
    public void Compose_ResolvesEveryServiceItRegistered()
    {
        // Arrange
        var services = ComposeServices();

        // Act
        var unbuildable = ReportEveryRegistrationItCannotBuild(services);

        // Assert
        // Reported together rather than through Assert.Empty, which abbreviates a collection: one missing registration
        // usually leaves several services unbuildable, and the reader needs the one that is actually absent.
        if (unbuildable.Length > 0)
        {
            Assert.Fail(
                $"The client registered services it cannot build:{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", unbuildable));
        }
    }

    /// <summary>
    /// The same pass over the configuration every head actually reads, rather than over keys a test invented.
    /// </summary>
    /// <remarks>
    /// The application's own <c>appsettings.json</c> travels into the bundle and is the one source a browser head can
    /// read, so a value edited out of it composes a head that fails while starting — which no other test here would
    /// notice, each of them stating its own configuration. The file is read from beside the test assembly rather than
    /// embedded, for the reason every other file this suite reads is: one file rather than two that have to agree.
    /// </remarks>
    [Fact]
    public void Compose_WithTheConfigurationTheApplicationShips_ResolvesEveryServiceItRegistered()
    {
        // Arrange
        var services = ComposeServices(
            new ConfigurationBuilder().AddJsonFile(ShippedConfiguration).Build());

        // Act
        var unbuildable = ReportEveryRegistrationItCannotBuild(services);

        // Assert
        if (unbuildable.Length > 0)
        {
            Assert.Fail(
                $"The configuration the application ships composes services it cannot build:{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", unbuildable));
        }
    }

    /// <summary>
    /// The other half of what the composition is for: every screen is drawn from a model, navigation builds each one
    /// from this provider, and a model asking for something nobody registered fails when its screen is opened rather
    /// than when the application starts.
    /// </summary>
    /// <remarks>
    /// The models are discovered rather than listed, so a screen added without its dependencies registered fails here
    /// instead of on the first navigation to it. A model is a record — MVUX requires one — and the bindable proxy the
    /// generator emits beside it is not, which is what separates the two without naming either.
    /// </remarks>
    [Fact]
    public void Compose_ConstructsEveryModelAScreenIsDrawnFrom()
    {
        // Arrange
        var services = ComposeServices();

        using var provider = BuiltProvider(services);

        var models = PresentationModels();

        // Act
        string[] unbuildable =
        [
            .. models
                .Select(model => ReportConstructing(provider, model))
                .OfType<string>(),
        ];

        // Assert
        // The count is asserted as well as the failures, because a discovery that found nothing would otherwise report
        // a graph in which every screen composes as one in which none was looked at.
        Assert.NotEmpty(models);

        if (unbuildable.Length > 0)
        {
            Assert.Fail(
                $"The client cannot build every model a screen is drawn from:{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", unbuildable));
        }
    }

    /// <summary>What the run shares is registered once and shared, which no resolution of a working graph would notice.</summary>
    [Fact]
    public void Compose_RegistersWhatEverySpaceSharesOncePerRun()
    {
        // Arrange
        var services = ComposeServices();

        // Act
        var registered = SharedForTheRun.ToDictionary(
            static shared => shared,
            shared => services.Where(descriptor => descriptor.ServiceType == shared).ToArray());

        // Assert
        Assert.All(
            registered,
            shared =>
            {
                var descriptor = Assert.Single(shared.Value);

                Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            });
    }

    /// <summary>
    /// What outlives a run is read from the platform's own settings store, which is the one part of the graph the
    /// tests stand in for.
    /// </summary>
    /// <remarks>
    /// Asserted from the registrations rather than by resolving them, because resolving is what a unit host cannot do
    /// here: each of the three reaches <c>ApplicationData.Current</c>, which belongs to a running application. So this
    /// is the reading that says the composition still names them — without it, the stand-ins the other tests put in
    /// their place would hide a head composed with something else entirely.
    /// </remarks>
    [Fact]
    public void Compose_ReadsWhatOutlivesARunFromThePlatformsOwnSettingsStore()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        ClientComposition.Compose(
            services,
            StatedConfiguration(StatedDeployment),
            new StubDeploymentAddressSource(null),
            UnkeptOwnerCredentialStore.Instance);

        // Assert
        Assert.Equal(
            typeof(LocalSettingsMailboxTreeMemory),
            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMailboxTreeMemory))
                .ImplementationType);

        Assert.Equal(
            typeof(LocalSettingsMessageListMemory),
            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMessageListMemory))
                .ImplementationType);

        Assert.Equal(
            typeof(LocalSettingsDeploymentChoiceStore),
            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IDeploymentChoiceStore))
                .ImplementationType);
    }

    /// <summary>What the installation stated reaches the type that holds it, by name rather than through a binder.</summary>
    [Fact]
    public void Compose_ReadsTheStatedDeploymentByName()
    {
        // Arrange
        var services = ComposeServices();

        // Act
        using var provider = BuiltProvider(services);

        var settings = provider.GetRequiredService<DeploymentSettings>();

        // Assert
        Assert.Equal("https://mail.example/", settings.Address);
    }

    /// <summary>
    /// A section stating no address composes, because a head nobody has pointed anywhere asks on its first screen
    /// rather than failing to start.
    /// </summary>
    [Fact]
    public void Compose_WithNoAddressStated_ComposesAHeadThatAsksForOne()
    {
        // Arrange
        var services = ComposeServices(StatedConfiguration([]));

        // Act
        using var provider = BuiltProvider(services);

        // Assert
        Assert.Empty(provider.GetRequiredService<DeploymentSettings>().Address);
        Assert.Null(provider.GetRequiredService<DeploymentAddress>().Current);
    }

    /// <summary>
    /// The three transports are the three the client is entitled to, each configured on the terms its own callers are
    /// held to.
    /// </summary>
    /// <remarks>
    /// Asserted from the transports rather than from the registrations, because what a caller gets is an
    /// <see cref="HttpClient" /> the factory built: the address the deployment's transport carries is read as it is
    /// created, and the two aimed at machines nobody has vouched for carry none at all.
    /// </remarks>
    [Fact]
    public async Task Compose_ConfiguresOneTransportPerKindOfMachineItReaches()
    {
        // Arrange
        var services = ComposeServices();

        await using var provider = BuiltProvider(services);

        await provider.GetRequiredService<DeploymentAddress>()
            .PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        var transports = provider.GetRequiredService<IHttpClientFactory>();

        // Act
        using var deployment = transports.CreateClient(DeploymentHttpClients.Deployment);
        using var signIn = transports.CreateClient(DeploymentHttpClients.SignIn);
        using var probe = transports.CreateClient(DeploymentHttpClients.DeploymentProbe);

        // Assert
        Assert.Equal(new Uri("https://mail.example/"), deployment.BaseAddress);
        Assert.Null(signIn.BaseAddress);
        Assert.Null(probe.BaseAddress);

        Assert.All(
            new[] { deployment, signIn, probe },
            transport => Assert.Equal(DeploymentOptions.DefaultTimeout, transport.Timeout));

        Assert.Equal(DeploymentExchange.MaxMailBodyBytes, deployment.MaxResponseContentBufferSize);
        Assert.Equal(DeploymentExchange.MaxDocumentBytes, signIn.MaxResponseContentBufferSize);
        Assert.Equal(DeploymentExchange.MaxDocumentBytes, probe.MaxResponseContentBufferSize);
    }

    /// <summary>The head that keeps a credential keeps it where it said, which is what the desktop head hands in.</summary>
    [Fact]
    public void Compose_WithAHeadThatKeepsACredential_RegistersThatStore()
    {
        // Arrange
        var services = new ServiceCollection();

        var store = new StubOwnerCredentialStore();

        // Act
        ClientComposition.Compose(
            services,
            StatedConfiguration(StatedDeployment),
            new StubDeploymentAddressSource(null),
            store);

        // Assert
        var registered = Assert.Single(
            services,
            static descriptor => descriptor.ServiceType == typeof(IOwnerCredentialStore));

        Assert.Same(store, registered.ImplementationInstance);
    }

    /// <summary>
    /// A head that keeps none composes all the same, and says so rather than falling back to somewhere a password may
    /// not be written.
    /// </summary>
    /// <remarks>
    /// The browser head is the case: every store a browser offers is scoped to the page's origin rather than to a
    /// person, so it keeps nothing and the person signs in each time. What this asserts is that composing without a
    /// store leaves the client saying it will ask again — never that some other place quietly took the password.
    /// </remarks>
    [Fact]
    public void Compose_WithAHeadThatKeepsNoCredential_ComposesAHeadThatSaysItWillAskAgain()
    {
        // Arrange
        var services = ComposeServices();

        // Act
        using var provider = BuiltProvider(services);

        // Assert
        Assert.Equal(
            CredentialPersistence.NotOfferedOnThisHead,
            provider.GetRequiredService<IOwnerCredentialStore>().Persistence);

        Assert.Equal(
            CredentialPersistence.NotOfferedOnThisHead,
            provider.GetRequiredService<OwnerSignIn>().Persistence);
    }

    /// <summary>Composes the client with one configuration, and with the services a running head's builder contributes.</summary>
    private static ServiceCollection ComposeServices(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();

        ClientComposition.Compose(
            services,
            configuration ?? StatedConfiguration(StatedDeployment),
            new StubDeploymentAddressSource(null),
            UnkeptOwnerCredentialStore.Instance);

        // What the Use* calls in App.OnLaunched put in beside the composition. Every one of them is the framework's,
        // and a screen reaching one of them is reaching a running head rather than anything registered here.
        services.AddSingleton<IStringLocalizer>(new StubStringLocalizer(new Dictionary<string, string>()));
        services.AddSingleton<INavigator, StubNavigator>();
        services.AddSingleton<IThemeService, StubThemeService>();
        services.AddSingleton<ILocalizationService>(new StubLocalizationService("en", "en", "pl"));

        // The three readers of the platform's own settings store, which is the one part of this graph a unit host
        // genuinely has none of: ApplicationData.Current belongs to a running application, and each of these reaches
        // it as it is read. They are stood in for so the rest of the graph can be resolved around them, and which
        // implementation the composition registered is asserted separately — see
        // Compose_ReadsWhatOutlivesARunFromThePlatformsOwnSettingsStore.
        services.Replace(ServiceDescriptor.Singleton<IMailboxTreeMemory>(new StubMailboxTreeMemory()));
        services.Replace(ServiceDescriptor.Singleton<IMessageListMemory>(new StubMessageListMemory()));
        services.Replace(ServiceDescriptor.Singleton<IDeploymentChoiceStore>(new StubDeploymentChoiceStore()));

        return services;
    }

    /// <summary>Builds the configuration one shape states, and nothing this machine holds.</summary>
    private static IConfiguration StatedConfiguration(IReadOnlyList<KeyValuePair<string, string?>> stated) =>
        new ConfigurationBuilder().AddInMemoryCollection(stated).Build();

    /// <summary>Builds the graph with both validations on, so a captured scope and an unconstructable type are reported here.</summary>
    private static ServiceProvider BuiltProvider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    /// <summary>Resolves every registration this repository owns, reporting each one the graph could not build.</summary>
    private static string[] ReportEveryRegistrationItCannotBuild(IServiceCollection services)
    {
        using var provider = BuiltProvider(services);

        return
        [
            .. services
                .Where(static descriptor => IsOwned(descriptor.ServiceType)
                    || (descriptor.ImplementationType is { } implementation && IsOwned(implementation)))
                .Where(static descriptor => !descriptor.ServiceType.IsGenericTypeDefinition)

                // One resolution per service type rather than per descriptor, because resolving a service type builds
                // every implementation registered against it.
                .DistinctBy(static descriptor => descriptor.ServiceType)
                .Select(descriptor => ReportResolving(provider, descriptor.ServiceType))
                .OfType<string>(),
        ];
    }

    /// <summary>Every model a screen is drawn from, discovered rather than listed.</summary>
    private static Type[] PresentationModels() =>
        [.. typeof(App).Assembly
            .GetTypes()
            .Where(static candidate => candidate.Namespace?.StartsWith(
                "MailFathom.Client.Presentation",
                StringComparison.Ordinal) is true)
            .Where(static candidate => candidate.Name.EndsWith("Model", StringComparison.Ordinal))
            .Where(static candidate => candidate is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(IsRecord)];

    /// <summary>Whether a type is a record, which every MVUX model is and no generated proxy beside one is.</summary>
    private static bool IsRecord(Type candidate) =>
        candidate.GetMethod(
            "<Clone>$",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    /// <summary>Resolves one registration, reporting what it could not build rather than ending the pass at the first one.</summary>
    /// <returns>The failure, or <see langword="null" /> where every implementation of the service resolved.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A registration that cannot be built is what this pass reports, and a factory throws whatever it throws — narrowing the catch would end the pass on the first failure and hide every registration behind it, which is the opposite of what a report of a broken graph is for.")]
    private static string? ReportResolving(IServiceProvider provider, Type service)
    {
        try
        {
            // Materialized rather than left as a query, because the resolution is what this asks for and a deferred
            // sequence would report every registration as sound without building one of them.
            _ = provider.GetServices(service).ToArray();

            return null;
        }
        catch (Exception failure)
        {
            return $"{service.FullName}: {failure.Message}";
        }
    }

    /// <summary>Builds one model the way navigation builds it, reporting what it could not resolve.</summary>
    /// <returns>The failure, or <see langword="null" /> where the model was constructed.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A model that cannot be built is what this pass reports, and a constructor throws whatever it throws — the reasoning is the one ReportResolving carries.")]
    private static string? ReportConstructing(IServiceProvider provider, Type model)
    {
        try
        {
            _ = ActivatorUtilities.CreateInstance(provider, model);

            return null;
        }
        catch (Exception failure)
        {
            return $"{model.FullName}: {failure.Message}";
        }
    }

    /// <summary>Whether a type is one of this repository's, including one named only as a generic argument.</summary>
    private static bool IsOwned(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("MailFathom.", StringComparison.Ordinal) is true
        || (type.IsGenericType && type.GetGenericArguments().Any(IsOwned));
}
