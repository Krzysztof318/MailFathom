// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
