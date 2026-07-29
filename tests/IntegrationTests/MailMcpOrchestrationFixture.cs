// Copyright © 2026 Krzysztof Kasprowicz

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using MailMcp.AppHost;
using Xunit;

namespace MailMcp.IntegrationTests;

/// <summary>The orchestrated PostgreSQL server, applied schema, and mail server the whole suite runs against.</summary>
/// <remarks>
/// <para>
/// The suite starts the repository's own app model rather than a container topology of its own, so what it verifies is
/// the orchestration a developer runs and a deployment mirrors. Starting it costs two image pulls, a server start, and
/// a migration run, so the application's lifetime is the assembly's: a test isolates itself through the data it writes,
/// never through a container of its own.
/// </para>
/// <para>
/// The MailMcp host resource is present in the app model but never started, because the suite verifies classes against
/// real infrastructure rather than the composed host. Every test therefore owns the database and the mailbox
/// exclusively; nothing synchronizes mail underneath it.
/// </para>
/// </remarks>
public sealed class MailMcpOrchestrationFixture : IAsyncLifetime
{
    /// <summary>Bounds the whole start-up, which on a cold machine includes pulling the images and building the migration project.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? application;

    /// <summary>Gets or sets the connection string once the orchestration issued it.</summary>
    private string? IssuedDatabaseConnectionString { get; set; }

    /// <summary>Gets or sets the mail server endpoints once the orchestration published them.</summary>
    private OrchestratedMailServerEndpoints? PublishedMailServerEndpoints { get; set; }

    /// <summary>Gets the connection string the orchestration issued for the migrated MailMcp database.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started yet.</exception>
    public string DatabaseConnectionString => this.IssuedDatabaseConnectionString
        ?? throw new InvalidOperationException(
            "The orchestrated database connection string is requested before the suite started the application.");

    /// <summary>Gets the host and ports the orchestrated mail server accepts IMAP and SMTP on.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started yet.</exception>
    public OrchestratedMailServerEndpoints MailServer => this.PublishedMailServerEndpoints
        ?? throw new InvalidOperationException(
            "The orchestrated mail server endpoints are requested before the suite started the application.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // Linked rather than a bare timeout, so a run cancelled during the image pull, the server start, or the
        // migration stops there instead of holding its container until the ten minutes elapse. The two reasons stay
        // distinguishable: the run's own token reports cancellation, and only this source reports a timeout.
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        startupCancellation.CancelAfter(StartupTimeout);

        var cancellationToken = startupCancellation.Token;

        // The one argument that selects the ephemeral container and volume names and leaves the host project unstarted.
        // Deliberately an argument: the app model refuses to read it from ambient configuration.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            [OrchestrationContract.IntegrationTestingArgument],
            cancellationToken);

        this.application = await builder.BuildAsync(cancellationToken);

        await this.application.StartAsync(cancellationToken);

        await this.application.ResourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.PostgresResourceName,
            cancellationToken);

        // Healthy rather than running, because the mail server's readiness endpoint is what states that its IMAP and
        // SMTP listeners are accepting. A test that seeded mail against a merely started container would race the
        // listener and fail as a connection refusal that says nothing about the behavior under test.
        await this.application.ResourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.MailServerResourceName,
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

        // Read once the resource is healthy, because the host port is allocated when the container starts rather than
        // when the app model describes it.
        this.PublishedMailServerEndpoints = new OrchestratedMailServerEndpoints(
            this.application.GetEndpoint(
                OrchestrationContract.MailServerResourceName,
                OrchestrationContract.MailServerImapEndpointName),
            this.application.GetEndpoint(
                OrchestrationContract.MailServerResourceName,
                OrchestrationContract.MailServerSmtpEndpointName));
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
