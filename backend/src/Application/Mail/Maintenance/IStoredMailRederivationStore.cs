// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>The state the re-derivation pass reads and writes as it walks one scope's stored mail.</summary>
/// <remarks>
/// <para>
/// One contract rather than several, for the reason the extraction backfill's is one: the operations describe a single
/// restartable walk — where the last invocation stopped, which emails come next, what one email's re-reading produced,
/// how far this invocation has come, and that the walk is over. A caller holding only some of them could not make the
/// walk terminate.
/// </para>
/// <para>
/// The resume position is per scope, because two scopes are two walks and an operator refreshing one account must not
/// move a cursor another account's walk is resuming from. It is cleared rather than parked once a walk finishes, so a
/// later release's re-derivation of the same scope starts at the beginning instead of behind the last one.
/// </para>
/// </remarks>
public interface IStoredMailRederivationStore
{
    /// <summary>Reads the position the last committed batch of this scope's walk reached.</summary>
    /// <param name="scope">The account, and the one folder of it, being walked.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identity of the last email a committed batch processed, or <see langword="null" /> when this scope's walk has not started.</returns>
    Task<StoredEmailId?> FindResumePositionAsync(StoredMailScope scope, CancellationToken cancellationToken);

    /// <summary>Reads the next bounded batch of stored emails whose MIME this pass re-reads.</summary>
    /// <param name="scope">The account, and the one folder of it, being walked.</param>
    /// <param name="resumeAfter">The position to continue past, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">The greatest number of emails to return.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The emails to re-read, in the stable order the resume position is expressed in, and never more than <paramref name="batchSize" />.</returns>
    /// <remarks>
    /// Only emails whose raw MIME is actually stored are returned, and a row a tombstone hides is left out. The first
    /// has nothing this pass could re-read, and the second is mail nothing may retrieve, which is the same rule both
    /// backfills over stored mail already apply.
    /// </remarks>
    Task<IReadOnlyList<StoredMailAwaitingRederivation>> GetEmailsToRederiveAsync(
        StoredMailScope scope,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Writes what re-reading one email's MIME produced onto its existing row.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="storedEmailId">The email being re-read.</param>
    /// <param name="metadata">What the MIME reader extracted.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// The row's own columns and nothing else. The search document, the passages, and the vectors are derived from the
    /// message's text, which the same immutable bytes read by the same reader produce unchanged, so rewriting them
    /// would spend a re-cut and a provider bill to arrive back where they already are —
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
    /// makes that a deliberately confirmed act rather than something a metadata refresh performs. Text written under a
    /// sensitive-content configuration this deployment no longer runs is the one case where it does change, and the
    /// extraction backfill's rebuild already owns it.
    /// </remarks>
    Task ApplyRederivedMetadataAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>Records how far this scope's walk has come, so an interrupted pass resumes instead of restarting.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="scope">The account, and the one folder of it, being walked.</param>
    /// <param name="position">The last email the batch processed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// Staged in the same session as the batch's writes, so a committed position always describes work that is itself
    /// committed and a crash between the two cannot step over an email.
    /// </remarks>
    Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredMailScope scope,
        StoredEmailId position,
        CancellationToken cancellationToken);

    /// <summary>Forgets this scope's walk, which is what an invocation that reached the end of it records.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="scope">The account, and the one folder of it, whose walk has finished.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been staged.</returns>
    /// <remarks>
    /// A finished walk keeps no position, so asking for the same scope again re-reads it from the beginning. That is
    /// the behavior the command exists for: the next release adds another property, and the operator asks again.
    /// </remarks>
    Task ClearResumePositionAsync(
        IPersistenceSession session,
        StoredMailScope scope,
        CancellationToken cancellationToken);
}
