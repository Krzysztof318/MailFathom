// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>The language one mail account is read in, which is what this deployment writes about its mail in.</summary>
/// <remarks>
/// <para>
/// It says what a derivation this deployment produces <em>about</em> one mailbox comes out in — the reading on a
/// message row, the statement about a conversation — rather than what language the mail itself is in. A mailbox's
/// derived text is one copy however many people are assigned the mailbox, so the value belongs to the account: two
/// users sharing one mailbox read one reading, and it cannot be written twice over to suit each of them.
/// </para>
/// <para>
/// A plain enumeration rather than a closed one, because a member is nothing but a name this process reads: nothing
/// carries a second identity, no row stores an ordinal, and the one place the value is written by hand is an account's
/// own record, which states the member name itself. That name is also the language's own name in English, which is
/// what an instruction composed for a derivation states, so there is no table beside this one to keep in step.
/// </para>
/// <para>
/// The set is closed at two because each member is a language this deployment can be held to: a prompt asking for a
/// third would produce text nothing here has ever read back. Adding one is a decision about what the product speaks
/// rather than a value to append.
/// </para>
/// <para>
/// English is declared first so that the reachable <see langword="default" /> is the same answer every unresolved read
/// already gives — an account this deployment no longer serves, a record held from before the language was asked for.
/// A value nobody stated therefore falls the way the client's own unset language falls rather than silently into the
/// other one.
/// </para>
/// </remarks>
public enum MailAccountLanguage
{
    /// <summary>English.</summary>
    English = 0,

    /// <summary>Polish.</summary>
    Polish = 1,
}
