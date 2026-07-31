// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using MailMcp.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MailMcp.IntegrationTests.Hosting;

/// <summary>Proves the database health check the enrichment layers on reports the orchestrated database healthy.</summary>
/// <remarks>
/// The registration is deliberately the enrichment half of the Aspire PostgreSQL EF Core integration rather than its
/// context-building half, because MailMcp composes its own connection string asynchronously during startup and supplies
/// the password per physical connection. Whether that arrangement still yields a working health check is a claim about
/// two libraries meeting a real server: a unit test can assert the settings the callback sets and nothing beyond them.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDatabaseHealthTests(MailMcpOrchestrationFixture orchestration)
{
    [Fact]
    public async Task CheckHealthAsync_ForTheEnrichedContext_ReportsTheOrchestratedDatabaseHealthy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var builder = new HostApplicationBuilder();
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default);
        builder.AddDatabaseHealthAndTelemetry(
            TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds));

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        // Act
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(cancellationToken);

        await host.StopAsync(cancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, report.Status);

        // A healthy report over no checks at all would say nothing about the database, so the entry itself is asserted.
        Assert.NotEmpty(report.Entries);
        Assert.All(report.Entries, entry => Assert.Equal(HealthStatus.Healthy, entry.Value.Status));
    }
}
