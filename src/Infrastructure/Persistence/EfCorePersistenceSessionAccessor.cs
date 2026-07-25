// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;

namespace MailMcp.Infrastructure.Persistence;

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
    MailMcpDbContext DbContext { get; }
}

/// <summary>Resolves the EF Core context enlisted in an application persistence session.</summary>
internal static class EfCorePersistenceSessionAccessor
{
    /// <summary>Gets the context enlisted in <paramref name="session" />'s transaction.</summary>
    /// <param name="session">The session the calling write operation must join.</param>
    /// <returns>The context whose pending changes commit with the session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="session" /> was not created by the EF Core persistence session factory. Writing
    /// through a foreign session would commit outside the caller's transaction, so this fails loudly instead.
    /// </exception>
    public static MailMcpDbContext DbContextOf(IPersistenceSession session)
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
