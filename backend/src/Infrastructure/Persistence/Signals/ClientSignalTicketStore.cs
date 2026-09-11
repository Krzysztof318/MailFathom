// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Signals;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Signals;

/// <summary>Holds the deployment's unspent signal tickets in the one table every replica of it writes to.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source rather than EF Core queries, for the reason the anti-replay record is written
/// that way: none of the three statements has a query shape, the callers are a minting route and a hub rather than a
/// unit of work, and what each of them needs is the outcome PostgreSQL reports rather than a tracked entity. A tracked
/// spend would also be a read followed by a delete, which is exactly the pair that admits two connections on one
/// ticket.
/// </para>
/// <para>
/// The identifiers in every statement come from the mapped entity's own constants, so the statements and the schema are
/// one description; every value is a parameter, so nothing a client presented is ever composed into text.
/// </para>
/// <para>
/// Each command is bounded by the caller's own cancellation token and by whatever <c>Command Timeout</c> the composed
/// connection string carries, which is the arrangement the anti-replay record is written under and for the same reason:
/// these statements open no EF Core context, so <c>Persistence:CommandTimeoutSeconds</c> does not reach them.
/// </para>
/// <para>
/// A failure to reach the database is translated rather than absorbed. It is not absorbed because the answer this store
/// gives is what decides whether a connection is admitted, so a store that cannot answer is one whose caller must not
/// admit — turning an outage into a redeemed ticket would open every connection presenting anything. It is translated
/// because the port it crosses is an application contract and one of its callers is a hub that has authenticated
/// nothing yet.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class ClientSignalTicketStore(NpgsqlDataSource dataSource) : IClientSignalTicketStore
{
    /// <summary>Writes the ticket, and reports having written it, only while the deployment is under its own bound.</summary>
    /// <remarks>
    /// The count and the insert are one statement, so the bound is the deployment's rather than a number each replica
    /// finds room under separately. It counts what stands rather than what is live, which is the conservative
    /// direction: a deployment at its bound holding expired rows refuses a mint the sweep would have made room for, and
    /// the next mint past the sweep interval is served.
    /// </remarks>
    private const string MintTicketStatement = $"""
        INSERT INTO "{ClientSignalTicketEntity.TableName}"
            ("{ClientSignalTicketEntity.IdentifierColumnName}", "{ClientSignalTicketEntity.UserIdColumnName}", "{ClientSignalTicketEntity.SecretDigestColumnName}", "{ClientSignalTicketEntity.ExpiresAtColumnName}")
        SELECT @identifier, @userId, @secretDigest, @expiresAt
        WHERE (SELECT count(*) FROM "{ClientSignalTicketEntity.TableName}") < @mostOutstanding;
        """;

    /// <summary>Removes the presented ticket and hands back what it held, in one statement.</summary>
    /// <remarks>
    /// Removing and returning together is what makes a ticket single-use across replicas: two connections presenting
    /// one identifier at the same instant leave one with a row and one with nothing, settled by PostgreSQL rather than
    /// by a check either of them makes between two statements. The row is removed whether or not it had expired,
    /// because a ticket presented late is spent as surely as one presented in time and leaving it would keep a row the
    /// sweep would otherwise have to reach.
    /// </remarks>
    private const string RedeemTicketStatement = $"""
        DELETE FROM "{ClientSignalTicketEntity.TableName}"
        WHERE "{ClientSignalTicketEntity.IdentifierColumnName}" = @identifier
        RETURNING "{ClientSignalTicketEntity.UserIdColumnName}", "{ClientSignalTicketEntity.SecretDigestColumnName}", "{ClientSignalTicketEntity.ExpiresAtColumnName}";
        """;

    /// <summary>Removes what can no longer be presented, reaching it through the index on the expiry.</summary>
    /// <remarks>
    /// The comparison is against the indexed column alone, so the plan is a range over what has already expired rather
    /// than a scan of the whole table — which is what keeps the removal proportional to the connections opened since
    /// the last one instead of to the deployment's bound.
    /// </remarks>
    private const string RemoveExpiredStatement = $"""
        DELETE FROM "{ClientSignalTicketEntity.TableName}"
        WHERE "{ClientSignalTicketEntity.ExpiresAtColumnName}" <= @removableFrom;
        """;

    /// <inheritdoc />
    public async Task<bool> TryMintAsync(
        string identifier,
        MailUserId user,
        ReadOnlyMemory<byte> secretDigest,
        DateTimeOffset expiresAt,
        int mostOutstanding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        await using var command = dataSource.CreateCommand(MintTicketStatement);
        command.Parameters.AddWithValue("identifier", identifier);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.Add(new NpgsqlParameter("secretDigest", NpgsqlDbType.Bytea)
        {
            Value = secretDigest.ToArray(),
        });
        command.Parameters.AddWithValue("expiresAt", expiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("mostOutstanding", mostOutstanding);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"A signal connection ticket could not be held in {ClientSignalTicketEntity.TableName}, so no ticket was minted.",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task<RedeemedClientSignalTicket?> RedeemAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        await using var command = dataSource.CreateCommand(RedeemTicketStatement);
        command.Parameters.AddWithValue("identifier", identifier);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new RedeemedClientSignalTicket(
                MailUserId.Create(reader.GetGuid(0)),
                reader.GetFieldValue<byte[]>(1),
                reader.GetFieldValue<DateTimeOffset>(2));
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"The ticket a signal connection presented could not be spent against {ClientSignalTicketEntity.TableName}, so the connection was refused rather than admitted unrecorded.",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RemoveExpiredStatement);
        command.Parameters.AddWithValue("removableFrom", removableFrom.ToUniversalTime());

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"The signal connection tickets past the point they could still be presented could not be removed from {ClientSignalTicketEntity.TableName}.",
                failure);
        }
    }

    private static ClientSignalTicketStoreUnavailableException Unavailable(string operatorSafeMessage, Exception failure) =>
        new(
            $"{operatorSafeMessage} Check that the database is reachable and that this build's migrations have been applied to it.",
            failure);
}
