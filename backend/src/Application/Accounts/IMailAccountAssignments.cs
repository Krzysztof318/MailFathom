// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which mail accounts one user is assigned.</summary>
/// <remarks>
/// <para>
/// An account is a record of its own and is assigned to however many users an administrator assigned it to, so which
/// user reaches which mailbox is a relation rather than a column on either side. This is the one port that reads it,
/// and <see cref="AssignedMailAccountCatalog" /> is the one caller: every caller-facing resolution then narrows to the
/// accounts this answers, so the mapping is read once rather than by each use case.
/// </para>
/// <para>
/// It answers about the user it is asked about rather than about the caller, which is why it is not itself a
/// caller-scoped port and is never injected into a use case. A read model reaching this directly would be free to ask
/// about somebody else; the catalog above is what binds the question to the user the work in hand is acting for.
/// </para>
/// </remarks>
public interface IMailAccountAssignments
{
    /// <summary>Gets the accounts one user is assigned, or empty when they are assigned none.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns>The identifiers of the accounts assigned to that user.</returns>
    /// <remarks>
    /// Empty is a real answer rather than an absent one: a user provisioned before an account was assigned to them,
    /// and one left after their last was unassigned, are both served nothing rather than served everything. What turns
    /// that into a scope reading nothing is <see cref="Emails.Mailboxes.MailboxScopeResolver" />, because an empty
    /// account list is read as unrestricted by every narrowing site.
    /// </remarks>
    IReadOnlyList<MailAccountId> AccountsAssignedTo(MailUserId user);

    /// <summary>Gets the users one account is assigned to, or empty when it is assigned to nobody.</summary>
    /// <param name="account">The account asked about.</param>
    /// <returns>The users assigned that account.</returns>
    /// <remarks>
    /// The relation read the other way round, which is what a fan-out needs: a change to one mailbox reaches every
    /// person served by it, and an account assigned to nobody reaches nobody rather than everybody. It answers about
    /// an account rather than about the caller for the reason above, so nothing caller-facing narrows by it — what
    /// uses it is work acting for no user at all, such as a signal a synchronization run raises.
    /// </remarks>
    IReadOnlyList<MailUserId> UsersAssignedTo(MailAccountId account);
}
