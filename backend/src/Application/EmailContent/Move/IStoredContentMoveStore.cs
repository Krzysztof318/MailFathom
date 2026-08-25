// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>The database side of the move: what is still held here, what one payload holds, and where a moved one now points.</summary>
/// <remarks>
/// <para>
/// One contract rather than three, because the operations describe one restartable walk over the four content tables:
/// which payloads come next, what one of them holds, where the row points once the object is verified, and how much is
/// left. A caller holding only some of them could not make the walk terminate.
/// </para>
/// <para>
/// <b>No operation here takes a persistence session, and that is the design rather than an omission.</b> The move puts
/// an object between reading a payload and repointing its row, so a session spanning the two would hold a database
/// transaction open across a network call — which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// forbids. Each operation is therefore its own unit of work, and the repoint is one statement whose own condition is
/// what makes it safe to issue alone.
/// </para>
/// </remarks>
public interface IStoredContentMoveStore
{
    /// <summary>Reads the next bounded batch of payloads of one kind that the database still holds.</summary>
    /// <param name="kind">The payload kind to read, which decides the table.</param>
    /// <param name="resumeAfter">The identity to continue past, or <see langword="null" /> to start at the beginning of the kind.</param>
    /// <param name="batchSize">The greatest number of payloads to name.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The payloads, ordered by the identity the resume position is expressed in, and never more than <paramref name="batchSize" />.</returns>
    /// <remarks>
    /// Only rows the database still holds are named, so a payload the move has already carried leaves the walk's own
    /// set. The resume position is what carries the walk past a payload it could not move, which would otherwise be
    /// named again on every batch and stand in front of everything behind it forever.
    /// </remarks>
    Task<IReadOnlyList<DatabaseBackedPayload>> GetPayloadsToMoveAsync(
        EmailContentKind kind,
        Guid? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Reads back the raw MIME one database-backed payload holds.</summary>
    /// <param name="kind">The payload kind, which decides the table.</param>
    /// <param name="payloadId">The identity of the row holding it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The bytes, or <see langword="null" /> when the row is gone or no longer database-backed.</returns>
    /// <remarks>
    /// An absent answer is an ordinary outcome rather than a failure: mail is erased and re-synchronized while a move is
    /// running, and a payload that stopped being the database's between the batch that named it and this read is one the
    /// move has nothing to do about.
    /// </remarks>
    Task<ReadOnlyMemory<byte>?> FindPayloadAsync(
        EmailContentKind kind,
        Guid payloadId,
        CancellationToken cancellationToken);

    /// <summary>Points one row at the verified object, leaving the payload the database was holding exactly where it is.</summary>
    /// <param name="kind">The payload kind, which decides the table.</param>
    /// <param name="payloadId">The identity of the row to repoint.</param>
    /// <param name="objectLocator">The whole key the verified object was written under.</param>
    /// <param name="verifiedAt">When the object was read back and found to be the payload the row describes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the row was still database-backed and now points at the object.</returns>
    /// <remarks>
    /// <para>
    /// One statement, conditional on the row still being database-backed, which is what makes it safe to issue outside
    /// any transaction the move opened: a re-synchronization that rewrote the payload while the object was being written
    /// wins, and this answers <see langword="false" /> rather than pointing a newer row at an older message.
    /// </para>
    /// <para>
    /// <b>The payload column is deliberately left as it is.</b> The row is now read from its object and the bytes beside
    /// it are a retained duplicate, held so that a read still works while a deployment is trusting its bucket for the
    /// first time —
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 6.
    /// Freeing it is an operator's own act through <c>IRetainedContentReleaseStore</c>, because it is the one
    /// irreversible step in the whole move and must not be a consequence of a background pass.
    /// </para>
    /// <para>
    /// The verification instant is what the safety interval is later measured from, so it is written here rather than
    /// derived from anything: it says how long this deployment has been reading that object, which is a different
    /// question from how old the mail is. The recorded length and digest stay exactly as they were, so the object
    /// remains checkable against the row that names it after the retained copy has gone.
    /// </para>
    /// </remarks>
    Task<bool> RepointAtObjectAsync(
        EmailContentKind kind,
        Guid payloadId,
        string objectLocator,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken);

    /// <summary>Counts what the database still holds, across every payload kind.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many payloads are left and how many bytes they carry.</returns>
    /// <remarks>
    /// Answered through the same predicate the batch query is narrowed by, so the backlog an operator watches and the
    /// work the move would actually do are one number rather than two that drift the first time either learns something.
    /// It is the whole deployment's figure rather than the current kind's, because what an operator asks is how much of
    /// their mail is still in the database.
    /// </remarks>
    Task<StoredContentBacklog> CountPayloadsAwaitingMoveAsync(CancellationToken cancellationToken);
}
