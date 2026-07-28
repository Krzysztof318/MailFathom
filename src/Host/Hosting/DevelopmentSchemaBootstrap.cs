// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>Creates the local schema at startup for a developer's own database, and refuses to do it anywhere else.</summary>
/// <remarks>
/// <para>
/// Temporary scaffolding that specification 19 removes together with
/// <see cref="IDevelopmentSchemaCreator" /> and <see cref="PersistenceOptions.CreateSchemaFromModelOnStartup" />, once
/// the reviewed baseline migration exists. Until then a developer has no other way to obtain the tables the host reads
/// and writes, because migrations are deliberately deferred while the schema is still growing.
/// </para>
/// <para>
/// Turning the setting on outside Development fails startup instead of creating anything. The environment is checked
/// here rather than trusted to deployment discipline, because the failure this guards against is silent: a schema
/// created from whatever the model happened to say that day looks like a working database until the first migration
/// tries to reconcile with it. An operator who wanted the tables gets a message; nobody gets an unreviewed schema.
/// </para>
/// <para>
/// The work runs in <see cref="IHostedService.StartAsync" /> rather than earlier, so the connection string composed
/// during the startup phase is already available, and in its own scope, because the creator resolves the scoped
/// <c>DbContext</c>.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DevelopmentSchemaBootstrap : IHostedService
{
    private readonly IHostEnvironment environment;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly PersistenceOptions options;
    private readonly ILogger<DevelopmentSchemaBootstrap> logger;

    /// <summary>Initializes a new development schema bootstrap.</summary>
    public DevelopmentSchemaBootstrap(
        IHostEnvironment environment,
        IServiceScopeFactory scopeFactory,
        IOptions<PersistenceOptions> options,
        ILogger<DevelopmentSchemaBootstrap> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.environment = environment;
        this.scopeFactory = scopeFactory;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when the schema bootstrap is enabled in any environment other than Development, before any statement
    /// reaches the database.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!this.options.CreateSchemaFromModelOnStartup)
        {
            return;
        }

        if (!this.environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Persistence:{nameof(PersistenceOptions.CreateSchemaFromModelOnStartup)} creates the schema from the EF Core model "
                + $"and is supported in the Development environment only, but the environment is '{this.environment.EnvironmentName}'. "
                + "Apply reviewed migrations instead.");
        }

        await using var scope = this.scopeFactory.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<IDevelopmentSchemaCreator>()
            .CreateSchemaAsync(cancellationToken);

        this.LogSchemaCreated(this.environment.EnvironmentName);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The local schema was created from the EF Core model in the {EnvironmentName} environment. This is temporary scaffolding and is replaced by reviewed migrations.")]
    private partial void LogSchemaCreated(string environmentName);
}
