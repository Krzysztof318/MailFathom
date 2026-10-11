// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Policies;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;

/// <summary>Finds and derives again, in <c>settings_accounts</c>, the endpoint switches whose stamp does not match their row.</summary>
/// <remarks>
/// Bare commands over the data source beside <see cref="PersistedUserSettingsDocumentWriter" />, for the reason it
/// gives and under the same lock order: the lock a policy write excludes record writes with is taken before the row,
/// so a derivation waiting behind a policy write holds nothing that write needs. The user is a parameter and no
/// identifier is composed from anything a caller supplied.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedUserEndpointSwitches(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout)
    : IUserEndpointSwitchStore
{
    /// <summary>What a row whose stamp does not match it looks like: its version or its organization moved past the stamp.</summary>
    /// <remarks>The organization is compared as a value that may be absent on either side, since a user in no organization is stamped as derived under none.</remarks>
    private const string StampTrails =
        """
        ("EndpointSwitchesVersion" <> "Version" OR "EndpointSwitchesOrganizationId" IS DISTINCT FROM "OrganizationId")
        """;

    /// <summary>Reads the first trailing rows past one identifier, and no document.</summary>
    private const string SelectTrailing =
        $"""
         SELECT "Id"
         FROM settings_accounts
         WHERE {StampTrails} AND (@after::uuid IS NULL OR "Id" > @after)
         ORDER BY "Id"
         LIMIT @limit;
         """;

    /// <summary>Locks the user's row where its stamp still trails, and reads what its switches are derived from.</summary>
    /// <remarks>
    /// The lock a record write and a move take, so the record and the organization read here are the ones the row
    /// holds when it is stamped. A replica that waited here behind another's derivation finds the stamp matching and
    /// reads nothing. The document is sent only within what this build binds a user's record from, as every other
    /// read of it is.
    /// </remarks>
    private const string LockTrailing =
        $"""
         SELECT
             "DisplayName",
             CASE WHEN octet_length("Document"::text) <= @maximumOctets THEN "Document"::text END,
             "McpEndpointEnabled",
             "ClientEndpointEnabled",
             "OrganizationId"
         FROM settings_accounts
         WHERE "Id" = @user AND {StampTrails}
         FOR UPDATE;
         """;

    /// <summary>Writes the derived switches onto the row the transaction holds, and stamps it with what it holds.</summary>
    private const string WriteDerived =
        """
        UPDATE settings_accounts
        SET "McpEndpointEnabled" = @mcpEndpoint,
            "ClientEndpointEnabled" = @clientEndpoint,
            "EndpointSwitchesVersion" = "Version",
            "EndpointSwitchesOrganizationId" = "OrganizationId"
        WHERE "Id" = @user;
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserId>> ReadTrailingAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        NpgsqlConnection connection;

        try
        {
            connection = await dataSource().OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new UserSettingsUnreadableException(
                UserSettingsReadFailures.DiagnoseWhileConnecting(exception),
                exception);
        }

        await using (connection)
        {
            try
            {
                await using var command = new NpgsqlCommand(SelectTrailing, connection);

                command.CommandTimeout = this.CommandTimeoutSeconds;
                command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Uuid) { Value = (object?)after ?? DBNull.Value });
                command.Parameters.AddWithValue("limit", limit);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                var trailing = new List<UserId>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    trailing.Add(UserId.Create(reader.GetGuid(0)));
                }

                return trailing;
            }
            catch (NpgsqlException exception)
            {
                throw new UserSettingsUnreadableException(
                    UserSettingsReadFailures.DiagnoseWhileReading(exception),
                    exception);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeriveAgainAsync(
        UserId user,
        Func<GovernedUserRecord, UserEndpointAccess?> derive,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("Endpoint switches are derived for a named user.", nameof(user));
        }

        ArgumentNullException.ThrowIfNull(derive);

        // Opened in a step of its own for the reason the record's writer gives: only this side knows which stage
        // failed, and a database that could not be reached is a row that certainly stood still.
        NpgsqlConnection connection;

        try
        {
            connection = await dataSource().OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new UserSettingsUnwritableException(
                UserSettingsWriteFailures.DiagnoseWhileConnecting(exception),
                exception);
        }

        await using (connection)
        {
            try
            {
                await using var write = await connection.BeginTransactionAsync(cancellationToken);

                await using (var hold = new NpgsqlCommand(GoverningSettingsPolicyStatements.HoldBesideRecordWrites, connection, write))
                {
                    hold.CommandTimeout = this.CommandTimeoutSeconds;

                    await hold.ExecuteNonQueryAsync(cancellationToken);
                }

                if (await this.LockAsync(connection, write, user, cancellationToken) is not { } held)
                {
                    return false;
                }

                var policies = await GoverningSettingsPolicyStatements.ReadAsync(
                    connection,
                    write,
                    held.OrganizationId,
                    this.CommandTimeoutSeconds,
                    cancellationToken);

                if (derive(new GovernedUserRecord(user, held.DisplayName, held.Json, held.Switches, policies, policies)) is not { } derived)
                {
                    return false;
                }

                await using var command = new NpgsqlCommand(WriteDerived, connection, write);

                command.CommandTimeout = this.CommandTimeoutSeconds;
                command.Parameters.AddWithValue("mcpEndpoint", derived.McpEndpoint);
                command.Parameters.AddWithValue("clientEndpoint", derived.ClientEndpoint);
                command.Parameters.AddWithValue("user", user.Value);

                await command.ExecuteNonQueryAsync(cancellationToken);
                await write.CommitAsync(cancellationToken);

                return true;
            }
            catch (NpgsqlException exception)
            {
                throw new UserSettingsUnwritableException(UserSettingsWriteFailures.Diagnose(exception), exception);
            }
        }
    }

    private int CommandTimeoutSeconds => (int)commandTimeout.Value.TotalSeconds;

    private async Task<HeldRow?> LockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction write,
        UserId user,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(LockTrailing, connection, write);

        command.CommandTimeout = this.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("user", user.Value);
        command.Parameters.AddWithValue("maximumOctets", UserSettingsDocument.MaximumOctets);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new HeldRow(
            reader.GetString(0),
            await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
            new UserEndpointAccess(reader.GetBoolean(2), reader.GetBoolean(3)),
            await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetGuid(4));
    }

    /// <summary>The row a derivation holds: who it is, what it states, what it serves, and where it stands.</summary>
    private readonly record struct HeldRow(string DisplayName, string? Json, UserEndpointAccess Switches, Guid? OrganizationId);
}
