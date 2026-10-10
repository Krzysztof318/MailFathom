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

/// <summary>Places a user or mail accounts from the records themselves, per question.</summary>
/// <remarks>
/// The account query places every account an operation names in one statement and reads at most two assignments per
/// account, because what a scope asks of them is only whether exactly one user holds the account: a third row would
/// change no answer. So a read across a user's collected books costs one round trip however many books there are, and
/// what it returns is bounded by twice the accounts asked about.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedAdministrativeTargets(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout) : IAdministrativeTargets
{
    private const string SelectMailAccountPlacements =
        """
        SELECT account."Id", account."OrganizationId", assignee."UserId"
        FROM settings_mail_accounts AS account
        LEFT JOIN LATERAL (
            SELECT assignment."UserId"
            FROM mail_account_assignments AS assignment
            WHERE assignment."MailAccountId" = account."Id"
            ORDER BY assignment."UserId"
            LIMIT 2
        ) AS assignee ON TRUE
        WHERE account."Id" = ANY(@accounts);
        """;

    private const string SelectUserPlacement =
        """
        SELECT "OrganizationId" FROM settings_accounts WHERE "Id" = @user;
        """;

    public async Task<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>> PlaceMailAccountsAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var placements = accounts.Distinct().ToDictionary(account => account, _ => AdministrativeTarget.Unplaced);
        var identities = placements.Keys
            .Select(account => (Account: account, Parsed: Guid.TryParse(account.Value, out var identity) ? identity : (Guid?)null))
            .Where(pair => pair.Parsed is not null)
            .ToLookup(pair => pair.Parsed!.Value, pair => pair.Account);

        if (identities.Count == 0)
        {
            return placements;
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectMailAccountPlacements, connection);
        command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;
        command.Parameters.AddWithValue("accounts", identities.Select(group => group.Key).ToArray());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var held = new Dictionary<Guid, (Guid? Organization, List<UserId> AssignedUsers)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var identity = reader.GetGuid(0);

            if (!held.TryGetValue(identity, out var account))
            {
                account = (reader.IsDBNull(1) ? null : reader.GetGuid(1), []);
                held.Add(identity, account);
            }

            if (!reader.IsDBNull(2))
            {
                account.AssignedUsers.Add(UserId.Create(reader.GetGuid(2)));
            }
        }

        foreach (var (identity, account) in held)
        {
            foreach (var asked in identities[identity])
            {
                placements[asked] = AdministrativeTarget.MailAccount(account.Organization, account.AssignedUsers);
            }
        }

        return placements;
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
