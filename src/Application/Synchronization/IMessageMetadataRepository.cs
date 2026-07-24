// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Synchronization;

/// <summary>Persists message metadata independently from raw MIME content.</summary>
public interface IMessageMetadataRepository
{
    /// <summary>Inserts or updates metadata for one remote occurrence idempotently within the supplied persistence session.</summary>
    Task UpsertMetadataAsync(
        ISession session,
        RemoteMessageMetadata metadata,
        CancellationToken cancellationToken);
}
