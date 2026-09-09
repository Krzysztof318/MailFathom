// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Answers what one account has asked MailFathom to file copies of its outgoing mail as.</summary>
/// <remarks>
/// <para>
/// It is a reader of its own rather than a value on the send, because the answer belongs to the account and not to the
/// message. An account whose provider files the sent copy itself says so once, and every message it sends is then
/// filed the same way.
/// </para>
/// <para>
/// Whether a provider does file the copy itself is configured rather than detected, and that is deliberate: a provider
/// that files it does so asynchronously, so looking in the folder immediately after a delivery cannot tell
/// <em>will appear shortly</em> from <em>will never appear</em>. Guessing from a look would put either a duplicate or a
/// gap in somebody's Sent folder, and only one of those is something a user can fix.
/// </para>
/// </remarks>
public interface IOutgoingMailFilingPolicyReader
{
    /// <summary>Reports whether this account files a copy of each delivered message into its sent folder.</summary>
    /// <param name="accountId">The account the message was sent as.</param>
    /// <returns><see langword="true" /> when a copy is appended after a successful delivery.</returns>
    /// <remarks>
    /// It defaults to <see langword="true" /> where an account says nothing, which is wrong in the direction a user
    /// can recover from: a duplicate they delete beats a record of what they sent that never existed.
    /// </remarks>
    bool FilesSentCopy(MailAccountId accountId);

    /// <summary>Reports whether this account takes its own sent copy back out once the provider files one beside it.</summary>
    /// <param name="accountId">The account the message was sent as.</param>
    /// <returns><see langword="true" /> when a copy this system appended is withdrawn on meeting the provider's own.</returns>
    /// <remarks>
    /// It is the answer to the duplicate the paragraph above leaves standing, and it is a separate question from
    /// <see cref="FilesSentCopy" /> because it is decided by something else: filing is decided before a message is sent
    /// and this after a second occurrence of it has actually been discovered, which is a fact rather than a guess about
    /// what a provider will do. It defaults to <see langword="true" />, so an account nobody configured ends with one
    /// copy of each message it sent.
    /// </remarks>
    bool WithdrawsDuplicateSentCopy(MailAccountId accountId);
}
