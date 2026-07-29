// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MailMcp.IntegrationTests;

/// <summary>Proves the suite reaches a real, migrated PostgreSQL database through the production registration path.</summary>
/// <remarks>
/// This is the harness test rather than a schema assertion: it fails when the orchestration does not start, when the
/// migration resource does not apply the baseline, or when the infrastructure registration cannot open a connection
/// against the database the orchestration issued. The schema, constraint, index, and query-plan verification that
/// specification 20 lists is written against this same fixture and is tracked separately.
/// </remarks>
public sealed class OrchestratedDatabaseSchemaTests(MailMcpOrchestrationFixture orchestration)
{
    [Fact]
    public async Task ReadPendingMigrationIdentifiersAsync_AgainstTheOrchestratedDatabase_ReportsNoPendingMigration()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // A real host rather than a bare service provider, because infrastructure composes the connection string during
        // hosted-service startup so that resolving a secret reference stays asynchronous. The connection string arrives
        // as ordinary configuration here: the orchestration issues it directly, so no secret block is involved.
        var builder = new HostApplicationBuilder();
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default);

        using var host = builder.Build();
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
}
