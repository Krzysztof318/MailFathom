// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Emails;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>Persists email metadata independently from raw MIME content.</summary>
/// <remarks>
/// The port narrows persistence to the single idempotent operation synchronization needs, which is the use-case-shaped
/// contract
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// chose over a generic repository or an exposed <c>IQueryable</c>. It also has no published
/// contract to restate: EF Core's query surface is a concrete <c>DbContext</c>, and MailFathom allows no fake provider to
/// stand in for PostgreSQL semantics, so the upsert is expressed in domain terms and asserted through this port.
/// </remarks>
public interface IEmailMetadataRepository
{
    /// <summary>Inserts or updates metadata for one remote occurrence idempotently and returns its stable local identity.</summary>
    /// <param name="session">The explicit persistence session this metadata write participates in.</param>
    /// <param name="metadata">The remote occurrence metadata to store.</param>
    /// <param name="extractedMetadata">
    /// What was read out of the occurrence's raw MIME, or <see langword="null" /> when nothing was read from it.
    /// </param>
    /// <param name="contentAvailability">Whether raw MIME content is stored for this occurrence, or why it is not.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stable local identifier of the inserted or existing stored email.</returns>
    /// <remarks>
    /// Absent extracted metadata is one state rather than several: nothing was read from this occurrence's MIME, whether
    /// because the payload was never fetched or because no reader could parse it. Which of those happened is already
    /// carried by <paramref name="contentAvailability" /> and by the run's own counters, and neither changes what this
    /// write can record. The fields only extraction supplies — participants, the received timestamp, thread ancestors,
    /// and the attachment summary — keep whatever an earlier run wrote rather than being cleared, because the remote
    /// message is immutable and a reader that fails this time is no reason to forget what it read last time.
    /// </remarks>
    Task<StoredEmailId> UpsertMetadataAsync(
        IPersistenceSession session,
        RemoteEmailMetadata metadata,
        ExtractedEmailMetadata? extractedMetadata,
        StoredEmailContentAvailability contentAvailability,
        CancellationToken cancellationToken);
}
