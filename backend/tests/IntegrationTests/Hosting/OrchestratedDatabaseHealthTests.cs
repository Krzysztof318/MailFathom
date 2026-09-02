// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves the database health check the enrichment layers on reports the orchestrated database healthy, under the tag that reaches the readiness probe.</summary>
/// <remarks>
/// <para>
/// The registration is deliberately the enrichment half of the Aspire PostgreSQL EF Core integration rather than its
/// context-building half, because MailFathom composes its own connection string asynchronously during startup and supplies
/// the password per physical connection. Whether that arrangement still yields a working health check is a claim about
/// two libraries meeting a real server: a unit test can assert the settings the callback sets and nothing beyond them.
/// </para>
/// <para>
/// The tag is asserted here for the same reason. The enrichment registers the check under a name it chooses, and the
/// readiness probe finds it by the tag MailFathom adds to that registration; a rename upstream would silently leave
/// readiness answering without consulting the database, which is exactly the failure a composed run catches and a
/// substitute cannot.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDatabaseHealthTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The tag the readiness probe selects its checks by, written out rather than referenced.</summary>
    /// <remarks>It is a published identity: a deployment reads it in the host's own composition and an operator never types it, but renaming it changes which checks a probe consults. Spelling it here is what makes this test fail on that rename instead of following it.</remarks>
    private const string ReadinessProbeTag = "ready";

    [Fact]
    public async Task CheckHealthAsync_ForTheEnrichedContext_ReportsTheOrchestratedDatabaseHealthy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var builder = new HostApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);
        builder.AddDatabaseHealthAndTelemetry(
            TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds),
            probeTags: [ReadinessProbeTag]);

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        // Act
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(
            registration => registration.Tags.Contains(ReadinessProbeTag),
            cancellationToken);

        await host.StopAsync(cancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, report.Status);

        // A healthy report over no checks at all would say nothing about the database, and selecting by tag is exactly
        // how the readiness probe would end up over none, so the entry itself is asserted.
        Assert.NotEmpty(report.Entries);
        Assert.All(report.Entries, entry => Assert.Equal(HealthStatus.Healthy, entry.Value.Status));
    }
}
