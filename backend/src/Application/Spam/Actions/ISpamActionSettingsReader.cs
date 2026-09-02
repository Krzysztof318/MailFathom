// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Answers what one owner asked to happen to mail a classification calls junk.</summary>
/// <remarks>
/// <para>
/// It is a port of its own rather than a second property on <see cref="ISpamClassificationSettingsReader" /> because the
/// two answer for different halves of the feature: that one decides whether a verdict is reached at all, and this one
/// decides whether anything is done about it. Keeping them apart is what lets the classifier stay a use case that writes
/// nothing but its own record — it never resolves this reader and cannot reach a mailbox through it.
/// </para>
/// <para>
/// The answer is one owner's because the act is on that owner's own mail server: moving a message and marking it read
/// are things done to somebody's mailbox, and nobody else's settings may decide them.
/// </para>
/// </remarks>
public interface ISpamActionSettingsReader
{
    /// <summary>Gets what one owner decided, as it stands now.</summary>
    /// <param name="owner">The owner whose mailbox would be written to.</param>
    /// <returns>Their settings, or <see cref="SpamActionSettings.None" /> where this deployment serves no such owner.</returns>
    /// <remarks>
    /// Read per request rather than captured, so an owner switching filing on reaches the next verdict without a
    /// restart — and so one switching it off stops the next one.
    /// </remarks>
    SpamActionSettings ActionsFor(MailOwnerId owner);
}
