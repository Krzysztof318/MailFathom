// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>One user this deployment serves, composed from the record that is the whole of what it knows about them.</summary>
/// <param name="User">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this user apart by.</param>
/// <param name="MailAccounts">The mail accounts assigned to this user, each carrying its own settings.</param>
/// <param name="Language">The language this deployment writes for them in; a record held from before the property existed states none and reads as English.</param>
/// <remarks>
/// One source reaches a user, and it is their own record, so everything here is bound out of that document rather than
/// out of a section. What is about a mailbox rather than about the person is not here at all: how its mail is
/// classified and what it is scanned for are the account's, so they are read off the declaration rather than off this
/// record and a mailbox two people share is judged once.
/// </remarks>
internal sealed record ServedMailUser(
    MailUserId User,
    string DisplayName,
    IReadOnlyList<MailSynchronizationAccountOptions> MailAccounts,
    MailUserLanguage Language = MailUserLanguage.English);
