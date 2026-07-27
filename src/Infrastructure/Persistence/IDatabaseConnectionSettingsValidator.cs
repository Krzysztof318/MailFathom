// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Proves that reloaded database connection settings can be used before they are published.</summary>
/// <remarks>
/// <para>
/// Resolving the references in a candidate is not enough on its own. The material behind
/// <c>Persistence:ConnectionString</c> is a connection string, so it has to parse and — when it is what supplies the
/// credential — still carry one; material that resolves but does not parse would pass a reference check, replace the
/// last known good settings, and then fail every connection opened afterwards, which is the outcome the reload
/// contract exists to prevent.
/// </para>
/// <para>
/// The contract lives in <c>Infrastructure</c> because only the adapter that composed the data source knows which
/// setting currently supplies the credential, and the host that publishes snapshots must not have to model the
/// provider's connection string to find out.
/// </para>
/// </remarks>
public interface IDatabaseConnectionSettingsValidator
{
    /// <summary>Finds everything that stops a reloaded candidate from being adopted.</summary>
    /// <param name="candidate">The connection settings a reloaded configuration would publish.</param>
    /// <param name="cancellationToken">Cancels the resolution the check performs.</param>
    /// <returns>One failure per problem, empty when the candidate can be adopted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>Reports nothing before host startup has composed the connection, because startup composition validates the first settings itself and fails the host when they are unusable.</remarks>
    Task<IReadOnlyList<DatabaseConnectionConfigurationFailure>> FindConfigurationFailuresAsync(
        PostgresConnectionSettings candidate,
        CancellationToken cancellationToken);
}
