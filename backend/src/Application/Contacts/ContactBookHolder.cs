// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Names whose contact book one act reads or writes: a user's own, or a mail account's.</summary>
/// <remarks>
/// <para>
/// There are two kinds of book and the kind decides the origin of everything in it. A user holds the people they wrote
/// down, which is <see cref="ContactOrigin.Asserted" />; a mail account holds the people its mail says it corresponds
/// with, which is <see cref="ContactOrigin.Collected" />. So a holder is not merely a key a row is filed under — it is
/// the whole of what makes collection unable to write into a user's book and a user unable to amend what a mailbox
/// picked up.
/// </para>
/// <para>
/// A mailbox is one mailbox however many people are assigned it, which is what puts the collected book on the account
/// rather than on each of them: an address the account corresponds with is recorded once and every assigned user reads
/// that one record, instead of one copy per person appearing the day an administrator assigns a second.
/// </para>
/// <para>
/// <see cref="Key" /> is what the store files a row under, and it is text because the two identifiers are:
/// a user is a UUID and a mail account is the identifier its declaration carries, and the mail graph beside this one
/// already records that identifier as text. It leads with the kind, and that prefix is load-bearing rather than
/// decorative: an account identifier is text an operator wrote and nothing constrains its shape, so one written as the
/// canonical form of some user's UUID would otherwise make that user's own book and that mailbox's collected book one
/// key — and every read narrows on the key alone, so every other user assigned the account would read that user's own
/// contacts. The prefix is what makes the two namespaces unable to meet, and the check constraint on the table asserts
/// the same derivation so a row cannot be filed under a key its own holder columns do not produce.
/// </para>
/// </remarks>
public sealed record ContactBookHolder
{
    /// <summary>The prefix a user's own book's key carries, which is what keeps it out of the accounts' namespace.</summary>
    public const string UserKeyPrefix = "user:";

    /// <summary>The prefix a mail account's collected book's key carries, which is what keeps it out of the users'.</summary>
    public const string AccountKeyPrefix = "account:";

    private ContactBookHolder(ContactOrigin origin, string key, MailUserId? user, MailAccountId? account)
    {
        this.Origin = origin;
        this.Key = key;
        this.User = user;
        this.Account = account;
    }

    /// <summary>Gets the origin every contact in this book carries.</summary>
    public ContactOrigin Origin { get; }

    /// <summary>Gets the value a row of this book is filed under, which leads with the kind of holder it names.</summary>
    public string Key { get; }

    /// <summary>Gets the user whose own book this is, or <see langword="null" /> when it is a mail account's.</summary>
    public MailUserId? User { get; }

    /// <summary>Gets the mail account whose collected book this is, or <see langword="null" /> when it is a user's own.</summary>
    public MailAccountId? Account { get; }

    /// <summary>Names one user's own book, which holds the people they wrote down.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The holder.</returns>
    public static ContactBookHolder Of(MailUserId user) =>
        new(ContactOrigin.Asserted, UserKeyPrefix + user.Value.ToString("D"), user, account: null);

    /// <summary>Names one mail account's book, which holds the people its mail says it corresponds with.</summary>
    /// <param name="account">The mail account.</param>
    /// <returns>The holder.</returns>
    public static ContactBookHolder Of(MailAccountId account) =>
        new(ContactOrigin.Collected, AccountKeyPrefix + account.Value, user: null, account);
}
