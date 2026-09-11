// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Data;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Sessions;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.ClientSessions;

/// <summary>Holds the deployment's signed-in client sessions in the one table every replica of it reads and writes.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source rather than EF Core queries, in the shape
/// <see cref="Signals.ClientSignalTicketStore" /> landed: the callers are an authentication handler and a sign-in route
/// rather than a unit of work, none of the statements has a query shape, and two of them are transactions whose lock
/// order is the point. The identifiers come from the mapped entities' own constants, so the statements and the schema
/// are one description, and every value is a parameter, so nothing a client presented is ever composed into text.
/// </para>
/// <para>
/// <b>Both writing paths take the same three things in the same order: the user row, then the credential row, then the
/// session row.</b> An erasure takes all three that way by construction, since it deletes the user and the credentials
/// and sessions follow by cascade, and a disable is the same order with the first step absent. Either inversion — a
/// renewal removing before locking, or a mint locking the credential before the user — turns a bounded wait into a
/// deadlock that PostgreSQL breaks by aborting a renewal, a disable, or an erasure outright.
/// </para>
/// <para>
/// <b>The share lock is what the enabled check needs, and an <c>EXISTS</c> clause in the insert would not do.</b> Such
/// a clause reads the state committed as of its own statement's start, so a disable committing while the insert runs is
/// invisible to it — and that disable's own removal has already passed over a row the insert is about to write, leaving
/// exactly the live session the lock exists to prevent. A plain update takes <c>FOR NO KEY UPDATE</c>, which conflicts
/// with <c>FOR SHARE</c>, so the two never proceed together whichever of them arrives first.
/// </para>
/// <para>
/// Each command is bounded by the caller's own cancellation token and by whatever <c>Command Timeout</c> the composed
/// connection string carries, which is the arrangement the signal ticket and the anti-replay record are both written
/// under: these statements open no EF Core context, so <c>Persistence:CommandTimeoutSeconds</c> does not reach them.
/// </para>
/// <para>
/// A failure to reach the database is translated rather than absorbed, and what the caller does with it is refuse as
/// unavailable rather than as unauthenticated — a store that cannot answer must not be read as a session nobody holds,
/// because a client meets that by asking for a password.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class ClientSessionStore(NpgsqlDataSource dataSource) : IClientSessionStore
{
    /// <summary>Holds the user row for the rest of the transaction, and reports whether the deployment still serves them.</summary>
    /// <remarks>
    /// The first of the three locks, and it earns its place twice over: it is what orders a mint or a renewal against
    /// an erasure, and under <c>READ COMMITTED</c> it also refuses outright where the erasure committed first, the row
    /// it asks for being gone by then.
    /// </remarks>
    private const string LockUserStatement = $"""
        SELECT 1 FROM "{UserAccountEntity.TableName}"
        WHERE "{UserAccountEntity.IdColumnName}" = @userId
        FOR SHARE;
        """;

    /// <summary>Holds the credential row for the rest of the transaction, and reports whether it still authenticates requests.</summary>
    /// <remarks>
    /// The condition and the lock are one statement, so a disable arriving while this runs waits for the commit and
    /// then removes what it wrote, and one that committed first leaves this matching no row. Taken on a session naming
    /// a credential and on no other, because an endpoint requiring none has nothing to lock and nothing an operator
    /// could disable.
    /// </remarks>
    private const string LockEnabledCredentialStatement = $"""
        SELECT 1 FROM "{UserCredentialEntity.TableName}"
        WHERE "{UserCredentialEntity.IdColumnName}" = @credentialId AND "{UserCredentialEntity.EnabledColumnName}"
        FOR SHARE;
        """;

    /// <summary>Writes the session, and reports having written it, only while the deployment is under its own bound.</summary>
    /// <remarks>
    /// The count and the insert are one statement, so ten thousand live sessions is one number for the deployment
    /// rather than a number each replica finds room under separately. It counts what stands rather than what is live,
    /// which is why the caller sweeps before it lets this refuse a sign-in.
    /// </remarks>
    private const string MintSessionStatement = $"""
        INSERT INTO "{ClientSessionEntity.TableName}"
            ("{ClientSessionEntity.IdentifierColumnName}", "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.CredentialIdColumnName}", "{ClientSessionEntity.PermissionsColumnName}", "{ClientSessionEntity.SecretDigestColumnName}", "{ClientSessionEntity.ExpiresAtColumnName}")
        SELECT @identifier, @userId, @credentialId, @permissions, @secretDigest, @expiresAt
        WHERE (SELECT count(*) FROM "{ClientSessionEntity.TableName}") < @mostLiveSessions;
        """;

    /// <summary>Writes the replacement a renewal carries the removed session's grant into.</summary>
    /// <remarks>A replacement never meets the bound, because it does not grow the table: the bound refuses a new sign-in rather than ending a live session, and a renewal refused for capacity would end every live session at its own expiry for as long as the deployment stayed full.</remarks>
    private const string RenewSessionStatement = $"""
        INSERT INTO "{ClientSessionEntity.TableName}"
            ("{ClientSessionEntity.IdentifierColumnName}", "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.CredentialIdColumnName}", "{ClientSessionEntity.PermissionsColumnName}", "{ClientSessionEntity.SecretDigestColumnName}", "{ClientSessionEntity.ExpiresAtColumnName}")
        VALUES (@identifier, @userId, @credentialId, @permissions, @secretDigest, @expiresAt);
        """;

    /// <summary>Reports what the deployment holds under one identifier.</summary>
    /// <remarks>The one statement on the request path: an index lookup by the key, on a request that is about to read mail out of the same database. Nothing in front of it, because a cache would make a revoked session go on working for its own window on every replica that had already read one.</remarks>
    private const string FindSessionStatement = $"""
        SELECT "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.CredentialIdColumnName}", "{ClientSessionEntity.PermissionsColumnName}", "{ClientSessionEntity.SecretDigestColumnName}", "{ClientSessionEntity.ExpiresAtColumnName}"
        FROM "{ClientSessionEntity.TableName}"
        WHERE "{ClientSessionEntity.IdentifierColumnName}" = @identifier;
        """;

    /// <summary>Reads which user and which credential the presented session names, which is what the two locks below it need.</summary>
    /// <remarks>
    /// A renewal's work begins at a session row rather than at a user, so this is how it learns what to lock and in
    /// which order. It carries the digest and the expiry so a renewal that is going to be refused is refused before it
    /// takes a lock an operator's act could wait on, and it settles nothing: the removal below carries the digest
    /// again, in its own condition, which is what actually guards the row.
    /// </remarks>
    private const string ReadRenewableSessionStatement = $"""
        SELECT "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.CredentialIdColumnName}"
        FROM "{ClientSessionEntity.TableName}"
        WHERE "{ClientSessionEntity.IdentifierColumnName}" = @identifier
          AND "{ClientSessionEntity.SecretDigestColumnName}" = @secretDigest
          AND "{ClientSessionEntity.ExpiresAtColumnName}" >= @renewableFrom;
        """;

    /// <summary>Removes the presented session and hands back what it held, in one statement.</summary>
    /// <remarks>
    /// Removing and returning together is what leaves exactly one live session where two requests present one token,
    /// whichever replica each of them reaches: one is left with a row and one with nothing, settled by PostgreSQL
    /// rather than by a check either of them makes between two statements. The digest is in the condition rather than
    /// compared afterwards, because a session survives being presented — a removal keyed on the identifier alone would
    /// let anybody writing the half of a token that is not a secret end somebody else's session. Comparing a digest
    /// here rather than in fixed time in the process is what this one case allows: a partial match on the digest of a
    /// thirty-two-byte random secret is no step toward the secret.
    /// </remarks>
    private const string RemoveSessionStatement = $"""
        DELETE FROM "{ClientSessionEntity.TableName}"
        WHERE "{ClientSessionEntity.IdentifierColumnName}" = @identifier
          AND "{ClientSessionEntity.SecretDigestColumnName}" = @secretDigest
        RETURNING "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.CredentialIdColumnName}", "{ClientSessionEntity.PermissionsColumnName}";
        """;

    /// <summary>Removes what can no longer authenticate anything, reaching it through the index on the expiry.</summary>
    /// <remarks>
    /// The comparison is against the indexed column alone, so the plan is a range over what has already expired rather
    /// than a scan of every session the deployment holds. It is strict rather than inclusive, because a session
    /// expiring at this instant still authenticates: the removal and the verification would otherwise disagree about
    /// the last instant of a lifetime, and the direction that disagreement fails in is a client signed out a moment
    /// early.
    /// </remarks>
    private const string RemoveExpiredStatement = $"""
        DELETE FROM "{ClientSessionEntity.TableName}"
        WHERE "{ClientSessionEntity.ExpiresAtColumnName}" < @removableFrom;
        """;

    /// <inheritdoc />
    public async Task<ClientSessionMintOutcome> TryMintAsync(
        MintedClientSessionRow row,
        ClientSessionGrant grant,
        int mostLiveSessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(grant);

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            if (!await StillAdmitsAsync(connection, transaction, grant.User, grant.CredentialId, cancellationToken))
            {
                return ClientSessionMintOutcome.NoLongerAdmitted;
            }

            await using var mint = new NpgsqlCommand(MintSessionStatement, connection, transaction);
            AddSessionParameters(mint, row, grant);
            mint.Parameters.AddWithValue("mostLiveSessions", mostLiveSessions);

            var written = await mint.ExecuteNonQueryAsync(cancellationToken) == 1;

            await transaction.CommitAsync(cancellationToken);

            return written ? ClientSessionMintOutcome.Minted : ClientSessionMintOutcome.BoundReached;
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"A client session could not be held in {ClientSessionEntity.TableName}, so the sign-in was refused rather than answered with a session nothing holds.",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task<HeldClientSession?> FindAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        await using var command = dataSource.CreateCommand(FindSessionStatement);
        command.Parameters.AddWithValue("identifier", identifier);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new HeldClientSession(
                GrantRead(reader, userIdOrdinal: 0, credentialIdOrdinal: 1, permissionsOrdinal: 2),
                reader.GetFieldValue<byte[]>(3),
                reader.GetFieldValue<DateTimeOffset>(4));
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"The session a request presented could not be read from {ClientSessionEntity.TableName}, so the request was refused as unavailable rather than as unauthenticated.",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task<ClientSessionGrant?> RenewAsync(
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        MintedClientSessionRow replacement,
        DateTimeOffset renewableFrom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(replacement);

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            if (await ReadRenewableAsync(connection, transaction, identifier, secretDigest, renewableFrom, cancellationToken)
                is not { } named)
            {
                return null;
            }

            if (!await StillAdmitsAsync(connection, transaction, named.User, named.CredentialId, cancellationToken))
            {
                return null;
            }

            if (await RemoveAsync(connection, transaction, identifier, secretDigest, cancellationToken)
                is not { } removed)
            {
                return null;
            }

            await using var insert = new NpgsqlCommand(RenewSessionStatement, connection, transaction);
            AddSessionParameters(insert, replacement, removed);

            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return removed;
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"A client session could not be renewed against {ClientSessionEntity.TableName}, so the renewal was refused rather than answered with a session nothing holds.",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        await using var command = dataSource.CreateCommand(RemoveSessionStatement);
        command.Parameters.AddWithValue("identifier", identifier);
        AddDigestParameter(command, secretDigest);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken);
        }
        catch (NpgsqlException failure)
        {
            throw Unavailable(
                $"A client session could not be ended in {ClientSessionEntity.TableName}, so the sign-out left it standing until its expiry.",
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
                $"The client sessions past the point they could still authenticate anything could not be removed from {ClientSessionEntity.TableName}.",
                failure);
        }
    }

    /// <summary>Takes the user row and then the credential row, and reports whether both still admit a session.</summary>
    /// <remarks>The order is the requirement rather than the two locks themselves: it is the order an erasure takes by construction, and taking it here is what makes a contending pair wait rather than leaves PostgreSQL to abort one of them.</remarks>
    private static async Task<bool> StillAdmitsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MailUserId user,
        Guid? credentialId,
        CancellationToken cancellationToken)
    {
        await using var locksUser = new NpgsqlCommand(LockUserStatement, connection, transaction);
        locksUser.Parameters.AddWithValue("userId", user.Value);

        if (await locksUser.ExecuteScalarAsync(cancellationToken) is null)
        {
            return false;
        }

        if (credentialId is not { } named)
        {
            return true;
        }

        await using var locksCredential = new NpgsqlCommand(LockEnabledCredentialStatement, connection, transaction);
        locksCredential.Parameters.AddWithValue("credentialId", named);

        return await locksCredential.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>Reads which user and which credential a renewable session names, or reports that it is not renewable.</summary>
    private static async Task<(MailUserId User, Guid? CredentialId)?> ReadRenewableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        DateTimeOffset renewableFrom,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadRenewableSessionStatement, connection, transaction);
        command.Parameters.AddWithValue("identifier", identifier);
        AddDigestParameter(command, secretDigest);
        command.Parameters.AddWithValue("renewableFrom", renewableFrom.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? (MailUserId.Create(reader.GetGuid(0)), reader.IsDBNull(1) ? null : reader.GetGuid(1))
            : null;
    }

    /// <summary>Removes the presented session inside the caller's transaction and hands back the grant it held.</summary>
    private static async Task<ClientSessionGrant?> RemoveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(RemoveSessionStatement, connection, transaction);
        command.Parameters.AddWithValue("identifier", identifier);
        AddDigestParameter(command, secretDigest);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? GrantRead(reader, userIdOrdinal: 0, credentialIdOrdinal: 1, permissionsOrdinal: 2)
            : null;
    }

    /// <summary>Reads a row's three grant columns back into what the session admits.</summary>
    /// <remarks>A name this release does not publish is dropped rather than raising, and the published order is restored, for the reason a credential's stored grant is read that way: a row written by a release that published a permission this one withdrew must not stop a session working, and a grant is a set rather than the order two writers happened to use.</remarks>
    private static ClientSessionGrant GrantRead(
        NpgsqlDataReader reader,
        int userIdOrdinal,
        int credentialIdOrdinal,
        int permissionsOrdinal)
    {
        var granted = new HashSet<MailFathomPermission>();

        foreach (var stored in reader.GetFieldValue<string[]>(permissionsOrdinal))
        {
            if (MailFathomPermission.TryParse(stored, out var permission))
            {
                granted.Add(permission);
            }
        }

        return new ClientSessionGrant(
            MailUserId.Create(reader.GetGuid(userIdOrdinal)),
            reader.IsDBNull(credentialIdOrdinal) ? null : reader.GetGuid(credentialIdOrdinal),
            [.. MailFathomPermission.All.Where(granted.Contains)]);
    }

    /// <summary>Writes the six values a session row is composed of onto an insert.</summary>
    private static void AddSessionParameters(
        NpgsqlCommand command,
        MintedClientSessionRow row,
        ClientSessionGrant grant)
    {
        command.Parameters.AddWithValue("identifier", row.Identifier);
        command.Parameters.AddWithValue("userId", grant.User.Value);
        command.Parameters.Add(new NpgsqlParameter("credentialId", NpgsqlDbType.Uuid)
        {
            Value = grant.CredentialId is { } credentialId ? credentialId : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("permissions", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = grant.Permissions.Select(permission => permission.Name).ToArray(),
        });
        AddDigestParameter(command, row.SecretDigest);
        command.Parameters.AddWithValue("expiresAt", row.ExpiresAt.ToUniversalTime());
    }

    private static void AddDigestParameter(NpgsqlCommand command, ReadOnlyMemory<byte> secretDigest) =>
        command.Parameters.Add(new NpgsqlParameter("secretDigest", NpgsqlDbType.Bytea)
        {
            Value = secretDigest.ToArray(),
        });

    private static ClientSessionStoreUnavailableException Unavailable(string operatorSafeMessage, Exception failure) =>
        new(
            $"{operatorSafeMessage} Check that the database is reachable and that this build's migrations have been applied to it.",
            failure);
}
