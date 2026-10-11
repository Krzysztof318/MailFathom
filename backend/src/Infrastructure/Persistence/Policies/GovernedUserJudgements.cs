// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Reads what the judgements of a policy write say the write has to do: refuse, or rewrite some rows' endpoint switches.</summary>
/// <remarks>
/// Apart from the store that runs the statements so that the decision is one a unit test reaches: which records stand
/// in the way of a policy and which rows a policy moves are computed from rows already read, and only issuing the
/// statements needs a database.
/// </remarks>
internal static class GovernedUserJudgements
{
    /// <summary>Finds the refusal of a policy write that would leave a governed record unservable.</summary>
    /// <param name="users">The governed users' rows.</param>
    /// <param name="judgements">One judgement per row, in the same order.</param>
    /// <returns>The refusal naming the first records in the way and how many there are in all, or <see langword="null" /> where no record refuses the write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two lists differ in length, which is a judge that did not answer for every record.</exception>
    internal static SettingsPolicyCommit? FindRecordsInTheWay(
        IReadOnlyList<StoredGovernedUserRow> users,
        IReadOnlyList<UserRecordPolicyJudgement> judgements) =>
        SettingsPolicyCommit.RefusedOver(users.Count, FindUsersInTheWay(users, judgements));

    /// <summary>Finds the users whose record a policy write would leave unservable.</summary>
    /// <param name="users">The governed users' rows.</param>
    /// <param name="judgements">One judgement per row, in the same order.</param>
    /// <returns>Every user in the way, in the order given, each with what refuses it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two lists differ in length, which is a judge that did not answer for every record.</exception>
    internal static IReadOnlyList<SettingsPolicyRecordInTheWay> FindUsersInTheWay(
        IReadOnlyList<StoredGovernedUserRow> users,
        IReadOnlyList<UserRecordPolicyJudgement> judgements) =>
        [
            .. Paired(users, judgements)
                .Where(user => user.Judgement.Refusals.Count > 0)
                .Select(user => new SettingsPolicyRecordInTheWay(user.Row.Id, user.Row.DisplayName, user.Judgement.Refusals)),
        ];

    /// <summary>Finds the rows whose endpoint switches a policy write moves, grouped by the switches each is to carry.</summary>
    /// <param name="users">The governed users' rows.</param>
    /// <param name="judgements">One judgement per row, in the same order.</param>
    /// <returns>The identifiers of the rows to rewrite under each pair of switches; a row already carrying what its judgement answers is in none, and so is a row whose judgement answers no switches, which is a record that could not be resolved under policies none of which forces one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two lists differ in length.</exception>
    internal static IReadOnlyDictionary<UserEndpointAccess, Guid[]> FindMovedSwitches(
        IReadOnlyList<StoredGovernedUserRow> users,
        IReadOnlyList<UserRecordPolicyJudgement> judgements) =>
        Paired(users, judgements)
            .Where(user => user.Judgement.EndpointAccess is { } access
                && (access.McpEndpoint != user.Row.McpEndpointEnabled || access.ClientEndpoint != user.Row.ClientEndpointEnabled))
            .GroupBy(user => user.Judgement.EndpointAccess!.Value, user => user.Row.Id)
            .ToDictionary(moved => moved.Key, moved => moved.ToArray());

    private static (StoredGovernedUserRow Row, UserRecordPolicyJudgement Judgement)[] Paired(
        IReadOnlyList<StoredGovernedUserRow> users,
        IReadOnlyList<UserRecordPolicyJudgement> judgements)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(judgements);

        return users.Count == judgements.Count
            ? [.. users.Zip(judgements)]
            : throw new ArgumentException(
                $"{judgements.Count} judgements answered for {users.Count} governed records, and a policy write is judged against every one of them.",
                nameof(judgements));
    }
}
