// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Spam;

/// <summary>Answers what one user decided about the classification of their own mail.</summary>
/// <remarks>
/// <para>
/// Read through a port for the reason every other decision about mail is: the paths that obey it must not reach for a
/// settings type of the host's, and the value has to be re-read rather than captured so that a configuration reload or
/// a user's own write takes effect without a restart. Reading it does not set anything off — a change decides what the
/// next classification runs with and never reclassifies what is already recorded.
/// </para>
/// <para>
/// Every answer is about one user, because junk is a judgement about that user's own mailbox and the actions it
/// triggers write to their own mail server. A user this deployment does not serve is answered exactly as one who
/// switched classification off, which is what keeps a mailbox scope that names somebody else's account from producing a
/// verdict about their mail.
/// </para>
/// </remarks>
public interface ISpamClassificationSettingsReader
{
    /// <summary>Gets which of the deployment's mailboxes classification runs for, as one value a walk can be narrowed by.</summary>
    /// <remarks>
    /// The set-based shape of the same decision <see cref="SettingsFor" /> answers per user, composed here so the two
    /// are one reading of the same postures. A walk over stored mail spans users and cannot ask about each of them in
    /// turn, so it narrows by the accounts and folders this names.
    /// </remarks>
    SpamClassificationScope ScopeInForce { get; }

    /// <summary>Gets the settings in force now for one user.</summary>
    /// <param name="user">The user whose mail the decision is about.</param>
    /// <returns>Their settings, or <see cref="SpamClassificationSettings.Disabled" /> where this deployment serves no such user.</returns>
    SpamClassificationSettings SettingsFor(MailUserId user);
}
