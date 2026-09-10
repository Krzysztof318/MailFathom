// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Credentials;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.ClientAssertions;

/// <summary>Records a served client assertion in the one table every replica of a deployment writes to.</summary>
/// <remarks>
/// <para>
/// Two bare commands over the data source rather than EF Core queries, for the reason the persisted configuration
/// document is read as one: neither statement has a query shape, the caller is an authentication handler that is not
/// inside a unit of work and must not join one, and the outcome the spend needs is the row count PostgreSQL reports for
/// an insert that may conflict. A tracked entity would turn a conflict into an exception the caller then had to
/// classify, and would put the record inside whichever transaction happened to be open.
/// </para>
/// <para>
/// The identifiers in both statements come from the mapped entity's own constants, so the statements and the schema are
/// one description; every value is a parameter, so nothing a client minted is ever composed into text.
/// </para>
/// <para>
/// Each command is bounded by the caller's own cancellation token and by whatever <c>Command Timeout</c> the composed
/// connection string carries, which is the arrangement the persisted configuration document is read under. It is not
/// bounded by <c>Persistence:CommandTimeoutSeconds</c>, because that value is written into the EF Core context options
/// rather than into the pool, and the two statements here open no context.
/// </para>
/// <para>
/// A failure to reach the database is not caught here and must not be. The answer this store gives is what decides
/// whether a request is served, so a store that cannot answer is one whose caller must not serve — and turning an
/// outage into a <see langword="true" /> would open the replay window this exists to close, while turning it into a
/// <see langword="false" /> would report a replay that did not happen and hide the outage from the operator.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class ClientAssertionSpendStore(NpgsqlDataSource dataSource) : IClientAssertionSpendStore
{
    /// <summary>Writes the record, and reports having written it, in one statement.</summary>
    /// <remarks>
    /// The conflict target is the whole key, so the only insert that does nothing is one whose exact pair is already
    /// recorded. Which replica issued it does not appear anywhere: the row says the deployment served the assertion,
    /// which is the property the deployment promises rather than a fact about a process.
    /// </remarks>
    private const string SpendIdentifierStatement = $"""
        INSERT INTO "{SpentClientAssertionEntity.TableName}"
            ("{SpentClientAssertionEntity.CredentialKeyColumnName}", "{SpentClientAssertionEntity.IdentifierColumnName}", "{SpentClientAssertionEntity.ExpiresAtColumnName}")
        VALUES (@credentialKey, @identifier, @expiresAt)
        ON CONFLICT ("{SpentClientAssertionEntity.CredentialKeyColumnName}", "{SpentClientAssertionEntity.IdentifierColumnName}") DO NOTHING;
        """;

    /// <summary>Removes what has expired, reaching it through the index on the expiry.</summary>
    /// <remarks>
    /// The comparison is against the indexed column alone, so the plan is a range over what has already expired rather
    /// than a scan of everything ever spent — which is what keeps the removal proportional to the traffic since the
    /// last one instead of to the deployment's whole history of authenticated requests.
    /// </remarks>
    private const string RemoveExpiredStatement = $"""
        DELETE FROM "{SpentClientAssertionEntity.TableName}"
        WHERE "{SpentClientAssertionEntity.ExpiresAtColumnName}" <= @now;
        """;

    /// <inheritdoc />
    public async Task<bool> TrySpendAsync(
        string credentialKey,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialKey);
        ArgumentNullException.ThrowIfNull(identifier);

        await using var command = dataSource.CreateCommand(SpendIdentifierStatement);
        command.Parameters.AddWithValue("credentialKey", credentialKey);
        command.Parameters.AddWithValue("identifier", identifier);
        command.Parameters.AddWithValue("expiresAt", expiresAt.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <inheritdoc />
    public async Task RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RemoveExpiredStatement);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
