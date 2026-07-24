// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Application.Synchronization;

/// <summary>Persists message metadata independently from raw MIME content.</summary>
public interface IMessageMetadataRepository
{
    /// <summary>Inserts or updates metadata for one remote occurrence idempotently and returns its stable local identity.</summary>
    /// <param name="session">The explicit persistence session this metadata write participates in.</param>
    /// <param name="metadata">The remote occurrence metadata to store.</param>
    /// <param name="contentAvailability">Whether raw MIME content is stored for this occurrence, or why it is not.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stable local identifier of the inserted or existing stored email.</returns>
    Task<StoredEmailId> UpsertMetadataAsync(
        ISession session,
        RemoteMessageMetadata metadata,
        StoredEmailContentAvailability contentAvailability,
        CancellationToken cancellationToken);
}
