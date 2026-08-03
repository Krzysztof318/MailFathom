// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

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
    /// <summary>Gets the context enlisted in this session's transaction.</summary>
    MailFathomDbContext DbContext { get; }
}

/// <summary>Resolves the EF Core context enlisted in an application persistence session.</summary>
internal static class EfCorePersistenceSessionAccessor
{
    /// <summary>Gets the context enlisted in <paramref name="session" />'s transaction.</summary>
    /// <param name="session">The session the calling write operation must join.</param>
    /// <returns>The context whose pending changes commit with the session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="session" /> is backed by a different persistence provider and therefore cannot
    /// supply an EF Core context. A session created by <see cref="PersistenceSessionFactory" /> in another scope is
    /// valid: writing through its context is exactly the intended behavior, because that is the transaction its
    /// caller opened.
    /// </exception>
    public static MailFathomDbContext DbContextOf(IPersistenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not IEfCorePersistenceSession efCoreSession)
        {
            throw new ArgumentException(
                $"This repository writes through EF Core and requires a session created by {nameof(PersistenceSessionFactory)}.",
                nameof(session));
        }

        return efCoreSession.DbContext;
    }
}
