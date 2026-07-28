// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.EmailContent;

/// <summary>Stores raw email content outside ordinary email metadata queries.</summary>
/// <remarks>
/// No storage library publishes a contract for this seam, and the store behind it is expected to move from a
/// PostgreSQL table to object storage without a use case noticing, so the port names the operation in domain terms
/// instead. It takes the caller's session rather than opening one of its own, which is what makes a content write
/// commit or roll back together with the metadata row it belongs to.
/// </remarks>
public interface IEmailContentStore
{
    /// <summary>Saves raw MIME content idempotently for one locally stored email.</summary>
    /// <param name="session">The explicit persistence session this content write participates in.</param>
    /// <param name="storedEmailId">The stable local identifier of the corresponding metadata row.</param>
    /// <param name="content">The raw content to store.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailContent content,
        CancellationToken cancellationToken);
}
