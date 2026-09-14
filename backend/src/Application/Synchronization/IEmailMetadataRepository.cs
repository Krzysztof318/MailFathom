// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

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

    /// <summary>Records that one stored email is the copy MailFathom itself filed of a message it sent.</summary>
    /// <param name="session">The explicit persistence session this write participates in, which is the one storing the email.</param>
    /// <param name="storedEmailId">The email that was just stored.</param>
    /// <param name="outgoingEmailId">The outgoing record the copy was filed from.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the join is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no stored email carries <paramref name="storedEmailId" />.</exception>
    /// <remarks>
    /// <para>
    /// The copy is stored like any other message — the user searches it, reads it, and sees it in a mailbox listing —
    /// and this is the one thing that has to be different about it: everything that reacts to newly synchronized mail
    /// must not react to a message this deployment itself put there. A rule conditioned on arriving mail would
    /// otherwise fire on the user's own outgoing message the moment its sent copy came back.
    /// </para>
    /// <para>
    /// It is a join to the outgoing record rather than a flag, because the useful question is which send this copy is
    /// of. A flag would answer only the narrow question the rule engine asks, and would have to be joined back to the
    /// record anyway by whatever asked the wider one.
    /// </para>
    /// </remarks>
    Task RecordFiledFromOutgoingAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Stores a message MailFathom filed itself into a held account's local folder, with no occurrence on any server.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="account">The account the message is filed for.</param>
    /// <param name="binding">The folder binding of the source folder playing the role the message is filed under.</param>
    /// <param name="extractedMetadata">What was read out of the message's MIME, or <see langword="null" /> when nothing could be.</param>
    /// <param name="sizeOctets">How large the message is.</param>
    /// <param name="flags">The flags the message is filed with.</param>
    /// <param name="filedFrom">The outgoing record a sent copy is filed from, or <see langword="null" /> for a draft.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The new stored email's identity, or <see langword="null" /> where <paramref name="binding" /> names a folder binding that is no longer stored.</returns>
    /// <remarks>
    /// <para>
    /// A binding that is gone is answered rather than raised, because it was read before the transaction and the drain
    /// removes exactly those source folders: the caller files nothing and says so, and the state the message was a copy of
    /// still commits.
    /// </para>
    /// <para>
    /// The row is written as a synchronized one is — the search document and the conversation are placed in the same
    /// session — so a draft or a sent message is found and threaded exactly as the copy a server would have returned.
    /// </para>
    /// <para>
    /// A stored message still names a folder binding, and a filed one names the binding of the source folder playing its
    /// role: that is where the source keeps the same kind of message, and what a provider's own copy of a sent message is
    /// later drained from.
    /// </para>
    /// </remarks>
    Task<StoredEmailId?> StoreFiledEmailAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolutionId binding,
        ExtractedEmailMetadata? extractedMetadata,
        long sizeOctets,
        AppendedMailFlags flags,
        OutgoingEmailId? filedFrom,
        CancellationToken cancellationToken);

    /// <summary>Finds the sent copies MailFathom filed locally that carry one of a batch's <c>Message-ID</c> values and no occurrence yet.</summary>
    /// <param name="account">The account whose filed copies are searched.</param>
    /// <param name="internetMessageIds">The non-empty <c>Message-ID</c> values one discovered batch carries, bounded by that batch.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The filed copy standing for each identity that has one, keyed by that identity.</returns>
    /// <remarks>
    /// Only a copy filed from an outgoing record answers, and only one whose payload is stored: the identity is the one this
    /// deployment minted for the send, which is what makes it safe to compare, and a copy whose payload is absent is no copy
    /// the provider's may stand in for. It is one read per batch, as the recognition of appended copies is, so a sent
    /// folder the drain has not emptied yet costs one query per batch rather than one per message.
    /// </remarks>
    Task<IReadOnlyDictionary<string, StoredEmailId>> FindFiledSentCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<string> internetMessageIds,
        CancellationToken cancellationToken);
}
