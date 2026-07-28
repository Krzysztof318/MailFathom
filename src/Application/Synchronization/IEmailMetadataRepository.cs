// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Persists email metadata independently from raw MIME content.</summary>
/// <remarks>
/// The port narrows persistence to the single idempotent operation synchronization needs, which is the use-case-shaped
/// contract
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// chose over a generic repository or an exposed <c>IQueryable</c>. It also has no published
/// contract to restate: EF Core's query surface is a concrete <c>DbContext</c>, and MailMcp allows no fake provider to
/// stand in for PostgreSQL semantics, so the upsert is expressed in domain terms and asserted through this port.
/// </remarks>
public interface IEmailMetadataRepository
{
    /// <summary>Inserts or updates metadata for one remote occurrence idempotently and returns its stable local identity.</summary>
    /// <param name="session">The explicit persistence session this metadata write participates in.</param>
    /// <param name="metadata">The remote occurrence metadata to store.</param>
    /// <param name="contentAvailability">Whether raw MIME content is stored for this occurrence, or why it is not.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stable local identifier of the inserted or existing stored email.</returns>
    Task<StoredEmailId> UpsertMetadataAsync(
        IPersistenceSession session,
        RemoteEmailMetadata metadata,
        StoredEmailContentAvailability contentAvailability,
        CancellationToken cancellationToken);
}
