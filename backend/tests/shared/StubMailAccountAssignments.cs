// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Answers which users reach which mailboxes, from what a test assigned rather than from a database.</summary>
/// <remarks>
/// Hand-written rather than substituted, because the relation is read from both ends and a test that arranged one end
/// would be free to contradict itself on the other: one assignment here answers <see cref="AccountsAssignedTo" /> and
/// <see cref="UsersAssignedTo" /> alike, which is what makes a mailbox two people share one mailbox in a test as well.
/// </remarks>
internal sealed class StubMailAccountAssignments : IMailAccountAssignments
{
    private readonly List<(UserId User, MailAccountId Account)> assignments = [];

    /// <summary>Assigns one mailbox to one user.</summary>
    /// <param name="user">The user the mailbox is served to.</param>
    /// <param name="accounts">The mailboxes assigned to them.</param>
    /// <returns>These assignments, so arrangements read as one expression.</returns>
    public StubMailAccountAssignments Assigning(UserId user, params MailAccountId[] accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        this.assignments.AddRange(accounts.Select(account => (user, account)));

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<MailAccountId> AccountsAssignedTo(UserId user) =>
        [.. this.assignments.Where(assignment => assignment.User == user).Select(assignment => assignment.Account)];

    /// <inheritdoc />
    public IReadOnlyList<UserId> UsersAssignedTo(MailAccountId account) =>
        [.. this.assignments.Where(assignment => assignment.Account == account).Select(assignment => assignment.User)];
}
