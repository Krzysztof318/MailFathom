// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Applies a resolved secret to a configured connection string.</summary>
/// <remarks>
/// The connection string in configuration keeps host, database, and user name and never carries the password, so a
/// configuration file leaked from a backup or a repository yields no database credential. When no password block is
/// configured the connection string is used unchanged, which keeps trust authentication and an orchestrator-provided
/// connection string working untouched.
/// </remarks>
internal static class ConnectionStringComposer
{
    /// <summary>Composes the connection settings a data source is built from.</summary>
    /// <param name="connectionString">The configured connection string, without a password.</param>
    /// <param name="configuredPassword">The password block, or <see langword="null" /> when the deployment configures none.</param>
    /// <param name="resolver">The resolver that turns the reference into material.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The connection settings, carrying the resolved password when one was configured.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured reference does not resolve.</exception>
    /// <remarks>
    /// Revealing the password as a string is the second documented framework-contract boundary: the PostgreSQL
    /// connection string is a string by definition. The composed value is never logged and never read back — the data
    /// source owns it — and the resolved material is erased as soon as it has been applied.
    /// </remarks>
    internal static async Task<NpgsqlConnectionStringBuilder> ComposeAsync(
        string connectionString,
        ConfiguredSecret? configuredPassword,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(resolver);

        var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);

        if (configuredPassword is null)
        {
            return connectionSettings;
        }

        var passwordResult = await resolver.ResolveAsync(configuredPassword.SecretReference, cancellationToken);
        if (passwordResult.Secret is not { } password)
        {
            throw new InvalidOperationException(
                $"The database password secret reference could not be resolved [{passwordResult.Failure}].");
        }

        using (password)
        {
            connectionSettings.Password = password.RevealAsString();
        }

        return connectionSettings;
    }
}
