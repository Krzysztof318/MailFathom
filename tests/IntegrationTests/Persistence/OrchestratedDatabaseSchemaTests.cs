// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Persistence;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Secrets;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the suite reaches a real, migrated PostgreSQL database, and reads the schema facts a build cannot infer.</summary>
/// <remarks>
/// The first test is the harness test rather than a schema assertion: it fails when the orchestration does not start,
/// when the migration resource does not apply the baseline, or when the infrastructure registration cannot open a
/// connection against the database the orchestration issued. The second is the other half of the inspector's contract,
/// and the half no unit test can reach at all: the text search configuration a lexical index was built with exists only
/// in PostgreSQL's own column catalogue.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDatabaseSchemaTests(MailFathomOrchestrationFixture orchestration)
{
    [Fact]
    public async Task ReadPendingMigrationIdentifiersAsync_AgainstTheOrchestratedDatabase_ReportsNoPendingMigration()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = ComposeHost(orchestration, PostgresTextSearchConfiguration.Default);
        await host.StartAsync(cancellationToken);

        // Act
        using var scope = host.Services.CreateScope();
        var pendingMigrations = await scope.ServiceProvider
            .GetRequiredService<IDatabaseSchemaInspector>()
            .ReadPendingMigrationIdentifiersAsync(cancellationToken);

        await host.StopAsync(cancellationToken);

        // Assert
        Assert.Empty(pendingMigrations);
    }

    /// <summary>Proves the inspector reports the configuration the applied schema holds, not the one this process wants.</summary>
    /// <remarks>
    /// The host is deliberately composed with a different configuration from the one the baseline migration applied. The
    /// reported name has to be the schema's, because that is the configuration the stored lexemes were built with and the
    /// whole point of the startup gate is to refuse a database whose index disagrees with this process. A reader that
    /// answered from the model would return <c>english</c> here and the mismatch would never be detected.
    /// </remarks>
    [Fact]
    public async Task ReadSearchVectorTextSearchConfigurationAsync_AgainstASchemaBuiltWithAnotherConfiguration_ReportsTheSchemasOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var composedConfiguration = PostgresTextSearchConfiguration.Create("english");
        using var host = ComposeHost(orchestration, composedConfiguration);
        await host.StartAsync(cancellationToken);

        // Act
        using var scope = host.Services.CreateScope();
        var appliedConfiguration = await scope.ServiceProvider
            .GetRequiredService<IDatabaseSchemaInspector>()
            .ReadSearchVectorTextSearchConfigurationAsync(cancellationToken);

        await host.StopAsync(cancellationToken);

        // Assert
        Assert.Equal(PostgresTextSearchConfiguration.Default.Value, appliedConfiguration);
        Assert.NotEqual(composedConfiguration.Value, appliedConfiguration);
    }

    /// <summary>Composes the production registrations against the orchestrated database.</summary>
    /// <remarks>
    /// A real host rather than a bare service provider, because infrastructure composes the connection string during
    /// hosted-service startup so that resolving a secret reference stays asynchronous. The connection string arrives as
    /// ordinary configuration here: the orchestration issues it directly, so no secret block is involved.
    /// </remarks>
    private static IHost ComposeHost(
        MailFathomOrchestrationFixture orchestration,
        PostgresTextSearchConfiguration textSearchConfiguration)
    {
        var builder = new HostApplicationBuilder();
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            textSearchConfiguration);

        return builder.Build();
    }
}
