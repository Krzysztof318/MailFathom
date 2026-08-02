// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails;

/// <summary>The state the extraction backfill reads and writes as it walks the emails stored before extraction existed.</summary>
/// <remarks>
/// <para>
/// The port is one contract rather than three because its four operations describe one restartable walk: where the
/// last run stopped, which emails come next, what one email's re-reading produced, and how far this run has come. A
/// caller holding only some of them could not make the walk terminate.
/// </para>
/// <para>
/// The resume position is what makes the walk finite. Selecting only emails that have no extraction would already be
/// idempotent, but a message no reader can parse never gains one, so such a walk would return the same unreadable
/// message on every run and never reach the messages behind it.
/// </para>
/// </remarks>
public interface IStoredEmailExtractionBackfillStore
{
    /// <summary>Reads the position the last committed batch reached, or nothing when the backfill has never run.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identity of the last email a committed batch processed, or <see langword="null" />.</returns>
    Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken);

    /// <summary>Reads the next bounded batch of stored emails whose MIME has never been read.</summary>
    /// <param name="resumeAfter">The position to continue past, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">The greatest number of emails to return.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The emails to extract, in the stable order the resume position is expressed in, and never more than <paramref name="batchSize" />.</returns>
    /// <remarks>
    /// Only emails whose raw MIME is actually stored are returned. An occurrence recorded without content has nothing
    /// to re-read, and returning it would make the backfill report work it can never complete.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAwaitingExtraction>> GetEmailsAwaitingExtractionAsync(
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Writes what re-reading one email's MIME produced onto its existing row and search document.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="storedEmailId">The email being re-read.</param>
    /// <param name="metadata">What the MIME reader extracted.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// This persists the classification markers specification 06 introduced before it persists any text derived from
    /// them. A row stored before that specification carries no markers at all, so reading one instead of writing it
    /// would leave every pre-existing encrypted message indistinguishable from an empty one.
    /// </remarks>
    Task ApplyExtractionAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>Records how far this run has come, so an interrupted backfill resumes instead of restarting.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="position">The last email the batch processed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// Staged in the same session as the batch's extractions, so a committed position always describes work that is
    /// itself committed and a crash between the two cannot skip an email.
    /// </remarks>
    Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId position,
        CancellationToken cancellationToken);
}
