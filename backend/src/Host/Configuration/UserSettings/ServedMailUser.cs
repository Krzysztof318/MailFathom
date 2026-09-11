// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>One user this deployment serves, composed from the record that is the whole of what it knows about them.</summary>
/// <param name="User">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this user apart by.</param>
/// <param name="MailAccounts">The user's mail accounts, as their record declares them.</param>
/// <param name="Language">The language this deployment writes for them in; a record held from before the property existed states none and reads as English.</param>
/// <param name="SpamClassification">How this user's own mail is classified, or nothing where their record states none.</param>
/// <param name="SensitiveContent">What this user asks to have their own mail scanned for, or nothing where they asked for nothing.</param>
/// <remarks>
/// One source reaches a user, and it is their own record, so everything here is bound out of that document rather than
/// out of a section. A block a record leaves unstated arrives as absence, and absence and a block that asks for nothing
/// compose to the same answer, so nothing downstream tells them apart.
/// </remarks>
internal sealed record ServedMailUser(
    MailUserId User,
    string DisplayName,
    IReadOnlyList<MailSynchronizationAccountOptions> MailAccounts,
    MailUserLanguage Language = MailUserLanguage.English,
    UserSpamClassificationOptions? SpamClassification = null,
    UserSensitiveContentOptions? SensitiveContent = null);
