// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Answers which users reach which mail accounts from the assignment relation itself.</summary>
/// <remarks>
/// A singleton over the pool rather than a scoped context, for the reason the served-account reader is one: what asks is
/// as often a signal raised outside any request as a work unit or a request, and each answer is one short statement over
/// an index that joins no transaction. The pool arrives behind a delegate because the signal channel is composed before
/// startup has composed the connection string the pool is built from.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedMailAccountAssignments(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout) : IMailAccountAssignments
{
    private const string SelectAccountsAssignedTo =
        """
        SELECT "MailAccountId" FROM mail_account_assignments WHERE "UserId" = @user
        ORDER BY "MailAccountId" LIMIT @limit;
        """;

    private const string SelectUsersAssignedTo =
        """
        SELECT "UserId" FROM mail_account_assignments WHERE "MailAccountId" = @account
        ORDER BY "UserId" LIMIT @limit;
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailAccountId>> ReadAccountsAssignedToAsync(
        UserId user,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectAccountsAssignedTo, connection);
        command.Parameters.AddWithValue("user", user.Value);
        command.Parameters.AddWithValue("limit", MailAccountRecord.MaximumAssignedPerUser);

        var accounts = await this.ReadIdentifiersAsync(command, cancellationToken);

        return
        [
            .. accounts
                .Select(static account => account.ToString("D"))
                .Order(StringComparer.Ordinal)
                .Select(MailAccountId.Create),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// An account identifier that is not one this deployment generates names no row, so it is answered with nobody
    /// rather than refused: the relation holds nothing for it, which is the same answer as an account assigned to
    /// nobody.
    /// </remarks>
    public async Task<IReadOnlyList<UserId>> ReadUsersAssignedToAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return [];
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectUsersAssignedTo, connection);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("limit", MailAccountRecord.MaximumUsersAssigned);

        var users = await this.ReadIdentifiersAsync(command, cancellationToken);

        return [.. users.Order().Select(UserId.Create)];
    }

    private async Task<List<Guid>> ReadIdentifiersAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var identifiers = new List<Guid>();

        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(reader.GetGuid(0));
        }

        return identifiers;
    }
}
