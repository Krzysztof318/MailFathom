// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;

namespace MailMcp.Application.MessageContent;

/// <summary>Stores raw message content outside ordinary message metadata queries.</summary>
public interface IMessageContentStore
{
    /// <summary>Saves raw MIME content idempotently for one occurrence.</summary>
    /// <param name="session">The explicit synchronization write session this content write participates in.</param>
    /// <param name="content">The raw content to store.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    Task SaveContentAsync(IMailSynchronizationUnitOfWorkSession session, RemoteMessageContent content, CancellationToken cancellationToken);
}
