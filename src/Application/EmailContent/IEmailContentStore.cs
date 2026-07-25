// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.EmailContent;

/// <summary>Stores raw email content outside ordinary email metadata queries.</summary>
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
