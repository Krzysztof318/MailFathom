// Copyright © 2026 Krzysztof Kasprowicz

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using MailMcp.AppHost;
using Xunit;

namespace MailMcp.IntegrationTests;

/// <summary>The orchestrated PostgreSQL server and applied schema the whole suite runs against.</summary>
/// <remarks>
/// <para>
/// The suite starts the repository's own app model rather than a container topology of its own, so what it verifies is
/// the orchestration a developer runs and a deployment mirrors. Starting it costs an image pull, a server start, and a
/// migration run, so the application's lifetime is the assembly's: a test isolates itself through the data it writes,
/// never through a container of its own.
/// </para>
/// <para>
/// The MailMcp host resource is present in the app model but never started, because the suite verifies classes against
/// a real database rather than the composed host. Every test therefore owns the database exclusively; nothing
/// synchronizes mail underneath it.
/// </para>
/// </remarks>
public sealed class MailMcpOrchestrationFixture : IAsyncLifetime
{
    /// <summary>Bounds the whole start-up, which on a cold machine includes pulling the image and building the migration project.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? application;

    /// <summary>Gets or sets the connection string once the orchestration issued it.</summary>
    private string? IssuedDatabaseConnectionString { get; set; }

    /// <summary>Gets the connection string the orchestration issued for the migrated MailMcp database.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started yet.</exception>
    public string DatabaseConnectionString => this.IssuedDatabaseConnectionString
        ?? throw new InvalidOperationException(
            "The orchestrated database connection string is requested before the suite started the application.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        using var startupCancellation = new CancellationTokenSource(StartupTimeout);
        var cancellationToken = startupCancellation.Token;

        // The argument reaches the app host's own command-line configuration, which is what selects the ephemeral
        // container and volume names and leaves the host project unstarted.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            [$"{OrchestrationContract.IntegrationTestingConfigurationKey}=true"],
            cancellationToken);

        this.application = await builder.BuildAsync(cancellationToken);

        await this.application.StartAsync(cancellationToken);

        await this.application.ResourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.PostgresResourceName,
            cancellationToken);

        // The migration resource runs dotnet-ef once and finishes, so it reaches a terminal state rather than a healthy
        // one. Waiting for it here is what lets every test assume the baseline schema is already applied.
        await this.application.ResourceNotifications.WaitForResourceAsync(
            OrchestrationContract.MigrationsResourceName,
            KnownResourceStates.Finished,
            cancellationToken);

        this.IssuedDatabaseConnectionString = await this.application.GetConnectionStringAsync(
            OrchestrationContract.DatabaseResourceName,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The orchestration started without issuing a connection string for the MailMcp database.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.application is not null)
        {
            await this.application.DisposeAsync();
        }
    }
}
