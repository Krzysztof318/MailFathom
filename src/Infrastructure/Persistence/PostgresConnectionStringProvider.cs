// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Composes the PostgreSQL connection string once, before anything opens a connection.</summary>
/// <remarks>
/// <para>
/// Composing it needs the database credentials, and resolving a secret is asynchronous. A dependency injection factory
/// is not: doing the work there would mean blocking a thread on a read that a slow or unreachable credential source
/// can stall, with no token to cancel it, so a host could hang during startup or shutdown instead of failing. Startup
/// is the one place where asynchronous work has a natural home, so the composition happens in
/// <see cref="IHostedLifecycleService.StartingAsync" /> under the host's own cancellation.
/// </para>
/// <para>
/// This type deliberately stops at the connection string and never builds the <see cref="NpgsqlDataSource" />. Building
/// it here would create a disposable the container has not made and therefore does not track, so a host that never
/// resolves a context — synchronization disabled, no request served — would shut down leaving a connection pool open.
/// Handing the container a plain string keeps creation and disposal in one place, and building a data source from an
/// already-composed string is synchronous and cheap.
/// </para>
/// <para>
/// The host runs <see cref="IHostedLifecycleService.StartingAsync" /> for every hosted service before any
/// <see cref="IHostedService.StartAsync" />, so no worker can reach the database first. Requesting the connection
/// string before that throws rather than quietly falling back to an unresolved one.
/// </para>
/// </remarks>
internal sealed partial class PostgresConnectionStringProvider : IHostedLifecycleService
{
    private readonly Func<PostgresConnectionSettings> currentConnectionSettings;
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly SecretResolutionOptions resolutionOptions;
    private readonly ILogger<PostgresConnectionStringProvider> logger;

    /// <summary>Initializes a new PostgreSQL connection string provider.</summary>
    /// <param name="currentConnectionSettings">Supplies the settings validated for the current configuration, re-read whenever a credential is needed.</param>
    /// <param name="secretReferenceResolver">The resolver that turns a reference into material.</param>
    /// <param name="resolutionOptions">The deployment's interpretation mode.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public PostgresConnectionStringProvider(
        Func<PostgresConnectionSettings> currentConnectionSettings,
        ISecretReferenceResolver secretReferenceResolver,
        SecretResolutionOptions resolutionOptions,
        ILogger<PostgresConnectionStringProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(currentConnectionSettings);
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(resolutionOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.currentConnectionSettings = currentConnectionSettings;
        this.secretReferenceResolver = secretReferenceResolver;
        this.resolutionOptions = resolutionOptions;
        this.logger = logger;
    }

    /// <summary>Gets or sets the connection string once startup composed it.</summary>
    private string? ComposedConnectionString { get; set; }

    /// <summary>Gets or sets which configured setting supplies the password per connection.</summary>
    private DatabasePasswordSource PasswordSource { get; set; }

    /// <summary>Gets the composed connection string, which deliberately carries no rotatable credential.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the host has not run startup composition yet.</exception>
    /// <remarks>
    /// A password provisioned through a secret block is stripped from this string and supplied per physical connection
    /// instead, so rotating it never means rebuilding the pool. Host, database, and user name are read once, because
    /// changing them describes a different database rather than a rotated credential. Call
    /// <see cref="SupplyThePasswordPerConnection" /> on the builder that consumes this string, or the deployment
    /// authenticates with no password at all.
    /// </remarks>
    internal string ConnectionString => this.ComposedConnectionString
        ?? throw new InvalidOperationException(
            "The PostgreSQL connection string is requested before host startup composed it. Register the infrastructure services on a host that runs hosted service lifecycle events, or use the design-time context factory outside a host.");

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when a configured reference does not resolve or a password is configured twice.</exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var startupSettings = this.currentConnectionSettings();

        var composed = await ConnectionStringComposer.ComposeAsync(
            startupSettings.ConfiguredConnectionString,
            startupSettings.ConnectionStringSecret,
            startupSettings.Password,
            this.secretReferenceResolver,
            cancellationToken);

        this.WarnWhenTheCredentialBypassedTheSecretBlock(startupSettings, composed.ConnectionSettings);

        this.ComposedConnectionString = composed.ConnectionSettings.ConnectionString;
        this.PasswordSource = composed.PasswordSource;
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

    /// <summary>Points a data source builder at the configured credential source instead of baking a password into it.</summary>
    /// <param name="dataSourceBuilder">The builder the container builds the data source from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataSourceBuilder" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The provider invokes this when it opens a physical connection, so a credential rotated behind an unchanged
    /// reference authenticates the next connection with no restart and no rebuilt data source. Connections already
    /// open are untouched and finish with the credential they authenticated with, which is what makes a rotation safe
    /// for work in flight. Pooled logical connections that reuse an open physical one likewise keep it; the rotated
    /// credential applies from the next physical connect.
    /// </para>
    /// <para>
    /// The synchronous provider deliberately throws, as the provider's own documentation recommends. Retrieval is
    /// asynchronous by contract and can reach a file or one day a managed store, and satisfying a synchronous callback
    /// would mean blocking a thread on it. Every MailMcp database access opens its connection asynchronously, so the
    /// synchronous path is unreachable rather than merely discouraged.
    /// </para>
    /// </remarks>
    internal void SupplyThePasswordPerConnection(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        ArgumentNullException.ThrowIfNull(dataSourceBuilder);

        if (this.PasswordSource == DatabasePasswordSource.None)
        {
            return;
        }

        dataSourceBuilder.UsePasswordProvider(
            _ => throw new NotSupportedException(
                "The PostgreSQL password is retrieved asynchronously. Open the connection with OpenAsync."),
            async (_, cancellationToken) => await this.RetrieveCurrentPasswordAsync(cancellationToken));
    }

    /// <summary>Retrieves the password the connection being opened should authenticate with.</summary>
    /// <param name="cancellationToken">Cancels the retrieval; the data source triggers it when it is disposed.</param>
    /// <returns>The current password.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no configured secret supplies one or the reference no longer resolves.</exception>
    /// <remarks>
    /// The configured blocks are re-read here rather than captured, so an operator who repoints
    /// <c>Persistence:Password</c> at a different credential name has it take effect on the next physical connection,
    /// exactly as rotating the material behind an unchanged name already does.
    /// </remarks>
    internal Task<string> RetrieveCurrentPasswordAsync(CancellationToken cancellationToken)
    {
        var connectionSettings = this.currentConnectionSettings();

        return ConnectionStringComposer.ResolveCurrentPasswordAsync(
            this.PasswordSource,
            connectionSettings.ConnectionStringSecret,
            connectionSettings.Password,
            this.secretReferenceResolver,
            cancellationToken);
    }

    /// <summary>Reports a database password that reached the connection string without a secret block.</summary>
    /// <remarks>
    /// It is a warning rather than a startup failure because the same shape is both a mistake and a legitimate
    /// deployment. An orchestrator or a configuration provider backed by a managed secret store injects a complete,
    /// already-resolved connection string that no operator could commit, and rejecting it would force a deployment to
    /// take the credential apart for no gain. Only <see cref="SecretValueInterpretation.ReferenceOnly" /> is reported,
    /// because that mode is the deployment stating that every secret arrives by reference, which this contradicts. The
    /// inline modes are the deliberate opt-in for a pre-resolved value, so warning there would be noise.
    /// </remarks>
    private void WarnWhenTheCredentialBypassedTheSecretBlock(
        PostgresConnectionSettings connectionSettings,
        NpgsqlConnectionStringBuilder composedConnectionSettings)
    {
        if (this.resolutionOptions.Interpretation == SecretValueInterpretation.ReferenceOnly
            && ConnectionStringComposer.CarriesPasswordFromOrdinaryConfiguration(
                composedConnectionSettings,
                connectionSettings.ConnectionStringSecret,
                connectionSettings.Password))
        {
            this.LogConnectionStringCarriesAPassword();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The configured connection string carries a database password that passed through no secret block, so it is neither erased after use nor rotatable by reference. Move it behind Persistence:Password, or supply the whole connection string through Persistence:ConnectionString.")]
    private partial void LogConnectionStringCarriesAPassword();
}
