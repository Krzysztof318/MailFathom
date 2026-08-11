// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>A numeric configuration value binds to an undefined member, which must fail rather than fall through to the strictest mode by accident.</summary>
    [Fact]
    public void AddSecretResolution_UndefinedInterpretation_FailsInsteadOfStartingInAModeNobodySelected()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddSecretResolution((SecretValueInterpretation)99));
    }

    [Theory]
    [InlineData(SecretValueInterpretation.ReferenceOnly)]
    [InlineData(SecretValueInterpretation.ReferenceOrInline)]
    [InlineData(SecretValueInterpretation.InlineOnly)]
    public void AddSecretResolution_DefinedInterpretation_RegistersTheDeploymentsMode(
        SecretValueInterpretation interpretation)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSecretResolution(interpretation);

        // Assert
        using var provider = services.BuildServiceProvider();
        Assert.Equal(interpretation, provider.GetRequiredService<SecretResolutionOptions>().Interpretation);
    }

    /// <summary>
    /// The container must be the only owner of the data source. One built inside the startup provider is invisible to
    /// the container, so a host that resolved no context would shut down leaving its connection pool open.
    /// </summary>
    [Fact]
    public async Task AddInfrastructure_AfterStartup_HandsTheContainerADataSourceItCreatedItself()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();
        var connectionStringProvider = provider.GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single();

        // Act
        await connectionStringProvider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<NpgsqlDataSource>());
        Assert.IsNotAssignableFrom<IDisposable>(connectionStringProvider);
        Assert.IsNotAssignableFrom<IAsyncDisposable>(connectionStringProvider);
    }

    /// <summary>
    /// One record per database round trip is most of what a deployment writes, so the executed-command event is
    /// emitted at <see cref="LogLevel.Debug" /> rather than at the <see cref="LogLevel.Information" /> EF Core
    /// defaults to. The distinction the assertion holds is against filtering the category out: the level is configured
    /// on the event, so lowering the category's minimum brings the records back, and every other event — a failed
    /// command above all — keeps the level EF Core chose for it.
    /// </summary>
    [Fact]
    public async Task AddInfrastructure_AfterStartup_LogsAnExecutedCommandAtDebugAndLeavesAFailedOneAtItsOwnLevel()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();
        var connectionStringProvider = provider.GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single();
        await connectionStringProvider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var coreOptions = provider.GetRequiredService<DbContextOptions<MailFathomDbContext>>()
            .FindExtension<CoreOptionsExtension>();

        // Assert
        Assert.NotNull(coreOptions);
        Assert.Equal(LogLevel.Debug, coreOptions.WarningsConfiguration.GetLevel(RelationalEventId.CommandExecuted));
        Assert.Null(coreOptions.WarningsConfiguration.GetLevel(RelationalEventId.CommandError));
    }

    [Fact]
    public async Task AddInfrastructure_DataSourceRequestedBeforeStartup_ThrowsInsteadOfUsingAnUncomposedConnectionString()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<NpgsqlDataSource>);
    }

    /// <summary>
    /// Serving lexical search alone is a supported state, so an instance that declared no embedding chain resolves no
    /// text embedding generator. A descriptor needing one would then be registered and unconstructable, which is a
    /// different thing from being absent: the container reports the first by throwing and the second by answering
    /// nothing, and only the second survives the build-time validation a Development run performs.
    /// </summary>
    [Fact]
    public async Task AddInfrastructure_WithoutAnEmbeddingChain_RegistersNeitherTheGenerationNorItsBackfill()
    {
        // Arrange
        await using var provider = BuildConfiguredProvider();

        // Act, Assert
        Assert.Null(provider.GetService<StoredEmailEmbeddingGenerator>());
        Assert.Null(provider.GetService<StoredEmailEmbeddingBackfill>());
    }

    /// <summary>
    /// The other side of that rule, and the one it is easy to get backwards. Semantic retrieval reads the same
    /// generator, but it asks for one through a factory instead of injecting it, so its descriptor builds without one —
    /// and it has to be registered whether or not a chain was declared, because a search is served by every deployment
    /// and <c>MailboxSearchReader</c> injects it. Moving it beside the units of work above would leave a lexical-only
    /// instance unable to resolve a search at all, which is the failure that rule exists to prevent rather than one it
    /// permits.
    /// </summary>
    [Fact]
    public void AddInfrastructure_WithoutAnEmbeddingChain_StillRegistersTheSearchThatFallsBackToLexical()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(
            _ => new PostgresConnectionSettings("Host=localhost;Database=mailfathom", null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(SemanticEmailSearch)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MailboxSearchReader)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    /// <summary>
    /// The pass the schedule paces is one thing the process does, so a scoped registration would let an activation
    /// release a waiter that does not exist and would report a due instant belonging to whichever request created the
    /// scope last. It is registered whether or not a chain was declared, because the status surface reads it on every
    /// deployment and an act that never happens simply never brings anything forward.
    /// </summary>
    [Fact]
    public void AddInfrastructure_OnAnyDeployment_RegistersTheBackfillScheduleAsOneInstancePerProcess()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(
            _ => new PostgresConnectionSettings("Host=localhost;Database=mailfathom", null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(EmbeddingBackfillSchedule)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    /// <summary>
    /// The retrieval an answering run reaches mail through is a reading of the search above, so it is registered for
    /// every deployment rather than only where a chat endpoint was declared: an instance that answers no questions
    /// resolves it and never calls it, while one that does would otherwise fail to compose its agent.
    /// </summary>
    [Fact]
    public void AddInfrastructure_WithoutAChatEndpoint_StillRegistersTheRetrievalAnAnsweringRunUses()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(
            _ => new PostgresConnectionSettings("Host=localhost;Database=mailfathom", null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IEmailKnowledgeSearch)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        // As itself as well, which is what a deployment that judges its candidates with the model wraps rather than
        // rebuilds.
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MailboxKnowledgeSearch)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(EmailKnowledgeBounds)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    /// <summary>
    /// The tool surface asks whether this deployment answers questions, so the capability that decides it and the use
    /// case behind it are registered whether or not a chat endpoint was declared. Requiring the answering agent here
    /// would make the deployment that has to report "no questions" the one deployment unable to report anything.
    /// </summary>
    [Fact]
    public void AddInfrastructure_WithoutAChatEndpoint_StillRegistersWhatReportsThatItAnswersNoQuestions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(
            _ => new PostgresConnectionSettings("Host=localhost;Database=mailfathom", null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MailAnsweringCapability)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MailboxQuestionReader)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MailAnswerBounds)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        // The answering agent belongs to the AI boundary and arrives only where a chat endpoint was declared, which is
        // what the capability above resolves optionally rather than requires.
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IMailQuestionAnswerer));
    }

    /// <summary>
    /// The other half of the same decision: an instance that declared a chain registers both units of work. Asserted
    /// against the descriptors rather than by resolving them, because constructing either reaches the stores they
    /// write through and those open a database this suite has none of.
    /// </summary>
    [Fact]
    public void AddEmailEmbeddingGeneration_OnAServiceCollection_RegistersTheGenerationAndTheBackfillItDrives()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEmailEmbeddingGeneration();

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(StoredEmailEmbeddingGenerator)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(StoredEmailEmbeddingBackfill)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddEmailEmbeddingGeneration_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddEmailEmbeddingGeneration(null!));
    }

    /// <summary>An opt-in nobody took must cost nothing, so the detector exists only where the switch put it.</summary>
    [Fact]
    public void AddSecretContentScanning_NotCalled_LeavesNoDetectorAndNoDeclarationBehind()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.Empty(provider.GetServices<ISensitiveContentCatalog>());
        Assert.Empty(provider.GetServices<ISensitiveContentScanner>());
    }

    /// <summary>Registering one without the other would turn the refusal a switch with nothing behind it earns into a scanner that finds nothing.</summary>
    [Fact]
    public void AddSecretContentScanning_Called_RegistersTheDetectorAndWhatItDeclares()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    [SensitiveContentCategory.Create("ProviderToken")],
                    []),
            ]));

        // Act
        services.AddSecretContentScanning();

        // Assert
        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            SensitiveContentScannerKind.Secrets,
            Assert.Single(provider.GetServices<ISensitiveContentCatalog>()).Scanner);
        Assert.Equal(
            SensitiveContentScannerKind.Secrets,
            Assert.Single(provider.GetServices<ISensitiveContentScanner>()).Scanner);
    }

    [Fact]
    public void AddSecretContentScanning_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddSecretContentScanning(null!));
    }

    private static ServiceProvider BuildConfiguredProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        services.AddInfrastructure(_ => new PostgresConnectionSettings(
            "Host=localhost;Database=mailfathom;Username=mailfathom",
            ConnectionStringSecret: null,
            Password: null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        return services.BuildServiceProvider();
    }
}
