// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Reads which of an account's mail still owes a reading of its attachments, and stores what one reading produced.</summary>
/// <remarks>
/// <para>
/// The two halves are one port for the reason the embedding store's are: what the read reports as outstanding is
/// exactly what the write stops reporting, and two implementations disagreeing about that would either read every
/// attachment on every run or read none of them ever again.
/// </para>
/// <para>
/// The selection carries the same admissions the cut does — the message is still local, the owner's rules have finished
/// with it and are not still moving it, its folder is one an operator asked to have embedded, and the classification
/// gate admits it. That is what makes withholding reach here without a second evaluation: an attachment cannot be more
/// visible than the message that carried it, so it inherits the message's verdict by never being selected.
/// </para>
/// </remarks>
public interface IStoredEmailAttachmentTextStore
{
    /// <summary>Reads a bounded batch of the account's mail whose attachments nothing has read yet.</summary>
    /// <param name="account">The account whose mail is walked.</param>
    /// <param name="resumeAfter">The identity the previous batch of this walk reached, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">The greatest number of messages to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages in identity order, or an empty list when the account owes none past the resume position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive.</exception>
    /// <remarks>
    /// The resume position exists for the messages a reading leaves unsettled rather than for the ones it settles, in
    /// the same way the rule queue's does. Most messages leave this selection by being read, but one whose stored copy
    /// needs fetching again, or whose picture a provider did not answer for, deliberately keeps no stamp — and without
    /// a cursor the next batch of the same pass would select exactly those messages again, derive them again, and never
    /// reach anything behind them. Mail carrying no attachment is never selected at all rather than being selected and
    /// stepped over, which is what keeps the walk proportional to the mail this feature is about.
    /// </remarks>
    Task<IReadOnlyList<EmailAwaitingAttachmentText>> GetEmailsAwaitingAttachmentTextAsync(
        MailAccountIdentity account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Stores one message's attachment readings, the passages they yield, and the fact that it was read.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="emailId">The message the readings belong to.</param>
    /// <param name="derived">What its attachments yielded and the configuration they were read under.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// One statement for all three, because a message stamped as read whose readings did not commit would never be
    /// offered to a parser again, and readings committed without the stamp would be read a second time on the next run.
    /// The passages are cut here rather than by the caller for the reason every other passage is: one writer decides
    /// what a stored chunk is, so nothing can store passages built to different rules.
    /// </remarks>
    Task SaveAttachmentTextAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        EmailAttachmentTextDerivation derived,
        CancellationToken cancellationToken);

    /// <summary>Stages the removal of every reading stored for one message's attachments.</summary>
    /// <param name="session">The explicit persistence session this removal participates in.</param>
    /// <param name="emailId">The message whose readings are removed.</param>
    /// <param name="cancellationToken">Cancels the read before anything is staged.</param>
    /// <returns>How many readings the removal reached, which is zero for a message whose attachments nothing read.</returns>
    /// <remarks>
    /// The junk verdict's other half. A reading is a second durable copy of derived mail content — a document's whole
    /// text, a model's description of a picture, and the file name a sender chose — so removing the passages cut from
    /// it while leaving it stored would keep exactly what the verdict called for the removal of. The message's
    /// derivation stamp is cleared with the rows, so a message the classification gate later re-admits is read again
    /// rather than left permanently without attachment text: junk stays out of the walk on the gate, which is what a
    /// reversed verdict moves, rather than on a stamp nothing would ever clear.
    /// Idempotent and staged, on the same terms the passage removal is.
    /// </remarks>
    Task<int> DiscardAttachmentTextAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        CancellationToken cancellationToken);
}
