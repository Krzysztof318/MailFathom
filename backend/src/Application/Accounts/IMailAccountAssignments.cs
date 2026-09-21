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
/// and the two directions have different callers.
/// </para>
/// <para>
/// Two read the user-to-accounts direction, and no third joins them without a reason of the same kind.
/// <see cref="AssignedMailAccountCatalog" /> is what every resolution over stored mail narrows through, so a use case
/// reading mail never asks the relation itself. <see cref="Contacts.ContactBookScopes" /> is the second, because the
/// contact books a user reads are their own beside the collected book of each account assigned to them, which is a set
/// of books rather than a set of mailboxes and is therefore not a question the catalog answers.
/// </para>
/// <para>
/// The account-to-users direction is read by the work that has a mailbox in hand and needs the people it serves: the
/// per-user ceilings fan out over it — <see cref="Emails.Embeddings.Limits.EmbeddingSpendGate" /> and
/// <see cref="Emails.AttachmentText.Limits.AttachmentDerivationSpendGate" /> — and a signal raised about an account is
/// fanned out to them.
/// </para>
/// <para>
/// It answers about the account or the user it is asked about rather than about the caller, which is why it is not
/// itself a caller-scoped port. A read model reaching the user direction of it is therefore free to ask about somebody
/// else, and something above it has to bind the question to the user the work in hand is acting for: the catalog for
/// mail, and <see cref="Contacts.ContactBookOwnership" /> for the books, which resolves the user from the principal
/// and refuses a caller acting for nobody before a scope is composed at all.
/// </para>
/// </remarks>
public interface IMailAccountAssignments
{
    /// <summary>Gets the accounts one user is assigned, or empty when they are assigned none.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns>The identifiers of the accounts assigned to that user.</returns>
    /// <remarks>
    /// Empty is a real answer rather than an absent one: a user provisioned before an account was assigned to them,
    /// and one left after their last was unassigned, are both served nothing rather than served everything.
    /// <see cref="Emails.Mailboxes.MailboxScopeResolver" /> turns that into
    /// <see cref="Emails.Mailboxes.MailboxScope.NothingReadable" /> rather than into a scope carrying an empty account
    /// list, so nothing downstream has to decide what an empty list means.
    /// </remarks>
    IReadOnlyList<MailAccountId> AccountsAssignedTo(UserId user);

    /// <summary>Gets the users one account is assigned to, or empty when it is assigned to nobody.</summary>
    /// <param name="account">The account asked about.</param>
    /// <returns>The users assigned that account.</returns>
    /// <remarks>
    /// The relation read the other way round, which is what a fan-out needs: a change to one mailbox reaches every
    /// person served by it, and an account assigned to nobody reaches nobody rather than everybody. It answers about
    /// an account rather than about the caller, so nothing narrows a caller's own reach by it — what uses it is work
    /// holding a mailbox and owing something per person: a signal a synchronization run raises, the per-user spend
    /// ceilings, and the language a shared mailbox's derived text is composed in.
    /// </remarks>
    IReadOnlyList<UserId> UsersAssignedTo(MailAccountId account);
}
