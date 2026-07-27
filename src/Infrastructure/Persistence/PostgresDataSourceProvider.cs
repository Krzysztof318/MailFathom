// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Builds the PostgreSQL data source once, before anything opens a connection.</summary>
/// <remarks>
/// <para>
/// Composing the data source needs the database credentials, and resolving a secret is asynchronous. A dependency
/// injection factory is not: doing the work there would mean blocking a thread on a read that a slow or unreachable
/// credential source can stall, with no token to cancel it, so a host could hang during startup or shutdown instead of
/// failing. Startup is the one place where asynchronous work has a natural home, so the composition happens in
/// <see cref="IHostedLifecycleService.StartingAsync" /> under the host's own cancellation and the factory only hands
/// out what is already built.
/// </para>
/// <para>
/// The host runs <see cref="IHostedLifecycleService.StartingAsync" /> for every hosted service before any
/// <see cref="IHostedService.StartAsync" />, so no worker can reach the database first. Requesting the data source
/// before that throws rather than quietly building a second one from an unresolved connection string.
/// </para>
/// <para>
/// Disposal belongs to the container, which tracks the data source the registered factory returns. This type must not
/// dispose it as well; a data source disposed twice would tear down a connection pool another owner still believes in.
/// </para>
/// </remarks>
internal sealed partial class PostgresDataSourceProvider : IHostedLifecycleService
{
    private readonly PostgresConnectionSettings connectionSettings;
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly SecretResolutionOptions resolutionOptions;
    private readonly ILogger<PostgresDataSourceProvider> logger;

    /// <summary>Initializes a new PostgreSQL data source provider.</summary>
    /// <param name="connectionSettings">Where the connection string and the password come from.</param>
    /// <param name="secretReferenceResolver">The resolver that turns a reference into material.</param>
    /// <param name="resolutionOptions">The deployment's interpretation mode.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public PostgresDataSourceProvider(
        PostgresConnectionSettings connectionSettings,
        ISecretReferenceResolver secretReferenceResolver,
        SecretResolutionOptions resolutionOptions,
        ILogger<PostgresDataSourceProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(resolutionOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.connectionSettings = connectionSettings;
        this.secretReferenceResolver = secretReferenceResolver;
        this.resolutionOptions = resolutionOptions;
        this.logger = logger;
    }

    /// <summary>Gets or sets the data source once startup composed it.</summary>
    private NpgsqlDataSource? ComposedDataSource { get; set; }

    /// <summary>Gets the composed data source.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the host has not run startup composition yet.</exception>
    internal NpgsqlDataSource DataSource => this.ComposedDataSource
        ?? throw new InvalidOperationException(
            "The PostgreSQL data source is requested before host startup composed it. Register the infrastructure services on a host that runs hosted service lifecycle events.");

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when a configured reference does not resolve or a password is configured twice.</exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var composed = await ConnectionStringComposer.ComposeAsync(
            this.connectionSettings.ConfiguredConnectionString,
            this.connectionSettings.ConnectionStringSecret,
            this.connectionSettings.Password,
            this.secretReferenceResolver,
            cancellationToken);

        this.WarnWhenTheCredentialBypassedTheSecretBlock(composed);

        this.ComposedDataSource = new NpgsqlDataSourceBuilder(composed.ConnectionString).Build();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reports a database password that reached the connection string without a secret block.</summary>
    /// <remarks>
    /// It is a warning rather than a startup failure because the same shape is both a mistake and a legitimate
    /// deployment. An orchestrator or a configuration provider backed by a managed secret store injects a complete,
    /// already-resolved connection string that no operator could commit, and rejecting it would force a deployment to
    /// take the credential apart for no gain. Only <see cref="SecretValueInterpretation.ReferenceOnly" /> is reported,
    /// because that mode is the deployment stating that every secret arrives by reference, which this contradicts. The
    /// inline modes are the deliberate opt-in for a pre-resolved value, so warning there would be noise.
    /// </remarks>
    private void WarnWhenTheCredentialBypassedTheSecretBlock(NpgsqlConnectionStringBuilder composed)
    {
        if (this.resolutionOptions.Interpretation == SecretValueInterpretation.ReferenceOnly
            && ConnectionStringComposer.CarriesPasswordFromOrdinaryConfiguration(
                composed,
                this.connectionSettings.ConnectionStringSecret,
                this.connectionSettings.Password))
        {
            this.LogConnectionStringCarriesAPassword();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The configured connection string carries a database password that passed through no secret block, so it is neither erased after use nor rotatable by reference. Move it behind Persistence:Password, or supply the whole connection string through Persistence:ConnectionString.")]
    private partial void LogConnectionStringCarriesAPassword();
}
