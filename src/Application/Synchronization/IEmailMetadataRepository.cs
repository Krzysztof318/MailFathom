// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>Persists email metadata independently from raw MIME content.</summary>
/// <remarks>
/// The port narrows persistence to the single idempotent operation synchronization needs, which is the use-case-shaped
/// contract
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
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

    /// <summary>Moves one stored email onto the occurrence a relocation put it at, instead of storing a second email there.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="storedEmailId">The email that was relocated, named by the mutation record.</param>
    /// <param name="occurrenceId">Where the destination folder now holds it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the row was carried across; <see langword="false" /> when another row already occupies the occurrence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrenceId" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no stored email carries <paramref name="storedEmailId" />, or when the occurrence names a folder binding that is not stored.</exception>
    /// <remarks>
    /// <para>
    /// Only the occurrence identity moves. The raw MIME, the extracted metadata, the search document, and the passages
    /// are all keyed by the local identity and describe a message that a relocation did not change, so re-deriving any
    /// of them would spend a fetch and a parse to arrive back where they already are.
    /// </para>
    /// <para>
    /// The flags become unobserved rather than being carried over as observed. What is stored still describes the
    /// message, but it was read in the folder the email has left, and the destination folder's own reconciliation window
    /// is what says whether it still holds.
    /// </para>
    /// <para>
    /// An occurrence another row already occupies is reported rather than written, because the occurrence identity is
    /// unique and a caller cannot decide for the mailbox which of two local emails is the one the server holds there.
    /// The caller then stores the discovery as it would any other, which leaves a duplicate visible instead of failing
    /// the run.
    /// </para>
    /// </remarks>
    Task<bool> TryCarryToOccurrenceAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken);
}
