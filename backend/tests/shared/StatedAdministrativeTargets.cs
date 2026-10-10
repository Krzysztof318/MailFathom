// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Places each mail account and user a test states, and places anything else nowhere, which only the deployment covers.</summary>
internal sealed class StatedAdministrativeTargets : IAdministrativeTargets
{
    private readonly Dictionary<MailAccountId, AdministrativeTarget> accounts = [];
    private readonly Dictionary<UserId, AdministrativeTarget> users = [];

    /// <summary>States that one mail account sits in an organization, assigned to some users.</summary>
    /// <param name="account">The account.</param>
    /// <param name="organizationId">Its organization, or <see langword="null" /> for none.</param>
    /// <param name="assignedUsers">The users it is assigned to.</param>
    /// <returns>This, so a test states several in one expression.</returns>
    internal StatedAdministrativeTargets WithMailAccount(MailAccountId account, Guid? organizationId, params UserId[] assignedUsers)
    {
        this.accounts[account] = AdministrativeTarget.MailAccount(organizationId, assignedUsers);

        return this;
    }

    /// <summary>States that one user is a member of an organization.</summary>
    /// <param name="user">The user.</param>
    /// <param name="organizationId">Their organization, or <see langword="null" /> for none.</param>
    /// <returns>This, so a test states several in one expression.</returns>
    internal StatedAdministrativeTargets WithUser(UserId user, Guid? organizationId)
    {
        this.users[user] = AdministrativeTarget.User(user, organizationId);

        return this;
    }

    public Task<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>> PlaceMailAccountsAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>>(accounts.Distinct().ToDictionary(
            account => account,
            account => this.accounts.GetValueOrDefault(account) ?? AdministrativeTarget.Unplaced));

    public Task<AdministrativeTarget> PlaceUserAsync(UserId user, CancellationToken cancellationToken) =>
        Task.FromResult(this.users.GetValueOrDefault(user) ?? AdministrativeTarget.Unplaced);
}
