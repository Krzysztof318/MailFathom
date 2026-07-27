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
/// command that does need a live database takes its connection string from
/// <see cref="DesignTimeConnectionStringVariableName" />, which is a developer's own local database.
/// </para>
/// </remarks>
internal sealed class MailMcpDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MailMcpDbContext>
{
    /// <summary>The environment variable a design-time command reads its connection string from.</summary>
    internal const string DesignTimeConnectionStringVariableName = "MAILMCP_DESIGN_TIME_CONNECTION_STRING";

    /// <summary>The local development database assumed when the variable is unset.</summary>
    /// <remarks>It carries no password, so a design-time default can never become a credential in source control.</remarks>
    internal const string LocalDevelopmentConnectionString = "Host=localhost;Database=mailmcp;Username=mailmcp";

    /// <inheritdoc />
    public MailMcpDbContext CreateDbContext(string[] args) => new(BuildOptions(
        Environment.GetEnvironmentVariable(DesignTimeConnectionStringVariableName)));

    /// <summary>Builds the design-time options for a connection string that may be absent.</summary>
    /// <param name="configuredConnectionString">The value of the design-time environment variable, or <see langword="null" />.</param>
    /// <returns>Options bound to the configured database, or to the local development default.</returns>
    internal static DbContextOptions<MailMcpDbContext> BuildOptions(string? configuredConnectionString) =>
        new DbContextOptionsBuilder<MailMcpDbContext>()
            .UseNpgsql(string.IsNullOrWhiteSpace(configuredConnectionString)
                ? LocalDevelopmentConnectionString
                : configuredConnectionString)
            .Options;
}
