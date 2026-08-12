// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using System.Text;
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
/// The two MailFathom host resources are present in the app model and neither starts with it. Most of the suite verifies
/// classes against real infrastructure rather than a composed host, and every one of those tests owns the database and
/// the mailbox exclusively; a MailFathom reconciling folders underneath them would make its synchronization part of
/// their environment. <see cref="StartMailFathomHostAsync" /> and <see cref="StartMutualTlsHostAsync" /> start them on
/// request, and each is called from one collection the orderer places after every other, so nothing is asserting on
/// that infrastructure by the time a host touches it.
/// </para>
/// </remarks>
public sealed class MailFathomOrchestrationFixture : IAsyncLifetime
{
    /// <summary>Bounds the whole start-up, which on a cold machine includes pulling the images and building the migration project.</summary>
    /// <remarks>
    /// The analyzer is the largest of those images by an order of magnitude and loads a language model before it reports
    /// healthy, so a cold first run spends most of this budget before the first test runs. Raising it is the answer if that
    /// ever stops being enough; shortening the wait is not, because a run that started asserting against a half-ready
    /// analyzer would report the feature broken rather than the machine slow.
    /// </remarks>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Bounds the composed host's own start, which builds and runs a project against an already-migrated database.</summary>
    private static readonly TimeSpan HostStartupTimeout = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim hostStartGate = new(1, 1);

    /// <summary>The host resources the suite has already started, so a second caller waits for one rather than starting it again.</summary>
    /// <remarks>Kept per resource rather than per address, because one host publishes several endpoints and asking for a second of them must not issue a second start command.</remarks>
    private readonly HashSet<string> startedHostResources = new(StringComparer.Ordinal);

    private DistributedApplication? application;

    /// <summary>Gets the certificates the mutual-TLS host is served with and judges presented certificates against.</summary>
    /// <remarks>Issued when this fixture is constructed, because the material has to exist before the app model is built: the host reads it from the environment variables the build injects it into.</remarks>
    public OrchestratedMutualTlsCertificates MutualTlsCertificates { get; } = new();

    /// <summary>Gets or sets the connection string once the orchestration issued it.</summary>
    private string? IssuedDatabaseConnectionString { get; set; }

    /// <summary>Gets or sets the mail server endpoints once the orchestration published them.</summary>
    private OrchestratedMailServerEndpoints? PublishedMailServerEndpoints { get; set; }

    /// <summary>Gets or sets the analyzer address once the orchestration published it.</summary>
    private Uri? PublishedPersonalDataAnalyzerAddress { get; set; }

    /// <summary>Gets or sets the spam daemon's address once the orchestration published it.</summary>
    private Uri? PublishedSpamScannerAddress { get; set; }

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

    /// <summary>Gets the base address the orchestrated personal-data analyzer answers on.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started yet.</exception>
    public Uri PersonalDataAnalyzer => this.PublishedPersonalDataAnalyzerAddress
        ?? throw new InvalidOperationException(
            "The orchestrated personal-data analyzer address is requested before the suite started the application.");

    /// <summary>Gets the host and port the orchestrated spam daemon answers its line protocol on.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started yet.</exception>
    public Uri SpamScanner => this.PublishedSpamScannerAddress
        ?? throw new InvalidOperationException(
            "The orchestrated spam daemon address is requested before the suite started the application.");

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

        SupplyMutualTlsMaterial(builder, this.MutualTlsCertificates);

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

        // Healthy rather than running, and for a sharper reason than the mail server's: the analyzer loads a language
        // model before it serves anything, so a test that waited only for the container would ask an analyzer that is not
        // ready yet and read the refusal as an analyzer that recognises nothing.
        await this.application.ResourceNotifications.WaitForResourceHealthyAsync(
            OrchestrationContract.PersonalDataAnalyzerResourceName,
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

        this.PublishedPersonalDataAnalyzerAddress = this.application.GetEndpoint(
            OrchestrationContract.PersonalDataAnalyzerResourceName,
            OrchestrationContract.PersonalDataAnalyzerEndpointName);

        this.PublishedSpamScannerAddress = this.application.GetEndpoint(
            OrchestrationContract.SpamScannerResourceName,
            OrchestrationContract.SpamScannerEndpointName);

        // Running rather than healthy, and then the protocol's own readiness command, because the daemon declares no
        // health check the app model could express: it speaks a line protocol on a TCP port, so there is no route to
        // probe. Waiting matters here for the same reason it does for the analyzer — the container fetches its rule
        // updates and compiles the corpus before it listens, and a test that asked a daemon which is not up yet would
        // read the refused connection as a scanner that cannot be reached.
        await this.application.ResourceNotifications.WaitForResourceAsync(
            OrchestrationContract.SpamScannerResourceName,
            KnownResourceStates.Running,
            cancellationToken);

        await WaitForSpamScannerAsync(this.PublishedSpamScannerAddress, cancellationToken);
    }

    /// <summary>Starts the composed MailFathom host and reports the address it serves on.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>The base address of the host's HTTP endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>
    /// <para>
    /// Starting is deliberately a request rather than part of <see cref="InitializeAsync" />. The host opens the same
    /// database every other test writes to, so bringing it up with the application would put it inside the environment
    /// of tests that assume they own that database. Only <c>ComposedHostCollectionDefinition</c> calls this, and the
    /// orderer places that collection after the infrastructure collections and before the mutual-TLS one.
    /// </para>
    /// <para>
    /// The first caller pays the start; the rest wait for it and receive the same address. The gate is held across the
    /// wait rather than around a flag, because two classes in the collection would otherwise both issue the start
    /// command and the second would race a resource that is already leaving its stopped state.
    /// </para>
    /// </remarks>
    public async Task<Uri> StartMailFathomHostAsync(CancellationToken cancellationToken) => AsAddress(
        await this.StartHostOnceAsync(
            OrchestrationContract.HostResourceName,
            OrchestrationContract.HostHttpEndpointName,
            cancellationToken),
        Uri.UriSchemeHttp);

    /// <summary>Starts the composed MailFathom host and reports the address its administrative surface serves on.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>The base address of the host's administrative endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>
    /// The same host as <see cref="StartMailFathomHostAsync" /> and a second socket on it, which is what the
    /// administrative endpoint is: its own listener, its own credentials, and no route in common with the MCP surface.
    /// Whichever of the two is asked for first starts the resource, and the other then reads its own endpoint from the
    /// host already running.
    /// </remarks>
    public async Task<Uri> StartMailFathomAdminEndpointAsync(CancellationToken cancellationToken) => AsAddress(
        await this.StartHostOnceAsync(
            OrchestrationContract.HostResourceName,
            OrchestrationContract.HostAdminEndpointName,
            cancellationToken),
        Uri.UriSchemeHttp);

    /// <summary>Opens a client aimed at the composed host's MCP surface, starting the host if it is not yet running.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>A client whose base address is the surface's, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>
    /// A test states which surface it is talking to and writes the route it is asking for; the address between the two
    /// is this fixture's to know, because it is a port the orchestration allocated when the resource started. Handing
    /// back a client with that address already set is what keeps a test from restating the pairing, and what keeps a
    /// test aimed at one surface from being able to reach the other by holding the wrong address.
    /// </remarks>
    public async Task<HttpClient> OpenMcpEndpointClientAsync(CancellationToken cancellationToken) => new()
    {
        BaseAddress = await this.StartMailFathomHostAsync(cancellationToken),
    };

    /// <summary>Opens a client aimed at the composed host's administrative surface, starting the host if it is not yet running.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>A client whose base address is the surface's, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>The second socket on the same host, for the reason <see cref="StartMailFathomAdminEndpointAsync" /> gives.</remarks>
    public async Task<HttpClient> OpenAdminEndpointClientAsync(CancellationToken cancellationToken) => new()
    {
        BaseAddress = await this.StartMailFathomAdminEndpointAsync(cancellationToken),
    };

    /// <summary>Starts the MailFathom host served over HTTPS behind mutual TLS and reports the address it serves on.</summary>
    /// <param name="cancellationToken">Cancels waiting for the host to become reachable.</param>
    /// <returns>The base address of the host's HTTPS endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the orchestration has not started, or when the host resource refused the start command.</exception>
    /// <remarks>
    /// A second host rather than a posture on the first, for the reason <see cref="OrchestrationContract.MutualTlsHostResourceName" />
    /// states: whether a client certificate is required is one answer for a whole process. It is started on request on
    /// the same terms, because it opens the same database, and only <c>MutualTlsHostCollectionDefinition</c> calls
    /// this — a collection of its own, which the orderer places after the one that starts the host above, so a second
    /// project process is never starting while that collection measures a rate limit.
    /// </remarks>
    public async Task<Uri> StartMutualTlsHostAsync(CancellationToken cancellationToken) => AsAddress(
        await this.StartHostOnceAsync(
            OrchestrationContract.MutualTlsHostResourceName,
            OrchestrationContract.MutualTlsHostHttpsEndpointName,
            cancellationToken),
        Uri.UriSchemeHttps);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.hostStartGate.Dispose();
        this.MutualTlsCertificates.Dispose();

        if (this.application is not null)
        {
            await this.application.DisposeAsync();
        }
    }

    /// <summary>Reads a published endpoint as the address a client calls it at.</summary>
    /// <remarks>
    /// Every endpoint this app model declares carries the <c>tcp</c> scheme, so that none of them reaches
    /// <c>ASPNETCORE_URLS</c> — MailFathom refuses that variable, because each surface states where it is served in its
    /// own configuration section. What the app model publishes is therefore a <c>tcp</c> address to a socket that speaks
    /// HTTP or HTTPS, and this is where the two are reconciled: once, rather than in every test that builds a client.
    /// </remarks>
    private static Uri AsAddress(Uri publishedEndpoint, string scheme) =>
        new UriBuilder(publishedEndpoint) { Scheme = scheme }.Uri;

    /// <summary>Waits until the spam daemon answers the one command its protocol defines for exactly this question.</summary>
    /// <remarks>
    /// Written here rather than expressed in the app model because no health check the app model can declare speaks this
    /// protocol. It asks the daemon directly, in the fixture that already owns waiting for the topology, and it is
    /// deliberately the readiness command rather than a scan: a daemon that answers it has compiled its corpus, and
    /// scoring a message to find that out would spend the most expensive request the suite makes on a wait.
    /// </remarks>
    private static async Task WaitForSpamScannerAsync(Uri published, CancellationToken cancellationToken)
    {
        var readiness = "PING SPAMC/1.5\r\n\r\n"u8.ToArray();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var connection = new TcpClient();

                await connection.ConnectAsync(published.Host, published.Port, cancellationToken);

                var stream = connection.GetStream();

                await stream.WriteAsync(readiness, cancellationToken);
                connection.Client.Shutdown(SocketShutdown.Send);

                using var answer = new MemoryStream();

                await stream.CopyToAsync(answer, cancellationToken);

                if (Encoding.ASCII.GetString(answer.ToArray()).Contains("PONG", StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (Exception failure) when (failure is SocketException or IOException)
            {
                // The container is up and the daemon is not listening yet, which is the ordinary state for the first
                // half-minute of a cold start.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    /// <summary>Hands the mutual-TLS host the material the app model deliberately does not carry.</summary>
    /// <remarks>
    /// The app model names the environment variables its secret references read and stops there, so no private key
    /// enters the repository. Supplying them here rather than through the app host's own environment is what keeps them
    /// on the one resource that needs them instead of on every process the orchestration starts.
    /// </remarks>
    private static void SupplyMutualTlsMaterial(
        IDistributedApplicationTestingBuilder builder,
        OrchestratedMutualTlsCertificates certificates)
    {
        var mutualTlsHost = builder.Resources
            .OfType<ProjectResource>()
            .Single(resource => string.Equals(
                resource.Name,
                OrchestrationContract.MutualTlsHostResourceName,
                StringComparison.Ordinal));

        builder.CreateResourceBuilder(mutualTlsHost)
            .WithEnvironment(
                OrchestrationContract.MutualTlsServerCertificateChainVariable,
                certificates.ServerCertificateChainPem)
            .WithEnvironment(
                OrchestrationContract.MutualTlsServerPrivateKeyVariable,
                certificates.ServerPrivateKeyPem)
            .WithEnvironment(
                OrchestrationContract.MutualTlsClientTrustAnchorVariable,
                certificates.ClientTrustAnchorPem);
    }

    private async Task<Uri> StartHostOnceAsync(
        string resourceName,
        string endpointName,
        CancellationToken cancellationToken)
    {
        await this.hostStartGate.WaitAsync(cancellationToken);

        try
        {
            var startedApplication = this.application
                ?? throw new InvalidOperationException(
                    $"The MailFathom host resource {resourceName} is started before the suite started the application.");

            if (!this.startedHostResources.Contains(resourceName))
            {
                using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startCancellation.CancelAfter(HostStartupTimeout);

                await StartHostResourceAsync(startedApplication, resourceName, startCancellation.Token);

                this.startedHostResources.Add(resourceName);
            }

            // Read once the resource is healthy, because the host port is allocated when the project starts rather than
            // when the app model describes it.
            return startedApplication.GetEndpoint(resourceName, endpointName);
        }
        finally
        {
            this.hostStartGate.Release();
        }
    }

    private static async Task StartHostResourceAsync(
        DistributedApplication startedApplication,
        string resourceName,
        CancellationToken cancellationToken)
    {
        // The resource carries WithExplicitStart, so the app model created it and left it stopped. This is the command
        // the dashboard's own Start button issues, which keeps the suite starting the host the way an operator would.
        var startResult = await startedApplication.ResourceCommands.ExecuteCommandAsync(
            resourceName,
            KnownResourceCommands.StartCommand,
            cancellationToken);

        if (!startResult.Success)
        {
            throw new InvalidOperationException(
                $"The MailFathom host resource {resourceName} refused to start [{startResult.Message}].");
        }

        await startedApplication.ResourceNotifications.WaitForResourceHealthyAsync(
            resourceName,
            cancellationToken);
    }
}
