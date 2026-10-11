// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Policies;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Commits one user's record into the single <c>settings_accounts</c> row that holds it.</summary>
/// <remarks>
/// <para>
/// One short transaction is the whole of the write. It takes the lock a policy write excludes record writes with,
/// locks the user's row where it still stands at the version the change was composed over, reads the policies that
/// govern the user, hands them to the caller to compose the record under, and replaces the row. The user and the
/// version are what make two writers safe — the loser finds no row at that version, commits nothing, and is told so
/// by an absent result rather than by an exception — and the lock is what makes the policies the caller composed
/// under the ones in force when the row is replaced.
/// </para>
/// <para>
/// It is bare commands over the data source rather than an EF Core write, beside the reader and for the reason the
/// reader gives: the row is a document rather than a graph and nothing about it is tracked. The user is a parameter
/// and no identifier is composed from anything a caller supplied.
/// </para>
/// <para>
/// What is under the integration marker is the statements and nothing else. The rules a candidate is refused by need
/// no server to decide, so they sit in <see cref="UserSettingsCommitRules" /> where the unit suite's measurement
/// reaches them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this writer.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedUserSettingsDocumentWriter(
    NpgsqlDataSource dataSource,
    DatabaseCommandTimeout commandTimeout,
    TimeProvider timeProvider)
    : IUserSettingsDocumentWriter
{
    /// <summary>Locks the user's row where it still stands at the expected version, and reads the organization whose policy governs it.</summary>
    /// <remarks>
    /// The lock a move of the user between organizations takes, so the organization read here is the one the user
    /// belongs to when the row is replaced. A row that has moved on, or is gone, answers nothing.
    /// </remarks>
    private const string LockRecord =
        """
        SELECT "OrganizationId"
        FROM settings_accounts
        WHERE "Id" = @user AND "Version" = @expectedVersion
        FOR UPDATE;
        """;

    /// <summary>Replaces the record the transaction holds, with the switches it is served on.</summary>
    /// <remarks>
    /// The cast is what turns the parameter into the column's own type, so the server parses the document and refuses
    /// text that is not JSON at all rather than storing something nothing can read back. The endpoint switches move in
    /// the same statement, so no request can find the row serving a user on an endpoint their committed record, read
    /// through the policies governing it, keeps them off.
    /// </remarks>
    private const string CommitRecord =
        """
        UPDATE settings_accounts
        SET "Document" = @document::jsonb,
            "Version" = "Version" + 1,
            "UpdatedAt" = @updatedAt,
            "McpEndpointEnabled" = @mcpEndpoint,
            "ClientEndpointEnabled" = @clientEndpoint
        WHERE "Id" = @user AND "Version" = @expectedVersion
        RETURNING "Version";
        """;

    /// <inheritdoc />
    public async Task<long?> CommitAsync(
        UserId user,
        long expectedVersion,
        Func<GoverningSettingsPolicies, UserRecordCandidate?> compose,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user record is written for a named user.", nameof(user));
        }

        ArgumentNullException.ThrowIfNull(compose);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        // Opened in a step of its own rather than left to the command, because the driver reports a connect timeout
        // and a command timeout as the same shape and the two are opposite answers: one is a database that could not
        // be reached and a row that certainly stood still, the other a statement the server accepted and never
        // answered. Only this side knows which stage failed, so each stage reports what its own failure can mean.
        NpgsqlConnection connection;

        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);
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

                // Before the row, so a write waiting behind a policy write holds nothing that write needs.
                await using (var hold = new NpgsqlCommand(GoverningSettingsPolicyStatements.HoldBesideRecordWrites, connection, write))
                {
                    hold.CommandTimeout = this.CommandTimeoutSeconds;

                    await hold.ExecuteNonQueryAsync(cancellationToken);
                }

                if (await this.LockAsync(connection, write, user, expectedVersion, cancellationToken) is not { } held)
                {
                    return null;
                }

                var policies = await GoverningSettingsPolicyStatements.ReadAsync(
                    connection,
                    write,
                    held.OrganizationId,
                    this.CommandTimeoutSeconds,
                    cancellationToken);

                if (compose(policies) is not { } candidate)
                {
                    return null;
                }

                UserSettingsCommitRules.RefuseWhatCannotBeCommitted(candidate.Json, expectedVersion);

                await using var command = new NpgsqlCommand(CommitRecord, connection, write);

                // Written onto the command rather than inherited from the pool, because nothing puts the configured
                // bound onto this data source: the setting reaches EF Core's commands through the enrichment that
                // configures its context, and these statements are not among those.
                command.CommandTimeout = this.CommandTimeoutSeconds;

                command.Parameters.AddWithValue("document", candidate.Json);
                command.Parameters.AddWithValue("updatedAt", timeProvider.GetUtcNow());
                command.Parameters.AddWithValue("mcpEndpoint", candidate.EndpointAccess.McpEndpoint);
                command.Parameters.AddWithValue("clientEndpoint", candidate.EndpointAccess.ClientEndpoint);
                command.Parameters.AddWithValue("user", user.Value);
                command.Parameters.AddWithValue("expectedVersion", expectedVersion);

                var committed = await command.ExecuteScalarAsync(cancellationToken) as long?;

                await write.CommitAsync(cancellationToken);

                return committed;
            }
            catch (NpgsqlException exception)
            {
                throw new UserSettingsUnwritableException(UserSettingsWriteFailures.Diagnose(exception), exception);
            }
        }
    }

    private int CommandTimeoutSeconds => (int)commandTimeout.Value.TotalSeconds;

    /// <summary>Locks the user's row at the expected version.</summary>
    /// <returns>The organization the row names, or <see langword="null" /> where no row stands at that version.</returns>
    private async Task<HeldRecord?> LockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction write,
        UserId user,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(LockRecord, connection, write);

        command.CommandTimeout = this.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("user", user.Value);
        command.Parameters.AddWithValue("expectedVersion", expectedVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new HeldRecord(await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetGuid(0));
    }

    /// <summary>The row a write holds, by the one thing about it the write reads.</summary>
    private readonly record struct HeldRecord(Guid? OrganizationId);
}
