// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>States what language each mailbox of a suite is read in.</summary>
/// <remarks>
/// A deployment that has been told nothing writes English about every mailbox, which is what the real reader answers
/// for an account it does not serve, so a suite arranging nothing gets the quiet answer rather than a roster.
/// </remarks>
internal sealed class SyntheticAccountLanguages : IMailAccountLanguages
{
    private readonly Dictionary<MailAccountId, MailAccountLanguage> declared = [];

    /// <summary>States the language one mailbox is read in.</summary>
    /// <param name="account">The mailbox.</param>
    /// <param name="language">The language its derivations come out in.</param>
    /// <returns>The same instance, so an arrangement reads as one expression.</returns>
    public SyntheticAccountLanguages Reading(MailAccountId account, MailAccountLanguage language)
    {
        this.declared[account] = language;

        return this;
    }

    /// <inheritdoc />
    public MailAccountLanguage LanguageOf(MailAccountId account) =>
        this.declared.GetValueOrDefault(account, MailAccountLanguage.English);
}
