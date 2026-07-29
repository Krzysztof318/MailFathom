// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;

namespace MailMcp.Host.Hosting;

/// <summary>Refuses to start when the database is missing migrations this build was compiled against.</summary>
/// <remarks>
/// <para>
/// The host verifies and never applies, in every environment including Development. Applying is owned by one mechanism
/// — the AppHost's <c>mailmcp-migrations</c> resource locally, and a reviewed deployment step elsewhere — because an
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
    private readonly ILogger<DatabaseSchemaStartupGate> logger;

    /// <summary>Initializes a new database schema startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the inspector is resolved from.</param>
    /// <param name="logger">The startup logger.</param>
    public DatabaseSchemaStartupGate(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseSchemaStartupGate> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DatabaseSchemaOutOfDateException">Thrown when the database has not applied every migration this build defines.</exception>
    /// <exception cref="DatabaseSchemaStateUnreadableException">Thrown when the migration history could not be read at all.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var pendingMigrations = await scope.ServiceProvider
            .GetRequiredService<IDatabaseSchemaInspector>()
            .ReadPendingMigrationIdentifiersAsync(cancellationToken);

        if (pendingMigrations.Count is 0)
        {
            this.LogSchemaCurrent();

            return;
        }

        throw new DatabaseSchemaOutOfDateException(
            $"The database has not applied {pendingMigrations.Count} migration(s) this build defines: {string.Join(", ", pendingMigrations)}. "
            + "Apply them through the AppHost's mailmcp-migrations resource locally, or as an explicit deployment step, and start the host again.",
            pendingMigrations);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The database carries every migration this build defines.")]
    private partial void LogSchemaCurrent();
}
