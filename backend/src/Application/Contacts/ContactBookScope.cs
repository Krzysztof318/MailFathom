// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Contacts;

/// <summary>The books one user reads: their own, and the collected book of every mail account they are assigned.</summary>
/// <remarks>
/// <para>
/// A read of "the contact book" is a read of several, because what a person knows about their correspondents is partly
/// what they wrote down and partly what their mailboxes picked up. This states which books those are and, just as
/// importantly, in what order — because two books may hold one address and only one of the two records is shown.
/// </para>
/// <para>
/// <b>The order is the user's own book first, then the accounts by the ordinal order of their identifiers.</b> A
/// record the user wrote therefore wins over anything a mailbox collected for the same address, which is the point of
/// writing one down: their name and their note are what they see. An address two of their mailboxes both collected is
/// answered from the first of the two in that order, so the answer is the same on every read rather than whichever row
/// the database returned first — and it is the same for two users of one mailbox, because the order is the accounts'
/// own rather than the order either user's record happened to declare them in.
/// </para>
/// <para>
/// The order is carried as <see cref="Keys" /> rather than recomputed per read, because the store answers the
/// precedence question with it: a record is hidden when one of its addresses is held in a book that comes before its
/// own.
/// </para>
/// </remarks>
public sealed record ContactBookScope
{
    private ContactBookScope(ContactBookHolder ownBook, IReadOnlyList<ContactBookHolder> books)
    {
        this.OwnBook = ownBook;
        this.Books = books;
        this.Keys = [.. books.Select(book => book.Key)];
    }

    /// <summary>Gets the user whose reading this scope describes.</summary>
    public MailUserId User => this.OwnBook.User!.Value;

    /// <summary>Gets the user's own book, which every write a caller makes goes into.</summary>
    public ContactBookHolder OwnBook { get; }

    /// <summary>Gets every book this scope reads, the user's own first and the accounts' after it in precedence order.</summary>
    public IReadOnlyList<ContactBookHolder> Books { get; }

    /// <summary>Gets the keys of <see cref="Books" />, in the same order, which is how the store applies the precedence.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Describes a user reading their own book and the collected books of the accounts they are assigned.</summary>
    /// <param name="user">The user.</param>
    /// <param name="assignedAccounts">The accounts assigned to them, in any order.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignedAccounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An account named twice contributes one book, because a book is the account's rather than the assignment's, and
    /// the accounts are ordered here rather than taken as given so that the precedence between two of them does not
    /// depend on which reader assembled the list. A user assigned nothing reads their own book alone, which is the same
    /// shape rather than a case of its own.
    /// </remarks>
    public static ContactBookScope Of(MailUserId user, IReadOnlyList<MailAccountId> assignedAccounts)
    {
        ArgumentNullException.ThrowIfNull(assignedAccounts);

        var ownBook = ContactBookHolder.Of(user);

        return new ContactBookScope(
            ownBook,
            [
                ownBook,
                .. assignedAccounts
                    .Select(ContactBookHolder.Of)
                    .DistinctBy(book => book.Key, StringComparer.Ordinal)
                    .OrderBy(book => book.Key, StringComparer.Ordinal),
            ]);
    }

    /// <summary>Describes a read of one user's own book alone, reaching nothing any mailbox collected.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The scope.</returns>
    /// <remarks>What an amendment resolves against: a caller amends the people they wrote down, and a collected record is promoted rather than edited in place.</remarks>
    public static ContactBookScope OfOwnBookAlone(MailUserId user) => Of(user, []);
}
