// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Persistence;

namespace MailMcp.Host.Configuration;

/// <summary>Maps the bound persistence settings onto the connection settings the database adapter takes.</summary>
/// <remarks>
/// The two sources are deliberately read differently. The connection string comes from ordinary configuration because
/// it names which database this is, while the secret blocks come from whichever snapshot the caller hands over — the
/// startup one when the pool is composed, the published one when a connection needs a credential. Keeping the mapping
/// in one place is what stops those two callers from drifting apart.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this mapper.")]
internal sealed class DatabaseConnectionSettingsMapper(IConfiguration configuration)
{
    /// <summary>Builds the adapter's connection settings from one persistence snapshot.</summary>
    /// <param name="persistenceSettings">The bound persistence settings.</param>
    /// <returns>Where the connection string and the credential come from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistenceSettings" /> is <see langword="null" />.</exception>
    internal PostgresConnectionSettings Map(PersistenceOptions persistenceSettings)
    {
        ArgumentNullException.ThrowIfNull(persistenceSettings);

        return new PostgresConnectionSettings(
            configuration.GetConnectionString("mailmcp"),
            persistenceSettings.ConnectionString,
            persistenceSettings.Password);
    }
}
