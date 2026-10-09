// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Answers the assignment relation as this process serves it: a user an erasure is deciding about reaches no mailbox.</summary>
/// <remarks>
/// The relation is read from PostgreSQL, where a user being erased stays assigned until the deletion commits. Every
/// reader of it — the caller's catalog every resolution over stored mail narrows through, the contact book, the spend
/// gates, the signals — is answered through here, so withholding the user in <see cref="ServedUsers" /> is what takes
/// their mailboxes away from every request and every signal at once.
/// </remarks>
internal sealed class WithholdingMailAccountAssignments(IMailAccountAssignments assignments, ServedUsers servedUsers)
    : IMailAccountAssignments
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailAccountId>> ReadAccountsAssignedToAsync(
        UserId user,
        CancellationToken cancellationToken) =>
        servedUsers.IsWithheld(user)
            ? []
            : await assignments.ReadAccountsAssignedToAsync(user, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserId>> ReadUsersAssignedToAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
    [
        .. (await assignments.ReadUsersAssignedToAsync(account, cancellationToken))
            .Where(user => !servedUsers.IsWithheld(user)),
    ];
}
