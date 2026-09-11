// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Answers which language this deployment writes for one user in.</summary>
/// <remarks>
/// <para>
/// It is a port because the answer comes out of that user's record, which is the host's, while every pass that
/// composes text for somebody lives above it. Resolution is synchronous and reaches no database: the answer follows
/// the roster the startup gate published and each user-document commit republishes, so a derivation never puts a read
/// in front of the call it is about to make.
/// </para>
/// <para>
/// A user this deployment does not serve is answered with <see cref="MailUserLanguage.English" /> rather than with
/// nothing, exactly as the client opens in English when it can read no preference. It is the same answer a record
/// held from before the language was asked for reads as, so one value covers both: a pass composing text for somebody
/// always has a language, and never one it had to decide for itself.
/// </para>
/// </remarks>
public interface IMailUserLanguages
{
    /// <summary>Finds the language this deployment writes for one user in.</summary>
    /// <param name="user">The user whose derivation is about to be composed.</param>
    /// <returns>That user's language, or English where this deployment serves no such user.</returns>
    MailUserLanguage ForUser(MailUserId user);
}
