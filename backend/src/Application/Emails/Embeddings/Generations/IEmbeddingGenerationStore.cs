// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>The profile rows a generation's life is written into, and every transition between them.</summary>
/// <remarks>
/// <para>
/// One contract rather than several, because these operations are one state machine: registering a generation, making
/// it the one retrieval reads, abandoning one that never got there, and clearing out what a replaced one left. A caller
/// holding only some of them could move a generation into a state nothing would take it out of.
/// </para>
/// <para>
/// It overlaps <see cref="IActiveEmbeddingProfileReader" /> deliberately and narrowly. That port answers the one
/// question the read path asks — which generation serves a search — and it is the only thing retrieval and the live
/// embedding worker are allowed to know about profiles. This one is the write side of the same table, and reading both
/// generations is part of deciding a transition rather than a second way to ask what is active.
/// </para>
/// </remarks>
public interface IEmbeddingGenerationStore
{
    /// <summary>Reads which generation serves retrieval and which one, if any, is being built.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Both generations, either of which may be absent.</returns>
    Task<EmbeddingGenerations> ReadGenerationsAsync(CancellationToken cancellationToken);

    /// <summary>Registers a generation to embed into, or resolves the one this geometry already has.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="identity">The geometry the generation is fixed to.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The registered profile, in its building state.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Resolved through the identity fingerprint rather than inserted blindly, which is what makes returning to a
    /// previous model a switch rather than a duplicate: the row is still there, its identity may never have moved, and
    /// the vectors that once hung on it were attributable to exactly this geometry. What the resolution does change is
    /// the row's lifecycle, which is the only part of a profile that moves.
    /// </remarks>
    Task<RegisteredEmbeddingProfile> RegisterBuildingAsync(
        IPersistenceSession session,
        EmbeddingProfileIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>Makes a built generation the one retrieval reads, and supersedes whichever one it replaces.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="built">The generation whose vectors are complete.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the switch applied, and <see langword="false" /> when the named generation was no longer being built.</returns>
    /// <remarks>
    /// One operation rather than two, because the two halves are one fact: an instance that superseded its old
    /// generation without promoting the new one would serve nothing, and one that promoted the new one first would
    /// briefly claim two. Both are written in the caller's transaction, so the switch a reader observes is the whole of
    /// it or none of it — and a generation somebody abandoned while the sweep was finishing it is not switched to at
    /// all, which is what the answer reports.
    /// </remarks>
    Task<bool> SwitchToAsync(IPersistenceSession session, EmbeddingProfileId built, CancellationToken cancellationToken);

    /// <summary>Abandons a generation that was being built, leaving whichever one is serving where it is.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="building">The generation to abandon.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the generation was abandoned, and <see langword="false" /> when it was no longer being built.</returns>
    /// <remarks>
    /// The row survives and its identity stays what it was, so activating the same model again later resolves to it
    /// rather than registering a second. What does not survive is the partial vectors, which the removal below reaches
    /// because the row is superseded like any other generation nothing reads. A generation that finished being built
    /// and became the one serving between the read and this write is not abandoned, because abandoning what searches
    /// are answered from is not what cancelling a reindex means.
    /// </remarks>
    Task<bool> AbandonAsync(IPersistenceSession session, EmbeddingProfileId building, CancellationToken cancellationToken);

    /// <summary>Finds a superseded generation that still holds vectors, if one does.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The generation whose vectors are still to be removed, or <see langword="null" /> when none is.</returns>
    /// <remarks>
    /// One at a time, because removal is bounded and a pass empties one generation before it looks at another. A
    /// generation activated again while its removal was in progress is no longer superseded and stops being reported
    /// here, which is what lets a rollback keep whatever vectors it still had.
    /// </remarks>
    Task<EmbeddingProfileId?> FindSupersededProfileHoldingVectorsAsync(CancellationToken cancellationToken);

    /// <summary>Removes a bounded batch of one superseded generation's vectors.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="profileId">The generation whose vectors are no longer read.</param>
    /// <param name="batchSize">The greatest number of vectors to remove in this batch.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many vectors this batch removed, which is zero exactly when the generation holds none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive.</exception>
    /// <remarks>
    /// Bounded rather than one statement, because a generation of a large mailbox is millions of rows and a single
    /// delete would hold one transaction, one lock set, and one write-ahead burst for as long as it took. They are
    /// removed rather than kept for a rollback window: they are personal data derived from mail whose purpose ended at
    /// the switch, which is the trade
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
    /// makes deliberately and states the cost of.
    /// </remarks>
    Task<int> RemoveVectorsAsync(
        IPersistenceSession session,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken);
}
