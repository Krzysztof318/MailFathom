// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>The connection settings a data source is built from, and where its password comes from afterwards.</summary>
/// <param name="ConnectionSettings">Everything but the credential: host, database, user name, and the provider's own options.</param>
/// <param name="PasswordSource">Which configured setting supplies the password when a physical connection opens.</param>
internal sealed record ComposedConnectionSettings(
    NpgsqlConnectionStringBuilder ConnectionSettings,
    DatabasePasswordSource PasswordSource);

/// <summary>Composes the PostgreSQL connection settings and keeps the credential out of them.</summary>
/// <remarks>
/// <para>
/// Two provisioning shapes are supported, because deployments genuinely differ. A connection string kept in ordinary
/// configuration carries host, database, and user name while the password comes from a secret block, so a
/// configuration file leaked from a backup yields no credential. Alternatively the whole connection string is itself
/// one secret, which is what a store-backed deployment usually wants: the connection string is not only a password,
/// and keeping it whole means one rotation instead of a credential split across two systems.
/// </para>
/// <para>
/// In both shapes the password is stripped from what the data source is built with and supplied per physical
/// connection instead, so rotating it takes effect without rebuilding anything. When neither shape is used the
/// configured connection string is applied unchanged, which keeps trust authentication and an orchestrator-provided
/// connection string working untouched — and leaves a credential written into that string unrotatable, which is why
/// the caller reports it.
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
    /// <returns>The credential-free connection settings and the source that supplies the credential per connection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured reference does not resolve, when neither source supplies a connection string, or when a password is configured twice.</exception>
    internal static async Task<ComposedConnectionSettings> ComposeAsync(
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

        if (configuredPassword is not null)
        {
            // Two sources for one credential leave the effective one decided by implementation order rather than by
            // configuration, and an operator rotating the one that loses would see no effect and no error.
            if (!string.IsNullOrEmpty(connectionSettings.Password))
            {
                throw new InvalidOperationException(
                    "The connection string already carries a password and Persistence:Password configures another. Remove one of them.");
            }

            return new ComposedConnectionSettings(connectionSettings, DatabasePasswordSource.PasswordSecret);
        }

        if (connectionStringSecret is not null && !string.IsNullOrEmpty(connectionSettings.Password))
        {
            connectionSettings.Password = null;

            return new ComposedConnectionSettings(connectionSettings, DatabasePasswordSource.ConnectionStringSecret);
        }

        return new ComposedConnectionSettings(connectionSettings, DatabasePasswordSource.None);
    }

    /// <summary>Retrieves the current database password from the configured source.</summary>
    /// <param name="passwordSource">Which configured setting supplies it.</param>
    /// <param name="connectionStringSecret">The block referencing a complete connection string, when that is the source.</param>
    /// <param name="configuredPassword">The password block, when that is the source.</param>
    /// <param name="resolver">The resolver that turns a reference into material.</param>
    /// <param name="cancellationToken">Cancels the resolution, which the data source triggers when it is disposed.</param>
    /// <returns>The password to authenticate the connection being opened with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no configured secret supplies a password, when the reference no longer resolves, or when the rotated material no longer carries one.</exception>
    /// <remarks>
    /// Revealing a resolved secret as a string is the second documented framework-contract boundary: the provider's
    /// password contract is a <see cref="string" />. The value is produced at the call that consumes it, never logged,
    /// and the resolved material is erased before this returns. Failing here fails the connection being opened rather
    /// than the process, so a credential source that is briefly unreachable stays a retryable connection failure.
    /// </remarks>
    internal static async Task<string> ResolveCurrentPasswordAsync(
        DatabasePasswordSource passwordSource,
        ConfiguredSecret? connectionStringSecret,
        ConfiguredSecret? configuredPassword,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return passwordSource switch
        {
            DatabasePasswordSource.PasswordSecret =>
                await ResolvePasswordSecretAsync(configuredPassword, resolver, cancellationToken),
            DatabasePasswordSource.ConnectionStringSecret =>
                await ResolvePasswordFromConnectionStringSecretAsync(connectionStringSecret, resolver, cancellationToken),
            _ => throw new InvalidOperationException(
                "No configured secret supplies the database password, so none can be retrieved for a connection."),
        };
    }

    /// <summary>Reports whether a password reached the connection string without passing through a secret block.</summary>
    /// <param name="connectionSettings">The composed connection settings.</param>
    /// <param name="connectionStringSecret">The block that supplied the connection string, or <see langword="null" />.</param>
    /// <param name="configuredPassword">The block that supplied the password, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the credential came from ordinary configuration.</returns>
    /// <remarks>
    /// This is a diagnostic, not a rule. Rejecting the shape would break a deployment whose connection string is
    /// injected whole and already resolved — an orchestrator, or a configuration provider backed by a managed secret
    /// store — where the credential never touched a file an operator could commit. It is also the one shape whose
    /// credential cannot rotate without a restart, because nothing re-reads it. The caller decides whether the
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

    private static async Task<string> ResolvePasswordSecretAsync(
        ConfiguredSecret? configuredPassword,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(configuredPassword?.SecretReference, cancellationToken);
        if (result.Secret is not { } password)
        {
            throw new InvalidOperationException(
                $"The database password secret reference could not be resolved [{result.Failure}].");
        }

        using (password)
        {
            return password.RevealAsString();
        }
    }

    /// <summary>Takes the password out of a rotated connection-string secret.</summary>
    /// <remarks>
    /// The rest of that connection string is deliberately ignored here. Host, database, and user name are read once,
    /// when the data source is built, because changing them describes a different database rather than a rotated
    /// credential, and adopting them under a live pool would leave already-open connections pointing elsewhere.
    /// </remarks>
    private static async Task<string> ResolvePasswordFromConnectionStringSecretAsync(
        ConfiguredSecret? connectionStringSecret,
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(connectionStringSecret?.SecretReference, cancellationToken);
        if (result.Secret is not { } material)
        {
            throw new InvalidOperationException(
                $"The connection string secret reference could not be resolved [{result.Failure}].");
        }

        using (material)
        {
            var rotatedConnectionSettings = ParseResolvedConnectionString(material.RevealAsString());

            return string.IsNullOrEmpty(rotatedConnectionSettings.Password)
                ? throw new InvalidOperationException(
                    "The material behind Persistence:ConnectionString no longer carries a password.")
                : rotatedConnectionSettings.Password;
        }
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
