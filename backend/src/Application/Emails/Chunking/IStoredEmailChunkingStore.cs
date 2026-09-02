// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Reads the mail whose passages the arrival pipeline still owes, and cuts them from what is already stored.</summary>
/// <remarks>
/// The selection is the whole contract. A message leaves it by being cut, so a pass needs no cursor of its own and a
/// repeat costs one query rather than a second walk of the mailbox; and what puts a message into it is stated by the
/// implementation rather than by the caller, so the classification gate, the folder's embedding switch, and the record
/// that the rules have finished with the message are one predicate instead of three the pass could get out of order.
/// </remarks>
public interface IStoredEmailChunkingStore
{
    /// <summary>Reads one bounded batch of the account's mail that is ready to be cut and has not been.</summary>
    /// <param name="account">The account whose mail is walked.</param>
    /// <param name="batchSize">How many messages the batch may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages, ordered by their identifier, each with the admission that let it through.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is below one.</exception>
    /// <remarks>
    /// No resume position is taken, because cutting a message is what takes it out of this selection: every message the
    /// batch names carries extracted text, and text extraction never produces an empty or blank reading, so the cut
    /// writes at least one passage for each of them and the following batch is the next messages rather than the same
    /// ones.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingChunking>> GetEmailsAwaitingChunkingAsync(
        MailAccountIdentity account,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Cuts one message's passages from the text an earlier extraction stored, inside the caller's session.</summary>
    /// <param name="session">The transaction the cut joins.</param>
    /// <param name="storedEmailId">The message to cut.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the passages are staged.</returns>
    /// <remarks>
    /// The text comes from the stored search document rather than from raw MIME, so the cut is a local write and lands on
    /// exactly the text every enabled scanner has already redacted.
    /// </remarks>
    Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken);
}
