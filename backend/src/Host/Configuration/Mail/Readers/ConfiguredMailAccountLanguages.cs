// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads each account's language from its own record.</summary>
/// <remarks>
/// <para>
/// One source and no layer over it, exactly as the classification posture beside it: the language a mailbox's
/// derivations come out in is the block that account's record carries and nothing else, so there is no deployment
/// section behind it to revert to.
/// </para>
/// <para>
/// An account this deployment does not serve, and one whose record was held from before the property existed, are one
/// answer rather than two — English, which is what <see cref="IMailAccountLanguages" /> states and why. It is read per
/// call rather than captured, so a language changed through the administrative surface reaches the next derivation
/// without a restart and without any pass holding a copy of its own.
/// </para>
/// </remarks>
internal sealed class ConfiguredMailAccountLanguages(MailSynchronizationOptions settings) : IMailAccountLanguages
{
    /// <inheritdoc />
    public MailAccountLanguage LanguageOf(MailAccountId account) =>
        settings.FindConfiguredAccount(account)?.ReadingLanguage ?? MailAccountLanguage.English;
}
