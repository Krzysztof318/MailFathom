// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Places a user or a mail account from the records themselves, per question.</summary>
/// <remarks>
/// The account query reads at most two assignments, because what a scope asks of them is only whether exactly one user
/// holds the account: a third row would change no answer and the relation's own bound already caps the rest.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedAdministrativeTargets(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout) : IAdministrativeTargets
{
    private const string SelectMailAccountPlacement =
        """
        SELECT account."OrganizationId", assignment."UserId"
        FROM settings_mail_accounts AS account
        LEFT JOIN mail_account_assignments AS assignment ON assignment."MailAccountId" = account."Id"
        WHERE account."Id" = @account
        ORDER BY assignment."UserId"
        LIMIT 2;
        """;

    private const string SelectUserPlacement =
        """
        SELECT "OrganizationId" FROM settings_accounts WHERE "Id" = @user;
        """;

    public async Task<AdministrativeTarget> PlaceMailAccountAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return AdministrativeTarget.Unplaced;
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectMailAccountPlacement, connection);
        command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
        command.Parameters.AddWithValue("account", accountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Guid? organization = null;
        var assignedUsers = new List<UserId>();

        while (await reader.ReadAsync(cancellationToken))
        {
            organization = reader.IsDBNull(0) ? null : reader.GetGuid(0);

            if (!reader.IsDBNull(1))
            {
                assignedUsers.Add(UserId.Create(reader.GetGuid(1)));
            }
        }

        return AdministrativeTarget.MailAccount(organization, assignedUsers);
    }

    public async Task<AdministrativeTarget> PlaceUserAsync(UserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            return AdministrativeTarget.Unplaced;
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectUserPlacement, connection);
        command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
        command.Parameters.AddWithValue("user", user.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return AdministrativeTarget.Unplaced;
        }

        return AdministrativeTarget.User(user, reader.IsDBNull(0) ? null : reader.GetGuid(0));
    }
}
