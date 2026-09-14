// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Answers what one account asks to happen to mail a classification calls junk.</summary>
/// <remarks>
/// <para>
/// It is a port of its own rather than a second property on <see cref="ISpamClassificationSettingsReader" /> because the
/// two answer for different halves of the feature: that one decides whether a verdict is reached at all, and this one
/// decides whether anything is done about it. Keeping them apart is what lets the classifier stay a use case that writes
/// nothing but its own record — it never resolves this reader and cannot reach a mailbox through it.
/// </para>
/// <para>
/// The answer is one account's because the act is on that account's own mail server: moving a message and marking it
/// read are things done to one mailbox, and no other mailbox's settings may decide them.
/// </para>
/// </remarks>
public interface ISpamActionSettingsReader
{
    /// <summary>Gets what one account states, as it stands now.</summary>
    /// <param name="account">The account whose mailbox would be written to.</param>
    /// <returns>Its settings, or <see cref="SpamActionSettings.None" /> where this deployment serves no such account.</returns>
    /// <remarks>
    /// Read per request rather than captured, so switching filing on reaches the next verdict without a restart — and
    /// so switching it off stops the next one.
    /// </remarks>
    SpamActionSettings ActionsFor(MailAccountId account);
}
