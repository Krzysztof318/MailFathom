// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>Reads what the judgements of a write over mail accounts say the write has to do: refuse, or rewrite some accounts' settings columns.</summary>
/// <remarks>
/// Apart from the stores that run the statements, for the reason <see cref="GovernedUserJudgements" /> is: which
/// accounts stand in the way of a write and which rows it moves are computed from rows already read, so a unit test
/// reaches the decision and only issuing the statements needs a database.
/// </remarks>
internal static class GovernedMailAccountJudgements
{
    /// <summary>Composes what the judge is asked about one row.</summary>
    /// <param name="row">The account's row, whose settings were within the bound and are therefore present.</param>
    /// <param name="standing">The policies governing the account as the write found it.</param>
    /// <param name="candidate">The policies that would govern it once the write committed.</param>
    /// <returns>The account as the judge reads it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the row holds no settings, which is a row the caller was to leave out.</exception>
    internal static GovernedMailAccount GovernedOf(
        StoredGovernedMailAccountRow row,
        GoverningSettingsPolicies standing,
        GoverningSettingsPolicies candidate)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new GovernedMailAccount(
            new MailAccountRecord(
                row.Id,
                row.EmailAddress,
                row.DisplayName,
                row.Document ?? throw new ArgumentException("A row whose settings were not read is not judged.", nameof(row)),
                row.Version),
            [.. row.Users.Select(UserId.Create)],
            standing,
            candidate);
    }

    /// <summary>Finds the accounts a write may not reach.</summary>
    /// <param name="accounts">The governed accounts' rows.</param>
    /// <param name="judgements">One judgement per row, in the same order.</param>
    /// <returns>Every account in the way, in the order given, each with what refuses it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two lists differ in length, which is a judge that did not answer for every account.</exception>
    internal static IReadOnlyList<SettingsPolicyRecordInTheWay> FindAccountsInTheWay(
        IReadOnlyList<StoredGovernedMailAccountRow> accounts,
        IReadOnlyList<MailAccountPolicyJudgement> judgements) =>
        [
            .. Paired(accounts, judgements)
                .Where(account => account.Judgement.Refusals.Count > 0)
                .Select(account => new SettingsPolicyRecordInTheWay(account.Row.Id, account.Row.DisplayName, account.Judgement.Refusals)
                {
                    Kind = SettingsPolicyRecordKind.MailAccount,
                }),
        ];

    /// <summary>Finds the accounts whose settings columns a write moves.</summary>
    /// <param name="accounts">The governed accounts' rows.</param>
    /// <param name="judgements">One judgement per row, in the same order.</param>
    /// <returns>Each account to rewrite, with the version its row stands at and the settings it is to hold; an account whose judgement answers no settings is not among them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two lists differ in length.</exception>
    internal static IReadOnlyList<(Guid Id, long Version, MailAccountQueryableSettings Settings)> FindMovedSettings(
        IReadOnlyList<StoredGovernedMailAccountRow> accounts,
        IReadOnlyList<MailAccountPolicyJudgement> judgements) =>
        [
            .. Paired(accounts, judgements)
                .Where(account => account.Judgement.Settings is not null)
                .Select(account => (account.Row.Id, account.Row.Version, account.Judgement.Settings!)),
        ];

    private static (StoredGovernedMailAccountRow Row, MailAccountPolicyJudgement Judgement)[] Paired(
        IReadOnlyList<StoredGovernedMailAccountRow> accounts,
        IReadOnlyList<MailAccountPolicyJudgement> judgements)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(judgements);

        return accounts.Count == judgements.Count
            ? [.. accounts.Zip(judgements)]
            : throw new ArgumentException(
                $"{judgements.Count} judgements answered for {accounts.Count} governed mail accounts, and a write is judged against every one of them.",
                nameof(judgements));
    }
}
