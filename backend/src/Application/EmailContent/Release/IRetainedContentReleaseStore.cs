// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Release;

/// <summary>The database side of the release: what is still retained beside an object, and the freeing of it.</summary>
/// <remarks>
/// <para>
/// A payload the move has carried leaves its bytes where they were. The row points at the object and is read from it,
/// and the <c>bytea</c> column beside it is a retained duplicate held so that a read still works while the endpoint is
/// being trusted for the first time — which is the one duplicated state
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 6
/// admits. This is what ends that state.
/// </para>
/// <para>
/// Separate from <see cref="IStoredContentMoveStore" /> although both walk the same four tables, because the two answer
/// opposite questions about a row: the move asks which payloads the database still owns, and this asks which payloads it
/// merely still holds. One contract carrying both would let a caller reach the irreversible half while meaning the
/// reversible one.
/// </para>
/// <para>
/// <b>No operation here takes a persistence session.</b> Freeing a column is one statement whose own predicate is what
/// makes it safe to issue alone, and nothing else in the release has to commit with it.
/// </para>
/// </remarks>
public interface IRetainedContentReleaseStore
{
    /// <summary>Counts the database copies retained beside payloads the move has already carried.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many payloads still carry one and how many bytes they hold between them.</returns>
    /// <remarks>
    /// The whole deployment's figure across every payload kind, because what an operator asks is how much of their
    /// database is duplication rather than which table it is in. It is read on request rather than published as a
    /// series: it costs an aggregate over the four content tables, and nothing needs it on a scrape interval.
    /// </remarks>
    Task<StoredContentBacklog> CountRetainedPayloadsAsync(CancellationToken cancellationToken);

    /// <summary>Frees a bounded batch of one payload kind's retained copies, leaving every row pointing at its object.</summary>
    /// <param name="kind">The payload kind to free, which decides the table.</param>
    /// <param name="verifiedOnOrBefore">The latest verification instant a copy may carry and still be freed, which is the safety interval expressed as a cutoff.</param>
    /// <param name="batchSize">The greatest number of copies to free.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many copies were freed and how much they were holding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> names no payload kind, or <paramref name="batchSize" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>This is the irreversible step, and the only one in the move.</b> What it removes is the last copy of a message
    /// this deployment holds outside the bucket, so the caller is the one that establishes there is nothing left to
    /// copy; this performs what it was asked for and nothing more.
    /// </para>
    /// <para>
    /// The recorded length and digest are untouched, which is what keeps a released payload checkable against its object
    /// afterwards. Only the bytes go.
    /// </para>
    /// <para>
    /// The rows are read before they are freed, because the volume is what an operator is being shown and a statement
    /// answering with a row count cannot say how much it removed. A row a concurrent write replaced between the two is
    /// left alone by the freeing statement's own predicate and is therefore not counted, so the count reports what was
    /// freed and the volume what the batch described.
    /// </para>
    /// </remarks>
    Task<ReleasedContentPayloads> ReleaseAsync(
        EmailContentKind kind,
        DateTimeOffset verifiedOnOrBefore,
        int batchSize,
        CancellationToken cancellationToken);
}
