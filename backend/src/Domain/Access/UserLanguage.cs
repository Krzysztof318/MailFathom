// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>The language one person reads, which is what this deployment writes for them in.</summary>
/// <remarks>
/// <para>
/// It answers for what is composed <em>for somebody</em> rather than for what is derived <em>from a mailbox</em> — the
/// card shown beside a contact of their own, the first version of a message that answers no correspondence. Both are
/// made on the asking, for one person, and are read by nobody else, so there is no shared copy for a mailbox's value
/// to settle and the person is the only thing anything is known about.
/// </para>
/// <para>
/// <see cref="Accounts.MailAccountLanguage" /> is the other half and is not this one. Everything derived from a
/// mailbox — the reading on a message row, the statement about a conversation — is one copy however many people are
/// assigned that mailbox, so its language is the account's and stays there. The two enumerations name the same two
/// languages today and are still different questions: one says what a mailbox is read in, this one says what a person
/// is written for.
/// </para>
/// <para>
/// A plain enumeration rather than a closed one, because a member is nothing but a name this process reads: nothing
/// carries a second identity, no row stores an ordinal, and the one place the value is written by hand is a user's own
/// record, which states the member name itself. That name is also the language's own name in English, which is what an
/// instruction composed for a derivation states, so there is no table beside this one to keep in step.
/// </para>
/// <para>
/// The set is closed at two because each member is a language this deployment can be held to: a prompt asking for a
/// third would produce text nothing here has ever read back. Adding one is a decision about what the product speaks
/// rather than a value to append.
/// </para>
/// <para>
/// English is declared first so that the reachable <see langword="default" /> is the same answer every unresolved read
/// already gives — a user this deployment no longer serves, a record held from before the language was asked for. A
/// value nobody stated therefore falls the way the client's own unset language falls rather than silently into the
/// other one.
/// </para>
/// </remarks>
public enum UserLanguage
{
    /// <summary>English.</summary>
    English = 0,

    /// <summary>Polish.</summary>
    Polish = 1,
}
