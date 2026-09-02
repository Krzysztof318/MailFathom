// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Exposes the EF Core context that backs one application persistence session.</summary>
/// <remarks>
/// This is the seam that makes <see cref="IPersistenceSession" /> a real transaction handle rather than a marker
/// parameter. A write repository must obtain its context from the caller's session so it cannot silently write
/// outside the caller's transaction, which is what happens when the repository injects its own context and merely
/// accepts a session it never uses.
/// </remarks>
internal interface IEfCorePersistenceSession
{
    /// <summary>Joins this session, opening its transaction if this is the first write to reach it.</summary>
    /// <param name="cancellationToken">Cancels opening the transaction.</param>
    /// <returns>The context enlisted in this session's transaction.</returns>
    Task<MailFathomDbContext> JoinAsync(CancellationToken cancellationToken);

    /// <summary>Holds a measurement of work staged here until this session's ending is known.</summary>
    /// <param name="measurement">The measurement to publish once the session has committed or rolled back.</param>
    void MeasureOnEnding(ISessionScopedMeasurement measurement);

    /// <summary>States that committing this session leaves an object with nothing pointing at it.</summary>
    /// <param name="objectLocators">The whole keys the rows this session is deleting carry.</param>
    /// <remarks>
    /// <para>
    /// Collected here, before the rows go, because that is the only moment the locators exist to be read. The payload
    /// row is removed by a cascade from the message it belongs to, so a deletion path that waited until after the
    /// commit would find the pointer to the object already gone and no way to derive one — a key is minted by the
    /// write that produced it and nothing about a row determines it.
    /// </para>
    /// <para>
    /// Stating it is not deleting it. What is stated on a session that rolls back is discarded with the session, and
    /// what is stated on one that commits is removed from the endpoint afterwards, which is the ordering
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see>
    /// § 7 requires: deleting an object before its row's deletion is durable would destroy mail whose deletion then
    /// rolled back.
    /// </para>
    /// </remarks>
    void ReleaseOnCommit(IReadOnlyCollection<string> objectLocators);
}

/// <summary>Resolves the EF Core context enlisted in an application persistence session.</summary>
internal static class EfCorePersistenceSessionAccessor
{
    /// <summary>Joins <paramref name="session" /> and answers with the context enlisted in its transaction.</summary>
    /// <param name="session">The session the calling write operation must join.</param>
    /// <param name="cancellationToken">Cancels opening the session's transaction.</param>
    /// <returns>The context whose pending changes commit with the session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="session" /> is backed by a different persistence provider and therefore cannot
    /// supply an EF Core context. A session created by <see cref="PersistenceSessionFactory" /> in another scope is
    /// valid: writing through its context is exactly the intended behavior, because that is the transaction its
    /// caller opened.
    /// </exception>
    /// <remarks>
    /// This is asynchronous because joining is what opens the transaction. A session holds none until a write reaches
    /// it, so a caller that has other work to finish first — handing bytes to a content endpoint, computing a digest
    /// over them — does that work outside the transaction by doing it before this call.
    /// </remarks>
    public static Task<MailFathomDbContext> JoinAsync(
        IPersistenceSession session,
        CancellationToken cancellationToken) => SessionOf(session).JoinAsync(cancellationToken);

    /// <summary>Gets the EF Core session <paramref name="session" /> is.</summary>
    /// <param name="session">The session the calling write operation must join.</param>
    /// <returns>The same session, seen through the seam a write operation needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="session" /> is backed by a different persistence provider, exactly as
    /// <see cref="JoinAsync" /> describes.
    /// </exception>
    public static IEfCorePersistenceSession SessionOf(IPersistenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not IEfCorePersistenceSession efCoreSession)
        {
            throw new ArgumentException(
                $"This repository writes through EF Core and requires a session created by {nameof(PersistenceSessionFactory)}.",
                nameof(session));
        }

        return efCoreSession;
    }
}
