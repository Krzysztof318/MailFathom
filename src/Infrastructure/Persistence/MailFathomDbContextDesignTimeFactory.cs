// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MailFathom.Infrastructure.Persistence;

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
internal sealed class MailFathomDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MailFathomDbContext>
{
    /// <summary>The environment variable the Aspire orchestration issues the database connection string through.</summary>
    /// <remarks>
    /// It is the double-underscore encoding of <c>ConnectionStrings:mailfathom</c>, the key every referencing resource
    /// receives, so the migration resource and the running host address the same server by construction rather than by
    /// a value copied into two places.
    /// </remarks>
    internal const string OrchestratedConnectionStringVariableName = "ConnectionStrings__mailfathom";

    /// <summary>The environment variable a design-time command run outside the orchestration reads instead.</summary>
    internal const string DesignTimeConnectionStringVariableName = "MAILFATHOM_DESIGN_TIME_CONNECTION_STRING";

    /// <summary>The local development database assumed when neither variable is set.</summary>
    /// <remarks>It carries no password, so a design-time default can never become a credential in source control.</remarks>
    internal const string LocalDevelopmentConnectionString = "Host=localhost;Database=mailfathom;Username=mailfathom";

    /// <summary>The environment variable the text search configuration a migration is generated for is read from.</summary>
    /// <remarks>
    /// It is the double-underscore encoding of <c>Persistence:TextSearchConfiguration</c>, so a deployment that
    /// configures a non-default configuration generates its migration by exporting the setting it already has rather
    /// than by learning a second name for it.
    /// </remarks>
    internal const string TextSearchConfigurationVariableName = "Persistence__TextSearchConfiguration";

    /// <inheritdoc />
    public MailFathomDbContext CreateDbContext(string[] args) => new(
        BuildOptions(
            Environment.GetEnvironmentVariable(OrchestratedConnectionStringVariableName),
            Environment.GetEnvironmentVariable(DesignTimeConnectionStringVariableName)),
        ReadTextSearchConfiguration(Environment.GetEnvironmentVariable(TextSearchConfigurationVariableName)));

    /// <summary>Resolves the text search configuration the generated migration compiles into the search vector.</summary>
    /// <param name="configuredName">The configured name, or <see langword="null" /> when the variable is unset.</param>
    /// <returns>The configured configuration, or the default when none is set.</returns>
    /// <exception cref="ArgumentException">Thrown when a name is set but is not one MailFathom supports.</exception>
    /// <remarks>
    /// The value is compiled into a stored generated column, so a migration is generated for exactly one configuration
    /// and cannot serve another. Reading it here is what lets a deployment that configures one produce a migration
    /// that agrees with it; the host verifies the two against the live schema at startup regardless, because a
    /// migration identifier is the same whichever configuration produced it.
    /// </remarks>
    internal static PostgresTextSearchConfiguration ReadTextSearchConfiguration(string? configuredName) =>
        string.IsNullOrWhiteSpace(configuredName)
            ? PostgresTextSearchConfiguration.Default
            : PostgresTextSearchConfiguration.Create(configuredName);

    /// <summary>Builds the design-time options from the first connection string that is present.</summary>
    /// <param name="orchestratedConnectionString">What the orchestration issued, or <see langword="null" /> outside it.</param>
    /// <param name="designTimeConnectionString">The developer's own override, or <see langword="null" />.</param>
    /// <returns>Options bound to the first configured database, or to the local development default.</returns>
    /// <remarks>
    /// The orchestrated value wins so that a stale override left in a shell cannot silently point a migration at a
    /// different database than the one the resource being migrated is running against.
    /// </remarks>
    internal static DbContextOptions<MailFathomDbContext> BuildOptions(
        string? orchestratedConnectionString,
        string? designTimeConnectionString)
    {
        var connectionString = new[] { orchestratedConnectionString, designTimeConnectionString }
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
            ?? LocalDevelopmentConnectionString;

        return new DbContextOptionsBuilder<MailFathomDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}
