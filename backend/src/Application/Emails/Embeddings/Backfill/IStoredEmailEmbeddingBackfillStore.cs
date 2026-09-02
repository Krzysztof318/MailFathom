// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>The state the embedding backfill reads and writes as it walks the mail the live path never reached.</summary>
/// <remarks>
/// <para>
/// One contract rather than several, for the reason the extraction backfill's port is one: its operations describe a
/// single restartable walk — where the last run stopped, how much the walk has left in front of it, which messages come
/// next, how a message with no passages gains them, and how far this run has come. A caller holding only some of them
/// could not make the walk terminate.
/// </para>
/// <para>
/// The walk is ordered by the stored-email identifier, which is the only ordering that is total, stable, and already
/// indexed. That identifier is time-ordered, so a message stored after the walk passed a point sorts after it and the
/// same walk reaches newly synchronized mail the bounded live backlog turned away.
/// </para>
/// </remarks>
public interface IStoredEmailEmbeddingBackfillStore
{
    /// <summary>Reads the position the current sweep has reached, or nothing when a sweep is about to start.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identity of the last message this sweep processed, or <see langword="null" /> to start at the beginning.</returns>
    Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken);

    /// <summary>Counts the messages that still have passages without a vector under the given profile.</summary>
    /// <param name="profileId">The profile whose vectors decide what is outstanding.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many stored messages the sweep about to start has to reach.</returns>
    /// <remarks>
    /// Asked once at the start of a sweep and never per run, because it is an unbounded count over every passage and the
    /// answer only has to be good enough for an operator to see the size of what they are paying for. What it counts is
    /// the whole sweep rather than what is left in front of the position, so the number an operator reads is the one the
    /// progress counters are measured against.
    /// </remarks>
    Task<int> CountEmailsAwaitingEmbeddingAsync(EmbeddingProfileId profileId, CancellationToken cancellationToken);

    /// <summary>Reads the next bounded batch of messages that are not current for the given profile.</summary>
    /// <param name="resumeAfter">The position to continue past, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="profileId">The profile whose vectors decide what is outstanding.</param>
    /// <param name="batchSize">The greatest number of messages to return.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The messages to bring up to date, in the order the resume position is expressed in, and never more than <paramref name="batchSize" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive.</exception>
    /// <remarks>
    /// Two conditions select a message, and they are the two halves of what this backfill exists for: a message whose
    /// extraction produced text and which carries no passages at all was stored before chunking existed, and a message
    /// carrying a passage with no vector under this profile was stored before the profile existed or was turned away by
    /// the live backlog's bound. A message no one may search is not selected, because vectors nothing may retrieve are a
    /// provider bill with no reader.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingEmbedding>> GetEmailsAwaitingEmbeddingAsync(
        StoredEmailId? resumeAfter,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Cuts one message's already-extracted text into passages, inside the caller's open session.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="storedEmailId">The message whose passages are missing.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the passages have been staged.</returns>
    /// <remarks>
    /// The text is the one an earlier extraction already stored, so nothing here re-reads raw MIME, reaches a mail
    /// server, or asks a provider for anything. Cutting it applies the same rules synchronization applies, which is what
    /// keeps a message this walk reaches from being cut differently than the message beside it.
    /// </remarks>
    Task DeriveChunksAsync(IPersistenceSession session, StoredEmailId storedEmailId, CancellationToken cancellationToken);

    /// <summary>Records how far this sweep has come, or ends the sweep so the next one starts at the beginning.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="position">The last message processed, or <see langword="null" /> to end the sweep.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// A <see langword="null" /> position is what makes this backfill a repeating sweep rather than a walk that finishes
    /// once. A message a provider refused keeps passages without vectors and the walk has already stepped past it, so
    /// without a sweep that starts again those passages would never be reached; ending the sweep is what promises they
    /// are.
    /// </remarks>
    Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId? position,
        CancellationToken cancellationToken);
}
