// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Refuses to start when the database is missing migrations this build was compiled against.</summary>
/// <remarks>
/// <para>
/// The host verifies and never applies, in every environment including Development. Applying is owned by one mechanism
/// — the AppHost's <c>mailfathom-migrations</c> resource locally, and a reviewed deployment step elsewhere — because an
/// instance that mutates schema while starting can race a second instance, can apply a destructive change nobody
/// reviewed at deploy time, and leaves the operator no point at which to take a backup. Two mechanisms that both apply
/// migrations would also make it impossible to say which one produced a given schema.
/// </para>
/// <para>
/// A stale schema is a startup failure rather than a warning. Serving traffic against a schema this build does not
/// recognize risks writing mail data into a shape the deletion and retention paths do not reach, and an instance that
/// refuses to start is diagnosed immediately while one that logs and continues is not.
/// </para>
/// <para>
/// The check runs in <see cref="IHostedService.StartAsync" /> rather than earlier, so the connection string composed
/// during the startup phase is already available, and in its own scope, because the inspector resolves the scoped
/// context. It is registered ahead of the workers, so nothing reads or writes mail before the schema is proven.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DatabaseSchemaStartupGate : IHostedService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly PostgresTextSearchConfiguration configuredTextSearchConfiguration;
    private readonly HostStartupGates startupGates;
    private readonly ILogger<DatabaseSchemaStartupGate> logger;

    /// <summary>Initializes a new database schema startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the inspector is resolved from.</param>
    /// <param name="configuredTextSearchConfiguration">The configuration the EF Core model was built from.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startupGates" /> is <see langword="null" />.</exception>
    public DatabaseSchemaStartupGate(
        IServiceScopeFactory scopeFactory,
        PostgresTextSearchConfiguration configuredTextSearchConfiguration,
        HostStartupGates startupGates,
        ILogger<DatabaseSchemaStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(startupGates);

        this.scopeFactory = scopeFactory;
        this.configuredTextSearchConfiguration = configuredTextSearchConfiguration;
        this.startupGates = startupGates;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DatabaseSchemaOutOfDateException">Thrown when the database has not applied every migration this build defines.</exception>
    /// <exception cref="DatabaseSchemaStateUnreadableException">Thrown when the schema state could not be read at all.</exception>
    /// <exception cref="DatabaseSchemaTextSearchConfigurationMismatchException">Thrown when the lexical index was built with another text search configuration.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var inspector = scope.ServiceProvider.GetRequiredService<IDatabaseSchemaInspector>();

        var pendingMigrations = await inspector.ReadPendingMigrationIdentifiersAsync(cancellationToken);

        if (pendingMigrations.Count is not 0)
        {
            throw new DatabaseSchemaOutOfDateException(
                $"The database has not applied {pendingMigrations.Count} migration(s) this build defines: {string.Join(", ", pendingMigrations)}. "
                + "Apply them through the AppHost's mailfathom-migrations resource locally, or as an explicit deployment step, and start the host again.",
                pendingMigrations);
        }

        // Only once the migration set matches, because the column this reads does not exist until the migration that
        // creates it has been applied.
        await this.VerifyTheLexicalIndexMatchesTheConfigurationAsync(inspector, cancellationToken);

        this.LogSchemaCurrent();

        this.startupGates.MarkCompleted(HostStartupGate.DatabaseSchema);
    }

    /// <summary>Fails startup when the search vector was built with a configuration this process is not configured for.</summary>
    /// <remarks>
    /// A migration is generated for one text search configuration and freezes it into a stored generated column, while
    /// the migration identifier is the same whichever configuration produced it. Comparing identifiers therefore
    /// cannot see this, and the consequence of missing it is silent: queries stemmed one way against lexemes built
    /// another return fewer results rather than an error. A schema that names no configuration at all is a failure
    /// too, raised by the inspector: this runs only once every migration is applied, so a database with no generated
    /// column, or one carrying an expression nothing recognizes, is not the database those migrations produce, and
    /// treating an answer the host could not identify as agreement is the one reading that starts the workers anyway.
    /// </remarks>
    private async Task VerifyTheLexicalIndexMatchesTheConfigurationAsync(
        IDatabaseSchemaInspector inspector,
        CancellationToken cancellationToken)
    {
        var schemaConfiguration = await inspector.ReadSearchVectorTextSearchConfigurationAsync(cancellationToken);

        if (string.Equals(schemaConfiguration, this.configuredTextSearchConfiguration.Value, StringComparison.Ordinal))
        {
            return;
        }

        throw new DatabaseSchemaTextSearchConfigurationMismatchException(
            $"The lexical email index was built with the '{schemaConfiguration}' text search configuration, but this host is configured for "
            + $"'{this.configuredTextSearchConfiguration.Value}'. Searching would stem queries one way and read lexemes built another, which returns "
            + $"fewer results rather than an error. Set Persistence:TextSearchConfiguration to '{schemaConfiguration}', or apply a migration generated "
            + $"for '{this.configuredTextSearchConfiguration.Value}' and rebuild the search documents.",
            schemaConfiguration,
            this.configuredTextSearchConfiguration.Value);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The database carries every migration this build defines.")]
    private partial void LogSchemaCurrent();
}
