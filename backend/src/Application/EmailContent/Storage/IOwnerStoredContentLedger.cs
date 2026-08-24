// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Answers how much stored mail content one owner holds, without reading their mail to find out.</summary>
/// <remarks>
/// <para>
/// A deployment-wide ceiling can ask the database what a table occupies, in constant time and without touching a row.
/// A per-owner ceiling has no such question available: a catalogue answers for a table and never for a share of one, and
/// summing an owner's payload lengths would put a scan of somebody's whole mailbox in front of every folder run. So the
/// figure is maintained as it changes — one counter per owner, moved inside the transaction that stores or removes the
/// payload — and read here as a single row.
/// </para>
/// <para>
/// The figure is the payload bytes rather than what a disk fills with, which is what the deployment's ceiling counts.
/// The two are deliberately different quantities, because only one of them is attributable to a person at all, and
/// nothing reconciles one against the other.
/// </para>
/// <para>
/// A maintained counter can drift where a payload leaves storage by a path nothing told it about, so the recomputation
/// it was derived from stays available rather than being a one-off in a migration. That is what makes drift repairable
/// instead of permanent, and it is the same statement the counter is asserted against.
/// </para>
/// </remarks>
public interface IOwnerStoredContentLedger
{
    /// <summary>Reads what one owner's stored mail content occupies, from the maintained counter.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes that owner's stored payloads hold.</returns>
    /// <remarks>
    /// An owner whose counter has never been written is derived once and adopted rather than answered as zero, so an
    /// upgraded deployment and one that has just erased and re-synchronized both start from what storage actually
    /// holds. Every later read is the single row.
    /// </remarks>
    Task<long> ReadStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken);

    /// <summary>Recomputes one owner's figure from their stored payloads and adopts it.</summary>
    /// <param name="owner">The owner whose counter is re-derived.</param>
    /// <param name="cancellationToken">Cancels the recomputation.</param>
    /// <returns>The figure that was adopted.</returns>
    /// <remarks>
    /// This is the expensive answer the maintained one exists to avoid, so it is a repair rather than something a run
    /// reaches for. It is issued as one statement, which is what keeps it from adopting a total that a concurrent
    /// store had already moved past.
    /// </remarks>
    Task<long> RederiveStoredContentBytesAsync(MailOwnerId owner, CancellationToken cancellationToken);
}
