// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Collection;

/// <summary>Counts how much of an account's stored mail one address wrote.</summary>
/// <remarks>
/// <para>
/// This is what makes a threshold possible without collection keeping a ledger of its own. The evidence that somebody
/// writes to the owner is the mail this deployment already holds, so it is read rather than accumulated — which is the
/// whole of why collection derives no personal data beyond the contacts it records. An owner who erases the collected
/// half of their book erases everything collection produced, because there is nothing else it wrote.
/// </para>
/// <para>
/// The count is over one account's mail rather than the whole store, so mail belonging to an account whose owner never
/// switched collection on never becomes the reason a contact was recorded.
/// </para>
/// </remarks>
public interface IAuthoredMailTally
{
    /// <summary>Counts the messages of one account that one address authored.</summary>
    /// <param name="accountId">The account whose stored mail is counted.</param>
    /// <param name="author">The address to count the messages of.</param>
    /// <param name="ceiling">The count to stop at, because the caller only asks whether a threshold is reached.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many messages that address wrote, at most <paramref name="ceiling" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ceiling" /> is not positive.</exception>
    /// <remarks>
    /// One message stored in two folders of one account counts once, because what the threshold is about is how often
    /// somebody wrote rather than how many copies of it a mailbox keeps. A message whose sender wrote no identifier for
    /// it is counted per stored copy, since nothing else distinguishes two of them.
    /// </remarks>
    Task<int> CountMessagesAuthoredByAsync(
        MailAccountId accountId,
        EmailAddress author,
        int ceiling,
        CancellationToken cancellationToken);
}
