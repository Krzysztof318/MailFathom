// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates a context for <c>dotnet ef</c> without starting the host.</summary>
/// <remarks>
/// <para>
/// EF Core's design-time tooling otherwise falls back to the application's service provider, where the connection
/// string is composed during <see cref="Microsoft.Extensions.Hosting.IHostedLifecycleService.StartingAsync" />. The
/// tooling never runs that, so every model and migration command would fail on a connection string that startup has
/// not composed yet. EF looks for this factory first, so it never reaches that path.
/// </para>
/// <para>
/// Design time deliberately resolves no secret. <c>migrations add</c> and <c>dbcontext script</c> need the model
/// rather than a reachable server, and a developer's workstation is not where a deployment credential belongs. A
/// command that does need a live database is expected to be run by the AppHost's migration resource, which supplies
/// <see cref="OrchestratedConnectionStringVariableName" />; the two fallbacks below exist for a command run outside it.
/// </para>
/// </remarks>
internal sealed class MailMcpDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MailMcpDbContext>
{
    /// <summary>The environment variable the Aspire orchestration issues the database connection string through.</summary>
    /// <remarks>
    /// It is the double-underscore encoding of <c>ConnectionStrings:mailmcp</c>, the key every referencing resource
    /// receives, so the migration resource and the running host address the same server by construction rather than by
    /// a value copied into two places.
    /// </remarks>
    internal const string OrchestratedConnectionStringVariableName = "ConnectionStrings__mailmcp";

    /// <summary>The environment variable a design-time command run outside the orchestration reads instead.</summary>
    internal const string DesignTimeConnectionStringVariableName = "MAILMCP_DESIGN_TIME_CONNECTION_STRING";

    /// <summary>The local development database assumed when neither variable is set.</summary>
    /// <remarks>It carries no password, so a design-time default can never become a credential in source control.</remarks>
    internal const string LocalDevelopmentConnectionString = "Host=localhost;Database=mailmcp;Username=mailmcp";

    /// <inheritdoc />
    /// <remarks>
    /// The default text search configuration is used, because design time has no deployment to read one from. A
    /// deployment that configures another one therefore differs from the generated migration in exactly one place —
    /// the search vector's expression — and applying that migration is what fixes the configuration for its data.
    /// </remarks>
    public MailMcpDbContext CreateDbContext(string[] args) => new(
        BuildOptions(
            Environment.GetEnvironmentVariable(OrchestratedConnectionStringVariableName),
            Environment.GetEnvironmentVariable(DesignTimeConnectionStringVariableName)),
        PostgresTextSearchConfiguration.Default);

    /// <summary>Builds the design-time options from the first connection string that is present.</summary>
    /// <param name="orchestratedConnectionString">What the orchestration issued, or <see langword="null" /> outside it.</param>
    /// <param name="designTimeConnectionString">The developer's own override, or <see langword="null" />.</param>
    /// <returns>Options bound to the first configured database, or to the local development default.</returns>
    /// <remarks>
    /// The orchestrated value wins so that a stale override left in a shell cannot silently point a migration at a
    /// different database than the one the resource being migrated is running against.
    /// </remarks>
    internal static DbContextOptions<MailMcpDbContext> BuildOptions(
        string? orchestratedConnectionString,
        string? designTimeConnectionString)
    {
        var connectionString = new[] { orchestratedConnectionString, designTimeConnectionString }
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
            ?? LocalDevelopmentConnectionString;

        return new DbContextOptionsBuilder<MailMcpDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}
