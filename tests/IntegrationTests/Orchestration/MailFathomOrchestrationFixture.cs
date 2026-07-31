// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using MailFathom.AppHost;
using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The orchestrated PostgreSQL server, applied schema, and mail server the whole suite runs against.</summary>
/// <remarks>
/// <para>
/// The suite starts the repository's own app model rather than a container topology of its own, so what it verifies is
/// the orchestration a developer runs and a deployment mirrors. Starting it costs two image pulls, a server start, and
/// a migration run, so the application's lifetime is the assembly's: a test isolates itself through the data it writes,
/// never through a container of its own.
/// </para>
/// <para>
/// The MailFathom host resource is present in the app model and does not start with it. Most of the suite verifies classes
/// against real infrastructure rather than the composed host, and every one of those tests owns the database and the
/// mailbox exclusively; a second MailFathom reconciling folders underneath them would make its synchronization part of
/// their environment. <see cref="StartMailFathomHostAsync" /> starts it on request, and the one collection that calls it
/// is ordered after every other, so nothing is asserting on that infrastructure by the time the host touches it.
/// </para>
/// </remarks>
public sealed class MailFathomOrchestrationFixture : IAsyncLifetime
{
    /// <summary>Bounds the whole start-up, which on a cold machine includes pulling the images and building the migration project.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Bounds the composed host's own start, which builds and runs a project against an already-migrated database.</summary>
    private static readonly TimeSpan HostStartupTimeout = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim hostStartGate = new(1, 1);

    private DistributedApplication? application;

    private Uri? startedHostBaseAddress;

    /// <summary>Gets or sets the connection string once the orchestration issued it.</summary>
    private string? IssuedDatabaseConnectionString { get; set; }

    /// <summary>Gets or sets the mail server endpoints once the orchestration published them.</summary>
    private OrchestratedMailServerEndpoints? PublishedMailServerEndpoints { get; set; }

    /// <summary>Gets the connection string the orchestration issued for the migrated MailFathom database.</summary>
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
                "The orchestration started without issuing a connection string for the MailFathom database.");

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

    /// <summary>Starts the composed MailFathom host and reports the address it serves on.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>The base address of the host's HTTP endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>
    /// <para>
    /// Starting is deliberately a request rather than part of <see cref="InitializeAsync" />. The host opens the same
    /// database every other test writes to, so bringing it up with the application would put it inside the environment
    /// of tests that assume they own that database. Only <c>ComposedHostCollectionDefinition</c> calls this, and that
    /// collection is ordered last.
    /// </para>
    /// <para>
    /// The first caller pays the start; the rest wait for it and receive the same address. The gate is held across the
    /// wait rather than around a flag, because two classes in the collection would otherwise both issue the start
    /// command and the second would race a resource that is already leaving its stopped state.
    /// </para>
    /// </remarks>
    public async Task<Uri> StartMailFathomHostAsync(CancellationToken cancellationToken)
    {
        await this.hostStartGate.WaitAsync(cancellationToken);

        try
        {
            if (this.startedHostBaseAddress is { } alreadyStarted)
            {
                return alreadyStarted;
            }

            using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startCancellation.CancelAfter(HostStartupTimeout);

            this.startedHostBaseAddress = await this.StartHostResourceAsync(startCancellation.Token);

            return this.startedHostBaseAddress;
        }
        finally
        {
            this.hostStartGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.hostStartGate.Dispose();

        if (this.application is not null)
        {
            await this.application.DisposeAsync();
        }
    }

    private async Task<Uri> StartHostResourceAsync(CancellationToken cancellationToken)
    {
        var startedApplication = this.application
            ?? throw new InvalidOperationException(
                "The MailFathom host is started before the suite started the application.");

        // The resource carries WithExplicitStart, so the app model created it and left it stopped. This is the command
        // the dashboard's own Start button issues, which keeps the suite starting the host the way an operator would.
        var startResult = await startedApplication.ResourceCommands.ExecuteCommandAsync(
            OrchestrationContract.HostResourceName,
            KnownResourceCommands.StartCommand,
            cancellationToken);

        if (!startResult.Success)
        {
            throw new InvalidOperationException(
                $"The MailFathom host resource refused to start [{startResult.Message}].");
        }

        await startedApplication.ResourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.HostResourceName,
            cancellationToken);

        // Read once the resource is healthy, because the host port is allocated when the project starts rather than
        // when the app model describes it.
        return startedApplication.GetEndpoint(
            OrchestrationContract.HostResourceName,
            OrchestrationContract.HostHttpEndpointName);
    }
}
