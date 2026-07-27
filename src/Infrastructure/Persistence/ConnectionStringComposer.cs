// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Applies resolved secrets to a configured connection string.</summary>
/// <remarks>
/// <para>
/// Two provisioning shapes are supported, because deployments genuinely differ. A connection string kept in ordinary
/// configuration carries host, database, and user name while the password joins it from a secret block, so a
/// configuration file leaked from a backup yields no credential. Alternatively the whole connection string is itself
/// one secret, which is what a store-backed deployment usually wants: the connection string is not only a password,
/// and keeping it whole means one rotation instead of a credential split across two systems.
/// </para>
/// <para>
/// When neither is used the configured connection string is applied unchanged, which keeps trust authentication and an
/// orchestrator-provided connection string working untouched.
/// </para>
/// </remarks>
internal static class ConnectionStringComposer
{
    /// <summary>Composes the connection settings a data source is built from.</summary>
    /// <param name="configuredConnectionString">The connection string from ordinary configuration, or <see langword="null" /> when a secret supplies it.</param>
    /// <param name="connectionStringSecret">The block referencing a complete connection string, or <see langword="null" /> when ordinary configuration supplies it.</param>
    /// <param name="configuredPassword">The password block, or <see langword="null" /> when the connection string already carries every credential.</param>
    /// <param name="resolver">The resolver that turns a reference into material.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The connection settings, carrying the resolved password when one was configured.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured reference does not resolve, when neither source supplies a connection string, or when a password is configured twice.</exception>
    /// <remarks>
    /// Revealing a resolved secret as a string is the second documented framework-contract boundary: a PostgreSQL
    /// connection string is a string by definition. The composed value is never logged and never read back — the data
    /// source owns it — and the resolved material is erased as soon as it has been applied.
    /// </remarks>
    internal static async Task<NpgsqlConnectionStringBuilder> ComposeAsync(
        string? configuredConnectionString,
        ConfiguredSecret? connectionStringSecret,
        ConfiguredSecret? configuredPassword,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var connectionSettings = await ComposeBaseSettingsAsync(
            configuredConnectionString,
            connectionStringSecret,
            resolver,
            cancellationToken);

        if (configuredPassword is null)
        {
            return connectionSettings;
        }

        // Two sources for one credential leave the effective one decided by implementation order rather than by
        // configuration, and an operator rotating the one that loses would see no effect and no error.
        if (!string.IsNullOrEmpty(connectionSettings.Password))
        {
            throw new InvalidOperationException(
                "The connection string already carries a password and Persistence:Password configures another. Remove one of them.");
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

    /// <summary>Reports whether a password reached the connection string without passing through a secret block.</summary>
    /// <param name="connectionSettings">The composed connection settings.</param>
    /// <param name="connectionStringSecret">The block that supplied the connection string, or <see langword="null" />.</param>
    /// <param name="configuredPassword">The block that supplied the password, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the credential came from ordinary configuration.</returns>
    /// <remarks>
    /// This is a diagnostic, not a rule. Rejecting the shape would break a deployment whose connection string is
    /// injected whole and already resolved — an orchestrator, or a configuration provider backed by a managed secret
    /// store — where the credential never touched a file an operator could commit. The caller decides whether the
    /// deployment's declared interpretation makes it worth reporting.
    /// </remarks>
    internal static bool CarriesPasswordFromOrdinaryConfiguration(
        NpgsqlConnectionStringBuilder connectionSettings,
        ConfiguredSecret? connectionStringSecret,
        ConfiguredSecret? configuredPassword)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);

        return connectionStringSecret is null
            && configuredPassword is null
            && !string.IsNullOrEmpty(connectionSettings.Password);
    }

    private static async Task<NpgsqlConnectionStringBuilder> ComposeBaseSettingsAsync(
        string? configuredConnectionString,
        ConfiguredSecret? connectionStringSecret,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        if (connectionStringSecret is null)
        {
            return string.IsNullOrWhiteSpace(configuredConnectionString)
                ? throw new InvalidOperationException(
                    "No PostgreSQL connection string is configured. Supply ConnectionStrings:mailmcp or Persistence:ConnectionString.")
                : new NpgsqlConnectionStringBuilder(configuredConnectionString);
        }

        var result = await resolver.ResolveAsync(connectionStringSecret.SecretReference, cancellationToken);
        if (result.Secret is not { } material)
        {
            throw new InvalidOperationException(
                $"The connection string secret reference could not be resolved [{result.Failure}].");
        }

        using (material)
        {
            return ParseResolvedConnectionString(material.RevealAsString());
        }
    }

    /// <summary>Parses a connection string that arrived as secret material.</summary>
    /// <remarks>
    /// The provider's own parse failure quotes the offending keyword and value, so letting it escape would print a
    /// resolved connection string — password included — into a startup log. The replacement names the setting only.
    /// </remarks>
    private static NpgsqlConnectionStringBuilder ParseResolvedConnectionString(string resolvedConnectionString)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(resolvedConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "The material behind Persistence:ConnectionString is not a valid PostgreSQL connection string.");
        }
    }
}
