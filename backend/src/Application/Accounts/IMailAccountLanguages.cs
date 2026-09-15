// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which language this deployment writes about one mailbox's mail in.</summary>
/// <remarks>
/// <para>
/// It is a port because the answer comes out of that account's record, which is the host's, while every pass that
/// derives text about a mailbox lives above it. Resolution is synchronous and reaches no database: the answer follows
/// the declarations the startup gate published and each account commit republishes, so a derivation never puts a read
/// in front of the call it is about to make.
/// </para>
/// <para>
/// The answer is the account's rather than a user's because the mail is one copy however many users are assigned the
/// mailbox, and so is everything derived from it. A reading written for one of two assigned users in their own
/// language would be the reading the other one gets as well, so the language is settled where the mail is — on the
/// account — and two people sharing a mailbox share what it says.
/// </para>
/// <para>
/// An account this deployment does not serve is answered with <see cref="MailAccountLanguage.English" /> rather than
/// with nothing, exactly as the client opens in English when it can read no preference. It is the same answer a record
/// held from before the language was asked for reads as, so one value covers both: a pass deriving text about a
/// mailbox always has a language, and never one it had to decide for itself.
/// </para>
/// </remarks>
public interface IMailAccountLanguages
{
    /// <summary>Finds the language this deployment writes about one mailbox's mail in.</summary>
    /// <param name="account">The mailbox whose derivation is about to be composed.</param>
    /// <returns>That account's language, or English where this deployment serves no such account.</returns>
    MailAccountLanguage LanguageOf(MailAccountId account);
}
